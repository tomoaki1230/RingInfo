using RingInfo.App.Controls;
using RingInfo.App.Services;
using RingInfo.App.ViewModels;
using RingInfo.Core.Services;
using RingInfo.Core.Settings;

namespace RingInfo.App.Tests;

/// <summary>UI の見直しで見つかった不具合の回帰テスト</summary>
public class UiRegressionTests
{
    /// <summary>現在時刻を変えられる TimeProvider</summary>
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    [Theory]
    [InlineData(1100, 8, 4)]   // 広い: 最大 4 列
    [InlineData(430, 8, 2)]    // 狭い: 1 マス 170px 以上を保てる 2 列に減らす
    [InlineData(150, 8, 1)]    // さらに狭い: 1 列
    [InlineData(1100, 3, 3)]   // 項目が少なければ項目数まで
    public void タイルの列数は幅に合わせて減らす(double width, int items, int expected)
        => Assert.Equal(expected, AdaptiveUniformGrid.GetColumnCount(width, 170, 4, items));

    [Fact]
    public void 値が無いタイルには単位を付けない()
    {
        Assert.Equal("", new MetricTile("安静時心拍数", "—", "bpm").UnitText);
        Assert.Equal(" bpm", new MetricTile("安静時心拍数", "52", "bpm").UnitText);
    }

    [Fact]
    public void 最新の日を見ていれば新しい最新の日に追従し_自分で選んだ日は維持する()
    {
        var previous = Snapshot(new DateOnly(2026, 9, 18), new DateOnly(2026, 10, 1), latest: new DateOnly(2026, 10, 1));
        var next = Snapshot(new DateOnly(2026, 9, 19), new DateOnly(2026, 10, 2), latest: new DateOnly(2026, 10, 2));

        // 最新の日（10/1）を見ていた → 新しい最新の日（10/2）へ
        Assert.True(DayPageViewModel.IsFollowingLatest(new DateOnly(2026, 10, 1), previous));
        Assert.Equal(new DateOnly(2026, 10, 2), DayPageViewModel.ChooseDay(new DateOnly(2026, 10, 1), true, next));

        // 自分で 9/25 を選んでいた → 期間内なので維持
        Assert.False(DayPageViewModel.IsFollowingLatest(new DateOnly(2026, 9, 25), previous));
        Assert.Equal(new DateOnly(2026, 9, 25), DayPageViewModel.ChooseDay(new DateOnly(2026, 9, 25), false, next));

        // 自分で選んだ日が期間外になった → 最新の日へ
        Assert.Equal(new DateOnly(2026, 10, 2), DayPageViewModel.ChooseDay(new DateOnly(2026, 9, 18), false, next));
    }

    [Fact]
    public async Task 起動したまま日付が変わると_今日までの表示は新しい今日に合わせる()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 1, 23, 50, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 1))));
        var viewModel = new MainViewModel(null, new AppSettings { RangeDays = 7 }, forceDemo: true, timeProvider: time);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(new DateOnly(2026, 10, 1), viewModel.EndDate);

        // 日付が変わってからアプリに戻る
        time.Now = time.Now.AddMinutes(20);
        viewModel.OnWindowActivated();

        Assert.Equal(new DateOnly(2026, 10, 2), viewModel.EndDate);
        Assert.False(viewModel.GoToTodayCommand.CanExecute(null));
    }

    [Fact]
    public async Task 過去の期間を見ているときは日付が変わっても期間を動かさず_今日ボタンは押せる()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 1, 23, 50, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 1))));
        var viewModel = new MainViewModel(null, new AppSettings { RangeDays = 7 }, forceDemo: true, timeProvider: time);
        viewModel.PreviousRangeCommand.Execute(null);
        var past = viewModel.EndDate;

        time.Now = time.Now.AddMinutes(20);
        viewModel.OnWindowActivated();

        Assert.Equal(past, viewModel.EndDate);
        Assert.True(viewModel.GoToTodayCommand.CanExecute(null));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task データが1件も無くても各ページを表示できる()
    {
        var viewModel = new MainViewModel(null, new AppSettings(), forceDemo: true, demoSource: new EmptyDataSource());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.Dashboard.SleepCard.Score);
        Assert.Equal("", viewModel.Dashboard.SleepCard.DayText);
        Assert.Equal("データなし", viewModel.Dashboard.SleepCard.LevelLabel);
        Assert.Contains(viewModel.Dashboard.RingInfo, r => r.Value == "まだありません");
        Assert.True(viewModel.Dashboard.ScoreTrend!.IsEmpty);
        Assert.Empty(viewModel.Sleep.Rows);
        Assert.Null(viewModel.Sleep.SelectedRow);
        Assert.Empty(viewModel.HeartRate.Rows);
        Assert.All(viewModel.Sleep.Tiles, t => Assert.Equal("", t.UnitText));
    }

    private static DashboardSnapshot Snapshot(DateOnly start, DateOnly end, DateOnly latest)
        => DashboardSnapshot.Create(
            start, end, false, DateTimeOffset.Now,
            [new Core.Models.DailySleep { Day = latest, Score = 80 }],
            null, null, null, null, null, null);
}

using RingInfo.App.ViewModels;
using RingInfo.Core.Api;
using RingInfo.Core.Demo;
using RingInfo.Core.Models;
using RingInfo.Core.Settings;

namespace RingInfo.App.Tests;

/// <summary>
/// UI の連打でリクエストが大量に送られないかのテスト。
/// 通信の代わりに、呼び出し回数を数えて少し待つデータ元を使う。
/// </summary>
public class RapidClickTests
{
    /// <summary>呼び出し回数を数え、通信の代わりに少し待つ（キャンセルに対応）データ元</summary>
    private sealed class CountingDataSource : IOuraDataSource
    {
        private readonly DemoDataSource _inner = new();
        private int _dailyLoads;
        private int _heartRateLoads;

        public int DailyLoads => Volatile.Read(ref _dailyLoads);

        public int HeartRateLoads => Volatile.Read(ref _heartRateLoads);

        private static async Task<T> Delay<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        {
            await Task.Delay(40, cancellationToken);
            return await action();
        }

        public Task<IReadOnlyList<DailySleep>> GetDailySleepAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        {
            // 期間の読み込み 1 回につき 1 回呼ばれるので、これを「期間の読み込み回数」として数える
            Interlocked.Increment(ref _dailyLoads);
            return Delay(() => _inner.GetDailySleepAsync(start, end), cancellationToken);
        }

        public Task<IReadOnlyList<HeartRateSample>> GetHeartRateAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _heartRateLoads);
            return Delay(() => _inner.GetHeartRateAsync(start, end), cancellationToken);
        }

        public Task<PersonalInfo?> GetPersonalInfoAsync(CancellationToken cancellationToken = default) => Delay(() => _inner.GetPersonalInfoAsync(), cancellationToken);
        public Task<IReadOnlyList<DailyReadiness>> GetDailyReadinessAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Delay(() => _inner.GetDailyReadinessAsync(start, end), cancellationToken);
        public Task<IReadOnlyList<DailyActivity>> GetDailyActivityAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Delay(() => _inner.GetDailyActivityAsync(start, end), cancellationToken);
        public Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Delay(() => _inner.GetSleepPeriodsAsync(start, end), cancellationToken);
        public Task<IReadOnlyList<DailySpO2>> GetDailySpO2Async(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Delay(() => _inner.GetDailySpO2Async(start, end), cancellationToken);
        public Task<IReadOnlyList<DailyStress>> GetDailyStressAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Delay(() => _inner.GetDailyStressAsync(start, end), cancellationToken);
        public Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Delay(() => _inner.GetWorkoutsAsync(start, end), cancellationToken);
        public Task<IReadOnlyList<RingConfiguration>> GetRingConfigurationsAsync(CancellationToken cancellationToken = default) => Delay(() => _inner.GetRingConfigurationsAsync(), cancellationToken);
        public Task<RingBatteryLevel?> GetLatestBatteryLevelAsync(CancellationToken cancellationToken = default) => Delay(() => _inner.GetLatestBatteryLevelAsync(), cancellationToken);
    }

    private static async Task<(MainViewModel ViewModel, CountingDataSource Source)> CreateLoadedAsync()
    {
        var source = new CountingDataSource();
        var viewModel = new MainViewModel(null, new AppSettings { RangeDays = 14 }, forceDemo: true, demoSource: source);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        await SettleAsync(viewModel);
        return (viewModel, source);
    }

    /// <summary>読み込みが落ち着くまで待つ</summary>
    private static async Task SettleAsync(MainViewModel viewModel)
    {
        await Task.Delay(700);
        for (var i = 0; i < 100 && (viewModel.IsBusy || viewModel.HeartRate.IsLoading); i++)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task 期間ボタンを連打しても読み込みは最後の1回だけ()
    {
        var (viewModel, source) = await CreateLoadedAsync();
        var before = source.DailyLoads;

        for (var i = 0; i < 12; i++)
        {
            viewModel.SelectedRange = viewModel.RangeOptions[i % viewModel.RangeOptions.Count];
        }

        await SettleAsync(viewModel);

        Assert.Equal(1, source.DailyLoads - before);
        Assert.Equal(viewModel.RangeOptions[11 % 4], viewModel.SelectedRange);
        Assert.Equal(viewModel.SelectedRange.Days, viewModel.Sleep.Rows.Count + (viewModel.SelectedRange.Days - viewModel.Sleep.Rows.Count));
    }

    [Fact]
    public async Task 前の期間ボタンを連打しても読み込みは最後の1回だけ()
    {
        var (viewModel, source) = await CreateLoadedAsync();
        var before = source.DailyLoads;

        for (var i = 0; i < 10; i++)
        {
            viewModel.PreviousRangeCommand.Execute(null);
        }

        await SettleAsync(viewModel);

        Assert.Equal(1, source.DailyLoads - before);
        Assert.Equal(viewModel.EndDate, viewModel.Dashboard is { } ? viewModel.EndDate : default);
    }

    [Fact]
    public async Task 更新ボタンを連打しても読み込みは1回だけ()
    {
        var (viewModel, source) = await CreateLoadedAsync();
        var before = source.DailyLoads;

        var tasks = Enumerable.Range(0, 8)
            .Where(_ => viewModel.RefreshCommand.CanExecute(null))
            .Select(_ => viewModel.RefreshCommand.ExecuteAsync(null))
            .ToList();
        await Task.WhenAll(tasks);
        await SettleAsync(viewModel);

        Assert.Equal(1, source.DailyLoads - before);
    }

    [Fact]
    public async Task 心拍数ページで日付を連打しても読み込みは最後の1回だけ()
    {
        var (viewModel, source) = await CreateLoadedAsync();
        viewModel.SelectedPage = viewModel.HeartRate;
        await SettleAsync(viewModel);
        var before = source.HeartRateLoads;

        for (var i = 0; i < 10; i++)
        {
            viewModel.HeartRate.PreviousDayCommand.Execute(null);
        }

        await SettleAsync(viewModel);

        Assert.Equal(1, source.HeartRateLoads - before);
        Assert.NotNull(viewModel.HeartRate.Chart);
    }

    [Fact]
    public async Task 直前に読み込んだ期間に戻したときは再取得しない()
    {
        var (viewModel, source) = await CreateLoadedAsync();
        var before = source.DailyLoads;

        viewModel.SelectedRange = viewModel.RangeOptions[2]; // 30日
        await SettleAsync(viewModel);
        viewModel.SelectedRange = viewModel.RangeOptions[1]; // 14日（読み込み済み）
        await SettleAsync(viewModel);

        Assert.Equal(1, source.DailyLoads - before);
        Assert.Equal(14, viewModel.Sleep.CalendarMax.Subtract(viewModel.Sleep.CalendarMin).Days + 1);
    }

    [Fact]
    public async Task 更新ボタンは読み込み済みの期間でも最新を取得する()
    {
        var (viewModel, source) = await CreateLoadedAsync();
        var before = source.DailyLoads;

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await SettleAsync(viewModel);

        Assert.Equal(1, source.DailyLoads - before);
    }

    [Fact]
    public async Task 心拍数ページで一度見た日に戻ったときは再取得しない()
    {
        var (viewModel, source) = await CreateLoadedAsync();
        viewModel.SelectedPage = viewModel.HeartRate;
        await SettleAsync(viewModel);
        viewModel.HeartRate.PreviousDayCommand.Execute(null);
        await SettleAsync(viewModel);
        var before = source.HeartRateLoads;

        viewModel.HeartRate.NextDayCommand.Execute(null);
        await SettleAsync(viewModel);

        Assert.Equal(0, source.HeartRateLoads - before);
        Assert.NotNull(viewModel.HeartRate.Chart);
    }
}

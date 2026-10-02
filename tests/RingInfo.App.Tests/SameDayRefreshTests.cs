using RingInfo.App.ViewModels;
using RingInfo.Core.Settings;

namespace RingInfo.App.Tests;

/// <summary>
/// 同じ日のうちに時間をおいて再表示したとき（例: 12 時 → 16 時）、その間のデータが取得されるかのテスト。
/// デモデータは「現在時刻まで」のデータを返すため、時刻を進めると新しいデータが増える。
/// </summary>
public class SameDayRefreshTests
{
    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    private static DateTimeOffset LocalTime(int hour)
    {
        var local = new DateTime(2026, 10, 2, hour, 0, 0);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static async Task SettleAsync(MainViewModel viewModel)
    {
        await Task.Delay(700);
        for (var i = 0; i < 100 && (viewModel.IsBusy || viewModel.HeartRate.IsLoading); i++)
        {
            await Task.Delay(50);
        }
    }

    private static async Task<(MainViewModel ViewModel, ManualTimeProvider Time)> LoadAtNoonAsync()
    {
        var time = new ManualTimeProvider(LocalTime(12));
        var viewModel = new MainViewModel(null, new AppSettings { RangeDays = 7 }, forceDemo: true, timeProvider: time);
        await viewModel.InitializeAsync();
        await SettleAsync(viewModel);
        return (viewModel, time);
    }

    [Fact]
    public async Task 時間をおいてアプリに戻ると今日のデータを取り直す()
    {
        var (viewModel, time) = await LoadAtNoonAsync();
        var stepsAtNoon = viewModel.Activity.Rows[0].Steps;

        time.Now = LocalTime(16);
        viewModel.OnWindowActivated();
        await SettleAsync(viewModel);

        Assert.NotEqual(stepsAtNoon, viewModel.Activity.Rows[0].Steps);
        Assert.Contains("16:00", viewModel.StatusText);
    }

    [Fact]
    public async Task 心拍数ページを開き直すと今日の心拍数を16時まで取り直す()
    {
        var (viewModel, time) = await LoadAtNoonAsync();
        viewModel.SelectedPage = viewModel.HeartRate;
        await SettleAsync(viewModel);
        var lastAtNoon = viewModel.HeartRate.Samples!.Max(s => s.Timestamp);
        Assert.True(lastAtNoon <= LocalTime(12));

        viewModel.SelectedPage = viewModel.Dashboard;
        time.Now = LocalTime(16);
        viewModel.SelectedPage = viewModel.HeartRate;
        await SettleAsync(viewModel);

        var lastAtFour = viewModel.HeartRate.Samples!.Max(s => s.Timestamp);
        Assert.True(lastAtFour > LocalTime(15).AddMinutes(50), $"最後の計測: {lastAtFour}");
    }

    [Fact]
    public async Task 更新ボタンを押せば16時までのデータを取得する()
    {
        var (viewModel, time) = await LoadAtNoonAsync();
        var stepsAtNoon = viewModel.Activity.Rows[0].Steps;

        time.Now = LocalTime(16);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        await SettleAsync(viewModel);

        Assert.NotEqual(stepsAtNoon, viewModel.Activity.Rows[0].Steps);
    }

    [Fact]
    public async Task 直後にアプリに戻っただけでは取り直さない()
    {
        var (viewModel, time) = await LoadAtNoonAsync();
        var status = viewModel.StatusText;

        time.Now = LocalTime(12).AddMinutes(1);
        viewModel.OnWindowActivated();
        await SettleAsync(viewModel);

        Assert.Equal(status, viewModel.StatusText);
    }
}

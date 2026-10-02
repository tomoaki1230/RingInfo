using RingInfo.Core.Demo;
using RingInfo.Core.Services;
using RingInfo.Core.Tests.Helpers;

namespace RingInfo.Core.Tests.Demo;

public class DemoDataSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 21, 0, 0, TimeSpan.FromHours(9));
    private static readonly DateOnly Start = new(2026, 7, 4);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static DemoDataSource Create(DateTimeOffset? now = null) => new(new FakeTimeProvider(now ?? Now), TestData.Tokyo);

    [Fact]
    public async Task 同じ日は常に同じ値になる()
    {
        var first = await Create().GetDailySleepAsync(Start, Today);
        var second = await Create().GetDailySleepAsync(Start, Today);

        Assert.Equal(first.Select(x => x.Score), second.Select(x => x.Score));
    }

    [Fact]
    public async Task スコアは1から100の範囲で未来の日は含まない()
    {
        var source = Create();
        var sleep = await source.GetDailySleepAsync(Start, Today.AddDays(5));
        var readiness = await source.GetDailyReadinessAsync(Start, Today.AddDays(5));
        var activity = await source.GetDailyActivityAsync(Start, Today.AddDays(5));

        Assert.Equal(90, sleep.Count);
        Assert.Equal(Today, sleep[^1].Day);
        Assert.All(sleep, x => Assert.InRange(x.Score!.Value, 1, 100));
        Assert.All(readiness, x => Assert.InRange(x.Score!.Value, 1, 100));
        Assert.All(activity, x => Assert.InRange(x.Score!.Value, 1, 100));
        Assert.All(activity, x => Assert.True(x.Day <= Today));
    }

    [Fact]
    public async Task 睡眠ステージの合計と各時間が一致する()
    {
        var periods = await Create().GetSleepPeriodsAsync(Start, Today);

        Assert.All(periods, p =>
        {
            Assert.Equal(p.TimeInBed, p.SleepPhase5Min!.Length * 300);
            Assert.Equal(p.TotalSleepDuration, p.DeepSleepDuration + p.LightSleepDuration + p.RemSleepDuration);
            Assert.Equal(p.TimeInBed, p.TotalSleepDuration + p.AwakeTime);
            Assert.Equal(p.SleepPhase5Min.Length, p.HeartRate!.Items.Count);
            Assert.InRange(p.TotalSleepDuration!.Value / 3600.0, 4.5, 9.5);
            Assert.True(p.BedtimeEnd > p.BedtimeStart);
            Assert.Equal(p.Day, DateOnly.FromDateTime(p.BedtimeEnd.DateTime));
        });
    }

    [Fact]
    public async Task 未完了の夜の睡眠は含まない()
    {
        // 午前 3 時の時点では当日の睡眠はまだ終わっていない
        var source = Create(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.FromHours(9)));

        var sleep = await source.GetDailySleepAsync(Today.AddDays(-1), Today);

        Assert.Equal([Today.AddDays(-1)], sleep.Select(x => x.Day));
    }

    [Fact]
    public async Task 心拍数は指定範囲かつ現在時刻までで_睡眠中は睡眠として記録される()
    {
        var source = Create();
        var (start, end) = DashboardService.GetDayRange(Today, TestData.Tokyo);

        var samples = await source.GetHeartRateAsync(start, end);
        var period = (await source.GetSleepPeriodsAsync(Today, Today)).Single();

        Assert.NotEmpty(samples);
        Assert.All(samples, s => Assert.InRange(s.Timestamp, start, Now));
        Assert.All(samples, s => Assert.InRange(s.Bpm, 35, 200));
        Assert.All(samples.Where(s => s.Timestamp < period.BedtimeEnd), s => Assert.Equal("sleep", s.Source));
        Assert.Contains(samples, s => s.Source == "awake");
    }
}

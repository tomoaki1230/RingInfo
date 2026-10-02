using RingInfo.Core.Api;
using RingInfo.Core.Demo;
using RingInfo.Core.Models;
using RingInfo.Core.Services;
using RingInfo.Core.Tests.Helpers;

namespace RingInfo.Core.Tests.Services;

public class DashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 21, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public async Task 期間内の全日をまとめる_データの無い日も含む()
    {
        var source = new DemoDataSource(new FakeTimeProvider(Now), TestData.Tokyo);
        var service = new DashboardService(source, isDemo: true, new FakeTimeProvider(Now));

        // 終了日を未来にしても、未来の日はデータ無しの日として含まれる
        var snapshot = await service.LoadAsync(new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 3));

        Assert.Equal(9, snapshot.Days.Count);
        Assert.Equal(new DateOnly(2026, 9, 25), snapshot.Days[0].Day);
        Assert.Equal(new DateOnly(2026, 10, 1), snapshot.Latest?.Day);
        Assert.False(snapshot.GetDay(new DateOnly(2026, 10, 3))!.HasAnyScore);
        Assert.True(snapshot.IsDemo);
        Assert.Empty(snapshot.Warnings);
        Assert.NotNull(snapshot.Ring);
        Assert.NotNull(snapshot.Battery);
        Assert.All(snapshot.Days.Where(d => d.Day <= new DateOnly(2026, 10, 1)), d => Assert.NotNull(d.MainSleep));
    }

    [Fact]
    public async Task 一部の取得に失敗しても警告として残し他のデータは返す()
    {
        var source = new FailingSource(new DemoDataSource(new FakeTimeProvider(Now), TestData.Tokyo), failSpO2: true);
        var service = new DashboardService(source, isDemo: false);

        var snapshot = await service.LoadAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 1));

        var warning = Assert.Single(snapshot.Warnings);
        Assert.StartsWith("血中酸素", warning);
        Assert.All(snapshot.Days, d => Assert.Null(d.SpO2));
        Assert.NotNull(snapshot.Latest?.SleepScore);
    }

    [Fact]
    public async Task 電池残量などの補助情報の失敗は警告にしない()
    {
        var source = new FailingSource(new DemoDataSource(new FakeTimeProvider(Now), TestData.Tokyo), failOptional: true);
        var service = new DashboardService(source, isDemo: false);

        var snapshot = await service.LoadAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 1));

        Assert.Empty(snapshot.Warnings);
        Assert.Null(snapshot.Battery);
        Assert.Null(snapshot.Ring);
        Assert.NotNull(snapshot.Latest?.SleepScore);
    }

    [Fact]
    public async Task 全て失敗した場合は例外になる()
    {
        var source = new FailingSource(new DemoDataSource(new FakeTimeProvider(Now), TestData.Tokyo), failAll: true);
        var service = new DashboardService(source, isDemo: false);

        await Assert.ThrowsAsync<OuraApiException>(() => service.LoadAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public async Task 認証エラーは警告にせず例外として通知する()
    {
        var source = new FailingSource(new DemoDataSource(new FakeTimeProvider(Now), TestData.Tokyo), authError: true);
        var service = new DashboardService(source, isDemo: false);

        await Assert.ThrowsAsync<OuraAuthenticationRequiredException>(() => service.LoadAsync(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void 日付範囲はローカル時刻の0時から翌0時になる()
    {
        var (start, end) = DashboardService.GetDayRange(new DateOnly(2026, 10, 1), TestData.Tokyo);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(9)), start);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(9)), end);
    }

    [Fact]
    public void 主な睡眠は_long_sleep_を優先し最も長いものを選ぶ()
    {
        var periods = new[]
        {
            new SleepPeriod { Id = "nap", Type = "late_nap", TotalSleepDuration = 30000 },
            new SleepPeriod { Id = "short", Type = SleepPeriod.TypeLongSleep, TotalSleepDuration = 10000 },
            new SleepPeriod { Id = "long", Type = SleepPeriod.TypeLongSleep, TotalSleepDuration = 20000 },
            new SleepPeriod { Id = "deleted", Type = "deleted", TotalSleepDuration = 50000 },
        };

        Assert.Equal("long", DashboardSnapshot.SelectMainSleep(periods)?.Id);
    }

    [Fact]
    public void 期間平均は値のある日だけで計算する()
    {
        var snapshot = DashboardSnapshot.Create(
            new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 1), false, Now,
            [new DailySleep { Day = new DateOnly(2026, 9, 29), Score = 70 }, new DailySleep { Day = new DateOnly(2026, 10, 1), Score = 90 }],
            null, null, null, null, null, null);

        Assert.Equal(80, snapshot.Average(d => d.SleepScore));
        Assert.Null(snapshot.Average(d => d.ActivityScore));
        Assert.Equal(new DateOnly(2026, 10, 1), snapshot.Latest?.Day);
    }

    /// <summary>指定したデータの取得を失敗させるデータソース</summary>
    private sealed class FailingSource(IOuraDataSource inner, bool failSpO2 = false, bool failAll = false, bool authError = false, bool failOptional = false) : IOuraDataSource
    {
        private Task<T> Run<T>(Func<Task<T>> action, bool fail = false)
        {
            if (authError)
            {
                throw new OuraAuthenticationRequiredException("再連携が必要");
            }

            if (failAll || fail)
            {
                // 補助情報はスコープ不足（401）、それ以外は通常のエラーで失敗させる
                throw fail && failOptional
                    ? new OuraApiException("スコープ不足", System.Net.HttpStatusCode.Unauthorized) { IsScopeError = true }
                    : new OuraApiException("取得失敗");
            }

            return action();
        }

        public Task<PersonalInfo?> GetPersonalInfoAsync(CancellationToken cancellationToken = default) => Run(() => inner.GetPersonalInfoAsync(cancellationToken), failOptional);
        public Task<IReadOnlyList<DailySleep>> GetDailySleepAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Run(() => inner.GetDailySleepAsync(start, end, cancellationToken));
        public Task<IReadOnlyList<DailyReadiness>> GetDailyReadinessAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Run(() => inner.GetDailyReadinessAsync(start, end, cancellationToken));
        public Task<IReadOnlyList<DailyActivity>> GetDailyActivityAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Run(() => inner.GetDailyActivityAsync(start, end, cancellationToken));
        public Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Run(() => inner.GetSleepPeriodsAsync(start, end, cancellationToken));
        public Task<IReadOnlyList<DailySpO2>> GetDailySpO2Async(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Run(() => inner.GetDailySpO2Async(start, end, cancellationToken), failSpO2);
        public Task<IReadOnlyList<DailyStress>> GetDailyStressAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Run(() => inner.GetDailyStressAsync(start, end, cancellationToken));
        public Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Run(() => inner.GetWorkoutsAsync(start, end, cancellationToken));
        public Task<IReadOnlyList<HeartRateSample>> GetHeartRateAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) => Run(() => inner.GetHeartRateAsync(start, end, cancellationToken));
        public Task<IReadOnlyList<RingConfiguration>> GetRingConfigurationsAsync(CancellationToken cancellationToken = default) => Run(() => inner.GetRingConfigurationsAsync(cancellationToken), failOptional);
        public Task<RingBatteryLevel?> GetLatestBatteryLevelAsync(CancellationToken cancellationToken = default) => Run(() => inner.GetLatestBatteryLevelAsync(cancellationToken), failOptional);
    }
}

public class DashboardServiceCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 21, 0, 0, TimeSpan.FromHours(9));

    /// <summary>呼び出し回数を数えるデータ元</summary>
    private sealed class CountingSource(IOuraDataSource inner) : IOuraDataSource
    {
        public int SleepCalls;
        public int HeartRateCalls;

        public Task<IReadOnlyList<DailySleep>> GetDailySleepAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref SleepCalls);
            return inner.GetDailySleepAsync(start, end, cancellationToken);
        }

        public Task<IReadOnlyList<HeartRateSample>> GetHeartRateAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref HeartRateCalls);
            return inner.GetHeartRateAsync(start, end, cancellationToken);
        }

        public Task<PersonalInfo?> GetPersonalInfoAsync(CancellationToken cancellationToken = default) => inner.GetPersonalInfoAsync(cancellationToken);
        public Task<IReadOnlyList<DailyReadiness>> GetDailyReadinessAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => inner.GetDailyReadinessAsync(start, end, cancellationToken);
        public Task<IReadOnlyList<DailyActivity>> GetDailyActivityAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => inner.GetDailyActivityAsync(start, end, cancellationToken);
        public Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => inner.GetSleepPeriodsAsync(start, end, cancellationToken);
        public Task<IReadOnlyList<DailySpO2>> GetDailySpO2Async(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => inner.GetDailySpO2Async(start, end, cancellationToken);
        public Task<IReadOnlyList<DailyStress>> GetDailyStressAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => inner.GetDailyStressAsync(start, end, cancellationToken);
        public Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => inner.GetWorkoutsAsync(start, end, cancellationToken);
        public Task<IReadOnlyList<RingConfiguration>> GetRingConfigurationsAsync(CancellationToken cancellationToken = default) => inner.GetRingConfigurationsAsync(cancellationToken);
        public Task<RingBatteryLevel?> GetLatestBatteryLevelAsync(CancellationToken cancellationToken = default) => inner.GetLatestBatteryLevelAsync(cancellationToken);
    }

    private static (DashboardService Service, CountingSource Source, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider(Now);
        var source = new CountingSource(new DemoDataSource(time, TestData.Tokyo));
        return (new DashboardService(source, isDemo: true, time), source, time);
    }

    [Fact]
    public async Task 同じ期間は一定時間使い回し_期限切れや強制更新では取り直す()
    {
        var (service, source, time) = Create();
        var start = new DateOnly(2026, 9, 18);
        var end = new DateOnly(2026, 10, 1);

        var first = await service.LoadAsync(start, end);
        var second = await service.LoadAsync(start, end);
        Assert.Same(first, second);
        Assert.Equal(1, source.SleepCalls);

        await service.LoadAsync(start, end, forceReload: true);
        Assert.Equal(2, source.SleepCalls);

        time.Now = time.Now.Add(DashboardService.SnapshotCacheDuration);
        await service.LoadAsync(start, end);
        Assert.Equal(3, source.SleepCalls);
    }

    [Fact]
    public async Task 心拍数は過去の日を今日より長く使い回す()
    {
        var (service, source, time) = Create();
        var today = new DateOnly(2026, 10, 1);
        var yesterday = today.AddDays(-1);

        await service.LoadHeartRateAsync(today, TestData.Tokyo);
        await service.LoadHeartRateAsync(yesterday, TestData.Tokyo);
        await service.LoadHeartRateAsync(today, TestData.Tokyo);
        await service.LoadHeartRateAsync(yesterday, TestData.Tokyo);
        Assert.Equal(2, source.HeartRateCalls);

        // 10 分後: 今日は取り直し、過去の日は使い回す
        time.Now = time.Now.AddMinutes(10);
        await service.LoadHeartRateAsync(today, TestData.Tokyo);
        await service.LoadHeartRateAsync(yesterday, TestData.Tokyo);
        Assert.Equal(3, source.HeartRateCalls);

        await service.LoadHeartRateAsync(yesterday, TestData.Tokyo, forceReload: true);
        Assert.Equal(4, source.HeartRateCalls);
    }
}

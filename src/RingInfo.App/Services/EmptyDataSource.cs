using RingInfo.Core.Api;
using RingInfo.Core.Models;

namespace RingInfo.App.Services;

/// <summary>データが 1 件も無い状態（連携直後でまだ同期されていない等）を再現するデータ元。画面確認・テスト用。</summary>
internal sealed class EmptyDataSource : IOuraDataSource
{
    public Task<PersonalInfo?> GetPersonalInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult<PersonalInfo?>(null);

    public Task<IReadOnlyList<DailySleep>> GetDailySleepAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Empty<DailySleep>();

    public Task<IReadOnlyList<DailyReadiness>> GetDailyReadinessAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Empty<DailyReadiness>();

    public Task<IReadOnlyList<DailyActivity>> GetDailyActivityAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Empty<DailyActivity>();

    public Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Empty<SleepPeriod>();

    public Task<IReadOnlyList<DailySpO2>> GetDailySpO2Async(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Empty<DailySpO2>();

    public Task<IReadOnlyList<DailyStress>> GetDailyStressAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Empty<DailyStress>();

    public Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => Empty<Workout>();

    public Task<IReadOnlyList<HeartRateSample>> GetHeartRateAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) => Empty<HeartRateSample>();

    public Task<IReadOnlyList<RingConfiguration>> GetRingConfigurationsAsync(CancellationToken cancellationToken = default) => Empty<RingConfiguration>();

    public Task<RingBatteryLevel?> GetLatestBatteryLevelAsync(CancellationToken cancellationToken = default) => Task.FromResult<RingBatteryLevel?>(null);

    private static Task<IReadOnlyList<T>> Empty<T>() => Task.FromResult<IReadOnlyList<T>>([]);
}

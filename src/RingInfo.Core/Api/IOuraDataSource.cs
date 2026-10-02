using RingInfo.Core.Models;

namespace RingInfo.Core.Api;

/// <summary>
/// Oura のデータ取得元。実 API（<see cref="OuraApiClient"/>）とデモデータ（Demo.DemoDataSource）で共通のインターフェース。
/// 日付範囲は start / end とも含む（閉区間）。
/// </summary>
public interface IOuraDataSource
{
    Task<PersonalInfo?> GetPersonalInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailySleep>> GetDailySleepAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyReadiness>> GetDailyReadinessAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyActivity>> GetDailyActivityAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailySpO2>> GetDailySpO2Async(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyStress>> GetDailyStressAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HeartRateSample>> GetHeartRateAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RingConfiguration>> GetRingConfigurationsAsync(CancellationToken cancellationToken = default);

    Task<RingBatteryLevel?> GetLatestBatteryLevelAsync(CancellationToken cancellationToken = default);
}

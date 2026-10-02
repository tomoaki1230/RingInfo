using System.Collections.Concurrent;
using RingInfo.Core.Api;
using RingInfo.Core.Models;

namespace RingInfo.Core.Services;

/// <summary>
/// 表示期間のデータをまとめて取得する。
/// 一部のエンドポイントが失敗しても他のデータは表示できるよう、失敗は警告として集約する。
/// ただし認証エラーと全件失敗は例外として通知する。
/// </summary>
public sealed class DashboardService(IOuraDataSource dataSource, bool isDemo, TimeProvider? timeProvider = null)
{
    /// <summary>期間のデータを再取得せずに使い回す時間（リクエスト上限への配慮）</summary>
    public static readonly TimeSpan SnapshotCacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>今日を含む日の心拍数を使い回す時間</summary>
    public static readonly TimeSpan TodayHeartRateCacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>過去の日の心拍数を使い回す時間</summary>
    public static readonly TimeSpan PastHeartRateCacheDuration = TimeSpan.FromMinutes(30);

    private const int MaxCacheEntries = 16;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _cacheSync = new();
    private readonly Dictionary<(DateOnly Start, DateOnly End), (DashboardSnapshot Snapshot, DateTimeOffset LoadedAt)> _snapshotCache = new();
    private readonly Dictionary<DateOnly, (IReadOnlyList<HeartRateSample> Samples, DateTimeOffset LoadedAt)> _heartRateCache = new();

    public bool IsDemo { get; } = isDemo;

    /// <summary>
    /// 期間のデータを取得する。直近 <see cref="SnapshotCacheDuration"/> 以内に同じ期間を取得していれば使い回す。
    /// </summary>
    /// <param name="forceReload">一時保存を使わず必ず取得する（「更新」ボタン）</param>
    public async Task<DashboardSnapshot> LoadAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default, bool forceReload = false)
    {
        var now = _timeProvider.GetUtcNow();
        if (!forceReload)
        {
            lock (_cacheSync)
            {
                if (_snapshotCache.TryGetValue((start, end), out var cached) && now - cached.LoadedAt < SnapshotCacheDuration)
                {
                    return cached.Snapshot;
                }
            }
        }

        var snapshot = await LoadFromSourceAsync(start, end, cancellationToken).ConfigureAwait(false);

        // 一部のデータが取得できなかった場合は、次回取り直せるよう保存しない
        if (snapshot.Warnings.Count == 0)
        {
            lock (_cacheSync)
            {
                if (_snapshotCache.Count >= MaxCacheEntries)
                {
                    _snapshotCache.Clear();
                }

                _snapshotCache[(start, end)] = (snapshot, now);
            }
        }

        return snapshot;
    }

    private async Task<DashboardSnapshot> LoadFromSourceAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var warnings = new ConcurrentQueue<string>();
        var failures = 0;
        var attempts = 0;

        // optional: 補助的な情報（個人情報・リング情報・電池残量）。
        // 許可されていないスコープや未対応のリングでも毎回警告が出ないよう、失敗しても表示しない。
        async Task<T?> TryGet<T>(string label, Func<Task<T>> action, bool optional = false)
        {
            Interlocked.Increment(ref attempts);
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken))
            {
                Interlocked.Increment(ref failures);
                if (!optional)
                {
                    warnings.Enqueue($"{label}: {ex.Message}");
                }

                return default;
            }
        }

        var personalTask = TryGet("個人情報", () => dataSource.GetPersonalInfoAsync(cancellationToken), optional: true);
        var ringTask = TryGet("リング情報", () => dataSource.GetRingConfigurationsAsync(cancellationToken), optional: true);
        var batteryTask = TryGet("電池残量", () => dataSource.GetLatestBatteryLevelAsync(cancellationToken), optional: true);
        var sleepTask = TryGet("睡眠スコア", () => dataSource.GetDailySleepAsync(start, end, cancellationToken));
        var readinessTask = TryGet("コンディション", () => dataSource.GetDailyReadinessAsync(start, end, cancellationToken));
        var activityTask = TryGet("アクティビティ", () => dataSource.GetDailyActivityAsync(start, end, cancellationToken));
        var periodsTask = TryGet("睡眠詳細", () => dataSource.GetSleepPeriodsAsync(start, end, cancellationToken));
        var spo2Task = TryGet("血中酸素", () => dataSource.GetDailySpO2Async(start, end, cancellationToken));
        var stressTask = TryGet("ストレス", () => dataSource.GetDailyStressAsync(start, end, cancellationToken));
        var workoutTask = TryGet("ワークアウト", () => dataSource.GetWorkoutsAsync(start, end, cancellationToken));

        await Task.WhenAll(personalTask, ringTask, batteryTask, sleepTask, readinessTask, activityTask, periodsTask, spo2Task, stressTask, workoutTask)
            .ConfigureAwait(false);

        if (failures == attempts)
        {
            // 全て失敗した場合は通信障害などとして扱う
            throw new OuraApiException(warnings.TryPeek(out var first) ? first : "データを取得できませんでした。");
        }

        var rings = await ringTask.ConfigureAwait(false);
        return DashboardSnapshot.Create(
            start,
            end,
            IsDemo,
            _timeProvider.GetUtcNow(),
            await sleepTask.ConfigureAwait(false),
            await readinessTask.ConfigureAwait(false),
            await activityTask.ConfigureAwait(false),
            await periodsTask.ConfigureAwait(false),
            await spo2Task.ConfigureAwait(false),
            await stressTask.ConfigureAwait(false),
            await workoutTask.ConfigureAwait(false),
            await personalTask.ConfigureAwait(false),
            rings?.OrderByDescending(r => r.SetUpAt ?? DateTimeOffset.MinValue).FirstOrDefault(),
            await batteryTask.ConfigureAwait(false),
            warnings.ToList());
    }

    /// <summary>
    /// 指定日（ローカル時刻の 0 時〜24 時）の心拍数を取得する。
    /// 一度取得した日は、今日なら 5 分、過去の日なら 30 分のあいだ使い回す。
    /// </summary>
    /// <param name="forceReload">一時保存を使わず必ず取得する（再読み込みボタン）</param>
    public async Task<IReadOnlyList<HeartRateSample>> LoadHeartRateAsync(DateOnly day, TimeZoneInfo timeZone, CancellationToken cancellationToken = default, bool forceReload = false)
    {
        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var duration = day >= today ? TodayHeartRateCacheDuration : PastHeartRateCacheDuration;
        if (!forceReload)
        {
            lock (_cacheSync)
            {
                if (_heartRateCache.TryGetValue(day, out var cached) && now - cached.LoadedAt < duration)
                {
                    return cached.Samples;
                }
            }
        }

        var (start, end) = GetDayRange(day, timeZone);
        var samples = await dataSource.GetHeartRateAsync(start, end, cancellationToken).ConfigureAwait(false);
        lock (_cacheSync)
        {
            if (_heartRateCache.Count >= MaxCacheEntries * 4)
            {
                _heartRateCache.Clear();
            }

            _heartRateCache[day] = (samples, now);
        }

        return samples;
    }

    /// <summary>ローカル日付の 0 時〜翌日 0 時を、その時点のオフセット付きで返す</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) GetDayRange(DateOnly day, TimeZoneInfo timeZone)
    {
        static DateTimeOffset AtMidnight(DateOnly d, TimeZoneInfo tz)
        {
            var local = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, tz.GetUtcOffset(local));
        }

        return (AtMidnight(day, timeZone), AtMidnight(day.AddDays(1), timeZone));
    }

    private static bool IsRecoverable(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        OuraAuthenticationRequiredException => false,
        OuraApiException { IsAuthenticationError: true } => false,
        _ => true,
    };
}

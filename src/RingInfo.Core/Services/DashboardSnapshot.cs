using RingInfo.Core.Models;

namespace RingInfo.Core.Services;

/// <summary>表示期間分の全データ（画面表示の元データ）</summary>
public sealed class DashboardSnapshot
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required bool IsDemo { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }

    public PersonalInfo? PersonalInfo { get; init; }
    public RingConfiguration? Ring { get; init; }
    public RingBatteryLevel? Battery { get; init; }

    /// <summary>期間内の全日（古い順、データの無い日も含む）</summary>
    public required IReadOnlyList<DayMetrics> Days { get; init; }

    /// <summary>一部のデータ取得に失敗した場合のメッセージ</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>スコアがある最新の日（無ければ null）</summary>
    public DayMetrics? Latest => Days.LastOrDefault(d => d.HasAnyScore);

    /// <summary>指定日のデータ（範囲外なら null）</summary>
    public DayMetrics? GetDay(DateOnly day) => Days.FirstOrDefault(d => d.Day == day);

    /// <summary>期間平均（値のある日のみ）。値が無ければ null</summary>
    public double? Average(Func<DayMetrics, double?> selector)
    {
        var values = Days.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    /// <summary>取得結果を日ごとにまとめてスナップショットを作る</summary>
    public static DashboardSnapshot Create(
        DateOnly start,
        DateOnly end,
        bool isDemo,
        DateTimeOffset loadedAt,
        IEnumerable<DailySleep>? dailySleep,
        IEnumerable<DailyReadiness>? readiness,
        IEnumerable<DailyActivity>? activity,
        IEnumerable<SleepPeriod>? sleepPeriods,
        IEnumerable<DailySpO2>? spo2,
        IEnumerable<DailyStress>? stress,
        IEnumerable<Workout>? workouts,
        PersonalInfo? personalInfo = null,
        RingConfiguration? ring = null,
        RingBatteryLevel? battery = null,
        IReadOnlyList<string>? warnings = null)
    {
        // 同じ日に複数ある場合は最後のもの（API の並び順で新しいもの）を採用する
        static Dictionary<DateOnly, T> ByDay<T>(IEnumerable<T>? items) where T : IDailyDocument
            => (items ?? []).GroupBy(x => x.Day).ToDictionary(g => g.Key, g => g.Last());

        var sleepByDay = ByDay(dailySleep);
        var readinessByDay = ByDay(readiness);
        var activityByDay = ByDay(activity);
        var spo2ByDay = ByDay(spo2);
        var stressByDay = ByDay(stress);
        var periodsByDay = (sleepPeriods ?? []).GroupBy(x => x.Day).ToDictionary(g => g.Key, g => SelectMainSleep(g));
        var workoutsByDay = (workouts ?? []).GroupBy(x => x.Day).ToDictionary(g => g.Key, g => (IReadOnlyList<Workout>)g.OrderBy(w => w.StartDatetime).ToList());

        var days = new List<DayMetrics>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            days.Add(new DayMetrics
            {
                Day = day,
                Sleep = sleepByDay.GetValueOrDefault(day),
                Readiness = readinessByDay.GetValueOrDefault(day),
                Activity = activityByDay.GetValueOrDefault(day),
                SpO2 = spo2ByDay.GetValueOrDefault(day),
                Stress = stressByDay.GetValueOrDefault(day),
                MainSleep = periodsByDay.GetValueOrDefault(day),
                Workouts = workoutsByDay.GetValueOrDefault(day) ?? [],
            });
        }

        return new DashboardSnapshot
        {
            StartDate = start,
            EndDate = end,
            IsDemo = isDemo,
            LoadedAt = loadedAt,
            PersonalInfo = personalInfo,
            Ring = ring,
            Battery = battery,
            Days = days,
            Warnings = warnings ?? [],
        };
    }

    /// <summary>その日の主な睡眠を選ぶ（long_sleep を優先し、最も長いもの）</summary>
    internal static SleepPeriod? SelectMainSleep(IEnumerable<SleepPeriod> periods)
    {
        var candidates = periods.Where(p => p.Type != "deleted").ToList();
        return candidates
            .OrderByDescending(p => p.Type == SleepPeriod.TypeLongSleep)
            .ThenByDescending(p => p.TotalSleepDuration ?? 0)
            .ThenByDescending(p => p.TimeInBed)
            .FirstOrDefault();
    }
}

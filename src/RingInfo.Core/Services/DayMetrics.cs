using RingInfo.Core.Models;

namespace RingInfo.Core.Services;

/// <summary>1 日分のデータをまとめたもの</summary>
public sealed class DayMetrics
{
    public required DateOnly Day { get; init; }
    public DailySleep? Sleep { get; init; }
    public DailyReadiness? Readiness { get; init; }
    public DailyActivity? Activity { get; init; }
    public DailySpO2? SpO2 { get; init; }
    public DailyStress? Stress { get; init; }

    /// <summary>その日の主な睡眠（夜間の睡眠）</summary>
    public SleepPeriod? MainSleep { get; init; }

    public IReadOnlyList<Workout> Workouts { get; init; } = [];

    public int? SleepScore => Sleep?.Score;
    public int? ReadinessScore => Readiness?.Score;
    public int? ActivityScore => Activity?.Score;
    public int? Steps => Activity?.Steps;
    public int? ActiveCalories => Activity?.ActiveCalories;
    public int? TotalCalories => Activity?.TotalCalories;
    public double? TotalSleepHours => MainSleep?.TotalSleepDuration / 3600.0;
    public int? LowestHeartRate => MainSleep?.LowestHeartRate;
    public double? AverageHeartRate => MainSleep?.AverageHeartRate;
    public int? AverageHrv => MainSleep?.AverageHrv;
    public double? AverageBreath => MainSleep?.AverageBreath;
    public double? TemperatureDeviation => Readiness?.TemperatureDeviation;
    public double? Spo2Average => SpO2?.Spo2Percentage?.Average;

    /// <summary>いずれかのスコアがあるか</summary>
    public bool HasAnyScore => SleepScore is not null || ReadinessScore is not null || ActivityScore is not null;
}

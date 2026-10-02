using System.Text.Json.Serialization;

namespace RingInfo.Core.Models;

// Oura API v2 (usercollection) のレスポンスモデル。
// JSON のプロパティ名は snake_case（OuraJson.Options の命名ポリシーで変換）。
// 数字を含むプロパティ名は命名ポリシーで正しく変換できないため明示的に指定している。

/// <summary>日単位のドキュメント（day プロパティを持つ）</summary>
public interface IDailyDocument
{
    DateOnly Day { get; }
}

/// <summary>複数ドキュメントのレスポンス（ページング付き）</summary>
public sealed class OuraCollectionResponse<T>
{
    public List<T> Data { get; set; } = [];

    public string? NextToken { get; set; }
}

/// <summary>個人情報</summary>
public sealed class PersonalInfo
{
    public string Id { get; set; } = "";
    public int? Age { get; set; }
    public double? Weight { get; set; }
    public double? Height { get; set; }
    public string? BiologicalSex { get; set; }
    public string? Email { get; set; }
}

/// <summary>日次の睡眠スコア</summary>
public sealed class DailySleep : IDailyDocument
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? Score { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public SleepContributors Contributors { get; set; } = new();
}

/// <summary>睡眠スコアの寄与要素（各 1〜100）</summary>
public sealed class SleepContributors
{
    public int? DeepSleep { get; set; }
    public int? Efficiency { get; set; }
    public int? Latency { get; set; }
    public int? RemSleep { get; set; }
    public int? Restfulness { get; set; }
    public int? Timing { get; set; }
    public int? TotalSleep { get; set; }
}

/// <summary>日次のコンディション（Readiness）スコア</summary>
public sealed class DailyReadiness : IDailyDocument
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? Score { get; set; }
    public double? TemperatureDeviation { get; set; }
    public double? TemperatureTrendDeviation { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ReadinessContributors Contributors { get; set; } = new();
}

/// <summary>コンディションスコアの寄与要素（各 1〜100）</summary>
public sealed class ReadinessContributors
{
    public int? ActivityBalance { get; set; }
    public int? BodyTemperature { get; set; }
    public int? HrvBalance { get; set; }
    public int? PreviousDayActivity { get; set; }
    public int? PreviousNight { get; set; }
    public int? RecoveryIndex { get; set; }
    public int? RestingHeartRate { get; set; }
    public int? SleepBalance { get; set; }
    public int? SleepRegularity { get; set; }
}

/// <summary>日次のアクティビティ</summary>
public sealed class DailyActivity : IDailyDocument
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? Score { get; set; }
    public int ActiveCalories { get; set; }
    public int TotalCalories { get; set; }
    public int TargetCalories { get; set; }
    public int Steps { get; set; }
    /// <summary>歩行換算距離（メートル）</summary>
    public int EquivalentWalkingDistance { get; set; }
    public int TargetMeters { get; set; }
    public int MetersToTarget { get; set; }
    /// <summary>高強度の活動時間（秒）</summary>
    public int HighActivityTime { get; set; }
    /// <summary>中強度の活動時間（秒）</summary>
    public int MediumActivityTime { get; set; }
    /// <summary>低強度の活動時間（秒）</summary>
    public int LowActivityTime { get; set; }
    /// <summary>座っていた時間（秒）</summary>
    public int SedentaryTime { get; set; }
    /// <summary>休息時間（秒）</summary>
    public int RestingTime { get; set; }
    /// <summary>非装着時間（秒）</summary>
    public int NonWearTime { get; set; }
    public int InactivityAlerts { get; set; }
    public double AverageMetMinutes { get; set; }

    [JsonPropertyName("class_5_min")]
    public string? Class5Min { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ActivityContributors Contributors { get; set; } = new();
}

/// <summary>アクティビティスコアの寄与要素（各 1〜100）</summary>
public sealed class ActivityContributors
{
    public int? MeetDailyTargets { get; set; }
    public int? MoveEveryHour { get; set; }
    public int? RecoveryTime { get; set; }
    public int? StayActive { get; set; }
    public int? TrainingFrequency { get; set; }
    public int? TrainingVolume { get; set; }
}

/// <summary>一定間隔のサンプル列（睡眠中の心拍・HRV など）</summary>
public sealed class SampleSeries
{
    /// <summary>サンプル間隔（秒）</summary>
    public double Interval { get; set; }
    public List<double?> Items { get; set; } = [];
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>睡眠期間（1 晩の睡眠や昼寝）</summary>
public sealed class SleepPeriod : IDailyDocument
{
    public const string TypeLongSleep = "long_sleep";

    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    /// <summary>deleted / sleep / long_sleep / late_nap / rest</summary>
    public string? Type { get; set; }
    public DateTimeOffset BedtimeStart { get; set; }
    public DateTimeOffset BedtimeEnd { get; set; }
    /// <summary>合計睡眠時間（秒）</summary>
    public int? TotalSleepDuration { get; set; }
    public int? DeepSleepDuration { get; set; }
    public int? RemSleepDuration { get; set; }
    public int? LightSleepDuration { get; set; }
    /// <summary>覚醒時間（秒）</summary>
    public int? AwakeTime { get; set; }
    /// <summary>ベッドにいた時間（秒）</summary>
    public int TimeInBed { get; set; }
    public int? Efficiency { get; set; }
    /// <summary>入眠までの時間（秒）</summary>
    public int? Latency { get; set; }
    public double? AverageHeartRate { get; set; }
    public int? LowestHeartRate { get; set; }
    public int? AverageHrv { get; set; }
    /// <summary>平均呼吸数（回/分）</summary>
    public double? AverageBreath { get; set; }
    public int? RestlessPeriods { get; set; }
    public SampleSeries? HeartRate { get; set; }
    public SampleSeries? Hrv { get; set; }

    /// <summary>5 分ごとの睡眠ステージ（1=深い, 2=浅い, 3=レム, 4=覚醒）</summary>
    [JsonPropertyName("sleep_phase_5_min")]
    public string? SleepPhase5Min { get; set; }

    [JsonPropertyName("movement_30_sec")]
    public string? Movement30Sec { get; set; }
}

/// <summary>心拍数のサンプル</summary>
public sealed class HeartRateSample
{
    public const string SourceAwake = "awake";
    public const string SourceSleep = "sleep";
    public const string SourceWorkout = "workout";

    public DateTimeOffset Timestamp { get; set; }
    public int Bpm { get; set; }
    /// <summary>awake / workout / rest / sleep / live / session</summary>
    public string? Source { get; set; }
}

/// <summary>日次の血中酸素（睡眠中の平均）</summary>
public sealed class DailySpO2 : IDailyDocument
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? BreathingDisturbanceIndex { get; set; }
    public SpO2Values? Spo2Percentage { get; set; }
}

public sealed class SpO2Values
{
    public double? Average { get; set; }
}

/// <summary>日次のストレス</summary>
public sealed class DailyStress : IDailyDocument
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    /// <summary>高ストレス状態の時間（秒）</summary>
    public int? StressHigh { get; set; }
    /// <summary>高回復状態の時間（秒）</summary>
    public int? RecoveryHigh { get; set; }
    /// <summary>restored / normal / stressful</summary>
    public string? DaySummary { get; set; }
}

/// <summary>ワークアウト</summary>
public sealed class Workout : IDailyDocument
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public string? Activity { get; set; }
    public double? Calories { get; set; }
    /// <summary>距離（メートル）</summary>
    public double? Distance { get; set; }
    public DateTimeOffset StartDatetime { get; set; }
    public DateTimeOffset EndDatetime { get; set; }
    /// <summary>easy / moderate / hard</summary>
    public string? Intensity { get; set; }
    public string? Label { get; set; }
    public string? Source { get; set; }
}

/// <summary>リングの構成情報</summary>
public sealed class RingConfiguration
{
    public string Id { get; set; } = "";
    public string? Color { get; set; }
    public string? Design { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? HardwareType { get; set; }
    public DateTimeOffset? SetUpAt { get; set; }
    public int? Size { get; set; }
}

/// <summary>リングの電池残量</summary>
public sealed class RingBatteryLevel
{
    public DateTimeOffset Timestamp { get; set; }
    public int Level { get; set; }
    public bool? Charging { get; set; }
    public bool? InCharger { get; set; }
}

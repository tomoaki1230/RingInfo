using System.Text;
using RingInfo.Core.Api;
using RingInfo.Core.Models;

namespace RingInfo.Core.Demo;

/// <summary>
/// Oura と未連携の状態で画面を確認するためのデモデータ。
/// 日付ごとに固定の乱数シードを使うため、同じ日は常に同じ値になる。
/// </summary>
public sealed class DemoDataSource : IOuraDataSource
{
    private const int SlotSeconds = 300;

    private static readonly string[] WorkoutActivities = ["walking", "running", "cycling", "strength_training", "yoga"];

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public DemoDataSource(TimeProvider? timeProvider = null, TimeZoneInfo? timeZone = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    private DateTimeOffset Now => TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone);

    private DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public Task<PersonalInfo?> GetPersonalInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<PersonalInfo?>(new PersonalInfo
        {
            Id = "demo-user",
            Age = 36,
            Height = 1.70,
            Weight = 63.5,
            BiologicalSex = null,
            Email = "demo@example.com",
        });

    public Task<IReadOnlyList<DailySleep>> GetDailySleepAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DailySleep>>(EachNight(start, end).Select(CreateDailySleep).ToList());

    public Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SleepPeriod>>(EachNight(start, end).Select(CreateSleepPeriod).ToList());

    public Task<IReadOnlyList<DailyReadiness>> GetDailyReadinessAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DailyReadiness>>(EachNight(start, end).Select(CreateReadiness).ToList());

    public Task<IReadOnlyList<DailySpO2>> GetDailySpO2Async(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DailySpO2>>(EachNight(start, end).Select(CreateSpO2).ToList());

    public Task<IReadOnlyList<DailyActivity>> GetDailyActivityAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DailyActivity>>(EachDay(start, end).Select(CreateActivity).ToList());

    public Task<IReadOnlyList<DailyStress>> GetDailyStressAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DailyStress>>(EachDay(start, end).Select(CreateStress).ToList());

    public Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Workout>>(EachDay(start, end)
            .Select(GetWorkout)
            .OfType<Workout>()
            .Where(w => w.EndDatetime <= Now)
            .ToList());

    public Task<IReadOnlyList<HeartRateSample>> GetHeartRateAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
    {
        var samples = new List<HeartRateSample>();
        var now = Now;
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, _timeZone).DateTime);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, _timeZone).DateTime);

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var morningNight = GetNight(day);
            var eveningNight = GetNight(day.AddDays(1));
            var workout = GetWorkout(day);
            var random = CreateRandom(day, 11);

            for (var t = LocalTime(day, 0); t < LocalTime(day.AddDays(1), 0); t = t.AddSeconds(SlotSeconds))
            {
                if (t < start || t >= end || t > now)
                {
                    continue;
                }

                var sleepBpm = morningNight.HeartRateAt(t) ?? eveningNight.HeartRateAt(t);
                if (sleepBpm is { } bpm)
                {
                    samples.Add(new HeartRateSample { Timestamp = t, Bpm = bpm, Source = HeartRateSample.SourceSleep });
                    continue;
                }

                if (workout is not null && t >= workout.StartDatetime && t < workout.EndDatetime)
                {
                    var progress = (t - workout.StartDatetime).TotalSeconds / (workout.EndDatetime - workout.StartDatetime).TotalSeconds;
                    var peak = workout.Intensity == "hard" ? 155 : workout.Intensity == "moderate" ? 135 : 112;
                    var value = peak - 25 * Math.Abs(progress - 0.55) + random.Next(-4, 5);
                    samples.Add(new HeartRateSample { Timestamp = t, Bpm = (int)Math.Round(value), Source = HeartRateSample.SourceWorkout });
                    continue;
                }

                // 日中: 朝から午後にかけて緩やかに上がり、夜に下がる
                var hour = (t - LocalTime(day, 0)).TotalHours;
                var daily = 66 + 8 * Math.Sin((hour - 8) / 14 * Math.PI);
                var spike = random.NextDouble() < 0.06 ? random.Next(10, 26) : 0;
                var awake = daily + spike + random.Next(-4, 5);
                samples.Add(new HeartRateSample { Timestamp = t, Bpm = (int)Math.Round(awake), Source = HeartRateSample.SourceAwake });
            }
        }

        return Task.FromResult<IReadOnlyList<HeartRateSample>>(samples);
    }

    public Task<IReadOnlyList<RingConfiguration>> GetRingConfigurationsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RingConfiguration>>(
        [
            new RingConfiguration
            {
                Id = "demo-ring",
                HardwareType = "gen4",
                Color = "stealth_black",
                Design = "horizon",
                Size = 9,
                FirmwareVersion = "2.9.24",
                SetUpAt = Now.AddDays(-214),
            },
        ]);

    public Task<RingBatteryLevel?> GetLatestBatteryLevelAsync(CancellationToken cancellationToken = default)
    {
        // 時刻に応じて少しずつ減っていく電池残量
        var hour = Now.Hour + Now.Minute / 60.0;
        var level = (int)Math.Clamp(Math.Round(92 - hour * 1.6), 15, 100);
        return Task.FromResult<RingBatteryLevel?>(new RingBatteryLevel { Timestamp = Now.AddMinutes(-7), Level = level, Charging = false, InCharger = false });
    }

    // ---------------------------------------------------------------- 日付の列挙

    private IEnumerable<DateOnly> EachDay(DateOnly start, DateOnly end)
    {
        var last = end > Today ? Today : end;
        for (var day = start; day <= last; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    /// <summary>睡眠が完了している日（起床日）だけを返す</summary>
    private IEnumerable<Night> EachNight(DateOnly start, DateOnly end)
        => EachDay(start, end).Select(GetNight).Where(n => n.BedtimeEnd <= Now);

    /// <summary>
    /// 日付と用途ごとの乱数。連続した値をそのままシードにすると最初の乱数が似通うため、整数ハッシュで撹拌する。
    /// </summary>
    private static Random CreateRandom(DateOnly day, int salt)
    {
        var x = unchecked((uint)((day.DayNumber * 31) + salt));
        x ^= x >> 16;
        x = unchecked(x * 0x7FEB352Du);
        x ^= x >> 15;
        x = unchecked(x * 0x846CA68Bu);
        x ^= x >> 16;
        return new Random((int)(x & 0x7FFFFFFF));
    }

    private DateTimeOffset LocalTime(DateOnly day, double hours)
    {
        var local = day.ToDateTime(TimeOnly.MinValue).AddHours(hours);
        return new DateTimeOffset(local, _timeZone.GetUtcOffset(local));
    }

    // ---------------------------------------------------------------- 睡眠

    /// <summary>起床日 day の夜の睡眠を生成する</summary>
    private Night GetNight(DateOnly day)
    {
        var random = CreateRandom(day, 1);
        var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        // 前日 23 時前後に就寝し、6.5〜9 時間ベッドにいる
        var startHour = 23.1 + ((random.NextDouble() - 0.5) * 1.6) + (weekend ? 0.5 : 0);
        var slots = (int)Math.Round((6.6 + (random.NextDouble() * 1.8) + (weekend ? 0.5 : 0)) * 12);
        var bedtimeStart = LocalTime(day.AddDays(-1), Math.Round(startHour * 60) / 60);
        var phases = BuildHypnogram(random, slots);

        var lowest = 47 + random.Next(0, 7);
        var hrv = 38 + random.Next(0, 30);
        var heartRates = new List<double?>(slots);
        var hrvs = new List<double?>(slots);
        for (var i = 0; i < slots; i++)
        {
            var progress = (double)i / slots;
            var curve = 9 * Math.Pow(progress - 0.55, 2) / 0.3;
            var awakeBoost = phases[i] == '4' ? 6 : 0;
            heartRates.Add(Math.Round(lowest + 2 + curve + awakeBoost + ((random.NextDouble() - 0.5) * 2)));
            hrvs.Add(Math.Round(Math.Max(10, hrv + (14 * (0.3 - Math.Pow(progress - 0.55, 2))) - (awakeBoost * 1.5) + ((random.NextDouble() - 0.5) * 5))));
        }

        return new Night(
            day,
            bedtimeStart,
            bedtimeStart.AddSeconds(slots * SlotSeconds),
            phases,
            heartRates,
            hrvs,
            lowest,
            hrv,
            13.2 + (random.NextDouble() * 2.6),
            random.Next(8, 30));
    }

    /// <summary>90 分周期の睡眠サイクルを模した 5 分刻みのステージ列（1=深い, 2=浅い, 3=レム, 4=覚醒）</summary>
    private static string BuildHypnogram(Random random, int slots)
    {
        var builder = new StringBuilder(slots);

        void Append(char phase, int count)
        {
            for (var i = 0; i < count && builder.Length < slots - 1; i++)
            {
                builder.Append(phase);
            }
        }

        Append('4', 1 + random.Next(0, 3));
        for (var cycle = 0; builder.Length < slots - 1; cycle++)
        {
            Append('2', 3 + random.Next(0, 3));
            Append('1', cycle switch
            {
                0 => 5 + random.Next(0, 3),
                1 => 3 + random.Next(0, 3),
                2 => random.Next(0, 3),
                _ => random.Next(0, 2),
            });
            Append('2', 2 + random.Next(0, 3));
            Append('3', cycle switch
            {
                0 => 1 + random.Next(0, 2),
                1 => 2 + random.Next(0, 3),
                _ => 3 + random.Next(0, 4),
            });
            if (random.NextDouble() < 0.35)
            {
                Append('4', 1);
            }
        }

        builder.Append('4');
        return builder.ToString();
    }

    private DailySleep CreateDailySleep(Night night)
    {
        var random = CreateRandom(night.Day, 2);
        var totalHours = night.TotalSleepSeconds / 3600.0;
        var contributors = new SleepContributors
        {
            TotalSleep = Clamp(40 + ((totalHours - 5) * 22)),
            Efficiency = Clamp((night.Efficiency - 70) * 3.4),
            Latency = Clamp(100 - Math.Max(0, (night.LatencySeconds / 60.0) - 10) * 2.5),
            DeepSleep = Clamp(night.DeepSeconds / (double)night.TotalSleepSeconds * 520),
            RemSleep = Clamp(night.RemSeconds / (double)night.TotalSleepSeconds * 420),
            Restfulness = Clamp(95 - (night.RestlessPeriods * 1.4)),
            Timing = Clamp(98 - (Math.Abs(night.MidpointHour - 3.0) * 18) - random.Next(0, 6)),
        };
        var score = (0.35 * contributors.TotalSleep!.Value) + (0.1 * contributors.Efficiency!.Value) + (0.1 * contributors.Restfulness!.Value)
            + (0.1 * contributors.RemSleep!.Value) + (0.1 * contributors.DeepSleep!.Value) + (0.1 * contributors.Latency!.Value)
            + (0.15 * contributors.Timing!.Value);

        return new DailySleep
        {
            Id = $"demo-daily-sleep-{night.Day:yyyyMMdd}",
            Day = night.Day,
            Score = Clamp(score, 35, 99),
            Timestamp = LocalTime(night.Day, 0),
            Contributors = contributors,
        };
    }

    private SleepPeriod CreateSleepPeriod(Night night) => new()
    {
        Id = $"demo-sleep-{night.Day:yyyyMMdd}",
        Day = night.Day,
        Type = SleepPeriod.TypeLongSleep,
        BedtimeStart = night.BedtimeStart,
        BedtimeEnd = night.BedtimeEnd,
        TimeInBed = night.TimeInBedSeconds,
        TotalSleepDuration = night.TotalSleepSeconds,
        DeepSleepDuration = night.DeepSeconds,
        LightSleepDuration = night.LightSeconds,
        RemSleepDuration = night.RemSeconds,
        AwakeTime = night.AwakeSeconds,
        Efficiency = (int)Math.Round(night.Efficiency),
        Latency = night.LatencySeconds,
        AverageHeartRate = Math.Round(night.HeartRates.Average(v => v ?? 0), 1),
        LowestHeartRate = night.Lowest,
        AverageHrv = (int)Math.Round(night.Hrvs.Average(v => v ?? 0)),
        AverageBreath = Math.Round(night.Breath, 1),
        RestlessPeriods = night.RestlessPeriods,
        SleepPhase5Min = night.Phases,
        HeartRate = new SampleSeries { Interval = SlotSeconds, Timestamp = night.BedtimeStart, Items = night.HeartRates },
        Hrv = new SampleSeries { Interval = SlotSeconds, Timestamp = night.BedtimeStart, Items = night.Hrvs },
    };

    private DailyReadiness CreateReadiness(Night night)
    {
        var random = CreateRandom(night.Day, 3);
        var sleepScore = CreateDailySleep(night).Score ?? 70;
        var hrvBalance = Clamp(55 + ((night.AverageHrv - 38) * 1.4) + random.Next(-5, 6));
        var temperature = Math.Round(((random.NextDouble() - 0.5) * 0.7) + (random.NextDouble() < 0.1 ? 0.5 : 0), 2);
        var contributors = new ReadinessContributors
        {
            ActivityBalance = Clamp(70 + random.Next(-10, 25)),
            BodyTemperature = Clamp(100 - (Math.Abs(temperature) * 70)),
            HrvBalance = hrvBalance,
            PreviousDayActivity = Clamp(65 + random.Next(-15, 30)),
            PreviousNight = Clamp(sleepScore + random.Next(-6, 7)),
            RecoveryIndex = Clamp(75 + random.Next(-20, 25)),
            RestingHeartRate = Clamp(100 - ((night.Lowest - 46) * 4)),
            SleepBalance = Clamp(72 + random.Next(-12, 20)),
            SleepRegularity = Clamp(70 + random.Next(-15, 25)),
        };
        var score = (0.4 * sleepScore) + (0.3 * hrvBalance) + (0.15 * contributors.RestingHeartRate!.Value) + (0.15 * contributors.BodyTemperature!.Value);

        return new DailyReadiness
        {
            Id = $"demo-readiness-{night.Day:yyyyMMdd}",
            Day = night.Day,
            Score = Clamp(score + random.Next(-3, 4), 40, 99),
            TemperatureDeviation = temperature,
            TemperatureTrendDeviation = Math.Round(temperature * 0.6, 2),
            Timestamp = LocalTime(night.Day, 0),
            Contributors = contributors,
        };
    }

    private DailySpO2 CreateSpO2(Night night)
    {
        var random = CreateRandom(night.Day, 4);
        return new DailySpO2
        {
            Id = $"demo-spo2-{night.Day:yyyyMMdd}",
            Day = night.Day,
            Spo2Percentage = new SpO2Values { Average = Math.Round(95.4 + (random.NextDouble() * 3.2), 3) },
            BreathingDisturbanceIndex = random.Next(0, 14),
        };
    }

    // ---------------------------------------------------------------- アクティビティ

    private DailyActivity CreateActivity(DateOnly day)
    {
        var random = CreateRandom(day, 5);
        var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        // 当日は現在時刻までの途中経過にする
        var fraction = day == Today ? Math.Clamp((Now - LocalTime(day, 0)).TotalHours / 24, 0.05, 1) : 1;
        var workout = GetWorkout(day);
        var hasWorkout = workout is not null && workout.EndDatetime <= Now;

        var steps = (int)Math.Round(((weekend ? 5500 : 7000) + random.Next(0, 6500) + (hasWorkout ? 2500 : 0)) * fraction);
        var high = (int)Math.Round(((hasWorkout ? 25 : 3) + random.Next(0, 15)) * 60 * fraction);
        var medium = (int)Math.Round((25 + random.Next(0, 45)) * 60 * fraction);
        var low = (int)Math.Round((150 + random.Next(0, 160)) * 60 * fraction);
        var activeCalories = (int)Math.Round((steps * 0.035) + (high / 60.0 * 9) + (medium / 60.0 * 4) + random.Next(0, 60));
        var nonWear = random.Next(0, 4) * 15 * 60;
        var resting = (int)Math.Round((7 + (random.NextDouble() * 1.5)) * 3600 * fraction);
        var sedentary = Math.Max(0, (int)Math.Round(86400 * fraction) - high - medium - low - resting - nonWear);
        var walkingDistance = (int)Math.Round(steps * 0.78);
        const int targetMeters = 9000;

        var contributors = new ActivityContributors
        {
            MeetDailyTargets = Clamp(activeCalories / 5.0 + random.Next(0, 15)),
            MoveEveryHour = Clamp(100 - random.Next(0, 6) * 8),
            RecoveryTime = Clamp(80 + random.Next(-20, 20)),
            StayActive = Clamp(100 - (sedentary / 3600.0 * 5) + random.Next(0, 10)),
            TrainingFrequency = Clamp(60 + random.Next(0, 40)),
            TrainingVolume = Clamp(55 + random.Next(0, 45)),
        };
        var score = contributors.MeetDailyTargets!.Value * 0.25 + contributors.MoveEveryHour!.Value * 0.2 + contributors.RecoveryTime!.Value * 0.15
            + contributors.StayActive!.Value * 0.2 + contributors.TrainingFrequency!.Value * 0.1 + contributors.TrainingVolume!.Value * 0.1;

        return new DailyActivity
        {
            Id = $"demo-activity-{day:yyyyMMdd}",
            Day = day,
            Score = Clamp(score, 40, 99),
            Steps = steps,
            ActiveCalories = activeCalories,
            TotalCalories = (int)Math.Round(1650 * fraction) + activeCalories,
            TargetCalories = 450,
            EquivalentWalkingDistance = walkingDistance,
            TargetMeters = targetMeters,
            MetersToTarget = Math.Max(0, targetMeters - walkingDistance),
            HighActivityTime = high,
            MediumActivityTime = medium,
            LowActivityTime = low,
            SedentaryTime = sedentary,
            RestingTime = resting,
            NonWearTime = nonWear,
            InactivityAlerts = random.Next(0, 3),
            AverageMetMinutes = Math.Round(1.4 + (random.NextDouble() * 0.6), 2),
            Timestamp = LocalTime(day, 4),
            Contributors = contributors,
        };
    }

    private DailyStress CreateStress(DateOnly day)
    {
        var random = CreateRandom(day, 6);
        var stressHigh = random.Next(0, 17) * 900;
        var recoveryHigh = random.Next(0, 13) * 900;
        var summary = stressHigh > recoveryHigh * 1.8 ? "stressful" : recoveryHigh > stressHigh * 1.5 ? "restored" : "normal";
        return new DailyStress
        {
            Id = $"demo-stress-{day:yyyyMMdd}",
            Day = day,
            StressHigh = stressHigh,
            RecoveryHigh = recoveryHigh,
            DaySummary = day == Today ? null : summary,
        };
    }

    private Workout? GetWorkout(DateOnly day)
    {
        var random = CreateRandom(day, 7);
        if (random.NextDouble() >= 0.45)
        {
            return null;
        }

        var activity = WorkoutActivities[random.Next(WorkoutActivities.Length)];
        var minutes = 30 + random.Next(0, 45);
        var start = LocalTime(day, 18 + (random.Next(0, 6) * 0.25));
        var intensity = random.Next(0, 3) switch { 0 => "easy", 1 => "moderate", _ => "hard" };
        double? distance = activity switch
        {
            "walking" => minutes * 85,
            "running" => minutes * 170,
            "cycling" => minutes * 330,
            _ => null,
        };

        return new Workout
        {
            Id = $"demo-workout-{day:yyyyMMdd}",
            Day = day,
            Activity = activity,
            Intensity = intensity,
            StartDatetime = start,
            EndDatetime = start.AddMinutes(minutes),
            Calories = Math.Round(minutes * (intensity == "hard" ? 10.5 : intensity == "moderate" ? 7.5 : 4.5)),
            Distance = distance,
            Source = "autodetected",
        };
    }

    private static int Clamp(double value, int min = 1, int max = 100) => (int)Math.Clamp(Math.Round(value), min, max);

    /// <summary>1 晩分の睡眠（デモ用の内部表現）</summary>
    private sealed record Night(
        DateOnly Day,
        DateTimeOffset BedtimeStart,
        DateTimeOffset BedtimeEnd,
        string Phases,
        List<double?> HeartRates,
        List<double?> Hrvs,
        int Lowest,
        int AverageHrv,
        double Breath,
        int RestlessPeriods)
    {
        private int Count(char phase) => Phases.Count(c => c == phase) * SlotSeconds;

        public int DeepSeconds => Count('1');
        public int LightSeconds => Count('2');
        public int RemSeconds => Count('3');
        public int AwakeSeconds => Count('4');
        public int TotalSleepSeconds => DeepSeconds + LightSeconds + RemSeconds;
        public int TimeInBedSeconds => Phases.Length * SlotSeconds;
        public double Efficiency => TotalSleepSeconds * 100.0 / TimeInBedSeconds;
        public int LatencySeconds => Phases.TakeWhile(c => c == '4').Count() * SlotSeconds;

        /// <summary>睡眠の中間時刻（0 時からの時間。前日の夜は負の値）</summary>
        public double MidpointHour
        {
            get
            {
                var midpoint = BedtimeStart.AddSeconds(TimeInBedSeconds / 2.0);
                return (midpoint.DateTime - Day.ToDateTime(TimeOnly.MinValue)).TotalHours;
            }
        }

        /// <summary>時刻 t が睡眠中ならその心拍数を返す</summary>
        public int? HeartRateAt(DateTimeOffset t)
        {
            if (t < BedtimeStart || t >= BedtimeEnd)
            {
                return null;
            }

            var index = (int)((t - BedtimeStart).TotalSeconds / SlotSeconds);
            return index < HeartRates.Count ? (int?)Math.Round(HeartRates[index] ?? Lowest) : null;
        }
    }
}

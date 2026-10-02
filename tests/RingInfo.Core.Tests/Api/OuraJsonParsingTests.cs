using System.Text.Json;
using RingInfo.Core.Api;
using RingInfo.Core.Models;
using RingInfo.Core.Tests.Helpers;

namespace RingInfo.Core.Tests.Api;

/// <summary>Oura サンドボックスの実レスポンスを使った JSON 解析テスト</summary>
public class OuraJsonParsingTests
{
    private static OuraCollectionResponse<T> Parse<T>(string file)
        => JsonSerializer.Deserialize<OuraCollectionResponse<T>>(TestData.Read(file), OuraJson.Options)!;

    [Fact]
    public void DailySleep_を解析できる()
    {
        var item = Parse<DailySleep>("daily_sleep.json").Data[0];

        Assert.Equal(new DateOnly(2026, 9, 24), item.Day);
        Assert.Equal(73, item.Score);
        Assert.Equal(90, item.Contributors.DeepSleep);
        Assert.Equal(80, item.Contributors.TotalSleep);
    }

    [Fact]
    public void DailyReadiness_を解析できる()
    {
        var item = Parse<DailyReadiness>("daily_readiness.json").Data[0];

        Assert.Equal(80, item.Score);
        Assert.Equal(0.5, item.TemperatureDeviation);
        Assert.Equal(80, item.Contributors.HrvBalance);
        Assert.Null(item.Contributors.SleepRegularity);
    }

    [Fact]
    public void DailyActivity_を解析できる_数字を含むプロパティ名も対象()
    {
        var item = Parse<DailyActivity>("daily_activity.json").Data[0];

        Assert.Equal(200, item.ActiveCalories);
        Assert.Equal(2000, item.EquivalentWalkingDistance);
        Assert.Equal("3241312341234123", item.Class5Min);
        Assert.Equal(64, item.Contributors.MeetDailyTargets);
    }

    [Fact]
    public void Sleep_を解析できる()
    {
        var item = Parse<SleepPeriod>("sleep.json").Data[0];

        Assert.Equal(56, item.AverageHrv);
        Assert.Equal(2370, item.TotalSleepDuration);
        Assert.Equal(3000, item.TimeInBed);
        Assert.Equal("late_nap", item.Type);
        Assert.Equal("123142341232", item.Movement30Sec);
        Assert.NotNull(item.HeartRate);
        Assert.Equal(60, item.HeartRate!.Interval);
        Assert.Equal(73, item.HeartRate.Items[0]);
    }

    [Fact]
    public void SpO2_Stress_Workout_RingConfiguration_を解析できる()
    {
        var spo2 = Parse<DailySpO2>("daily_spo2.json").Data[0];
        Assert.Equal(93.0, spo2.Spo2Percentage?.Average);
        Assert.Equal(1, spo2.BreathingDisturbanceIndex);

        var stress = Parse<DailyStress>("daily_stress.json").Data[0];
        Assert.Equal("normal", stress.DaySummary);
        Assert.Equal(15, stress.StressHigh);

        var workout = Parse<Workout>("workout.json").Data[0];
        Assert.Equal("running", workout.Activity);
        Assert.Equal("hard", workout.Intensity);

        var ring = Parse<RingConfiguration>("ring_configuration.json").Data[0];
        Assert.Equal("gen3", ring.HardwareType);
        Assert.Equal(12, ring.Size);
    }

    [Fact]
    public void HeartRate_を解析できる()
    {
        var item = Parse<HeartRateSample>("heartrate.json").Data[0];

        Assert.Equal(60, item.Bpm);
        Assert.Equal("awake", item.Source);
        Assert.Equal(TimeSpan.Zero, item.Timestamp.Offset);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using RingInfo.App.Charts;
using RingInfo.Core.Formatting;
using RingInfo.Core.Services;

namespace RingInfo.App.ViewModels;

/// <summary>ダッシュボード: 最新のスコアと主要な指標、スコアの推移、リング情報</summary>
public sealed partial class DashboardPageViewModel : PageViewModel
{
    public override string Title => "ダッシュボード";

    public override string IconGlyph => "";

    [ObservableProperty]
    private ScoreCard _sleepCard = ScoreCard.Empty("睡眠");

    [ObservableProperty]
    private ScoreCard _readinessCard = ScoreCard.Empty("コンディション");

    [ObservableProperty]
    private ScoreCard _activityCard = ScoreCard.Empty("アクティビティ");

    [ObservableProperty]
    private IReadOnlyList<MetricTile> _tiles = [];

    [ObservableProperty]
    private ChartData? _scoreTrend;

    [ObservableProperty]
    private IReadOnlyList<InfoRow> _ringInfo = [];

    [ObservableProperty]
    private string _periodText = "";

    [ObservableProperty]
    private bool _hasData;

    private DashboardSnapshot? _snapshot;

    public override void Update(DashboardSnapshot snapshot)
    {
        _snapshot = snapshot;
        HasData = snapshot.Latest is not null;
        PeriodText = $"{DisplayFormat.DayLong(snapshot.StartDate)} 〜 {DisplayFormat.DayLong(snapshot.EndDate)}";

        // スコアは指標ごとに値のある最新の日を表示する
        var sleepDay = snapshot.Days.LastOrDefault(d => d.SleepScore is not null);
        var readinessDay = snapshot.Days.LastOrDefault(d => d.ReadinessScore is not null);
        var activityDay = snapshot.Days.LastOrDefault(d => d.ActivityScore is not null);

        SleepCard = new ScoreCard(
            "睡眠",
            sleepDay?.SleepScore,
            DayText(sleepDay),
            $"合計睡眠 {DisplayFormat.Duration(sleepDay?.MainSleep?.TotalSleepDuration)}",
            AverageText(snapshot.Average(d => d.SleepScore)));
        ReadinessCard = new ScoreCard(
            "コンディション",
            readinessDay?.ReadinessScore,
            DayText(readinessDay),
            $"体温 {DisplayFormat.Temperature(readinessDay?.TemperatureDeviation)}",
            AverageText(snapshot.Average(d => d.ReadinessScore)));
        ActivityCard = new ScoreCard(
            "アクティビティ",
            activityDay?.ActivityScore,
            DayText(activityDay),
            $"歩数 {DisplayFormat.Number(activityDay?.Steps)}",
            AverageText(snapshot.Average(d => d.ActivityScore)));

        Tiles = BuildTiles(snapshot);

        BuildScoreTrend(snapshot);
        RingInfo = BuildRingInfo(snapshot);
    }

    protected override void RebuildCharts()
    {
        if (_snapshot is not null)
        {
            BuildScoreTrend(_snapshot);
        }
    }

    private void BuildScoreTrend(DashboardSnapshot snapshot)
    {
        var days = snapshot.Days.Select(d => d.Day).ToList();
        ScoreTrend = ChartFactory.DailyLines(
            days,
            [
                new DailySeries("睡眠", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.SleepScore).ToList(), v => $"{v:0}"),
                new DailySeries("コンディション", ChartTheme.Series2, snapshot.Days.Select(d => (double?)d.ReadinessScore).ToList(), v => $"{v:0}"),
                new DailySeries("アクティビティ", ChartTheme.Series3, snapshot.Days.Select(d => (double?)d.ActivityScore).ToList(), v => $"{v:0}"),
            ],
            minimum: ScoreAxisMinimum(snapshot),
            maximum: 100);
    }

    private static List<MetricTile> BuildTiles(DashboardSnapshot snapshot)
    {
        T? LatestValue<T>(Func<DayMetrics, T?> selector) where T : struct
            => snapshot.Days.Select(selector).LastOrDefault(v => v.HasValue);

        var stressDay = snapshot.Days.LastOrDefault(d => d.Stress?.StressHigh is not null);
        var activityDay = snapshot.Days.LastOrDefault(d => d.Activity is not null);

        return
        [
            new MetricTile("安静時心拍数", DisplayFormat.Number(LatestValue(d => d.LowestHeartRate)), "bpm", "睡眠中の最低値"),
            new MetricTile("心拍変動（HRV）", DisplayFormat.Number(LatestValue(d => d.AverageHrv)), "ms", "睡眠中の平均"),
            new MetricTile("呼吸数", DisplayFormat.Number(LatestValue(d => d.AverageBreath), 1), "回/分", "睡眠中の平均"),
            new MetricTile("血中酸素（SpO2）", DisplayFormat.Number(LatestValue(d => d.Spo2Average), 1), "%", "睡眠中の平均"),
            new MetricTile("総消費カロリー", DisplayFormat.Number(activityDay?.TotalCalories), "kcal",
                activityDay is null ? null : $"活動 {DisplayFormat.Number(activityDay.ActiveCalories)} kcal"),
            new MetricTile("歩行換算距離", DisplayFormat.Distance(activityDay?.Activity?.EquivalentWalkingDistance), null,
                activityDay is null ? null : DisplayFormat.DayShort(activityDay.Day)),
            new MetricTile("高ストレス時間", DisplayFormat.Duration(stressDay?.Stress?.StressHigh), null,
                stressDay is null ? null : $"回復 {DisplayFormat.Duration(stressDay.Stress?.RecoveryHigh)}"),
            new MetricTile("期間の平均睡眠", DisplayFormat.Number(snapshot.Average(d => d.TotalSleepHours), 1), "時間", "1 晩あたり"),
        ];
    }

    private static List<InfoRow> BuildRingInfo(DashboardSnapshot snapshot)
    {
        var rows = new List<InfoRow>();
        if (snapshot.Ring is { } ring)
        {
            rows.Add(new InfoRow("モデル", Labels.HardwareType(ring.HardwareType)));
            rows.Add(new InfoRow("デザイン", Labels.RingDesign(ring.Design)));
            rows.Add(new InfoRow("カラー", Labels.RingColor(ring.Color)));
            rows.Add(new InfoRow("サイズ", ring.Size?.ToString() ?? DisplayFormat.NoValue));
            rows.Add(new InfoRow("ファームウェア", ring.FirmwareVersion ?? DisplayFormat.NoValue));
            if (ring.SetUpAt is { } setUpAt)
            {
                rows.Add(new InfoRow("セットアップ日", DisplayFormat.DayLong(DateOnly.FromDateTime(setUpAt.LocalDateTime))));
            }
        }

        else if (!snapshot.IsDemo)
        {
            rows.Add(new InfoRow("リング情報", "取得できませんでした。設定画面から再度連携すると表示されます。"));
        }
        else
        {
            rows.Add(new InfoRow("リング情報", "まだありません"));
        }

        if (snapshot.Battery is { } battery)
        {
            var state = battery.Charging == true ? "（充電中）" : "";
            rows.Add(new InfoRow("電池残量", $"{battery.Level}%{state}  {battery.Timestamp.LocalDateTime:M/d HH:mm} 時点"));
        }

        if (snapshot.PersonalInfo is { } person)
        {
            rows.Add(new InfoRow("アカウント", person.Email ?? DisplayFormat.NoValue));
            if (person.Age is { } age)
            {
                rows.Add(new InfoRow("年齢", $"{age} 歳"));
            }
        }

        return rows;
    }

    private static string DayText(DayMetrics? day) => day is null ? "" : DisplayFormat.DayShort(day.Day);

    private static string AverageText(double? average) => average is null ? "" : $"期間平均 {average:0}";

    /// <summary>スコア軸の下限（データに合わせて 10 刻みで切り下げ、最大 50）</summary>
    internal static double ScoreAxisMinimum(DashboardSnapshot snapshot)
    {
        var min = snapshot.Days
            .SelectMany(d => new[] { d.SleepScore, d.ReadinessScore, d.ActivityScore })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty(50)
            .Min();
        return Math.Min(50, Math.Floor((min - 5) / 10.0) * 10);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using RingInfo.App.Charts;
using RingInfo.Core.Formatting;
using RingInfo.Core.Services;

namespace RingInfo.App.ViewModels;

/// <summary>コンディションページ: Readiness・体温・安静時心拍・HRV・血中酸素・ストレス</summary>
public sealed partial class ConditionPageViewModel : DayPageViewModel
{
    public override string Title => "コンディション";

    public override string IconGlyph => "";

    [ObservableProperty]
    private ScoreCard _scoreCard = ScoreCard.Empty("コンディションスコア");

    [ObservableProperty]
    private IReadOnlyList<MetricTile> _tiles = [];

    [ObservableProperty]
    private IReadOnlyList<ContributorItem> _contributors = [];

    [ObservableProperty]
    private ChartData? _scoreTrend;

    [ObservableProperty]
    private ChartData? _temperatureChart;

    [ObservableProperty]
    private ChartData? _restingHeartRateChart;

    [ObservableProperty]
    private ChartData? _hrvChart;

    [ObservableProperty]
    private ChartData? _spo2Chart;

    [ObservableProperty]
    private ChartData? _stressChart;

    [ObservableProperty]
    private IReadOnlyList<ConditionRow> _rows = [];

    protected override IEnumerable<IDayRow> RowsForSelection => Rows;

    /// <summary>期間のグラフ（テーマ変更時はこれだけを作り直す）</summary>
    protected override void BuildPeriodCharts(DashboardSnapshot snapshot)
    {
        var days = snapshot.Days.Select(d => d.Day).ToList();

        ScoreTrend = ChartFactory.DailyLines(
            days,
            [new DailySeries("コンディションスコア", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.ReadinessScore).ToList(), v => $"{v:0}")],
            minimum: DashboardPageViewModel.ScoreAxisMinimum(snapshot),
            maximum: 100);

        TemperatureChart = ChartFactory.DivergingBars(
            days,
            new DailySeries("体温偏差", ChartTheme.DivergingPositive, snapshot.Days.Select(d => d.TemperatureDeviation).ToList(), v => DisplayFormat.Temperature(v)),
            minimumRange: 0.5);

        RestingHeartRateChart = ChartFactory.DailyLines(
            days,
            [new DailySeries("安静時心拍数", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.LowestHeartRate).ToList(), v => $"{v:0} bpm")]);

        HrvChart = ChartFactory.DailyLines(
            days,
            [new DailySeries("平均 HRV", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.AverageHrv).ToList(), v => $"{v:0} ms")]);

        Spo2Chart = ChartFactory.DailyLines(
            days,
            [new DailySeries("血中酸素", ChartTheme.Series1, snapshot.Days.Select(d => d.Spo2Average).ToList(), v => $"{v:0.0}%")],
            maximum: 100);

        static double? Minutes(int? seconds) => seconds / 60.0;
        static string MinutesText(double minutes) => DisplayFormat.Duration((int)Math.Round(minutes * 60));
        StressChart = ChartFactory.DailyBars(
            days,
            [
                new DailySeries("回復", ChartTheme.Series1, snapshot.Days.Select(d => Minutes(d.Stress?.RecoveryHigh)).ToList(), MinutesText),
                new DailySeries("ストレス", ChartTheme.Series2, snapshot.Days.Select(d => Minutes(d.Stress?.StressHigh)).ToList(), MinutesText),
            ],
            axisFormat: v => $"{v:0}分");
    }

    protected override void UpdatePeriod(DashboardSnapshot snapshot)
    {
        Rows = snapshot.Days
            .Where(d => d.Readiness is not null || d.SpO2 is not null || d.Stress is not null)
            .OrderByDescending(d => d.Day)
            .Select(d => new ConditionRow(
                d.Day,
                DisplayFormat.DayShort(d.Day),
                DisplayFormat.Score(d.ReadinessScore),
                DisplayFormat.Temperature(d.TemperatureDeviation),
                DisplayFormat.Number(d.LowestHeartRate),
                DisplayFormat.Number(d.AverageHrv),
                DisplayFormat.Percent(d.Spo2Average, 1),
                DisplayFormat.Number(d.SpO2?.BreathingDisturbanceIndex),
                DisplayFormat.DurationShort(d.Stress?.StressHigh),
                DisplayFormat.DurationShort(d.Stress?.RecoveryHigh),
                Labels.StressSummary(d.Stress?.DaySummary)))
            .ToList();
    }

    protected override void UpdateSelectedDay(DayMetrics? metrics, DateOnly day)
    {
        var readiness = metrics?.Readiness;
        var average = Snapshot?.Average(d => d.ReadinessScore);
        ScoreCard = new ScoreCard(
            "コンディションスコア",
            metrics?.ReadinessScore,
            DisplayFormat.DayShort(day),
            readiness is null ? "コンディションのデータがありません" : $"体温 {DisplayFormat.Temperature(readiness.TemperatureDeviation)}",
            average is null ? "" : $"期間平均 {average:0}");

        var stress = metrics?.Stress;
        Tiles =
        [
            new MetricTile("体温偏差", DisplayFormat.Temperature(readiness?.TemperatureDeviation), null,
                readiness?.TemperatureTrendDeviation is { } trend ? $"傾向 {DisplayFormat.Temperature(trend)}" : "基準値との差"),
            new MetricTile("安静時心拍数", DisplayFormat.Number(metrics?.LowestHeartRate), "bpm"),
            new MetricTile("平均 HRV", DisplayFormat.Number(metrics?.AverageHrv), "ms"),
            new MetricTile("血中酸素（SpO2）", DisplayFormat.Number(metrics?.Spo2Average, 1), "%"),
            new MetricTile("呼吸の乱れ指数", DisplayFormat.Number(metrics?.SpO2?.BreathingDisturbanceIndex), null, "睡眠中"),
            new MetricTile("高ストレス時間", DisplayFormat.Duration(stress?.StressHigh)),
            new MetricTile("高回復時間", DisplayFormat.Duration(stress?.RecoveryHigh)),
            new MetricTile("1 日のまとめ", Labels.StressSummary(stress?.DaySummary)),
        ];

        var c = readiness?.Contributors;
        Contributors =
        [
            new ContributorItem("前夜の睡眠", c?.PreviousNight),
            new ContributorItem("睡眠バランス", c?.SleepBalance),
            new ContributorItem("睡眠の規則性", c?.SleepRegularity),
            new ContributorItem("前日の活動", c?.PreviousDayActivity),
            new ContributorItem("活動バランス", c?.ActivityBalance),
            new ContributorItem("体温", c?.BodyTemperature),
            new ContributorItem("安静時心拍数", c?.RestingHeartRate),
            new ContributorItem("HRV バランス", c?.HrvBalance),
            new ContributorItem("回復指数", c?.RecoveryIndex),
        ];
    }
}

/// <summary>コンディションの表の行</summary>
public sealed record ConditionRow(
    DateOnly Day,
    string DayText,
    string Score,
    string Temperature,
    string RestingHeartRate,
    string Hrv,
    string Spo2,
    string BreathingDisturbance,
    string StressHigh,
    string RecoveryHigh,
    string Summary) : IDayRow;

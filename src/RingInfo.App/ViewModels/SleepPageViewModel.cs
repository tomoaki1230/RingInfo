using CommunityToolkit.Mvvm.ComponentModel;
using RingInfo.App.Charts;
using RingInfo.Core.Formatting;
using RingInfo.Core.Models;
using RingInfo.Core.Services;

namespace RingInfo.App.ViewModels;

/// <summary>睡眠ページ: 選択した夜の詳細（ステージ・心拍）と期間の推移</summary>
public sealed partial class SleepPageViewModel : DayPageViewModel
{
    public override string Title => "睡眠";

    public override string IconGlyph => "";

    [ObservableProperty]
    private ScoreCard _scoreCard = ScoreCard.Empty("睡眠スコア");

    [ObservableProperty]
    private IReadOnlyList<MetricTile> _tiles = [];

    [ObservableProperty]
    private IReadOnlyList<BreakdownItem> _stages = [];

    [ObservableProperty]
    private IReadOnlyList<ContributorItem> _contributors = [];

    [ObservableProperty]
    private ChartData? _hypnogram;

    [ObservableProperty]
    private ChartData? _heartRateChart;

    [ObservableProperty]
    private ChartData? _hrvChart;

    [ObservableProperty]
    private ChartData? _scoreTrend;

    [ObservableProperty]
    private ChartData? _stageTrend;

    [ObservableProperty]
    private IReadOnlyList<SleepRow> _rows = [];

    [ObservableProperty]
    private string _bedtimeText = "";

    protected override IEnumerable<IDayRow> RowsForSelection => Rows;

    /// <summary>期間のグラフ（テーマ変更時はこれだけを作り直す）</summary>
    protected override void BuildPeriodCharts(DashboardSnapshot snapshot)
    {
        var days = snapshot.Days.Select(d => d.Day).ToList();
        ScoreTrend = ChartFactory.DailyLines(
            days,
            [new DailySeries("睡眠スコア", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.SleepScore).ToList(), v => $"{v:0}")],
            minimum: DashboardPageViewModel.ScoreAxisMinimum(snapshot),
            maximum: 100);

        static double? Hours(int? seconds) => seconds / 3600.0;
        static string HoursText(double hours) => DisplayFormat.Duration((int)Math.Round(hours * 3600));

        // 積み上げの順序（下から）: 深い → 浅い → レム → 覚醒
        StageTrend = ChartFactory.DailyBars(
            days,
            [
                new DailySeries("深い睡眠", ChartTheme.StageDeep, snapshot.Days.Select(d => Hours(d.MainSleep?.DeepSleepDuration)).ToList(), HoursText),
                new DailySeries("浅い睡眠", ChartTheme.StageLight, snapshot.Days.Select(d => Hours(d.MainSleep?.LightSleepDuration)).ToList(), HoursText),
                new DailySeries("レム睡眠", ChartTheme.StageRem, snapshot.Days.Select(d => Hours(d.MainSleep?.RemSleepDuration)).ToList(), HoursText),
                new DailySeries("覚醒", ChartTheme.StageAwake, snapshot.Days.Select(d => Hours(d.MainSleep?.AwakeTime)).ToList(), HoursText),
            ],
            stacked: true,
            axisFormat: v => $"{v:0}h");
    }

    protected override void UpdatePeriod(DashboardSnapshot snapshot)
    {
        Rows = snapshot.Days
            .Where(d => d.Sleep is not null || d.MainSleep is not null)
            .OrderByDescending(d => d.Day)
            .Select(d => new SleepRow(
                d.Day,
                DisplayFormat.DayShort(d.Day),
                DisplayFormat.Score(d.SleepScore),
                DisplayFormat.DurationShort(d.MainSleep?.TotalSleepDuration),
                DisplayFormat.DurationShort(d.MainSleep?.DeepSleepDuration),
                DisplayFormat.DurationShort(d.MainSleep?.RemSleepDuration),
                DisplayFormat.DurationShort(d.MainSleep?.LightSleepDuration),
                DisplayFormat.DurationShort(d.MainSleep?.AwakeTime),
                DisplayFormat.Percent(d.MainSleep?.Efficiency),
                DisplayFormat.Time(d.MainSleep?.BedtimeStart),
                DisplayFormat.Time(d.MainSleep?.BedtimeEnd),
                DisplayFormat.Number(d.MainSleep?.LowestHeartRate),
                DisplayFormat.Number(d.MainSleep?.AverageHrv)))
            .ToList();
    }

    protected override void UpdateSelectedDay(DayMetrics? metrics, DateOnly day)
    {
        var sleep = metrics?.MainSleep;
        var average = Snapshot?.Average(d => d.SleepScore);
        ScoreCard = new ScoreCard(
            "睡眠スコア",
            metrics?.SleepScore,
            DisplayFormat.DayShort(day),
            sleep is null ? "睡眠データがありません" : $"合計睡眠 {DisplayFormat.Duration(sleep.TotalSleepDuration)}",
            average is null ? "" : $"期間平均 {average:0}");

        BedtimeText = sleep is null ? "" : $"{DisplayFormat.Time(sleep.BedtimeStart)} 〜 {DisplayFormat.Time(sleep.BedtimeEnd)}";

        Tiles =
        [
            new MetricTile("合計睡眠時間", DisplayFormat.Duration(sleep?.TotalSleepDuration)),
            new MetricTile("ベッドにいた時間", DisplayFormat.Duration(sleep?.TimeInBed)),
            new MetricTile("就寝 〜 起床", sleep is null ? DisplayFormat.NoValue : BedtimeText),
            new MetricTile("睡眠効率", DisplayFormat.Number(sleep?.Efficiency), "%"),
            new MetricTile("入眠までの時間", DisplayFormat.Duration(sleep?.Latency)),
            new MetricTile("平均心拍数", DisplayFormat.Number(sleep?.AverageHeartRate, 0), "bpm", sleep?.LowestHeartRate is { } low ? $"最低 {low} bpm" : null),
            new MetricTile("平均 HRV", DisplayFormat.Number(sleep?.AverageHrv), "ms"),
            new MetricTile("呼吸数", DisplayFormat.Number(sleep?.AverageBreath, 1), "回/分"),
        ];

        var c = metrics?.Sleep?.Contributors;
        Contributors =
        [
            new ContributorItem("合計睡眠時間", c?.TotalSleep),
            new ContributorItem("睡眠効率", c?.Efficiency),
            new ContributorItem("安らかさ", c?.Restfulness),
            new ContributorItem("レム睡眠", c?.RemSleep),
            new ContributorItem("深い睡眠", c?.DeepSleep),
            new ContributorItem("入眠までの時間", c?.Latency),
            new ContributorItem("睡眠のタイミング", c?.Timing),
        ];

    }

    /// <summary>選択日のグラフと色見本（テーマ変更時はこれだけを作り直す）</summary>
    protected override void BuildDayCharts(DayMetrics? metrics, DateOnly day)
    {
        var sleep = metrics?.MainSleep;
        Stages = BuildStages(sleep);
        Hypnogram = ChartFactory.Hypnogram(sleep);
        HeartRateChart = ChartFactory.SleepSamples(sleep, sleep?.HeartRate, "心拍数", "bpm", ChartTheme.Series1);
        HrvChart = ChartFactory.SleepSamples(sleep, sleep?.Hrv, "HRV", "ms", ChartTheme.Series1);
    }

    private static List<BreakdownItem> BuildStages(SleepPeriod? sleep)
    {
        if (sleep is null)
        {
            return [];
        }

        var stages = new (string Name, int? Seconds, OxyPlot.OxyColor Color)[]
        {
            ("覚醒", sleep.AwakeTime, ChartTheme.StageAwake),
            ("レム睡眠", sleep.RemSleepDuration, ChartTheme.StageRem),
            ("浅い睡眠", sleep.LightSleepDuration, ChartTheme.StageLight),
            ("深い睡眠", sleep.DeepSleepDuration, ChartTheme.StageDeep),
        };
        var total = stages.Sum(s => s.Seconds ?? 0);
        return stages
            .Select(s => new BreakdownItem(
                s.Name,
                DisplayFormat.Duration(s.Seconds),
                total > 0 && s.Seconds is { } sec ? $"{sec * 100.0 / total:0}%" : "",
                BreakdownItem.ToBrush(s.Color)))
            .ToList();
    }
}

/// <summary>睡眠の表の行</summary>
public sealed record SleepRow(
    DateOnly Day,
    string DayText,
    string Score,
    string Total,
    string Deep,
    string Rem,
    string Light,
    string Awake,
    string Efficiency,
    string Bedtime,
    string WakeTime,
    string LowestHeartRate,
    string Hrv) : IDayRow;

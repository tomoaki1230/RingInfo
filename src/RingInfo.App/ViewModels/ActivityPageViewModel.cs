using CommunityToolkit.Mvvm.ComponentModel;
using RingInfo.App.Charts;
using RingInfo.Core.Formatting;
using RingInfo.Core.Services;

namespace RingInfo.App.ViewModels;

/// <summary>アクティビティページ: 歩数・消費カロリー・活動時間・ワークアウト</summary>
public sealed partial class ActivityPageViewModel : DayPageViewModel
{
    public override string Title => "アクティビティ";

    public override string IconGlyph => "";

    [ObservableProperty]
    private ScoreCard _scoreCard = ScoreCard.Empty("アクティビティスコア");

    [ObservableProperty]
    private IReadOnlyList<MetricTile> _tiles = [];

    [ObservableProperty]
    private IReadOnlyList<BreakdownItem> _intensity = [];

    [ObservableProperty]
    private IReadOnlyList<ContributorItem> _contributors = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDayWorkouts))]
    private IReadOnlyList<WorkoutRow> _dayWorkouts = [];

    public bool HasDayWorkouts => DayWorkouts.Count > 0;

    [ObservableProperty]
    private ChartData? _stepsChart;

    [ObservableProperty]
    private ChartData? _caloriesChart;

    [ObservableProperty]
    private ChartData? _intensityChart;

    [ObservableProperty]
    private ChartData? _scoreTrend;

    [ObservableProperty]
    private IReadOnlyList<ActivityRow> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<WorkoutRow> _workouts = [];

    protected override IEnumerable<IDayRow> RowsForSelection => Rows;

    /// <summary>期間のグラフ（テーマ変更時はこれだけを作り直す）</summary>
    protected override void BuildPeriodCharts(DashboardSnapshot snapshot)
    {
        var days = snapshot.Days.Select(d => d.Day).ToList();

        StepsChart = ChartFactory.DailyBars(
            days,
            [new DailySeries("歩数", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.Steps).ToList(), v => $"{v:N0} 歩")],
            axisFormat: v => v >= 1000 ? $"{v / 1000:0.#}k" : $"{v:0}");

        CaloriesChart = ChartFactory.DailyBars(
            days,
            [new DailySeries("活動カロリー", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.ActiveCalories).ToList(), v => $"{v:N0} kcal")]);

        static double? Minutes(int? seconds) => seconds / 60.0;
        static string MinutesText(double minutes) => DisplayFormat.Duration((int)Math.Round(minutes * 60));

        // 積み上げの順序（下から）: 低 → 中 → 高
        IntensityChart = ChartFactory.DailyBars(
            days,
            [
                new DailySeries("低強度", ChartTheme.IntensityLow, snapshot.Days.Select(d => Minutes(d.Activity?.LowActivityTime)).ToList(), MinutesText),
                new DailySeries("中強度", ChartTheme.IntensityMedium, snapshot.Days.Select(d => Minutes(d.Activity?.MediumActivityTime)).ToList(), MinutesText),
                new DailySeries("高強度", ChartTheme.IntensityHigh, snapshot.Days.Select(d => Minutes(d.Activity?.HighActivityTime)).ToList(), MinutesText),
            ],
            stacked: true,
            axisFormat: v => $"{v:0}分");

        ScoreTrend = ChartFactory.DailyLines(
            days,
            [new DailySeries("アクティビティスコア", ChartTheme.Series1, snapshot.Days.Select(d => (double?)d.ActivityScore).ToList(), v => $"{v:0}")],
            minimum: DashboardPageViewModel.ScoreAxisMinimum(snapshot),
            maximum: 100);
    }

    protected override void UpdatePeriod(DashboardSnapshot snapshot)
    {
        Rows = snapshot.Days
            .Where(d => d.Activity is not null)
            .OrderByDescending(d => d.Day)
            .Select(d => new ActivityRow(
                d.Day,
                DisplayFormat.DayShort(d.Day),
                DisplayFormat.Score(d.ActivityScore),
                DisplayFormat.Number(d.Steps),
                DisplayFormat.Number(d.ActiveCalories),
                DisplayFormat.Number(d.TotalCalories),
                DisplayFormat.Distance(d.Activity?.EquivalentWalkingDistance),
                DisplayFormat.DurationShort(d.Activity?.HighActivityTime),
                DisplayFormat.DurationShort(d.Activity?.MediumActivityTime),
                DisplayFormat.DurationShort(d.Activity?.LowActivityTime),
                DisplayFormat.DurationShort(d.Activity?.SedentaryTime)))
            .ToList();

        Workouts = snapshot.Days
            .SelectMany(d => d.Workouts)
            .OrderByDescending(w => w.StartDatetime)
            .Select(WorkoutRow.From)
            .ToList();
    }

    protected override void UpdateSelectedDay(DayMetrics? metrics, DateOnly day)
    {
        var activity = metrics?.Activity;
        var average = Snapshot?.Average(d => d.ActivityScore);
        ScoreCard = new ScoreCard(
            "アクティビティスコア",
            metrics?.ActivityScore,
            DisplayFormat.DayShort(day),
            activity is null ? "活動データがありません" : $"歩数 {DisplayFormat.Number(activity.Steps)}",
            average is null ? "" : $"期間平均 {average:0}");

        var targetNote = activity is { TargetCalories: > 0 }
            ? $"目標 {activity.TargetCalories:N0} kcal・{activity.ActiveCalories * 100.0 / activity.TargetCalories:0}%"
            : null;
        Tiles =
        [
            new MetricTile("歩数", DisplayFormat.Number(activity?.Steps), "歩"),
            new MetricTile("活動カロリー", DisplayFormat.Number(activity?.ActiveCalories), "kcal", targetNote),
            new MetricTile("総消費カロリー", DisplayFormat.Number(activity?.TotalCalories), "kcal"),
            new MetricTile("歩行換算距離", DisplayFormat.Distance(activity?.EquivalentWalkingDistance)),
            new MetricTile("座っていた時間", DisplayFormat.Duration(activity?.SedentaryTime)),
            new MetricTile("休息時間", DisplayFormat.Duration(activity?.RestingTime)),
            new MetricTile("非活動アラート", DisplayFormat.Number(activity?.InactivityAlerts), "回"),
            new MetricTile("平均 MET", DisplayFormat.Number(activity?.AverageMetMinutes, 2)),
        ];

        var c = activity?.Contributors;
        Contributors =
        [
            new ContributorItem("毎日の目標の達成", c?.MeetDailyTargets),
            new ContributorItem("1 時間ごとの活動", c?.MoveEveryHour),
            new ContributorItem("活動の継続", c?.StayActive),
            new ContributorItem("回復の時間", c?.RecoveryTime),
            new ContributorItem("トレーニングの頻度", c?.TrainingFrequency),
            new ContributorItem("トレーニングの量", c?.TrainingVolume),
        ];

        DayWorkouts = (metrics?.Workouts ?? []).Select(WorkoutRow.From).ToList();
    }

    /// <summary>選択日の色見本（テーマ変更時はこれだけを作り直す）</summary>
    protected override void BuildDayCharts(DayMetrics? metrics, DateOnly day)
    {
        var activity = metrics?.Activity;
        var activeTotal = activity is null ? 0 : activity.HighActivityTime + activity.MediumActivityTime + activity.LowActivityTime;
        string Share(int? seconds) => activeTotal > 0 && seconds is { } s ? $"{s * 100.0 / activeTotal:0}%" : "";
        Intensity = activity is null
            ? []
            :
            [
                new BreakdownItem("高強度", DisplayFormat.Duration(activity.HighActivityTime), Share(activity.HighActivityTime), BreakdownItem.ToBrush(ChartTheme.IntensityHigh)),
                new BreakdownItem("中強度", DisplayFormat.Duration(activity.MediumActivityTime), Share(activity.MediumActivityTime), BreakdownItem.ToBrush(ChartTheme.IntensityMedium)),
                new BreakdownItem("低強度", DisplayFormat.Duration(activity.LowActivityTime), Share(activity.LowActivityTime), BreakdownItem.ToBrush(ChartTheme.IntensityLow)),
            ];
    }
}

/// <summary>アクティビティの表の行</summary>
public sealed record ActivityRow(
    DateOnly Day,
    string DayText,
    string Score,
    string Steps,
    string ActiveCalories,
    string TotalCalories,
    string Distance,
    string High,
    string Medium,
    string Low,
    string Sedentary) : IDayRow;

/// <summary>ワークアウトの表の行</summary>
public sealed record WorkoutRow(DateOnly Day, string DayText, string Time, string Activity, string Intensity, string Duration, string Calories, string Distance) : IDayRow
{
    /// <summary>カード表示用（カロリーと、あれば距離）</summary>
    public string Summary => Distance == DisplayFormat.NoValue ? Calories : $"{Calories}・{Distance}";

    public static WorkoutRow From(Core.Models.Workout w) => new(
        w.Day,
        DisplayFormat.DayShort(w.Day),
        $"{DisplayFormat.Time(w.StartDatetime)}〜{DisplayFormat.Time(w.EndDatetime)}",
        w.Label is { Length: > 0 } label && label != "None" ? $"{Labels.WorkoutActivity(w.Activity)}（{label}）" : Labels.WorkoutActivity(w.Activity),
        Labels.WorkoutIntensity(w.Intensity),
        DisplayFormat.Duration((int)(w.EndDatetime - w.StartDatetime).TotalSeconds),
        w.Calories is { } kcal ? $"{kcal:N0} kcal" : DisplayFormat.NoValue,
        DisplayFormat.Distance(w.Distance));
}

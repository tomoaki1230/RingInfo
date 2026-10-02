using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using RingInfo.Core.Formatting;
using RingInfo.Core.Models;

namespace RingInfo.App.Charts;

/// <summary>日ごとの値の系列（null は欠測）</summary>
/// <param name="Title">凡例・ツールチップに表示する名前</param>
/// <param name="Color">系列色</param>
/// <param name="Values">日付リストと同じ順序の値</param>
/// <param name="Format">ツールチップでの値の表示形式</param>
internal sealed record DailySeries(string Title, OxyColor Color, IReadOnlyList<double?> Values, Func<double, string> Format);

/// <summary>ツールチップ用の文字列を持つ棒</summary>
internal sealed class LabeledBarItem(double value, int categoryIndex, string text) : BarItem(value, categoryIndex)
{
    public string Text { get; } = text;
}

/// <summary>ツールチップ用の文字列を持つ点</summary>
internal sealed record LabeledPoint(DataPoint Point, string Text)
{
    public static readonly LabeledPoint Gap = new(DataPoint.Undefined, "");
}

/// <summary>ツールチップ用の文字列を持つ矩形</summary>
internal sealed class LabeledRectangleItem(double x0, double x1, double y0, double y1, string text) : RectangleBarItem(x0, y0, x1, y1)
{
    public string Text { get; } = text;
}

/// <summary>画面に表示するグラフ（OxyPlot の PlotModel）を作成する</summary>
internal static class ChartFactory
{
    private const string DayAxisKey = "days";
    private const string ValueAxisKey = "values";

    /// <summary>日ごとの折れ線グラフ（スコアの推移など）</summary>
    public static ChartData DailyLines(IReadOnlyList<DateOnly> days, IReadOnlyList<DailySeries> series, double? minimum = null, double? maximum = null)
    {
        var model = CreateModel(showLegend: series.Count > 1);
        model.Axes.Add(CreateDayAxis(days, AxisPosition.Bottom, DayAxisKey));
        model.Axes.Add(CreateValueAxis(AxisPosition.Left, ValueAxisKey, minimum, maximum));

        var showMarkers = days.Count <= 31;
        foreach (var s in series)
        {
            var points = new List<LabeledPoint>(days.Count);
            for (var i = 0; i < days.Count; i++)
            {
                points.Add(s.Values[i] is { } value
                    ? new LabeledPoint(new DataPoint(i, value), $"{DisplayFormat.DayShort(days[i])}\n{s.Title}: {s.Format(value)}")
                    : LabeledPoint.Gap); // 欠測日は線を途切れさせる
            }

            model.Series.Add(new LineSeries
            {
                Title = s.Title,
                Color = s.Color,
                StrokeThickness = 2,
                LineJoin = LineJoin.Round,
                MarkerType = showMarkers ? MarkerType.Circle : MarkerType.None,
                MarkerSize = 4,
                MarkerFill = s.Color,
                MarkerStroke = ChartTheme.Surface,
                MarkerStrokeThickness = 2,
                XAxisKey = DayAxisKey,
                YAxisKey = ValueAxisKey,
                CanTrackerInterpolatePoints = false,
                ItemsSource = points,
                Mapping = item => ((LabeledPoint)item).Point,
                TrackerFormatString = "{Text}",
            });
        }

        return new ChartData(model, IsEmpty(series));
    }

    /// <summary>日ごとの棒グラフ（積み上げ・横並びに対応）</summary>
    public static ChartData DailyBars(
        IReadOnlyList<DateOnly> days,
        IReadOnlyList<DailySeries> series,
        bool stacked = false,
        double? maximum = null,
        Func<double, string>? axisFormat = null)
    {
        var model = CreateModel(showLegend: series.Count > 1);
        var dayAxis = CreateDayAxis(days, AxisPosition.Bottom, DayAxisKey);
        dayAxis.GapWidth = days.Count > 31 ? 0.25 : 0.6;
        model.Axes.Add(dayAxis);
        var valueAxis = CreateValueAxis(AxisPosition.Left, ValueAxisKey, 0, maximum);
        if (axisFormat is not null)
        {
            valueAxis.LabelFormatter = axisFormat;
        }

        model.Axes.Add(valueAxis);

        foreach (var s in series)
        {
            var bars = new BarSeries
            {
                Title = s.Title,
                FillColor = s.Color,
                IsStacked = stacked,
                // 積み上げの区切りは面の色の隙間で表現する（棒が細くなる長い期間では付けない）
                StrokeColor = ChartTheme.Surface,
                StrokeThickness = stacked && days.Count <= 31 ? 1 : 0,
                XAxisKey = ValueAxisKey,
                YAxisKey = DayAxisKey,
                TrackerFormatString = "{Text}",
            };
            for (var i = 0; i < days.Count; i++)
            {
                if (s.Values[i] is { } value)
                {
                    bars.Items.Add(new LabeledBarItem(value, i, $"{DisplayFormat.DayShort(days[i])}\n{s.Title}: {s.Format(value)}"));
                }
            }

            model.Series.Add(bars);
        }

        return new ChartData(model, IsEmpty(series));
    }

    /// <summary>正負で色を分けた棒グラフ（体温偏差など）</summary>
    public static ChartData DivergingBars(IReadOnlyList<DateOnly> days, DailySeries series, double minimumRange)
    {
        var model = CreateModel(showLegend: false);
        var dayAxis = CreateDayAxis(days, AxisPosition.Bottom, DayAxisKey);
        dayAxis.GapWidth = days.Count > 31 ? 0.25 : 0.6;
        model.Axes.Add(dayAxis);

        var values = series.Values.Where(v => v.HasValue).Select(v => Math.Abs(v!.Value)).DefaultIfEmpty(0).ToList();
        var range = Math.Max(minimumRange, values.Max() * 1.15);
        var valueAxis = CreateValueAxis(AxisPosition.Left, ValueAxisKey, -range, range);
        valueAxis.LabelFormatter = v => v.ToString("+0.0;-0.0;0");
        model.Axes.Add(valueAxis);

        // 0 の基準線
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = 0,
            Color = ChartTheme.TextMuted,
            StrokeThickness = 1,
            LineStyle = LineStyle.Solid,
            XAxisKey = DayAxisKey,
            YAxisKey = ValueAxisKey,
        });

        var bars = new BarSeries
        {
            Title = series.Title,
            FillColor = series.Color,
            NegativeFillColor = ChartTheme.DivergingNegative,
            StrokeThickness = 0,
            XAxisKey = ValueAxisKey,
            YAxisKey = DayAxisKey,
            TrackerFormatString = "{Text}",
        };
        for (var i = 0; i < days.Count; i++)
        {
            if (series.Values[i] is { } value)
            {
                bars.Items.Add(new LabeledBarItem(value, i, $"{DisplayFormat.DayShort(days[i])}\n{series.Title}: {series.Format(value)}"));
            }
        }

        model.Series.Add(bars);
        return new ChartData(model, IsEmpty([series]));
    }

    /// <summary>睡眠ステージの推移（ヒプノグラム）</summary>
    public static ChartData Hypnogram(SleepPeriod? period)
    {
        var model = CreateModel(showLegend: false);
        if (period is null || string.IsNullOrEmpty(period.SleepPhase5Min))
        {
            return new ChartData(model, true);
        }

        var start = period.BedtimeStart.DateTime;
        model.Axes.Add(CreateTimeAxis(start, start.AddSeconds(period.SleepPhase5Min.Length * 300)));

        // 上から 覚醒 / レム / 浅い / 深い
        var stages = new (char Code, string Name, double Level, OxyColor Color)[]
        {
            ('4', "覚醒", 3, ChartTheme.StageAwake),
            ('3', "レム睡眠", 2, ChartTheme.StageRem),
            ('2', "浅い睡眠", 1, ChartTheme.StageLight),
            ('1', "深い睡眠", 0, ChartTheme.StageDeep),
        };
        var levelAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -0.5,
            Maximum = 3.5,
            MajorStep = 1,
            MinorStep = 1,
            IsZoomEnabled = false,
            IsPanEnabled = false,
            TextColor = ChartTheme.TextSecondary,
            TicklineColor = OxyColors.Transparent,
            AxislineStyle = LineStyle.None,
            MajorGridlineStyle = LineStyle.None,
            LabelFormatter = v => stages.FirstOrDefault(s => Math.Abs(s.Level - v) < 0.01).Name ?? "",
        };
        model.Axes.Add(levelAxis);

        var series = stages.ToDictionary(
            s => s.Code,
            s => new RectangleBarSeries { Title = s.Name, FillColor = s.Color, StrokeThickness = 0, TrackerFormatString = "{Text}" });

        // 同じステージが続く区間をまとめて 1 つの矩形にする
        var phases = period.SleepPhase5Min;
        var index = 0;
        while (index < phases.Length)
        {
            var code = phases[index];
            var end = index;
            while (end < phases.Length && phases[end] == code)
            {
                end++;
            }

            var stage = stages.FirstOrDefault(s => s.Code == code);
            if (stage.Name is not null)
            {
                var t0 = start.AddSeconds(index * 300);
                var t1 = start.AddSeconds(end * 300);
                var text = $"{stage.Name}\n{t0:HH:mm}〜{t1:HH:mm}（{DisplayFormat.Duration((end - index) * 300)}）";
                series[code].Items.Add(new LabeledRectangleItem(
                    DateTimeAxis.ToDouble(t0), DateTimeAxis.ToDouble(t1), stage.Level - 0.38, stage.Level + 0.38, text));
            }

            index = end;
        }

        foreach (var s in series.Values)
        {
            model.Series.Add(s);
        }

        return new ChartData(model, false);
    }

    /// <summary>睡眠中の心拍数（または HRV）の推移</summary>
    public static ChartData SleepSamples(SleepPeriod? period, SampleSeries? samples, string title, string unit, OxyColor color)
    {
        var model = CreateModel(showLegend: false);
        if (period is null || samples is null || samples.Items.Count == 0 || samples.Interval <= 0)
        {
            return new ChartData(model, true);
        }

        // サンプルの時刻は就寝時刻のオフセット（記録地の時刻）に合わせて表示する
        var start = samples.Timestamp.ToOffset(period.BedtimeStart.Offset).DateTime;
        var end = start.AddSeconds(samples.Interval * samples.Items.Count);
        model.Axes.Add(CreateTimeAxis(start, end));
        model.Axes.Add(CreateValueAxis(AxisPosition.Left, ValueAxisKey, null, null));

        var line = new LineSeries
        {
            Title = title,
            Color = color,
            StrokeThickness = 2,
            LineJoin = LineJoin.Round,
            YAxisKey = ValueAxisKey,
            CanTrackerInterpolatePoints = false,
            TrackerFormatString = title + "\n{2:HH:mm}: {4:0} " + unit,
        };
        for (var i = 0; i < samples.Items.Count; i++)
        {
            var time = start.AddSeconds(samples.Interval * i);
            line.Points.Add(samples.Items[i] is { } value ? new DataPoint(DateTimeAxis.ToDouble(time), value) : DataPoint.Undefined);
        }

        model.Series.Add(line);
        return new ChartData(model, line.Points.All(p => !p.IsDefined()), ChartTheme.HoverController);
    }

    /// <summary>1 日の心拍数（計測の種類ごとに色分け）</summary>
    public static ChartData HeartRateTimeline(IReadOnlyList<HeartRateSample> samples, DateTime dayStart, TimeSpan offset)
    {
        var model = CreateModel(showLegend: true);
        model.Axes.Add(CreateTimeAxis(dayStart, dayStart.AddDays(1), majorHours: 3));
        model.Axes.Add(CreateValueAxis(AxisPosition.Left, ValueAxisKey, null, null));

        // 種類: 日中 / ワークアウト / 睡眠（それ以外は日中にまとめる）
        var groups = new (string Name, OxyColor Color, Func<string?, bool> Match)[]
        {
            ("日中", ChartTheme.Series1, s => s is not (HeartRateSample.SourceSleep or HeartRateSample.SourceWorkout)),
            ("ワークアウト", ChartTheme.Series2, s => s == HeartRateSample.SourceWorkout),
            ("睡眠", ChartTheme.Series3, s => s == HeartRateSample.SourceSleep),
        };

        foreach (var group in groups)
        {
            var line = new LineSeries
            {
                Title = group.Name,
                Color = group.Color,
                StrokeThickness = 1.5,
                LineJoin = LineJoin.Round,
                YAxisKey = ValueAxisKey,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = group.Name + "\n{2:HH:mm}: {4:0} bpm",
            };

            DateTime? previous = null;
            foreach (var sample in samples.Where(s => group.Match(s.Source)))
            {
                var time = sample.Timestamp.ToOffset(offset).DateTime;

                // 16 分以上の空白は線をつながない
                if (previous is { } p && (time - p).TotalMinutes > 16)
                {
                    line.Points.Add(DataPoint.Undefined);
                }

                line.Points.Add(new DataPoint(DateTimeAxis.ToDouble(time), sample.Bpm));
                previous = time;
            }

            if (line.Points.Count > 0)
            {
                model.Series.Add(line);
            }
        }

        return new ChartData(model, samples.Count == 0);
    }

    // ---------------------------------------------------------------- 共通部品

    private static PlotModel CreateModel(bool showLegend)
    {
        var model = new PlotModel
        {
            Background = OxyColors.Transparent,
            PlotAreaBorderThickness = new OxyThickness(0),
            PlotAreaBorderColor = OxyColors.Transparent,
            TextColor = ChartTheme.TextSecondary,
            DefaultFont = "Segoe UI",
            DefaultFontSize = 12,
            Padding = new OxyThickness(0, 4, 8, 0),
            IsLegendVisible = showLegend,
        };

        if (showLegend)
        {
            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.TopLeft,
                LegendPlacement = LegendPlacement.Outside,
                LegendOrientation = LegendOrientation.Horizontal,
                LegendTextColor = ChartTheme.TextSecondary,
                LegendFontSize = 12,
                LegendItemSpacing = 16,
                LegendPadding = 0,
                LegendMargin = 4,
                LegendSymbolLength = 14,
            });
        }

        return model;
    }

    private static CategoryAxis CreateDayAxis(IReadOnlyList<DateOnly> days, AxisPosition position, string key)
    {
        var axis = new CategoryAxis
        {
            Position = position,
            Key = key,
            TextColor = ChartTheme.TextMuted,
            AxislineStyle = LineStyle.Solid,
            AxislineColor = ChartTheme.Axis,
            TicklineColor = ChartTheme.Axis,
            MajorTickSize = 0,
            MinorTickSize = 0,
            IsZoomEnabled = false,
            IsPanEnabled = false,
            // 日数が多い場合はラベルを間引く
            MajorStep = Math.Max(1, (int)Math.Ceiling(days.Count / 12.0)),
        };
        axis.Labels.AddRange(days.Select(DisplayFormat.DayAxis));
        return axis;
    }

    private static LinearAxis CreateValueAxis(AxisPosition position, string key, double? minimum, double? maximum)
    {
        var axis = new LinearAxis
        {
            Position = position,
            Key = key,
            TextColor = ChartTheme.TextMuted,
            AxislineStyle = LineStyle.None,
            TicklineColor = OxyColors.Transparent,
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = ChartTheme.Gridline,
            MinorGridlineStyle = LineStyle.None,
            MinimumPadding = 0.05,
            MaximumPadding = 0.1,
            IsZoomEnabled = false,
            IsPanEnabled = false,
        };
        if (minimum is { } min)
        {
            axis.Minimum = min;
        }

        if (maximum is { } max)
        {
            axis.Maximum = max;
        }

        return axis;
    }

    private static DateTimeAxis CreateTimeAxis(DateTime start, DateTime end, int? majorHours = null)
    {
        var axis = new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = DateTimeAxis.ToDouble(start),
            Maximum = DateTimeAxis.ToDouble(end),
            StringFormat = "HH:mm",
            TextColor = ChartTheme.TextMuted,
            AxislineStyle = LineStyle.Solid,
            AxislineColor = ChartTheme.Axis,
            TicklineColor = ChartTheme.Axis,
            MajorGridlineStyle = LineStyle.None,
            IntervalType = DateTimeIntervalType.Hours,
            MinorIntervalType = DateTimeIntervalType.Hours,
            IsZoomEnabled = false,
            IsPanEnabled = false,
            AbsoluteMinimum = DateTimeAxis.ToDouble(start),
            AbsoluteMaximum = DateTimeAxis.ToDouble(end),
        };
        if (majorHours is { } hours)
        {
            axis.MajorStep = hours / 24.0;
            axis.MinorStep = 1 / 24.0;
        }

        return axis;
    }

    private static bool IsEmpty(IEnumerable<DailySeries> series) => series.All(s => s.Values.All(v => v is null));
}

/// <summary>グラフの表示データ</summary>
/// <param name="Model">OxyPlot のモデル（1 つの PlotView にのみ割り当てる）</param>
/// <param name="IsEmpty">表示するデータが無いか</param>
/// <param name="Controller">マウス操作の割り当て</param>
public sealed record ChartData(PlotModel Model, bool IsEmpty, IPlotController? Controller = null)
{
    public IPlotController ActualController => Controller ?? ChartTheme.HoverController;
}

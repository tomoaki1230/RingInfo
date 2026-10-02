using System.Globalization;
using OxyPlot;
using OxyPlot.Series;
using RingInfo.App.Charts;
using RingInfo.Core.Models;

namespace RingInfo.App.Tests;

public class ChartFactoryTests
{
    private static readonly DateOnly[] Days = [new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1)];

    private static DailySeries Series(string title, params double?[] values) => new(title, ChartTheme.Series1, values, v => $"{v:0.#}");

    [Fact]
    public void 折れ線は欠測日で途切れ_ツールチップ用の文字列を持つ()
    {
        var chart = ChartFactory.DailyLines(Days, [Series("睡眠", 80, null, 90)]);

        var line = Assert.IsType<LineSeries>(Assert.Single(chart.Model.Series));
        var points = line.ItemsSource.Cast<LabeledPoint>().ToList();
        Assert.Equal(3, points.Count);
        Assert.False(points[1].Point.IsDefined());
        Assert.Equal("10/1(木)\n睡眠: 90", points[2].Text);
        Assert.Equal("{Text}", line.TrackerFormatString);
        Assert.False(chart.IsEmpty);
        Assert.False(chart.Model.IsLegendVisible);
    }

    [Fact]
    public void 全て欠測ならデータなしになる()
    {
        var chart = ChartFactory.DailyLines(Days, [Series("睡眠", null, null, null)]);

        Assert.True(chart.IsEmpty);
    }

    [Fact]
    public void 棒グラフは値のある日だけ棒を作り_積み上げに対応する()
    {
        var chart = ChartFactory.DailyBars(Days, [Series("深い", 1, null, 1.5), Series("浅い", 3, 4, null)], stacked: true);

        var bars = chart.Model.Series.OfType<BarSeries>().ToList();
        Assert.Equal(2, bars.Count);
        Assert.All(bars, b => Assert.True(b.IsStacked));
        Assert.Equal([0, 2], bars[0].Items.Select(i => i.CategoryIndex));
        Assert.Equal("9/29(火)\n深い: 1", ((LabeledBarItem)bars[0].Items[0]).Text);
        Assert.True(chart.Model.IsLegendVisible);
    }

    [Fact]
    public void ツールチップの書式で項目の文字列を参照できる()
    {
        var text = StringHelper.Format(CultureInfo.InvariantCulture, "{Text}", new LabeledBarItem(1, 0, "表示内容"));

        Assert.Equal("表示内容", text);
    }

    [Fact]
    public void ヒプノグラムは同じステージが続く区間を1つにまとめる()
    {
        var period = new SleepPeriod
        {
            BedtimeStart = new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.FromHours(9)),
            SleepPhase5Min = "4422211334",
        };

        var chart = ChartFactory.Hypnogram(period);

        var items = chart.Model.Series.OfType<RectangleBarSeries>().SelectMany(s => s.Items).OfType<LabeledRectangleItem>().ToList();
        Assert.Equal(5, items.Count);
        Assert.Contains(items, i => i.Text.StartsWith("浅い睡眠\n23:10〜23:25"));
        Assert.False(chart.IsEmpty);
    }

    [Fact]
    public void ステージ情報が無ければヒプノグラムはデータなし()
    {
        Assert.True(ChartFactory.Hypnogram(null).IsEmpty);
        Assert.True(ChartFactory.Hypnogram(new SleepPeriod()).IsEmpty);
    }

    [Fact]
    public void 心拍数は種類ごとの系列に分かれ_16分以上の空白で途切れる()
    {
        var start = new DateTime(2026, 10, 1);
        var offset = TimeSpan.FromHours(9);
        HeartRateSample Sample(int minutes, string source) => new()
        {
            Timestamp = new DateTimeOffset(start.AddMinutes(minutes), offset),
            Bpm = 60,
            Source = source,
        };
        var samples = new[]
        {
            Sample(0, "sleep"), Sample(5, "sleep"),
            Sample(600, "awake"), Sample(605, "awake"), Sample(700, "awake"),
            Sample(1100, "workout"),
        };

        var chart = ChartFactory.HeartRateTimeline(samples, start, offset);

        var lines = chart.Model.Series.OfType<LineSeries>().ToDictionary(s => s.Title);
        Assert.Equal(["日中", "ワークアウト", "睡眠"], lines.Keys);
        Assert.Equal(4, lines["日中"].Points.Count);
        Assert.False(lines["日中"].Points[2].IsDefined());
        Assert.Same(ChartTheme.HoverController, chart.ActualController);
    }
}

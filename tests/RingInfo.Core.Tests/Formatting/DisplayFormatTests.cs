using RingInfo.Core.Formatting;
using RingInfo.Core.Services;

namespace RingInfo.Core.Tests.Formatting;

public class DisplayFormatTests
{
    [Theory]
    [InlineData(27120, "7時間32分")]
    [InlineData(1500, "25分")]
    [InlineData(3600, "1時間0分")]
    [InlineData(null, "—")]
    public void 時間を日本語で表示する(int? seconds, string expected)
        => Assert.Equal(expected, DisplayFormat.Duration(seconds));

    [Theory]
    [InlineData(27120, "7:32")]
    [InlineData(300, "0:05")]
    public void 時間を短い形式で表示する(int seconds, string expected)
        => Assert.Equal(expected, DisplayFormat.DurationShort(seconds));

    [Theory]
    [InlineData(0.34, "+0.3℃")]
    [InlineData(-0.26, "-0.3℃")]
    [InlineData(0.0, "±0.0℃")]
    [InlineData(null, "—")]
    public void 体温偏差を符号付きで表示する(double? value, string expected)
        => Assert.Equal(expected, DisplayFormat.Temperature(value));

    [Fact]
    public void 数値と距離と日付を表示する()
    {
        Assert.Equal("12,345", DisplayFormat.Number(12345));
        Assert.Equal("5.2 km", DisplayFormat.Distance(5234));
        Assert.Equal("850 m", DisplayFormat.Distance(850));
        Assert.Equal("10/1(木)", DisplayFormat.DayShort(new DateOnly(2026, 10, 1)));
        Assert.Equal("2026/10/01(木)", DisplayFormat.DayLong(new DateOnly(2026, 10, 1)));
    }

    [Theory]
    [InlineData(92, ScoreLevel.Optimal, "最適")]
    [InlineData(85, ScoreLevel.Optimal, "最適")]
    [InlineData(84, ScoreLevel.Good, "良好")]
    [InlineData(65, ScoreLevel.Fair, "まずまず")]
    [InlineData(40, ScoreLevel.PayAttention, "要注意")]
    [InlineData(null, ScoreLevel.None, "データなし")]
    public void スコアの評価区分(int? score, ScoreLevel level, string label)
    {
        Assert.Equal(level, ScoreRating.GetLevel(score));
        Assert.Equal(label, ScoreRating.GetLabel(score));
    }
}

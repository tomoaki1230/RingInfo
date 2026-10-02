using System.Windows.Media;
using OxyPlot;
using RingInfo.Core.Formatting;
using RingInfo.Core.Services;

namespace RingInfo.App.ViewModels;

/// <summary>数値のタイル表示（ラベル・値・単位・補足）</summary>
public sealed record MetricTile(string Label, string Value, string? Unit = null, string? Note = null)
{
    /// <summary>値の後ろに付ける単位（値が無い「—」のときは出さない）</summary>
    public string UnitText => string.IsNullOrEmpty(Unit) || Value == DisplayFormat.NoValue ? "" : " " + Unit;
}

/// <summary>ラベルと値の 1 行</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>スコアの寄与要素（0〜100 のバー）</summary>
public sealed record ContributorItem(string Label, int? Value)
{
    public string ValueText => DisplayFormat.Score(Value);

    public int BarValue => Value ?? 0;

    public ScoreLevel Level => ScoreRating.GetLevel(Value);
}

/// <summary>スコアカード（円形ゲージ付き）</summary>
public sealed record ScoreCard(string Title, int? Score, string DayText, string Caption, string AverageText)
{
    public string ScoreText => DisplayFormat.Score(Score);

    public ScoreLevel Level => ScoreRating.GetLevel(Score);

    public string LevelLabel => ScoreRating.GetLabel(Score);

    public static ScoreCard Empty(string title) => new(title, null, DisplayFormat.NoValue, "", "");
}

/// <summary>色見本付きの内訳（睡眠ステージなど）。凡例を兼ねる。</summary>
public sealed record BreakdownItem(string Name, string Value, string Share, Brush Swatch)
{
    public static Brush ToBrush(OxyColor color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}

/// <summary>表示期間の選択肢</summary>
public sealed record RangeOption(int Days, string Label);

/// <summary>日別の表の行</summary>
public interface IDayRow
{
    DateOnly Day { get; }
}

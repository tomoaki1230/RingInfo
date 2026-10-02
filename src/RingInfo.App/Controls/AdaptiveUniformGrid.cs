using System.Windows;
using System.Windows.Controls;

namespace RingInfo.App.Controls;

/// <summary>
/// 幅に応じて列数を自動で決める均等グリッド。
/// 1 マスの幅が MinItemWidth を下回らない範囲で、最大 MaxColumns 列まで並べる（狭いウィンドウでタイルの値が切れないようにする）。
/// </summary>
public sealed class AdaptiveUniformGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveUniformGrid),
        new FrameworkPropertyMetadata(170.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveUniformGrid),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>幅と項目数から列数を求める</summary>
    internal static int GetColumnCount(double width, double minItemWidth, int maxColumns, int itemCount)
    {
        if (itemCount == 0)
        {
            return 1;
        }

        var byWidth = double.IsInfinity(width) || minItemWidth <= 0 ? maxColumns : (int)Math.Floor(width / minItemWidth);
        return Math.Clamp(Math.Min(byWidth, itemCount), 1, Math.Max(1, maxColumns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = InternalChildren.Count;
        var columns = GetColumnCount(availableSize.Width, MinItemWidth, MaxColumns, count);
        var cellWidth = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width / columns;

        var rowHeight = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var rows = (int)Math.Ceiling(count / (double)columns);
        return new Size(cellWidth * columns, rowHeight * rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        var columns = GetColumnCount(finalSize.Width, MinItemWidth, MaxColumns, count);
        var cellWidth = finalSize.Width / columns;
        var rowHeight = InternalChildren.Cast<UIElement>().Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max();

        for (var i = 0; i < count; i++)
        {
            InternalChildren[i].Arrange(new Rect((i % columns) * cellWidth, (i / columns) * rowHeight, cellWidth, rowHeight));
        }

        return finalSize;
    }
}

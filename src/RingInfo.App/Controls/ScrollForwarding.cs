using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RingInfo.App.Controls;

/// <summary>
/// 表の中でマウスホイールを回したときの動き。
/// 表がまだスクロールできる間は表をスクロールし、表の端まで来たら（またはスクロール不要なら）ページをスクロールする。
/// </summary>
public static class ScrollForwarding
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ScrollForwarding), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    /// <summary>表がその方向にまだスクロールできるか</summary>
    internal static bool CanScroll(double verticalOffset, double scrollableHeight, int delta)
        => delta > 0 ? verticalOffset > 0 : verticalOffset < scrollableHeight;

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement element || VisualTreeHelper.GetParent(element) is not UIElement parent)
        {
            return;
        }

        if (FindScrollViewer(element) is { } inner && CanScroll(inner.VerticalOffset, inner.ScrollableHeight, e.Delta))
        {
            // 表自身をスクロールさせる
            return;
        }

        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RingInfo.Core.Services;

namespace RingInfo.App.Converters;

/// <summary>bool → Visibility（ConverterParameter="Invert" で反転）</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is true;
        if (parameter as string == "Invert")
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>null または空文字 → Collapsed</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null || (value is string s && string.IsNullOrWhiteSpace(s)) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>スコアの評価区分 → 状態色（良好=緑 / まずまず=黄 / 要注意=橙）</summary>
public sealed class ScoreLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ScoreLevel.Optimal or ScoreLevel.Good => "StatusGoodBrush",
            ScoreLevel.Fair => "StatusWarningBrush",
            ScoreLevel.PayAttention => "StatusSeriousBrush",
            _ => "TextMutedBrush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

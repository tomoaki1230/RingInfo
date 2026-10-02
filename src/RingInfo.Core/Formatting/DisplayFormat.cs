using System.Globalization;

namespace RingInfo.Core.Formatting;

/// <summary>画面表示用の書式（日本語）</summary>
public static class DisplayFormat
{
    public const string NoValue = "—";

    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>秒数を「7時間32分」形式にする</summary>
    public static string Duration(int? seconds)
    {
        if (seconds is null)
        {
            return NoValue;
        }

        var totalMinutes = (int)Math.Round(seconds.Value / 60.0);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0 ? $"{hours}時間{minutes}分" : $"{minutes}分";
    }

    /// <summary>秒数を「7:32」形式にする（表用）</summary>
    public static string DurationShort(int? seconds)
    {
        if (seconds is null)
        {
            return NoValue;
        }

        var totalMinutes = (int)Math.Round(seconds.Value / 60.0);
        return $"{totalMinutes / 60}:{totalMinutes % 60:00}";
    }

    public static string Number(int? value) => value?.ToString("N0", Japanese) ?? NoValue;

    public static string Number(double? value, int decimals) => value?.ToString("N" + decimals, Japanese) ?? NoValue;

    public static string Score(int? score) => score?.ToString(CultureInfo.InvariantCulture) ?? NoValue;

    /// <summary>体温偏差を「+0.3℃」形式にする</summary>
    public static string Temperature(double? deviation)
        => deviation is null ? NoValue : deviation.Value.ToString("+0.0;-0.0;±0.0", CultureInfo.InvariantCulture) + "℃";

    public static string Percent(double? value, int decimals = 0)
        => value is null ? NoValue : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture) + "%";

    /// <summary>メートルを「5.2 km」形式にする</summary>
    public static string Distance(double? meters)
    {
        if (meters is null)
        {
            return NoValue;
        }

        return meters.Value >= 1000
            ? (meters.Value / 1000).ToString("F1", CultureInfo.InvariantCulture) + " km"
            : Math.Round(meters.Value).ToString(CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>「10/1(水)」形式</summary>
    public static string DayShort(DateOnly day) => day.ToString("M/d(ddd)", Japanese);

    /// <summary>「2026/10/01(水)」形式</summary>
    public static string DayLong(DateOnly day) => day.ToString("yyyy/MM/dd(ddd)", Japanese);

    /// <summary>グラフ軸用「10/1」形式</summary>
    public static string DayAxis(DateOnly day) => day.ToString("M/d", CultureInfo.InvariantCulture);

    /// <summary>時刻を「23:45」形式にする（記録時のオフセットのまま表示）</summary>
    public static string Time(DateTimeOffset? value) => value?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? NoValue;
}

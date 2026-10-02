using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using RingInfo.App.Charts;
using RingInfo.Core.Settings;

namespace RingInfo.App.Services;

/// <summary>実際に表示しているテーマ</summary>
public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>
/// 配色の切り替え。設定で「システムに合わせる」を選んでいる場合は、
/// Windows の「アプリ モード（ライト／ダーク）」に合わせ、変更にも自動で追従する。
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool _listening;

    /// <summary>
    /// 現在のテーマの色。ブラシの Color はこのパレットにバインドしている。
    /// （WPF はリソースのブラシを読み取り専用にするため、直接は書き換えられない。バインドしたブラシは読み取り専用にならない）
    /// </summary>
    private static readonly LivePalette Palette = new();

    private static bool _brushesCreated;

    /// <summary>テーマごとの色（Colors.*.xaml から 1 回だけ読み込む）</summary>
    private static readonly Dictionary<AppTheme, Dictionary<string, Color>> Palettes = new();

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static ThemePreference Preference { get; private set; } = ThemePreference.System;

    /// <summary>テーマが変わったとき（UI スレッドで呼ばれる）</summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>Windows の設定（AppsUseLightTheme）からテーマを判定する。読めない場合はライト。</summary>
    public static AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return AppTheme.Light;
        }
    }

    /// <summary>テーマの選択を適用する（「システムに合わせる」なら以後の Windows の変更にも追従）</summary>
    public static void SetPreference(ThemePreference preference)
    {
        Preference = preference;
        Apply(preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => DetectSystemTheme(),
        });

        if (!_listening && Application.Current is { } app)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            app.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }

    /// <summary>
    /// テーマの色を適用する。
    /// ブラシ自体は差し替えず色だけを書き換えるため、画面の要素がリソースを探し直す処理が起きず高速に切り替わる。
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        var palette = GetPalette(theme);
        Palette.Set(palette);
        if (!_brushesCreated)
        {
            // 初回: パレットにバインドしたブラシを作り、先頭の色辞書と差し替える
            _brushesCreated = true;
            var live = new ResourceDictionary();
            foreach (var key in palette.Keys)
            {
                var brush = new SolidColorBrush();
                BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding($"[{key}]") { Source = Palette });
                live[key] = brush;
            }

            var dictionaries = Application.Current.Resources.MergedDictionaries;
            if (dictionaries.Count > 0)
            {
                dictionaries[0] = live;
            }
            else
            {
                dictionaries.Add(live);
            }
        }

        ChartTheme.IsDark = theme == AppTheme.Dark;
        var changed = Current != theme;
        Current = theme;
        if (changed)
        {
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static Dictionary<string, Color> GetPalette(AppTheme theme)
    {
        if (!Palettes.TryGetValue(theme, out var palette))
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"/RingInfo;component/Themes/Colors.{theme}.xaml", UriKind.Relative),
            };
            palette = dictionary.Keys.OfType<string>()
                .Where(key => dictionary[key] is SolidColorBrush)
                .ToDictionary(key => key, key => ((SolidColorBrush)dictionary[key]).Color);
            Palettes[theme] = palette;
        }

        return palette;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Preference != ThemePreference.System
            || e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        // SystemEvents は別スレッドから通知されるため UI スレッドで切り替える
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (Preference == ThemePreference.System)
            {
                var theme = DetectSystemTheme();
                if (theme != Current)
                {
                    Apply(theme);
                }
            }
        });
    }
}

/// <summary>テーマの色の一覧（インデクサー経由でブラシにバインドする）</summary>
internal sealed class LivePalette : INotifyPropertyChanged
{
    private Dictionary<string, Color> _colors = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public Color this[string key] => _colors.TryGetValue(key, out var color) ? color : Colors.Transparent;

    public void Set(Dictionary<string, Color> colors)
    {
        _colors = colors;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }
}

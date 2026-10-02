using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RingInfo.App.Services;

namespace RingInfo.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UpdateTitleBar();
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;

        // 起動したまま日付が変わった場合に、アプリに戻ったタイミングで新しい日に合わせる
        Activated += (_, _) => (DataContext as ViewModels.MainViewModel)?.OnWindowActivated();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => UpdateTitleBar();

    /// <summary>タイトルバーの色をテーマに合わせる（Windows 10 20H1 以降）</summary>
    private void UpdateTitleBar()
    {
        const int DwmwaUseImmersiveDarkMode = 20;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = ThemeManager.Current == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

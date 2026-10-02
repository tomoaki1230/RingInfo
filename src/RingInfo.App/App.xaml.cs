using System.Windows;
using System.Windows.Threading;
using RingInfo.App.Services;
using RingInfo.App.ViewModels;
using RingInfo.Core.Settings;

namespace RingInfo.App;

public partial class App : Application
{
    /// <summary>予期しないエラーの記録先: %LOCALAPPDATA%\RingInfo\error.log</summary>
    public static string ErrorLogPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RingInfo", "error.log");

    /// <summary>スクリーンショットモード（エラー時にダイアログを出さない）</summary>
    private static bool _headless;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 画面確認用: RingInfo.exe --screenshot <出力フォルダー> [--days 7|14|30|90] [--theme light|dark] [--size 幅x高さ] [--empty]
        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        if (screenshotIndex >= 0)
        {
            _headless = true;
            var directory = e.Args.ElementAtOrDefault(screenshotIndex + 1) ?? "screenshots";
            var daysIndex = Array.IndexOf(e.Args, "--days");
            var days = daysIndex >= 0 && int.TryParse(e.Args.ElementAtOrDefault(daysIndex + 1), out var value) ? value : AppSettings.DefaultRangeDays;
            var themeIndex = Array.IndexOf(e.Args, "--theme");
            ThemeManager.SetPreference(themeIndex >= 0 && Enum.TryParse<ThemePreference>(e.Args.ElementAtOrDefault(themeIndex + 1), ignoreCase: true, out var theme)
                ? theme
                : ThemePreference.System);
            var sizeIndex = Array.IndexOf(e.Args, "--size");
            var size = sizeIndex >= 0 ? e.Args.ElementAtOrDefault(sizeIndex + 1)?.Split('x') : null;
            var width = size is { Length: 2 } && double.TryParse(size[0], out var w) ? w : 1400;
            var height = size is { Length: 2 } && double.TryParse(size[1], out var h) ? h : 900;
            var empty = e.Args.Contains("--empty");
            ScreenshotRunner.MeasureOnly = e.Args.Contains("--measure-only");
            ScreenshotRunner.ShowConnected = e.Args.Contains("--connected");
            try
            {
                await ScreenshotRunner.RunAsync(directory, days, width, height, empty);
            }
            finally
            {
                Shutdown();
            }

            return;
        }

        var store = new SettingsStore(SettingsStore.DefaultFilePath, new DpapiSecretProtector());
        var settings = store.Load();

        // 設定のテーマ（既定は Windows のライト／ダーク設定に追従）
        ThemeManager.SetPreference(settings.Theme);

        var viewModel = new MainViewModel(store, settings, builtInClient: BuiltInClient.Load(), applyTheme: ThemeManager.SetPreference);
        ThemeManager.ThemeChanged += (_, _) => viewModel.RebuildCharts();
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();

        if (store.LastLoadWarning is { } warning)
        {
            viewModel.WarningMessage = warning;
        }

        await viewModel.InitializeAsync();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteErrorLog(e.Exception);
        e.Handled = true;
        if (_headless)
        {
            Current.Shutdown(1);
            return;
        }

        MessageBox.Show(
            $"予期しないエラーが発生しました。\n\n{e.Exception.Message}\n\n詳細: {ErrorLogPath}",
            "RingInfo",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void WriteErrorLog(Exception exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ErrorLogPath)!);
            System.IO.File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // ログを書けなくてもアプリは続行する
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RingInfo.App.ViewModels;
using RingInfo.Core.Settings;

namespace RingInfo.App.Services;

/// <summary>
/// デモデータで全ページを描画して PNG に保存する（README 用の画像作成・表示確認用）。
/// ウィンドウは画面外に配置するため、操作中の画面には表示されない。
/// </summary>
internal static class ScreenshotRunner
{
    /// <param name="empty">データが 1 件も無い状態で描画する</param>
    public static async Task RunAsync(string outputDirectory, int rangeDays, double width = 1400, double height = 900, bool empty = false)
    {
        Directory.CreateDirectory(outputDirectory);

        var viewModel = new MainViewModel(
            settingsStore: null,
            ShowConnected
                ? new AppSettings
                {
                    RangeDays = rangeDays,
                    ClientId = "screenshot-client",
                    ClientSecret = "screenshot-secret",
                    TokenClientId = "screenshot-client",
                    Token = new RingInfo.Core.Auth.OAuthToken("screenshot", "screenshot", DateTimeOffset.Now.AddDays(30), null),
                }
                : new AppSettings { RangeDays = rangeDays },
            forceDemo: true,
            BuiltInClient.Load(),
            demoSource: empty ? new EmptyDataSource() : null);
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        Application.Current.MainWindow = window;
        window.Show();

        await viewModel.InitializeAsync();

        var index = 1;
        foreach (var page in viewModel.Pages)
        {
            viewModel.SelectedPage = page;
            if (page == viewModel.SettingsPage)
            {
                // 詳細設定も開いた状態で確認する
                viewModel.SettingsPage.IsAdvancedVisible = true;
            }

            await page.OnActivatedAsync();
            await WaitForRenderAsync(window);

            var name = $"{index:00}_{page.GetType().Name.Replace("PageViewModel", "").ToLowerInvariant()}";
            if (window.Content is FrameworkElement root)
            {
                Save(root, Path.Combine(outputDirectory, name + "_window.png"));
            }

            if (FindPageContent(window, page) is { } content)
            {
                Save(content, Path.Combine(outputDirectory, name + "_full.png"));
            }

            // ヘルプページではライセンスの全文を開いた状態も保存する
            if (page == viewModel.Help)
            {
                viewModel.Help.ShowLicenseCommand.Execute(viewModel.Help.Libraries[1]);
                await WaitForRenderAsync(window);
                if (FindPageContent(window, page) is { } helpContent)
                {
                    Save(helpContent, Path.Combine(outputDirectory, name + "_license.png"));
                }

                viewModel.Help.CloseLicenseCommand.Execute(null);
            }

            // 睡眠ページではカレンダーを開いた状態も保存する
            if (page == viewModel.Sleep && FindVisual<Controls.DayNavigator>(window, n => n.IsVisible) is { } navigator)
            {
                navigator.OpenCalendar();
                await WaitForRenderAsync(window);
                Save(navigator.CalendarElement, Path.Combine(outputDirectory, name + "_calendar.png"));
                navigator.CalendarElement.MoveMonth(-1);
                await WaitForRenderAsync(window);
                Save(navigator.CalendarElement, Path.Combine(outputDirectory, name + "_calendar_prev.png"));
            }

            index++;
        }

        // 読み込み中の表示（期間を切り替えた直後）
        viewModel.SelectedPage = viewModel.Dashboard;
        viewModel.SelectedRange = viewModel.RangeOptions[^1];
        await Task.Delay(150);
        if (window.Content is FrameworkElement busyRoot)
        {
            Save(busyRoot, Path.Combine(outputDirectory, "loading_window.png"));
        }

        await Task.Delay(1500);

        await MeasureThemeSwitchAsync(window, viewModel, outputDirectory);
        await MeasureUpdateAsync(window, viewModel, outputDirectory);
        window.Close();
    }

    /// <summary>データ反映（ページの更新と画面の描画）にかかる時間を計測して update_timing.txt に書く（性能確認用）</summary>
    private static async Task MeasureUpdateAsync(Window window, MainViewModel viewModel, string outputDirectory)
    {
        var lines = new List<string>();
        foreach (var page in new PageViewModel[] { viewModel.Dashboard, viewModel.Sleep, viewModel.Activity, viewModel.Condition })
        {
            viewModel.SelectedPage = page;
            await WaitForRenderAsync(window);
            // 描画の間隔の最大値 = 画面（進捗表示のアニメーションを含む）が止まっていた最長時間
            var frameWatch = System.Diagnostics.Stopwatch.StartNew();
            long lastFrame = 0, maxGap = 0;
            EventHandler onRendering = (_, _) =>
            {
                var now = frameWatch.ElapsedMilliseconds;
                maxGap = Math.Max(maxGap, now - lastFrame);
                lastFrame = now;
            };
            System.Windows.Media.CompositionTarget.Rendering += onRendering;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await viewModel.RefreshCommand.ExecuteAsync(null);
            var loaded = watch.ElapsedMilliseconds;
            window.UpdateLayout();
            var layout = watch.ElapsedMilliseconds;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);
            System.Windows.Media.CompositionTarget.Rendering -= onRendering;
            lines.Add($"{page.Title} 表示中に更新: 取得〜全ページ反映 {loaded} ms / レイアウト完了 {layout} ms / 描画完了まで {watch.ElapsedMilliseconds} ms / 画面が止まった最長時間 {maxGap} ms");
        }

        // ページを 1 つずつ更新して、どのページのレイアウトに時間がかかるかを測る（ダッシュボード表示中）
        viewModel.SelectedPage = viewModel.Dashboard;
        await WaitForRenderAsync(window);
        if (viewModel.LastSnapshot is { } snapshot)
        {
            foreach (var page in viewModel.Pages)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                page.Update(snapshot);
                var updated = watch.ElapsedMilliseconds;
                window.UpdateLayout();
                lines.Add($"  {page.Title} だけ更新: 計算 {updated} ms / レイアウト完了 {watch.ElapsedMilliseconds} ms");
            }
        }

        File.WriteAllLines(Path.Combine(outputDirectory, "update_timing.txt"), lines);
    }

    /// <summary>
    /// 実データで、期間を 90 日に切り替えたときと、その後に各ページを開いたときに、
    /// 画面が止まった最長時間を計測して real_timing.txt に書く（性能確認用。保存済みの連携情報を使う）。
    /// </summary>
    public static async Task MeasureRealAsync(string outputDirectory, double width = 1400, double height = 900)
    {
        Directory.CreateDirectory(outputDirectory);
        var store = new SettingsStore(SettingsStore.DefaultFilePath, new DpapiSecretProtector());
        var settings = store.Load();
        var originalDays = settings.RangeDays;
        settings.RangeDays = 14;

        // トークンが自動更新された場合に失われないよう、本物の保存先を使う
        var viewModel = new MainViewModel(store, settings, builtInClient: BuiltInClient.Load());
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        Application.Current.MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
        await WaitForRenderAsync(window);

        var lines = new List<string> { $"実データ: {!viewModel.IsDemo}" };

        async Task Measure(string label, Action action)
        {
            var frames = System.Diagnostics.Stopwatch.StartNew();
            long last = 0, maxGap = 0;
            EventHandler onRendering = (_, _) =>
            {
                var now = frames.ElapsedMilliseconds;
                maxGap = Math.Max(maxGap, now - last);
                last = now;
            };
            System.Windows.Media.CompositionTarget.Rendering += onRendering;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            action();
            await Task.Delay(50);
            for (var i = 0; i < 400 && (viewModel.IsBusy || viewModel.HeartRate.IsLoading); i++)
            {
                await Task.Delay(25);
            }

            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var elapsed = watch.ElapsedMilliseconds;
            await Task.Delay(100);
            System.Windows.Media.CompositionTarget.Rendering -= onRendering;
            lines.Add($"{label}: 完了まで {elapsed} ms / 画面が止まった最長時間 {maxGap} ms");
        }

        foreach (var startPage in new PageViewModel[] { viewModel.Dashboard, viewModel.Sleep })
        {
            viewModel.SelectedRange = viewModel.RangeOptions[1];
            await Measure($"{startPage.Title} 表示中に 14 日へ", () => viewModel.SelectedPage = startPage);
            await Measure($"{startPage.Title} 表示中に 90 日へ切り替え", () => viewModel.SelectedRange = viewModel.RangeOptions[^1]);
            foreach (var page in viewModel.Pages)
            {
                await Measure($"  90 日で {page.Title} を開く", () => viewModel.SelectedPage = page);
            }
        }

        // 期間の設定を元に戻して保存する
        viewModel.SelectedRange = viewModel.RangeOptions.FirstOrDefault(r => r.Days == originalDays) ?? viewModel.RangeOptions[1];
        await Task.Delay(800);
        File.WriteAllLines(Path.Combine(outputDirectory, "real_timing.txt"), lines);
        window.Close();
    }

    /// <summary>テーマ切り替えにかかる時間を計測して theme_timing.txt に書く（性能確認用）</summary>
    private static async Task MeasureThemeSwitchAsync(Window window, MainViewModel viewModel, string outputDirectory)
    {
        viewModel.SelectedPage = viewModel.Sleep;
        await WaitForRenderAsync(window);
        var lines = new List<string>();
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark, AppTheme.Light, AppTheme.Dark })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ThemeManager.Apply(theme);
            var applied = watch.ElapsedMilliseconds;
            viewModel.RebuildCharts();
            var rebuilt = watch.ElapsedMilliseconds;
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            lines.Add($"{theme}: 色の差し替え {applied} ms / グラフ再作成まで {rebuilt} ms / 描画完了まで {watch.ElapsedMilliseconds} ms");
        }

        File.WriteAllLines(Path.Combine(outputDirectory, "theme_timing.txt"), lines);
    }

    private static async Task WaitForRenderAsync(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
        }
    }

    private static T? FindVisual<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && predicate(match))
            {
                return match;
            }

            if (FindVisual(child, predicate) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>表示中のページのスクロール内容（ページ全体）を探す</summary>
    private static FrameworkElement? FindPageContent(DependencyObject parent, PageViewModel page)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is UserControl { IsVisible: true } view && view.DataContext == page && view.Content is ScrollViewer { Content: FrameworkElement content })
            {
                return content;
            }

            if (FindPageContent(child, page) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>設定画面を「連携済み」の表示にする（データはデモのまま。画面確認用）</summary>
    public static bool ShowConnected { get; set; }

    /// <summary>画像を書き出さない（性能の計測だけを行う。書き出し自体がレイアウトに影響するため）</summary>
    public static bool MeasureOnly { get; set; }

    private static void Save(FrameworkElement element, string path)
    {
        if (MeasureOnly)
        {
            return;
        }

        var width = Math.Ceiling(element.ActualWidth);
        var height = Math.Ceiling(element.ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // 親要素の位置やクリップの影響を受けないよう VisualBrush 経由で描画する
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var background = Application.Current.TryFindResource("PageBrush") as Brush ?? Brushes.Black;
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(
                new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null,
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

using RingInfo.App.ViewModels;
using RingInfo.Core.Formatting;
using RingInfo.Core.Settings;

namespace RingInfo.App.Tests;

public class MainViewModelTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static async Task<MainViewModel> CreateLoadedAsync()
    {
        var viewModel = new MainViewModel(settingsStore: null, new AppSettings(), forceDemo: true);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task デモデータを読み込んで各ページを更新する()
    {
        var viewModel = await CreateLoadedAsync();

        Assert.True(viewModel.IsDemo);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(Today, viewModel.EndDate);
        Assert.Equal(Today.AddDays(-13), viewModel.StartDate);
        Assert.NotNull(viewModel.Dashboard.SleepCard.Score);
        Assert.NotNull(viewModel.Dashboard.ScoreTrend);
        Assert.NotEmpty(viewModel.Dashboard.RingInfo);
        Assert.NotEmpty(viewModel.Sleep.Rows);
        Assert.NotEmpty(viewModel.Activity.Rows);
        Assert.NotEmpty(viewModel.Condition.Rows);
        Assert.Contains("最終更新", viewModel.StatusText);
    }

    [Fact]
    public void 期間の前後移動は今日より先に進まない()
    {
        var viewModel = new MainViewModel(settingsStore: null, new AppSettings { RangeDays = 7 }, forceDemo: true);

        Assert.Equal(7, viewModel.SelectedRange.Days);
        Assert.False(viewModel.NextRangeCommand.CanExecute(null));

        viewModel.PreviousRangeCommand.Execute(null);
        Assert.Equal(Today.AddDays(-7), viewModel.EndDate);
        Assert.True(viewModel.NextRangeCommand.CanExecute(null));

        viewModel.NextRangeCommand.Execute(null);
        Assert.Equal(Today, viewModel.EndDate);
    }

    [Fact]
    public void 設定ページではデモのお知らせを出さない()
    {
        var viewModel = new MainViewModel(settingsStore: null, new AppSettings(), forceDemo: true);

        Assert.True(viewModel.ShowDemoBanner);
        viewModel.OpenSettingsCommand.Execute(null);
        Assert.Same(viewModel.SettingsPage, viewModel.SelectedPage);
        Assert.True(viewModel.SettingsPage.IsSelected);
        Assert.False(viewModel.Dashboard.IsSelected);
        Assert.False(viewModel.ShowDemoBanner);
        Assert.False(viewModel.ShowPeriodControls);

        viewModel.SelectedPage = viewModel.Help;
        Assert.False(viewModel.ShowPeriodControls);

        viewModel.SelectedPage = viewModel.Sleep;
        Assert.True(viewModel.ShowPeriodControls);
    }

    [Fact]
    public async Task 表の行を選ぶとその日の詳細を表示する()
    {
        var viewModel = await CreateLoadedAsync();
        var sleep = viewModel.Sleep;
        var row = sleep.Rows[3];

        sleep.SelectedRow = row;

        Assert.Equal(row.Day, sleep.SelectedDay);
        Assert.Equal(DisplayFormat.DayShort(row.Day), sleep.ScoreCard.DayText);
        Assert.Equal(row.Score, sleep.ScoreCard.ScoreText);
    }

    [Fact]
    public async Task 前の日へ移動でき_期間の最初の日で止まる()
    {
        var viewModel = await CreateLoadedAsync();
        var activity = viewModel.Activity;

        Assert.Equal(Today, activity.SelectedDay);
        Assert.False(activity.NextDayCommand.CanExecute(null));

        while (activity.PreviousDayCommand.CanExecute(null))
        {
            activity.PreviousDayCommand.Execute(null);
        }

        Assert.Equal(viewModel.StartDate, activity.SelectedDay);
        Assert.Same(activity.Rows[^1], activity.SelectedRow);
    }
}

public class SettingsPageViewModelTests
{
    private static SettingsPageViewModel Create(AppSettings settings, Action? save = null)
        => new(settings, null, save ?? (() => { }), () => Task.CompletedTask);

    [Fact]
    public void 登録フォームの入力値にリダイレクトURIを表示し_入力状態で手順2の完了を判定する()
    {
        var viewModel = Create(new AppSettings());

        var redirect = Assert.Single(viewModel.RegistrationFields, f => f.Label == "Redirect URIs");
        Assert.Equal(AppSettings.DefaultRedirectUri, redirect.Value);
        Assert.True(redirect.CanCopy);
        Assert.False(Assert.Single(viewModel.RegistrationFields, f => f.Label == "Contact Email").CanCopy);
        Assert.All(
            viewModel.RegistrationFields.Where(f => f.Label is "Website" or "Privacy Policy" or "Terms of Service"),
            f => Assert.Equal("https://github.com/tomoaki1230", f.Value));
        Assert.True(viewModel.IsGuideVisible);
        Assert.Equal("https://developer.ouraring.com/applications", SettingsPageViewModel.DeveloperPortalUrl);

        Assert.False(viewModel.HasCredentials);
        viewModel.ClientId = "id";
        viewModel.ClientSecret = "secret";
        Assert.True(viewModel.HasCredentials);
    }

    [Fact]
    public async Task ClientIdが無いと連携せずにメッセージを表示する()
    {
        var viewModel = Create(new AppSettings());

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsError);
        Assert.Contains("Client ID", viewModel.Message);
    }

    [Fact]
    public async Task リダイレクトURIの形式が正しくないと連携しない()
    {
        var viewModel = Create(new AppSettings());
        viewModel.ClientId = "id";
        viewModel.ClientSecret = "secret";
        viewModel.RedirectUri = "https://example.com/callback";

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsError);
        Assert.Contains("localhost", viewModel.Message);
    }

    [Fact]
    public async Task 保存すると設定に反映し_ClientIdが変わればトークンを破棄する()
    {
        var settings = new AppSettings
        {
            ClientId = "old",
            ClientSecret = "secret",
            Token = new Core.Auth.OAuthToken("at", "rt", null, null),
        };
        var saved = 0;
        var viewModel = Create(settings, () => saved++);
        Assert.True(viewModel.IsConnected);

        viewModel.ClientId = "  new-id  ";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("new-id", settings.ClientId);
        Assert.Null(settings.Token);
        Assert.False(viewModel.IsConnected);
        Assert.Equal(1, saved);
        Assert.False(viewModel.IsError);
    }
}

public class SettingsPageBuiltInClientTests
{
    private static readonly OuraClientInfo BuiltIn = new("built-in-id", "built-in-secret", AppSettings.DefaultRedirectUri);

    [Fact]
    public void 配布版では登録手順を出さず_連携ボタンだけを案内する()
    {
        var viewModel = new SettingsPageViewModel(new AppSettings(), null, () => { }, () => Task.CompletedTask, builtInClient: BuiltIn);

        Assert.True(viewModel.HasBuiltInClient);
        Assert.False(viewModel.ShowRegistrationSteps);
        Assert.Equal("Oura と連携する", viewModel.ConnectStepTitle);
        Assert.Contains("ログインするだけ", viewModel.IntroText);
        Assert.False(viewModel.IsConnected);
    }

    [Fact]
    public async Task 上級者向けに自分で登録したアプリへ切り替えられる()
    {
        var settings = new AppSettings();
        var saved = 0;
        var reloaded = 0;
        var viewModel = new SettingsPageViewModel(settings, null, () => saved++, () => { reloaded++; return Task.CompletedTask; }, builtInClient: BuiltIn);

        await viewModel.ToggleCustomClientCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowRegistrationSteps);
        Assert.True(settings.UseCustomClient);
        Assert.Equal(1, saved);
        Assert.Equal(1, reloaded);
        Assert.Equal("通常の連携方法に戻す", viewModel.CustomClientToggleText);
    }

    [Fact]
    public void 組み込みのアプリで取得したトークンがあれば連携済み()
    {
        var settings = new AppSettings { Token = new Core.Auth.OAuthToken("at", "rt", null, null), TokenClientId = "built-in-id" };

        var viewModel = new SettingsPageViewModel(settings, null, () => { }, () => Task.CompletedTask, builtInClient: BuiltIn);

        Assert.True(viewModel.IsConnected);
        Assert.False(viewModel.IsGuideVisible);
    }
}

public class SettingsThemeTests
{
    [Fact]
    public void テーマを選ぶと設定に保存して切り替える()
    {
        var settings = new AppSettings();
        var saved = 0;
        ThemePreference? applied = null;
        var viewModel = new SettingsPageViewModel(settings, null, () => saved++, () => Task.CompletedTask, applyTheme: t => applied = t);

        Assert.Equal(ThemePreference.System, viewModel.SelectedTheme.Value);

        viewModel.SelectedTheme = viewModel.ThemeOptions.Single(o => o.Value == ThemePreference.Light);

        Assert.Equal(ThemePreference.Light, settings.Theme);
        Assert.Equal(ThemePreference.Light, applied);
        Assert.Equal(1, saved);
    }
}

public class CalendarTests
{
    [Fact]
    public async Task カレンダーで選べるのは取得期間内の日だけ()
    {
        var viewModel = new MainViewModel(settingsStore: null, new AppSettings { RangeDays = 7 }, forceDemo: true);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var sleep = viewModel.Sleep;
        var start = viewModel.StartDate.ToDateTime(TimeOnly.MinValue);

        Assert.Equal(start, sleep.CalendarMin);
        Assert.Equal(viewModel.EndDate.ToDateTime(TimeOnly.MinValue), sleep.CalendarMax);

        sleep.CalendarDate = start.AddDays(2);
        Assert.Equal(viewModel.StartDate.AddDays(2), sleep.SelectedDay);

        // 期間外は無視される
        sleep.CalendarDate = start.AddDays(-1);
        Assert.Equal(viewModel.StartDate.AddDays(2), sleep.SelectedDay);
    }

    [Fact]
    public void カレンダーは日曜始まりの6週分で_範囲外と選択中を判定する()
    {
        var days = RingInfo.App.Controls.MonthCalendar.BuildDays(
            new DateTime(2026, 10, 1), new DateTime(2026, 9, 18), new DateTime(2026, 10, 1), new DateTime(2026, 9, 30), new DateTime(2026, 10, 1));

        Assert.Equal(42, days.Count);
        Assert.Equal(new DateTime(2026, 9, 27), days[0].Date); // 2026/10/1 は木曜 → 前の日曜から
        Assert.True(days.Single(d => d.Date == new DateTime(2026, 9, 30)).IsSelected);
        Assert.True(days.Single(d => d.Date == new DateTime(2026, 10, 1)).IsToday);
        Assert.False(days.Single(d => d.Date == new DateTime(2026, 10, 2)).IsSelectable);
        Assert.True(days.Single(d => d.Date == new DateTime(2026, 9, 27)).IsSelectable);
        Assert.False(days.Single(d => d.Date == new DateTime(2026, 9, 27)).IsCurrentMonth);
    }
}

public class HeartRateTableAndThemeTests
{
    [Fact]
    public async Task 心拍数ページに日別の表があり_行を選ぶとその日になる()
    {
        var viewModel = new MainViewModel(settingsStore: null, new AppSettings { RangeDays = 7 }, forceDemo: true);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var heartRate = viewModel.HeartRate;

        Assert.Equal(7, heartRate.Rows.Count);
        Assert.Equal(viewModel.EndDate, heartRate.Rows[0].Day);
        Assert.Same(heartRate.Rows[0], heartRate.SelectedRow);

        heartRate.SelectedRow = heartRate.Rows[2];
        Assert.Equal(heartRate.Rows[2].Day, heartRate.SelectedDay);
    }

    [Fact]
    public async Task テーマ変更では表示中のページだけすぐにグラフを作り直す()
    {
        var viewModel = new MainViewModel(settingsStore: null, new AppSettings(), forceDemo: true);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var sleepRows = viewModel.Sleep.Rows;
        var dashboardChart = viewModel.Dashboard.ScoreTrend;
        var sleepChart = viewModel.Sleep.ScoreTrend;

        viewModel.RebuildCharts();

        // 表示中（ダッシュボード）はすぐに作り直す
        Assert.NotSame(dashboardChart, viewModel.Dashboard.ScoreTrend);
        Assert.False(viewModel.Dashboard.ChartsOutdated);

        // 他のページは開いたときに作り直し、表は作り直さない
        Assert.True(viewModel.Sleep.ChartsOutdated);
        Assert.Same(sleepChart, viewModel.Sleep.ScoreTrend);
        viewModel.SelectedPage = viewModel.Sleep;
        Assert.NotSame(sleepChart, viewModel.Sleep.ScoreTrend);
        Assert.False(viewModel.Sleep.ChartsOutdated);
        Assert.Same(sleepRows, viewModel.Sleep.Rows);
    }
}

public class HelpPageTests
{
    [Fact]
    public void ヘルプにバージョン_製作者_著作権_商標を表示する()
    {
        var help = new HelpPageViewModel();

        Assert.Equal("1.0.0", help.Version);
        Assert.Equal("Tomoaki Bessho", help.Author);
        Assert.Equal("Copyright (c) 2026 Tomoaki Bessho", help.Copyright);
        Assert.Equal("Oura製品に関する「OURA」「OURARING」などの名称やロゴは、開発元である Oura Health Oy の登録商標です。", help.Trademark);
    }

    [Fact]
    public void ヘルプページはサイドバーの最後にある()
    {
        var viewModel = new MainViewModel(settingsStore: null, new AppSettings(), forceDemo: true);

        Assert.Same(viewModel.Help, viewModel.Pages[^1]);
        Assert.Equal("ヘルプ", viewModel.Help.Title);
    }
}

public class LicenseTests
{
    [Fact]
    public void すべてのライブラリのライセンス全文を読み込める()
    {
        var help = new HelpPageViewModel();

        Assert.All(help.Libraries, library =>
        {
            var text = HelpPageViewModel.LoadLicenseText(library.ResourceName);
            Assert.Contains("Permission is hereby granted, free of charge", text);
            Assert.Contains("Copyright", text);
        });
        Assert.Contains("OxyPlot contributors", HelpPageViewModel.LoadLicenseText("OxyPlot"));
        Assert.Contains(".NET Foundation", HelpPageViewModel.LoadLicenseText("CommunityToolkit.Mvvm"));
    }

    [Fact]
    public void ライセンス名を押すと全文を表示し_もう一度押すか閉じると隠す()
    {
        var help = new HelpPageViewModel();
        var oxyPlot = help.Libraries.Single(l => l.ResourceName == "OxyPlot");

        help.ShowLicenseCommand.Execute(oxyPlot);
        Assert.True(help.IsLicenseVisible);
        Assert.Contains("OxyPlot", help.LicenseTitle);
        Assert.Contains("MIT License", help.LicenseText);

        help.ShowLicenseCommand.Execute(oxyPlot);
        Assert.False(help.IsLicenseVisible);

        help.ShowLicenseCommand.Execute(help.Libraries[0]);
        help.CloseLicenseCommand.Execute(null);
        Assert.False(help.IsLicenseVisible);
        Assert.Equal("", help.LicenseText);
    }
}

public class MembershipNoticeTests
{
    [Fact]
    public void 設定とヘルプにメンバーシップの注意を表示する()
    {
        var settings = new SettingsPageViewModel(new AppSettings(), null, () => { }, () => Task.CompletedTask);
        var help = new HelpPageViewModel();

        Assert.Contains("有効な Oura メンバーシップに加入していないユーザーのデータは、Oura API 経由で取得できません", settings.MembershipNotice);
        Assert.Equal(settings.MembershipNotice, help.MembershipNotice);
    }
}

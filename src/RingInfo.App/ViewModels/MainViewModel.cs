using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RingInfo.App.Services;
using RingInfo.Core.Api;
using RingInfo.Core.Auth;
using RingInfo.Core.Demo;
using RingInfo.Core.Formatting;
using RingInfo.Core.Models;
using RingInfo.Core.Services;
using RingInfo.Core.Settings;

namespace RingInfo.App.ViewModels;

/// <summary>
/// メイン画面: ページの切り替え、表示期間の選択、データの読み込みを担当する。
/// Oura と連携済みなら実 API、未連携ならデモデータを表示する。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore? _settingsStore;
    private readonly AppSettings _settings;
    private readonly bool _forceDemo;
    private readonly OuraClientInfo? _builtInClient;
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.Local;
    private readonly object _settingsSync = new();
    private DashboardService _service;
    private CancellationTokenSource? _loadCancellation;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// トークンの保管場所（アプリ全体で 1 つ）。設定の変更などでデータ取得元を作り直しても共有し、
    /// 1 回限り有効なリフレッシュトークンを二重に使わないようにする。
    /// </summary>
    private readonly TokenStore _tokenStore = new();
    private readonly IOuraDataSource? _demoSource;

    /// <param name="settingsStore">設定の保存先（null なら保存しない）</param>
    /// <param name="settings">読み込んだ設定</param>
    /// <param name="forceDemo">連携状態に関わらずデモデータを表示する（スクリーンショット用）</param>
    /// <param name="builtInClient">配布版に組み込まれた Oura API アプリの情報（無ければ利用者が登録する）</param>
    /// <param name="applyTheme">テーマを変更したときに配色を切り替える処理</param>
    /// <param name="demoSource">未連携時に表示するデータ（省略時はデモデータ。画面確認・テスト用）</param>
    /// <param name="timeProvider">現在時刻（テスト用）</param>
    public MainViewModel(
        SettingsStore? settingsStore,
        AppSettings settings,
        bool forceDemo = false,
        OuraClientInfo? builtInClient = null,
        Action<ThemePreference>? applyTheme = null,
        IOuraDataSource? demoSource = null,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _demoSource = demoSource;
        _tokenStore.TokenRefreshed += OnTokenRefreshed;
        _settingsStore = settingsStore;
        _settings = settings;
        _forceDemo = forceDemo;
        _builtInClient = builtInClient;

        Dashboard = new DashboardPageViewModel();
        Sleep = new SleepPageViewModel();
        Activity = new ActivityPageViewModel();
        Condition = new ConditionPageViewModel();
        HeartRate = new HeartRatePageViewModel(LoadHeartRateAsync, _timeZone, () => Today, _timeProvider);
        SettingsPage = new SettingsPageViewModel(settings, settingsStore?.FilePath, SaveSettings, OnConnectionChangedAsync, () => SelectedPage = Dashboard, builtInClient, applyTheme);
        Help = new HelpPageViewModel(settingsStore?.FilePath, App.ErrorLogPath);
        Pages = [Dashboard, Sleep, Activity, Condition, HeartRate, SettingsPage, Help];

        RangeOptions = [new(7, "7日"), new(14, "14日"), new(30, "30日"), new(90, "90日")];
        _selectedRange = RangeOptions.FirstOrDefault(r => r.Days == settings.RangeDays) ?? RangeOptions[1];
        _endDate = Today;
        _service = CreateService();
        _selectedPage = Dashboard;
        Dashboard.IsSelected = true;
        UpdateModeText();
    }

    public DashboardPageViewModel Dashboard { get; }

    public SleepPageViewModel Sleep { get; }

    public ActivityPageViewModel Activity { get; }

    public ConditionPageViewModel Condition { get; }

    public HeartRatePageViewModel HeartRate { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public HelpPageViewModel Help { get; }

    public ObservableCollection<PageViewModel> Pages { get; }

    public IReadOnlyList<RangeOption> RangeOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDemoBanner), nameof(ShowPeriodControls))]
    private PageViewModel _selectedPage;

    /// <summary>上部の期間選択・更新ボタンを表示するか（データを表示しない設定・ヘルプでは隠す）</summary>
    public bool ShowPeriodControls => SelectedPage != SettingsPage && SelectedPage != Help;

    [ObservableProperty]
    private RangeOption _selectedRange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    [NotifyCanExecuteChangedFor(nameof(NextRangeCommand), nameof(GoToTodayCommand))]
    private DateOnly _endDate;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _needsReconnect;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>読み込み中に表示する、いま行っている処理</summary>
    [ObservableProperty]
    private string _busyText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDemoBanner))]
    private bool _isDemo;

    /// <summary>デモ表示中のお知らせ（設定ページでは不要）</summary>
    public bool ShowDemoBanner => IsDemo && SelectedPage != SettingsPage;

    [ObservableProperty]
    private string _modeText = "";

    public DateOnly StartDate => EndDate.AddDays(-(SelectedRange.Days - 1));

    public string RangeText => $"{DisplayFormat.DayLong(StartDate)} 〜 {DisplayFormat.DayLong(EndDate)}";

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone).DateTime);

    /// <summary>起動時の読み込み</summary>
    public Task InitializeAsync() => LoadAsync(forceReload: false);

    /// <summary>
    /// 期間・日付の切り替えから読み込みを始めるまでの待ち時間。
    /// 連打しても最後の操作の分だけ読み込む（リクエスト上限への配慮）。
    /// </summary>
    public static readonly TimeSpan LoadDelay = TimeSpan.FromMilliseconds(400);

    private CancellationTokenSource? _scheduledLoad;

    /// <summary>少し待ってから読み込む。待っている間に再度呼ばれたら、前の予約は取り消す。</summary>
    private void ScheduleLoad()
    {
        _scheduledLoad?.Cancel();
        var scheduled = _scheduledLoad = new CancellationTokenSource();
        IsBusy = true;
        BusyText = "読み込みを準備中…";
        _ = RunScheduledLoadAsync(scheduled.Token);
    }

    private async Task RunScheduledLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(LoadDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await LoadAsync(forceReload: false);
    }

    partial void OnSelectedPageChanged(PageViewModel? oldValue, PageViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        newValue.IsSelected = true;
        newValue.RefreshChartsIfOutdated();
        _ = newValue.OnActivatedAsync();
    }

    partial void OnSelectedRangeChanged(RangeOption value)
    {
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(RangeText));
        _settings.RangeDays = value.Days;
        SaveSettings();
        ScheduleLoad();
    }

    partial void OnEndDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(StartDate));
        _followsToday = value == Today;
        ScheduleLoad();
    }

    /// <summary>「今日まで」の期間を表示しているか（日付が変わったら新しい今日に合わせる）</summary>
    private bool _followsToday = true;

    /// <summary>最後に表示したデータ（性能確認用）</summary>
    internal DashboardSnapshot? LastSnapshot { get; private set; }

    /// <summary>最後にデータを取得した時刻（UTC）</summary>
    private DateTimeOffset? _lastLoadedAt;

    /// <summary>
    /// ウィンドウがアクティブになったとき（アプリに戻ったとき）。
    /// ・起動したまま日付が変わっていた場合、「今日まで」を表示していれば新しい今日に合わせて読み込み直す
    /// ・今日を含む期間を表示していて、前回の取得から一定時間（5 分）たっていれば取り直す（今日のデータは時間とともに増えるため）
    /// </summary>
    public void OnWindowActivated()
    {
        if (_followsToday && EndDate < Today)
        {
            EndDate = Today;
            return;
        }

        NextRangeCommand.NotifyCanExecuteChanged();
        GoToTodayCommand.NotifyCanExecuteChanged();

        if (EndDate >= Today && !IsBusy && _lastLoadedAt is { } loadedAt
            && _timeProvider.GetUtcNow() - loadedAt >= DashboardService.SnapshotCacheDuration)
        {
            _ = LoadAsync(forceReload: false);
        }
    }

    [RelayCommand]
    private void PreviousRange() => EndDate = EndDate.AddDays(-SelectedRange.Days);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NextRange()
    {
        var next = EndDate.AddDays(SelectedRange.Days);
        EndDate = next > Today ? Today : next;
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoToToday() => EndDate = Today;

    private bool CanGoForward() => EndDate < Today;

    [RelayCommand]
    private void OpenSettings() => SelectedPage = SettingsPage;

    [RelayCommand]
    private void DismissWarning() => WarningMessage = null;

    /// <summary>「更新」ボタン: 一時保存を使わず、Oura から最新のデータを取得する</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(forceReload: true);

    private async Task LoadAsync(bool forceReload)
    {
        // 予約済みの読み込みは、この読み込みに置き換える
        _scheduledLoad?.Cancel();

        // 起動したまま日付が変わった場合: 「今日まで」を表示していれば新しい今日に合わせる（EndDate の変更で読み込み直す）
        if (_followsToday && EndDate < Today)
        {
            EndDate = Today;
            return;
        }

        NextRangeCommand.NotifyCanExecuteChanged();
        GoToTodayCommand.NotifyCanExecuteChanged();
        _loadCancellation?.Cancel();
        var cancellation = _loadCancellation = new CancellationTokenSource();
        IsBusy = true;
        BusyText = IsDemo ? "データを読み込み中…" : "Oura からデータを取得中…";

        try
        {
            var snapshot = await _service.LoadAsync(StartDate, EndDate, cancellation.Token, forceReload);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ErrorMessage = null;
            NeedsReconnect = false;
            LastSnapshot = snapshot;
            BusyText = "表示を更新中…";

            // 表示中のページを先に更新し、他のページは 1 ページずつ、合間に画面の描画を挟んで更新する
            // （まとめて更新すると、期間が長いときに画面と進捗表示が止まって見える）
            var ordered = Pages.OrderByDescending(p => p == SelectedPage).ToList();
            foreach (var page in ordered)
            {
                page.Update(snapshot);
                page.MarkChartsUpToDate();
                await YieldToRenderAsync();
                if (cancellation.IsCancellationRequested)
                {
                    // 新しい読み込みに置き換わった（残りのページは新しい読み込みで更新される）
                    return;
                }
            }

            WarningMessage = snapshot.Warnings.Count == 0
                ? null
                : "一部のデータを取得できませんでした。" + string.Join(" / ", snapshot.Warnings);
            StatusText = $"最終更新 {TimeZoneInfo.ConvertTime(snapshot.LoadedAt, _timeZone):HH:mm:ss}";
            _lastLoadedAt = snapshot.LoadedAt;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 期間を選び直した場合など、新しい読み込みに置き換わった
        }
        catch (OuraAuthenticationRequiredException ex)
        {
            ShowError(ex.Message, reconnect: true);
        }
        catch (OuraApiException ex)
        {
            ShowError(ex.Message, reconnect: ex.IsAuthenticationError);
        }
        catch (Exception ex)
        {
            ShowError($"データの読み込み中にエラーが発生しました。（{ex.Message}）", reconnect: false);
        }
        finally
        {
            if (_loadCancellation == cancellation)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// テーマ変更時にグラフを作り直す（データの再取得や表の作り直しはしない）。
    /// 表示中のページはすぐに、他のページは開いたときに作り直す。
    /// </summary>
    public void RebuildCharts()
    {
        foreach (var page in Pages)
        {
            page.MarkChartsOutdated(rebuildNow: page == SelectedPage);
        }
    }

    /// <summary>画面の描画と入力の処理をいったん行わせる（WPF 上でのみ。テストでは何もしない）</summary>
    private static async Task YieldToRenderAsync()
    {
        if (SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext)
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void ShowError(string message, bool reconnect)
    {
        ErrorMessage = message;
        NeedsReconnect = reconnect;
        StatusText = "読み込みに失敗しました";
    }

    private Task<IReadOnlyList<HeartRateSample>> LoadHeartRateAsync(DateOnly day, bool forceReload, CancellationToken cancellationToken)
        => _service.LoadHeartRateAsync(day, _timeZone, cancellationToken, forceReload);

    /// <summary>連携・連携解除・認証情報の変更後に、データ取得元を作り直して再読み込みする</summary>
    private Task OnConnectionChangedAsync()
    {
        _service = CreateService();
        UpdateModeText();
        SettingsPage.RefreshConnectionState();
        return LoadAsync(forceReload: false);
    }

    private DashboardService CreateService()
    {
        if (!_forceDemo
            && OuraClientSelector.IsConnected(_settings, _builtInClient)
            && _settings.Token is { } token
            && OuraClientSelector.Resolve(_settings, _builtInClient) is { } client
            && LoopbackAuthorizationListener.TryValidateRedirectUri(client.RedirectUri, out var redirectUri, out _))
        {
            var credentials = new OAuthClientCredentials(client.ClientId, client.ClientSecret, redirectUri!);
            _tokenStore.Replace(token);
            var tokenManager = new OAuthTokenManager(new OuraOAuthClient(AppHttp.Client), credentials, _tokenStore);
            IsDemo = false;
            return new DashboardService(new OuraApiClient(AppHttp.Client, tokenManager), isDemo: false);
        }

        _tokenStore.Replace(null);
        IsDemo = true;
        return new DashboardService(_demoSource ?? new DemoDataSource(_timeProvider, _timeZone), isDemo: true, _timeProvider);
    }

    /// <summary>トークンが自動更新されたら保存する（リフレッシュトークンは 1 回限りのため必須）</summary>
    private void OnTokenRefreshed(object? sender, OAuthToken token)
    {
        lock (_settingsSync)
        {
            _settings.Token = token;
        }

        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_settingsStore is null)
        {
            return;
        }

        lock (_settingsSync)
        {
            try
            {
                _settingsStore.Save(_settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WarningMessage = $"設定を保存できませんでした。（{ex.Message}）";
            }
        }
    }

    private void UpdateModeText()
        => ModeText = IsDemo ? "デモデータ" : "Oura 連携中";
}

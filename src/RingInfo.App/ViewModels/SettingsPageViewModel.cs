using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RingInfo.App.Services;
using RingInfo.Core.Auth;
using RingInfo.Core.Settings;

namespace RingInfo.App.ViewModels;

/// <summary>テーマの選択肢</summary>
public sealed record ThemeOption(ThemePreference Value, string Label);

/// <summary>Oura 開発者ページの登録フォームに入力する 1 項目</summary>
/// <param name="Label">フォームの項目名（英語表記のまま）</param>
/// <param name="Value">入力する値（コピー対象。null ならコピー不可）</param>
/// <param name="Hint">補足説明</param>
public sealed record RegistrationField(string Label, string? Value, string Hint)
{
    public bool CanCopy => !string.IsNullOrEmpty(Value);

    public string DisplayValue => Value ?? "";
}

/// <summary>
/// 設定ページ: Oura との連携。
/// 配布版（Oura API アプリの情報が組み込まれている）では「Oura と連携する」を押すだけ。
/// 組み込みが無い場合や「自分で登録したアプリを使う」を選んだ場合は、
/// 3 つの手順（アプリ登録 → ID/Secret 入力 → 連携）で案内する。
/// </summary>
public sealed partial class SettingsPageViewModel : PageViewModel
{
    /// <summary>Oura の API アプリ管理ページ（2026 年時点の開発者ポータル）</summary>
    public const string DeveloperPortalUrl = "https://developer.ouraring.com/applications";

    /// <summary>
    /// 登録フォームの Website / Privacy Policy / Terms of Service に入れる URL（RingInfo 作者の GitHub）。
    /// 利用者が自分の URL を持っていなくても登録できるようにするため。
    /// </summary>
    public const string PublisherUrl = "https://github.com/tomoaki1230";

    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Func<Task> _onConnectionChanged;
    private readonly Action? _openDashboard;
    private readonly OuraClientInfo? _builtInClient;
    private readonly Action<ThemePreference>? _applyTheme;
    private CancellationTokenSource? _connectCancellation;
    private Uri? _authorizationUri;

    public SettingsPageViewModel(
        AppSettings settings,
        string? settingsFilePath,
        Action saveSettings,
        Func<Task> onConnectionChanged,
        Action? openDashboard = null,
        OuraClientInfo? builtInClient = null,
        Action<ThemePreference>? applyTheme = null)
    {
        _applyTheme = applyTheme;
        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == settings.Theme) ?? ThemeOptions[0];
        _settings = settings;
        _saveSettings = saveSettings;
        _onConnectionChanged = onConnectionChanged;
        _openDashboard = openDashboard;
        _builtInClient = builtInClient;
        _useCustomClient = settings.UseCustomClient;
        SettingsFilePath = settingsFilePath;
        _clientId = settings.ClientId;
        _clientSecret = settings.ClientSecret;
        _redirectUri = settings.RedirectUri;
        _isGuideVisible = !OuraClientSelector.IsConnected(settings, builtInClient);
    }

    public override string Title => "設定";

    public override string IconGlyph => "";

    public string? SettingsFilePath { get; }

    public string DeveloperPortal => DeveloperPortalUrl;

    /// <summary>Oura メンバーシップについての注意</summary>
    public string MembershipNotice => HelpPageViewModel.MembershipNoticeText;

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "システムに合わせる"),
        new(ThemePreference.Light, "ライト"),
        new(ThemePreference.Dark, "ダーク"),
    ];

    /// <summary>画面のテーマ（選ぶとすぐに切り替えて保存する）</summary>
    [ObservableProperty]
    private ThemeOption _selectedTheme;

    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        _settings.Theme = value.Value;
        _saveSettings();
        _applyTheme?.Invoke(value.Value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCredentials))]
    private string _clientId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCredentials))]
    private string _clientSecret;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegistrationFields))]
    private string _redirectUri;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowConnectedPanel), nameof(ShowConnectButton))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand), nameof(CancelConnectCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _accountText = "";

    /// <summary>連携手順の表示（連携済みなら初期状態で折りたたむ）</summary>
    [ObservableProperty]
    private bool _isGuideVisible;

    /// <summary>リダイレクト URI などの詳細設定の表示</summary>
    [ObservableProperty]
    private bool _isAdvancedVisible;

    /// <summary>配布版でも自分で登録したアプリを使う（上級者向け）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRegistrationSteps), nameof(IntroText), nameof(ConnectStepTitle), nameof(CustomClientToggleText))]
    private bool _useCustomClient;

    public bool IsConnected => OuraClientSelector.IsConnected(_settings, _builtInClient);

    /// <summary>配布版（アプリ情報が組み込まれている）か</summary>
    public bool HasBuiltInClient => _builtInClient is not null;

    /// <summary>アプリ登録・ID/Secret 入力の手順を表示するか（自分で登録したアプリを使う場合）</summary>
    public bool ShowRegistrationSteps => !HasBuiltInClient || UseCustomClient;

    public string IntroText => ShowRegistrationSteps
        ? "下の 3 つの手順で、あなたの Oura Ring のデータを表示できるようになります（所要時間 5 分ほど）。"
        : "「Oura と連携する」を押して、Oura アカウントでログインするだけで、あなたの Oura Ring のデータを表示できます。";

    public string ConnectStepTitle => ShowRegistrationSteps ? "Oura にデータの読み取りを許可する" : "Oura と連携する";

    public string CustomClientToggleText => UseCustomClient
        ? "通常の連携方法に戻す"
        : "上級者向け: 自分で Oura に登録したアプリ（Client ID / Secret）を使う";

    /// <summary>手順 2（Client ID / Secret の入力）が済んでいるか</summary>
    public bool HasCredentials => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public bool ShowConnectedPanel => IsConnected && !IsConnecting;

    public bool ShowConnectButton => !IsConnecting;

    public string ConnectionStatus => IsConnected ? "Oura と連携済みです" : "まだ Oura と連携していません（デモデータを表示中）";

    public string ConnectButtonText => IsConnected ? "もう一度連携する" : "Oura と連携する";

    public string TokenExpiryText => _settings.Token?.ExpiresAt is { } expiresAt
        ? $"アクセストークンの有効期限: {expiresAt.LocalDateTime:yyyy/MM/dd HH:mm}（期限が近づくと自動で更新します）"
        : "";

    /// <summary>Oura 開発者ページの「New Application」に入力する値</summary>
    public IReadOnlyList<RegistrationField> RegistrationFields =>
    [
        new("Display Name", "RingInfo", "アプリの名前。連携時の許可画面に表示されます。"),
        new("Description", "Personal desktop viewer for my Oura Ring data", "アプリの説明。自由に書き換えてかまいません。"),
        new("Contact Email", null, "Oura に登録しているメールアドレス（Oura アプリにログインしているメールアドレス）を入力してください。"),
        new("Website", PublisherUrl, "RingInfo 作者の GitHub ページです。ご自身の URL を持っていなくても、このままコピーして使えます。"),
        new("Privacy Policy", PublisherUrl, "Website と同じ URL を入れてください。"),
        new("Terms of Service", PublisherUrl, "Website と同じ URL を入れてください。"),
        new("Redirect URIs", RedirectUri.Trim(), "【重要】この値を 1 文字も変えずに入力してください。末尾に / を付けないこと。"),
        new("Scopes", null, "すべてチェックが入ったままにしてください。"),
        new("I agree to the Oura API Agreement", null, "チェックを入れてから、ページ下部のボタンで作成（保存）します。"),
    ];

    public override void Update(Core.Services.DashboardSnapshot snapshot)
    {
        AccountText = snapshot.IsDemo || snapshot.PersonalInfo?.Email is not { } email ? "" : $"アカウント: {email}";
    }

    /// <summary>連携状態が変わったときに表示を更新する</summary>
    public void RefreshConnectionState()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(TokenExpiryText));
        OnPropertyChanged(nameof(ShowConnectedPanel));
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleGuide() => IsGuideVisible = !IsGuideVisible;

    [RelayCommand]
    private async Task ToggleCustomClientAsync()
    {
        UseCustomClient = !UseCustomClient;
        _settings.UseCustomClient = UseCustomClient;
        _saveSettings();
        Message = null;
        RefreshConnectionState();

        // 使うアプリが変わると連携状態も変わるため、データを取得し直す
        await _onConnectionChanged();
    }

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedVisible = !IsAdvancedVisible;

    [RelayCommand]
    private void ResetRedirectUri() => RedirectUri = AppSettings.DefaultRedirectUri;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate(requireSecret: false, out _))
        {
            return;
        }

        var wasConnected = IsConnected;
        ApplyInputs();
        _saveSettings();
        ShowMessage("設定を保存しました。", isError: false);

        // 連携中に認証情報を変えた場合は、新しい設定でデータを取得し直す
        if (wasConnected || IsConnected)
        {
            await _onConnectionChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        OAuthClientCredentials credentials;
        if (ShowRegistrationSteps)
        {
            if (!Validate(requireSecret: true, out var redirectUri))
            {
                return;
            }

            ApplyInputs();
            _saveSettings();
            credentials = new OAuthClientCredentials(ClientId.Trim(), ClientSecret.Trim(), redirectUri!);
        }
        else
        {
            var client = _builtInClient!;
            if (!LoopbackAuthorizationListener.TryValidateRedirectUri(client.RedirectUri, out var redirectUri, out var error))
            {
                ShowMessage($"組み込みの設定が正しくありません。{error}", isError: true);
                return;
            }

            credentials = new OAuthClientCredentials(client.ClientId, client.ClientSecret, redirectUri!);
        }

        _connectCancellation = new CancellationTokenSource();
        IsConnecting = true;
        Message = null;
        try
        {
            var flow = new OAuthAuthorizationFlow(new OuraOAuthClient(AppHttp.Client));
            var token = await flow.AuthorizeAsync(credentials, OpenBrowser, _connectCancellation.Token);

            _settings.Token = token;
            _settings.TokenClientId = credentials.ClientId;
            _saveSettings();
            RefreshConnectionState();
            IsGuideVisible = false;
            ShowMessage("連携しました。データを読み込んでいます…", isError: false);
            await _onConnectionChanged();
            ShowMessage("連携が完了しました。左のメニューから各ページを開くと、あなたのデータが表示されます。", isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowMessage("連携をキャンセルしました。", isError: true);
        }
        catch (OAuthException ex)
        {
            ShowMessage(ShowRegistrationSteps
                ? ex.Message + " 解決しない場合は、手順 1 の Redirect URIs と手順 2 の入力内容を確認してください。"
                : ex.Message, isError: true);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowMessage($"ブラウザを開けませんでした。（{ex.Message}）", isError: true);
        }
        finally
        {
            IsConnecting = false;
            _connectCancellation.Dispose();
            _connectCancellation = null;
        }
    }

    private bool CanConnect() => !IsConnecting;

    private void OpenBrowser(Uri uri)
    {
        _authorizationUri = uri;
        ShellLauncher.Open(uri);
    }

    /// <summary>ブラウザが開かなかった・閉じてしまった場合に、認可ページをもう一度開く</summary>
    [RelayCommand]
    private void ReopenBrowser()
    {
        if (_authorizationUri is not null)
        {
            ShellLauncher.Open(_authorizationUri);
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnecting))]
    private void CancelConnect() => _connectCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        _settings.Token = null;
        _settings.TokenClientId = null;
        _saveSettings();
        RefreshConnectionState();
        AccountText = "";
        IsGuideVisible = true;
        ShowMessage("連携を解除しました。デモデータの表示に切り替えます。", isError: false);
        await _onConnectionChanged();
    }

    private bool CanDisconnect() => IsConnected && !IsConnecting;

    [RelayCommand]
    private void OpenDashboard() => _openDashboard?.Invoke();

    [RelayCommand]
    private void OpenDeveloperPortal() => ShellLauncher.Open(DeveloperPortalUrl);

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            ShowMessage($"「{text}」をコピーしました。開発者ページの入力欄に貼り付けてください。", isError: false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException)
        {
            ShowMessage("クリップボードにコピーできませんでした。", isError: true);
        }
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        if (Path.GetDirectoryName(SettingsFilePath) is { } directory)
        {
            Directory.CreateDirectory(directory);
            ShellLauncher.Open(directory);
        }
    }

    private bool Validate(bool requireSecret, out Uri? redirectUri)
    {
        redirectUri = null;
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            ShowMessage("Client ID を入力してください（手順 2）。開発者ページで作成したアプリの詳細に表示されています。", isError: true);
            return false;
        }

        if (requireSecret && string.IsNullOrWhiteSpace(ClientSecret))
        {
            ShowMessage("Client Secret を入力してください（手順 2）。開発者ページで作成したアプリの詳細に表示されています。", isError: true);
            return false;
        }

        if (!LoopbackAuthorizationListener.TryValidateRedirectUri(RedirectUri, out redirectUri, out var error))
        {
            IsAdvancedVisible = true;
            ShowMessage(error!, isError: true);
            return false;
        }

        return true;
    }

    private void ApplyInputs()
    {
        // 自分で登録したアプリの Client ID が変わった場合、そのアプリで取得したトークンは使えない
        if (ShowRegistrationSteps
            && !string.Equals(_settings.ClientId, ClientId.Trim(), StringComparison.Ordinal)
            && (_settings.TokenClientId ?? _settings.ClientId) == _settings.ClientId)
        {
            _settings.Token = null;
            _settings.TokenClientId = null;
        }

        _settings.ClientId = ClientId.Trim();
        _settings.ClientSecret = ClientSecret.Trim();
        _settings.RedirectUri = RedirectUri.Trim();
        RefreshConnectionState();
    }

    private void ShowMessage(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}

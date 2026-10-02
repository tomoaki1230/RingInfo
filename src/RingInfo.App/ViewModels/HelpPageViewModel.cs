using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RingInfo.App.Services;

namespace RingInfo.App.ViewModels;

/// <summary>使用しているライブラリとそのライセンス</summary>
/// <param name="Name">ライブラリ名</param>
/// <param name="Version">バージョン</param>
/// <param name="LicenseName">ライセンスの種類</param>
/// <param name="ResourceName">ライセンス全文の埋め込みリソース名（Licenses フォルダーのファイル名）</param>
/// <param name="Note">補足</param>
public sealed record LibraryLicense(string Name, string Version, string LicenseName, string ResourceName, string? Note = null);

/// <summary>ヘルプページ: バージョン・製作者・著作権・商標・使い方の概要・ライセンス</summary>
public sealed partial class HelpPageViewModel : PageViewModel
{
    public const string AuthorName = "Tomoaki Bessho";
    /// <summary>製作者の GitHub プロフィール（ヘルプの「GitHub」リンク）</summary>
    public const string AuthorUrl = "https://github.com/tomoaki1230";

    /// <summary>Oura メンバーシップについての注意（設定画面・ヘルプで共通）</summary>
    public const string MembershipNoticeText = "有効な Oura メンバーシップに加入していないユーザーのデータは、Oura API 経由で取得できません（Oura の仕様）。メンバーシップが有効でない場合、連携はできてもデータは表示されません。";

    /// <summary>Oura の商標についての表記</summary>
    public const string TrademarkNotice = "Oura製品に関する「OURA」「OURARING」などの名称やロゴは、開発元である Oura Health Oy の登録商標です。";

    public HelpPageViewModel(string? settingsFilePath = null, string? errorLogPath = null)
    {
        // ユーザー名が写らないよう %APPDATA% などの書き方で表示する
        SettingsFilePath = settingsFilePath is null ? "（保存しないモード）" : Services.PathDisplay.ToDisplay(settingsFilePath);
        ErrorLogPath = Services.PathDisplay.ToDisplay(errorLogPath);

        var assembly = typeof(HelpPageViewModel).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "不明";
        // ビルド情報（+コミットハッシュ）が付いている場合は除く
        Version = version.Split('+')[0];
        Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? $"Copyright (c) {AuthorName}";
    }

    public override string Title => "ヘルプ";

    public override string IconGlyph => "";

    public string AppName => "RingInfo";

    public string Description => "Oura Ring のデータを Windows PC で見るためのアプリ";

    public string Version { get; }

    public string Author => AuthorName;

    public string Copyright { get; }

    public string Trademark => TrademarkNotice;

    public string MembershipNotice => MembershipNoticeText;

    public string SettingsFilePath { get; }

    public string ErrorLogPath { get; }

    /// <summary>使用しているライブラリ（いずれも MIT License）</summary>
    public IReadOnlyList<LibraryLicense> Libraries { get; } =
    [
        new("CommunityToolkit.Mvvm", "8.4.2", "MIT License", "CommunityToolkit.Mvvm"),
        new("OxyPlot（OxyPlot.Wpf / OxyPlot.Core）", "2.2.0", "MIT License", "OxyPlot"),
        new("System.Security.Cryptography.ProtectedData", "8.0.0", "MIT License", "DotNet"),
        new(".NET ランタイム / WPF", Environment.Version.ToString(), "MIT License", "DotNet", "ランタイム同梱版に含まれる"),
    ];

    /// <summary>全文を表示中のライセンス（無ければ null）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLicenseVisible), nameof(LicenseTitle))]
    private LibraryLicense? _selectedLicense;

    [ObservableProperty]
    private string _licenseText = "";

    public bool IsLicenseVisible => SelectedLicense is not null;

    public string LicenseTitle => SelectedLicense is { } license ? $"{license.Name} — {license.LicenseName}" : "";

    /// <summary>ライセンスの全文を表示する（同じものをもう一度押すと閉じる）</summary>
    [RelayCommand]
    private void ShowLicense(LibraryLicense? license)
    {
        if (license is null || license == SelectedLicense)
        {
            CloseLicense();
            return;
        }

        LicenseText = LoadLicenseText(license.ResourceName);
        SelectedLicense = license;
    }

    [RelayCommand]
    private void CloseLicense()
    {
        SelectedLicense = null;
        LicenseText = "";
    }

    /// <summary>埋め込みリソースからライセンス全文を読み込む</summary>
    internal static string LoadLicenseText(string resourceName)
    {
        using var stream = typeof(HelpPageViewModel).Assembly.GetManifestResourceStream($"RingInfo.Licenses.{resourceName}.txt");
        if (stream is null)
        {
            return "ライセンスの全文を読み込めませんでした。";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>使い方の要点</summary>
    public IReadOnlyList<InfoRow> Tips { get; } =
    [
        new("はじめに", "「設定」で Oura と連携すると、あなたのデータが表示されます（連携前はデモデータ）。"),
        new("期間", "画面右上で 7 / 14 / 30 / 90 日を選び、◀ ▶ で前後の期間に移動できます。"),
        new("日付", "各ページの日付を押すとカレンダーが開き、取得期間内の日を選べます。日別の表の行を選んでも切り替わります。"),
        new("グラフ", "グラフにマウスを乗せると、その時点・その日の値が表示されます。"),
        new("更新", "期間を切り替えたときは、5 分以内に読み込んだデータを再利用します。「更新」を押すと Oura から最新のデータを取得します。睡眠のデータは Oura アプリと同期した後に反映されます。"),
        new("テーマ", "「設定」の「画面のテーマ」で、システムに合わせる／ライト／ダークを選べます。"),
    ];

    [RelayCommand]
    private void OpenAuthorPage() => ShellLauncher.Open(AuthorUrl);
}

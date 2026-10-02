using RingInfo.Core.Auth;

namespace RingInfo.Core.Settings;

/// <summary>画面のテーマの選択</summary>
public enum ThemePreference
{
    /// <summary>Windows の設定（アプリ モード）に合わせる</summary>
    System,
    Light,
    Dark,
}

/// <summary>アプリの設定（メモリ上では平文。保存時に機密情報を暗号化する）</summary>
public sealed class AppSettings
{
    public const string DefaultRedirectUri = "http://localhost:8765/callback";
    public const int DefaultRangeDays = 14;

    /// <summary>Oura に登録した API アプリの Client ID</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Oura に登録した API アプリの Client Secret</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>OAuth のリダイレクト URI（Oura のアプリ設定と完全一致させる）</summary>
    public string RedirectUri { get; set; } = DefaultRedirectUri;

    /// <summary>取得済みのトークン（未連携なら null）</summary>
    public OAuthToken? Token { get; set; }

    /// <summary>トークンを取得したときの Client ID（古い設定では null）</summary>
    public string? TokenClientId { get; set; }

    /// <summary>配布版でも、利用者が自分で登録した API アプリ（ClientId / ClientSecret）を使う</summary>
    public bool UseCustomClient { get; set; }

    /// <summary>画面のテーマ</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>表示期間（日数）</summary>
    public int RangeDays { get; set; } = DefaultRangeDays;

    /// <summary>自分で登録したアプリで連携済みか（組み込みのアプリも考慮する場合は OuraClientSelector を使う）</summary>
    public bool IsConnected => OuraClientSelector.IsConnected(this, builtIn: null);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

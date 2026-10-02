namespace RingInfo.Core.Auth;

/// <summary>OAuth2 のトークン</summary>
/// <param name="AccessToken">アクセストークン</param>
/// <param name="RefreshToken">リフレッシュトークン（1 回限り有効）</param>
/// <param name="ExpiresAt">アクセストークンの有効期限</param>
/// <param name="Scope">許可されたスコープ（スペース区切り）</param>
public sealed record OAuthToken(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string? Scope)
{
    /// <summary>有効期限切れ（または期限間近）かどうか</summary>
    public bool IsExpired(DateTimeOffset now, TimeSpan margin) => ExpiresAt is { } expiresAt && now + margin >= expiresAt;
}

/// <summary>Oura に登録した API アプリケーションの情報</summary>
public sealed record OAuthClientCredentials(string ClientId, string ClientSecret, Uri RedirectUri);

/// <summary>OAuth の処理に失敗した場合の例外（メッセージは利用者向けの日本語）</summary>
/// <param name="requiresReauthorization">
/// 再連携が必要か。リフレッシュトークンや Client ID/Secret が無効な場合は true。
/// 通信エラー・タイムアウト・サーバーの一時的なエラーは false（時間をおけば回復する）。
/// </param>
public sealed class OAuthException(string message, string? errorCode = null, Exception? innerException = null, bool requiresReauthorization = false)
    : Exception(message, innerException)
{
    /// <summary>OAuth のエラーコード（invalid_grant など）</summary>
    public string? ErrorCode { get; } = errorCode;

    public bool RequiresReauthorization { get; } = requiresReauthorization;
}

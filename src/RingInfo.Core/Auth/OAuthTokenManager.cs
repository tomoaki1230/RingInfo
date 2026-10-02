using RingInfo.Core.Api;

namespace RingInfo.Core.Auth;

/// <summary>
/// OAuth トークンを提供し、期限切れや 401 の際にリフレッシュトークンで自動更新する。
/// トークンは <see cref="TokenStore"/> に保管し、作り直された他の OAuthTokenManager と共有する
/// （リフレッシュトークンは 1 回限り有効なため、更新処理は保管場所単位で排他する）。
/// </summary>
public sealed class OAuthTokenManager : IAccessTokenProvider
{
    /// <summary>期限切れとみなす余裕時間</summary>
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(2);

    private readonly OuraOAuthClient _oauthClient;
    private readonly OAuthClientCredentials _credentials;
    private readonly TokenStore _store;
    private readonly TimeProvider _timeProvider;

    public OAuthTokenManager(OuraOAuthClient oauthClient, OAuthClientCredentials credentials, TokenStore store, TimeProvider? timeProvider = null)
    {
        _oauthClient = oauthClient;
        _credentials = credentials;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public OAuthTokenManager(OuraOAuthClient oauthClient, OAuthClientCredentials credentials, OAuthToken initialToken, TimeProvider? timeProvider = null)
        : this(oauthClient, credentials, new TokenStore(initialToken), timeProvider)
    {
    }

    /// <summary>トークンが更新されたとき（保存用）。任意のスレッドから呼ばれる。</summary>
    public event EventHandler<OAuthToken>? TokenRefreshed
    {
        add => _store.TokenRefreshed += value;
        remove => _store.TokenRefreshed -= value;
    }

    public OAuthToken? CurrentToken => _store.Token;

    public async Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        await _store.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var token = _store.Token
                ?? throw new OuraAuthenticationRequiredException("Oura と連携していません。設定画面から連携してください。");
            var generation = _store.Generation;

            var rejected = rejectedToken is not null && rejectedToken == token.AccessToken;
            if (!rejected && !token.IsExpired(_timeProvider.GetUtcNow(), ExpiryMargin))
            {
                // 他の呼び出し（他の OAuthTokenManager を含む）で既に更新済みの場合もここで新しいトークンを返す
                return token.AccessToken;
            }

            if (string.IsNullOrEmpty(token.RefreshToken))
            {
                throw new OuraAuthenticationRequiredException("アクセストークンの有効期限が切れました。設定画面から再度連携してください。");
            }

            OAuthToken refreshed;
            try
            {
                refreshed = await _oauthClient.RefreshAsync(_credentials, token.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (OAuthException ex) when (ex.RequiresReauthorization)
            {
                throw new OuraAuthenticationRequiredException($"トークンを更新できませんでした。設定画面から再度連携してください。（{ex.Message}）", ex);
            }
            catch (OAuthException ex)
            {
                // 通信エラーや一時的なエラー: 再連携は不要（時間をおいて更新すれば回復する）
                throw new OuraApiException(ex.Message, innerException: ex);
            }

            _store.TryUpdate(generation, refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            _store.Gate.Release();
        }
    }
}

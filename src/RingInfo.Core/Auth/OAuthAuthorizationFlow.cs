namespace RingInfo.Core.Auth;

/// <summary>
/// 「Oura と連携」ボタンの一連の処理。
/// ローカルリスナー起動 → ブラウザで認可ページを開く → リダイレクト受信 → トークン交換。
/// </summary>
public sealed class OAuthAuthorizationFlow(OuraOAuthClient oauthClient)
{
    /// <summary>ブラウザでの操作を待つ最大時間</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <param name="credentials">登録したアプリの情報</param>
    /// <param name="openBrowser">認可 URL をブラウザで開く処理</param>
    public async Task<OAuthToken> AuthorizeAsync(OAuthClientCredentials credentials, Action<Uri> openBrowser, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var state = Pkce.CreateState();
        var codeVerifier = Pkce.CreateCodeVerifier();
        var authorizationUri = OuraOAuthClient.BuildAuthorizationUri(
            credentials.ClientId, credentials.RedirectUri, OuraOAuthClient.DefaultScopes, state, Pkce.CreateCodeChallenge(codeVerifier));

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);

        // state が一致しない応答（以前の連携で開いた古いタブなど）は無視して待ち続ける
        var callbackTask = LoopbackAuthorizationListener.StartAsync(credentials.RedirectUri, timeoutSource.Token, c => c.State == state);
        try
        {
            openBrowser(authorizationUri);
        }
        catch
        {
            // ブラウザを開けなかった場合は待ち受けを終了してポートを解放する（5 分間ふさがったままにしない）
            timeoutSource.Cancel();
            try
            {
                await callbackTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }

        AuthorizationCallback callback;
        try
        {
            callback = await callbackTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OAuthException("ブラウザでの認可が時間内に完了しませんでした。もう一度お試しください。");
        }

        if (!string.IsNullOrEmpty(callback.Error))
        {
            throw new OAuthException(callback.Error == "access_denied"
                ? "Oura でのアクセス許可が拒否されました。"
                : $"Oura から認可エラーが返されました（{callback.Error}）。", callback.Error);
        }

        if (callback.State != state)
        {
            throw new OAuthException("認可応答の state が一致しません。もう一度お試しください。");
        }

        if (string.IsNullOrEmpty(callback.Code))
        {
            throw new OAuthException("認可コードを受け取れませんでした。");
        }

        return await oauthClient.ExchangeCodeAsync(credentials, callback.Code, codeVerifier, cancellationToken).ConfigureAwait(false);
    }
}

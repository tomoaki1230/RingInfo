using System.Net;
using System.Text;

namespace RingInfo.Core.Auth;

/// <summary>
/// ローカル（http://localhost:ポート）で認可後のリダイレクトを受け取る簡易 HTTP サーバー。
/// </summary>
public static class LoopbackAuthorizationListener
{
    /// <summary>リダイレクト URI がこのリスナーで受信できる形式か検証する</summary>
    public static bool TryValidateRedirectUri(string? value, out Uri? redirectUri, out string? error)
    {
        redirectUri = null;
        error = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            error = "リダイレクト URI の形式が正しくありません。例: http://localhost:8765/callback";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)
        {
            error = "リダイレクト URI は http://localhost:ポート番号/パス の形式で指定してください。";
            return false;
        }

        if (uri.IsDefaultPort)
        {
            error = "リダイレクト URI にはポート番号を指定してください（例: 8765）。";
            return false;
        }

        redirectUri = uri;
        return true;
    }

    /// <summary>
    /// リスナーを開始する。ブラウザを開く前に呼び出し、返された Task を待機すること。
    /// </summary>
    /// <param name="accept">
    /// 受け付ける応答の条件（state の一致など）。条件に合わない応答（以前の連携で開いた古いタブなど）は
    /// 「このページは古い」と返して待機を続ける。省略時はすべて受け付ける。
    /// </param>
    /// <exception cref="OAuthException">ポートが使用中などで待ち受けできない場合</exception>
    public static Task<AuthorizationCallback> StartAsync(Uri redirectUri, CancellationToken cancellationToken, Func<AuthorizationCallback, bool>? accept = null)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{redirectUri.Host}:{redirectUri.Port}/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            ((IDisposable)listener).Dispose();
            throw new OAuthException($"ポート {redirectUri.Port} で待ち受けできませんでした。他のアプリが使用していないか確認してください。", innerException: ex);
        }

        return WaitAsync(listener, redirectUri, accept, cancellationToken);
    }

    private static async Task<AuthorizationCallback> WaitAsync(HttpListener listener, Uri redirectUri, Func<AuthorizationCallback, bool>? accept, CancellationToken cancellationToken)
    {
        using var _ = listener;
        await using var registration = cancellationToken.Register(listener.Stop);

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("連携がキャンセルされました。", ex, cancellationToken);
            }

            var requestUri = context.Request.Url;
            if (requestUri is null || !IsCallbackPath(requestUri, redirectUri))
            {
                // favicon などの無関係なリクエストは 404 を返して待機を続ける
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            var callback = AuthorizationCallback.Parse(requestUri);
            if (accept is not null && !accept(callback))
            {
                await WriteResponseAsync(context.Response, CallbackPage.Stale).ConfigureAwait(false);
                continue;
            }

            await WriteResponseAsync(context.Response, callback.IsSuccess ? CallbackPage.Success : CallbackPage.Failure).ConfigureAwait(false);
            return callback;
        }
    }

    internal static bool IsCallbackPath(Uri requestUri, Uri redirectUri)
        => string.Equals(requestUri.AbsolutePath.TrimEnd('/'), redirectUri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private enum CallbackPage
    {
        Success,
        Failure,
        Stale,
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, CallbackPage page)
    {
        var (title, message) = page switch
        {
            CallbackPage.Success => ("連携しました", "RingInfo と Oura の連携が完了しました。このタブを閉じてアプリに戻ってください。"),
            CallbackPage.Stale => ("古いページです", "以前の連携で開いたページからの応答のため、無視しました。このタブを閉じ、最新のタブで操作してください。"),
            _ => ("連携できませんでした", "認可が拒否されたか、エラーが発生しました。アプリに戻ってもう一度お試しください。"),
        };
        var html = $$"""
            <!doctype html>
            <html lang="ja"><head><meta charset="utf-8"><title>RingInfo - {{title}}</title>
            <style>body{font-family:system-ui,"Segoe UI","Yu Gothic UI",sans-serif;background:#0d0d0d;color:#fff;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}
            main{background:#1a1a19;padding:32px 40px;border-radius:12px;max-width:480px}h1{font-size:20px;margin:0 0 12px}p{color:#c3c2b7;line-height:1.6;margin:0}</style>
            </head><body><main><h1>{{title}}</h1><p>{{message}}</p></main></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }
}

using System.Net;

namespace RingInfo.Core.Api;

/// <summary>Oura API がエラーを返した場合の例外（メッセージは利用者向けの日本語）</summary>
public class OuraApiException : Exception
{
    public OuraApiException(string message, HttpStatusCode? statusCode = null, string? responseBody = null, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ResponseBody { get; }

    /// <summary>429 の場合に再試行までの待ち時間</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>認証エラー（トークンが無効で再連携が必要）かどうか。スコープ不足は含まない。</summary>
    public bool IsAuthenticationError => StatusCode == HttpStatusCode.Unauthorized && !IsScopeError;

    /// <summary>トークンは有効だが、そのデータのスコープが許可されていない（401 + "not authorized access ... scope"）</summary>
    public bool IsScopeError { get; init; }

    /// <summary>401 の応答本文がスコープ不足を示しているか</summary>
    internal static bool IsScopeErrorBody(string? body)
        => body is not null && body.Contains("scope", StringComparison.OrdinalIgnoreCase) && body.Contains("not authorized", StringComparison.OrdinalIgnoreCase);

    internal static async Task<OuraApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await SafeReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        var status = response.StatusCode;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;

        if (status == HttpStatusCode.Unauthorized && IsScopeErrorBody(body))
        {
            return new OuraApiException("このデータへのアクセスが許可されていません（スコープ不足）。設定画面から再度連携してください。", status, body)
            {
                IsScopeError = true,
            };
        }

        var message = status switch
        {
            HttpStatusCode.BadRequest => "リクエストの内容が正しくありません（400）。",
            HttpStatusCode.Unauthorized => "認証の有効期限が切れたか、トークンが無効です（401）。設定画面から再度連携してください。",
            HttpStatusCode.Forbidden => "Oura API がアクセスを拒否しました（403）。有効な Oura メンバーシップに加入していない場合、API 経由でデータを取得できません。連携時にこのデータの共有を許可していない可能性もあります。",
            HttpStatusCode.NotFound => "データが見つかりません（404）。",
            HttpStatusCode.TooManyRequests => retryAfter is { } wait
                ? $"API のリクエスト上限に達しました（429）。{Math.Ceiling(wait.TotalSeconds)} 秒ほど待ってから再試行してください。"
                : "API のリクエスト上限に達しました（429）。しばらく待ってから再試行してください。",
            >= HttpStatusCode.InternalServerError => $"Oura のサーバーでエラーが発生しました（{(int)status}）。時間をおいて再試行してください。",
            _ => $"Oura API がエラーを返しました（{(int)status}）。",
        };

        return new OuraApiException(message, status, body, retryAfter);
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

/// <summary>Oura との連携（再認証）が必要な場合の例外</summary>
public sealed class OuraAuthenticationRequiredException(string message, Exception? innerException = null)
    : Exception(message, innerException);

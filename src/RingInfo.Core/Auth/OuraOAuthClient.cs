using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingInfo.Core.Auth;

/// <summary>
/// Oura の OAuth2（認可コードフロー + PKCE）クライアント。
/// 仕様: https://cloud.ouraring.com/docs/authentication
/// </summary>
public sealed class OuraOAuthClient(HttpClient httpClient, TimeProvider? timeProvider = null)
{
    public static readonly Uri AuthorizeEndpoint = new("https://cloud.ouraring.com/oauth/authorize");
    public static readonly Uri TokenEndpoint = new("https://api.ouraring.com/oauth/token");

    /// <summary>このアプリが要求するスコープ（ring_configuration はリング情報・電池残量に必要）</summary>
    public static readonly IReadOnlyList<string> DefaultScopes =
        ["email", "personal", "daily", "heartrate", "workout", "tag", "session", "spo2", "ring_configuration"];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// ブラウザで開く認可 URL を組み立てる。
    /// redirect_uri は Oura に登録した値と完全一致が必要なため、入力どおりの文字列（OriginalString）を使う。
    /// </summary>
    public static Uri BuildAuthorizationUri(string clientId, Uri redirectUri, IEnumerable<string> scopes, string state, string codeChallenge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var parameters = new (string Key, string Value)[]
        {
            ("response_type", "code"),
            ("client_id", clientId.Trim()),
            ("redirect_uri", redirectUri.OriginalString),
            ("scope", string.Join(' ', scopes)),
            ("state", state),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"),
        };
        var query = string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return new Uri($"{AuthorizeEndpoint}?{query}");
    }

    /// <summary>認可コードをアクセストークンに交換する</summary>
    public Task<OAuthToken> ExchangeCodeAsync(OAuthClientCredentials credentials, string code, string codeVerifier, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = credentials.RedirectUri.OriginalString,
            ["client_id"] = credentials.ClientId.Trim(),
            ["client_secret"] = credentials.ClientSecret.Trim(),
            ["code_verifier"] = codeVerifier,
        };
        return RequestTokenAsync(form, previousRefreshToken: null, cancellationToken);
    }

    /// <summary>リフレッシュトークンで新しいトークンを取得する（リフレッシュトークンは 1 回限り有効）</summary>
    public Task<OAuthToken> RefreshAsync(OAuthClientCredentials credentials, string refreshToken, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = credentials.ClientId.Trim(),
            ["client_secret"] = credentials.ClientSecret.Trim(),
        };
        return RequestTokenAsync(form, refreshToken, cancellationToken);
    }

    private async Task<OAuthToken> RequestTokenAsync(Dictionary<string, string> form, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthException("Oura の認証サーバーに接続できませんでした。ネットワーク接続を確認してください。", innerException: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OAuthException("Oura の認証サーバーからの応答がタイムアウトしました。時間をおいて再試行してください。", innerException: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateError(response, body);
            }

            TokenResponse? token;
            try
            {
                token = JsonSerializer.Deserialize<TokenResponse>(body);
            }
            catch (JsonException ex)
            {
                throw new OAuthException("認証サーバーの応答を解析できませんでした。", innerException: ex);
            }

            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new OAuthException("認証サーバーの応答にアクセストークンが含まれていません。");
            }

            DateTimeOffset? expiresAt = token.ExpiresIn is > 0
                ? _timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn.Value)
                : null;

            // 応答にリフレッシュトークンが無い場合は従来のものを保持する
            var refreshToken = string.IsNullOrEmpty(token.RefreshToken) ? previousRefreshToken : token.RefreshToken;
            return new OAuthToken(token.AccessToken, refreshToken, expiresAt, token.Scope);
        }
    }

    private static OAuthException CreateError(HttpResponseMessage response, string body)
    {
        string? errorCode = null;
        string? description = null;
        try
        {
            var error = JsonSerializer.Deserialize<TokenErrorResponse>(body);
            errorCode = error?.Error;
            description = error?.ErrorDescription ?? error?.Detail;
        }
        catch (JsonException)
        {
            // JSON 以外のエラー応答はステータスコードのみで判断する
        }

        var status = (int)response.StatusCode;
        var message = errorCode switch
        {
            "invalid_client" => "Client ID または Client Secret が正しくありません。",
            "invalid_grant" => "認可コードまたはリフレッシュトークンが無効です。もう一度連携してください。",
            "invalid_request" => "認証リクエストが正しくありません。リダイレクト URI の設定を確認してください。",
            _ when status >= 500 => $"Oura の認証サーバーで一時的なエラーが発生しました（{status}）。時間をおいて再試行してください。",
            _ when status == 429 => "認証サーバーのリクエスト上限に達しました（429）。しばらく待ってから再試行してください。",
            _ => $"トークンの取得に失敗しました（{status}）。",
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            message += $" 詳細: {description}";
        }

        // 4xx（429 を除く）はトークンや認証情報の問題なので再連携が必要。5xx・429 は一時的なもの
        var requiresReauthorization = status is >= 400 and < 500 && status != 429;
        return new OAuthException(message, errorCode, requiresReauthorization: requiresReauthorization);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long? ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class TokenErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }
}

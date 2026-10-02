using System.Net;
using RingInfo.Core.Auth;
using RingInfo.Core.Tests.Helpers;

namespace RingInfo.Core.Tests.Auth;

public class OuraOAuthClientTests
{
    private static readonly OAuthClientCredentials Credentials = new("client-1", "secret-1", new Uri("http://localhost:8765/callback"));
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 認可URLに必要なパラメーターが含まれる()
    {
        var uri = OuraOAuthClient.BuildAuthorizationUri("client-1", Credentials.RedirectUri, ["daily", "heartrate"], "state-1", "challenge-1");
        var query = AuthorizationCallback.ParseQuery(uri.Query);

        Assert.StartsWith("https://cloud.ouraring.com/oauth/authorize?", uri.ToString());
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-1", query["client_id"]);
        Assert.Equal("http://localhost:8765/callback", query["redirect_uri"]);
        Assert.Equal("daily heartrate", query["scope"]);
        Assert.Equal("state-1", query["state"]);
        Assert.Equal("challenge-1", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
    }

    [Fact]
    public async Task 認可コードをトークンに交換できる()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""
            {"token_type":"bearer","access_token":"at-1","expires_in":86400,"refresh_token":"rt-1","scope":"daily heartrate"}
            """));
        var client = new OuraOAuthClient(new HttpClient(handler), new FakeTimeProvider(Now));

        var token = await client.ExchangeCodeAsync(Credentials, "code-1", "verifier-1");

        Assert.Equal("at-1", token.AccessToken);
        Assert.Equal("rt-1", token.RefreshToken);
        Assert.Equal(Now.AddDays(1), token.ExpiresAt);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.ouraring.com/oauth/token", request.Uri.ToString());
        Assert.Equal("authorization_code", request.Form("grant_type"));
        Assert.Equal("code-1", request.Form("code"));
        Assert.Equal("verifier-1", request.Form("code_verifier"));
        Assert.Equal("client-1", request.Form("client_id"));
        Assert.Equal("secret-1", request.Form("client_secret"));
        Assert.Equal("http://localhost:8765/callback", request.Form("redirect_uri"));
    }

    [Fact]
    public async Task リフレッシュ応答に新しいリフレッシュトークンが無ければ従来のものを保持する()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""{"access_token":"at-2","expires_in":"3600"}"""));
        var client = new OuraOAuthClient(new HttpClient(handler), new FakeTimeProvider(Now));

        var token = await client.RefreshAsync(Credentials, "rt-1");

        Assert.Equal("at-2", token.AccessToken);
        Assert.Equal("rt-1", token.RefreshToken);
        Assert.Equal(Now.AddHours(1), token.ExpiresAt);
        Assert.Equal("refresh_token", handler.Requests[0].Form("grant_type"));
        Assert.Equal("rt-1", handler.Requests[0].Form("refresh_token"));
    }

    [Fact]
    public async Task エラー応答は日本語メッセージの例外になる()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            """{"error":"invalid_grant","error_description":"expired"}""", HttpStatusCode.BadRequest));
        var client = new OuraOAuthClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshAsync(Credentials, "rt-1"));

        Assert.Equal("invalid_grant", ex.ErrorCode);
        Assert.Contains("無効", ex.Message);
        Assert.Contains("expired", ex.Message);
    }
}

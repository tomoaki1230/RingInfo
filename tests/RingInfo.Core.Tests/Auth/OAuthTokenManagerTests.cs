using RingInfo.Core.Api;
using RingInfo.Core.Auth;
using RingInfo.Core.Tests.Helpers;

namespace RingInfo.Core.Tests.Auth;

public class OAuthTokenManagerTests
{
    private static readonly OAuthClientCredentials Credentials = new("client-1", "secret-1", new Uri("http://localhost:8765/callback"));
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static (OAuthTokenManager Manager, FakeHttpMessageHandler Handler) Create(OAuthToken token)
    {
        var counter = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var n = Interlocked.Increment(ref counter);
            return FakeHttpMessageHandler.Json($$"""{"access_token":"at-new{{n}}","refresh_token":"rt-new{{n}}","expires_in":86400}""");
        });
        var time = new FakeTimeProvider(Now);
        var manager = new OAuthTokenManager(new OuraOAuthClient(new HttpClient(handler), time), Credentials, token, time);
        return (manager, handler);
    }

    [Fact]
    public async Task 有効なトークンはそのまま返す()
    {
        var (manager, handler) = Create(new OAuthToken("at-1", "rt-1", Now.AddHours(1), null));

        Assert.Equal("at-1", await manager.GetAccessTokenAsync(null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task 期限切れ間近ならリフレッシュして保存イベントを発行する()
    {
        var (manager, handler) = Create(new OAuthToken("at-1", "rt-1", Now.AddSeconds(30), null));
        OAuthToken? saved = null;
        manager.TokenRefreshed += (_, token) => saved = token;

        var token = await manager.GetAccessTokenAsync(null, CancellationToken.None);

        Assert.Equal("at-new1", token);
        Assert.Equal("rt-new1", saved?.RefreshToken);
        Assert.Equal("rt-1", handler.Requests[0].Form("refresh_token"));
    }

    [Fact]
    public async Task 同時に拒否されてもリフレッシュは1回だけ行う()
    {
        var (manager, handler) = Create(new OAuthToken("at-1", "rt-1", Now.AddHours(1), null));

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => manager.GetAccessTokenAsync("at-1", CancellationToken.None)));

        Assert.All(results, t => Assert.Equal("at-new1", t));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task リフレッシュトークンが無ければ再連携が必要()
    {
        var (manager, _) = Create(new OAuthToken("at-1", null, Now.AddSeconds(-1), null));

        await Assert.ThrowsAsync<OuraAuthenticationRequiredException>(() => manager.GetAccessTokenAsync(null, CancellationToken.None));
    }
}

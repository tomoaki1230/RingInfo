using System.Net;
using System.Net.Sockets;
using RingInfo.Core.Api;
using RingInfo.Core.Auth;
using RingInfo.Core.Tests.Helpers;

namespace RingInfo.Core.Tests.Api;

/// <summary>通信まわりの見直しで見つかった不具合の再現・回帰テスト</summary>
public class NetworkRobustnessTests
{
    private static readonly OAuthClientCredentials Credentials = new("client-1", "secret-1", new Uri("http://localhost:8765/callback"));
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- トークン更新

    [Fact]
    public async Task トークン更新時の通信エラーは再連携を求めず通信エラーとして扱う()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("offline"));
        var time = new FakeTimeProvider(Now);
        var manager = new OAuthTokenManager(new OuraOAuthClient(new HttpClient(handler), time), Credentials, new OAuthToken("at", "rt", Now.AddSeconds(-1), null), time);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => manager.GetAccessTokenAsync(null, CancellationToken.None));

        Assert.IsNotType<OuraAuthenticationRequiredException>(ex);
        var api = Assert.IsType<OuraApiException>(ex);
        Assert.Contains("ネットワーク", api.Message);
    }

    [Fact]
    public async Task トークン更新時のサーバーエラーは再連携を求めない()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var time = new FakeTimeProvider(Now);
        var manager = new OAuthTokenManager(new OuraOAuthClient(new HttpClient(handler), time), Credentials, new OAuthToken("at", "rt", Now.AddSeconds(-1), null), time);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => manager.GetAccessTokenAsync(null, CancellationToken.None));

        Assert.IsNotType<OuraAuthenticationRequiredException>(ex);
    }

    [Fact]
    public async Task リフレッシュトークンが無効なら再連携を求める()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest));
        var time = new FakeTimeProvider(Now);
        var manager = new OAuthTokenManager(new OuraOAuthClient(new HttpClient(handler), time), Credentials, new OAuthToken("at", "rt", Now.AddSeconds(-1), null), time);

        await Assert.ThrowsAsync<OuraAuthenticationRequiredException>(() => manager.GetAccessTokenAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task 認証サーバーのタイムアウトは日本語メッセージの例外になる()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new TaskCanceledException("timeout"));
        var client = new OuraOAuthClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => client.RefreshAsync(Credentials, "rt"));

        Assert.Contains("タイムアウト", ex.Message);
    }

    [Fact]
    public async Task 複数の取得元が同じ保存先を共有すると_更新済みのトークンを使い回し二重に更新しない()
    {
        var refreshCount = 0;
        var handler = new FakeHttpMessageHandler((_, body) =>
        {
            Interlocked.Increment(ref refreshCount);
            return body!.Contains("refresh_token=rt-1")
                ? FakeHttpMessageHandler.Json("""{"access_token":"at-2","refresh_token":"rt-2","expires_in":86400}""")
                : FakeHttpMessageHandler.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        });
        var time = new FakeTimeProvider(Now);
        var store = new TokenStore(new OAuthToken("at-1", "rt-1", Now.AddSeconds(-1), null));
        var oauth = new OuraOAuthClient(new HttpClient(handler), time);
        var first = new OAuthTokenManager(oauth, Credentials, store, time);
        var second = new OAuthTokenManager(oauth, Credentials, store, time);

        Assert.Equal("at-2", await first.GetAccessTokenAsync(null, CancellationToken.None));
        Assert.Equal("at-2", await second.GetAccessTokenAsync(null, CancellationToken.None));
        Assert.Equal(1, refreshCount);
    }

    // ---------------------------------------------------------------- API 呼び出し

    [Fact]
    public async Task 同じ_next_token_が返り続けても無限に取得しない()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""{"data":[],"next_token":"same"}"""));
        var client = new OuraApiClient(new HttpClient(handler), new StaticAccessTokenProvider("t"));

        await client.GetRingConfigurationsAsync();

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task リクエスト上限は待ち時間が短ければ待って1回だけ再試行する()
    {
        var calls = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                return limited;
            }

            return FakeHttpMessageHandler.Json("""{"id":"u1"}""");
        });
        var client = new OuraApiClient(new HttpClient(handler), new StaticAccessTokenProvider("t"));

        var info = await client.GetPersonalInfoAsync();

        Assert.Equal("u1", info?.Id);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task 整数の項目が小数で返っても解析できる()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""
            {"data":[{"id":"s","day":"2026-10-01","average_hrv":56.0,"lowest_heart_rate":48.4,"time_in_bed":28800.0,"total_sleep_duration":null,
              "bedtime_start":"2026-09-30T23:00:00+09:00","bedtime_end":"2026-10-01T07:00:00+09:00"}],"next_token":null}
            """));
        var client = new OuraApiClient(new HttpClient(handler), new StaticAccessTokenProvider("t"));

        var periods = await client.GetSleepPeriodsAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));

        var period = Assert.Single(periods);
        Assert.Equal(56, period.AverageHrv);
        Assert.Equal(48, period.LowestHeartRate);
        Assert.Equal(28800, period.TimeInBed);
        Assert.Null(period.TotalSleepDuration);
    }

    [Fact]
    public async Task 同時に送るリクエストは一定数までに抑える()
    {
        var current = 0;
        var max = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref max, now);
            Thread.Sleep(30);
            Interlocked.Decrement(ref current);
            return FakeHttpMessageHandler.Json("""{"data":[],"next_token":null}""");
        });
        var client = new OuraApiClient(new HttpClient(handler), new StaticAccessTokenProvider("t"));

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => client.GetRingConfigurationsAsync())));

        Assert.InRange(max, 1, OuraApiClient.MaxConcurrentRequests);
    }

    // ---------------------------------------------------------------- 認可フロー

    [Fact]
    public async Task ブラウザを開けなかった場合は待ち受けを終了してポートを解放する()
    {
        var credentials = Credentials with { RedirectUri = new Uri($"http://localhost:{GetFreePort()}/callback") };
        var flow = new OAuthAuthorizationFlow(new OuraOAuthClient(new HttpClient()));

        await Assert.ThrowsAnyAsync<Exception>(() => flow.AuthorizeAsync(credentials, _ => throw new System.ComponentModel.Win32Exception("no browser"), CancellationToken.None));

        // 同じポートですぐに待ち受けできる（解放されている）
        using var cts = new CancellationTokenSource();
        var again = LoopbackAuthorizationListener.StartAsync(credentials.RedirectUri, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => again);
    }

    [Fact]
    public async Task 古いタブからの別の_state_の応答は無視して正しい応答を待つ()
    {
        var port = GetFreePort();
        var credentials = Credentials with { RedirectUri = new Uri($"http://localhost:{port}/callback") };
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""{"access_token":"at","refresh_token":"rt","expires_in":3600}"""));
        var flow = new OAuthAuthorizationFlow(new OuraOAuthClient(new HttpClient(handler)));
        string? state = null;

        var task = flow.AuthorizeAsync(credentials, uri => state = AuthorizationCallback.ParseQuery(uri.Query)["state"], CancellationToken.None, TimeSpan.FromSeconds(10));
        using var http = new HttpClient();
        await http.GetAsync($"http://localhost:{port}/callback?code=old&state=stale");
        await http.GetAsync($"http://localhost:{port}/callback?code=new&state={Uri.EscapeDataString(state!)}");
        var token = await task;

        Assert.Equal("at", token.AccessToken);
        Assert.Contains("code=new", handler.Requests.Single().Body);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}

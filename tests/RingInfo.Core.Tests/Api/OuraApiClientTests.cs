using System.Net;
using RingInfo.Core.Api;
using RingInfo.Core.Tests.Helpers;

namespace RingInfo.Core.Tests.Api;

public class OuraApiClientTests
{
    private static OuraApiClient CreateClient(FakeHttpMessageHandler handler, IAccessTokenProvider? tokens = null)
        => new(new HttpClient(handler), tokens ?? new StaticAccessTokenProvider("token-1"));

    [Fact]
    public async Task 日付範囲は終了日の翌日まで要求し_範囲外の日は除外する()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""
            {"data":[
              {"id":"a","day":"2026-09-30","score":70,"contributors":{}},
              {"id":"b","day":"2026-10-01","score":80,"contributors":{}},
              {"id":"c","day":"2026-10-02","score":90,"contributors":{}}
            ],"next_token":null}
            """));
        var client = CreateClient(handler);

        var result = await client.GetDailySleepAsync(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1));

        Assert.Equal(["a", "b"], result.Select(x => x.Id));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v2/usercollection/daily_sleep", request.Uri.AbsolutePath);
        Assert.Equal("2026-09-30", request.Query("start_date"));
        Assert.Equal("2026-10-02", request.Query("end_date"));
        Assert.Equal("Bearer token-1", request.Authorization);
    }

    [Fact]
    public async Task next_token_を辿って全ページを取得する()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
            request.RequestUri!.Query.Contains("next_token=page2")
                ? FakeHttpMessageHandler.Json("""{"data":[{"id":"2","day":"2026-10-01","contributors":{}}],"next_token":null}""")
                : FakeHttpMessageHandler.Json("""{"data":[{"id":"1","day":"2026-09-30","contributors":{}}],"next_token":"page2"}"""));
        var client = CreateClient(handler);

        var result = await client.GetDailyReadinessAsync(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1));

        Assert.Equal(["1", "2"], result.Select(x => x.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("page2", handler.Requests[1].Query("next_token"));
        Assert.Equal("2026-09-30", handler.Requests[1].Query("start_date"));
    }

    [Fact]
    public async Task 心拍数はオフセット付き日時で要求する()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""
            {"data":[
              {"timestamp":"2026-09-30T15:05:00+00:00","bpm":61,"source":"awake"},
              {"timestamp":"2026-09-30T15:00:00+00:00","bpm":60,"source":"awake"}
            ],"next_token":null}
            """));
        var client = CreateClient(handler);
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(9));

        var result = await client.GetHeartRateAsync(start, start.AddDays(1));

        Assert.Equal([60, 61], result.Select(x => x.Bpm));
        Assert.Equal("2026-10-01T00:00:00+09:00", handler.Requests[0].Query("start_datetime"));
        Assert.Equal("2026-10-02T00:00:00+09:00", handler.Requests[0].Query("end_datetime"));
    }

    [Fact]
    public async Task 電池残量は_latest_を指定して最新を返す()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("""
            {"data":[{"timestamp":"2026-10-01T01:00:00Z","level":80},{"timestamp":"2026-10-01T02:00:00Z","level":78}],"next_token":null}
            """));
        var client = CreateClient(handler);

        var result = await client.GetLatestBatteryLevelAsync();

        Assert.Equal(78, result?.Level);
        Assert.Equal("true", handler.Requests[0].Query("latest"));
    }

    [Fact]
    public async Task 認証エラー時はトークンを更新して1回だけ再試行する()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
            request.Headers.Authorization?.Parameter == "old"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : FakeHttpMessageHandler.Json("""{"id":"u1","age":30,"email":"a@example.com"}"""));
        var tokens = new RotatingTokenProvider("old", "new");
        var client = CreateClient(handler, tokens);

        var result = await client.GetPersonalInfoAsync();

        Assert.Equal("a@example.com", result?.Email);
        Assert.Equal(["Bearer old", "Bearer new"], handler.Requests.Select(r => r.Authorization));
        Assert.Equal("old", tokens.LastRejected);
    }

    [Fact]
    public async Task 再試行後も認証エラーなら認証エラーの例外になる()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = CreateClient(handler, new RotatingTokenProvider("old", "new"));

        var ex = await Assert.ThrowsAsync<OuraApiException>(() => client.GetPersonalInfoAsync());

        Assert.True(ex.IsAuthenticationError);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task スコープ不足の401はトークンを更新せず_認証エラーとも区別する()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            """{"detail":"Token is not authorized access ring_configuration scope."}""", HttpStatusCode.Unauthorized));
        var tokens = new RotatingTokenProvider("old", "new");
        var client = CreateClient(handler, tokens);

        var ex = await Assert.ThrowsAsync<OuraApiException>(() => client.GetRingConfigurationsAsync());

        Assert.True(ex.IsScopeError);
        Assert.False(ex.IsAuthenticationError);
        Assert.Single(handler.Requests);
        Assert.Null(tokens.LastRejected);
    }

    [Fact]
    public async Task リクエスト上限では待ち時間を含むメッセージになる()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<OuraApiException>(() => client.GetDailyActivityAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
        Assert.Contains("30 秒", ex.Message);
    }

    [Fact]
    public async Task アクセス拒否ではメンバーシップが原因の可能性を案内する()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<OuraApiException>(() => client.GetDailySleepAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));

        Assert.Contains("メンバーシップ", ex.Message);
        Assert.False(ex.IsAuthenticationError);
    }

    [Fact]
    public async Task 通信エラーは利用者向けメッセージの例外になる()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("network down"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<OuraApiException>(() => client.GetRingConfigurationsAsync());

        Assert.Contains("ネットワーク", ex.Message);
    }

    [Fact]
    public async Task 終了日が開始日より前なら引数エラー()
    {
        var client = CreateClient(new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json("{}")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetDailySleepAsync(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)));
    }

    /// <summary>拒否されたら次のトークンを返すテスト用プロバイダー</summary>
    private sealed class RotatingTokenProvider(params string[] tokens) : IAccessTokenProvider
    {
        private int _index;

        public string? LastRejected { get; private set; }

        public Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
        {
            if (rejectedToken is not null)
            {
                LastRejected = rejectedToken;
                _index++;
            }

            return Task.FromResult(tokens[Math.Min(_index, tokens.Length - 1)]);
        }
    }
}

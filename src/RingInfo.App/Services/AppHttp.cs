using System.Net.Http;
using System.Net.Http.Headers;

namespace RingInfo.App.Services;

/// <summary>アプリ全体で共有する HttpClient</summary>
internal static class AppHttp
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        // 長時間起動したままでも DNS の変更などに追従できるよう、接続は一定時間で作り直す
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RingInfo", "1.0"));
        return client;
    }
}

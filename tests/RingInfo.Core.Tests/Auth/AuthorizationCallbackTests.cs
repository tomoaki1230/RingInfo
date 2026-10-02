using System.Net.Sockets;
using RingInfo.Core.Auth;

namespace RingInfo.Core.Tests.Auth;

public class AuthorizationCallbackTests
{
    [Fact]
    public void 成功時のリダイレクトを解析できる()
    {
        var callback = AuthorizationCallback.Parse(new Uri("http://localhost:8765/callback?code=TRUQR7C6&scope=email%20personal&state=abc"));

        Assert.True(callback.IsSuccess);
        Assert.Equal("TRUQR7C6", callback.Code);
        Assert.Equal("abc", callback.State);
        Assert.Equal("email personal", callback.Scope);
    }

    [Fact]
    public void 拒否時のリダイレクトを解析できる()
    {
        var callback = AuthorizationCallback.Parse(new Uri("http://localhost:8765/callback?error=access_denied&state=abc"));

        Assert.False(callback.IsSuccess);
        Assert.Equal("access_denied", callback.Error);
    }

    [Theory]
    [InlineData("http://localhost:8765/callback", true)]
    [InlineData("http://127.0.0.1:5000/", true)]
    [InlineData("https://localhost:8765/callback", false)]
    [InlineData("http://example.com:8765/callback", false)]
    [InlineData("http://localhost/callback", false)]
    [InlineData("not a uri", false)]
    public void リダイレクトURIの形式を検証する(string value, bool expected)
    {
        Assert.Equal(expected, LoopbackAuthorizationListener.TryValidateRedirectUri(value, out _, out var error));
        Assert.Equal(expected, error is null);
    }

    [Fact]
    public async Task ローカルリスナーがリダイレクトを受信できる()
    {
        var port = GetFreePort();
        var redirectUri = new Uri($"http://localhost:{port}/callback");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitTask = LoopbackAuthorizationListener.StartAsync(redirectUri, cts.Token);

        using var http = new HttpClient();
        var notFound = await http.GetAsync($"http://localhost:{port}/favicon.ico");
        var page = await http.GetStringAsync($"http://localhost:{port}/callback?code=c1&state=s1");
        var callback = await waitTask;

        Assert.Equal(System.Net.HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Contains("連携が完了しました", page);
        Assert.Equal("c1", callback.Code);
        Assert.Equal("s1", callback.State);
    }

    [Fact]
    public async Task ローカルリスナーはキャンセルできる()
    {
        var redirectUri = new Uri($"http://localhost:{GetFreePort()}/callback");
        using var cts = new CancellationTokenSource();
        var waitTask = LoopbackAuthorizationListener.StartAsync(redirectUri, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

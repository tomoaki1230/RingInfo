using System.Net;
using System.Text;

namespace RingInfo.Core.Tests.Helpers;

/// <summary>HTTP 通信を差し替えるテスト用ハンドラー</summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        }

        return responder(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body)
{
    public string? Query(string name) => ParseForm(Uri.Query).GetValueOrDefault(name);

    public string? Form(string name) => ParseForm(Body ?? "").GetValueOrDefault(name);

    private static Dictionary<string, string> ParseForm(string value) => RingInfo.Core.Auth.AuthorizationCallback.ParseQuery(value);
}

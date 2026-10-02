using RingInfo.Core.Auth;
using RingInfo.Core.Settings;

namespace RingInfo.Core.Tests.Settings;

public class OuraClientSelectorTests
{
    private static readonly OuraClientInfo BuiltIn = new("built-in-id", "built-in-secret", AppSettings.DefaultRedirectUri);
    private static readonly OAuthToken Token = new("at", "rt", null, null);

    [Fact]
    public void 組み込みの設定ファイルを読み取れる_リダイレクトURIは省略可()
    {
        var info = OuraClientInfo.TryParse("""{"clientId":" id-1 ","clientSecret":"secret-1"}""");

        Assert.Equal(new OuraClientInfo("id-1", "secret-1", AppSettings.DefaultRedirectUri), info);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("""{"clientId":"id-1"}""")]
    [InlineData("""{"clientId":"","clientSecret":"s"}""")]
    public void 必須項目が無い設定ファイルは使わない(string? json)
        => Assert.Null(OuraClientInfo.TryParse(json));

    [Fact]
    public void 組み込みがあれば通常は組み込みを使い_独自登録を選ぶと利用者の設定を使う()
    {
        var settings = new AppSettings { ClientId = "custom-id", ClientSecret = "custom-secret", RedirectUri = "http://localhost:9000/cb" };

        Assert.Same(BuiltIn, OuraClientSelector.Resolve(settings, BuiltIn));

        settings.UseCustomClient = true;
        Assert.Equal(new OuraClientInfo("custom-id", "custom-secret", "http://localhost:9000/cb"), OuraClientSelector.Resolve(settings, BuiltIn));
    }

    [Fact]
    public void 組み込みが無く入力も無ければ使えるアプリは無い()
        => Assert.Null(OuraClientSelector.Resolve(new AppSettings(), builtIn: null));

    [Fact]
    public void トークンを取得したアプリと現在のアプリが一致する場合だけ連携済み()
    {
        var settings = new AppSettings { Token = Token, TokenClientId = "built-in-id" };
        Assert.True(OuraClientSelector.IsConnected(settings, BuiltIn));

        // 別のアプリで取得したトークンは使えない
        settings.TokenClientId = "other-id";
        Assert.False(OuraClientSelector.IsConnected(settings, BuiltIn));

        // 古い設定（TokenClientId なし）は入力済みの Client ID で取得したものとみなす
        var legacy = new AppSettings { ClientId = "built-in-id", Token = Token };
        Assert.True(OuraClientSelector.IsConnected(legacy, BuiltIn));
        Assert.False(OuraClientSelector.IsConnected(new AppSettings { Token = Token }, builtIn: null));
    }
}

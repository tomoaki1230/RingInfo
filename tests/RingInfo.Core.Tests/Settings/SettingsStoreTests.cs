using System.Security.Cryptography;
using System.Text;
using RingInfo.Core.Auth;
using RingInfo.Core.Settings;

namespace RingInfo.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "RingInfoTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ファイルが無ければ初期設定を返す()
    {
        var settings = new SettingsStore(FilePath, new FakeProtector()).Load();

        Assert.Equal(AppSettings.DefaultRedirectUri, settings.RedirectUri);
        Assert.Equal(AppSettings.DefaultRangeDays, settings.RangeDays);
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.False(settings.IsConnected);
    }

    [Fact]
    public void 保存した設定を読み込める_機密情報は暗号化して保存する()
    {
        var store = new SettingsStore(FilePath, new FakeProtector());
        var expiresAt = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        store.Save(new AppSettings
        {
            ClientId = "client-1",
            ClientSecret = "secret-xyz",
            RedirectUri = "http://localhost:9999/cb",
            RangeDays = 30,
            Token = new OAuthToken("access-xyz", "refresh-xyz", expiresAt, "daily"),
            TokenClientId = "client-1",
            UseCustomClient = true,
            Theme = ThemePreference.Light,
        });

        var loaded = new SettingsStore(FilePath, new FakeProtector()).Load();
        var raw = File.ReadAllText(FilePath);

        Assert.Equal("client-1", loaded.ClientId);
        Assert.Equal("secret-xyz", loaded.ClientSecret);
        Assert.Equal("http://localhost:9999/cb", loaded.RedirectUri);
        Assert.Equal(30, loaded.RangeDays);
        Assert.Equal(new OAuthToken("access-xyz", "refresh-xyz", expiresAt, "daily"), loaded.Token);
        Assert.True(loaded.IsConnected);
        Assert.Equal("client-1", loaded.TokenClientId);
        Assert.True(loaded.UseCustomClient);
        Assert.Equal(ThemePreference.Light, loaded.Theme);
        Assert.DoesNotContain("secret-xyz", raw);
        Assert.DoesNotContain("access-xyz", raw);
        Assert.DoesNotContain("refresh-xyz", raw);
    }

    [Fact]
    public void 壊れたファイルは初期設定で読み込み警告を残す()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ broken");
        var store = new SettingsStore(FilePath, new FakeProtector());

        var settings = store.Load();

        Assert.Equal("", settings.ClientId);
        Assert.NotNull(store.LastLoadWarning);
    }

    [Fact]
    public void 復号できない機密情報は破棄して警告を残す()
    {
        new SettingsStore(FilePath, new FakeProtector()).Save(new AppSettings { ClientId = "c", ClientSecret = "s" });
        var store = new SettingsStore(FilePath, new FailingProtector());

        var settings = store.Load();

        Assert.Equal("c", settings.ClientId);
        Assert.Equal("", settings.ClientSecret);
        Assert.NotNull(store.LastLoadWarning);
    }

    /// <summary>テスト用の可逆な暗号化（文字列を反転して Base64 化）</summary>
    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string plainText) => Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plainText.Reverse().ToArray())));

        public string Unprotect(string protectedText) => new(Encoding.UTF8.GetString(Convert.FromBase64String(protectedText)).Reverse().ToArray());
    }

    private sealed class FailingProtector : ISecretProtector
    {
        public string Protect(string plainText) => throw new CryptographicException();

        public string Unprotect(string protectedText) => throw new CryptographicException("cannot decrypt");
    }
}

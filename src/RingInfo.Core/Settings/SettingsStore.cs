using System.Text.Json;
using System.Text.Json.Serialization;
using RingInfo.Core.Auth;

namespace RingInfo.Core.Settings;

/// <summary>
/// 設定を JSON ファイルに保存・読み込みする。
/// Client Secret とトークンは <see cref="ISecretProtector"/> で暗号化して保存する。
/// 複数スレッド（トークン自動更新など）から呼ばれるため排他制御する。
/// </summary>
public sealed class SettingsStore(string filePath, ISecretProtector protector)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _sync = new();

    public string FilePath { get; } = filePath;

    /// <summary>直近の読み込みで発生した問題（無ければ null）</summary>
    public string? LastLoadWarning { get; private set; }

    /// <summary>既定の保存先: %APPDATA%\RingInfo\settings.json</summary>
    public static string DefaultFilePath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RingInfo", "settings.json");

    public AppSettings Load()
    {
        lock (_sync)
        {
            LastLoadWarning = null;
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            SettingsFile? file;
            try
            {
                file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(FilePath), JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                LastLoadWarning = $"設定ファイルを読み込めなかったため、初期設定で起動しました。（{ex.Message}）";
                return new AppSettings();
            }

            if (file is null)
            {
                return new AppSettings();
            }

            var settings = new AppSettings
            {
                ClientId = file.ClientId ?? "",
                RedirectUri = string.IsNullOrWhiteSpace(file.RedirectUri) ? AppSettings.DefaultRedirectUri : file.RedirectUri,
                RangeDays = file.RangeDays > 0 ? file.RangeDays : AppSettings.DefaultRangeDays,
                UseCustomClient = file.UseCustomClient,
                TokenClientId = file.TokenClientId,
                Theme = Enum.TryParse<ThemePreference>(file.Theme, ignoreCase: true, out var theme) ? theme : ThemePreference.System,
                ClientSecret = TryUnprotect(file.ProtectedClientSecret) ?? "",
            };

            var tokenJson = TryUnprotect(file.ProtectedToken);
            if (tokenJson is not null)
            {
                try
                {
                    settings.Token = JsonSerializer.Deserialize<OAuthToken>(tokenJson, JsonOptions);
                }
                catch (JsonException)
                {
                    LastLoadWarning = "保存されたトークンを読み込めませんでした。再度連携してください。";
                }
            }

            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            var file = new SettingsFile
            {
                ClientId = settings.ClientId,
                RedirectUri = settings.RedirectUri,
                RangeDays = settings.RangeDays,
                UseCustomClient = settings.UseCustomClient,
                TokenClientId = settings.TokenClientId,
                Theme = settings.Theme.ToString(),
                ProtectedClientSecret = string.IsNullOrEmpty(settings.ClientSecret) ? null : protector.Protect(settings.ClientSecret),
                ProtectedToken = settings.Token is null ? null : protector.Protect(JsonSerializer.Serialize(settings.Token, JsonOptions)),
            };

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 書き込み途中で壊れないよう一時ファイル経由で置き換える
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
    }

    private string? TryUnprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(protectedText);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            // 別ユーザー・別 PC で暗号化された値などは復号できない
            LastLoadWarning = "保存された認証情報を復号できませんでした。設定画面から再度入力してください。";
            return null;
        }
    }

    /// <summary>ファイルに保存する形式</summary>
    private sealed class SettingsFile
    {
        public int Version { get; set; } = 1;
        public string? ClientId { get; set; }
        public string? ProtectedClientSecret { get; set; }
        public string? RedirectUri { get; set; }
        public string? ProtectedToken { get; set; }
        public int RangeDays { get; set; }
        public bool UseCustomClient { get; set; }
        public string? TokenClientId { get; set; }
        public string? Theme { get; set; }
    }
}

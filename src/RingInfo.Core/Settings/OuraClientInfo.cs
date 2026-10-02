using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingInfo.Core.Settings;

/// <summary>Oura に登録した API アプリの情報</summary>
/// <param name="ClientId">Client ID</param>
/// <param name="ClientSecret">Client Secret</param>
/// <param name="RedirectUri">登録したリダイレクト URI</param>
public sealed record OuraClientInfo(string ClientId, string ClientSecret, string RedirectUri)
{
    /// <summary>
    /// 配布者がビルド時に組み込む設定（oura-client.json）を読み取る。
    /// 必須項目が無い・形式が正しくない場合は null。
    /// </summary>
    public static OuraClientInfo? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<ClientFile>(json);
            if (string.IsNullOrWhiteSpace(file?.ClientId) || string.IsNullOrWhiteSpace(file.ClientSecret))
            {
                return null;
            }

            var redirectUri = string.IsNullOrWhiteSpace(file.RedirectUri) ? AppSettings.DefaultRedirectUri : file.RedirectUri.Trim();
            return new OuraClientInfo(file.ClientId.Trim(), file.ClientSecret.Trim(), redirectUri);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ClientFile
    {
        [JsonPropertyName("clientId")]
        public string? ClientId { get; set; }

        [JsonPropertyName("clientSecret")]
        public string? ClientSecret { get; set; }

        [JsonPropertyName("redirectUri")]
        public string? RedirectUri { get; set; }
    }
}

/// <summary>
/// 実際に使う API アプリの判定。
/// 配布版（組み込みのアプリ情報あり）では、利用者が「自分で登録したアプリを使う」を選ばない限り組み込みを使う。
/// </summary>
public static class OuraClientSelector
{
    public static OuraClientInfo? Resolve(AppSettings settings, OuraClientInfo? builtIn)
    {
        if (builtIn is not null && !settings.UseCustomClient)
        {
            return builtIn;
        }

        return string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret)
            ? null
            : new OuraClientInfo(settings.ClientId.Trim(), settings.ClientSecret.Trim(), settings.RedirectUri.Trim());
    }

    /// <summary>
    /// 連携済みか。トークンは取得したアプリでしか使えないため、取得時のアプリと現在のアプリが一致している必要がある。
    /// </summary>
    public static bool IsConnected(AppSettings settings, OuraClientInfo? builtIn)
        => settings.Token is not null
           && Resolve(settings, builtIn) is { } client
           && string.Equals(settings.TokenClientId ?? settings.ClientId.Trim(), client.ClientId, StringComparison.Ordinal);
}

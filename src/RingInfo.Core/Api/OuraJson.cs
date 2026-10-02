using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingInfo.Core.Api;

/// <summary>Oura API の JSON 設定</summary>
public static class OuraJson
{
    /// <summary>API レスポンス用（snake_case）</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new LenientInt32Converter(), new LenientNullableInt32Converter() },
    };
}

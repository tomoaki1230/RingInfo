namespace RingInfo.Core.Auth;

/// <summary>認可後にリダイレクト URI へ渡されるパラメーター</summary>
public sealed record AuthorizationCallback(string? Code, string? State, string? Error, string? Scope)
{
    public bool IsSuccess => !string.IsNullOrEmpty(Code) && string.IsNullOrEmpty(Error);

    /// <summary>リダイレクト URL のクエリを解析する</summary>
    public static AuthorizationCallback Parse(Uri uri)
    {
        var query = ParseQuery(uri.Query);
        return new AuthorizationCallback(
            query.GetValueOrDefault("code"),
            query.GetValueOrDefault("state"),
            query.GetValueOrDefault("error"),
            query.GetValueOrDefault("scope"));
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf('=');
            var key = Decode(index < 0 ? part : part[..index]);
            var value = index < 0 ? "" : Decode(part[(index + 1)..]);
            result.TryAdd(key, value);
        }

        return result;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}

namespace RingInfo.Core.Tests.Helpers;

internal static class TestData
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    /// <summary>日本時間（UTC+9 固定、夏時間なし）</summary>
    public static TimeZoneInfo Tokyo { get; } = TimeZoneInfo.CreateCustomTimeZone("Test/Tokyo", TimeSpan.FromHours(9), "Tokyo", "Tokyo");
}

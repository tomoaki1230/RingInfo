namespace RingInfo.Core.Tests.Helpers;

/// <summary>現在時刻を固定するテスト用 TimeProvider</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}

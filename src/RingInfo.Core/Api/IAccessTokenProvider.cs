namespace RingInfo.Core.Api;

/// <summary>API 呼び出し用アクセストークンの提供元</summary>
public interface IAccessTokenProvider
{
    /// <summary>
    /// 有効なアクセストークンを返す。
    /// </summary>
    /// <param name="rejectedToken">直前に 401 で拒否されたトークン。指定された場合は更新を試みる。</param>
    /// <exception cref="OuraAuthenticationRequiredException">再認証が必要な場合</exception>
    Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken);
}

/// <summary>固定トークンを返すプロバイダー（更新はできない）</summary>
public sealed class StaticAccessTokenProvider(string accessToken) : IAccessTokenProvider
{
    public Task<string> GetAccessTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        if (rejectedToken is not null)
        {
            throw new OuraAuthenticationRequiredException("アクセストークンが無効です。設定画面から再度連携してください。");
        }

        return Task.FromResult(accessToken);
    }
}

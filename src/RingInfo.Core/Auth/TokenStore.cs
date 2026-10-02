namespace RingInfo.Core.Auth;

/// <summary>
/// 現在の OAuth トークンの保管場所。
/// 複数の OAuthTokenManager（設定の変更などで作り直される）が同じ保管場所を共有することで、
/// 1 回限り有効なリフレッシュトークンを二重に使う事故を防ぐ。
/// </summary>
public sealed class TokenStore(OAuthToken? initialToken = null)
{
    private readonly object _sync = new();
    private OAuthToken? _token = initialToken;
    private int _generation;

    /// <summary>更新処理の排他（共有するすべての OAuthTokenManager で 1 つ）</summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>トークンが自動更新されたとき（保存用）。任意のスレッドから呼ばれる。</summary>
    public event EventHandler<OAuthToken>? TokenRefreshed;

    public OAuthToken? Token
    {
        get
        {
            lock (_sync)
            {
                return _token;
            }
        }
    }

    /// <summary>世代（連携し直す・連携を解除するたびに増える）</summary>
    internal int Generation
    {
        get
        {
            lock (_sync)
            {
                return _generation;
            }
        }
    }

    /// <summary>連携し直した・解除した場合に、トークンを置き換える</summary>
    public void Replace(OAuthToken? token)
    {
        lock (_sync)
        {
            if (Equals(_token, token))
            {
                return;
            }

            _token = token;
            _generation++;
        }
    }

    /// <summary>
    /// 自動更新の結果を反映する。更新中に連携し直された（世代が変わった）場合は、古い結果で上書きしない。
    /// </summary>
    internal bool TryUpdate(int generation, OAuthToken token)
    {
        lock (_sync)
        {
            if (generation != _generation)
            {
                return false;
            }

            _token = token;
        }

        TokenRefreshed?.Invoke(this, token);
        return true;
    }
}

namespace RingInfo.Core.Services;

/// <summary>スコアの評価区分（Oura の区分に準拠）</summary>
public enum ScoreLevel
{
    None,
    /// <summary>要注意（0〜59）</summary>
    PayAttention,
    /// <summary>まずまず（60〜69）</summary>
    Fair,
    /// <summary>良好（70〜84）</summary>
    Good,
    /// <summary>最適（85〜100）</summary>
    Optimal,
}

public static class ScoreRating
{
    public static ScoreLevel GetLevel(int? score) => score switch
    {
        null => ScoreLevel.None,
        >= 85 => ScoreLevel.Optimal,
        >= 70 => ScoreLevel.Good,
        >= 60 => ScoreLevel.Fair,
        _ => ScoreLevel.PayAttention,
    };

    public static string GetLabel(ScoreLevel level) => level switch
    {
        ScoreLevel.Optimal => "最適",
        ScoreLevel.Good => "良好",
        ScoreLevel.Fair => "まずまず",
        ScoreLevel.PayAttention => "要注意",
        _ => "データなし",
    };

    public static string GetLabel(int? score) => GetLabel(GetLevel(score));
}

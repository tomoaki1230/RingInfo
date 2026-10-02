namespace RingInfo.Core.Formatting;

/// <summary>API の列挙値を日本語の表示名に変換する</summary>
public static class Labels
{
    public static string HardwareType(string? value) => value switch
    {
        null or "" => DisplayFormat.NoValue,
        "gen1" => "Oura Ring 第1世代",
        "gen2" => "Oura Ring Gen2",
        "gen2m" => "Oura Ring Gen2M",
        "gen3" => "Oura Ring Gen3",
        "gen4" => "Oura Ring 4",
        "or5" => "Oura Ring 5",
        _ => value,
    };

    public static string RingColor(string? value) => value switch
    {
        null or "" => DisplayFormat.NoValue,
        "brushed_silver" => "ブラッシュドシルバー",
        "glossy_black" => "ブラック（光沢）",
        "glossy_gold" => "ゴールド（光沢）",
        "glossy_white" => "ホワイト（光沢）",
        "gucci" => "Gucci",
        "matt_gold" => "マットゴールド",
        "rose" => "ローズ",
        "silver" => "シルバー",
        "stealth_black" => "ステルスブラック",
        "titanium" => "チタン",
        "titanium_and_gold" => "チタン＆ゴールド",
        "cloud" => "クラウド",
        "petal" => "ペタル",
        "midnight" => "ミッドナイト",
        "tide" => "タイド",
        "deep_rose" => "ディープローズ",
        _ => value,
    };

    public static string RingDesign(string? value) => value switch
    {
        null or "" => DisplayFormat.NoValue,
        "heritage" => "ヘリテージ",
        "horizon" => "ホライズン",
        "balance" => "バランス",
        "balance_diamond" => "バランス ダイヤモンド",
        "ceramic" => "セラミック",
        _ => value,
    };

    public static string StressSummary(string? value) => value switch
    {
        null or "" => DisplayFormat.NoValue,
        "restored" => "回復できた",
        "normal" => "普通",
        "stressful" => "ストレス多め",
        _ => value,
    };

    public static string HeartRateSource(string? value) => value switch
    {
        "awake" => "日中",
        "sleep" => "睡眠",
        "workout" => "ワークアウト",
        "rest" => "休息",
        "live" => "ライブ計測",
        "session" => "セッション",
        _ => "その他",
    };

    public static string WorkoutIntensity(string? value) => value switch
    {
        "easy" => "軽い",
        "moderate" => "中程度",
        "hard" => "きつい",
        _ => value ?? DisplayFormat.NoValue,
    };

    public static string WorkoutActivity(string? value) => value switch
    {
        null or "" => DisplayFormat.NoValue,
        "walking" => "ウォーキング",
        "running" => "ランニング",
        "cycling" => "サイクリング",
        "swimming" => "水泳",
        "hiking" => "ハイキング",
        "yoga" => "ヨガ",
        "strength_training" => "筋力トレーニング",
        "dancing" => "ダンス",
        "tennis" => "テニス",
        "golf" => "ゴルフ",
        "soccer" => "サッカー",
        "basketball" => "バスケットボール",
        "elliptical" => "エリプティカル",
        "rowing" => "ローイング",
        "pilates" => "ピラティス",
        "housework" => "家事",
        _ => value.Replace('_', ' '),
    };

    public static string BiologicalSex(string? value) => value switch
    {
        "male" => "男性",
        "female" => "女性",
        null or "" => DisplayFormat.NoValue,
        _ => value,
    };
}

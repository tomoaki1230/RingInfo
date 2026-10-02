using OxyPlot;

namespace RingInfo.App.Charts;

/// <summary>
/// グラフの配色。ライト／ダークで値が変わる（Themes/Colors.*.xaml と対応）。
/// 系列色は色覚多様性を考慮して検証済みの順序で割り当てる（順番を入れ替えないこと）。
/// テーマを切り替えたらグラフを作り直す必要がある（PlotModel 作成時の色が使われるため）。
/// </summary>
internal static class ChartTheme
{
    /// <summary>ダークテーマかどうか（ThemeManager が設定する）</summary>
    public static bool IsDark { get; set; } = true;

    private static OxyColor Pick(string dark, string light) => OxyColor.Parse(IsDark ? dark : light);

    public static OxyColor Surface => Pick("#1A1A19", "#FFFFFF");
    public static OxyColor TextPrimary => Pick("#FFFFFF", "#15181C");
    public static OxyColor TextSecondary => Pick("#C3C2B7", "#3A414B");
    public static OxyColor TextMuted => Pick("#898781", "#5C6470");
    public static OxyColor Gridline => Pick("#2C2C2A", "#E6E9EE");
    public static OxyColor Axis => Pick("#383835", "#B4BCC7");

    // カテゴリ色（固定順）: 1=青, 2=橙, 3=緑
    public static OxyColor Series1 => Pick("#3987E5", "#2A78D6");
    public static OxyColor Series2 => Pick("#D95926", "#EB6834");
    public static OxyColor Series3 => Pick("#199E70", "#1BAF7A");

    // 正負で色を分ける（体温偏差）: 青 ↔ 赤
    public static OxyColor DivergingNegative => Pick("#3987E5", "#2A78D6");
    public static OxyColor DivergingPositive => Pick("#E66767", "#E34948");

    // 睡眠ステージ（順序のある段階色・青の単色相。深いほど濃い）
    public static OxyColor StageDeep => Pick("#256ABF", "#184F95");
    public static OxyColor StageLight => Pick("#3987E5", "#2A78D6");
    public static OxyColor StageRem => Pick("#86B6EF", "#5598E7");
    public static OxyColor StageAwake => Pick("#CDE2FB", "#86B6EF");

    // 活動の強度（順序のある段階色。背景に近い色ほど低強度）
    public static OxyColor IntensityLow => Pick("#256ABF", "#86B6EF");
    public static OxyColor IntensityMedium => Pick("#5598E7", "#3987E5");
    public static OxyColor IntensityHigh => Pick("#9EC5F4", "#1C5CAB");

    /// <summary>マウスを乗せるだけで値を表示する（拡大・移動なし）</summary>
    public static PlotController HoverController { get; } = CreateHoverController();

    private static PlotController CreateHoverController()
    {
        var controller = new PlotController();
        controller.UnbindAll();
        controller.BindMouseEnter(PlotCommands.HoverSnapTrack);
        return controller;
    }
}

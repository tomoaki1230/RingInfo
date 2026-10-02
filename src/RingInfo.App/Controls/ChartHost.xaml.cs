using System.Windows;
using System.Windows.Controls;
using RingInfo.App.Charts;

namespace RingInfo.App.Controls;

/// <summary>
/// グラフ（OxyPlot）と「データなし」表示をまとめた部品。
/// 非表示のページのグラフは描かない（表示されたときに描く）。
/// OxyPlot は一度表示したグラフを非表示でもデータ更新のたびに描き直すため、
/// 期間が長いと全ページ分の描画で画面が数秒止まっていた。
/// </summary>
public partial class ChartHost : UserControl
{
    public ChartHost()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdatePlot();
        IsVisibleChanged += (_, _) => UpdatePlot();
    }

    private void UpdatePlot()
    {
        var chart = IsVisible ? DataContext as ChartData : null;
        if (!ReferenceEquals(Plot.Model, chart?.Model))
        {
            Plot.Model = chart?.Model;
        }

        Plot.Controller = chart?.ActualController ?? ChartTheme.HoverController;
    }
}

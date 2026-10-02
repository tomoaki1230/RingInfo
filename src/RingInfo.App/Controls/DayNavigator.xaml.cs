using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RingInfo.App.Controls;

/// <summary>選択日の前後移動（◀ 日付 ▶）。日付を押すとカレンダーで選べる（取得期間内のみ）。</summary>
public partial class DayNavigator : UserControl
{
    public DayNavigator()
    {
        InitializeComponent();
    }

    /// <summary>カレンダーの表示内容（スクリーンショット用）</summary>
    internal MonthCalendar CalendarElement => Calendar;

    /// <summary>カレンダーを開く</summary>
    internal void OpenCalendar()
    {
        Calendar.ShowSelectedMonth();
        CalendarPopup.IsOpen = true;

        // キーボードでも操作できるよう、選択中の日にフォーカスを移す
        Dispatcher.BeginInvoke(Calendar.FocusSelectedDay, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Esc でカレンダーを閉じて日付ボタンに戻る</summary>
    private void OnCalendarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CalendarPopup.IsOpen = false;
            DateButton.Focus();
            e.Handled = true;
        }
    }

    private void OnDateButtonClick(object sender, RoutedEventArgs e) => OpenCalendar();

    private void OnDateSelected(object? sender, EventArgs e)
    {
        CalendarPopup.IsOpen = false;
        DateButton.Focus();
    }
}

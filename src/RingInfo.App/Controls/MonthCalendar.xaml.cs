using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace RingInfo.App.Controls;

/// <summary>カレンダーの 1 日分</summary>
public sealed record CalendarDay(DateTime Date, bool IsCurrentMonth, bool IsSelectable, bool IsSelected, bool IsToday)
{
    public string Text => Date.Day.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// テーマに合わせた月表示のカレンダー。MinDate〜MaxDate の日だけ選択できる。
/// </summary>
public partial class MonthCalendar : UserControl
{
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
        nameof(SelectedDate), typeof(DateTime?), typeof(MonthCalendar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDatesChanged));

    public static readonly DependencyProperty MinDateProperty = DependencyProperty.Register(
        nameof(MinDate), typeof(DateTime), typeof(MonthCalendar), new PropertyMetadata(DateTime.MinValue, OnDatesChanged));

    public static readonly DependencyProperty MaxDateProperty = DependencyProperty.Register(
        nameof(MaxDate), typeof(DateTime), typeof(MonthCalendar), new PropertyMetadata(DateTime.MaxValue, OnDatesChanged));

    /// <summary>表示中の月の 1 日</summary>
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public MonthCalendar()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>日付を選んだとき（ポップアップを閉じるため）</summary>
    public event EventHandler? DateSelected;

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public DateTime MinDate
    {
        get => (DateTime)GetValue(MinDateProperty);
        set => SetValue(MinDateProperty, value);
    }

    public DateTime MaxDate
    {
        get => (DateTime)GetValue(MaxDateProperty);
        set => SetValue(MaxDateProperty, value);
    }

    /// <summary>選択中の日（無ければ選択できる最後の日）の月を表示する</summary>
    public void ShowSelectedMonth()
    {
        var date = SelectedDate ?? (MaxDate < DateTime.MaxValue ? MaxDate : DateTime.Today);
        _month = new DateTime(date.Year, date.Month, 1);
        Refresh();
    }

    private static void OnDatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MonthCalendar)d).ShowSelectedMonth();

    private void Refresh()
    {
        MonthTitle.Text = _month.ToString("yyyy年 M月", CultureInfo.InvariantCulture);
        DayItems.ItemsSource = BuildDays(_month, MinDate.Date, MaxDate.Date, SelectedDate?.Date, DateTime.Today);
        PreviousMonthButton.IsEnabled = MinDate.Date < _month;
        NextMonthButton.IsEnabled = MaxDate.Date >= _month.AddMonths(1);
        RangeText.Text = MinDate > DateTime.MinValue && MaxDate < DateTime.MaxValue
            ? $"選択できる期間: {MinDate:M/d} 〜 {MaxDate:M/d}"
            : "";
    }

    /// <summary>日曜始まりで 6 週分（42 日）の日付を作る</summary>
    internal static List<CalendarDay> BuildDays(DateTime month, DateTime min, DateTime max, DateTime? selected, DateTime today)
    {
        var first = month.AddDays(-(int)month.DayOfWeek);
        return Enumerable.Range(0, 42)
            .Select(i => first.AddDays(i))
            .Select(date => new CalendarDay(
                date,
                date.Month == month.Month,
                date >= min && date <= max,
                date == selected,
                date == today))
            .ToList();
    }

    /// <summary>選択中の日（無ければ選べる最初の日）のボタンにフォーカスを移す</summary>
    public void FocusSelectedDay()
    {
        var days = DayItems.Items.OfType<CalendarDay>().ToList();
        var target = days.FirstOrDefault(d => d.IsSelected) ?? days.FirstOrDefault(d => d.IsSelectable);
        if (target is not null && DayItems.ItemContainerGenerator.ContainerFromItem(target) is ContentPresenter presenter
            && System.Windows.Media.VisualTreeHelper.GetChildrenCount(presenter) > 0
            && System.Windows.Media.VisualTreeHelper.GetChild(presenter, 0) is Button button)
        {
            button.Focus();
        }
    }

    /// <summary>表示する月を前後に動かす（スクリーンショット用）</summary>
    internal void MoveMonth(int months)
    {
        _month = _month.AddMonths(months);
        Refresh();
    }

    private void OnPreviousMonth(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        Refresh();
    }

    private void OnNextMonth(object sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        Refresh();
    }

    private void OnDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CalendarDay { IsSelectable: true } day })
        {
            SelectedDate = day.Date;
            DateSelected?.Invoke(this, EventArgs.Empty);
        }
    }
}

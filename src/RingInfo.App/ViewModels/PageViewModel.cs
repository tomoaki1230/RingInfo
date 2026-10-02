using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RingInfo.Core.Formatting;
using RingInfo.Core.Services;

namespace RingInfo.App.ViewModels;

/// <summary>サイドバーから切り替える各ページの基底クラス</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Segoe MDL2 Assets / Segoe Fluent Icons の文字</summary>
    public abstract string IconGlyph { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>データを再読み込みしたとき</summary>
    public virtual void Update(DashboardSnapshot snapshot)
    {
    }

    /// <summary>ページが表示されたとき</summary>
    public virtual Task OnActivatedAsync() => Task.CompletedTask;

    /// <summary>テーマ変更後、まだグラフを作り直していないか</summary>
    public bool ChartsOutdated { get; private set; }

    /// <summary>テーマが変わったことを通知する（表示中のページはすぐに、他はページを開いたときに作り直す）</summary>
    public void MarkChartsOutdated(bool rebuildNow)
    {
        ChartsOutdated = true;
        if (rebuildNow)
        {
            RefreshChartsIfOutdated();
        }
    }

    /// <summary>Update でグラフも作り直した場合</summary>
    public void MarkChartsUpToDate() => ChartsOutdated = false;

    public void RefreshChartsIfOutdated()
    {
        if (ChartsOutdated)
        {
            ChartsOutdated = false;
            RebuildCharts();
        }
    }

    /// <summary>グラフ（テーマの色を使う部分）だけを作り直す。表や数値は作り直さない。</summary>
    protected virtual void RebuildCharts()
    {
    }
}

/// <summary>
/// 「選択した日の詳細」と「期間の推移」を表示するページの基底クラス。
/// 日付の前後移動と、表の行選択による日付の切り替えに対応する。
/// </summary>
public abstract partial class DayPageViewModel : PageViewModel
{
    private bool _syncingRow;

    protected DashboardSnapshot? Snapshot { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDayText), nameof(CalendarDate))]
    [NotifyCanExecuteChangedFor(nameof(PreviousDayCommand), nameof(NextDayCommand))]
    private DateOnly? _selectedDay;

    /// <summary>表で選択中の行</summary>
    [ObservableProperty]
    private IDayRow? _selectedRow;

    public string SelectedDayText => SelectedDay is { } day ? DisplayFormat.DayLong(day) : DisplayFormat.NoValue;

    /// <summary>カレンダーで選択中の日（取得期間外の日は選べない）</summary>
    public DateTime? CalendarDate
    {
        get => SelectedDay?.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } date && Snapshot is { } range)
            {
                var day = DateOnly.FromDateTime(date);
                if (day >= range.StartDate && day <= range.EndDate)
                {
                    SelectedDay = day;
                }
            }
        }
    }

    /// <summary>カレンダーで選べる最初の日（取得期間の開始日）</summary>
    public DateTime CalendarMin => Snapshot?.StartDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;

    /// <summary>カレンダーで選べる最後の日（取得期間の終了日）</summary>
    public DateTime CalendarMax => Snapshot?.EndDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.MaxValue;

    public override void Update(DashboardSnapshot snapshot)
    {
        var followLatest = IsFollowingLatest(SelectedDay, Snapshot);
        Snapshot = snapshot;
        OnPropertyChanged(nameof(CalendarMin));
        OnPropertyChanged(nameof(CalendarMax));
        BuildPeriodCharts(snapshot);
        UpdatePeriod(snapshot);

        var target = ChooseDay(SelectedDay, followLatest, snapshot);
        if (target == SelectedDay)
        {
            RefreshSelectedDay();
        }
        else
        {
            SelectedDay = target;
        }

        PreviousDayCommand.NotifyCanExecuteChanged();
        NextDayCommand.NotifyCanExecuteChanged();
    }

    /// <summary>選択中の日が「最新の日」（自分で別の日を選んでいない）か</summary>
    internal static bool IsFollowingLatest(DateOnly? selectedDay, DashboardSnapshot? previous)
        => selectedDay is null || previous is null || selectedDay == (previous.Latest?.Day ?? previous.EndDate);

    /// <summary>
    /// 再読み込み後に表示する日。
    /// 最新の日を見ていた場合は新しい最新の日へ（日付が変わった・新しいデータが届いた場合に追従）、
    /// 自分で選んだ日が期間内ならその日を維持する。
    /// </summary>
    internal static DateOnly ChooseDay(DateOnly? selectedDay, bool followLatest, DashboardSnapshot snapshot)
    {
        var latest = snapshot.Latest?.Day ?? snapshot.EndDate;
        if (followLatest || selectedDay is not { } current || current < snapshot.StartDate || current > snapshot.EndDate)
        {
            return latest;
        }

        return current;
    }

    /// <summary>期間全体の表・数値を更新する</summary>
    protected abstract void UpdatePeriod(DashboardSnapshot snapshot);

    /// <summary>期間全体のグラフを作る</summary>
    protected virtual void BuildPeriodCharts(DashboardSnapshot snapshot)
    {
    }

    /// <summary>選択日の数値を更新する</summary>
    protected abstract void UpdateSelectedDay(DayMetrics? metrics, DateOnly day);

    /// <summary>選択日のグラフ（テーマの色を使う部分）を作る</summary>
    protected virtual void BuildDayCharts(DayMetrics? metrics, DateOnly day)
    {
    }

    protected override void RebuildCharts()
    {
        if (Snapshot is null)
        {
            return;
        }

        BuildPeriodCharts(Snapshot);
        if (SelectedDay is { } day)
        {
            BuildDayCharts(Snapshot.GetDay(day), day);
        }
    }

    /// <summary>表の行（選択日と同期するため）</summary>
    protected abstract IEnumerable<IDayRow> RowsForSelection { get; }

    partial void OnSelectedDayChanged(DateOnly? value) => RefreshSelectedDay();

    partial void OnSelectedRowChanged(IDayRow? value)
    {
        if (!_syncingRow && value is not null && value.Day != SelectedDay)
        {
            SelectedDay = value.Day;
        }
    }

    private void RefreshSelectedDay()
    {
        if (Snapshot is null || SelectedDay is not { } day)
        {
            return;
        }

        var metrics = Snapshot.GetDay(day);
        UpdateSelectedDay(metrics, day);
        BuildDayCharts(metrics, day);

        _syncingRow = true;
        SelectedRow = RowsForSelection.FirstOrDefault(r => r.Day == day);
        _syncingRow = false;
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousDay() => SelectedDay = SelectedDay?.AddDays(-1);

    private bool CanGoPrevious() => Snapshot is not null && SelectedDay is { } day && day > Snapshot.StartDate;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextDay() => SelectedDay = SelectedDay?.AddDays(1);

    private bool CanGoNext() => Snapshot is not null && SelectedDay is { } day && day < Snapshot.EndDate;
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RingInfo.App.Charts;
using RingInfo.Core.Formatting;
using RingInfo.Core.Models;
using RingInfo.Core.Services;

namespace RingInfo.App.ViewModels;

/// <summary>心拍数ページ: 選択した日の 1 日の心拍数（ページを開いたときに取得する）</summary>
/// <param name="loader">指定日の心拍数を取得する処理（日付・一時保存を使わないか・キャンセル）</param>
/// <param name="timeZone">表示に使うタイムゾーン</param>
/// <param name="today">今日の日付</param>
/// <param name="timeProvider">現在時刻（テスト用）</param>
public sealed partial class HeartRatePageViewModel(
    Func<DateOnly, bool, CancellationToken, Task<IReadOnlyList<HeartRateSample>>> loader,
    TimeZoneInfo timeZone,
    Func<DateOnly>? today = null,
    TimeProvider? timeProvider = null) : PageViewModel
{
    private readonly Func<DateOnly> _today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>表示中の心拍数を取得した時刻（UTC）</summary>
    private DateTimeOffset? _loadedAt;

    private DashboardSnapshot? _snapshot;
    private DateOnly? _loadedDay;
    private CancellationTokenSource? _loadCancellation;
    private IReadOnlyList<HeartRateSample>? _samples;

    /// <summary>表示中の心拍数（テスト用）</summary>
    internal IReadOnlyList<HeartRateSample>? Samples => _samples;
    private bool _syncingRow;

    public override string Title => "心拍数";

    public override string IconGlyph => "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDayText), nameof(CalendarDate))]
    [NotifyCanExecuteChangedFor(nameof(PreviousDayCommand), nameof(NextDayCommand))]
    private DateOnly? _selectedDay;

    [ObservableProperty]
    private ChartData? _chart;

    [ObservableProperty]
    private IReadOnlyList<MetricTile> _tiles = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>日別の表（取得済みの睡眠データから作る。行を選ぶとその日の心拍数を表示）</summary>
    [ObservableProperty]
    private IReadOnlyList<HeartRateRow> _rows = [];

    [ObservableProperty]
    private IDayRow? _selectedRow;

    partial void OnSelectedRowChanged(IDayRow? value)
    {
        if (!_syncingRow && value is not null && value.Day != SelectedDay)
        {
            SelectedDay = value.Day;
        }
    }

    private void SyncSelectedRow()
    {
        _syncingRow = true;
        SelectedRow = Rows.FirstOrDefault(r => r.Day == SelectedDay);
        _syncingRow = false;
    }

    public string SelectedDayText => SelectedDay is { } day ? DisplayFormat.DayLong(day) : DisplayFormat.NoValue;

    /// <summary>カレンダーで選択中の日（取得期間外の日は選べない）</summary>
    public DateTime? CalendarDate
    {
        get => SelectedDay?.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } date && _snapshot is { } range)
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
    public DateTime CalendarMin => _snapshot?.StartDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;

    /// <summary>カレンダーで選べる最後の日（取得期間の終了日）</summary>
    public DateTime CalendarMax => _snapshot?.EndDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.MaxValue;

    public override void Update(DashboardSnapshot snapshot)
    {
        var followLatest = DayPageViewModel.IsFollowingLatest(SelectedDay, _snapshot);
        _snapshot = snapshot;
        _loadedDay = null;
        OnPropertyChanged(nameof(CalendarMin));
        OnPropertyChanged(nameof(CalendarMax));
        Rows = snapshot.Days
            .Where(d => d.MainSleep is not null)
            .OrderByDescending(d => d.Day)
            .Select(d => new HeartRateRow(
                d.Day,
                DisplayFormat.DayShort(d.Day),
                DisplayFormat.Number(d.LowestHeartRate),
                DisplayFormat.Number(d.AverageHeartRate, 0),
                DisplayFormat.Number(d.AverageHrv)))
            .ToList();
        var target = DayPageViewModel.ChooseDay(SelectedDay, followLatest, snapshot);
        if (target != SelectedDay)
        {
            SelectedDay = target;
        }

        PreviousDayCommand.NotifyCanExecuteChanged();
        NextDayCommand.NotifyCanExecuteChanged();
        SyncSelectedRow();
        if (IsSelected)
        {
            _ = LoadAsync();
        }
    }

    /// <summary>
    /// ページを開いたとき。別の日を表示している場合に加え、
    /// 今日を表示していて前回の取得から一定時間（5 分）たっている場合も取り直す（今日の心拍数は時間とともに増えるため）。
    /// </summary>
    public override Task OnActivatedAsync()
    {
        var stale = _loadedDay == SelectedDay
            && SelectedDay >= _today()
            && _loadedAt is { } loadedAt
            && _timeProvider.GetUtcNow() - loadedAt >= Core.Services.DashboardService.TodayHeartRateCacheDuration;
        return _loadedDay != SelectedDay || stale ? LoadAsync() : Task.CompletedTask;
    }

    /// <summary>日付を切り替えてから読み込むまでの待ち時間（連打しても最後の日だけ読み込む）</summary>
    public static readonly TimeSpan LoadDelay = TimeSpan.FromMilliseconds(350);

    partial void OnSelectedDayChanged(DateOnly? value)
    {
        SyncSelectedRow();
        if (IsSelected)
        {
            _ = LoadAsync(delay: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousDay() => SelectedDay = SelectedDay?.AddDays(-1);

    private bool CanGoPrevious() => _snapshot is not null && SelectedDay is { } day && day > _snapshot.StartDate;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextDay() => SelectedDay = SelectedDay?.AddDays(1);

    private bool CanGoNext() => _snapshot is not null && SelectedDay is { } day && day < _snapshot.EndDate;

    /// <summary>再読み込みボタン: 一時保存を使わずに取得する</summary>
    [RelayCommand]
    private Task ReloadAsync() => LoadAsync(forceReload: true);

    private async Task LoadAsync(bool delay = false, bool forceReload = false)
    {
        if (SelectedDay is not { } day)
        {
            return;
        }

        _loadCancellation?.Cancel();
        var cancellation = _loadCancellation = new CancellationTokenSource();
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            if (delay)
            {
                await Task.Delay(LoadDelay, cancellation.Token);
            }

            var samples = await loader(day, forceReload, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _samples = samples;
            BuildChart(day);
            Tiles = BuildTiles(samples);
            _loadedDay = day;
            _loadedAt = _timeProvider.GetUtcNow();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 別の日を選び直した場合
        }
        catch (Exception ex)
        {
            ErrorMessage = $"心拍数を取得できませんでした。{ex.Message}";
            Chart = null;
            Tiles = [];
        }
        finally
        {
            if (_loadCancellation == cancellation)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>テーマ変更時は取得済みの心拍数からグラフだけを作り直す（再取得しない）</summary>
    protected override void RebuildCharts()
    {
        if (_loadedDay is { } day && _samples is not null)
        {
            BuildChart(day);
        }
    }

    private void BuildChart(DateOnly day)
    {
        var (start, _) = DashboardService.GetDayRange(day, timeZone);
        Chart = ChartFactory.HeartRateTimeline(_samples ?? [], start.DateTime, start.Offset);
    }

    private static List<MetricTile> BuildTiles(IReadOnlyList<HeartRateSample> samples)
    {
        static string Avg(IEnumerable<HeartRateSample> items)
        {
            var list = items.ToList();
            return list.Count == 0 ? DisplayFormat.NoValue : $"{list.Average(s => s.Bpm):0}";
        }

        var awake = samples.Where(s => s.Source is not (HeartRateSample.SourceSleep or HeartRateSample.SourceWorkout));
        var sleep = samples.Where(s => s.Source == HeartRateSample.SourceSleep);
        var min = samples.MinBy(s => s.Bpm);
        var max = samples.MaxBy(s => s.Bpm);

        return
        [
            new MetricTile("平均", Avg(samples), "bpm"),
            new MetricTile("最低", min is null ? DisplayFormat.NoValue : $"{min.Bpm}", "bpm", min is null ? null : $"{min.Timestamp.LocalDateTime:HH:mm}"),
            new MetricTile("最高", max is null ? DisplayFormat.NoValue : $"{max.Bpm}", "bpm", max is null ? null : $"{max.Timestamp.LocalDateTime:HH:mm}（{Labels.HeartRateSource(max.Source)}）"),
            new MetricTile("日中の平均", Avg(awake), "bpm"),
            new MetricTile("睡眠中の平均", Avg(sleep), "bpm"),
            new MetricTile("計測数", DisplayFormat.Number(samples.Count), "件"),
        ];
    }
}

/// <summary>心拍数ページの日別の表の行（睡眠中の値）</summary>
public sealed record HeartRateRow(DateOnly Day, string DayText, string RestingHeartRate, string AverageHeartRate, string Hrv) : IDayRow;

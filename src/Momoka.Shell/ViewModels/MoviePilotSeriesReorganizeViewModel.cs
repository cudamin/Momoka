using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSeriesReorganizeViewModel : PageViewModel
{
    private MoviePilotService? _service;
    private MoviePilotTransferContext? _context;
    private string _connection = "";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<MoviePilotSeriesTransferRow> _allRows = [];
    private bool _released;
    private bool _updatingSelection;

    // 控件只在列表/分组模式切换时更换视图，所有模式共享同一批行和勾选状态。
    internal event Action? PresentationChanged;
    internal event Action? ResourceSelectionChanged;

    public ObservableCollection<MoviePilotSeriesTransferRow> Rows { get; } = [];
    public ObservableCollection<MoviePilotSeriesTransferGroup> Groups { get; } = [];
    public string Title => _context?.Title ?? "";
    public string Query { get; private set; } = "";
    public string CountLabel => Rows.Count == _allRows.Count ? $"共 {_allRows.Count} 条资源" : $"显示 {Rows.Count} / {_allRows.Count} 条资源";
    public int SelectedCount => Rows.Count(row => row.Selected);
    public string SelectionLabel => $"已选 {SelectedCount} 条";
    public string SubmitLabel => SelectedCount == 0 ? "选择要重新整理的资源" : $"批量重新整理（{SelectedCount}）";
    public bool CanSelect => !_released && !Busy && Rows.Count > 0;
    public bool CanContinue => CanSelect && SelectedCount > 0 && _service?.Available == true && _service.IsCurrentConnection(_connection);
    public Visibility EmptyVisibility => Show(IsReady && !Busy && !NoticeOpen && Rows.Count == 0);
    public Visibility SelectionVisibility => Show(Rows.Count > 0);
    public string EmptyTitle => _allRows.Count == 0 ? "没有找到当前剧的整理记录" : "没有符合搜索与筛选条件的资源";
    public string EmptyMessage => _allRows.Count == 0
        ? "可刷新重试，或在 MoviePilot 媒体整理中核对这部剧的 TMDB 编号。"
        : "修改关键词或切回全部状态即可恢复；不会整理被筛掉的资源。";
    public string StatusLabel => StatusFilter switch { MoviePilotTransferStatusFilter.Success => "成功", MoviePilotTransferStatusFilter.Failed => "失败", _ => "全部" };
    public string SortLabel => Sort switch
    {
        MoviePilotTransferSort.Title => "标题",
        MoviePilotTransferSort.Path => "路径",
        MoviePilotTransferSort.Mode => "转移方式",
        MoviePilotTransferSort.Size => "大小",
        MoviePilotTransferSort.Time => "时间",
        MoviePilotTransferSort.Status => "状态",
        _ => "默认顺序"
    };
    public string StatusAutomationName => "状态筛选：" + StatusLabel;
    public string SortAutomationName => "本页排序：" + SortLabel + (Sort == MoviePilotTransferSort.Default ? "" : Descending ? "，降序" : "，升序");
    public string ViewAutomationName => Grouped ? "显示模式：分组模式" : "显示模式：列表模式";
    public string ViewGlyph => Grouped ? "\uE8FD" : "\uE8A4";
    public string DirectionAction => Descending ? "升序" : "降序";
    public Visibility DirectionVisibility => Show(Sort != MoviePilotTransferSort.Default);

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";
    [ObservableProperty]
    public partial MoviePilotTransferStatusFilter StatusFilter { get; set; }
    [ObservableProperty]
    public partial MoviePilotTransferSort Sort { get; set; }
    [ObservableProperty]
    public partial bool Descending { get; set; }
    [ObservableProperty]
    public partial bool Grouped { get; set; }

    internal void Attach(MoviePilotService service, MoviePilotTransferContext context)
    {
        Query = MoviePilotSeriesTransfer.Query(context.MediaId);
        _service = service;
        _context = context;
        _connection = service.ConnectionStamp;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Query));
    }

    public override async Task ReloadAsync()
    {
        if (_released || _service is null || _context is null) return;
        var token = BeginLoad();
        ClearRows();
        ClearNotice();
        Announce();
        try
        {
            var rows = await _service.FindSeriesTransferHistoriesAsync(_context.MediaId, token).ConfigureAwait(true);
            if (!IsCurrent(token) || _released || !_service.Available || !_service.IsCurrentConnection(_connection)) return;
            foreach (var history in rows)
            {
                var row = new MoviePilotSeriesTransferRow(history);
                row.PropertyChanged += OnRowChanged;
                _allRows.Add(row);
            }
            Rebuild();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token) && !_released) Report("读取当前剧的整理资源失败", error);
        }
        finally
        {
            EndLoad(token);
            if (!_released) Announce();
        }
    }

    internal IReadOnlyList<MoviePilotTransferHistory> Select()
    {
        if (!CanContinue || _context is null) throw new MoviePilotException("请重新读取当前剧的资源并选择整理记录");
        var histories = Rows.Where(row => row.Selected).Select(row => row.History).ToArray();
        MoviePilotSeriesTransfer.CreateContext(_context, histories);
        return histories;
    }

    internal void SelectAll() => UpdateSelection(Rows, true);
    internal void ClearSelection() => UpdateSelection(Rows, false);
    internal void SetSelected(IEnumerable<MoviePilotSeriesTransferRow> rows, bool selected) => UpdateSelection(rows, selected);
    internal void ToggleGroup(MoviePilotSeriesTransferGroup group)
    {
        if (_released || !Groups.Contains(group)) return;
        try { group.Toggle(); }
        finally { PresentationChanged?.Invoke(); }
    }

    private void UpdateSelection(IEnumerable<MoviePilotSeriesTransferRow> rows, bool selected)
    {
        if (_released || (selected && !CanSelect)) return;
        var visible = Rows.ToHashSet();
        _updatingSelection = true;
        try { foreach (var row in rows) if (visible.Contains(row)) row.Selected = selected; }
        finally { _updatingSelection = false; }
        Announce();
        ResourceSelectionChanged?.Invoke();
    }

    internal void SelectionProblem(MoviePilotException error)
    {
        Notify(null, error.Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        Announce();
    }

    internal Task LoadArtworkAsync(MoviePilotSeriesTransferRow row) => _released || _service is null || !Rows.Contains(row)
        ? Task.CompletedTask : row.LoadArtworkAsync(_service, _connection, _lifetime.Token);

    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_released || _updatingSelection || args.PropertyName != nameof(MoviePilotSeriesTransferRow.Selected)) return;
        Announce();
        ResourceSelectionChanged?.Invoke();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnStatusFilterChanged(MoviePilotTransferStatusFilter value) => Rebuild();
    partial void OnSortChanged(MoviePilotTransferSort value)
    {
        if (value == MoviePilotTransferSort.Default && Descending) Descending = false;
        else Rebuild();
    }
    partial void OnDescendingChanged(bool value) => Rebuild();
    partial void OnGroupedChanged(bool value) => Rebuild();
    protected override void BusyChanged() => Announce();

    private void Rebuild()
    {
        if (_released) return;
        var histories = MoviePilotTransferHistoryView.Apply(_allRows.Select(row => row.History), SearchText, StatusFilter, Sort, Descending);
        var byId = _allRows.ToDictionary(row => row.History.Id);
        var visible = histories.Select(history => byId[history.Id]).ToArray();
        var retained = visible.ToHashSet();
        _updatingSelection = true;
        try
        {
            // 上游也会丢弃筛掉的选择；清除筛选不会自动重新授权这些记录。
            foreach (var row in _allRows) if (!retained.Contains(row)) row.Selected = false;
            Rows.Clear();
            foreach (var row in visible) Rows.Add(row);
            Groups.Clear();
            if (Grouped)
                foreach (var group in visible.GroupBy(row => row.Title, StringComparer.Ordinal))
                    Groups.Add(new MoviePilotSeriesTransferGroup(group.Key, group.ToArray(), true));
            else if (visible.Length > 0) Groups.Add(new MoviePilotSeriesTransferGroup("", visible, false));
        }
        finally
        {
            _updatingSelection = false;
            Announce();
            PresentationChanged?.Invoke();
        }
    }

    private void Announce()
    {
        foreach (var name in new[]
        {
            nameof(CountLabel), nameof(SelectedCount), nameof(SelectionLabel), nameof(SubmitLabel), nameof(CanSelect), nameof(CanContinue),
            nameof(EmptyVisibility), nameof(SelectionVisibility), nameof(EmptyTitle), nameof(EmptyMessage), nameof(StatusLabel), nameof(SortLabel),
            nameof(StatusAutomationName), nameof(SortAutomationName), nameof(ViewAutomationName), nameof(ViewGlyph), nameof(DirectionAction), nameof(DirectionVisibility)
        }) OnPropertyChanged(name);
    }

    private void ClearRows()
    {
        foreach (var row in _allRows)
        {
            row.PropertyChanged -= OnRowChanged;
            row.Selected = false;
            row.Retire();
        }
        _allRows.Clear();
        Rows.Clear();
        Groups.Clear();
        PresentationChanged?.Invoke();
    }

    public override void Dispose()
    {
        if (_released) return;
        _released = true;
        _lifetime.Cancel();
        ClearRows();
        base.Dispose();
        Announce();
    }
}

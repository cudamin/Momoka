using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 筛选面板：网页端 <c>modules/filtermenu</c> 那一块。改的是传进来的 <see cref="ItemFilters"/>，改完说一声，
/// 由页面重查并记住。
/// <para>
/// 行与顺序来自 <see cref="EmbyFilterBy.Fields"/> —— 播放状态、收藏、重制、类型、家长评分、工作室、年份、状态，
/// 再是 功能 / 元数据 / 缺少元数据 三组。哪些行出现取决于这一页列的是什么（<c>getVisibleFilters</c> 的那几条
/// 规则），所以每次打开都重建：电视剧库的「节目」页上没有 媒体信息 这一组，电影页上就有。
/// </para>
/// <para>
/// 每一处改动立刻生效，所以没有「确定」键：网格就在面板后面，一个要按确认的面板等于把答案藏起来。
/// </para>
/// </summary>
public sealed partial class FilterPanelViewModel : ObservableObject
{
    /// <summary>
    /// One request per list for as long as this panel lives, kept as the task rather than its result so
    /// two openings of the same row share one round trip. A failed task is dropped, so closing the
    /// panel and opening it again retries instead of remembering the failure.
    /// </summary>
    private readonly Dictionary<string, Task<IReadOnlyList<string>>> _requests = new(StringComparer.Ordinal);

    private ItemFilters _selection = new();
    private string? _preset;
    private Version? _serverVersion;
    private MediaServerKind _serverKind;
    private IReadOnlyList<EmbyFilterField>? _fields;
    private Func<string, CancellationToken, Task<IReadOnlyList<string>>>? _lookup;
    private CancellationTokenSource? _cancel;

    /// <summary>
    /// Something was ticked. The page reloads its grid and writes the selection down.
    /// </summary>
    internal event EventHandler? Changed;

    /// <summary>The rows on screen, in order; see <see cref="FilterRow"/>.</summary>
    public ObservableCollection<FilterRow> Rows { get; } = [];

    /// <summary>
    /// What is on, in words. The button outside only has room for a number, and a panel that has been
    /// scrolled away from its ticks needs to be able to say so.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "全部内容";

    /// <summary>Every choice this panel is offering, for the self-check to report on.</summary>
    internal IReadOnlyList<string> Labels => [.. Rows.SelectMany(row => row.Labels)];

    /// <summary>The row labels in order, headings excluded — the panel's own structure.</summary>
    internal IReadOnlyList<string> RowLabels => [.. Rows.Select(row => row.Label)];

    /// <summary>
    /// IsBlank rather than IsEmpty, so an id this build does not recognise can still be cleared —
    /// otherwise it is unreachable: not counted, not shown, never removed.
    /// </summary>
    private bool CanClear => !_selection.IsBlank;

    /// <summary>
    /// Rebuilds the panel for the grid it is about to be shown over. Called on every open rather than
    /// once: which rows apply depends on what the grid lists and on a server version that may still
    /// have been in flight when the page opened.
    /// </summary>
    internal void Open(
        ItemFilters selection,
        string? preset,
        Version? serverVersion,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> lookup,
        MediaServerKind serverKind = MediaServerKind.Emby,
        IReadOnlyList<EmbyFilterField>? fields = null)
    {
        _requests.Clear();
        _selection = selection;
        _preset = preset;
        _serverVersion = serverVersion;
        _serverKind = serverKind;
        _fields = fields;
        _lookup = lookup;

        // 取消过的那个不能再用：Dismiss() 只取消、不换新的，而取消是一去不回的 —— 接着开的话每一次
        // Lookup 拿到的都是一个已经取消的令牌，四张按需去问服务器的表会一直是空的，日志里一句话都没有。
        if (_cancel is null || _cancel.IsCancellationRequested)
        {
            _cancel = new CancellationTokenSource();

            // 缓存里要扔的只有被取消连坐、没跑完的那几份；已经拿到答案的照旧有效。
            foreach (var key in _requests
                .Where(entry => !entry.Value.IsCompletedSuccessfully)
                .Select(entry => entry.Key)
                .ToList())
            {
                _requests.Remove(key);
            }
        }

        Build();
    }

    /// <summary>
    /// The page is leaving. Not called when the panel merely closes: a list still arriving is worth
    /// keeping, since the next open would otherwise ask for it again.
    /// </summary>
    internal void Dismiss() => _cancel?.Cancel();

    /// <inheritdoc cref="_requests"/>
    /// <returns>Null when there is nothing to ask yet, which is a panel built before its page attached.</returns>
    internal Task<IReadOnlyList<string>>? Lookup(string key)
    {
        if (_lookup is null || _cancel is null) return null;
        if (_requests.TryGetValue(key, out var cached)) return cached;

        var request = _lookup(key, _cancel.Token);
        _requests[key] = request;
        return request;
    }

    /// <summary>Drops a failed request, so opening its row again asks the server rather than the cache.</summary>
    internal void Forget(string key) => _requests.Remove(key);

    /// <summary>The selection changed. Redraws what this panel says about itself, then tells the page.</summary>
    internal void Notify()
    {
        ShowSummary();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A rebuild rather than a walk over the controls: clearing also resets every dropdown to 全部 and
    /// closes every open list, which is the state a panel with nothing on should be in.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        if (!_selection.Clear()) return;

        Build();
        Notify();
    }

    private void Build()
    {
        Rows.Clear();

        var fields = _fields ?? EmbyFilterBy.For(_preset, _serverVersion, _serverKind);
        string? previousGroup = null;

        foreach (var field in fields)
        {
            // The heading goes in front of the first row of its group, not above the panel: 播放状态 has
            // no group at all, and 功能 starts a heading mid-list.
            var heading = field.Group is { } group && group != previousGroup ? group : null;
            previousGroup = field.Group;

            Rows.Add(Row(field, heading));
        }

        ShowSummary();
    }

    private FilterRow Row(EmbyFilterField field, string? heading) => field.Kind switch
    {
        FilterKind.Select => new FilterSelectRow(field, heading, _preset, _selection, Notify),
        FilterKind.Switch => new FilterSwitchRow(field, heading, _selection, Notify),
        FilterKind.Checks => new FilterChecksRow(field, heading, _selection, Notify),
        _ => new FilterValuesRow(field, List(field.ListKey), _selection, this, heading)
    };

    private static EmbyFilterList List(string? key) =>
        EmbyFilterBy.Lists.First(list => list.Key == key);

    private void ShowSummary()
    {
        Summary = _selection.IsEmpty ? "全部内容" : _selection.Describe(_fields);
        ClearCommand.NotifyCanExecuteChanged();
    }
}

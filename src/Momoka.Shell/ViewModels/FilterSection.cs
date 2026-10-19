using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Diagnostics;
using Momoka.Emby;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// One tickable box in a <see cref="FilterValuesRow"/> — one genre, parental rating, studio, tag or year
/// the server reported.
/// <para>
/// The tick writes through a pair of delegates handed over at construction, which is the shape
/// <see cref="SettingRow"/> already uses and for the same reason: the access stays compiled and checked,
/// so a renamed member is a build error rather than a box that silently stops filtering.
/// </para>
/// </summary>
public sealed partial class FilterCheck : ObservableObject
{
    private readonly Action<bool> _write;
    private readonly bool _seeded;
    private bool _syncing;

    internal FilterCheck(string label, bool isChecked, Action<bool> write)
    {
        Label = label;
        _write = write;

        IsChecked = isChecked;

        // Set last, so seeding the box above did not count as ticking it.
        _seeded = true;
    }

    /// <summary>What the box says: the genre, rating, studio, tag or year itself.</summary>
    public string Label { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>
    /// Redraws the box without counting as a tick. A checkbox group whose options exclude each other
    /// (已播放/未播放, HD/SD) has to uncheck the other box when one is chosen; writing through
    /// <see cref="IsChecked"/> would call back into the selection and could undo the choice.
    /// </summary>
    internal void Set(bool value)
    {
        if (IsChecked == value) return;

        _syncing = true;
        try { IsChecked = value; }
        finally { _syncing = false; }
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_seeded || _syncing) return;
        _write(value);
    }
}

/// <summary>
/// One row of the filter panel: a label with a control under it, exactly as Emby's own filter dialog
/// draws it (<c>modules/filtermenu/filtermenu.template.html</c>).
/// <para>
/// Three shapes — a dropdown, a switch, a dropdown of checkboxes — and one heading in front of the first
/// row of each group (功能 / 元数据 / 缺少元数据). The rows are data and the shapes are markup, which is
/// the arrangement the settings page already uses: <see cref="Views.FilterSectionTemplates"/> picks the
/// shape for each.
/// </para>
/// </summary>
public abstract class FilterRow : ObservableObject
{
    protected FilterRow(EmbyFilterField field, string? heading)
    {
        Field = field;
        Heading = heading;
    }

    /// <summary>The catalogue row this one draws.</summary>
    public EmbyFilterField Field { get; }

    /// <summary>播放状态, 类型, 预告片, IMDb Id.</summary>
    public string Label => Field.Label;

    /// <summary>
    /// The group heading to draw above this row, or null. Only the first row of a group carries it, so
    /// the heading appears once and the rows under it stay a plain column.
    /// </summary>
    public string? Heading { get; }

    public Visibility HeadingVisibility => Heading is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>What this row is offering, for the self-check to report on.</summary>
    internal abstract IEnumerable<string> Labels { get; }
}

/// <summary>
/// A dropdown of mutually exclusive choices — 播放状态, 状态, 预告片, 已锁定. The web client draws these as
/// <c>&lt;select&gt;</c> boxes; a <see cref="Microsoft.UI.Xaml.Controls.ComboBox"/> is the same thing.
/// </summary>
public sealed partial class FilterSelectRow : FilterRow
{
    private readonly ItemFilters _selection;
    private readonly Action _changed;
    private readonly IReadOnlyList<FilterChoice> _choices;
    private readonly bool _seeded;

    internal FilterSelectRow(EmbyFilterField field, string? heading, string? preset, ItemFilters selection, Action changed)
        : base(field, heading)
    {
        _selection = selection;
        _changed = changed;
        _choices = field.ChoicesFor(preset);
        Choices = [.. _choices.Select(choice => choice.Label)];

        var current = selection.Choice(field);
        var index = _choices.ToList().IndexOf(current);
        SelectedIndex = index < 0 ? 0 : index;

        _seeded = true;
    }

    /// <summary>The choice labels, 全部 first — what the dropdown shows.</summary>
    public IReadOnlyList<string> Choices { get; }

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    internal override IEnumerable<string> Labels => Choices;

    partial void OnSelectedIndexChanged(int value)
    {
        if (!_seeded) return;
        if (value < 0 || value >= _choices.Count) return;

        if (_selection.Select(Field, _choices[value])) _changed();
    }

}

/// <summary>
/// A switch — 收藏, 重制, and the three 缺少元数据 rows, where the switch being on means the id is
/// <b>missing</b> and the query therefore carries <c>false</c>.
/// </summary>
public sealed partial class FilterSwitchRow : FilterRow
{
    private readonly ItemFilters _selection;
    private readonly Action _changed;
    private readonly bool _seeded;

    internal FilterSwitchRow(EmbyFilterField field, string? heading, ItemFilters selection, Action changed)
        : base(field, heading)
    {
        _selection = selection;
        _changed = changed;
        IsOn = selection.IsOn(field);
        _seeded = true;
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    internal override IEnumerable<string> Labels => [Label];

    partial void OnIsOnChanged(bool value)
    {
        if (!_seeded) return;

        if (Field.Choices.FirstOrDefault()?.OptionId is not { } id) return;
        if (EmbyFilterBy.Find(id) is not { } option) return;

        if (_selection.Set(option, value)) _changed();
    }

}

/// <summary>
/// A row of independent checkboxes — Jellyfin's 播放状态, 视频类型, 功能 and 剧集 groups.
/// <para>
/// Several boxes can be on at once. The catalogue options themselves carry the exclusions that matter
/// (已播放/未播放 and HD/SD), and <see cref="Refresh"/> redraws the sibling boxes after a change so a
/// selection that was cleared by an exclusion disappears from the panel too.
/// </para>
/// </summary>
public sealed partial class FilterChecksRow : FilterRow
{
    private readonly ItemFilters _selection;
    private readonly Action _changed;
    private readonly List<(string Id, FilterCheck Check)> _checks = [];

    internal FilterChecksRow(EmbyFilterField field, string? heading, ItemFilters selection, Action changed)
        : base(field, heading)
    {
        _selection = selection;
        _changed = changed;

        foreach (var choice in field.Choices)
        {
            if (choice.OptionId is not { } id) continue;

            var check = new FilterCheck(choice.Label, selection.Has(id), on => Toggle(id, on));
            Values.Add(check);
            _checks.Add((id, check));
        }
    }

    public ObservableCollection<FilterCheck> Values { get; } = [];

    internal override IEnumerable<string> Labels => Values.Select(value => value.Label);

    private void Toggle(string id, bool on)
    {
        if (EmbyFilterBy.Find(id) is not { } option) return;
        if (_selection.Set(option, on))
        {
            Refresh();
            _changed();
        }
    }

    private void Refresh()
    {
        foreach (var (id, check) in _checks) check.Set(_selection.Has(id));
    }
}

/// <summary>
/// A dropdown of checkboxes whose values are not in any catalogue, because they are whatever the rows
/// this grid is listing happen to have — 类型, 家长评分, 工作室, 标签, 年份.
/// <para>
/// The web client draws these as a <c>&lt;select multiple&gt;</c>: closed it says 全部, opened it is a
/// list you tick several of. That is a button with a flyout here, and the request for the list starts
/// when the flyout is first opened rather than when the panel is.
/// </para>
/// </summary>
/// </summary>
public sealed partial class FilterValuesRow : FilterRow
{
    private const string Category = "筛选";

    private readonly EmbyFilterList _list;
    private readonly ItemFilters _selection;
    private readonly FilterPanelViewModel _owner;

    internal FilterValuesRow(EmbyFilterField field, EmbyFilterList list, ItemFilters selection, FilterPanelViewModel owner, string? heading)
        : base(field, heading)
    {
        _list = list;
        _selection = selection;
        _owner = owner;

        ShowSummary();
    }

    public ObservableCollection<FilterCheck> Values { get; } = [];

    /// <summary>全部, or 已选 N 项 — what the closed dropdown says.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "全部";

    /// <summary>正在读取…, a failure, or 这里没有可选的类型. Collapsed once there are boxes to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteVisibility))]
    public partial string Note { get; set; } = "";

    /// <summary>Two-way: the flyout's own open state, and the trigger for the request.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public Visibility NoteVisibility => Note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    internal override IEnumerable<string> Labels => [Label];

    partial void OnIsOpenChanged(bool value)
    {
        if (value) _ = FillAsync();
    }

    /// <summary>
    /// Fills this row from the server. Reentrant on purpose — a row opened twice is opened again soon
    /// enough — and both callers await the one request, which the panel keeps.
    /// </summary>
    private async Task FillAsync()
    {
        if (_owner.Lookup(_list.Key) is not { } request) return;

        if (!request.IsCompleted)
        {
            Values.Clear();
            Note = "正在读取…";
        }

        try
        {
            Render(await request.ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
            _owner.Forget(_list.Key);
        }
        catch (Exception error)
        {
            _owner.Forget(_list.Key);
            Log.Warn(Category, $"读取「{_list.Label}」的可选值失败", error);

            Values.Clear();
            Note = Failure.Describe(error);
        }
    }

    private void Render(IReadOnlyList<string> values)
    {
        Values.Clear();

        // Anything already ticked that the server did not offer goes in anyway: a value that vanished
        // from the library — renamed, or filtered out by something else — must stay untickable-off.
        var chosen = _selection.Values(_list.Key);
        var all = values.Concat(chosen.Where(value => !values.Contains(value))).ToList();

        if (all.Count == 0)
        {
            Note = $"这里没有可选的{Label}";
            return;
        }

        Note = "";

        foreach (var value in all) Values.Add(Check(value));
    }

    private FilterCheck Check(string value) => new(
        value,
        _selection.Values(_list.Key).Contains(value),
        on =>
        {
            if (!_selection.SetValue(_list.Key, value, on)) return;

            ShowSummary();
            _owner.Notify();
        });

    private void ShowSummary()
    {
        var count = _selection.Values(_list.Key).Count;
        Summary = count == 0 ? "全部" : $"已选 {count} 项";
    }
}

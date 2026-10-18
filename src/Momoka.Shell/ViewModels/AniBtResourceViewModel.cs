using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.AniBt;
using Momoka.MoviePilot;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>AniBT 发布列表的一行：发布会话里的字幕组、集号、清晰度，下载仍交给 MoviePilot。</summary>
public sealed partial class AniBtResourceRow : ObservableObject
{
    private readonly Func<AniBtResourceRow, Task> _download;
    private readonly Action<AniBtResourceRow>? _openDetails;

    public AniBtResourceRow(AniBtResource item, Func<AniBtResourceRow, Task> download,
        Action<AniBtResourceRow>? openDetails = null)
    {
        Item = item;
        _download = download;
        _openDetails = openDetails;
    }

    public AniBtResource Item { get; }
    public AniBtRelease Release => Item.Release;
    public MoviePilotResource Resource => Item.Resource;
    public string Title => Release.Title;
    public string EpisodeBadge => AniBtReleaseLabels.EpisodeBadge(Release.EpisodeKey);
    public string EpisodeChoice => AniBtReleaseLabels.EpisodeChoice(Release.EpisodeKey);
    public string GroupName => AniBtReleaseLabels.Group(Release.GroupName);

    /// <summary>筛选按 RSS 原值比，不按「未标…」这些给人看的占位字比。</summary>
    public string RawName => (Title + "\n" + Release.GroupName + "\n" + Release.Resolution + "\n" + Release.EpisodeKey)
        .ToLowerInvariant();

    public string PublishedText => AniBtReleaseLabels.Published(Release.PublishedAt, DateTimeOffset.Now);
    public bool IsRecent => Release.PublishedAt is { } published
        && DateTimeOffset.Now - published is { Ticks: > 0 } elapsed && elapsed < TimeSpan.FromDays(1);
    public Visibility RecentVisibility => Show(IsRecent);

    /// <summary>标题下面那一行：字幕组、清晰度、体积、相对时间。缺的项直接不写。</summary>
    public string MetaLine => string.Join(" · ", new[]
    {
        Release.GroupName.Trim().Length > 0 ? Release.GroupName.Trim() : null,
        Release.Resolution.Trim().Length > 0 ? Release.Resolution.Trim() : null,
        Resource.SizeText,
        PublishedText
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadLabel))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial MoviePilotDownloadState State { get; set; }

    public bool CanDownload => Resource.DownloadProblem is null &&
        State is MoviePilotDownloadState.Idle or MoviePilotDownloadState.Failed;

    public string DownloadLabel => Resource.DownloadProblem is not null ? "身份待核对" : State switch
    {
        MoviePilotDownloadState.Working => "添加中…",
        MoviePilotDownloadState.Done => "已加入下载",
        MoviePilotDownloadState.Failed => "重试下载",
        MoviePilotDownloadState.Uncertain => "请先核对",
        _ => "下载"
    };

    public string DownloadAutomationName => $"下载：{Title}";
    public string DetailsAutomationName => $"打开 AniBT 发布页：{Title}";
    public bool CanOpenDetails => Resource.DetailsUri is not null && _openDetails is not null;

    [RelayCommand(CanExecute = nameof(CanOpenDetails))]
    private void OpenDetails() => _openDetails?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task Download() => _download(this);

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// AniBT 这一页自己的筛选与排序：集号、字幕组、清晰度和关键词都按 RSS 原值来，不借用 MoviePilot
/// 的站点、优惠、季号与 HR 概念。集合实例稳定，改筛选用原地增删，不把行对象换掉。
/// </summary>
public sealed partial class AniBtBrowserViewModel : ObservableObject
{
    public const string AllEpisodes = "全部集数";
    public const string AllResolutions = "全部清晰度";
    public const string AllGroups = "全部字幕组";

    private readonly List<AniBtResourceRow> _all = [];
    private bool _resetting;

    public ObservableCollection<AniBtResourceRow> Rows { get; } = [];
    public ObservableCollection<string> Episodes { get; } = [AllEpisodes];
    public ObservableCollection<string> Resolutions { get; } = [AllResolutions];
    public ObservableCollection<string> Groups { get; } = [AllGroups];

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string SelectedEpisode { get; set; } = AllEpisodes;
    [ObservableProperty] public partial string SelectedResolution { get; set; } = AllResolutions;
    [ObservableProperty] public partial string SelectedGroup { get; set; } = AllGroups;
    [ObservableProperty] public partial int SortIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial string EmptyMessage { get; set; } = "";

    public Visibility EmptyVisibility => Show(_all.Count == 0 && EmptyMessage.Length > 0);
    public Visibility FiltersVisibility => Show(_all.Count > 0);
    public Visibility NoMatchesVisibility => Show(_all.Count > 0 && Rows.Count == 0);
    public string CountText => _all.Count == 0 ? "" : $"{Rows.Count} / {_all.Count}";
    public Visibility ResetVisibility => Show(HasFilters);
    private bool HasFilters => Text.Trim().Length > 0 || IsChosen(SelectedEpisode, AllEpisodes)
        || IsChosen(SelectedResolution, AllResolutions) || IsChosen(SelectedGroup, AllGroups);

    internal void SetResults(IEnumerable<AniBtResourceRow> rows, string emptyMessage)
    {
        _all.Clear();
        _all.AddRange(rows);
        EmptyMessage = emptyMessage;

        // 先把选项填好、再写选中项：绑定在 ItemsSource 换掉的那一拍会把 SelectedItem 写成空，
        // 反过来做就会把刚写好的「全部」当场抹掉（探针在 2026-10-19 抓到过）。
        _resetting = true;
        FillChoices(Episodes, AllEpisodes, _all.Select(row => row.EpisodeChoice), EpisodeOrder);
        FillChoices(Resolutions, AllResolutions, _all
            .Where(row => row.Release.Resolution.Trim().Length > 0)
            .Select(row => row.Release.Resolution.Trim()), ResolutionOrder);
        FillChoices(Groups, AllGroups, _all
            .Where(row => row.Release.GroupName.Trim().Length > 0)
            .Select(row => row.GroupName), StringComparer.CurrentCultureIgnoreCase);
        Text = "";
        SelectedEpisode = AllEpisodes;
        SelectedResolution = AllResolutions;
        SelectedGroup = AllGroups;
        SortIndex = 0;
        _resetting = false;

        OnPropertyChanged(nameof(FiltersVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
        Apply();
    }

    internal void Clear()
    {
        _all.Clear();
        _resetting = true;
        Text = "";
        SelectedEpisode = AllEpisodes;
        SelectedResolution = AllResolutions;
        SelectedGroup = AllGroups;
        SortIndex = 0;
        Episodes.Clear(); Episodes.Add(AllEpisodes);
        Resolutions.Clear(); Resolutions.Add(AllResolutions);
        Groups.Clear(); Groups.Add(AllGroups);
        _resetting = false;
        Rows.Clear();
        EmptyMessage = "";
        OnPropertyChanged(nameof(FiltersVisibility));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(NoMatchesVisibility));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ResetVisibility));
    }

    [RelayCommand]
    private void ResetFilters()
    {
        _resetting = true;
        Text = "";
        SelectedEpisode = AllEpisodes;
        SelectedResolution = AllResolutions;
        SelectedGroup = AllGroups;
        _resetting = false;
        Apply();
    }

    partial void OnTextChanged(string value) => Apply();
    partial void OnSelectedEpisodeChanged(string value) => Apply();
    partial void OnSelectedResolutionChanged(string value) => Apply();
    partial void OnSelectedGroupChanged(string value) => Apply();
    partial void OnSortIndexChanged(int value) => Apply();

    private void Apply()
    {
        if (_resetting) return;

        IEnumerable<AniBtResourceRow> query = _all;
        var terms = Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length > 0)
            query = query.Where(row => terms.All(term => row.RawName.Contains(term, StringComparison.OrdinalIgnoreCase)));
        // 下拉在 ItemsSource 换掉时会把 SelectedItem 写成空；空就是「全部」，不能当成一条必须匹配的值。
        if (IsChosen(SelectedEpisode, AllEpisodes))
            query = query.Where(row => row.EpisodeChoice == SelectedEpisode);
        if (IsChosen(SelectedResolution, AllResolutions))
            query = query.Where(row => row.Release.Resolution.Trim() == SelectedResolution);
        if (IsChosen(SelectedGroup, AllGroups))
            query = query.Where(row => row.GroupName == SelectedGroup);

        query = SortIndex switch
        {
            1 => query.OrderBy(row => row.Release.PublishedAt ?? DateTimeOffset.MinValue),
            2 => query.OrderByDescending(row => row.Resource.Size),
            3 => query.OrderBy(row => row.Resource.Size),
            _ => query.OrderByDescending(row => row.Release.PublishedAt ?? DateTimeOffset.MinValue)
        };

        Sync(Rows, query.ToList());
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(NoMatchesVisibility));
        OnPropertyChanged(nameof(ResetVisibility));
    }

    private static void Sync(ObservableCollection<AniBtResourceRow> rows, IReadOnlyList<AniBtResourceRow> desired)
    {
        var keep = desired.ToHashSet();
        for (var index = rows.Count - 1; index >= 0; index--)
            if (!keep.Contains(rows[index])) rows.RemoveAt(index);
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < rows.Count && ReferenceEquals(rows[index], desired[index])) continue;
            var existing = rows.IndexOf(desired[index]);
            if (existing >= 0) rows.Move(existing, index);
            else rows.Insert(index, desired[index]);
        }
    }

    private static void FillChoices(ObservableCollection<string> choices, string all,
        IEnumerable<string> values, Func<string, string> key)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).OrderBy(key, StringComparer.CurrentCultureIgnoreCase).ToArray();
        choices.Clear();
        choices.Add(all);
        foreach (var value in distinct) choices.Add(value);
    }

    private static void FillChoices(ObservableCollection<string> choices, string all,
        IEnumerable<string> values, IComparer<string> comparer)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).OrderBy(value => value, comparer).ToArray();
        choices.Clear();
        choices.Add(all);
        foreach (var value in distinct) choices.Add(value);
    }

    private static string EpisodeOrder(string value) =>
        value == AniBtReleaseLabels.UnknownEpisode || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? "999999" + value : number.ToString("D8", CultureInfo.InvariantCulture);

    private static string ResolutionOrder(string value)
    {
        var digits = new string(value.TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? (9999 - number).ToString("D4", CultureInfo.InvariantCulture) + value : value;
    }

    private static bool IsChosen(string? value, string all) => value is { Length: > 0 } && value != all;

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

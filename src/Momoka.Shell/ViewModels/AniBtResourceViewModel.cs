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
    public string SizeText => Resource.SizeText;
    public string TechnicalText => string.Join(" · ", new[] { Release.Format, Release.Subtitle }
        .Concat(Release.Tags ?? []).Where(value => !string.IsNullOrWhiteSpace(value)));
    public string LanguageText => string.Join(" + ", Release.Languages ?? []);
    public string ResolutionText => Release.Resolution;
    public string EpisodeText => int.TryParse(Release.EpisodeKey, out var number) ? $"第 {number} 集" : AniBtReleaseLabels.EpisodeChoice(Release.EpisodeKey);
    public Visibility LanguageVisibility => Show(LanguageText.Length > 0);
    public Visibility ResolutionVisibility => Show(ResolutionText.Length > 0);
    public Visibility DownloadVisibility => Show(Release.HasTorrent);

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

public sealed record AniBtProgressCell(int Episode, bool Published)
{
    public string Label => Episode.ToString(CultureInfo.InvariantCulture);
    public string Description => $"第 {Episode} 集：{(Published ? "已发布" : "未发布")}";
    public double FillOpacity => Published ? 1 : 0.12;
}

public sealed partial class AniBtResourceGroup : ObservableObject
{
    public AniBtResourceGroup(string name, IReadOnlyList<AniBtResourceRow> rows, AniBtReleaseGroup? source, int? episodes)
    {
        Name = name;
        Rows = rows;
        RssUri = source?.RssUri;
        Total = source?.TotalReleases ?? rows.Count;
        var published = source?.PublishedEpisodes.Where(episode => episode > 0 && (episodes is not > 0 || episode <= episodes)).ToHashSet();
        PublishedText = published is null ? "" : episodes is > 0 ? $"{published.Count} / {episodes} 集" : $"已发布 {published.Count} 集";
        if (published is not null && episodes is > 0 and <= 60)
            foreach (var episode in Enumerable.Range(1, episodes.Value)) Cells.Add(new AniBtProgressCell(episode, published.Contains(episode)));
        Metadata = string.Join(" · ", rows.SelectMany(row => new[] { row.LanguageText, row.Release.Format })
            .Where(value => value.Length > 0).Distinct());
        ApplyRows(rows);
    }

    public string Name { get; }
    public string Initial => Name.Length == 0 ? "?" : Name[..1];
    public IReadOnlyList<AniBtResourceRow> Rows { get; }
    public ObservableCollection<AniBtResourceRow> VisibleRows { get; } = [];
    public ObservableCollection<AniBtProgressCell> Cells { get; } = [];
    public Uri? RssUri { get; }
    public int Total { get; }
    public string Metadata { get; }
    public string PublishedText { get; }
    public string CountText => $"{Total} 条";
    public Visibility RssVisibility => RssUri is not null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MoreVisibility => _filtered.Count > 5 ? Visibility.Visible : Visibility.Collapsed;
    public string MoreText => Expanded ? "收起" : $"展开其余 {_filtered.Count - 5} 条";
    public string ScopeText => Total > Rows.Count ? $"展示最近 {Rows.Count} / {Total} 条" : "";
    public Visibility ScopeVisibility => ScopeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    private IReadOnlyList<AniBtResourceRow> _filtered = [];

    [ObservableProperty] public partial bool IsOpen { get; set; } = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreText))]
    public partial bool Expanded { get; set; }

    partial void OnExpandedChanged(bool value) => Refresh();
    [RelayCommand] private void ToggleMore() => Expanded = !Expanded;
    internal void ApplyRows(IReadOnlyList<AniBtResourceRow> rows)
    {
        _filtered = rows;
        Refresh();
        OnPropertyChanged(nameof(MoreVisibility));
        OnPropertyChanged(nameof(MoreText));
    }

    private void Refresh()
    {
        VisibleRows.Clear();
        foreach (var row in Expanded ? _filtered : _filtered.Take(5)) VisibleRows.Add(row);
    }
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
    private readonly List<AniBtResourceGroup> _groups = [];
    private bool _hasProgress;

    public ObservableCollection<AniBtResourceRow> Rows { get; } = [];
    public ObservableCollection<AniBtResourceGroup> ResourceGroups { get; } = [];
    public ObservableCollection<AniBtResourceGroup> ProgressGroups { get; } = [];
    public ObservableCollection<string> Episodes { get; } = [AllEpisodes];
    public ObservableCollection<string> Resolutions { get; } = [AllResolutions];
    public ObservableCollection<string> Groups { get; } = [AllGroups];

    [ObservableProperty] public partial string Text { get; set; } = "";
    [ObservableProperty] public partial string SelectedEpisode { get; set; } = AllEpisodes;
    [ObservableProperty] public partial string SelectedResolution { get; set; } = AllResolutions;
    [ObservableProperty] public partial string SelectedGroup { get; set; } = AllGroups;
    [ObservableProperty] public partial int SortIndex { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(ResourcesVisibility))]
    public partial int ViewIndex { get; set; }

    public Visibility ProgressVisibility => Show(ViewIndex == 0 && _hasProgress);
    public Visibility ResourcesVisibility => Show(ViewIndex != 0 || !_hasProgress);
    public Visibility TabsVisibility => Show(_hasProgress);
    public string ProgressTabText => $"发布进度  {ProgressGroups.Count} 组";
    public string ResourcesTabText => $"资源  {_groups.Sum(group => group.Total)} 条";

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

    internal void SetResults(IEnumerable<AniBtResourceRow> rows, string emptyMessage,
        IReadOnlyList<AniBtReleaseGroup>? groups = null, AniBtAnime? anime = null)
    {
        _all.Clear();
        _all.AddRange(rows);
        _groups.Clear();
        _hasProgress = groups is { Count: > 0 };
        if (groups is not null)
            foreach (var group in groups)
            {
                var ids = group.Items.Select(release => release.ReleaseId).ToHashSet();
                _groups.Add(new AniBtResourceGroup(group.Name, _all.Where(row => ids.Contains(row.Release.ReleaseId)).ToArray(), group, anime?.EpisodeCount));
            }
        else
            foreach (var group in _all.GroupBy(row => row.GroupName))
                _groups.Add(new AniBtResourceGroup(group.Key, group.ToArray(), null, anime?.EpisodeCount));
        ProgressGroups.Clear();
        foreach (var group in _groups) ProgressGroups.Add(group);
        ViewIndex = _hasProgress ? 0 : 1;
        AnnounceGroups();
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
        _groups.Clear();
        ResourceGroups.Clear();
        ProgressGroups.Clear();
        _hasProgress = false;
        AnnounceGroups();
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
        ResourceGroups.Clear();
        foreach (var group in _groups)
        {
            var visible = Rows.Where(group.Rows.Contains).ToArray();
            group.ApplyRows(visible);
            if (visible.Length > 0) ResourceGroups.Add(group);
        }
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(NoMatchesVisibility));
        OnPropertyChanged(nameof(ResetVisibility));
    }

    private void AnnounceGroups()
    {
        OnPropertyChanged(nameof(TabsVisibility));
        OnPropertyChanged(nameof(ProgressVisibility));
        OnPropertyChanged(nameof(ResourcesVisibility));
        OnPropertyChanged(nameof(ProgressTabText));
        OnPropertyChanged(nameof(ResourcesTabText));
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

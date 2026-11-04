using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Momoka.AniBt;
using Momoka.MoviePilot;

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
    [NotifyPropertyChangedFor(nameof(DownloadGlyph))]
    [NotifyPropertyChangedFor(nameof(DownloadAutomationName))]
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

    public string DownloadGlyph => Resource.DownloadProblem is not null ? "\uE7BA" : State switch
    {
        MoviePilotDownloadState.Working => "\uE895",
        MoviePilotDownloadState.Done => "\uE73E",
        MoviePilotDownloadState.Failed => "\uE72C",
        MoviePilotDownloadState.Uncertain => "\uE7BA",
        _ => "\uE896"
    };

    public string DownloadAutomationName => $"{DownloadLabel}：{Title}";
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
        LogoUrl = source?.LogoUrl;
        Total = source?.TotalReleases ?? rows.Count;
        var published = source?.PublishedEpisodes.Where(episode => episode > 0 && (episodes is not > 0 || episode <= episodes)).ToHashSet();
        PublishedText = published is null ? "" : episodes is > 0 ? $"{published.Count} / {episodes} 集" : $"已发布 {published.Count} 集";
        if (published is not null && episodes is > 0 and <= 60)
            foreach (var episode in Enumerable.Range(1, episodes.Value)) Cells.Add(new AniBtProgressCell(episode, published.Contains(episode)));
        Metadata = string.Join(" · ", rows.SelectMany(row => new[] { row.LanguageText, row.Release.Format })
            .Where(value => value.Length > 0).Distinct());
        Refresh();
    }

    public string Name { get; }
    public string? LogoUrl { get; }
    public Visibility LogoVisibility => !_logoFailed && !string.IsNullOrWhiteSpace(LogoUrl)
        ? Visibility.Visible : Visibility.Collapsed;
    private bool _logoFailed;

    internal void MarkLogoFailed()
    {
        if (_logoFailed) return;
        _logoFailed = true;
        OnPropertyChanged(nameof(LogoVisibility));
    }
    public string Initial => Name.Length == 0 ? "?" : Name[..1];
    public IReadOnlyList<AniBtResourceRow> Rows { get; }
    public ObservableCollection<AniBtResourceRow> VisibleRows { get; } = [];
    public ObservableCollection<AniBtProgressCell> Cells { get; } = [];
    public Uri? RssUri { get; }
    public int Total { get; }
    public string Metadata { get; }
    public string PublishedText { get; }
    public string CountText => $"{Total} 条";
    public Visibility MoreVisibility => Rows.Count > 5 ? Visibility.Visible : Visibility.Collapsed;
    public string MoreText => Expanded ? "收起" : $"展开其余 {Rows.Count - 5} 条";
    public string ScopeText => Total > Rows.Count ? $"展示最近 {Rows.Count} / {Total} 条" : "";
    public Visibility ScopeVisibility => ScopeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] public partial bool IsOpen { get; set; } = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreText))]
    public partial bool Expanded { get; set; }

    partial void OnExpandedChanged(bool value) => Refresh();
    [RelayCommand] private void ToggleMore() => Expanded = !Expanded;
    private void Refresh()
    {
        VisibleRows.Clear();
        foreach (var row in Expanded ? Rows : Rows.Take(5)) VisibleRows.Add(row);
    }
}

/// <summary>完整发布进度与按字幕组排列的资源；发布默认按时间从新到旧，不保留隐藏筛选状态。</summary>
public sealed partial class AniBtBrowserViewModel : ObservableObject
{
    private bool _hasProgress;

    public ObservableCollection<AniBtResourceRow> Rows { get; } = [];
    public ObservableCollection<AniBtResourceGroup> ResourceGroups { get; } = [];
    public ObservableCollection<AniBtResourceGroup> ProgressGroups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(ResourcesVisibility))]
    public partial int ViewIndex { get; set; }

    public Visibility ProgressVisibility => Show(ViewIndex == 0 && _hasProgress);
    public Visibility ResourcesVisibility => Show(ViewIndex != 0 || !_hasProgress);
    public Visibility TabsVisibility => Show(_hasProgress);
    public string ProgressTabText => $"发布进度  {ProgressGroups.Count} 组";
    public string ResourcesTabText => $"资源  {ProgressGroups.Sum(group => group.Total)} 条";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial string EmptyMessage { get; set; } = "";

    public Visibility EmptyVisibility => Show(Rows.Count == 0 && EmptyMessage.Length > 0);

    internal void SetResults(IEnumerable<AniBtResourceRow> rows, string emptyMessage,
        IReadOnlyList<AniBtReleaseGroup>? groups = null, AniBtAnime? anime = null)
    {
        var ordered = rows.OrderByDescending(row => row.Release.PublishedAt ?? DateTimeOffset.MinValue).ToArray();
        Rows.Clear();
        foreach (var row in ordered) Rows.Add(row);
        ResourceGroups.Clear();
        ProgressGroups.Clear();
        _hasProgress = groups is { Count: > 0 };
        if (groups is not null)
            foreach (var group in groups)
            {
                var ids = group.Items.Select(release => release.ReleaseId).ToHashSet();
                AddGroup(new AniBtResourceGroup(group.Name, Rows.Where(row => ids.Contains(row.Release.ReleaseId)).ToArray(), group, anime?.EpisodeCount));
            }
        else
            foreach (var group in Rows.GroupBy(row => row.GroupName))
                AddGroup(new AniBtResourceGroup(group.Key, group.ToArray(), null, anime?.EpisodeCount));
        ViewIndex = _hasProgress ? 0 : 1;
        EmptyMessage = emptyMessage;
        AnnounceGroups();
    }

    private void AddGroup(AniBtResourceGroup group)
    {
        ProgressGroups.Add(group);
        if (group.Rows.Count > 0) ResourceGroups.Add(group);
    }

    internal void Clear()
    {
        Rows.Clear();
        ResourceGroups.Clear();
        ProgressGroups.Clear();
        _hasProgress = false;
        EmptyMessage = "";
        AnnounceGroups();
    }

    private void AnnounceGroups()
    {
        OnPropertyChanged(nameof(TabsVisibility));
        OnPropertyChanged(nameof(ProgressVisibility));
        OnPropertyChanged(nameof(ResourcesVisibility));
        OnPropertyChanged(nameof(ProgressTabText));
        OnPropertyChanged(nameof(ResourcesTabText));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

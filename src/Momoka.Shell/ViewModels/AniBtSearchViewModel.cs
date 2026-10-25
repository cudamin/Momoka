using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.AniBt;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Momoka.Shell.ViewModels;

public sealed partial class AniBtAnimeRow : ObservableObject
{
    private readonly Func<AniBtAnimeRow, Task> _open;
    private BitmapImage? _artwork;

    public AniBtAnimeRow(AniBtAnime anime, Func<AniBtAnimeRow, Task> open)
    {
        Anime = anime;
        _open = open;
    }

    public AniBtAnime Anime { get; }
    public string Title => Anime.Title;
    public string OriginalTitle => Anime.OriginalTitle;
    public Visibility OriginalVisibility => !string.IsNullOrWhiteSpace(OriginalTitle)
        && !string.Equals(Title, OriginalTitle, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public string Description => string.Join(" · ", new[]
    {
        FormatLabel(Anime.Format),
        Anime.EpisodeCount is > 0 ? $"{Anime.EpisodeCount} 集" : null,
        Anime.Score is { } score ? "★ " + score.ToString("0.0", CultureInfo.InvariantCulture) : null
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string CompactDescription => string.Join(" · ", new[] { Anime.Format,
        Anime.EpisodeCount is > 0 ? $"{Anime.EpisodeCount}集" : null,
        Anime.Score is { } score ? "★" + score.ToString("0.0", CultureInfo.InvariantCulture) : null }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public Visibility DescriptionVisibility => Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>封面还没到（或没有图）时那块方块里写的字；封面载入失败时也看得见这一层。</summary>
    public string Initial => Anime.Title.Trim() is { Length: > 0 } title ? title[..1] : "?";

    /// <summary>
    /// AniBT 公开搜索只给一个 https 封面地址。按卡片宽度解码，缺图交给下面的首字母占位；
    /// 载入失败不改变这一行可用性，也不弹提示。
    /// </summary>
    public BitmapImage? Artwork => _artwork ??= AniBtArtwork.Create(Anime.ImageUrl, 240);

    public static string FormatLabel(string format) => format switch
    {
        "TV" => "TV动画",
        "TV_SHORT" => "短篇动画",
        "MOVIE" => "剧场版",
        "SPECIAL" => "特别篇",
        _ => format
    };

    public static string StatusLabel(string status) => status switch
    {
        "RELEASING" => "连载中",
        "FINISHED" => "已完结",
        "NOT_YET_RELEASED" => "未开播",
        "CANCELLED" => "已取消",
        "HIATUS" => "暂停",
        _ => status
    };

    public string OpenAutomationName => $"查看 AniBT 资源：{Title}";

    [RelayCommand]
    private Task Open() => _open(this);
}

/// <summary>AniBT 公开接口给的封面直接交给 BitmapImage；只收 https、不带凭据的地址。</summary>
internal static class AniBtArtwork
{
    internal static BitmapImage? Create(string? address, int logicalWidth)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return null;
        return new BitmapImage
        {
            UriSource = uri,
            DecodePixelType = DecodePixelType.Logical,
            DecodePixelWidth = Math.Max(1, logicalWidth)
        };
    }
}

/// <summary>AniBT 番剧搜索与资源列表；下载沿用 MoviePilot 的连接身份、选项与防重状态。</summary>
public sealed partial class AniBtSearchViewModel : PageViewModel
{
    private const string IdlePrompt = "输入番剧名称，按回车或点击搜索图标，在 AniBT 查找动画资源";
    private AniBtService? _service;
    private MoviePilotService? _moviePilot;
    private ISystemLauncher? _launcher;
    private Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>>? _chooseDownload;
    private CancellationToken _query;
    private string _connection = "";
    private string _keyword = "";
    private bool _active;
    private int? _episode;
    private string? _cursor;
    private bool _filtering;

    public ObservableCollection<AniBtAnimeRow> Results { get; } = [];
    public ObservableCollection<AniBtAnimeRow> VisibleResults { get; } = [];
    public ObservableCollection<string> Formats { get; } = ["全部格式"];
    public ObservableCollection<string> Statuses { get; } = ["全部状态"];
    public ObservableCollection<string> Years { get; } = ["全部年份"];
    public AniBtBrowserViewModel Browser { get; } = new();

    [ObservableProperty] public partial string SelectedFormat { get; set; } = "全部格式";
    [ObservableProperty] public partial string SelectedStatus { get; set; } = "全部状态";
    [ObservableProperty] public partial string SelectedYear { get; set; } = "全部年份";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreVisibility))]
    public partial bool HasMore { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryLines))]
    [NotifyPropertyChangedFor(nameof(SummaryToggleText))]
    public partial bool SummaryExpanded { get; set; }

    public int SummaryLines => SummaryExpanded ? 0 : 3;
    public string SummaryToggleText => SummaryExpanded ? "收起简介" : "展开简介";
    public string SelectedSummary => SelectedAnime?.Description ?? "";
    public Visibility SummaryVisibility => Show(SelectedSummary.Length > 0);
    public string SelectedTags => string.Join(" · ", SelectedAnime?.Genres ?? []);
    public Visibility TagsVisibility => Show(SelectedTags.Length > 0);
    public Visibility OfficialSiteVisibility => Show(!string.IsNullOrWhiteSpace(SelectedAnime?.OfficialSite));
    public Visibility AniDbVisibility => Show(SelectedAnime?.ExternalLinks.Any(link => link.Label == "AniDB") == true);
    public Visibility AniListVisibility => Show(SelectedAnime?.ExternalLinks.Any(link => link.Label == "AniList") == true);
    public Visibility MyAnimeListVisibility => Show(SelectedAnime?.ExternalLinks.Any(link => link.Label == "MyAnimeList") == true);
    public Visibility MoreVisibility => Show(HasMore);
    public Visibility SearchFiltersVisibility => Show(Results.Count > 0);
    public Visibility FormatFilterVisibility => Show(Formats.Count > 1);
    public Visibility StatusFilterVisibility => Show(Statuses.Count > 1);
    public Visibility YearFilterVisibility => Show(Years.Count > 1);
    public Visibility NoFilteredResultsVisibility => Show(Results.Count > 0 && VisibleResults.Count == 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListVisibility))]
    [NotifyPropertyChangedFor(nameof(ResourcesVisibility))]
    [NotifyPropertyChangedFor(nameof(CanChooseAnime))]
    [NotifyCanExecuteChangedFor(nameof(ReturnToResultsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadResourcesCommand))]
    public partial AniBtAnime? SelectedAnime { get; set; }

    [ObservableProperty]
    public partial BitmapImage? SelectedArtwork { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; } = true;

    [ObservableProperty] public partial string EmptyTitle { get; set; } = "搜索 AniBT 番剧";

    [ObservableProperty]
    public partial string EmptyNotice { get; set; } = IdlePrompt;

    public string SelectedTitle => SelectedAnime?.Title ?? "";
    public string SelectedOriginalTitle => string.Join(" · ", new[] { SelectedAnime?.OriginalTitle, SelectedAnime?.EnglishTitle }
        .Where(value => !string.IsNullOrWhiteSpace(value) && value != SelectedTitle).Distinct());
    public Visibility SelectedOriginalVisibility => Show(SelectedOriginalTitle.Length > 0);
    public string SelectedMeta => string.Join(" · ", new[]
    {
        SelectedAnime?.Year?.ToString(), AniBtAnimeRow.FormatLabel(SelectedAnime?.Format ?? ""),
        SelectedAnime is { EpisodeCount: > 0 } selected ? $"{selected.EpisodeCount} 集" : null,
        SelectedAnime?.Score is { } score ? "★ " + score.ToString("0.0", CultureInfo.InvariantCulture) : null
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public Visibility SelectedMetaVisibility => SelectedMeta.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedBgm => SelectedAnime is { BgmId: > 0 } anime ? $"Bangumi #{anime.BgmId}" : "";
    public Visibility SelectedBgmVisibility => SelectedBgm.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedInitial => SelectedAnime?.Title.Trim() is { Length: > 0 } title ? title[..1] : "?";

    public string ResourceScope => _episode is { } episode
        ? $"第 {episode} 集 · 请先选择对应季度的番剧；按 AniBT 集号精确查询，合集可能不在结果内"
        : "各字幕组最近发布 · 下载交给 MoviePilot";
    public Visibility EpisodeScopeVisibility => Show(_episode is not null);
    public Visibility ListVisibility => Show(SelectedAnime is null);
    public Visibility ResourcesVisibility => Show(SelectedAnime is not null);
    public Visibility EmptyVisibility => Show(ShowEmptyNotice);
    public string ResultCountText => Results.Count == 0 ? "" : VisibleResults.Count == Results.Count
        ? $"{Results.Count} 部番剧" : $"{VisibleResults.Count} / {Results.Count} 部番剧";
    public Visibility ResultCountVisibility => Show(Results.Count > 0);
    public bool CanChooseAnime => _active && !Busy && SelectedAnime is null && ConnectionMatches;
    private bool CanReturnToResults => _active && SelectedAnime is not null;
    private bool CanReloadResources => CanReturnToResults && !Busy && ConnectionMatches;

    // An empty stamp is allowed before the first search. Every populated result is tied to its captured connection.
    internal bool ConnectionMatches => _moviePilot?.Available == true
        && (_connection.Length == 0 || _moviePilot.IsCurrentConnection(_connection));

    internal void Attach(AniBtService service, MoviePilotService moviePilot, ISystemLauncher launcher)
    {
        Cancel();
        _service = service;
        _moviePilot = moviePilot;
        _launcher = launcher;
    }

    internal void UseDownloadPicker(Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>> choose) =>
        _chooseDownload = choose;

    internal int? Episode => _episode;

    internal void RssCopied(Exception? error = null)
    {
        if (error is null) Notify(null, "已复制番剧 RSS 链接", InfoBarSeverity.Success);
        else Report("复制 RSS 链接失败", error);
    }

    internal void PrepareSearch(bool preserveEpisode = false)
    {
        var episode = preserveEpisode ? _episode : null;
        Cancel();
        _episode = episode;
        AnnounceScope();
        _active = true;
        IsReady = true;
        AnnounceCommands();
    }

    internal Task SearchAsync(string keyword, int? episode = null)
    {
        PrepareSearch();
        _episode = episode;
        AnnounceScope();
        _keyword = keyword.Trim();
        if (_keyword.Length == 0) return Task.CompletedTask;
        if (_service is null || _moviePilot?.Available != true)
        {
            EmptyTitle = "MoviePilot 未连接";
            EmptyNotice = "MoviePilot 尚未连接，请在设置中启用并连接后再试";
            ShowEmptyNotice = true;
            return Task.CompletedTask;
        }
        return LoadSearchAsync();
    }

    public override Task ReloadAsync() => SelectedAnime is { } anime ? LoadResourcesAsync(anime) : SearchAsync(_keyword, _episode);

    [RelayCommand]
    private Task ShowAllEpisodes()
    {
        _episode = null;
        AnnounceScope();
        return SelectedAnime is { } anime ? LoadResourcesAsync(anime) : Task.CompletedTask;
    }

    private void AnnounceScope()
    {
        OnPropertyChanged(nameof(ResourceScope));
        OnPropertyChanged(nameof(EpisodeScopeVisibility));
    }

    partial void OnSelectedAnimeChanged(AniBtAnime? value)
    {
        SelectedArtwork = AniBtArtwork.Create(value?.ImageUrl, 240);
        SummaryExpanded = false;
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedOriginalTitle));
        OnPropertyChanged(nameof(SelectedOriginalVisibility));
        OnPropertyChanged(nameof(SelectedMeta));
        OnPropertyChanged(nameof(SelectedMetaVisibility));
        OnPropertyChanged(nameof(SelectedBgm));
        OnPropertyChanged(nameof(SelectedBgmVisibility));
        OnPropertyChanged(nameof(SelectedInitial));
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(SummaryVisibility));
        OnPropertyChanged(nameof(SelectedTags));
        OnPropertyChanged(nameof(TagsVisibility));
        OnPropertyChanged(nameof(OfficialSiteVisibility));
        OnPropertyChanged(nameof(AniDbVisibility));
        OnPropertyChanged(nameof(AniListVisibility));
        OnPropertyChanged(nameof(MyAnimeListVisibility));
    }

    [RelayCommand] private void ToggleSummary() => SummaryExpanded = !SummaryExpanded;

    [RelayCommand]
    private void OpenAnimeSite(string site)
    {
        if (!_active || SelectedAnime is not { } anime || _launcher is null || !ConnectionMatches) return;
        var address = site switch
        {
            "bangumi" => $"https://bgm.tv/subject/{anime.BgmId}",
            "official" => anime.OfficialSite,
            "AniDB" or "AniList" or "MyAnimeList" => anime.ExternalLinks.FirstOrDefault(link => link.Label == site)?.Uri.AbsoluteUri,
            _ => $"https://anibt.net/anime/{anime.BgmId}"
        };
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0)
            try { _launcher.OpenUrl(uri.AbsoluteUri); }
            catch (Exception error) { Report("打开番剧页面失败", error); }
    }

    private async Task LoadSearchAsync(bool append = false)
    {
        if (!_active || _service is null || _moviePilot?.Available != true) return;
        if (append && !ConnectionMatches) return;
        var token = BeginLoad();
        _query = token;
        ShowEmptyNotice = false;
        try
        {
            if (!append) _connection = _moviePilot.ConnectionStamp;
            if (!ConnectionMatches) return;
            var found = await _service.SearchCatalogAsync(_keyword, append ? _cursor : null, token).ConfigureAwait(true);
            if (!Current(token)) return;
            foreach (var anime in found.Items)
                if (!Results.Any(row => row.Anime.AnimeId == anime.AnimeId && row.Anime.BgmId == anime.BgmId))
                    Results.Add(new AniBtAnimeRow(anime, OpenResourcesAsync));
            _cursor = found.Cursor;
            HasMore = found.HasMore;
            RefreshSearchFilters();
            AnnounceResults();
            EmptyTitle = "没有找到番剧";
            EmptyNotice = $"AniBT 上没搜到「{_keyword}」，试试原名或其他译名";
            ShowEmptyNotice = Results.Count == 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(token)) Report("搜索 AniBT 失败", error);
        }
        finally { EndLoad(token); }
    }

    [RelayCommand]
    private Task LoadMore() => !Busy && HasMore && SelectedAnime is null ? LoadSearchAsync(true) : Task.CompletedTask;

    partial void OnSelectedFormatChanged(string value) => FilterResults();
    partial void OnSelectedStatusChanged(string value) => FilterResults();
    partial void OnSelectedYearChanged(string value) => FilterResults();

    private void RefreshSearchFilters()
    {
        _filtering = true;
        Fill(Formats, Results.Select(row => AniBtAnimeRow.FormatLabel(row.Anime.Format)));
        Fill(Statuses, Results.Select(row => AniBtAnimeRow.StatusLabel(row.Anime.Status)));
        Fill(Years, Results.Select(row => row.Anime.Year?.ToString(CultureInfo.InvariantCulture) ?? "").OrderDescending());
        _filtering = false;
        FilterResults();
    }

    private static void Fill(ObservableCollection<string> choices, IEnumerable<string> values)
    {
        foreach (var value in values.Where(value => value.Length > 0).Distinct())
            if (!choices.Contains(value)) choices.Add(value);
    }

    private void FilterResults()
    {
        if (_filtering) return;
        var desired = Results.Where(row =>
            (string.IsNullOrEmpty(SelectedFormat) || SelectedFormat == "全部格式" || AniBtAnimeRow.FormatLabel(row.Anime.Format) == SelectedFormat)
            && (string.IsNullOrEmpty(SelectedStatus) || SelectedStatus == "全部状态" || AniBtAnimeRow.StatusLabel(row.Anime.Status) == SelectedStatus)
            && (string.IsNullOrEmpty(SelectedYear) || SelectedYear == "全部年份" || row.Anime.Year?.ToString(CultureInfo.InvariantCulture) == SelectedYear)).ToArray();
        var keep = desired.ToHashSet();
        for (var index = VisibleResults.Count - 1; index >= 0; index--)
            if (!keep.Contains(VisibleResults[index])) VisibleResults.RemoveAt(index);
        for (var index = 0; index < desired.Length; index++)
        {
            if (index < VisibleResults.Count && ReferenceEquals(VisibleResults[index], desired[index])) continue;
            var existing = VisibleResults.IndexOf(desired[index]);
            if (existing >= 0) VisibleResults.Move(existing, index);
            else VisibleResults.Insert(index, desired[index]);
        }
        AnnounceResults();
    }

    private Task OpenResourcesAsync(AniBtAnimeRow row) => CanChooseAnime && Results.Contains(row)
        ? LoadResourcesAsync(row.Anime) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanReloadResources))]
    private Task ReloadResources() => SelectedAnime is { } anime ? LoadResourcesAsync(anime) : Task.CompletedTask;

    private async Task LoadResourcesAsync(AniBtAnime anime)
    {
        if (!_active || _service is null || _moviePilot is null || !ConnectionMatches) return;
        var token = BeginLoad();
        _query = token;
        SelectedAnime = anime;
        Browser.Clear();
        ShowEmptyNotice = false;
        try
        {
            IReadOnlyList<AniBtResource> found;
            AniBtBrowsePage? page = null;
            if (_episode is not null) found = await _service.ResourcesAsync(anime, token, _episode).ConfigureAwait(true);
            else
            {
                page = await _service.BrowseAsync(anime, token).ConfigureAwait(true);
                found = page.Resources;
            }
            if (!Current(token) || !ReferenceEquals(SelectedAnime, anime)) return;
            if (page is not null) SelectedAnime = page.Anime;
            Browser.SetResults(found.Select(item => new AniBtResourceRow(item, DownloadAsync, OpenDetails)
            {
                State = MoviePilotResourceRow.StateOf(_moviePilot.DownloadState(item.Resource))
            }), _episode is { } number
                ? $"没有找到第 {number} 集，可查看全部集数以查找合集，或返回选择对应季度"
                : "这个番剧最近的发布中没有可下载资源，稍后再试或返回选择其他结果", page?.Groups, SelectedAnime);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(token)) Report("读取 AniBT 资源失败", error);
        }
        finally { EndLoad(token); }
    }

    [RelayCommand(CanExecute = nameof(CanReturnToResults))]
    private void ReturnToResults()
    {
        if (!CanReturnToResults) return;
        // Changing panels invalidates resource reads and any open download confirmation without re-querying the anime list.
        var token = BeginLoad();
        _query = token;
        SelectedAnime = null;
        Browser.Clear();
        EmptyTitle = "搜索 AniBT 番剧";
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = Results.Count == 0;
        AnnounceResults();
        EndLoad(token);
    }

    private bool Current(CancellationToken token) => _active && IsCurrent(token) && ConnectionMatches;

    private void AnnounceResults()
    {
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(ResultCountVisibility));
        OnPropertyChanged(nameof(SearchFiltersVisibility));
        OnPropertyChanged(nameof(FormatFilterVisibility));
        OnPropertyChanged(nameof(StatusFilterVisibility));
        OnPropertyChanged(nameof(YearFilterVisibility));
        OnPropertyChanged(nameof(NoFilteredResultsVisibility));
    }

    private void OpenDetails(AniBtResourceRow row)
    {
        if (!Current(_query) || !Browser.Rows.Contains(row) || _launcher is null || row.Resource.DetailsUri is not { } uri) return;
        try { _launcher.OpenUrl(uri.AbsoluteUri); }
        catch (Exception error) { Report("打开资源页面失败", error); }
    }

    private async Task DownloadAsync(AniBtResourceRow row)
    {
        if (_moviePilot is null || _chooseDownload is null || !row.CanDownload || !Current(_query)
            || SelectedAnime is null || !Browser.Rows.Contains(row)) return;
        var token = _query;
        var options = await _chooseDownload(row.Resource, token).ConfigureAwait(true);
        if (options is null || !Current(token)) return;
        row.State = MoviePilotDownloadState.Working;
        try
        {
            await _moviePilot.DownloadAsync(row.Resource, token, options).ConfigureAwait(true);
            if (!Current(token)) return;
            row.State = MoviePilotDownloadState.Done;
            Notify(null, $"已加入 MoviePilot 下载：{row.Title}", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            if (!Current(token)) return;
            row.State = error is MoviePilotOperationUncertainException or MoviePilotOperationBlockedException
                ? MoviePilotDownloadState.Uncertain : MoviePilotDownloadState.Failed;
            Report($"加入下载未完成：{row.Title}", error);
        }
    }

    protected override void BusyChanged() => AnnounceCommands();

    private void AnnounceCommands()
    {
        OnPropertyChanged(nameof(CanChooseAnime));
        ReturnToResultsCommand.NotifyCanExecuteChanged();
        ReloadResourcesCommand.NotifyCanExecuteChanged();
    }

    public override void Cancel()
    {
        _active = false;
        base.Cancel();
        _connection = "";
        _keyword = "";
        _episode = null;
        AnnounceScope();
        SelectedAnime = null;
        Results.Clear();
        VisibleResults.Clear();
        _filtering = true;
        Formats.Clear(); Formats.Add("全部格式");
        Statuses.Clear(); Statuses.Add("全部状态");
        Years.Clear(); Years.Add("全部年份");
        SelectedFormat = "全部格式";
        SelectedStatus = "全部状态";
        SelectedYear = "全部年份";
        _filtering = false;
        _cursor = null;
        HasMore = false;
        Browser.Clear();
        ClearNotice();
        EmptyTitle = "搜索 AniBT 番剧";
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = true;
        AnnounceResults();
        AnnounceCommands();
    }
}

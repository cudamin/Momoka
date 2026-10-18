using System.Collections.ObjectModel;
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
        Anime.Year?.ToString(), Anime.Format,
        Anime.EpisodeCount is > 0 ? $"{Anime.EpisodeCount} 集" : null
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public Visibility DescriptionVisibility => Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>封面还没到（或没有图）时那块方块里写的字；封面载入失败时也看得见这一层。</summary>
    public string Initial => Anime.Title.Trim() is { Length: > 0 } title ? title[..1] : "?";

    /// <summary>
    /// AniBT 公开搜索只给一个 https 封面地址。按卡片宽度解码，缺图交给下面的首字母占位；
    /// 载入失败不改变这一行可用性，也不弹提示。
    /// </summary>
    public BitmapImage? Artwork => _artwork ??= AniBtArtwork.Create(Anime.ImageUrl, 96);

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

    public ObservableCollection<AniBtAnimeRow> Results { get; } = [];
    public AniBtBrowserViewModel Browser { get; } = new();

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
    public string SelectedOriginalTitle => SelectedAnime?.OriginalTitle ?? "";
    public Visibility SelectedOriginalVisibility => SelectedAnime is { } anime
        && !string.IsNullOrWhiteSpace(anime.OriginalTitle)
        && !string.Equals(anime.Title, anime.OriginalTitle, StringComparison.Ordinal)
        ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedMeta => string.Join(" · ", new[]
    {
        SelectedAnime?.Year?.ToString(), SelectedAnime?.Format,
        SelectedAnime is { EpisodeCount: > 0 } selected ? $"{selected.EpisodeCount} 集" : null
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public Visibility SelectedMetaVisibility => SelectedMeta.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedBgm => SelectedAnime is { BgmId: > 0 } anime ? $"Bangumi #{anime.BgmId}" : "";
    public Visibility SelectedBgmVisibility => SelectedBgm.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedInitial => SelectedAnime?.Title.Trim() is { Length: > 0 } title ? title[..1] : "?";

    public string ResourceScope => _episode is { } episode
        ? $"第 {episode} 集 · 请先选择对应季度的番剧；按 AniBT 集号精确查询，合集可能不在结果内"
        : "最近最多 100 条公开发布 · 下载交给 MoviePilot";
    public Visibility EpisodeScopeVisibility => Show(_episode is not null);
    public Visibility ListVisibility => Show(SelectedAnime is null);
    public Visibility ResourcesVisibility => Show(SelectedAnime is not null);
    public Visibility EmptyVisibility => Show(ShowEmptyNotice);
    public string ResultCountText => Results.Count == 0 ? "" : $"{Results.Count} 部番剧";
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
        SelectedArtwork = AniBtArtwork.Create(value?.ImageUrl, 88);
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedOriginalTitle));
        OnPropertyChanged(nameof(SelectedOriginalVisibility));
        OnPropertyChanged(nameof(SelectedMeta));
        OnPropertyChanged(nameof(SelectedMetaVisibility));
        OnPropertyChanged(nameof(SelectedBgm));
        OnPropertyChanged(nameof(SelectedBgmVisibility));
        OnPropertyChanged(nameof(SelectedInitial));
    }

    private async Task LoadSearchAsync()
    {
        if (!_active || _service is null || _moviePilot?.Available != true) return;
        var token = BeginLoad();
        _query = token;
        ShowEmptyNotice = false;
        try
        {
            _connection = _moviePilot.ConnectionStamp;
            if (!ConnectionMatches) return;
            var found = await _service.SearchAsync(_keyword, 1, token).ConfigureAwait(true);
            if (!Current(token)) return;
            foreach (var anime in found.Items)
                if (!Results.Any(row => row.Anime.AnimeId == anime.AnimeId && row.Anime.BgmId == anime.BgmId))
                    Results.Add(new AniBtAnimeRow(anime, OpenResourcesAsync));
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
            var found = await _service.ResourcesAsync(anime, token, _episode).ConfigureAwait(true);
            if (!Current(token) || !ReferenceEquals(SelectedAnime, anime)) return;
            Browser.SetResults(found.Select(item => new AniBtResourceRow(item, DownloadAsync, OpenDetails)
            {
                State = MoviePilotResourceRow.StateOf(_moviePilot.DownloadState(item.Resource))
            }), _episode is { } number
                ? $"没有找到第 {number} 集，可查看全部集数以查找合集，或返回选择对应季度"
                : "这个番剧最近的发布中没有可下载资源，稍后再试或返回选择其他结果");
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
        Browser.Clear();
        ClearNotice();
        EmptyTitle = "搜索 AniBT 番剧";
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = true;
        AnnounceResults();
        AnnounceCommands();
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

public sealed partial class MoviePilotSubtitleRow(Momoka.MoviePilot.MoviePilotSubtitle subtitle, Func<MoviePilotSubtitleRow, Task> download, MoviePilotOperationState state) : ObservableObject
{
    public Momoka.MoviePilot.MoviePilotSubtitle Subtitle { get; } = subtitle;
    public string Title => Subtitle.Title;
    public string Meta => Subtitle.Meta;
    public string Description => Subtitle.Description;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(DownloadLabel))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    public partial MoviePilotOperationState State { get; set; } = state;
    public bool CanDownload => State == MoviePilotOperationState.Idle;
    public string DownloadLabel => State switch { MoviePilotOperationState.Working => "下载中…", MoviePilotOperationState.Submitted => "已提交", MoviePilotOperationState.Uncertain => "请先核对", _ => "下载字幕" };
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => download(this);
}

/// <summary>一条资源的下载走到哪了。四档，那颗键要说四种话，能不能按也跟着它走。</summary>
public enum MoviePilotDownloadState
{
    Idle,
    Working,
    Done,
    Failed,
    Uncertain
}

/// <summary>
/// 资源列表里的一行（一个种子）：包一条 <see cref="MoviePilotResource"/>，加上下载键要的可见性和命令。
/// 下载逻辑不在这儿（要服务和确认），回调给视图模型 —— 同 <see cref="MoviePilotResult"/> 的路子。
/// </summary>
public sealed partial class MoviePilotResourceRow : ObservableObject
{
    private readonly Func<MoviePilotResourceRow, Task> _download;
    private readonly Action<MoviePilotResourceRow>? _openDetails;

    public MoviePilotResourceRow(MoviePilotResource resource, Func<MoviePilotResourceRow, Task> download,
        Action<MoviePilotResourceRow>? openDetails = null)
    {
        Resource = resource;
        _download = download;
        _openDetails = openDetails;
    }

    public MoviePilotResource Resource { get; }

    public string Title => Resource.Title;

    public string DisplayTitle => Resource.DisplayTitle;
    public string SeasonEpisode => Resource.SeasonEpisode ?? "";
    public string Seeders => $"↑ {Resource.Seeders}";
    public string Peers => $"↓ {Resource.Peers}";
    public string SizeText => Resource.SizeText;
    public string PromotionBadge => Resource.PromotionBadge;
    public string Resolution => Resource.Resolution ?? "";
    public string VideoCodec => Resource.VideoCodec ?? "";
    public string Edition => Resource.Edition ?? "";
    public string ReleaseGroup => Resource.ReleaseGroup ?? "";
    public string ResourceType => Resource.ResourceType ?? "";
    public string WebSource => Resource.WebSource ?? "";
    public string ResourceEffect => Resource.ResourceEffect ?? "";
    public Visibility PromotionVisibility => Show(PromotionBadge);
    public Visibility SeasonEpisodeVisibility => Show(SeasonEpisode);
    public Visibility ResolutionVisibility => Show(Resolution);
    public Visibility VideoVisibility => Show(VideoCodec);
    public Visibility EditionVisibility => Show(Edition);
    public Visibility GroupVisibility => Show(ReleaseGroup);
    public Visibility ResourceTypeVisibility => Show(ResourceType);
    public Visibility WebSourceVisibility => Show(WebSource);
    public Visibility EffectVisibility => Show(ResourceEffect);
    public Visibility HitAndRunVisibility => Resource.HitAndRun ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsVisibility => CanOpenDetails ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DownloadVisibility => Resource.DownloadProblem is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DownloadProblemVisibility => Resource.DownloadProblem is null ? Visibility.Collapsed : Visibility.Visible;
    public string DownloadProblem => Resource.DownloadProblem ?? "";
    private static Visibility Show(string value) => value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string SiteName => Resource.SiteName ?? "";

    public Visibility SiteVisibility => string.IsNullOrWhiteSpace(Resource.SiteName) ? Visibility.Collapsed : Visibility.Visible;

    public string MetaLine => Resource.MetaLine;

    public Visibility MetaVisibility => Resource.MetaLine.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

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

    internal static MoviePilotDownloadState StateOf(MoviePilotOperationState state) => state switch
    {
        MoviePilotOperationState.Working => MoviePilotDownloadState.Working,
        MoviePilotOperationState.Submitted => MoviePilotDownloadState.Done,
        MoviePilotOperationState.Uncertain => MoviePilotDownloadState.Uncertain,
        _ => MoviePilotDownloadState.Idle
    };

    public string DownloadAutomationName => $"下载：{Title}";
    public string DetailsAutomationName => $"打开种子页面：{Title}";
    public bool CanOpenDetails => Resource.DetailsUri is not null && _openDetails is not null;
    public string DetailsHint => CanOpenDetails ? "在浏览器打开站点详情，不会开始下载" : "站点未提供可打开的种子页面";
    public string Description => Resource.Description ?? "";
    public Visibility DescriptionVisibility => Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string LabelsText => Resource.LabelsText;
    public Visibility LabelsVisibility => LabelsText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string PromotionText => Resource.PromotionText;
    public string PromotionHint => string.IsNullOrWhiteSpace(Resource.FreeUntil)
        ? "优惠以站点当前规则为准" : $"优惠截止：{Resource.FreeUntil}；以站点为准";
    public string PublishedLine => Resource.PublishedLine;

    [RelayCommand(CanExecute = nameof(CanOpenDetails))]
    private void OpenDetails() => _openDetails?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task Download() => _download(this);
}

/// <summary>
/// 资源列表（一部片的可下载种子）的视图模型：进来就搜、铺成一行行，每行的「下载」接到二次确认加下单上。
/// 复用 <see cref="PageViewModel"/> 的忙/通知条/载入代次。
/// </summary>
public sealed partial class MoviePilotResourceViewModel : PageViewModel
{
    private const string Category = "moviepilot";

    private MoviePilotService? _service;
    private ISystemLauncher? _launcher;
    private MoviePilotMedia? _media;
    private MoviePilotMediaSearchOptions? _options;
    private CancellationToken _query;
    private Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>>? _chooseDownload;

    internal void UseDownloadPicker(Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>> choose) => _chooseDownload = choose;

    public MoviePilotResourceViewModel()
    {
        EmptyNotice = "";
        SearchSites.SelectionChanged += OnSearchSitesChanged;
    }

    public MoviePilotSitePickerViewModel SearchSites { get; } = new();

    public MoviePilotResourceBrowserViewModel Browser { get; } = new();
    public ObservableCollection<MoviePilotResourceRow> Resources => Browser.Rows;

    /// <summary>对话框标题那一行——搜的是哪部片。</summary>
    public string Heading => (_media?.Display ?? "资源") + (_options?.Subtitles == true ? " · 字幕" : "");
    public ObservableCollection<MoviePilotSubtitleRow> Subtitles { get; } = [];
    public Visibility SubtitlesVisibility => Show(_options?.Subtitles == true);
    public Visibility TorrentsVisibility => Show(_options?.Subtitles != true);
    internal Func<MoviePilotSubtitle, IReadOnlyList<string>, CancellationToken, Task<string?>>? ChooseSubtitlePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; }

    public Visibility EmptyVisibility => Show(ShowEmptyNotice);

    [ObservableProperty]
    public partial string EmptyNotice { get; set; }

    internal void Attach(MoviePilotService service, MoviePilotMedia media, ISystemLauncher launcher, MoviePilotMediaSearchOptions? options = null)
    {
        _service = service;
        _launcher = launcher;
        _media = media;
        _options = options;
        Subtitles.Clear();
        OnPropertyChanged(nameof(SubtitlesVisibility)); OnPropertyChanged(nameof(TorrentsVisibility));
        SearchSites.Attach(service, media.Type);
        OnPropertyChanged(nameof(Heading));
    }

    public override Task ReloadAsync() => SearchAsync();

    internal void PrepareSearch()
    {
        base.Cancel();
        IsReady = true;
        Browser.SetResults([]);
        Subtitles.Clear();
        EmptyNotice = "选择搜索站点后，点击“搜索资源”开始";
        ShowEmptyNotice = true;
    }

    /// <summary>去搜这部片的资源。各站点现捞，可能要等几十秒——忙态那根线会一直转着。</summary>
    internal async Task SearchAsync()
    {
        if (_service is null || _media is null) return;

        var token = BeginLoad();
        _query = token;
        Browser.SetResults([]);
        Subtitles.Clear();
        ShowEmptyNotice = false;

        if (!SearchSites.HasSelection)
        {
            EmptyNotice = "请先选择搜索站点，或使用 MoviePilot 默认站点";
            ShowEmptyNotice = true;
            EndLoad(token);
            return;
        }

        try
        {
            if (_options?.Subtitles == true)
            {
                Subtitles.Clear();
                var subtitles = await _service.SearchSubtitlesAsync(_media, SearchSites.Selection, _options.Episode, token);
                if (!IsCurrent(token)) return;
                foreach (var subtitle in subtitles) Subtitles.Add(new(subtitle, DownloadSubtitleAsync, _service.SubtitleState(subtitle)));
                EmptyNotice = "没有找到字幕"; ShowEmptyNotice = subtitles.Count == 0; EndLoad(token); return;
            }
            var found = await _service.SearchResourcesAsync(_media, token, SearchSites.Selection, _options).ConfigureAwait(true);
            if (!IsCurrent(token)) return;

            Browser.SetResults(found.Select(resource => new MoviePilotResourceRow(resource, DownloadAsync, OpenDetails)
            {
                State = MoviePilotResourceRow.StateOf(_service.DownloadState(resource))
            }));
            if (_options?.Episode is > 0 and var episode)
            {
                var selection = "E" + episode.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                if (Browser.Episodes.Contains(selection)) Browser.SelectedEpisode = selection;
            }

            EmptyNotice = "没找到可下载的资源，换个别的片或稍后再试";
            ShowEmptyNotice = found.Count == 0;
            EndLoad(token);
        }
        catch (OperationCanceledException)
        {
            EndLoad(token);
        }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;

            Log.Warn(Category, $"搜《{_media.Title}》资源失败", error);
            Report("搜索资源失败", error);
            Browser.SetResults([]);
            ShowEmptyNotice = false;
            EndLoad(token);
        }
    }

    private async Task DownloadSubtitleAsync(MoviePilotSubtitleRow row)
    {
        if (_service is null || !IsCurrent(_query) || !Subtitles.Contains(row) || ChooseSubtitlePath is null || !row.CanDownload) return;
        var query = _query;
        row.State = MoviePilotOperationState.Working;
        try
        {
            var paths = await _service.SubtitlePathsAsync(row.Subtitle, query);
            if (!IsCurrent(query)) return;
            var path = await ChooseSubtitlePath(row.Subtitle, paths, query);
            if (path is null || !IsCurrent(query)) { row.State = MoviePilotOperationState.Idle; return; }
            var count = await _service.DownloadSubtitleAsync(row.Subtitle, path, query);
            if (!IsCurrent(query)) return;
            row.State = MoviePilotOperationState.Submitted;
            Notify("字幕下载", count > 0 ? $"MoviePilot 已保存 {count} 个字幕文件。" : "请求已提交，请在 MoviePilot 核对保存结果。", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            row.State = _service.SubtitleState(row.Subtitle);
            if (IsCurrent(query)) Report("字幕下载失败", error);
        }
    }

    private void OnSearchSitesChanged()
    {
        base.Cancel();
        IsReady = true;
        Browser.SetResults([]);
        EmptyNotice = SearchSites.HasSelection ? "搜索站点已更改，点击“搜索资源”应用" : "请至少选择一个站点，或使用 MoviePilot 默认站点";
        Subtitles.Clear();
        ShowEmptyNotice = true;
    }

    public override void Cancel()
    {
        SearchSites.Cancel();
        Subtitles.Clear();
        base.Cancel();
    }

    private void OpenDetails(MoviePilotResourceRow row)
    {
        if (row.Resource.DetailsUri is not { } uri || _launcher is null) return;
        try { _launcher.OpenUrl(uri.AbsoluteUri); }
        catch (Exception error) { Report("打开种子页面失败", error); }
    }

    /// <summary>
    /// 一行的「下载」按下去：先二次确认（对外副作用 —— 真的把种子加进 MoviePilot 的下载器、开始下载），确认了
    /// 才下单。成功置「已加入下载」，失败置「重试下载」，那句话已是给人看的。
    /// </summary>
    private async Task DownloadAsync(MoviePilotResourceRow row)
    {
        if (_service is null || !row.CanDownload || !IsCurrent(_query)) return;
        var token = _query;
        if (_chooseDownload is null) return;
        var options = await _chooseDownload(row.Resource, token).ConfigureAwait(true);
        if (options is null || !IsCurrent(token)) return;

        row.State = MoviePilotDownloadState.Working;
        try
        {
            await _service.DownloadAsync(row.Resource, token, options).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            row.State = MoviePilotDownloadState.Done;
            Notify(null, $"已加入下载：{row.Title}", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;
            row.State = error is MoviePilotOperationUncertainException or MoviePilotOperationBlockedException
                ? MoviePilotDownloadState.Uncertain : MoviePilotDownloadState.Failed;
            Log.Warn(Category, $"下载「{row.Title}」未完成", error);
            Report($"加入下载未完成：{row.Title}", error);
        }
    }
}

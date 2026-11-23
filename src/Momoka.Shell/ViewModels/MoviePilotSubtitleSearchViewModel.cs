using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 搜索页「MoviePilot 字幕」这一格的一张影片卡。字幕接口认的是 MoviePilot 的影片身份
/// （<c>media_source</c>＋<c>media_id</c>），所以先按关键词找片，再点一部片读它的字幕。
/// </summary>
public sealed partial class MoviePilotSubtitleMediaRow : ObservableObject
{
    internal MoviePilotSubtitleMediaRow(MoviePilotMedia media, Action<MoviePilotSubtitleMediaRow> open)
    {
        Media = media;
        OpenCommand = new RelayCommand(() => open(this));
    }

    public MoviePilotMedia Media { get; }
    public IRelayCommand OpenCommand { get; }

    public string Title => Media.Display;

    /// <summary>类型与来源那一行；两样都没有就整行收起。</summary>
    public string Meta => string.Join(" · ", new[] { Media.Type ?? "", Media.SourceLabel }
        .Where(value => value.Length > 0));
    public Visibility MetaVisibility => Meta.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// 搜索页「MoviePilot 字幕」来源的视图模型：关键词先找片（<c>media/search</c>），点一部片再按它的身份读字幕
/// （<c>search/subtitle/media</c>），字幕可下载到 MoviePilot 的默认或指定目录。
/// <para>
/// 从订阅详情「搜索字幕」进来时已经知道是哪部片，直接走 <see cref="LoadSubtitlesAsync"/>，不经过找片那一步。
/// 字幕接口没有按片名的关键词搜索，所以这一格不能像资源那样一个词直接出结果。
/// </para>
/// </summary>
public sealed partial class MoviePilotSubtitleSearchViewModel : PageViewModel
{
    private const string Category = "moviepilot";
    private const string IdlePrompt = "输入片名，按回车查找影片；点一部影片查看它的 MoviePilot 字幕";

    private MoviePilotService? _service;
    private string _lastKeyword = "";
    private MoviePilotMedia? _media;
    private int? _episode;
    private MoviePilotDetailSearch? _detailSearch;
    public MoviePilotSubtitleSearchViewModel() => SearchSites.SelectionChanged += OnSearchSitesChanged;
    public MoviePilotSearchProgressViewModel SearchProgress { get; } = new();

    /// <summary>这一格的活跃期：下载字幕的确认可以横跨一次搜索，收起时一起取消。</summary>
    private CancellationTokenSource _life = new();

    private CancellationToken BeginAction()
    {
        if (_life.IsCancellationRequested) { _life.Dispose(); _life = new(); }
        return _life.Token;
    }

    internal Func<MoviePilotSubtitle, IReadOnlyList<string>, CancellationToken, Task<string?>>? ChooseSubtitlePath { get; set; }

    /// <summary>这一格的搜索站点：字幕接口同样认 <c>sites</c>，与资源那一格共用同一份「不限类型」的选择。</summary>
    public MoviePilotSitePickerViewModel SearchSites { get; } = new();

    public ObservableCollection<MoviePilotSubtitleMediaRow> MediaResults { get; } = [];
    public ObservableCollection<MoviePilotSubtitleRow> Subtitles { get; } = [];

    /// <summary>找片结果那一列表：还没有选定影片时才显示。</summary>
    public Visibility MediaListVisibility => Show(_media is null && MediaResults.Count > 0);

    /// <summary>字幕结果那一列表：选定影片之后才显示（没有字幕时让位给空态）。</summary>
    public Visibility SubtitlesVisibility => Show(_media is not null);

    /// <summary>「重新选择影片」只在手里真有一份片单时才出现；从订阅详情带身份进来时没有片单可回。</summary>
    public Visibility BackVisibility => Show(_media is not null && MediaResults.Count > 0);

    internal bool HasSelectedMedia => _media is not null;
    public string Heading => _media is { } media ? media.Display + " · 字幕" : "MoviePilot 字幕";
    public string CountText => _media is not null
        ? Subtitles.Count == 0 ? "" : $"{Subtitles.Count} 个字幕"
        : MediaResults.Count == 0 ? "" : $"{MediaResults.Count} 部影片";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; } = true;

    [ObservableProperty]
    public partial string EmptyNotice { get; set; } = IdlePrompt;

    public Visibility EmptyVisibility => Show(ShowEmptyNotice);

    internal void Attach(MoviePilotService service)
    {
        _ = BeginAction();
        _service = service;
        SearchSites.Attach(service);
    }

    public override Task ReloadAsync() => _media is { } media
        ? LoadSubtitlesCoreAsync(media, _episode) : SearchAsync(_lastKeyword);

    /// <summary>按片名找片：只铺影片卡，不发字幕请求；点一张卡才读那部片的字幕。</summary>
    internal async Task SearchAsync(string keyword)
    {
        if (_service is not { } service) return;
        var text = keyword.Trim();
        if (_media is not null && _detailSearch is { } detail && string.Equals(text, detail.Media.Title, StringComparison.Ordinal))
        {
            await ReloadAsync().ConfigureAwait(true);
            return;
        }
        _detailSearch = null;
        _lastKeyword = text;
        _media = null;
        _episode = null;
        Subtitles.Clear();
        var token = BeginLoad();
        if (!IsCurrent(token)) return;
        MediaResults.Clear();
        ShowEmptyNotice = false;
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(MediaListVisibility));
        OnPropertyChanged(nameof(SubtitlesVisibility));
        OnPropertyChanged(nameof(BackVisibility));
        OnPropertyChanged(nameof(CountText));
        if (text.Length == 0)
        {
            EmptyNotice = IdlePrompt;
            ShowEmptyNotice = true;
            EndLoad(token);
            return;
        }
        try
        {
            var found = await service.SearchAsync(text, token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            MediaResults.Clear();
            foreach (var media in found.Where(media => media.CanSubscribe && !string.IsNullOrWhiteSpace(media.Type)))
            {
                if (!IsCurrent(token)) return;
                MediaResults.Add(new MoviePilotSubtitleMediaRow(media, OpenMedia));
            }
            EmptyNotice = $"MoviePilot 上没搜到「{text}」的可搜字幕影片，换个片名或稍后再试";
            ShowEmptyNotice = MediaResults.Count == 0;
            OnPropertyChanged(nameof(MediaListVisibility));
            OnPropertyChanged(nameof(BackVisibility));
            OnPropertyChanged(nameof(CountText));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;
            Log.Warn(Category, $"找片「{text}」失败", error);
            Report("查找影片失败", error);
            MediaResults.Clear();
            ShowEmptyNotice = false;
            OnPropertyChanged(nameof(MediaListVisibility));
            OnPropertyChanged(nameof(BackVisibility));
            OnPropertyChanged(nameof(CountText));
        }
        finally { EndLoad(token); }
    }

    /// <summary>读一部片的字幕。订阅详情「搜索字幕」走这一支：那里已经有影片身份，不必再找片。</summary>
    internal Task LoadSubtitlesAsync(MoviePilotMedia media, int? episode, MoviePilotDetailSearch? detail = null)
    {
        if (_service is null) return Task.CompletedTask;
        if (detail is not null) MediaResults.Clear();
        _detailSearch = detail;
        _media = media;
        _episode = episode;
        // 片单留着：「重新选择影片」要能回到刚才那一批，不在这一步清掉。
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(MediaListVisibility));
        OnPropertyChanged(nameof(SubtitlesVisibility));
        OnPropertyChanged(nameof(BackVisibility));
        OnPropertyChanged(nameof(CountText));
        return LoadSubtitlesCoreAsync(media, episode);
    }

    private async Task LoadSubtitlesCoreAsync(MoviePilotMedia media, int? episode)
    {
        if (_service is not { Available: true } service) return;
        var token = BeginLoad();
        if (!IsCurrent(token)) return;
        Subtitles.Clear();
        ShowEmptyNotice = false;
        if (!media.CanSubscribe || string.IsNullOrWhiteSpace(media.Type))
        {
            EmptyNotice = "这部剧缺少 MoviePilot 的影片身份，暂时搜不了字幕";
            ShowEmptyNotice = true;
            EndLoad(token);
            return;
        }
        try
        {
            await SearchSites.PrepareSelectionAsync().ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            if (!SearchSites.HasSelection)
            {
                EmptyNotice = "请先选择搜索站点，或使用 MoviePilot 默认站点";
                ShowEmptyNotice = true;
                return;
            }
            var progress = SearchProgress.Start(media.Title, media.Year, () => IsCurrent(token), subtitle: true);
            var found = await service.SearchSubtitlesAsync(media, SearchSites.Selection, episode, token, progress).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            foreach (var subtitle in found)
            {
                if (!IsCurrent(token)) return;
                Subtitles.Add(new(subtitle, DownloadSubtitleAsync, service.SubtitleState(subtitle)));
            }
            EmptyNotice = "没有找到字幕";
            ShowEmptyNotice = found.Count == 0;
            OnPropertyChanged(nameof(CountText));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;
            Log.Warn(Category, $"搜《{media.Title}》字幕失败", error);
            Report("搜索字幕失败", error);
            Subtitles.Clear();
            ShowEmptyNotice = false;
            OnPropertyChanged(nameof(CountText));
        }
        finally { EndLoad(token); }
    }

    protected override void BusyChanged()
    {
        if (!Busy) SearchProgress.Stop();
    }

    /// <summary>点一张影片卡：换成这部片的字幕列表。</summary>
    private void OpenMedia(MoviePilotSubtitleMediaRow row)
    {
        if (_service is null || !MediaResults.Contains(row)) return;
        _ = LoadSubtitlesAsync(row.Media, null);
    }

    [RelayCommand]
    private void BackToMediaList()
    {
        if (_media is null) return;
        _detailSearch = null;
        _media = null;
        _episode = null;
        Subtitles.Clear();
        base.Cancel();
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = MediaResults.Count == 0;
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(MediaListVisibility));
        OnPropertyChanged(nameof(SubtitlesVisibility));
        OnPropertyChanged(nameof(BackVisibility));
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>键入新词：收起上一批，不发请求（同搜索页其它非媒体库来源的规矩）。</summary>
    internal void Clear()
    {
        base.Cancel();
        _detailSearch = null;
        _lastKeyword = "";
        _media = null;
        _episode = null;
        MediaResults.Clear();
        Subtitles.Clear();
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = true;
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(MediaListVisibility));
        OnPropertyChanged(nameof(SubtitlesVisibility));
        OnPropertyChanged(nameof(BackVisibility));
        OnPropertyChanged(nameof(CountText));
    }

    private async Task DownloadSubtitleAsync(MoviePilotSubtitleRow row)
    {
        if (_service is null || !Subtitles.Contains(row) || ChooseSubtitlePath is null || !row.CanDownload) return;
        var query = BeginAction();
        row.State = MoviePilotOperationState.Working;
        try
        {
            var paths = await _service.SubtitlePathsAsync(row.Subtitle, query);
            if (query.IsCancellationRequested) return;
            var path = await ChooseSubtitlePath(row.Subtitle, paths, query);
            if (path is null || query.IsCancellationRequested) { row.State = MoviePilotOperationState.Idle; return; }
            var count = await _service.DownloadSubtitleAsync(row.Subtitle, path, query);
            if (query.IsCancellationRequested) return;
            row.State = MoviePilotOperationState.Submitted;
            Notify("字幕下载", count > 0 ? $"MoviePilot 已保存 {count} 个字幕文件。" : "请求已提交，请在 MoviePilot 核对保存结果。", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            row.State = _service.SubtitleState(row.Subtitle);
            if (!query.IsCancellationRequested) Report("字幕下载失败", error);
        }
    }

    public override void Cancel()
    {
        if (!_life.IsCancellationRequested) _life.Cancel();
        SearchSites.Cancel();
        base.Cancel();
    }

    /// <summary>换来源或离页：取消在飞的请求，并把这一格收回「还没搜」的样子。</summary>
    internal void Release()
    {
        _detailSearch = null;
        Cancel();
        _lastKeyword = "";
        _media = null;
        _episode = null;
        MediaResults.Clear();
        Subtitles.Clear();
        ClearNotice();
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = true;
        IsReady = true;
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(MediaListVisibility));
        OnPropertyChanged(nameof(SubtitlesVisibility));
        OnPropertyChanged(nameof(BackVisibility));
        OnPropertyChanged(nameof(CountText));
    }

    public override void Dispose()
    {
        SearchSites.SelectionChanged -= OnSearchSitesChanged;
        SearchProgress.Stop();
        _life.Dispose();
        base.Dispose();
    }

    private void OnSearchSitesChanged()
    {
        base.Cancel();
        IsReady = true;
        Notify("搜索站点已更改", "按回车或点击搜索图标，按新站点重新搜索字幕。", InfoBarSeverity.Informational);
    }
}

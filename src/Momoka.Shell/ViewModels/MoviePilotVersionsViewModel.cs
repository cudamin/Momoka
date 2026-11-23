using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;

namespace Momoka.Shell.ViewModels;

/// <summary>独立窗口的关键词资源搜索；过滤和结果行与精确搜索共用。</summary>
public sealed partial class MoviePilotVersionsViewModel : PageViewModel
{
    private const string Category = "moviepilot";
    private const string IdlePrompt = "输入关键词，按回车或点击搜索图标，在 MoviePilot 各站点查找资源";
    private MoviePilotService? _service;
    private ISystemLauncher? _launcher;
    private string _lastKeyword = "";
    private int? _lastAutoEpisode;
    private MoviePilotDetailSearch? _detailSearch;
    private IReadOnlyList<MoviePilotResource> _resources = [];
    private IReadOnlyList<MoviePilotSearchPage> _nextPages = [];
    private CancellationToken _query;

    /// <summary>本次运行内的上一次结果；只有搜索页那一份接它（见 <see cref="UseCache"/>）。</summary>
    private MoviePilotResourceSearchCache? _cache;

    /// <summary>
    /// 这一页的这次活跃期，不是某一次搜索：改站点会作废在飞的搜索，但留下的卡片仍可打开下载确认。
    /// 窗口收起时取消，下一次操作再开一段新的。
    /// </summary>
    private CancellationTokenSource _life = new();

    private CancellationToken BeginAction()
    {
        if (_life.IsCancellationRequested) { _life.Dispose(); _life = new(); }
        return _life.Token;
    }

    private Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>>? _chooseDownload;

    internal void UseDownloadPicker(Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>> choose) => _chooseDownload = choose;

    public MoviePilotVersionsViewModel() => SearchSites.SelectionChanged += OnSearchSitesChanged;
    public MoviePilotSitePickerViewModel SearchSites { get; } = new();

    public MoviePilotResourceBrowserViewModel Browser { get; } = new();
    public MoviePilotSearchProgressViewModel SearchProgress { get; } = new();
    public ObservableCollection<MoviePilotResourceRow> Versions => Browser.Rows;
    public Visibility MoreVisibility => Show(_nextPages.Count > 0);
    private bool CanLoadMore => !Busy && _nextPages.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; } = true;
    public Visibility EmptyVisibility => Show(ShowEmptyNotice);
    [ObservableProperty] public partial string EmptyNotice { get; set; } = IdlePrompt;

    internal void Attach(MoviePilotService service, ISystemLauncher launcher)
    {
        _ = BeginAction();
        _service = service;
        _launcher = launcher;
        SearchSites.Attach(service);
    }

    /// <summary>
    /// 搜索页那一份接上「本次运行内保留上一次结果」的缓存（用户令 2026-11-22）。独立窗口那一份不接：
    /// 它每次都是按关键词现开的一趟，不该拿另一处的结果顶上。
    /// </summary>
    internal void UseCache(MoviePilotResourceSearchCache cache) => _cache = cache;

    /// <summary>
    /// 把缓存里同一个词的上一批结果直接摆出来（不发请求）。连接换过或词不同都返回 false，由调用方去真搜。
    /// </summary>
    internal bool RestoreCached(string keyword)
    {
        if (_service is not { Available: true } service || _cache is null
            || _cache.Match(service.ConnectionStamp, keyword) is not { } snapshot) return false;
        base.Cancel();
        _lastKeyword = snapshot.Keyword;
        _lastAutoEpisode = snapshot.AutoEpisode;
        _detailSearch = snapshot.DetailSearch;
        var preference = service.SearchSitePreference();
        var sameSites = _detailSearch?.Sites is { } sites
            ? !preference.UseDefault && preference.SiteIds.ToHashSet().SetEquals(sites.Ids)
            : preference.UseDefault;
        SetNextPages(sameSites ? snapshot.NextPages ?? [] : []);
        ShowResources(service, snapshot.Resources, snapshot.AutoEpisode);
        EmptyNotice = snapshot.EmptyNotice;
        ShowEmptyNotice = snapshot.ShowEmptyNotice;
        IsReady = true;
        return true;
    }

    /// <summary>键入新词：收起上一批结果，不发请求（同搜索页其它非媒体库来源的规矩）。</summary>
    internal void Clear()
    {
        base.Cancel();
        _lastKeyword = "";
        _lastAutoEpisode = null;
        _detailSearch = null;
        Browser.SetResults([]);
        SetNextPages([]);
        _resources = [];
        EmptyNotice = IdlePrompt;
        ShowEmptyNotice = true;
    }

    public override Task ReloadAsync() => SearchCoreAsync(_lastKeyword, _lastAutoEpisode);

    internal Task SearchDetailAsync(MoviePilotDetailSearch search)
    {
        _detailSearch = search;
        // 网页详情资源搜索保留完整结果，单集参数只用于字幕搜索。
        return SearchCoreAsync(search.Media.Title, null);
    }

    /// <summary>
    /// 搜一次关键词。<paramref name="autoEpisode"/> 是集页面开窗时带进来的目标集（词里已经不发集号了，见
    /// <see cref="MoviePilotVersionQuery.Keyword"/>）：结果出来后浏览器自动按这一集筛，用户改词自己搜时不传，
    /// 自动筛选随之解除；刷新（<see cref="ReloadAsync"/>）带着上次的目标集重搜。
    /// </summary>
    internal Task SearchAsync(string keyword, int? autoEpisode = null)
    {
        if (_detailSearch is not null && string.Equals(keyword.Trim(), _lastKeyword, StringComparison.Ordinal))
            return ReloadAsync();
        _detailSearch = null;
        return SearchCoreAsync(keyword, autoEpisode);
    }

    private async Task SearchCoreAsync(string keyword, int? autoEpisode)
    {
        if (_service is not { } service) return;
        var text = keyword.Trim();
        _lastKeyword = text;
        _lastAutoEpisode = autoEpisode;
        var token = BeginLoad();
        _query = token;
        if (!IsCurrent(token)) return;
        Browser.SetResults([]);
        SetNextPages([]);
        _resources = [];
        ShowEmptyNotice = false;
        if (text.Length == 0)
        {
            EmptyNotice = IdlePrompt;
            ShowEmptyNotice = true;
            EndLoad(token);
            return;
        }
        // 搜别的词从这一刻起就换掉缓存里的上一批：在飞期间离开再回来也不会把旧片的结果挂在新词下面。
        var detail = _detailSearch;
        _cache?.Save(new(service.ConnectionStamp, text, [], autoEpisode,
            "上次搜索没有完成，按回车或点击搜索图标重新搜索", true, detail));
        var progress = SearchProgress.Start(detail?.Media.Title ?? "关键词：" + text, detail?.Media.Year, () => IsCurrent(token));
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
            var sites = SearchSites.Selection;
            if (detail is not null) _detailSearch = detail = detail with { Sites = sites };
            var page = detail is null
                ? new MoviePilotResourcePage(await service.SearchByKeywordAsync(text, token, sites, progress).ConfigureAwait(true), [])
                : await service.SearchDetailResourcesAsync(detail, token, progress).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            var found = page.Resources;
            ShowResources(service, found, autoEpisode);
            SetNextPages(page.NextPages);
            EmptyNotice = $"MoviePilot 上没搜到「{text}」的可下载版本，换个关键字或稍后再试";
            ShowEmptyNotice = found.Count == 0;
            SaveResults(service);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;
            Report("搜索版本失败", error);
        }
        finally { EndLoad(token); }
    }

    private void ShowResources(MoviePilotService service, IReadOnlyList<MoviePilotResource> resources, int? autoEpisode)
    {
        _resources = resources;
        Browser.SetResults(resources.Select(resource => new MoviePilotResourceRow(resource, DownloadAsync, OpenDetails)
        {
            State = MoviePilotResourceRow.StateOf(service.DownloadState(resource))
        }), autoEpisode);
    }

    private void SetNextPages(IReadOnlyList<MoviePilotSearchPage> pages)
    {
        _nextPages = pages;
        OnPropertyChanged(nameof(MoreVisibility));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    private void SaveResults(MoviePilotService service) =>
        _cache?.Save(new(service.ConnectionStamp, _lastKeyword, _resources, _lastAutoEpisode,
            EmptyNotice, ShowEmptyNotice, _detailSearch, _nextPages));

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMore()
    {
        if (_service is not { } service || _detailSearch is not { } detail || !CanLoadMore) return;
        var pages = _nextPages.ToArray();
        var token = BeginLoad();
        var progress = SearchProgress.Start(detail.Media.Title, detail.Media.Year, () => IsCurrent(token));
        try
        {
            foreach (var page in pages)
            {
                try
                {
                    var result = await service.SearchDetailResourcesAsync(detail, token, progress, page).ConfigureAwait(true);
                    if (!IsCurrent(token)) return;
                    if (result.Resources.Count > 0)
                    {
                        _resources = [.. _resources, .. result.Resources];
                        Browser.AppendResults(result.Resources.Select(resource => new MoviePilotResourceRow(resource, DownloadAsync, OpenDetails)
                        {
                            State = MoviePilotResourceRow.StateOf(service.DownloadState(resource))
                        }));
                        ShowEmptyNotice = false;
                    }
                    SetNextPages(_nextPages.Where(item => item != page).Concat(result.NextPages).ToArray());
                    SaveResults(service);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    if (!IsCurrent(token)) return;
                    Report("加载更多资源失败，可重试未完成的来源", error);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (IsCurrent(token)) Report("加载更多资源失败", error); }
        finally { EndLoad(token); }
    }

    protected override void BusyChanged()
    {
        if (!Busy) SearchProgress.Stop();
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 进入搜索页的「MP 资源」来源时先铺 MoviePilot 网页端上次搜索留下的结果（网页的资源搜索页打开时
    /// 看到的就是它，见 <see cref="MoviePilotService.LastSearchResultsAsync"/>）。只在这一页还没有结果时读：
    /// 自己搜出来的和已经铺过的缓存都不覆盖。后台预读不转忙态，失败只记日志、保留「输入关键词」空态，
    /// 用户照常可以直接搜。
    /// </summary>
    internal async Task LoadCachedResultsAsync()
    {
        var service = _service;
        if (service is not { Available: true } || Busy || Browser.HasResults || _lastKeyword.Length != 0) return;
        var token = BeginBackgroundLoad();
        try
        {
            var found = await service.LastSearchResultsAsync(token).ConfigureAwait(true);
            if (!IsCurrent(token) || !ReferenceEquals(service, _service) || Browser.HasResults) return;
            Browser.SetResults(found.Select(resource => new MoviePilotResourceRow(resource, DownloadAsync, OpenDetails)
            {
                State = MoviePilotResourceRow.StateOf(service.DownloadState(resource))
            }));
            ShowEmptyNotice = found.Count == 0;
            if (found.Count == 0) EmptyNotice = IdlePrompt;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token)) Log.Warn(Category, "读取 MoviePilot 上次搜索结果失败", error);
        }
        finally { EndLoad(token); }
    }

    /// <summary>
    /// 站点范围只影响下一次搜索（2026-10-29 用户令：取消勾选默认站点时不许把眼前的结果清空）。
    /// 在飞的旧搜索仍然作废 —— 它的站点范围已经变了；已经铺出来的卡片留在原地，下一次搜索才换掉它们。
    /// </summary>
    private void OnSearchSitesChanged()
    {
        base.Cancel();
        SetNextPages([]);
        IsReady = true;
        if (_service is { } service) SaveResults(service);
        if (Browser.HasResults)
        {
            Notify("搜索站点已更改", "当前结果仍是刚才那次搜索的；按回车或点搜索图标后才会按新站点重搜。", InfoBarSeverity.Informational);
            return;
        }
        EmptyNotice = SearchSites.HasSelection ? "搜索站点已更改，按回车或点击搜索图标重新搜索" : "请至少选择一个站点，或使用 MoviePilot 默认站点";
        ShowEmptyNotice = true;
    }

    public override void Cancel()
    {
        if (!_life.IsCancellationRequested) _life.Cancel();
        SearchSites.Cancel();
        base.Cancel();
    }

    public override void Dispose()
    {
        SearchSites.SelectionChanged -= OnSearchSitesChanged;
        SearchProgress.Stop();
        _life.Dispose();
        base.Dispose();
    }

    private void OpenDetails(MoviePilotResourceRow row)
    {
        if (row.Resource.DetailsUri is not { } uri || _launcher is null) return;
        try { _launcher.OpenUrl(uri.AbsoluteUri); }
        catch (Exception error) { Report("打开种子页面失败", error); }
    }

    private async Task DownloadAsync(MoviePilotResourceRow row)
    {
        if (_service is null || !row.CanDownload || !Browser.Rows.Contains(row)) return;
        var token = BeginAction();
        if (_chooseDownload is null) return;
        var options = await _chooseDownload(row.Resource, token).ConfigureAwait(true);
        if (options is null || token.IsCancellationRequested) return;
        row.State = MoviePilotDownloadState.Working;
        try
        {
            await _service.DownloadAsync(row.Resource, token, options).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;
            row.State = MoviePilotDownloadState.Done;
            Notify(null, $"已加入下载：{row.Title}", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            row.State = error is MoviePilotOperationUncertainException or MoviePilotOperationBlockedException
                ? MoviePilotDownloadState.Uncertain : MoviePilotDownloadState.Failed;
            Report($"加入下载未完成：{row.Title}", error);
        }
    }
}

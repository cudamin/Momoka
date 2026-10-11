using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

/// <summary>独立窗口的关键词资源搜索；过滤和结果行与精确搜索共用。</summary>
public sealed partial class MoviePilotVersionsViewModel : PageViewModel
{
    private const string IdlePrompt = "输入关键词，按回车或点击搜索图标，在 MoviePilot 各站点查找资源";
    private MoviePilotService? _service;
    private ISystemLauncher? _launcher;
    private string _lastKeyword = "";
    private int? _lastAutoEpisode;
    private CancellationToken _query;
    private Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>>? _chooseDownload;

    internal void UseDownloadPicker(Func<MoviePilotResource, CancellationToken, Task<MoviePilotDownloadOptions?>> choose) => _chooseDownload = choose;

    public MoviePilotVersionsViewModel() => SearchSites.SelectionChanged += OnSearchSitesChanged;
    public MoviePilotSitePickerViewModel SearchSites { get; } = new();

    public MoviePilotResourceBrowserViewModel Browser { get; } = new();
    public ObservableCollection<MoviePilotResourceRow> Versions => Browser.Rows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; } = true;
    public Visibility EmptyVisibility => Show(ShowEmptyNotice);
    [ObservableProperty] public partial string EmptyNotice { get; set; } = IdlePrompt;

    internal void Attach(MoviePilotService service, ISystemLauncher launcher)
    {
        _service = service;
        _launcher = launcher;
        SearchSites.Attach(service);
    }

    public override Task ReloadAsync() => SearchAsync(_lastKeyword, _lastAutoEpisode);

    /// <summary>
    /// 搜一次关键词。<paramref name="autoEpisode"/> 是集页面开窗时带进来的目标集（词里已经不发集号了，见
    /// <see cref="MoviePilotVersionQuery.Keyword"/>）：结果出来后浏览器自动按这一集筛，用户改词自己搜时不传，
    /// 自动筛选随之解除；刷新（<see cref="ReloadAsync"/>）带着上次的目标集重搜。
    /// </summary>
    internal async Task SearchAsync(string keyword, int? autoEpisode = null)
    {
        if (_service is null) return;
        var text = keyword.Trim();
        _lastKeyword = text;
        _lastAutoEpisode = autoEpisode;
        var token = BeginLoad();
        _query = token;
        Browser.SetResults([]);
        ShowEmptyNotice = false;
        if (text.Length == 0)
        {
            EmptyNotice = IdlePrompt;
            ShowEmptyNotice = true;
            EndLoad(token);
            return;
        }
        if (!SearchSites.HasSelection)
        {
            EmptyNotice = "请先选择搜索站点，或使用 MoviePilot 默认站点";
            ShowEmptyNotice = true;
            EndLoad(token);
            return;
        }
        try
        {
            var found = await _service.SearchByKeywordAsync(text, token, SearchSites.Selection).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
            Browser.SetResults(found.Select(resource => new MoviePilotResourceRow(resource, DownloadAsync, OpenDetails)
            {
                State = MoviePilotResourceRow.StateOf(_service.DownloadState(resource))
            }), autoEpisode);
            EmptyNotice = $"MoviePilot 上没搜到「{text}」的可下载版本，换个关键字或稍后再试";
            ShowEmptyNotice = found.Count == 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;
            Report("搜索版本失败", error);
        }
        finally { EndLoad(token); }
    }

    private void OnSearchSitesChanged()
    {
        base.Cancel();
        IsReady = true;
        Browser.SetResults([]);
        EmptyNotice = SearchSites.HasSelection ? "搜索站点已更改，按回车或点击搜索图标重新搜索" : "请至少选择一个站点，或使用 MoviePilot 默认站点";
        ShowEmptyNotice = true;
    }

    public override void Cancel()
    {
        SearchSites.Cancel();
        base.Cancel();
    }

    private void OpenDetails(MoviePilotResourceRow row)
    {
        if (row.Resource.DetailsUri is not { } uri || _launcher is null) return;
        try { _launcher.OpenUrl(uri.AbsoluteUri); }
        catch (Exception error) { Report("打开种子页面失败", error); }
    }

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
            Report($"加入下载未完成：{row.Title}", error);
        }
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 订阅卡片的墙侧展示：批量选择标记与执行状态。执行态逐条移植网页端 SubscribeCard 的
/// rightBottomStateDisplay —— 用户主动搜索的短暂反馈优先，否则暂停／待定，其余不显示。
/// 终态窗口与站点等待的过期由列表页的轮询节拍统一重算（见 <see cref="MoviePilotSubscriptionsViewModel.PollWallExecutionAsync"/>），
/// 卡片自己不起计时器。
/// </summary>
public sealed partial class MoviePilotSubscriptionCard
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool BatchMode { get; set; }

    public string ExecutionText { get; private set; } = "";
    public MoviePilotExecutionTone ExecutionTone { get; private set; } = MoviePilotExecutionTone.Secondary;
    public bool HasExecution => ExecutionText.Length > 0;
    public Visibility ExecutionVisibility => HasExecution ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>卡片右下角那一刻的状态词：执行态优先，其次暂停／待定；订阅中且无执行态时留空（网页端同判据）。</summary>
    public string WallStateText => HasExecution ? ExecutionText
        : Subscription.Paused ? "已暂停" : Subscription.State == "P" ? "待定中" : "";
    public Visibility WallStateVisibility => WallStateText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public MoviePilotExecutionTone WallStateTone => HasExecution ? ExecutionTone
        : Subscription.Paused ? MoviePilotExecutionTone.Secondary : MoviePilotExecutionTone.Info;

    /// <summary>按当前时间重算可见执行态；只在读数变化时通知，避免每个轮询节拍都惊动整张卡片。</summary>
    internal void RefreshExecution(DateTimeOffset now)
    {
        var visible = MoviePilotExecutionText.VisibleCardExecution(Subscription.Execution, now);
        var text = visible is null ? "" : MoviePilotExecutionText.LabelText(visible, now);
        var tone = visible is null ? MoviePilotExecutionTone.Secondary
            : MoviePilotExecutionText.Tone(MoviePilotExecutionText.Label(visible, now)!.Value);
        if (text == ExecutionText && tone == ExecutionTone) return;
        ExecutionText = text;
        ExecutionTone = tone;
        OnPropertyChanged(nameof(ExecutionText));
        OnPropertyChanged(nameof(ExecutionTone));
        OnPropertyChanged(nameof(HasExecution));
        OnPropertyChanged(nameof(ExecutionVisibility));
        OnPropertyChanged(nameof(WallStateText));
        OnPropertyChanged(nameof(WallStateVisibility));
        OnPropertyChanged(nameof(WallStateTone));
    }
}

/// <summary>
/// 订阅墙（我的订阅）的状态与行为，逐条移植网页端 SubscribeListView／subscribe-index：
/// 关键词／状态筛选、排序与拖拽落盘、批量选择与暂停／启用／删除、订阅维护，以及执行批次的高频轮询。
/// 只服务列表页（电影／电视剧分类页与汇总库）；主页那两排与详情页不立墙、不轮询。
/// <para>
/// 轮询的定时器住在视图里（外壳惯例：DispatcherTimer 只在 View），视图按 2.5 秒节拍调
/// <see cref="PollWallExecutionAsync"/>；本类负责单趟在途收口、签名比对与「该不该补刷完整列表」的判断。
/// </para>
/// </summary>
public sealed partial class MoviePilotSubscriptionsViewModel
{
    /// <summary>网页端 ACTIVE_CARD_REFRESH_INTERVAL_MS：有活动卡片或列表出错时，至少隔这么久才补刷一次完整列表。</summary>
    private static readonly TimeSpan CardRefreshInterval = TimeSpan.FromSeconds(15);

    private bool _wallPage;
    private bool _wallActive;
    private IReadOnlyList<int> _wallOrder = [];
    private string _wallOrderConnection = "";
    private readonly HashSet<int> _wallSelected = [];
    private IReadOnlyList<MoviePilotSubscriptionBatch> _wallBatches = [];
    private readonly HashSet<string> _wallDismissedBatches = new(StringComparer.Ordinal);
    private string? _wallBatchSignature;
    private bool _wallBatchReadFailed;
    private bool _wallListFailed;
    private DateTimeOffset _wallListRequestedAt = DateTimeOffset.MinValue;
    private MoviePilotSubscriptionBatch? _wallVisibleBatch;
    private Task? _wallListRequest;
    private bool _wallListPending;
    private bool _wallListPendingSilent = true;
    private Task<bool>? _wallBatchRequest;
    private Task? _wallPollRequest;
    private bool _wallResetting;

    /// <summary>墙上此刻显示的卡片，复用 Movies／Series 里的同一批实例（图片与选择标记不另起一份）。</summary>
    public ObservableCollection<MoviePilotSubscriptionCard> Wall { get; } = [];

    [ObservableProperty]
    public partial string WallKeyword { get; set; } = "";
    [ObservableProperty]
    public partial MoviePilotSubscriptionWallFilter WallFilter { get; set; }
    /// <summary>显式排序偏好；null 表示未选过，按「存过自定义顺序就自定义、否则添加时间」起步。</summary>
    [ObservableProperty]
    public partial MoviePilotSubscriptionSort? WallSortChoice { get; set; }
    [ObservableProperty]
    public partial bool WallSortMode { get; set; }
    [ObservableProperty]
    public partial bool WallBatchMode { get; set; }
    [ObservableProperty]
    public partial bool WallSearchBusy { get; set; }
    [ObservableProperty]
    public partial bool WallRefreshBusy { get; set; }
    [ObservableProperty]
    public partial bool WallMetadataBusy { get; set; }

    public bool IsWallSeries => _pageCategory == HomeLayout.MoviePilotSeries;
    public MoviePilotSubscriptionSort EffectiveWallSort => MoviePilotSubscriptionWall.NormalizeSort(
        WallSortChoice ?? MoviePilotSubscriptionWall.DefaultSort(_wallOrder.Count > 0), IsWallSeries);
    public bool CanWallSort => MoviePilotSubscriptionWall.CanDragSort(EffectiveWallSort, WallKeyword, WallFilter, WallBatchMode);
    public bool CanWallDrag => WallSortMode && CanWallSort;
    public string WallSortLabel => MoviePilotSubscriptionWall.SortLabel(EffectiveWallSort);
    public string WallFilterLabel => MoviePilotSubscriptionWall.FilterLabel(WallFilter);
    public IReadOnlyList<MoviePilotSubscriptionWallFilter> WallFilterOptions => MoviePilotSubscriptionWall.FilterOptions(IsWallSeries);
    public IReadOnlyList<MoviePilotSubscriptionSort> WallSortOptions => MoviePilotSubscriptionWall.SortOptions(IsWallSeries);

    public int WallCount => Wall.Count;
    public Visibility WallVisibility => Show(Wall.Count > 0);
    public bool WallListFailed => _wallListFailed;
    public Visibility WallListErrorVisibility => Show(_wallPage && _wallListFailed);
    public Visibility WallEmptyVisibility => Show(_wallPage && !Busy && IsReady && Wall.Count == 0 && !_wallListFailed);
    public string WallEmptyTitle => WallKeyword.Trim().Length > 0 || WallFilter != MoviePilotSubscriptionWallFilter.All
        ? "没有符合条件的数据" : "没有数据";
    public string WallEmptyDescription => WallKeyword.Trim().Length > 0 || WallFilter != MoviePilotSubscriptionWallFilter.All
        ? "请尝试更改筛选条件" : "请通过搜索添加电影、电视剧订阅。";

    public int WallSelectedCount => _wallSelected.Count;
    public bool IsWallAllSelected => Wall.Count > 0 && Wall.All(card => _wallSelected.Contains(card.Subscription.Id));
    public string WallBatchSummary => $"已选择 {_wallSelected.Count}/{Wall.Count} 项";
    public Visibility WallBatchBannerVisibility => Show(WallBatchMode);
    public bool CanWallBatchOperate => CanManage && WallBatchMode && _wallSelected.Count > 0;
    public bool CanWallAdmin => _service?.IsSuperUser == true;

    public MoviePilotSubscriptionBatch? WallVisibleBatch => _wallVisibleBatch;
    public Visibility WallBatchVisibility => Show(_wallVisibleBatch is not null);
    public string WallBatchLabel => _wallVisibleBatch is { } batch
        ? MoviePilotExecutionText.LabelText(batch, DateTimeOffset.Now) is { Length: > 0 } text ? text : "排队中" : "";
    public MoviePilotExecutionTone WallBatchTone => _wallVisibleBatch is { } batch
        ? MoviePilotExecutionText.BatchTone(batch) : MoviePilotExecutionTone.Info;
    public string WallBatchProgressText => _wallVisibleBatch is { } batch ? $"{batch.ProcessedCount}/{batch.TotalCount}" : "";
    public int WallBatchProgress => _wallVisibleBatch is { } batch ? MoviePilotExecutionText.BatchProgressPercent(batch) : 0;
    public bool WallBatchIndeterminate => _wallVisibleBatch is { } batch && MoviePilotExecutionText.BatchProgressIndeterminate(batch);
    public bool WallBatchCanCancel => _wallVisibleBatch?.CanCancel == true;
    public bool WallBatchCanDismiss => _wallVisibleBatch is { } batch && !batch.CanCancel && MoviePilotExecutionText.IsTerminal(batch.State);

    private IEnumerable<MoviePilotSubscription> WallSubscriptions => _pageCategory switch
    {
        HomeLayout.MoviePilotMovies => Movies.Select(card => card.Subscription),
        HomeLayout.MoviePilotSeries => Series.Select(card => card.Subscription),
        _ => Movies.Concat(Series).Select(card => card.Subscription)
    };

    partial void OnWallKeywordChanged(string value) => RebuildWall();
    partial void OnWallFilterChanged(MoviePilotSubscriptionWallFilter value) => RebuildWall();
    partial void OnWallSortChoiceChanged(MoviePilotSubscriptionSort? value)
    {
        PersistWallSort();
        RebuildWall();
    }
    partial void OnWallSortModeChanged(bool value) => OnPropertyChanged(nameof(CanWallDrag));
    partial void OnWallBatchModeChanged(bool value)
    {
        SyncWallCardFlags();
        SyncWallSortMode();
        RaiseWallBatchState();
    }

    /// <summary>视图在页面可见时置 true、离开时置 false；轮询据此收放（网页端 props.active）。</summary>
    internal void SetWallActive(bool active) => _wallActive = active;

    internal void SelectWallFilter(MoviePilotSubscriptionWallFilter filter) => WallFilter = filter;

    /// <summary>选排序：非自定义会退出拖拽排序模式（网页端 selectSubscribeSort）；不在当前类型选项里的忽略。</summary>
    internal void SelectWallSort(MoviePilotSubscriptionSort sort)
    {
        if (!MoviePilotSubscriptionWall.SortOptions(IsWallSeries).Contains(sort)) return;
        WallSortChoice = sort;
        if (sort != MoviePilotSubscriptionSort.Custom) WallSortMode = false;
    }

    /// <summary>切换拖拽排序模式：进入时先退批量、并把排序定为自定义（网页端 toggleSubscribeSortMode）。</summary>
    internal void ToggleWallSortMode()
    {
        if (WallSortMode) { WallSortMode = false; return; }
        if (WallBatchMode) ExitWallBatchMode();
        WallSortChoice = MoviePilotSubscriptionSort.Custom;
        WallSortMode = true;
        SyncWallSortMode();
    }

    internal void ToggleWallBatchMode()
    {
        if (WallBatchMode) ExitWallBatchMode();
        else WallBatchMode = true;
    }

    internal void ExitWallBatchMode()
    {
        WallBatchMode = false;
        _wallSelected.Clear();
        SyncWallCardFlags();
        RaiseWallBatchState();
    }

    internal void ToggleWallSelected(MoviePilotSubscriptionCard card)
    {
        if (!WallBatchMode || !Wall.Contains(card)) return;
        var id = card.Subscription.Id;
        if (!_wallSelected.Add(id)) _wallSelected.Remove(id);
        card.IsSelected = _wallSelected.Contains(id);
        RaiseWallBatchState();
    }

    internal void ToggleWallSelectAll()
    {
        if (IsWallAllSelected) _wallSelected.Clear();
        else foreach (var card in Wall) _wallSelected.Add(card.Subscription.Id);
        SyncWallCardFlags();
        RaiseWallBatchState();
    }

    internal Task WallBatchPauseAsync() => RunWallBatchAsync(MoviePilotSubscriptionAction.Pause);
    internal Task WallBatchResumeAsync() => RunWallBatchAsync(MoviePilotSubscriptionAction.Resume);
    internal Task WallBatchDeleteAsync() => RunWallBatchAsync(MoviePilotSubscriptionAction.Delete);

    /// <summary>
    /// 批量暂停／启用／删除。逐条走服务的操作闸（结果不明锁核对、占用中不重发）；删除以服务器确认回执为准。
    /// 失败项保留在选择集里供核对后重试，删除全部成功才退批量；暂停／启用无论成败都退批量（网页端同判据）。
    /// </summary>
    private async Task RunWallBatchAsync(MoviePilotSubscriptionAction action)
    {
        if (!CanManage || _service is null || _lifetime is null || _pageCategory is null) return;
        if (_wallSelected.Count == 0) { Notify("批量操作", "请先选择要操作的订阅", InfoBarSeverity.Warning); return; }
        var verb = action switch
        {
            MoviePilotSubscriptionAction.Pause => "批量暂停",
            MoviePilotSubscriptionAction.Resume => "批量启用",
            _ => "批量删除"
        };
        var count = _wallSelected.Count;
        var confirm = action switch
        {
            MoviePilotSubscriptionAction.Pause => $"确定要暂停选中的 {count} 个订阅吗？",
            MoviePilotSubscriptionAction.Resume => $"确定要启用选中的 {count} 个订阅吗？",
            _ => $"确定要删除选中的 {count} 个订阅吗？此操作不可恢复！"
        };
        var token = _lifetime.Token;
        Working = true;
        try
        {
            if (!await ConfirmAsync(verb, confirm, verb)) return;
            if (token.IsCancellationRequested || !Live) return;
            var selected = Wall.Where(card => _wallSelected.Contains(card.Subscription.Id))
                .Select(card => card.Subscription).ToArray();
            var result = await _service.ManageSubscriptionBatchAsync(selected, action, token);
            if (token.IsCancellationRequested || !Live) return;
            NotifyWallBatchResult(verb, action, result);
            if (action == MoviePilotSubscriptionAction.Delete)
            {
                _wallSelected.Clear();
                foreach (var id in result.FailedIds) _wallSelected.Add(id);
                await RefreshWallListAsync(false);
                if (result.FailedIds.Count == 0) ExitWallBatchMode();
                else { SyncWallCardFlags(); RaiseWallBatchState(); }
            }
            else
            {
                await RefreshWallListAsync(false);
                ExitWallBatchMode();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && Live) Report(verb, error); }
        finally { if (!token.IsCancellationRequested) Working = false; }
    }

    private void NotifyWallBatchResult(string verb, MoviePilotSubscriptionAction action, MoviePilotSubscriptionBatchResult result)
    {
        var success = action switch
        {
            MoviePilotSubscriptionAction.Pause => $"成功暂停 {result.SuccessCount} 个订阅",
            MoviePilotSubscriptionAction.Resume => $"成功启用 {result.SuccessCount} 个订阅",
            _ => $"成功删除 {result.SuccessCount} 个订阅"
        };
        var failed = action switch
        {
            MoviePilotSubscriptionAction.Pause => $"暂停失败 {result.FailedCount} 个订阅",
            MoviePilotSubscriptionAction.Resume => $"启用失败 {result.FailedCount} 个订阅",
            _ => $"删除失败 {result.FailedCount} 个订阅"
        };
        if (result.FailedCount == 0) { Notify(verb, success, InfoBarSeverity.Success); return; }
        var message = result.SuccessCount > 0 ? $"{success}；{failed}" : failed;
        if (result.UncertainCount > 0) message += $"（其中 {result.UncertainCount} 个结果不明，请在卡片上核对后重试）";
        Notify(verb, message, result.SuccessCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
    }

    /// <summary>搜索当前媒体类型全部订阅：可能成批触发下载，先二次确认；回执按网页端的提示梯展示。</summary>
    internal async Task WallSearchAllAsync()
    {
        if (WallSearchBusy || !CanManage || _service is null || _lifetime is null || _pageCategory is null) return;
        WallSearchBusy = true;
        var token = _lifetime.Token;
        try
        {
            if (!await ConfirmAsync("确认搜索当前媒体类型订阅",
                "系统会依次搜索当前账号可访问的当前媒体类型订阅，并访问相应站点。订阅较多时需要一些时间，是否继续？", "搜索")) return;
            if (token.IsCancellationRequested || !Live) return;
            var submission = await _service.SearchAllSubscriptionsAsync(IsWallSeries, token);
            if (token.IsCancellationRequested || !Live) return;
            var notice = MoviePilotSubscriptionSearchText.SearchAllNotice(submission);
            Notify("订阅维护", notice.Message, notice.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && Live) Report("订阅维护", error); }
        finally { if (!token.IsCancellationRequested) WallSearchBusy = false; }
    }

    /// <summary>刷新当前媒体类型订阅资源（管理员入口，无需二次确认）。</summary>
    internal async Task WallRefreshResourcesAsync()
    {
        if (WallRefreshBusy || !CanManage || _service is null || _lifetime is null || _pageCategory is null) return;
        WallRefreshBusy = true;
        var token = _lifetime.Token;
        try
        {
            await _service.RefreshSubscriptionsAsync(IsWallSeries, token);
            if (token.IsCancellationRequested || !Live) return;
            Notify("订阅维护", "订阅刷新任务已启动！", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && Live) Report("订阅维护", error); }
        finally { if (!token.IsCancellationRequested) WallRefreshBusy = false; }
    }

    /// <summary>更新当前媒体类型订阅元数据（管理员入口，无需二次确认）。</summary>
    internal async Task WallRefreshMetadataAsync()
    {
        if (WallMetadataBusy || !CanManage || _service is null || _lifetime is null || _pageCategory is null) return;
        WallMetadataBusy = true;
        var token = _lifetime.Token;
        try
        {
            await _service.RefreshSubscriptionMetadataAsync(IsWallSeries, token);
            if (token.IsCancellationRequested || !Live) return;
            Notify("订阅维护", "订阅元数据更新任务已启动！", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested && Live) Report("订阅维护", error); }
        finally { if (!token.IsCancellationRequested) WallMetadataBusy = false; }
    }

    /// <summary>只停止横幅所代表的那次完整搜索；成功后静默补刷列表与批次（网页端 cancelExecutionBatch）。</summary>
    internal async Task CancelWallBatchAsync()
    {
        if (_wallVisibleBatch is not { CanCancel: true } batch || _service is null || _lifetime is null) return;
        var token = _lifetime.Token;
        try
        {
            await _service.CancelSubscriptionExecutionBatchAsync(batch, token);
            if (token.IsCancellationRequested || !Live) return;
            Notify("订阅搜索", "正在停止这次搜索", InfoBarSeverity.Success);
            await RefreshWallListAsync(true);
            await ReadWallBatchesAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested && Live) Notify("订阅搜索", "暂时无法停止，请稍后重试", InfoBarSeverity.Error); }
    }

    /// <summary>收起已经结束的搜索横幅，不影响后台记录或后续搜索（网页端 dismissExecutionBatch）。</summary>
    internal void DismissWallBatch()
    {
        if (_wallVisibleBatch is not { } batch || MoviePilotExecutionText.IsActive(batch)) return;
        _wallDismissedBatches.Add(batch.BatchId);
        RefreshWallBatch(DateTimeOffset.Now);
    }

    /// <summary>
    /// 拖拽落盘（网页端 saveSubscribeOrder）：先把屏上顺序记为自定义顺序并发到服务器，失败则回滚到落盘前、
    /// 重新排序并提示。只在可拖拽语境（自定义＋无关键词＋状态全部＋非批量＋排序模式）下生效。
    /// </summary>
    internal async Task SaveWallOrderAsync()
    {
        if (!CanWallDrag || _service is null || _lifetime is null || _pageCategory is null) return;
        var token = _lifetime.Token;
        var confirmed = _wallOrder;
        var order = Wall.Select(card => card.Subscription.Id).ToArray();
        _wallOrder = order;
        WallSortChoice = MoviePilotSubscriptionSort.Custom;
        try
        {
            await _service.SaveSubscriptionOrderAsync(IsWallSeries, order, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (token.IsCancellationRequested || !Live) return;
            _wallOrder = confirmed;
            RebuildWall();
            Log.Info("moviepilot", $"保存订阅自定义顺序失败：{error.Message}");
            Notify("保存排序", "请求失败，请稍后重试", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// 一个轮询节拍（网页端 runExecutionPoll）：高频只读轻量批次；批次签名变了、或有活动卡片／列表出错且距上次
    /// 列表请求超过 15 秒，才静默补刷完整列表。每拍都重算卡片执行态与横幅，让终态窗口与站点等待自然过期。
    /// 单趟在途收口，重复触发只等同一个请求。
    /// </summary>
    internal async Task PollWallExecutionAsync()
    {
        if (_wallPollRequest is { } inflight) { await inflight; return; }
        var request = RunWallPollAsync();
        _wallPollRequest = request;
        try { await request; }
        finally { if (ReferenceEquals(_wallPollRequest, request)) _wallPollRequest = null; }
    }

    private async Task RunWallPollAsync()
    {
        if (!_wallActive || !_wallPage || _detail || !Live || _service is null || _lifetime is not { IsCancellationRequested: false }) return;
        var token = _lifetime.Token;
        var batchChanged = await ReadWallBatchesAsync();
        if (!_wallActive || !Live || token.IsCancellationRequested) return;
        var now = DateTimeOffset.Now;
        var cardRefreshDue = (MoviePilotExecutionText.HasActiveCardExecution(WallSubscriptions) || _wallListFailed)
            && now - _wallListRequestedAt >= CardRefreshInterval;
        if (batchChanged || cardRefreshDue) await RefreshWallListAsync(true);
        RefreshWallExecution(now);
    }

    private void RefreshWallExecution(DateTimeOffset now)
    {
        foreach (var card in Wall) card.RefreshExecution(now);
        RefreshWallBatch(now);
    }

    /// <summary>批次读取的单趟在途收口（网页端 fetchExecutionBatches）；可见性、手动刷新与轮询共享同一个请求。</summary>
    private async Task<bool> ReadWallBatchesAsync()
    {
        if (_wallBatchRequest is { } inflight) return await inflight;
        if (_service is null || _lifetime is not { IsCancellationRequested: false }) return false;
        var request = RequestWallBatchesAsync();
        _wallBatchRequest = request;
        try { return await request; }
        finally { if (ReferenceEquals(_wallBatchRequest, request)) _wallBatchRequest = null; }
    }

    /// <summary>读批次并比对签名；失败保留上次快照、只立失败标记，不判订阅列表失败（网页端 requestExecutionBatches）。</summary>
    private async Task<bool> RequestWallBatchesAsync()
    {
        var token = _lifetime!.Token;
        try
        {
            var batches = await _service!.SubscriptionExecutionBatchesAsync(token);
            if (!Live || token.IsCancellationRequested) return false;
            var signature = MoviePilotExecutionText.Signature(batches);
            var changed = _wallBatchReadFailed || (_wallBatchSignature is not null && signature != _wallBatchSignature);
            _wallBatchReadFailed = false;
            _wallBatchSignature = signature;
            _wallBatches = batches;
            RefreshWallBatch(DateTimeOffset.Now);
            return changed;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception) { if (Live) _wallBatchReadFailed = true; return false; }
    }

    /// <summary>
    /// 列表刷新的单趟在途 worker（网页端 fetchSubscriptions）：在途期间的刷新意图合并到同一个 trailing 槽位，
    /// 显式刷新优先于静默。静默刷新遇显式加载在飞（Busy）时让位，避免后台核对取消用户的手动刷新。
    /// </summary>
    internal async Task RefreshWallListAsync(bool silent)
    {
        if (_wallListRequest is { } inflight)
        {
            _wallListPending = true;
            _wallListPendingSilent = _wallListPendingSilent && silent;
            await inflight;
            return;
        }
        var request = RunWallListRefreshAsync(silent);
        _wallListRequest = request;
        try { await request; }
        finally
        {
            if (ReferenceEquals(_wallListRequest, request)) _wallListRequest = null;
            _wallListPending = false;
        }
    }

    private async Task RunWallListRefreshAsync(bool silent)
    {
        var context = silent;
        while (true)
        {
            _wallListPending = false;
            await RequestWallListAsync(context);
            if (!_wallListPending) return;
            context = _wallListPendingSilent;
        }
    }

    private async Task RequestWallListAsync(bool silent)
    {
        if (_service is null || _lifetime is not { IsCancellationRequested: false }) return;
        if (silent && Busy) return;
        var initial = !IsReady;
        _wallListRequestedAt = DateTimeOffset.Now;
        var token = !silent || initial ? BeginLoad() : BeginBackgroundLoad();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            var items = await _service.SubscriptionsAsync(pending.Token);
            if (!IsCurrent(token) || !Live) return;
            _wallListFailed = false;
            Apply(Movies, items.Where(item => !item.IsSeries).ToArray());
            Apply(Series, items.Where(item => item.IsSeries).ToArray());
            NotifyRows();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsCurrent(token)) return;
            _wallListFailed = true;
            if (!silent || initial) Report("读取 MoviePilot 订阅失败", error);
        }
        finally { EndLoad(token); OnPropertyChanged(nameof(WallListErrorVisibility)); }
    }

    /// <summary>
    /// 按当前关键词／状态／排序重建墙，复用 Movies／Series 的卡片实例；选择集裁剪到可见项，
    /// 再逐卡重算执行态与横幅。汇总库与主页不立墙（_pageCategory 为空）。
    /// </summary>
    private void RebuildWall()
    {
        if (_pageCategory is null)
        {
            if (Wall.Count > 0) Wall.Clear();
            return;
        }
        var source = IsWallSeries ? Series : Movies;
        var items = source.Select(card => card.Subscription).ToArray();
        var matched = items.Where(item => MoviePilotSubscriptionWall.Matches(item, WallKeyword, WallFilter)).ToArray();
        var sorted = MoviePilotSubscriptionWall.Sort(matched, EffectiveWallSort, _wallOrder);
        var visible = new HashSet<int>();
        for (var index = 0; index < sorted.Count; index++)
        {
            var card = source.First(candidate => candidate.Subscription.Id == sorted[index].Id);
            visible.Add(card.Subscription.Id);
            if (index < Wall.Count && ReferenceEquals(Wall[index], card)) continue;
            var existing = Wall.IndexOf(card);
            if (existing >= 0) Wall.Move(existing, index);
            else Wall.Insert(index, card);
        }
        while (Wall.Count > sorted.Count) Wall.RemoveAt(Wall.Count - 1);
        _wallSelected.IntersectWith(visible);
        var now = DateTimeOffset.Now;
        foreach (var card in source)
        {
            card.IsSelected = _wallSelected.Contains(card.Subscription.Id);
            card.BatchMode = WallBatchMode;
        }
        foreach (var card in Wall) card.RefreshExecution(now);
        RefreshWallBatch(now);
        SyncWallSortMode();
        RaiseWallState();
    }

    private void SyncWallCardFlags()
    {
        foreach (var card in Movies.Concat(Series))
        {
            card.BatchMode = WallBatchMode;
            card.IsSelected = _wallSelected.Contains(card.Subscription.Id);
        }
    }

    /// <summary>语境不再允许拖拽（换了非自定义排序、来了关键词／筛选、进了批量）时自动退出排序模式。</summary>
    private void SyncWallSortMode()
    {
        if (WallSortMode && !CanWallSort) WallSortMode = false;
    }

    private void PersistWallSort()
    {
        // 复位（离页／换连接）把偏好清回 null 只是收状态，不能据此抹掉设置文件里已存的那一份。
        if (_wallResetting || _service is null || _pageCategory is null) return;
        _service.SaveSubscriptionSortPreference(IsWallSeries ? "电视剧" : "电影",
            WallSortChoice is { } sort ? MoviePilotSubscriptionWall.SortKey(sort) : null);
    }

    /// <summary>每个连接读一次自定义顺序；读不到按空表兜底。没有显式排序偏好时同步默认值并落盘（网页端 syncDefaultSortBy）。</summary>
    private async Task LoadWallOrderAsync(CancellationToken token)
    {
        var parsed = MoviePilotSubscriptionWall.ParseSort(_service!.SubscriptionSortPreference(IsWallSeries ? "电视剧" : "电影"));
        WallSortChoice = parsed is { } sort ? MoviePilotSubscriptionWall.NormalizeSort(sort, IsWallSeries) : null;
        try { _wallOrder = await _service.SubscriptionOrderAsync(IsWallSeries, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { _wallOrder = []; Log.Info("moviepilot", $"读取订阅自定义顺序失败：{error.Message}"); }
        if (WallSortChoice is null) WallSortChoice = MoviePilotSubscriptionWall.DefaultSort(_wallOrder.Count > 0);
    }

    private void RefreshWallBatch(DateTimeOffset now)
    {
        _wallVisibleBatch = MoviePilotExecutionText.SelectVisibleBatch(_wallBatches, _wallDismissedBatches, now);
        OnPropertyChanged(nameof(WallVisibleBatch));
        OnPropertyChanged(nameof(WallBatchVisibility));
        OnPropertyChanged(nameof(WallBatchLabel));
        OnPropertyChanged(nameof(WallBatchTone));
        OnPropertyChanged(nameof(WallBatchProgressText));
        OnPropertyChanged(nameof(WallBatchProgress));
        OnPropertyChanged(nameof(WallBatchIndeterminate));
        OnPropertyChanged(nameof(WallBatchCanCancel));
        OnPropertyChanged(nameof(WallBatchCanDismiss));
    }

    private void RaiseWallState()
    {
        OnPropertyChanged(nameof(WallCount));
        OnPropertyChanged(nameof(WallVisibility));
        OnPropertyChanged(nameof(WallEmptyVisibility));
        OnPropertyChanged(nameof(WallEmptyTitle));
        OnPropertyChanged(nameof(WallEmptyDescription));
        OnPropertyChanged(nameof(EffectiveWallSort));
        OnPropertyChanged(nameof(CanWallSort));
        OnPropertyChanged(nameof(CanWallDrag));
        OnPropertyChanged(nameof(WallSortLabel));
        OnPropertyChanged(nameof(WallFilterLabel));
        RaiseWallBatchState();
    }

    private void RaiseWallBatchState()
    {
        OnPropertyChanged(nameof(WallSelectedCount));
        OnPropertyChanged(nameof(IsWallAllSelected));
        OnPropertyChanged(nameof(WallBatchSummary));
        OnPropertyChanged(nameof(WallBatchBannerVisibility));
        OnPropertyChanged(nameof(CanWallBatchOperate));
    }

    /// <summary>离页／换连接时收掉墙的全部状态；在途 worker 由寿命取消收口，这里只清数据与标记。</summary>
    private void ResetWall()
    {
        _wallResetting = true;
        _wallActive = false;
        _wallOrder = [];
        _wallOrderConnection = "";
        _wallSelected.Clear();
        _wallBatches = [];
        _wallDismissedBatches.Clear();
        _wallBatchSignature = null;
        _wallBatchReadFailed = false;
        _wallListFailed = false;
        _wallListRequestedAt = DateTimeOffset.MinValue;
        _wallVisibleBatch = null;
        _wallListPending = false;
        _wallListPendingSilent = true;
        WallKeyword = "";
        WallFilter = MoviePilotSubscriptionWallFilter.All;
        WallSortChoice = null;
        WallSortMode = false;
        WallBatchMode = false;
        _wallResetting = false;
        Wall.Clear();
        RaiseWallState();
        OnPropertyChanged(nameof(WallListErrorVisibility));
        OnPropertyChanged(nameof(WallBatchVisibility));
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.SubscriptionWallFixture;

/// <summary>
/// 订阅墙视图模型的离线验收：真实 <see cref="MoviePilotSubscriptionsViewModel"/>（含 Wall 分部）＋真实
/// <see cref="MoviePilotService"/>，只把 HTTP 换成假处理器。覆盖筛选排序、排序偏好落盘、拖拽回滚、批量操作、
/// 订阅维护、执行横幅与轮询节拍。WinUI 类型用 Boundaries.cs 里的惰性桩。
/// </summary>
internal static class WallTests
{
    internal static async Task RunAsync(List<string> failures)
    {
        await FilterAndSort(failures);
        await SortPreference(failures);
        await OrderSaveRestore(failures);
        await BatchSelection(failures);
        await BatchOperations(failures);
        await Maintenance(failures);
        await ExecutionBanner(failures);
        await Polling(failures);
        CardExecution(failures);
        await ListFailure(failures);
    }

    private static async Task FilterAndSort(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions =
        [
            Sub(1, "Alpha", "电视剧", "R", total: 10, lack: 0, completed: 10, date: "2026-11-01T00:00:00"),
            Sub(2, "Beta", "电视剧", "S", date: "2026-11-03T00:00:00"),
            Sub(3, "Gamma", "电视剧", "P", date: "2026-11-02T00:00:00")
        ];
        await wall.Vm.ReloadAsync();
        if (wall.Vm.Wall.Count != 3) failures.Add($"filter: expected 3 wall cards, got {wall.Vm.Wall.Count}");
        if (!ReferenceEquals(wall.Vm.Wall[0], wall.Vm.Series.First(card => card.Subscription.Id == wall.Vm.Wall[0].Subscription.Id)))
            failures.Add("filter: wall did not reuse the series card instance");

        wall.Vm.SelectWallFilter(MoviePilotSubscriptionWallFilter.Paused);
        if (!wall.Vm.Wall.Select(card => card.Subscription.Id).SequenceEqual(new[] { 2 }))
            failures.Add("filter: paused filter did not isolate Beta");
        wall.Vm.SelectWallFilter(MoviePilotSubscriptionWallFilter.Completed);
        if (!wall.Vm.Wall.Select(card => card.Subscription.Id).SequenceEqual(new[] { 1 }))
            failures.Add("filter: completed filter did not isolate Alpha");
        wall.Vm.SelectWallFilter(MoviePilotSubscriptionWallFilter.Pending);
        if (!wall.Vm.Wall.Select(card => card.Subscription.Id).SequenceEqual(new[] { 3 }))
            failures.Add("filter: pending filter did not isolate Gamma");
        wall.Vm.SelectWallFilter(MoviePilotSubscriptionWallFilter.All);

        wall.Vm.WallKeyword = "alp";
        if (!wall.Vm.Wall.Select(card => card.Subscription.Id).SequenceEqual(new[] { 1 }))
            failures.Add("filter: keyword did not match case-insensitively");
        wall.Vm.WallKeyword = "";

        wall.Vm.SelectWallSort(MoviePilotSubscriptionSort.AddTime);
        if (!wall.Vm.Wall.Select(card => card.Subscription.Id).SequenceEqual(new[] { 2, 3, 1 }))
            failures.Add("sort: add-time descending order wrong");
        if (wall.Vm.WallEmptyVisibility != Microsoft.UI.Xaml.Visibility.Collapsed)
            failures.Add("filter: non-empty wall showed empty state");
    }

    private static async Task SortPreference(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        await wall.Vm.ReloadAsync();
        if (wall.Vm.WallSortChoice != MoviePilotSubscriptionSort.AddTime)
            failures.Add("sortpref: empty order did not default to add-time");
        if (wall.Settings.MoviePilot.SubscriptionSorts.GetValueOrDefault("电视剧") != "date")
            failures.Add("sortpref: default sort was not persisted");

        wall.Vm.SelectWallSort(MoviePilotSubscriptionSort.LastUpdate);
        if (wall.Settings.MoviePilot.SubscriptionSorts.GetValueOrDefault("电视剧") != "last_update")
            failures.Add("sortpref: explicit choice was not persisted");
        if (wall.Vm.WallSortLabel != "最后更新时间") failures.Add("sortpref: sort label wrong");

        // 再附加（离页又回来）复位墙状态，但不能抹掉设置文件里已存的排序偏好。
        wall.Vm.Attach(wall.Service);
        if (wall.Settings.MoviePilot.SubscriptionSorts.GetValueOrDefault("电视剧") != "last_update")
            failures.Add("sortpref: re-attach wiped the persisted sort");

        using var movie = new WallHarness(HomeLayout.MoviePilotMovies);
        movie.Handler.Subscriptions = [Sub(9, "Movie", "电影", "R")];
        await movie.Vm.ReloadAsync();
        movie.Vm.SelectWallSort(MoviePilotSubscriptionSort.LackEpisode);
        if (movie.Vm.EffectiveWallSort == MoviePilotSubscriptionSort.LackEpisode)
            failures.Add("sortpref: movie accepted lack-episode sort");
        if (movie.Vm.WallSortOptions.Contains(MoviePilotSubscriptionSort.LackEpisode))
            failures.Add("sortpref: movie sort options included lack-episode");

        using var custom = new WallHarness(HomeLayout.MoviePilotSeries);
        custom.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R"), Sub(2, "Beta", "电视剧", "R")];
        custom.Handler.Order = new object[] { new { id = 2 }, new { id = 1 } };
        await custom.Vm.ReloadAsync();
        if (custom.Vm.WallSortChoice != MoviePilotSubscriptionSort.Custom)
            failures.Add("sortpref: stored custom order did not default to custom");
        if (!custom.Vm.Wall.Select(card => card.Subscription.Id).SequenceEqual(new[] { 2, 1 }))
            failures.Add("sortpref: custom order not applied to wall");

        // 顺序读取失败按空表兜底，列表照常立起来。
        using var broken = new WallHarness(HomeLayout.MoviePilotSeries);
        broken.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        broken.Handler.FailOrderLoad = true;
        await broken.Vm.ReloadAsync();
        if (broken.Vm.Wall.Count != 1 || broken.Vm.WallSortChoice != MoviePilotSubscriptionSort.AddTime)
            failures.Add("sortpref: order load failure did not fall back to add-time");
    }

    private static async Task OrderSaveRestore(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions =
        [
            Sub(1, "Alpha", "电视剧", "R", date: "2026-11-01T00:00:00"),
            Sub(2, "Beta", "电视剧", "R", date: "2026-11-02T00:00:00"),
            Sub(3, "Gamma", "电视剧", "R", date: "2026-11-03T00:00:00")
        ];
        wall.Handler.Order = new object[] { new { id = 1 }, new { id = 2 }, new { id = 3 } };
        await wall.Vm.ReloadAsync();
        wall.Vm.ToggleWallSortMode();
        if (!wall.Vm.WallSortMode || !wall.Vm.CanWallDrag) failures.Add("order: sort mode did not enable drag");
        if (wall.Vm.WallBatchMode) failures.Add("order: sort mode left batch mode on");

        // 拖拽：把 Gamma 移到最前，落盘。
        wall.Vm.Wall.Move(2, 0);
        await wall.Vm.SaveWallOrderAsync();
        if (wall.Handler.OrderSaves != 1) failures.Add($"order: expected 1 save, got {wall.Handler.OrderSaves}");
        if (wall.Handler.Bodies.Last() != "[{\"id\":3},{\"id\":1},{\"id\":2}]")
            failures.Add("order: save body did not match the dragged order: " + wall.Handler.Bodies.Last());

        // 落盘失败：回滚到落盘前的顺序并提示。
        wall.Handler.FailOrderSave = true;
        wall.Vm.Wall.Move(0, 2);
        await wall.Vm.SaveWallOrderAsync();
        if (!wall.Vm.Wall.Select(card => card.Subscription.Id).SequenceEqual(new[] { 3, 1, 2 }))
            failures.Add("order: failed save did not restore the confirmed order");
        if (!wall.Vm.NoticeOpen) failures.Add("order: failed save did not notify");

        // 关键词语境不允许拖拽：排序模式自动退出。
        wall.Handler.FailOrderSave = false;
        wall.Vm.WallKeyword = "alpha";
        if (wall.Vm.WallSortMode) failures.Add("order: keyword did not auto-exit sort mode");
    }

    private static async Task BatchSelection(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R"), Sub(2, "Beta", "电视剧", "R"), Sub(3, "Gamma", "电视剧", "R")];
        await wall.Vm.ReloadAsync();
        wall.Vm.ToggleWallBatchMode();
        if (!wall.Vm.WallBatchMode) failures.Add("batch: toggle did not enter batch mode");
        if (!wall.Vm.Wall.All(card => card.BatchMode)) failures.Add("batch: cards did not enter batch mode");

        wall.Vm.ToggleWallSelected(wall.Vm.Wall[0]);
        wall.Vm.ToggleWallSelected(wall.Vm.Wall[1]);
        if (wall.Vm.WallSelectedCount != 2) failures.Add($"batch: expected 2 selected, got {wall.Vm.WallSelectedCount}");
        if (wall.Vm.WallBatchSummary != "已选择 2/3 项") failures.Add("batch: summary wrong: " + wall.Vm.WallBatchSummary);
        if (wall.Vm.IsWallAllSelected) failures.Add("batch: partial selection reported all-selected");

        wall.Vm.ToggleWallSelectAll();
        if (!wall.Vm.IsWallAllSelected || wall.Vm.WallSelectedCount != 3) failures.Add("batch: select-all did not select all");
        wall.Vm.ToggleWallSelectAll();
        if (wall.Vm.WallSelectedCount != 0) failures.Add("batch: deselect-all did not clear");

        // 筛选裁剪选择集：选全部后切到暂停，选择集只剩可见项。
        wall.Vm.ToggleWallSelectAll();
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R"), Sub(2, "Beta", "电视剧", "S")];
        await wall.Vm.ReloadAsync();
        wall.Vm.SelectWallFilter(MoviePilotSubscriptionWallFilter.Paused);
        if (wall.Vm.WallSelectedCount != 1) failures.Add($"batch: filter did not prune selection, got {wall.Vm.WallSelectedCount}");

        wall.Vm.ExitWallBatchMode();
        if (wall.Vm.WallBatchMode || wall.Vm.WallSelectedCount != 0) failures.Add("batch: exit did not clear mode and selection");
    }

    private static async Task BatchOperations(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R"), Sub(2, "Beta", "电视剧", "R")];
        await wall.Vm.ReloadAsync();
        wall.Vm.ToggleWallBatchMode();
        wall.Vm.ToggleWallSelectAll();

        // 无选择 → 警告，不发请求。
        wall.Vm.ToggleWallSelectAll();
        await wall.Vm.WallBatchPauseAsync();
        if (wall.Vm.NoticeMessage != "请先选择要操作的订阅") failures.Add("batch: empty selection did not warn");
        if (wall.Handler.Requests.Any(request => request.Contains("subscribe/status/"))) failures.Add("batch: empty selection sent requests");

        // 暂停全部成功 → 成功提示并退出批量。
        wall.Vm.ToggleWallSelectAll();
        await wall.Vm.WallBatchPauseAsync();
        if (wall.Handler.Requests.Count(request => request.Contains("subscribe/status/") && request.Contains("state=S")) != 2)
            failures.Add("batch: pause did not send two state=S requests");
        if (wall.Vm.NoticeMessage != "成功暂停 2 个订阅") failures.Add("batch: pause notice wrong: " + wall.Vm.NoticeMessage);
        if (wall.Vm.WallBatchMode) failures.Add("batch: pause did not exit batch mode");

        // 删除部分失败：失败项保留选中、留在批量模式。
        wall.Handler.DeleteFailIds.Add(2);
        wall.Vm.ToggleWallBatchMode();
        wall.Vm.ToggleWallSelectAll();
        await wall.Vm.WallBatchDeleteAsync();
        if (wall.Vm.NoticeSeverity != Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning)
            failures.Add("batch: mixed delete did not warn");
        if (wall.Vm.WallSelectedCount != 1 || !wall.Vm.Wall.First(card => card.Subscription.Id == 2).IsSelected)
            failures.Add("batch: failed delete item was not kept selected");
        if (!wall.Vm.WallBatchMode) failures.Add("batch: partial delete exited batch mode");

        // 确认拒绝 → 不发请求。
        using var denied = new WallHarness(HomeLayout.MoviePilotSeries, confirm: false);
        denied.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        await denied.Vm.ReloadAsync();
        denied.Vm.ToggleWallBatchMode();
        denied.Vm.ToggleWallSelectAll();
        await denied.Vm.WallBatchDeleteAsync();
        if (denied.Handler.Requests.Any(request => request.StartsWith("DELETE"))) failures.Add("batch: denied confirm still deleted");
    }

    private static async Task Maintenance(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        await wall.Vm.ReloadAsync();
        if (!wall.Vm.CanWallAdmin) failures.Add("maintenance: superuser not surfaced");

        wall.Handler.SearchData = new { target_count = 3, queued_count = 2, ongoing_count = 1 };
        await wall.Vm.WallSearchAllAsync();
        if (!wall.Handler.Requests.Any(request => request.StartsWith("POST subscribe/search?") && request.Contains("%E7%94%B5%E8%A7%86%E5%89%A7")))
            failures.Add("maintenance: search did not POST the TV type");
        if (wall.Vm.NoticeMessage != "已安排 2 个当前媒体类型订阅搜索，另有 1 个正在处理中")
            failures.Add("maintenance: search notice ladder wrong: " + wall.Vm.NoticeMessage);
        if (wall.Vm.WallSearchBusy) failures.Add("maintenance: search busy flag stuck");

        wall.Handler.SearchSuccess = false;
        await wall.Vm.WallSearchAllAsync();
        if (wall.Vm.NoticeSeverity != Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error)
            failures.Add("maintenance: search rejection did not report error");

        await wall.Vm.WallRefreshResourcesAsync();
        if (wall.Vm.NoticeMessage != "订阅刷新任务已启动！") failures.Add("maintenance: refresh notice wrong");
        if (!wall.Handler.Requests.Any(request => request.StartsWith("POST subscribe/refresh?"))) failures.Add("maintenance: refresh not sent");

        await wall.Vm.WallRefreshMetadataAsync();
        if (wall.Vm.NoticeMessage != "订阅元数据更新任务已启动！") failures.Add("maintenance: metadata notice wrong");
        if (!wall.Handler.Requests.Any(request => request.StartsWith("POST subscribe/check?"))) failures.Add("maintenance: metadata not sent");

        using var user = new WallHarness(HomeLayout.MoviePilotSeries, superUser: false);
        user.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        await user.Vm.ReloadAsync();
        if (user.Vm.CanWallAdmin) failures.Add("maintenance: non-admin surfaced admin entry");
    }

    private static async Task ExecutionBanner(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        wall.Handler.Batches = [Batch("b1", "manual", "running", total: 4, processed: 1, canCancel: true)];
        wall.Vm.SetWallActive(true);
        await wall.Vm.ReloadAsync();
        await wall.Vm.PollWallExecutionAsync();
        if (wall.Vm.WallVisibleBatch?.BatchId != "b1") failures.Add("banner: active manual batch not shown");
        if (!wall.Vm.WallBatchCanCancel || wall.Vm.WallBatchCanDismiss) failures.Add("banner: active batch cancel/dismiss flags wrong");
        if (wall.Vm.WallBatchProgressText != "1/4" || wall.Vm.WallBatchProgress != 25) failures.Add("banner: progress wrong");

        await wall.Vm.CancelWallBatchAsync();
        if (!wall.Handler.Requests.Any(request => request.StartsWith("PUT subscribe/execution/batches/b1/cancel")))
            failures.Add("banner: cancel did not PUT the batch");
        if (wall.Vm.NoticeMessage != "正在停止这次搜索") failures.Add("banner: cancel notice wrong");

        // 停止失败：按网页端原话提示，不抛服务器细节。
        wall.Handler.FailCancel = true;
        wall.Handler.Batches = [Batch("b1", "manual", "running", total: 4, processed: 1, canCancel: true)];
        await wall.Vm.PollWallExecutionAsync();
        await wall.Vm.CancelWallBatchAsync();
        if (wall.Vm.NoticeMessage != "暂时无法停止，请稍后重试") failures.Add("banner: cancel failure notice wrong: " + wall.Vm.NoticeMessage);
        wall.Handler.FailCancel = false;

        // 终态失败批次：可收起；收起后横幅消失。
        wall.Handler.Batches = [Batch("b2", "manual", "failed", updatedAt: DateTimeOffset.Now.ToString("O"))];
        await wall.Vm.PollWallExecutionAsync();
        if (wall.Vm.WallVisibleBatch?.BatchId != "b2" || !wall.Vm.WallBatchCanDismiss)
            failures.Add("banner: recent failed batch not dismissible");
        wall.Vm.DismissWallBatch();
        if (wall.Vm.WallVisibleBatch is not null) failures.Add("banner: dismiss did not hide the batch");

        // 后台来源不展示横幅。
        using var background = new WallHarness(HomeLayout.MoviePilotSeries);
        background.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        background.Handler.Batches = [Batch("b3", "new", "running")];
        background.Vm.SetWallActive(true);
        await background.Vm.ReloadAsync();
        await background.Vm.PollWallExecutionAsync();
        if (background.Vm.WallVisibleBatch is not null) failures.Add("banner: background source showed a banner");
    }

    private static async Task Polling(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        wall.Handler.Batches = [Batch("b1", "manual", "running", total: 4, processed: 1)];
        wall.Vm.SetWallActive(true);
        await wall.Vm.ReloadAsync();
        var listReadsAfterReload = wall.Handler.ListReads;
        await wall.Vm.PollWallExecutionAsync();
        var batchReads = wall.Handler.BatchReads;
        if (batchReads < 1) failures.Add("poll: first tick did not read batches");

        // 签名不变 → 不补刷完整列表。
        await wall.Vm.PollWallExecutionAsync();
        if (wall.Handler.ListReads != listReadsAfterReload) failures.Add("poll: unchanged signature refreshed the list");
        if (wall.Handler.BatchReads <= batchReads) failures.Add("poll: second tick did not read batches");

        // 签名变化 → 静默补刷完整列表。
        wall.Handler.Batches = [Batch("b1", "manual", "running", total: 4, processed: 2)];
        await wall.Vm.PollWallExecutionAsync();
        if (wall.Handler.ListReads != listReadsAfterReload + 1) failures.Add("poll: changed signature did not refresh the list");

        // 页面不活动 → 轮询不动。
        wall.Vm.SetWallActive(false);
        var reads = wall.Handler.BatchReads;
        await wall.Vm.PollWallExecutionAsync();
        if (wall.Handler.BatchReads != reads) failures.Add("poll: inactive page still polled");

        // 批次读取失败保留快照，不判列表失败。
        using var failing = new WallHarness(HomeLayout.MoviePilotSeries);
        failing.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        failing.Handler.Batches = [Batch("b1", "manual", "running")];
        failing.Vm.SetWallActive(true);
        await failing.Vm.ReloadAsync();
        await failing.Vm.PollWallExecutionAsync();
        failing.Handler.FailBatches = true;
        await failing.Vm.PollWallExecutionAsync();
        if (failing.Vm.WallVisibleBatch?.BatchId != "b1") failures.Add("poll: failed batch read dropped the snapshot");
        if (failing.Vm.WallListFailed) failures.Add("poll: batch failure marked the list failed");
    }

    private static void CardExecution(List<string> failures)
    {
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.FromHours(8));
        var searching = new MoviePilotSubscriptionCard(Parse(Sub(1, "Alpha", "电视剧", "R",
            execution: new { state = "searching", phase = "searching", source = "manual", updated_at = now.ToString("O") })));
        searching.RefreshExecution(now);
        if (searching.ExecutionText != "搜索中" || !searching.HasExecution) failures.Add("card: searching execution not shown");
        if (searching.WallStateText != "搜索中") failures.Add("card: wall state did not prefer execution");

        // 终态完成：窗口内显示，窗口外恢复卡片原始信息。
        var completed = new MoviePilotSubscriptionCard(Parse(Sub(2, "Beta", "电视剧", "R",
            execution: new { state = "completed", phase = "completed", source = "manual", submitted_count = 2, updated_at = now.ToString("O") })));
        completed.RefreshExecution(now);
        if (completed.ExecutionText != "已添加下载") failures.Add("card: completed-with-submits label wrong: " + completed.ExecutionText);
        completed.RefreshExecution(now.AddSeconds(6));
        if (completed.HasExecution) failures.Add("card: completed execution outlived its 5s window");

        // 后台来源不显示执行态。
        var background = new MoviePilotSubscriptionCard(Parse(Sub(3, "Gamma", "电视剧", "S",
            execution: new { state = "searching", phase = "searching", source = "new", updated_at = now.ToString("O") })));
        background.RefreshExecution(now);
        if (background.HasExecution) failures.Add("card: background source showed execution");
        if (background.WallStateText != "已暂停") failures.Add("card: paused wall state wrong without execution");
    }

    private static async Task ListFailure(List<string> failures)
    {
        using var wall = new WallHarness(HomeLayout.MoviePilotSeries);
        wall.Handler.Subscriptions = [Sub(1, "Alpha", "电视剧", "R")];
        await wall.Vm.ReloadAsync();
        wall.Handler.FailList = true;
        await wall.Vm.RefreshWallListAsync(false);
        if (!wall.Vm.WallListFailed) failures.Add("listfail: explicit refresh failure did not raise the error flag");
        if (wall.Vm.WallListErrorVisibility != Microsoft.UI.Xaml.Visibility.Visible) failures.Add("listfail: error bar not visible");
        if (wall.Vm.Wall.Count != 1) failures.Add("listfail: failure dropped the previous snapshot");

        wall.Handler.FailList = false;
        await wall.Vm.RefreshWallListAsync(false);
        if (wall.Vm.WallListFailed || wall.Vm.WallListErrorVisibility != Microsoft.UI.Xaml.Visibility.Collapsed)
            failures.Add("listfail: recovery did not clear the error flag");
    }

    private static MoviePilotSubscription Parse(object subscription)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(subscription));
        return MoviePilotSubscription.Parse(document.RootElement)!;
    }

    private static object Sub(int id, string name, string type, string state, int total = 0, int lack = 0, int completed = 0,
        string date = "", string lastUpdate = "", object? execution = null, int bestVersion = 0) => new
    {
        id, name, type, state,
        total_episode = total, lack_episode = lack, completed_episode = completed,
        date, last_update = lastUpdate, execution_status = execution, best_version = bestVersion,
        media_source = "themoviedb", media_id = id.ToString(), season = type == "电视剧" ? 1 : (int?)null,
        username = "tester"
    };

    private static object Batch(string id, string source, string state, string phase = "", int total = 0, int processed = 0,
        bool canCancel = false, string updatedAt = "") => new
    {
        batch_id = id, source, state, phase,
        total_count = total, processed_count = processed, can_cancel = canCancel, updated_at = updatedAt,
        download_counts = new { submitted = 0 }, downloads = Array.Empty<object>()
    };
}

/// <summary>一套真实服务＋真实墙 VM，只把 HTTP 换成假处理器；Dispose 时收掉寿命与连接。</summary>
internal sealed class WallHarness : IDisposable
{
    private readonly MoviePilotClient _client;
    public WallHandler Handler { get; }
    public MoviePilotService Service { get; }
    public MoviePilotSubscriptionsViewModel Vm { get; }
    public AppSettings Settings { get; }

    public WallHarness(string category, bool confirm = true, bool superUser = true)
    {
        Handler = new WallHandler { SuperUser = superUser };
        _client = new MoviePilotClient(Handler);
        Settings = new AppSettings();
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "http://wall.invalid:3001";
        Settings.MoviePilot.Username = "tester";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(Settings.MoviePilot, "placeholder");
        Service = new MoviePilotService(_client, credentials, Settings);
        Vm = new MoviePilotSubscriptionsViewModel();
        Vm.UsePageCategory(category);
        Vm.Attach(Service);
        Vm.UseConfirm((_, _, _) => Task.FromResult(confirm));
    }

    public void Dispose()
    {
        Vm.Cancel();
        Vm.Dispose();
        Service.Dispose();
        _client.Dispose();
        Handler.Dispose();
    }
}

/// <summary>订阅墙用到的全部路由；可配置列表／批次／顺序与各类失败，并记录请求与正文。</summary>
internal sealed class WallHandler : HttpMessageHandler
{
    public object[] Subscriptions = [];
    public object[] Batches = [];
    public object? Order;
    public bool SuperUser = true;
    public bool FailList, FailBatches, FailOrderLoad, FailOrderSave, FailCancel, SearchSuccess = true;
    public object? SearchData;
    public HashSet<int> DeleteFailIds = [];
    public List<string> Requests = [];
    public List<string> Bodies = [];
    public int ListReads, BatchReads, OrderSaves;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI");
        if (uri.Host != "wall.invalid") throw new InvalidOperationException("Only the wall fixture URI is allowed");
        var path = uri.AbsolutePath.Replace("/api/v1/", "", StringComparison.Ordinal);
        var method = request.Method;
        Requests.Add(method + " " + path + uri.Query);
        if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

        if (path == "login/access-token") return Reply(new { access_token = "placeholder-token", super_user = SuperUser, user_name = "tester" });
        if (path == "subscribe/" && method == HttpMethod.Get) { ListReads++; return FailList ? Error() : Reply(Subscriptions); }
        if (path == "subscribe/execution/batches" && method == HttpMethod.Get) { BatchReads++; return FailBatches ? Error() : Reply(Batches); }
        if (path.StartsWith("subscribe/execution/batches/", StringComparison.Ordinal) && path.EndsWith("/cancel", StringComparison.Ordinal) && method == HttpMethod.Put)
            return FailCancel ? Reply(new { success = false, message = "已经结束，暂时无法停止" }) : Reply(new { success = true });
        if (path.StartsWith("user/config/", StringComparison.Ordinal) && method == HttpMethod.Get) return FailOrderLoad ? Error() : Reply(new { value = Order });
        if (path.StartsWith("user/config/", StringComparison.Ordinal) && method == HttpMethod.Post)
        { OrderSaves++; return FailOrderSave ? Reply(new { success = false, message = "超出大小上限" }) : Reply(new { success = true }); }
        if (path.StartsWith("subscribe/status/", StringComparison.Ordinal) && method == HttpMethod.Put) return Reply(new { success = true });
        if (path.StartsWith("subscribe/", StringComparison.Ordinal) && method == HttpMethod.Delete)
        {
            var id = int.Parse(path["subscribe/".Length..]);
            return Reply(new { success = true, data = new { status = DeleteFailIds.Contains(id) ? "pending" : "deleted" } });
        }
        if (path == "subscribe/search" && method == HttpMethod.Post)
            return SearchSuccess ? Reply(new { success = true, data = SearchData }) : Reply(new { success = false, message = "没有需要搜索的订阅" });
        if (path == "subscribe/refresh" && method == HttpMethod.Post) return Reply(new { success = true });
        if (path == "subscribe/check" && method == HttpMethod.Post) return Reply(new { success = true });
        throw new InvalidOperationException("Unexpected wall route: " + method + " " + path + uri.Query);
    }

    private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Error() => new(HttpStatusCode.NotFound)
    { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
}

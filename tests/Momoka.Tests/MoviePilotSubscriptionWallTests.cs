using System.Text.Json;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 订阅墙与执行状态的纯函数测试，对照网页端 subscriptionExecution.ts / SubscribeListView / SubscribeCard
/// 的可见性与排序规则逐条钉住。全部离线：不读网络，时间由调用方给定。
/// </summary>
internal static class MoviePilotSubscriptionWallTests
{
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:00:00+08:00");

    private static MoviePilotSubscriptionExecution Execution(string state, string phase = "", string source = "",
        string nextRunAt = "", int submitted = 0, string updatedAt = "") => new()
    {
        State = state, Phase = phase, Source = source, NextRunAt = nextRunAt,
        SubmittedCount = submitted, UpdatedAt = updatedAt
    };

    private static MoviePilotSubscriptionBatch Batch(string id, string state, string phase = "", string source = "manual",
        string updatedAt = "", int total = 10, int processed = 0, bool canCancel = false) => new()
    {
        BatchId = id, State = state, Phase = phase, Source = source, UpdatedAt = updatedAt,
        TotalCount = total, ProcessedCount = processed, CanCancel = canCancel
    };

    private static MoviePilotSubscription Subscription(int id, string type = "电视剧", string state = "R",
        int? total = null, int? lack = null, int? completed = null, string name = "示例", string date = "", string lastUpdate = "",
        int bestVersion = 0) => MoviePilotSubscription.Parse(Json($$"""
        {"id":{{id}},"name":"{{name}}","type":"{{type}}","state":"{{state}}","date":"{{date}}","last_update":"{{lastUpdate}}",
         "total_episode":{{total?.ToString() ?? "null"}},"lack_episode":{{lack?.ToString() ?? "null"}},
         "completed_episode":{{completed?.ToString() ?? "null"}},"best_version":{{bestVersion}}}
        """))!;

    internal static void Register()
    {
        StageAndLabel();
        CardVisibility();
        BatchVisibility();
        SignatureAndProgress();
        SearchSubmission();
        WallClassifyAndFilter();
        WallSort();
        WallOptionsAndLabels();
    }

    private static void StageAndLabel()
    {
        Test("订阅执行状态：终态以 state 为准，残留阶段不把已结束任务显示成进行中", () =>
        {
            Assert.Equal("completed", MoviePilotExecutionText.Stage(Execution("completed", "searching")));
            Assert.Equal("searching", MoviePilotExecutionText.Stage(Execution("running", "searching")));
            Assert.Equal("running", MoviePilotExecutionText.Stage(Execution("running")));
            Assert.Equal(MoviePilotExecutionLabel.Submitted, MoviePilotExecutionText.Label(Execution("completed", "searching", submitted: 2), Now));
            Assert.True(MoviePilotExecutionText.IsTerminal("skipped"));
            Assert.False(MoviePilotExecutionText.IsActive(Execution("completed", "searching")));
            Assert.True(MoviePilotExecutionText.IsActive(Execution("running", "waiting_site_budget")));
        });
        Test("订阅执行状态：九种展示词归并与跳过不展示", () =>
        {
            Assert.Null(MoviePilotExecutionText.Label(Execution("skipped"), Now));
            Assert.Equal(MoviePilotExecutionLabel.NoResult, MoviePilotExecutionText.Label(Execution("completed"), Now));
            Assert.Equal(MoviePilotExecutionLabel.Submitted, MoviePilotExecutionText.Label(Execution("completed", submitted: 1), Now));
            foreach (var state in new[] { "queued", "scheduled", "waiting_subscription" })
                Assert.Equal(MoviePilotExecutionLabel.Queued, MoviePilotExecutionText.Label(Execution(state), Now));
            foreach (var state in new[] { "running", "matching", "searching", "waiting_site_budget" })
                Assert.Equal(MoviePilotExecutionLabel.Searching, MoviePilotExecutionText.Label(Execution(state), Now));
            foreach (var state in new[] { "preparing", "submitting" })
                Assert.Equal(MoviePilotExecutionLabel.Submitting, MoviePilotExecutionText.Label(Execution(state), Now));
            Assert.Equal(MoviePilotExecutionLabel.Failed, MoviePilotExecutionText.Label(Execution("failed"), Now));
            Assert.Equal(MoviePilotExecutionLabel.Cancelling, MoviePilotExecutionText.Label(Execution("cancelling"), Now));
            Assert.Equal(MoviePilotExecutionLabel.Cancelled, MoviePilotExecutionText.Label(Execution("cancelled"), Now));
            Assert.Equal(MoviePilotExecutionLabel.Searching, MoviePilotExecutionText.Label(Execution("未来新阶段"), Now));
        });
        Test("订阅执行状态：站点等待超过两分钟才给继续时间，更短等待显示搜索中", () =>
        {
            var soon = Execution("running", "waiting_site_budget", nextRunAt: Now.AddSeconds(119).ToString("O"));
            var later = Execution("running", "waiting_site_budget", nextRunAt: Now.AddSeconds(121).ToString("O"));
            Assert.Null(MoviePilotExecutionText.ResumeAt(soon, Now));
            Assert.Equal(MoviePilotExecutionLabel.Searching, MoviePilotExecutionText.Label(soon, Now));
            Assert.NotNull(MoviePilotExecutionText.ResumeAt(later, Now));
            Assert.Equal(MoviePilotExecutionLabel.Waiting, MoviePilotExecutionText.Label(later, Now));
            Assert.Equal(Now.AddSeconds(121).ToLocalTime().ToString("HH:mm") + " 再搜", MoviePilotExecutionText.LabelText(later, Now));
            Assert.Equal("搜索中", MoviePilotExecutionText.LabelText(soon, Now));
            Assert.Equal("", MoviePilotExecutionText.LabelText(Execution("skipped"), Now));
            Assert.Null(MoviePilotExecutionText.ResumeAt(Execution("running", "waiting_site_budget", nextRunAt: "不是时间"), Now));
            Assert.Null(MoviePilotExecutionText.ResumeAt(Execution("running", "searching", nextRunAt: Now.AddHours(1).ToString("O")), Now));
        });
        Test("订阅执行状态：批次完成按 download_counts.submitted 区分已添加与暂无资源", () =>
        {
            var batch = MoviePilotSubscriptionBatch.ParseList(Json("""
                [{"batch_id":"b1","source":"manual","state":"completed","phase":"submitting","download_counts":{"submitted":3}},
                 {"batch_id":"b2","source":"manual","state":"completed","download_counts":{}}]
                """));
            Assert.Equal(MoviePilotExecutionLabel.Submitted, MoviePilotExecutionText.Label(batch[0], Now));
            Assert.Equal(MoviePilotExecutionLabel.NoResult, MoviePilotExecutionText.Label(batch[1], Now));
            Assert.Equal("", batch[0].NextRunAt);
        });
        Test("订阅执行状态：execution_status 缺失或形状不对时没有执行态", () =>
        {
            Assert.Null(MoviePilotSubscriptionExecution.Parse(Json("null")));
            Assert.Null(MoviePilotSubscriptionExecution.Parse(default));
            var parsed = MoviePilotSubscriptionExecution.Parse(Json("""
                {"state":"running","phase":"searching","source":"manual","batch_id":"b1","can_cancel":true,"submitted_count":2,
                 "next_run_at":"2026-11-11T12:05:00","error":"站点超时","updated_at":"2026-11-11T11:59:00"}
                """))!;
            Assert.Equal("running", parsed.State); Assert.Equal("searching", parsed.Phase);
            Assert.Equal("manual", parsed.Source); Assert.Equal("b1", parsed.BatchId);
            Assert.True(parsed.CanCancel); Assert.Equal(2, parsed.SubmittedCount);
            Assert.Equal("站点超时", parsed.Error);
            var sub = Subscription(1);
            Assert.Null(sub.Execution);
        });
    }

    private static void CardVisibility()
    {
        Test("订阅卡片执行态：后台来源与跳过不展示，空来源不算后台", () =>
        {
            Assert.Null(MoviePilotExecutionText.VisibleCardExecution(null, Now));
            Assert.Null(MoviePilotExecutionText.VisibleCardExecution(Execution("running", "searching", source: "new"), Now));
            Assert.Null(MoviePilotExecutionText.VisibleCardExecution(Execution("running", "searching", source: "fallback"), Now));
            Assert.Null(MoviePilotExecutionText.VisibleCardExecution(Execution("skipped"), Now));
            Assert.NotNull(MoviePilotExecutionText.VisibleCardExecution(Execution("running", "searching"), Now));
            Assert.NotNull(MoviePilotExecutionText.VisibleCardExecution(Execution("running", "searching", source: "manual"), Now));
        });
        Test("订阅卡片执行态：完成 5 秒、取消 10 秒、失败 60 秒后恢复原始信息", () =>
        {
            foreach (var (state, window) in new[] { ("completed", 5), ("cancelled", 10), ("failed", 60) })
            {
                var fresh = Execution(state, updatedAt: Now.AddSeconds(-window + 1).ToString("O"));
                var stale = Execution(state, updatedAt: Now.AddSeconds(-window - 1).ToString("O"));
                Assert.NotNull(MoviePilotExecutionText.VisibleCardExecution(fresh, Now));
                Assert.Null(MoviePilotExecutionText.VisibleCardExecution(stale, Now));
            }
            // 终态先看 state 再看 phase：state 非终态时残留的终态 phase 也按窗口收起。
            var lingering = Execution("running", "failed", updatedAt: Now.AddSeconds(-61).ToString("O"));
            Assert.Null(MoviePilotExecutionText.VisibleCardExecution(lingering, Now));
            // 更新时间解析不出来按刚发生处理，宁可多显示一会儿。
            Assert.NotNull(MoviePilotExecutionText.VisibleCardExecution(Execution("failed", updatedAt: "不是时间"), Now));
            Assert.NotNull(MoviePilotExecutionText.VisibleCardExecution(Execution("failed"), Now));
            // 活动状态没有窗口，一直显示。
            Assert.NotNull(MoviePilotExecutionText.VisibleCardExecution(Execution("running", "searching", updatedAt: Now.AddDays(-1).ToString("O")), Now));
        });
        Test("订阅卡片执行态：活动卡片判定只看活动词，后台来源不参与", () =>
        {
            Assert.False(MoviePilotExecutionText.HasActiveCardExecution([]));
            Assert.False(MoviePilotExecutionText.HasActiveCardExecution([Subscription(1)]));
            var active = MoviePilotSubscription.Parse(Json("""
                {"id":1,"name":"剧","type":"电视剧","execution_status":{"state":"running","phase":"searching","source":"manual"}}
                """))!;
            var background = MoviePilotSubscription.Parse(Json("""
                {"id":2,"name":"剧","type":"电视剧","execution_status":{"state":"running","phase":"searching","source":"new"}}
                """))!;
            var emptySource = MoviePilotSubscription.Parse(Json("""
                {"id":3,"name":"剧","type":"电视剧","execution_status":{"state":"queued"}}
                """))!;
            Assert.True(MoviePilotExecutionText.HasActiveCardExecution([active]));
            Assert.False(MoviePilotExecutionText.HasActiveCardExecution([background]));
            Assert.True(MoviePilotExecutionText.HasActiveCardExecution([emptySource]));
            // 与网页端一致：终态 state 配活动 phase 仍算「要刷新」，展示层另按终态收起。
            var lingering = MoviePilotSubscription.Parse(Json("""
                {"id":4,"name":"剧","type":"电视剧","execution_status":{"state":"completed","phase":"searching"}}
                """))!;
            Assert.True(MoviePilotExecutionText.HasActiveCardExecution([lingering]));
        });
    }

    private static void BatchVisibility()
    {
        Test("订阅批次横幅：只展示用户主动发起的搜索，进行中优先于最近结束", () =>
        {
            var dismissed = new HashSet<string>();
            var background = Batch("auto", "running", "searching", source: "new");
            var active = Batch("run", "running", "searching");
            var failed = Batch("bad", "failed", updatedAt: Now.AddSeconds(-30).ToString("O"));
            Assert.Null(MoviePilotExecutionText.SelectVisibleBatch([background], dismissed, Now));
            Assert.Equal("run", MoviePilotExecutionText.SelectVisibleBatch([failed, active, background], dismissed, Now)!.BatchId);
            Assert.Equal("bad", MoviePilotExecutionText.SelectVisibleBatch([background, failed], dismissed, Now)!.BatchId);
            Assert.Null(MoviePilotExecutionText.SelectVisibleBatch([], dismissed, Now));
        });
        Test("订阅批次横幅：失败留 60 秒、取消留 10 秒，完成与跳过不展示，可手动收起", () =>
        {
            var dismissed = new HashSet<string>();
            Assert.NotNull(MoviePilotExecutionText.SelectVisibleBatch([Batch("f", "failed", updatedAt: Now.AddSeconds(-59).ToString("O"))], dismissed, Now));
            Assert.Null(MoviePilotExecutionText.SelectVisibleBatch([Batch("f", "failed", updatedAt: Now.AddSeconds(-61).ToString("O"))], dismissed, Now));
            Assert.NotNull(MoviePilotExecutionText.SelectVisibleBatch([Batch("c", "cancelled", updatedAt: Now.AddSeconds(-9).ToString("O"))], dismissed, Now));
            Assert.Null(MoviePilotExecutionText.SelectVisibleBatch([Batch("c", "cancelled", updatedAt: Now.AddSeconds(-11).ToString("O"))], dismissed, Now));
            Assert.Null(MoviePilotExecutionText.SelectVisibleBatch([Batch("d", "completed", updatedAt: Now.ToString("O"))], dismissed, Now));
            Assert.Null(MoviePilotExecutionText.SelectVisibleBatch([Batch("s", "skipped", updatedAt: Now.ToString("O"))], dismissed, Now));
            // 更新时间解析不出来时保持可见。
            Assert.NotNull(MoviePilotExecutionText.SelectVisibleBatch([Batch("f", "failed", updatedAt: "不是时间")], dismissed, Now));
            dismissed.Add("f");
            Assert.Null(MoviePilotExecutionText.SelectVisibleBatch([Batch("f", "failed", updatedAt: Now.ToString("O"))], dismissed, Now));
            // 进行中的批次不受收起名单影响（收起只针对终态）。
            Assert.NotNull(MoviePilotExecutionText.SelectVisibleBatch([Batch("f", "running", "searching")], dismissed, Now));
        });
        Test("订阅批次横幅：终态不被残留阶段重新激活", () =>
        {
            Assert.False(MoviePilotExecutionText.IsActive(Batch("b", "completed", "searching")));
            Assert.False(MoviePilotExecutionText.IsActive(Batch("b", "cancelled", "submitting")));
            Assert.True(MoviePilotExecutionText.IsActive(Batch("b", "running", "waiting_subscription")));
            Assert.True(MoviePilotExecutionText.IsActive(Batch("b", "queued", "")));
            // state 不在活动词表但 phase 在，也算进行中。
            Assert.True(MoviePilotExecutionText.IsActive(Batch("b", "未来状态", "searching")));
        });
        Test("订阅批次解析：非数组报错，缺标识跳过，重复标识去重", () =>
        {
            Assert.Throws<MoviePilotException>(() => MoviePilotSubscriptionBatch.ParseList(Json("{}")));
            var batches = MoviePilotSubscriptionBatch.ParseList(Json("""
                [{"batch_id":"b1","source":"manual","state":"running","phase":"searching","total_count":10,"processed_count":3,
                  "finished_count":2,"failed_count":1,"cancelled_count":0,"skipped_count":0,"created_at":"2026-11-11T11:00:00",
                  "updated_at":"2026-11-11T11:59:00","current_subscription_id":12,"current_site_id":8,"error":null,
                  "can_cancel":true,"download_counts":{"submitted":2},"downloads":[{"title":"资源"}],"downloads_omitted":1},
                 {"batch_id":"b1","source":"manual","state":"running"},
                 {"source":"manual","state":"running"}]
                """));
            Assert.Equal(1, batches.Count);
            var batch = batches[0];
            Assert.Equal(10, batch.TotalCount); Assert.Equal(3, batch.ProcessedCount);
            Assert.Equal(2, batch.FinishedCount); Assert.Equal(1, batch.FailedCount);
            Assert.Equal(12, batch.CurrentSubscriptionId); Assert.Equal(8, batch.CurrentSiteId);
            Assert.True(batch.CanCancel); Assert.Equal(2, batch.SubmittedCount); Assert.Equal(1, batch.DownloadsOmitted);
            Assert.Equal("", batch.Error);
        });
    }

    private static void SignatureAndProgress()
    {
        Test("订阅批次签名：进度字段变化才算变化，明细内容参与比对", () =>
        {
            var first = MoviePilotSubscriptionBatch.ParseList(Json("""
                [{"batch_id":"b1","source":"manual","state":"running","phase":"searching","total_count":10,"processed_count":3,
                  "download_counts":{"submitted":2},"downloads":[{"title":"资源"}],"downloads_omitted":0}]
                """));
            var same = MoviePilotSubscriptionBatch.ParseList(Json("""
                [{"batch_id":"b1","source":"manual","state":"running","phase":"searching","total_count":10,"processed_count":3,
                  "download_counts":{"submitted":2},"downloads":[{"title":"资源"}],"downloads_omitted":0}]
                """));
            var progressed = MoviePilotSubscriptionBatch.ParseList(Json("""
                [{"batch_id":"b1","source":"manual","state":"running","phase":"searching","total_count":10,"processed_count":4,
                  "download_counts":{"submitted":2},"downloads":[{"title":"资源"}],"downloads_omitted":0}]
                """));
            var newDownload = MoviePilotSubscriptionBatch.ParseList(Json("""
                [{"batch_id":"b1","source":"manual","state":"running","phase":"searching","total_count":10,"processed_count":3,
                  "download_counts":{"submitted":3},"downloads":[{"title":"资源"},{"title":"另一条"}],"downloads_omitted":0}]
                """));
            Assert.Equal(MoviePilotExecutionText.Signature(first), MoviePilotExecutionText.Signature(same));
            Assert.True(MoviePilotExecutionText.Signature(first) != MoviePilotExecutionText.Signature(progressed));
            Assert.True(MoviePilotExecutionText.Signature(first) != MoviePilotExecutionText.Signature(newDownload));
            Assert.Equal(MoviePilotExecutionText.Signature([]), MoviePilotExecutionText.Signature([]));
        });
        Test("订阅批次进度：百分比四舍五入封顶一百，总数未知用不定进度", () =>
        {
            Assert.Equal(0, MoviePilotExecutionText.BatchProgressPercent(Batch("b", "running", total: 0)));
            Assert.Equal(33, MoviePilotExecutionText.BatchProgressPercent(Batch("b", "running", total: 3, processed: 1)));
            Assert.Equal(100, MoviePilotExecutionText.BatchProgressPercent(Batch("b", "running", total: 3, processed: 5)));
            Assert.True(MoviePilotExecutionText.BatchProgressIndeterminate(Batch("b", "running", total: 0)));
            Assert.False(MoviePilotExecutionText.BatchProgressIndeterminate(Batch("b", "running", total: 1)));
            Assert.False(MoviePilotExecutionText.BatchProgressIndeterminate(Batch("b", "completed", total: 0)));
        });
        Test("订阅批次色调：等待订阅为警告，活动为信息，失败为错误，取消为次要", () =>
        {
            Assert.Equal(MoviePilotExecutionTone.Warning, MoviePilotExecutionText.BatchTone(Batch("b", "running", "waiting_subscription")));
            Assert.Equal(MoviePilotExecutionTone.Info, MoviePilotExecutionText.BatchTone(Batch("b", "running", "searching")));
            Assert.Equal(MoviePilotExecutionTone.Error, MoviePilotExecutionText.BatchTone(Batch("b", "failed")));
            Assert.Equal(MoviePilotExecutionTone.Secondary, MoviePilotExecutionText.BatchTone(Batch("b", "cancelled")));
            Assert.Equal(MoviePilotExecutionTone.Info, MoviePilotExecutionText.Tone(MoviePilotExecutionLabel.Queued));
            Assert.Equal(MoviePilotExecutionTone.Primary, MoviePilotExecutionText.Tone(MoviePilotExecutionLabel.Searching));
            Assert.Equal(MoviePilotExecutionTone.Success, MoviePilotExecutionText.Tone(MoviePilotExecutionLabel.Submitted));
            Assert.Equal(MoviePilotExecutionTone.Secondary, MoviePilotExecutionText.Tone(MoviePilotExecutionLabel.NoResult));
            Assert.Equal(MoviePilotExecutionTone.Error, MoviePilotExecutionText.Tone(MoviePilotExecutionLabel.Failed));
            Assert.Equal(MoviePilotExecutionTone.Warning, MoviePilotExecutionText.Tone(MoviePilotExecutionLabel.Cancelling));
        });
    }

    private static void SearchSubmission()
    {
        Test("订阅整类搜索回执：五种提示分支与网页端一致", () =>
        {
            Assert.Equal("当前媒体类型订阅搜索已安排！", MoviePilotSubscriptionSearchText.SearchAllNotice(null).Message);
            Assert.True(MoviePilotSubscriptionSearchText.SearchAllNotice(null).IsSuccess);
            var empty = MoviePilotSubscriptionSearchText.SearchAllNotice(new(0, 0, 0));
            Assert.Equal("当前媒体类型没有需要搜索的订阅", empty.Message); Assert.False(empty.IsSuccess);
            var running = MoviePilotSubscriptionSearchText.SearchAllNotice(new(5, 0, 5));
            Assert.Equal("5 个当前媒体类型订阅已经在搜索中，请稍候", running.Message); Assert.False(running.IsSuccess);
            var mixed = MoviePilotSubscriptionSearchText.SearchAllNotice(new(5, 3, 2));
            Assert.Equal("已安排 3 个当前媒体类型订阅搜索，另有 2 个正在处理中", mixed.Message); Assert.True(mixed.IsSuccess);
            var scheduled = MoviePilotSubscriptionSearchText.SearchAllNotice(new(3, 3, 0));
            Assert.Equal("已安排 3 个当前媒体类型订阅，系统会自动依次搜索", scheduled.Message); Assert.True(scheduled.IsSuccess);
        });
        Test("订阅整类搜索回执：旧后端成功但不带数据读作 null", () =>
        {
            Assert.Null(MoviePilotSubscriptionSearchSubmission.Parse(Json("null")));
            Assert.Null(MoviePilotSubscriptionSearchSubmission.Parse(default));
            var parsed = MoviePilotSubscriptionSearchSubmission.Parse(Json("""{"target_count":5,"queued_count":3,"ongoing_count":2}"""))!;
            Assert.Equal(5, parsed.TargetCount); Assert.Equal(3, parsed.QueuedCount); Assert.Equal(2, parsed.OngoingCount);
            var bare = MoviePilotSubscriptionSearchSubmission.Parse(Json("{}"))!;
            Assert.Equal(0, bare.TargetCount); Assert.Equal(0, bare.QueuedCount); Assert.Equal(0, bare.OngoingCount);
        });
    }

    private static void WallClassifyAndFilter()
    {
        Test("订阅墙状态：待定暂停优先，电影恒为全部，电视剧按集数三分", () =>
        {
            Assert.Equal(MoviePilotSubscriptionWallStatus.Pending, MoviePilotSubscriptionWall.Classify(Subscription(1, state: "P")));
            Assert.Equal(MoviePilotSubscriptionWallStatus.Paused, MoviePilotSubscriptionWall.Classify(Subscription(2, state: "S")));
            Assert.Equal(MoviePilotSubscriptionWallStatus.Pending, MoviePilotSubscriptionWall.Classify(Subscription(3, "电影", "P")));
            Assert.Equal(MoviePilotSubscriptionWallStatus.All, MoviePilotSubscriptionWall.Classify(Subscription(4, "电影", "R", total: 1, lack: 1)));
            Assert.Equal(MoviePilotSubscriptionWallStatus.Completed, MoviePilotSubscriptionWall.Classify(Subscription(5, total: 12, lack: 0, completed: 12)));
            Assert.Equal(MoviePilotSubscriptionWallStatus.Subscribing, MoviePilotSubscriptionWall.Classify(Subscription(6, total: 12, lack: 3, completed: 9)));
            Assert.Equal(MoviePilotSubscriptionWallStatus.NotStarted, MoviePilotSubscriptionWall.Classify(Subscription(7, total: 12, lack: 12, completed: 0)));
            Assert.Equal(MoviePilotSubscriptionWallStatus.NotStarted, MoviePilotSubscriptionWall.Classify(Subscription(8, total: 12, lack: 12)));
            Assert.Equal(MoviePilotSubscriptionWallStatus.NotStarted, MoviePilotSubscriptionWall.Classify(Subscription(9)));
            // 洗版不覆盖暂停与待定。
            Assert.Equal(MoviePilotSubscriptionWallStatus.Paused, MoviePilotSubscriptionWall.Classify(Subscription(10, state: "S", total: 12, lack: 0, bestVersion: 1)));
        });
        Test("订阅墙筛选：关键词小写包含，洗版与进度状态正交", () =>
        {
            var pausedWash = Subscription(1, state: "S", total: 12, lack: 3, completed: 9, name: "示例剧 Alpha", bestVersion: 1);
            Assert.True(MoviePilotSubscriptionWall.Matches(pausedWash, "alpha", MoviePilotSubscriptionWallFilter.All));
            Assert.True(MoviePilotSubscriptionWall.Matches(pausedWash, "  ALPHA  ", MoviePilotSubscriptionWallFilter.All));
            Assert.False(MoviePilotSubscriptionWall.Matches(pausedWash, "beta", MoviePilotSubscriptionWallFilter.All));
            Assert.True(MoviePilotSubscriptionWall.Matches(pausedWash, "", MoviePilotSubscriptionWallFilter.Paused));
            Assert.True(MoviePilotSubscriptionWall.Matches(pausedWash, "", MoviePilotSubscriptionWallFilter.BestVersion));
            Assert.False(MoviePilotSubscriptionWall.Matches(pausedWash, "", MoviePilotSubscriptionWallFilter.Subscribing));
            Assert.False(MoviePilotSubscriptionWall.Matches(Subscription(2, name: "另一部"), "", MoviePilotSubscriptionWallFilter.BestVersion));
            // 电影只有待定/暂停/洗版可筛，进度三档永远不命中。
            var movie = Subscription(3, "电影", name: "电影片");
            Assert.True(MoviePilotSubscriptionWall.Matches(movie, "", MoviePilotSubscriptionWallFilter.All));
            Assert.False(MoviePilotSubscriptionWall.Matches(movie, "", MoviePilotSubscriptionWallFilter.NotStarted));
            Assert.False(MoviePilotSubscriptionWall.Matches(movie, "", MoviePilotSubscriptionWallFilter.Subscribing));
            Assert.False(MoviePilotSubscriptionWall.Matches(movie, "", MoviePilotSubscriptionWallFilter.Completed));
            Assert.True(MoviePilotSubscriptionWall.Matches(Subscription(4, "电影", "P"), "", MoviePilotSubscriptionWallFilter.Pending));
        });
    }

    private static void WallSort()
    {
        Test("订阅墙排序偏好：键值往返、电影归一缺失集数、默认跟随自定义顺序", () =>
        {
            foreach (var sort in new[] { MoviePilotSubscriptionSort.Custom, MoviePilotSubscriptionSort.LastUpdate,
                MoviePilotSubscriptionSort.AddTime, MoviePilotSubscriptionSort.LackEpisode })
                Assert.Equal(sort, MoviePilotSubscriptionWall.ParseSort(MoviePilotSubscriptionWall.SortKey(sort)));
            Assert.Null(MoviePilotSubscriptionWall.ParseSort("未知"));
            Assert.Null(MoviePilotSubscriptionWall.ParseSort(null));
            Assert.Equal(MoviePilotSubscriptionSort.AddTime, MoviePilotSubscriptionWall.NormalizeSort(MoviePilotSubscriptionSort.LackEpisode, false));
            Assert.Equal(MoviePilotSubscriptionSort.LackEpisode, MoviePilotSubscriptionWall.NormalizeSort(MoviePilotSubscriptionSort.LackEpisode, true));
            Assert.Equal(MoviePilotSubscriptionSort.Custom, MoviePilotSubscriptionWall.DefaultSort(true));
            Assert.Equal(MoviePilotSubscriptionSort.AddTime, MoviePilotSubscriptionWall.DefaultSort(false));
            Assert.Equal("SubscribeMovieOrder", MoviePilotSubscriptionWall.OrderConfigKey(false));
            Assert.Equal("SubscribeTvOrder", MoviePilotSubscriptionWall.OrderConfigKey(true));
        });
        Test("订阅墙拖拽排序：只在自定义加无筛选加非批量时可用", () =>
        {
            Assert.True(MoviePilotSubscriptionWall.CanDragSort(MoviePilotSubscriptionSort.Custom, "", MoviePilotSubscriptionWallFilter.All, false));
            Assert.False(MoviePilotSubscriptionWall.CanDragSort(MoviePilotSubscriptionSort.AddTime, "", MoviePilotSubscriptionWallFilter.All, false));
            Assert.False(MoviePilotSubscriptionWall.CanDragSort(MoviePilotSubscriptionSort.Custom, "剧", MoviePilotSubscriptionWallFilter.All, false));
            Assert.True(MoviePilotSubscriptionWall.CanDragSort(MoviePilotSubscriptionSort.Custom, "  ", MoviePilotSubscriptionWallFilter.All, false));
            Assert.False(MoviePilotSubscriptionWall.CanDragSort(MoviePilotSubscriptionSort.Custom, "", MoviePilotSubscriptionWallFilter.Paused, false));
            Assert.False(MoviePilotSubscriptionWall.CanDragSort(MoviePilotSubscriptionSort.Custom, "", MoviePilotSubscriptionWallFilter.All, true));
        });
        Test("订阅墙时间值：ISO 与斜杠兼容格式都收，解析不出记零", () =>
        {
            var iso = MoviePilotSubscriptionWall.TimeValue("2026-11-11T12:00:00+08:00");
            Assert.Equal(DateTimeOffset.Parse("2026-11-11T12:00:00+08:00").ToUnixTimeMilliseconds(), iso);
            Assert.True(MoviePilotSubscriptionWall.TimeValue("2026-11-11 12:00:00") > 0);
            Assert.Equal(0, MoviePilotSubscriptionWall.TimeValue("不是时间"));
            Assert.Equal(0, MoviePilotSubscriptionWall.TimeValue(""));
            Assert.Equal(0, MoviePilotSubscriptionWall.TimeValue(null));
        });
        Test("订阅墙排序：自定义顺序未登记的按添加时间倒序补齐", () =>
        {
            var items = new[]
            {
                Subscription(1, date: "2026-11-01T10:00:00+08:00"),
                Subscription(2, date: "2026-11-03T10:00:00+08:00"),
                Subscription(3, date: "2026-11-02T10:00:00+08:00"),
                Subscription(4, date: "2026-11-04T10:00:00+08:00")
            };
            var sorted = MoviePilotSubscriptionWall.Sort(items, MoviePilotSubscriptionSort.Custom, [3, 1]);
            Assert.True(new[] { 3, 1, 4, 2 }.SequenceEqual(sorted.Select(item => item.Id)));
            // 空顺序表退化为添加时间倒序。
            Assert.True(new[] { 4, 2, 3, 1 }.SequenceEqual(MoviePilotSubscriptionWall.Sort(items, MoviePilotSubscriptionSort.Custom, []).Select(item => item.Id)));
        });
        Test("订阅墙排序：更新时间与缺失集数倒序，并列按添加时间倒序且稳定", () =>
        {
            var items = new[]
            {
                Subscription(1, date: "2026-11-01T10:00:00+08:00", lastUpdate: "2026-11-05T10:00:00+08:00", total: 12, lack: 3),
                Subscription(2, date: "2026-11-03T10:00:00+08:00", lastUpdate: "2026-11-07T10:00:00+08:00", total: 12, lack: 5),
                Subscription(3, date: "2026-11-02T10:00:00+08:00", lastUpdate: "2026-11-05T10:00:00+08:00", total: 12, lack: 5),
                Subscription(4, date: "2026-11-04T10:00:00+08:00", lastUpdate: "", total: 12, lack: 0)
            };
            Assert.True(new[] { 2, 1, 3, 4 }.SequenceEqual(MoviePilotSubscriptionWall.Sort(items, MoviePilotSubscriptionSort.LastUpdate, []).Select(item => item.Id)));
            // 缺失集数：5 并列按添加时间倒序（2 比 3 新），再 3，再 0。
            Assert.True(new[] { 2, 3, 1, 4 }.SequenceEqual(MoviePilotSubscriptionWall.Sort(items, MoviePilotSubscriptionSort.LackEpisode, []).Select(item => item.Id)));
            Assert.True(new[] { 4, 2, 3, 1 }.SequenceEqual(MoviePilotSubscriptionWall.Sort(items, MoviePilotSubscriptionSort.AddTime, []).Select(item => item.Id)));
            // 缺失字段按 0 处理，不抛异常。
            var bare = new[] { Subscription(5), Subscription(6, lack: 2) };
            Assert.True(new[] { 6, 5 }.SequenceEqual(MoviePilotSubscriptionWall.Sort(bare, MoviePilotSubscriptionSort.LackEpisode, []).Select(item => item.Id)));
        });
    }

    private static void WallOptionsAndLabels()
    {
        Test("订阅墙选项：电影只有基本筛选与三种排序，电视剧展开进度状态与缺失集数", () =>
        {
            Assert.True(new[] { MoviePilotSubscriptionWallFilter.All, MoviePilotSubscriptionWallFilter.BestVersion,
                MoviePilotSubscriptionWallFilter.Pending, MoviePilotSubscriptionWallFilter.Paused }
                .SequenceEqual(MoviePilotSubscriptionWall.FilterOptions(false)));
            Assert.True(new[] { MoviePilotSubscriptionWallFilter.All, MoviePilotSubscriptionWallFilter.BestVersion,
                MoviePilotSubscriptionWallFilter.NotStarted, MoviePilotSubscriptionWallFilter.Subscribing,
                MoviePilotSubscriptionWallFilter.Pending, MoviePilotSubscriptionWallFilter.Paused, MoviePilotSubscriptionWallFilter.Completed }
                .SequenceEqual(MoviePilotSubscriptionWall.FilterOptions(true)));
            Assert.False(MoviePilotSubscriptionWall.SortOptions(false).Contains(MoviePilotSubscriptionSort.LackEpisode));
            Assert.True(MoviePilotSubscriptionWall.SortOptions(true).Contains(MoviePilotSubscriptionSort.LackEpisode));
            Assert.True(new[] { MoviePilotSubscriptionSort.Custom, MoviePilotSubscriptionSort.LastUpdate, MoviePilotSubscriptionSort.AddTime }
                .SequenceEqual(MoviePilotSubscriptionWall.SortOptions(false)));
        });
        Test("订阅墙展示词：筛选与排序选项的中文标签", () =>
        {
            Assert.Equal("全部", MoviePilotSubscriptionWall.FilterLabel(MoviePilotSubscriptionWallFilter.All));
            Assert.Equal("洗版中", MoviePilotSubscriptionWall.FilterLabel(MoviePilotSubscriptionWallFilter.BestVersion));
            Assert.Equal("未开始", MoviePilotSubscriptionWall.FilterLabel(MoviePilotSubscriptionWallFilter.NotStarted));
            Assert.Equal("订阅中", MoviePilotSubscriptionWall.FilterLabel(MoviePilotSubscriptionWallFilter.Subscribing));
            Assert.Equal("待定", MoviePilotSubscriptionWall.FilterLabel(MoviePilotSubscriptionWallFilter.Pending));
            Assert.Equal("暂停", MoviePilotSubscriptionWall.FilterLabel(MoviePilotSubscriptionWallFilter.Paused));
            Assert.Equal("订阅完成", MoviePilotSubscriptionWall.FilterLabel(MoviePilotSubscriptionWallFilter.Completed));
            Assert.Equal("自定义", MoviePilotSubscriptionWall.SortLabel(MoviePilotSubscriptionSort.Custom));
            Assert.Equal("最后更新时间", MoviePilotSubscriptionWall.SortLabel(MoviePilotSubscriptionSort.LastUpdate));
            Assert.Equal("添加时间", MoviePilotSubscriptionWall.SortLabel(MoviePilotSubscriptionSort.AddTime));
            Assert.Equal("缺失集数", MoviePilotSubscriptionWall.SortLabel(MoviePilotSubscriptionSort.LackEpisode));
        });
    }
}
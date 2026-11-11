using System.Net;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 订阅墙的服务层：执行批次轮询/停止、自定义顺序读写、批量暂停/启用/删除、三个维护入口。
/// 判据全部来自网页端源码（SubscribeListView.vue 与后端 subscribe.py / user-v3.py）：
/// 明确拒绝（success:false）按服务器原话抛、不锁核对；传输超时才锁；删除只认 status=="deleted"。
/// </summary>
internal static class MoviePilotSubscriptionExecutionTests
{
    private const string Session = """{"access_token":"fixture-token","super_user":true,"user_name":"fixture"}""";
    private const string Series = """{"id":12,"name":"示例剧","year":"2026","type":"电视剧","media_source":"douban","media_id":"42","season":2,"total_episode":4,"start_episode":1,"lack_episode":1,"state":"R"}""";
    private const string Batch = """{"batch_id":"b1","source":"manual","state":"running","phase":"searching","total_count":10,"processed_count":3,"can_cancel":true,"updated_at":"2026-11-11T12:00:00"}""";
    private static string SeriesWithId(int id) => Series.Replace("\"id\":12", "\"id\":" + id)
        .Replace("\"media_id\":\"42\"", "\"media_id\":\"" + (id + 100) + "\"");
    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true; settings.MoviePilot.Url = "https://fixture.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(settings.MoviePilot, "fixture-password");
        return settings;
    }
    private static MoviePilotService Service(StubTransport transport, AppSettings? settings = null) =>
        new(new MoviePilotClient(transport), new(PassthroughSecretProtector.Instance), settings ?? Settings());
    private static StubTransport Transport() => new StubTransport().Answer("login/access-token", Session).Answer("subscribe/", "[" + Series + "]");

    internal static void Register()
    {
        Test("MoviePilot 订阅执行批次：limit=10 只读、盖连接戳、去重并跳过无编号", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Answer("subscribe/execution/batches", "[" + Batch + ",{\"state\":\"running\"}," + Batch + "]");
            var service = Service(transport);
            var batches = service.SubscriptionExecutionBatchesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, batches.Count);
            Assert.Equal("b1", batches[0].BatchId);
            Assert.Equal(service.ConnectionStamp, batches[0].ConnectionStamp);
            Assert.True(service.IsCurrentConnection(batches[0].ConnectionStamp));
            var sent = transport.Only("subscribe/execution/batches");
            Assert.Equal("GET", sent.Method);
            Assert.Equal("https://fixture.invalid:3001/api/v1/subscribe/execution/batches?limit=10", sent.Url);
        });
        Test("MoviePilot 订阅执行批次：非数组回人话异常", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Answer("subscribe/execution/batches", "{}");
            var error = Assert.Catch<MoviePilotException>(() => Service(transport).SubscriptionExecutionBatchesAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("格式不正确", error.Message);
        });
        Test("MoviePilot 订阅执行批次：停止走 PUT，明确拒绝按原话抛且不锁核对", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Sequence("subscribe/execution/batches/b1/cancel",
                    (HttpStatusCode.OK, "{\"success\":true}"),
                    (HttpStatusCode.OK, "{\"success\":false,\"message\":\"已经结束，暂时无法停止\"}"))
                .Answer("subscribe/execution/batches", "[" + Batch + "]");
            var service = Service(transport);
            var batch = service.SubscriptionExecutionBatchesAsync(CancellationToken.None).GetAwaiter().GetResult().Single();
            service.CancelSubscriptionExecutionBatchAsync(batch, CancellationToken.None).GetAwaiter().GetResult();
            var sent = transport.Only("subscribe/execution/batches/b1/cancel");
            Assert.Equal("PUT", sent.Method);
            Assert.Equal("", sent.Body);
            var error = Assert.Catch<MoviePilotException>(() => service.CancelSubscriptionExecutionBatchAsync(batch, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal("已经结束，暂时无法停止", error.Message);
            Assert.False(error is MoviePilotOperationBlockedException);
            Assert.Equal(2, transport.Count("subscribe/execution/batches/b1/cancel"));
        });
        Test("MoviePilot 订阅执行批次：停止超时锁核对，旧连接戳与换账号都拒绝发送", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Throw("subscribe/execution/batches/b1/cancel", new HttpRequestException("fixture timeout"))
                .Answer("subscribe/execution/batches", "[" + Batch + "]");
            var settings = Settings();
            var service = Service(transport, settings);
            var batch = service.SubscriptionExecutionBatchesAsync(CancellationToken.None).GetAwaiter().GetResult().Single();
            Assert.Throws<MoviePilotOperationUncertainException>(() => service.CancelSubscriptionExecutionBatchAsync(batch, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.CancelSubscriptionExecutionBatchAsync(batch, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("subscribe/execution/batches/b1/cancel"));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.CancelSubscriptionExecutionBatchAsync(batch with { ConnectionStamp = "" }, CancellationToken.None).GetAwaiter().GetResult());
            settings.MoviePilot.Username = "other";
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.CancelSubscriptionExecutionBatchAsync(batch, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("subscribe/execution/batches/b1/cancel"));
        });
        Test("MoviePilot 订阅顺序：按类型取配置键，空值与坏编号读作空表", () =>
        {
            using var transport = Transport()
                .Sequence("user/config/SubscribeTvOrder",
                    (HttpStatusCode.OK, """{"value":[{"id":3},{"id":1},{"id":3},{"id":0},{"id":-2},{"id":"x"}]}"""),
                    (HttpStatusCode.OK, """{"value":"oops"}"""))
                .Answer("user/config/SubscribeMovieOrder", "{\"value\":null}");
            var service = Service(transport);
            Assert.True(new[] { 3, 1 }.SequenceEqual(service.SubscriptionOrderAsync(true, CancellationToken.None).GetAwaiter().GetResult()));
            Assert.Equal(0, service.SubscriptionOrderAsync(true, CancellationToken.None).GetAwaiter().GetResult().Count);
            Assert.Equal(0, service.SubscriptionOrderAsync(false, CancellationToken.None).GetAwaiter().GetResult().Count);
            Assert.Equal("GET", transport.Only("user/config/SubscribeMovieOrder").Method);
            Assert.Equal("https://fixture.invalid:3001/api/v1/user/config/SubscribeTvOrder", transport.SentTo("user/config/SubscribeTvOrder")[0].Url);
        });
        Test("MoviePilot 订阅顺序：落盘正文是 id 数组，明确拒绝不锁核对", () =>
        {
            using var transport = Transport()
                .Sequence("user/config/SubscribeMovieOrder",
                    (HttpStatusCode.OK, "{\"success\":true}"),
                    (HttpStatusCode.OK, "{\"success\":false,\"message\":\"配置内容过大\"}"),
                    (HttpStatusCode.OK, "{\"success\":true}"));
            var service = Service(transport);
            service.SaveSubscriptionOrderAsync(false, [5, 2], CancellationToken.None).GetAwaiter().GetResult();
            var error = Assert.Catch<MoviePilotException>(() => service.SaveSubscriptionOrderAsync(false, [5, 2], CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal("配置内容过大", error.Message);
            Assert.False(error is MoviePilotOperationBlockedException);
            service.SaveSubscriptionOrderAsync(false, [5, 2], CancellationToken.None).GetAwaiter().GetResult();
            var sent = transport.SentTo("user/config/SubscribeMovieOrder");
            Assert.Equal(3, sent.Count);
            Assert.True(sent.All(request => request.Method == "POST"));
            Assert.Equal("[{\"id\":5},{\"id\":2}]", sent[0].Body);
        });
        Test("MoviePilot 订阅批量暂停：逐条 PUT，失败项保留待重试", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Answer("subscribe/status/12", "{\"success\":true}")
                .Fail("subscribe/status/13", HttpStatusCode.BadRequest)
                .Answer("subscribe/status/14", "{\"success\":true}")
                .Answer("subscribe/", "[" + Series + "," + SeriesWithId(13) + "," + SeriesWithId(14) + "]");
            var service = Service(transport);
            var items = service.SubscriptionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            var result = service.ManageSubscriptionBatchAsync(items, MoviePilotSubscriptionAction.Pause, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(0, result.UncertainCount);
            Assert.True(new[] { 13 }.SequenceEqual(result.FailedIds));
            Assert.Equal(MoviePilotSubscriptionBatchOutcome.Failed, result.Items[1].Outcome);
            Assert.NotNull(result.Items[1].Error);
            Assert.Equal("PUT", transport.Only("subscribe/status/13").Method);
            Assert.Throws<ArgumentOutOfRangeException>(() => service.ManageSubscriptionBatchAsync(items, MoviePilotSubscriptionAction.Search, CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("MoviePilot 订阅批量删除：只认 deleted 回执，未确认与超时各归其档", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Answer("subscribe/12", "{\"success\":true,\"data\":{\"status\":\"deleted\"}}")
                .Answer("subscribe/13", "{\"success\":true,\"data\":{}}")
                .Throw("subscribe/14", new HttpRequestException("fixture timeout"))
                .Sequence("subscribe/",
                    (HttpStatusCode.OK, "[" + Series + "," + SeriesWithId(13) + "," + SeriesWithId(14) + "]"),
                    (HttpStatusCode.OK, "{\"success\":true}"),
                    (HttpStatusCode.OK, "{\"success\":true}"));
            var service = Service(transport);
            var items = service.SubscriptionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            service.SubscribeAsync(items[0].Media, CancellationToken.None).GetAwaiter().GetResult();
            service.SubscribeAsync(items[1].Media, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(MoviePilotOperationState.Submitted, service.SubscribeState(items[0].Media));
            var result = service.ManageSubscriptionBatchAsync(items, MoviePilotSubscriptionAction.Delete, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(MoviePilotSubscriptionBatchOutcome.Success, result.Items[0].Outcome);
            Assert.Equal(MoviePilotSubscriptionBatchOutcome.Failed, result.Items[1].Outcome);
            Assert.Contains("未确认删除", result.Items[1].Error!);
            Assert.Equal(MoviePilotSubscriptionBatchOutcome.Uncertain, result.Items[2].Outcome);
            Assert.True(new[] { 13, 14 }.SequenceEqual(result.FailedIds));
            // 明确删除成功才清掉新增订阅的防重状态；未确认的那条保持已提交。
            Assert.Equal(MoviePilotOperationState.Idle, service.SubscribeState(items[0].Media));
            Assert.Equal(MoviePilotOperationState.Submitted, service.SubscribeState(items[1].Media));
            Assert.True(service.SubscriptionNeedsReview(items[2]));
            var retry = service.ManageSubscriptionBatchAsync([items[2]], MoviePilotSubscriptionAction.Delete, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(MoviePilotSubscriptionBatchOutcome.Blocked, retry.Items[0].Outcome);
            Assert.Equal(1, transport.Count("subscribe/14"));
        });
        Test("MoviePilot 订阅批量：中途取消不再发剩余条目", () =>
        {
            using var cts = new CancellationTokenSource();
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Answer("subscribe/status/12", "{\"success\":true}")
                .Answer("subscribe/status/13", "{\"success\":true}")
                .Answer("subscribe/", "[" + Series + "," + SeriesWithId(13) + "]")
                .When("subscribe/status/12", () => cts.Cancel());
            var service = Service(transport);
            var items = service.SubscriptionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.Throws<OperationCanceledException>(() => service.ManageSubscriptionBatchAsync(items, MoviePilotSubscriptionAction.Pause, cts.Token).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("subscribe/status/13").Count);
        });
        Test("MoviePilot 订阅维护：搜索全部按类型编码并解析安排回执", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Answer("subscribe/search?type=", "{\"success\":true,\"data\":{\"target_count\":5,\"queued_count\":3,\"ongoing_count\":2}}");
            var service = Service(transport);
            var submission = service.SearchAllSubscriptionsAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            Assert.NotNull(submission);
            Assert.Equal(5, submission!.TargetCount);
            Assert.Equal(3, submission.QueuedCount);
            Assert.Equal(2, submission.OngoingCount);
            var sent = transport.Only("subscribe/search?type=");
            Assert.Equal("POST", sent.Method);
            Assert.Equal("", sent.Body);
            Assert.Equal("https://fixture.invalid:3001/api/v1/subscribe/search?type=%E7%94%B5%E8%A7%86%E5%89%A7", sent.Url);
            transport.Answer("subscribe/search?type=", "{\"success\":true,\"data\":null}");
            Assert.Null(service.SearchAllSubscriptionsAsync(false, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("type=%E7%94%B5%E5%BD%B1", transport.SentTo("subscribe/search?type=")[1].Url);
        });
        Test("MoviePilot 订阅维护：没有需要搜索是明确拒绝，不锁核对", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Answer("subscribe/search?type=", "{\"success\":false,\"message\":\"没有需要搜索的订阅\"}");
            var service = Service(transport);
            var error = Assert.Catch<MoviePilotException>(() => service.SearchAllSubscriptionsAsync(true, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal("没有需要搜索的订阅", error.Message);
            Assert.False(error is MoviePilotOperationBlockedException);
            Assert.Catch<MoviePilotException>(() => service.SearchAllSubscriptionsAsync(true, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(2, transport.Count("subscribe/search?type="));
        });
        Test("MoviePilot 订阅维护：刷新与元数据走各自路径，405 才退回 GET", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Sequence("subscribe/refresh?type=", (HttpStatusCode.MethodNotAllowed, ""), (HttpStatusCode.OK, "{\"success\":true}"))
                .Answer("subscribe/check?type=", "{\"success\":true}");
            var service = Service(transport);
            service.RefreshSubscriptionsAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            var refresh = transport.SentTo("subscribe/refresh?type=");
            Assert.Equal(2, refresh.Count);
            Assert.Equal("POST", refresh[0].Method);
            Assert.Equal("GET", refresh[1].Method);
            service.RefreshSubscriptionMetadataAsync(false, CancellationToken.None).GetAwaiter().GetResult();
            var check = transport.Only("subscribe/check?type=");
            Assert.Equal("POST", check.Method);
            Assert.Contains("type=%E7%94%B5%E5%BD%B1", check.Url);
        });
        Test("MoviePilot 订阅维护：超时锁核对不重放", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Throw("subscribe/check?type=", new HttpRequestException("fixture timeout"));
            var service = Service(transport);
            Assert.Throws<MoviePilotOperationUncertainException>(() => service.RefreshSubscriptionMetadataAsync(true, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.RefreshSubscriptionMetadataAsync(true, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("subscribe/check?type="));
        });
        Test("MoviePilot 订阅排序偏好：按媒体类型读写、同值不重复落盘、空值清除", () =>
        {
            var settings = Settings();
            var saves = 0;
            var service = new MoviePilotService(new MoviePilotClient(Transport()), new(PassthroughSecretProtector.Instance), settings, () => saves++);
            Assert.Null(service.SubscriptionSortPreference("电视剧"));
            service.SaveSubscriptionSortPreference("电视剧", "last_update");
            Assert.Equal("last_update", service.SubscriptionSortPreference("电视剧"));
            Assert.Equal(1, saves);
            // 同值再存不触发落盘；另一类互不影响。
            service.SaveSubscriptionSortPreference("电视剧", "last_update");
            Assert.Equal(1, saves);
            Assert.Null(service.SubscriptionSortPreference("电影"));
            service.SaveSubscriptionSortPreference("电影", "date");
            Assert.Equal(2, saves);
            Assert.Equal("last_update", service.SubscriptionSortPreference("电视剧"));
            // 空值清除那一类。
            service.SaveSubscriptionSortPreference("电视剧", null);
            Assert.Null(service.SubscriptionSortPreference("电视剧"));
            Assert.Equal(3, saves);
            Assert.Equal("date", service.SubscriptionSortPreference("电影"));
        });
    }
}

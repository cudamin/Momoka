using System.Net;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotSubscriptionActivityTests
{
    private const string Series = """{"id":12,"name":"示例剧","year":"2026","type":"电视剧","media_source":"themoviedb","media_id":"42","season":0,"state":"R"}""";
    private const string Event = """{"event_id":"event-1","kind":"search","source":"manual","status":"waiting","started_at":"2026-11-09T02:00:00Z","finished_at":null,"counts":{"requested_sites":2,"sites":3,"discovered":8,"qualified":1,"submitted":0},"details_loaded":false,"details":{"phase":"queued","reason":"搜索请求已安排"}}""";
    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "https://fixture.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(settings.MoviePilot, "fixture-password");
        return settings;
    }

    private static StubTransport Transport() => new StubTransport()
        .Answer("login/access-token", """{"access_token":"fixture-token","super_user":true,"user_name":"fixture"}""");

    private static MoviePilotService Service(StubTransport transport, AppSettings? settings = null, string series = Series) =>
        new(new MoviePilotClient(transport.Answer("subscribe/", "[" + series + "]")), new(PassthroughSecretProtector.Instance), settings ?? Settings());

    private static MoviePilotSubscription Read(MoviePilotService service) => service.SubscriptionsAsync(CancellationToken.None).GetAwaiter().GetResult().Single();

    internal static void Register()
    {
        Test("MoviePilot 分享：电视剧发送四个精确字段，特别篇标题不重复年份或季号", () =>
        {
            using var transport = Transport().Answer("subscribe/share", "{\"success\":true}");
            using var service = Service(transport); var subscription = Read(service);
            service.ShareSubscriptionAsync(subscription, "分享说明", "分享者", CancellationToken.None).GetAwaiter().GetResult();
            var request = transport.Only("subscribe/share");
            Assert.Equal("POST", request.Method);
            var body = JsonSerializer.Deserialize<JsonElement>(request.Body);
            Assert.Equal(4, body.EnumerateObject().Count());
            Assert.Equal(12, body.GetProperty("subscribe_id").GetInt32());
            Assert.Equal("示例剧 S00", body.GetProperty("share_title").GetString());
            Assert.Equal("分享说明", body.GetProperty("share_comment").GetString());
            Assert.Equal("分享者", body.GetProperty("share_user").GetString());
            Assert.False(service.SubscriptionNeedsReview(subscription));
        });
        Test("MoviePilot 分享：电影和空表单在发送前拒绝", () =>
        {
            using var transport = Transport();
            using var service = Service(transport, series: Series.Replace("电视剧", "电影", StringComparison.Ordinal)); var subscription = Read(service);
            Assert.Throws<MoviePilotException>(() => service.ShareSubscriptionAsync(subscription, "说明", "用户", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Count("subscribe/share"));
            using var seriesTransport = Transport(); using var seriesService = Service(seriesTransport); var series = Read(seriesService);
            foreach (var (comment, user) in new[] { ("", "用户"), ("说明", "  ") })
                Assert.Throws<MoviePilotException>(() => seriesService.ShareSubscriptionAsync(series, comment, user, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, seriesTransport.Count("subscribe/share"));
        });
        Test("MoviePilot 分享：超时与405不改发GET，并锁定该订阅其他写操作", () =>
        {
            foreach (var timeout in new[] { true, false })
            {
                using var transport = Transport();
                if (timeout) transport.Throw("subscribe/share", new HttpRequestException("fixture timeout"));
                else transport.Fail("subscribe/share", HttpStatusCode.MethodNotAllowed);
                using var service = Service(transport); var subscription = Read(service);
                Assert.Throws<MoviePilotOperationUncertainException>(() => service.ShareSubscriptionAsync(subscription, "说明", "用户", CancellationToken.None).GetAwaiter().GetResult());
                Assert.Throws<MoviePilotOperationBlockedException>(() => service.ShareSubscriptionAsync(subscription, "另一说明", "用户", CancellationToken.None).GetAwaiter().GetResult());
                Assert.Throws<MoviePilotOperationBlockedException>(() => service.ManageSubscriptionAsync(subscription, MoviePilotSubscriptionAction.Pause, CancellationToken.None).GetAwaiter().GetResult());
                Assert.Equal(1, transport.Count("subscribe/share"));
                Assert.Equal("POST", transport.Only("subscribe/share").Method);
                Assert.True(service.SubscriptionNeedsReview(subscription));
            }
        });
        Test("MoviePilot 活动：GET保留过滤及稳定游标，排队与空列表不虚构完成", () =>
        {
            using var transport = Transport().Answer("subscribe/12/activity", "{\"items\":[" + Event + "],\"next_cursor\":\"cursor-2\"}");
            using var service = Service(transport); var subscription = Read(service);
            var page = service.SubscriptionActivityAsync(subscription, 7, "search", "cursor-1", CancellationToken.None).GetAwaiter().GetResult();
            var request = transport.Only("subscribe/12/activity");
            Assert.Equal("GET", request.Method);
            Assert.Contains("days=7", request.Url); Assert.Contains("kind=search", request.Url); Assert.Contains("before=cursor-1", request.Url);
            Assert.Equal("cursor-2", page.NextCursor);
            Assert.Equal(1, page.Items.Count); var activity = page.Items[0];
            Assert.Equal("event-1", activity.EventId); Assert.Contains("订阅搜索", activity.Title);
            Assert.Contains("已安排", activity.Summary); Assert.DoesNotContain("已入库", activity.Summary);
            Assert.False(activity.DetailsLoaded); Assert.Equal(2026, activity.StartedAt!.Value.Year);
            transport.Answer("subscribe/12/activity", "{\"items\":[],\"next_cursor\":null}");
            var empty = service.SubscriptionActivityAsync(subscription, null, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(0, empty.Items.Count); Assert.Null(empty.NextCursor);
            var allTime = transport.SentTo("subscribe/12/activity").Last().Url;
            Assert.DoesNotContain("days=", allTime); Assert.DoesNotContain("kind=", allTime); Assert.DoesNotContain("before=", allTime);
        });
        Test("MoviePilot 活动：详情按事件主键读取，白名单投影不输出私有字段与链接", () =>
        {
            const string detail = """{"event_id":"event-1","kind":"refresh","source":"automatic","status":"empty","started_at":"2026-11-09T02:00:00Z","counts":{"discovered":86,"related":3,"qualified":0,"submitted":0},"details":{"reason":"没有符合条件的资源","rules":{"resolution":"2160p","cookie":"private-cookie","unknown":{"token":"private-token"}},"resources":[{"title":"示例.S01E01","site":"示例站","decision":"rejected","reason":"720p不符合2160p","enclosure":"https://private.invalid/file?token=private-token","cookie":"private-cookie"}],"sites":[{"id":1,"name":"示例站","candidates":3,"status":"checked","password":"private-password"}],"changes":[{"field":"resolution","before":"1080p","after":"2160p"},{"field":"cookie","before":"private-cookie","after":"private-token"}],"unknown":{"token":"private-token"}}}""";
            using var transport = Transport().Answer("subscribe/12/activity/event-1", detail);
            using var service = Service(transport); var subscription = Read(service);
            var activity = service.SubscriptionActivityDetailAsync(subscription, "event-1", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("GET", transport.Only("subscribe/12/activity/event-1").Method);
            Assert.True(activity.DetailsLoaded); Assert.Contains("无可下载资源", activity.Summary);
            Assert.Contains("86", activity.Summary); Assert.Contains("3", activity.Summary);
            Assert.Contains("示例.S01E01", activity.Details); Assert.Contains("示例站", activity.Details); Assert.Contains("720p不符合2160p", activity.Details);
            Assert.Contains("1080p", activity.Details); Assert.Contains("2160p", activity.Details);
            foreach (var secret in new[] { "private-cookie", "private-token", "private-password", "private.invalid", "enclosure", "unknown" })
                Assert.DoesNotContain(secret, activity.Details);
        });
        Test("MoviePilot 活动：无效过滤游标和错事件在读取前或解析时拒绝", () =>
        {
            using var transport = Transport().Answer("subscribe/12/activity", "{}");
            using var service = Service(transport); var subscription = Read(service);
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityAsync(subscription, 0, null, null, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityAsync(subscription, 366, null, null, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityAsync(subscription, 7, "delete", null, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityAsync(subscription, 7, null, "a/b?token=secret", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityDetailAsync(subscription, "../secret", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Count("subscribe/12/activity"));
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityAsync(subscription, null, null, null, CancellationToken.None).GetAwaiter().GetResult());
            transport.Answer("subscribe/12/activity", Event.Replace("event-1", "event-other", StringComparison.Ordinal));
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityDetailAsync(subscription, "event-1", CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("MoviePilot 活动：缺失与非法计数显示未知，安全字段里的链接和凭据也不泄露", () =>
        {
            const string detail = """{"event_id":"event-1","kind":"search","source":"new-source","status":"running","started_at":"bad-date","counts":{"sites":"3","discovered":-1},"details":{"reason":"失败 https://private.invalid/a?token=private-token Authorization: Bearer private-bearer","resources_omitted":4,"attempts":[{"started_at":"2026-11-09T02:00:00Z","finished_at":"2026-11-09T02:10:00Z","status":"waiting","counts":{"related":2,"submitted":1},"rules":{"resolution":"1080p","cookie":"private-cookie"}}]}}""";
            using var transport = Transport().Answer("subscribe/12/activity/event-1", detail);
            using var service = Service(transport); var subscription = Read(service);
            var activity = service.SubscriptionActivityDetailAsync(subscription, "event-1", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Contains("未知", activity.Summary); Assert.DoesNotContain("提交下载 0", activity.Summary);
            Assert.Null(activity.StartedAt); Assert.Equal("来源未知", activity.Source);
            Assert.Contains("4 项资源未保留明细", activity.Details); Assert.Contains("恢复执行", activity.Details);
            Assert.Contains("1080p", activity.Details);
            foreach (var secret in new[] { "private.invalid", "private-token", "private-bearer", "private-cookie" }) Assert.DoesNotContain(secret, activity.Details);
        });
        Test("MoviePilot 分享：提交中拒绝重复，预先取消不发送请求", () =>
        {
            using var transport = Transport().Answer("subscribe/share", "{\"success\":true}");
            using var service = Service(transport); var subscription = Read(service);
            transport.When("subscribe/share", () => Assert.Throws<MoviePilotOperationBlockedException>(() =>
                service.ShareSubscriptionAsync(subscription, "另一说明", "用户", CancellationToken.None).GetAwaiter().GetResult()));
            service.ShareSubscriptionAsync(subscription, "说明", "用户", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, transport.Count("subscribe/share"));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => service.ShareSubscriptionAsync(subscription, "说明", "用户", cancel.Token).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Count("subscribe/share")); Assert.False(service.SubscriptionNeedsReview(subscription));
        });
        Test("MoviePilot 活动：404与503保留失败，连接改变后拒绝旧卡与迟到响应", () =>
        {
            using var transport = Transport().Fail("subscribe/12/activity", HttpStatusCode.NotFound);
            var settings = Settings(); using var service = Service(transport, settings); var subscription = Read(service);
            Assert.Equal(HttpStatusCode.NotFound, Assert.Catch<MoviePilotException>(() => service.SubscriptionActivityAsync(subscription, 7, null, null, CancellationToken.None).GetAwaiter().GetResult()).Status);
            transport.Fail("subscribe/12/activity", HttpStatusCode.ServiceUnavailable);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, Assert.Catch<MoviePilotException>(() => service.SubscriptionActivityAsync(subscription, 7, null, null, CancellationToken.None).GetAwaiter().GetResult()).Status);
            transport.Answer("subscribe/12/activity", "{\"items\":[" + Event + "],\"next_cursor\":null}").When("subscribe/12/activity", () => settings.MoviePilot.Username = "other");
            Assert.Throws<OperationCanceledException>(() => service.SubscriptionActivityAsync(subscription, 7, null, null, CancellationToken.None).GetAwaiter().GetResult());
            var before = transport.Total;
            Assert.Throws<MoviePilotException>(() => service.SubscriptionActivityDetailAsync(subscription, "event-1", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<MoviePilotException>(() => service.ShareSubscriptionAsync(subscription, "说明", "用户", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(before, transport.Total);
        });
    }
}

using System.Net;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotCalendarTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);
    private static readonly DateOnly End = new(2026, 12, 1);
    private const string Session = """{"access_token":"anonymous-calendar-token","token_type":"bearer","user_name":"fixture","super_user":false}""";
    private const string Series = """{"date":"2026-11-20","subscribe":{"id":12,"name":"日历剧集","type":"电视剧","year":"2026","season":0,"poster":"https://fixture.invalid/poster","total_episode":12,"lack_episode":0,"completed_episode":8,"last_update":"2026-11-20 10:00:00"},"episodes":[{"episode_number":2,"name":"第二集"},{"episode_number":1,"name":"第一集"}],"runtime":45}""";
    private const string Movie = """{"date":"2026-11-01","subscribe":{"id":13,"name":"日历电影","type":"电影","year":"2025"},"episodes":[],"runtime":120}""";

    public static void Register()
    {
        Test("MoviePilot 日历：读取真实日期窗口、分组摘要、特别篇与原始计数", () =>
        {
            using var transport = Transport().Answer("subscribe/calendar", Page(2, Series, Movie));
            using var service = Service(transport);
            var entries = Read(service);
            Assert.Equal(2, entries.Count);
            Assert.Equal(new DateOnly(2026, 11, 1), entries[0].Date);
            Assert.Equal(120, entries[0].RuntimeMinutes);
            var series = entries[1];
            Assert.Equal(12, series.SubscriptionId);
            Assert.Equal(0, series.Season);
            Assert.Equal(2026, series.Year);
            Assert.Equal(12, series.TotalEpisodes);
            Assert.Equal(8, series.CompletedEpisodes);
            Assert.Equal(0, series.MissingEpisodes);
            Assert.Equal(2, series.Episodes.Count);
            Assert.Equal(1, series.Episodes[0].Number);
            Assert.Equal("第一集", series.Episodes[0].Title);
            Assert.Null(series.Episodes[0].IsInLibrary, "下载已完成不能当作库内事实");
            Assert.True(service.IsCurrentConnection(series.ConnectionStamp));
            var request = transport.Only("subscribe/calendar");
            Assert.Equal("GET", request.Method);
            Assert.Contains("start=2026-11-01&end=2026-12-01&page=1&count=200", request.Url);
        });

        Test("MoviePilot 日历：超过一页完整读取并按同日订阅去重", () =>
        {
            using var transport = Transport().Sequence("subscribe/calendar", (HttpStatusCode.OK, Page(201, Series)),
                (HttpStatusCode.OK, Page(201, Series, Movie)));
            using var service = Service(transport);
            Assert.Equal(2, Read(service).Count);
            Assert.Equal(2, transport.Count("subscribe/calendar"));
            Assert.Contains("page=2&count=200", transport.SentTo("subscribe/calendar")[1].Url);
        });

        Test("MoviePilot 日历：失常重复分页不会无限读取或显示不完整结果", () =>
        {
            using var transport = Transport().Answer("subscribe/calendar", Page(1000, Series));
            using var service = Service(transport);
            Assert.Throws<MoviePilotException>(() => Read(service));
            Assert.Equal(2, transport.Count("subscribe/calendar"));
        });

        Test("MoviePilot 日历：范围含开始不含结束、不跨时区偏移也不猜坏日期", () =>
        {
            var before = Movie.Replace("2026-11-01", "2026-10-31", StringComparison.Ordinal);
            var after = Movie.Replace("2026-11-01", "2026-12-01", StringComparison.Ordinal);
            var invalid = Movie.Replace("2026-11-01", "not-a-date", StringComparison.Ordinal);
            var result = MoviePilotCalendar.ParsePage(Json(Page(5, before, Movie, after, invalid, Series)), Start, End, "fixture");
            Assert.Equal(2, result.Entries.Count);
            Assert.True(result.Entries.Any(entry => entry.Date == Start));
            Assert.False(result.Entries.Any(entry => entry.Date == End));
        });

        Test("MoviePilot 日历：只根据实际 library 文件标单集已入库", () =>
        {
            using var transport = Transport().Answer("subscribe/calendar", Page(1, Series))
                .Answer("subscribe/files/12", """{"subscribe":{"id":12,"name":"日历剧集","type":"电视剧","season":0},"episodes":{"1":{"library":[],"download":[{"file_path":"anonymous-download"}]},"2":{"library":[{"file_path":"anonymous-library"}],"download":[]}}}""");
            using var service = Service(transport);
            var entries = Read(service);
            Assert.Equal(false, entries[0].Episodes[0].IsInLibrary);
            Assert.Equal(true, entries[0].Episodes[1].IsInLibrary);
            Assert.Equal(1, transport.Count("subscribe/files/12"));
        });

        Test("MoviePilot 日历：同一订阅跨日只补查一次库内统计", () =>
        {
            using var transport = Transport().Answer("subscribe/calendar", Page(2, Series,
                    Series.Replace("2026-11-20", "2026-11-21", StringComparison.Ordinal)))
                .Answer("subscribe/files/12", """{"subscribe":{"id":12,"name":"日历剧集","type":"电视剧","season":0},"episodes":{}}""");
            using var service = Service(transport);
            Assert.Equal(2, Read(service).Count);
            Assert.Equal(1, transport.Count("subscribe/files/12"));
        });

        Test("MoviePilot 日历：文件统计失败或订阅身份改变保留未知", () =>
        {
            foreach (var data in new[] { "{}", """{"subscribe":{"id":12,"name":"同号新订阅","type":"电视剧","season":0},"episodes":{"1":{"library":[{}]}}}""" })
            {
                using var transport = Transport().Answer("subscribe/calendar", Page(1, Series)).Answer("subscribe/files/12", data);
                using var service = Service(transport);
                Assert.Null(Read(service)[0].Episodes[0].IsInLibrary);
            }
        });

        Test("MoviePilot 日历：无日历接口和精确旧路由422给升级指引", () =>
        {
            foreach (var response in new[]
            {
                (HttpStatusCode.NotFound, ""),
                (HttpStatusCode.UnprocessableEntity, """{"detail":[{"type":"int_parsing","input":"calendar","loc":["path","subscribe_id"]}]}""")
            })
            {
                using var transport = Transport().Sequence("subscribe/calendar", response);
                using var service = Service(transport);
                var error = Assert.Catch<MoviePilotException>(() => Read(service));
                Assert.Contains("3.2.3", error.Message);
                Assert.Null(error.Body);
                Assert.Equal(1, transport.Count("subscribe/calendar"));
            }
        });

        Test("MoviePilot 日历：其它422和权限拒绝不误判旧版，不回显服务端正文", () =>
        {
            foreach (var status in new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.Forbidden, HttpStatusCode.BadGateway })
            {
                using var transport = Transport().Sequence("subscribe/calendar", (status, """{"detail":"anonymous-private-marker"}"""));
                using var service = Service(transport);
                var error = Assert.Catch<MoviePilotException>(() => Read(service));
                Assert.DoesNotContain("3.2.3", error.Message);
                Assert.DoesNotContain("anonymous-private-marker", error.ToString());
                Assert.Null(error.Body);
                if (status == HttpStatusCode.Forbidden) Assert.Contains("权限", error.Message);
            }
        });

        Test("MoviePilot 日历：业务信封错误也使用本地恢复说明", () =>
        {
            using var transport = Transport().Answer("subscribe/calendar", """{"success":false,"message":"anonymous-private-marker"}""");
            using var service = Service(transport);
            var error = Assert.Catch<MoviePilotException>(() => Read(service));
            Assert.DoesNotContain("anonymous-private-marker", error.ToString());
            Assert.Null(error.Body);
        });

        Test("MoviePilot 日历：无效日期窗口、禁用与外部取消均不请求", () =>
        {
            using var transport = Transport();
            var settings = Settings();
            using var service = Service(transport, settings);
            foreach (var end in new[] { Start, Start.AddDays(-1), Start.AddDays(94) })
                Assert.Throws<ArgumentOutOfRangeException>(() => service.CalendarAsync(Start, end).GetAwaiter().GetResult());
            settings.MoviePilot.CalendarEnabled = false;
            Assert.Throws<OperationCanceledException>(() => Read(service));
            settings.MoviePilot.CalendarEnabled = true;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => service.CalendarAsync(Start, End, cancellation.Token).GetAwaiter().GetResult());
            settings.MoviePilot.Enabled = false;
            Assert.Throws<OperationCanceledException>(() => Read(service));
            Assert.Equal(0, transport.Total);
        });

        Test("MoviePilot 日历：在途禁用恢复、连接往返均拒绝旧结果", () =>
        {
            foreach (var change in new Action<MoviePilotSettings>[]
            {
                settings => { settings.CalendarEnabled = false; settings.CalendarEnabled = true; },
                settings => { settings.Enabled = false; settings.Enabled = true; },
                settings => { var name = settings.Username; settings.Username = "other"; settings.Username = name; }
            })
            {
                var settings = Settings();
                using var transport = Transport().Answer("subscribe/calendar", Page(1, Series))
                    .When("subscribe/calendar", () => change(settings.MoviePilot));
                using var service = Service(transport, settings);
                Assert.Throws<OperationCanceledException>(() => Read(service));
                Assert.Equal(0, transport.Count("subscribe/files/12"));
            }
        });

        Test("MoviePilot 日历：库统计迟到旧响应也不能覆盖更换连接", () =>
        {
            var settings = Settings();
            using var transport = Transport().Answer("subscribe/calendar", Page(1, Series))
                .Answer("subscribe/files/12", """{"subscribe":{"id":12,"name":"日历剧集","type":"电视剧","season":0},"episodes":{}}""")
                .When("subscribe/files/12", () => settings.MoviePilot.Username = "other");
            using var service = Service(transport, settings);
            Assert.Throws<OperationCanceledException>(() => Read(service));
        });

        Test("MoviePilot 日历：401仅正常刷新一次令牌，不把认证失败当空月", () =>
        {
            using var transport = Transport().Sequence("subscribe/calendar", (HttpStatusCode.Unauthorized, ""), (HttpStatusCode.OK, Page(1, Movie)));
            using var service = Service(transport);
            Assert.Equal(1, Read(service).Count);
            Assert.Equal(2, transport.Count("login/access-token"));
            transport.Fail("subscribe/calendar", HttpStatusCode.Unauthorized);
            transport.Sequence("subscribe/calendar", (HttpStatusCode.Unauthorized, ""));
            Assert.Throws<MoviePilotException>(() => Read(service));
            Assert.False(service.Available);
        });

        Test("MoviePilot 日历：缺失items或total不是空日历", () =>
        {
            foreach (var response in new[] { "{}", "[]", "{\"items\":[]}", "{\"total\":0}", "{\"total\":-1,\"items\":[]}" })
            {
                using var transport = Transport().Answer("subscribe/calendar", response);
                using var service = Service(transport);
                Assert.Throws<MoviePilotException>(() => Read(service));
            }
        });
    }

    private static IReadOnlyList<MoviePilotCalendarEntry> Read(MoviePilotService service) => service.CalendarAsync(Start, End).GetAwaiter().GetResult();
    private static StubTransport Transport() => new StubTransport().Answer("login/access-token", Session);
    private static string Page(int total, params string[] entries) => "{\"total\":" + total + ",\"items\":[" + string.Join(',', entries) + "]}";
    private static JsonElement Json(string data) { using var document = JsonDocument.Parse(data); return document.RootElement.Clone(); }
    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "https://fixture.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(settings.MoviePilot, "fixture");
        return settings;
    }
    private static MoviePilotService Service(StubTransport transport, AppSettings? settings = null) =>
        new(new MoviePilotClient(transport), new MoviePilotCredentials(PassthroughSecretProtector.Instance), settings ?? Settings());
}

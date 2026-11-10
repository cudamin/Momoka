using System.Net;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotLocalMediaTests
{
    private static string Media(string source = "themoviedb", string id = "42", string type = "电视剧") =>
        JsonSerializer.Serialize(new { title = "示例作品", media_source = source, media_id = id, type, year = "2026", poster_path = "https://image.fixture.invalid/t/p/original/poster.jpg" });

    private static string Context(string meta, string? media = null) =>
        "{\"meta_info\":" + meta + ",\"media_info\":" + (media ?? Media()) + ",\"media_info_is_target\":false,\"candidate_recognized\":false}";

    private static MoviePilotService Service(StubTransport transport, out AppSettings settings)
    {
        settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "https://fixture.invalid:3001";
        settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(settings.MoviePilot, "fixture-password");
        transport.Answer("login/access-token", """{"access_token":"fixture-token"}""");
        return new(new MoviePilotClient(transport), new(PassthroughSecretProtector.Instance), settings);
    }

    private static StubTransport Answers(string context, string? detail = null, string episodes = "[]") => new StubTransport()
        .Answer("media/recognize?", context).Answer("media/42?", detail ?? Media()).Answer("tmdb/42/", episodes);

    private static MoviePilotLocalRecognition Recognize(MoviePilotService service, string context = "作品/Season 00/作品.S00E01.mkv", CancellationToken token = default) =>
        service.RecognizeLocalFileAsync(context, token).GetAwaiter().GetResult();

    private static void OnlyReads(StubTransport transport, int count)
    {
        var reads = transport.SentTo("/api/v1/").Where(sent => !sent.Url.Contains("login/access-token", StringComparison.Ordinal)).ToArray();
        Assert.Equal(count, reads.Length);
        Assert.True(reads.All(sent => sent.Method == "GET" && sent.Body.Length == 0));
        Assert.False(reads.Any(sent => sent.Url.Contains("transfer", StringComparison.Ordinal) || sent.Url.Contains("scrape", StringComparison.Ordinal) || sent.Url.Contains("refresh", StringComparison.Ordinal)));
    }

    internal static void Register()
    {
        Test("MP 本机识别：电影固定 GET 查询、详情复核和连接身份，不套资源 false 标志", () =>
        {
            var transport = Answers(Context("{}", Media(type: "电影")), Media(type: "电影"));
            using var service = Service(transport, out _);
            const string fileContext = "电影/示例作品.2026 & 1080p.mkv";
            var result = Recognize(service, fileContext);
            Assert.Equal("42", result.Detail.Media.MediaId);
            Assert.Equal("电影", result.Detail.Media.Type);
            Assert.Null(result.Season);
            Assert.Null(result.Episode);
            Assert.True(service.IsCurrentConnection(result.Detail.Media.ConnectionStamp));
            Assert.Equal("https://fixture.invalid:3001/api/v1/media/recognize?title=" + Uri.EscapeDataString(fileContext), transport.Only("media/recognize?").Url);
            Assert.Contains("media_source=themoviedb&type_name=" + Uri.EscapeDataString("电影"), transport.Only("media/42?").Url);
            OnlyReads(transport, 2);
        });
        Test("MP 本机识别：文件名的未知类型不否决已识别电影", () =>
        {
            var transport = Answers(Context("""{"type":"未知"}""", Media(type: "电影")), Media(type: "电影"));
            using var service = Service(transport, out _);
            Assert.Equal("电影", Recognize(service).Detail.Media.Type);
            OnlyReads(transport, 2);
        });
        Test("MP 本机识别：S00 单集保留自身 ID、字段及配置图片域名", () =>
        {
            var transport = Answers(Context("""{"begin_season":"0","end_season":0,"season_list":[0],"begin_episode":"1","end_episode":1,"episode_list":[1],"total_episode":1}"""), episodes:
                """[{"id":701,"season_number":0,"episode_number":1,"name":"特别篇","overview":"单集简介","air_date":"2026-11-10","still_path":"/still.jpg","runtime":24,"vote_average":8}]""");
            using var service = Service(transport, out _);
            var result = Recognize(service);
            Assert.Equal<int?>(0, result.Season);
            Assert.Equal<int?>(1, result.Episode);
            Assert.Equal("701", result.EpisodeId);
            Assert.Equal("特别篇", result.EpisodeTitle);
            Assert.Equal("单集简介", result.EpisodeOverview);
            Assert.Equal("2026-11-10", result.EpisodeAirDate);
            Assert.Equal("https://image.fixture.invalid/t/p/original/still.jpg", result.EpisodeImageUrl);
            Assert.Contains("tmdb/42/0", transport.Only("tmdb/42/").Url);
            OnlyReads(transport, 3);
        });
        Test("MP 本机识别：明确列表可单独提供季集", () =>
        {
            var transport = Answers(Context("""{"season_list":[2],"episode_list":[3]}"""), episodes: """[{"id":"702","season_number":"2","episode_number":"3"}]""");
            using var service = Service(transport, out _);
            var result = Recognize(service);
            Assert.Equal<int?>(2, result.Season);
            Assert.Equal<int?>(3, result.Episode);
            Assert.Equal("702", result.EpisodeId);
        });
        foreach (var meta in new[]
        {
            "{}", "{\"begin_season\":0}", "{\"begin_episode\":1}",
            "{\"begin_season\":0,\"begin_episode\":0}", "{\"begin_season\":-1,\"begin_episode\":1}",
            "{\"begin_season\":0,\"end_season\":1,\"begin_episode\":1}",
            "{\"begin_season\":0,\"begin_episode\":1,\"end_episode\":2}",
            "{\"begin_season\":0,\"begin_episode\":1,\"episode_list\":[1,2]}",
            "{\"begin_season\":0,\"season_list\":[1],\"begin_episode\":1}",
            "{\"season_list\":[0,1],\"episode_list\":[1]}",
            "{\"begin_season\":0,\"begin_episode\":1,\"total_episode\":2}",
            "{\"begin_season\":0,\"begin_episode\":1,\"end_episode\":0}",
            "{\"begin_season\":0,\"begin_episode\":1,\"episode_list\":[1,\"invalid\"]}",
            "{\"begin_season\":0,\"begin_episode\":1,\"episode_list\":[null]}",
            "{\"end_season\":0,\"end_episode\":1}",
            "{\"begin_season\":0,\"begin_episode\":1,\"season_list\":{}}",
            "{\"begin_season\":0,\"begin_episode\":1,\"end_episode\":\"invalid\"}"
        })
            Test("MP 本机识别：季集不明确或范围拒绝 " + meta, () =>
            {
                var transport = Answers(Context(meta));
                using var service = Service(transport, out _);
                Assert.Throws<MoviePilotException>(() => Recognize(service));
                Assert.Equal(0, transport.SentTo("media/42?").Count);
                Assert.Equal(0, transport.SentTo("tmdb/42/").Count);
                OnlyReads(transport, 1);
            });
        foreach (var media in new[] { "null", "{}", Media(id: ""), Media(source: ""), Media(id: "invalid"), Media(id: "0"), Media(type: "音乐"), Media(type: ""), Media(source: "musicbrainz") })
            Test("MP 本机识别：无匹配、无有效身份或音乐拒绝 " + media, () =>
            {
                var transport = Answers(Context("""{"begin_season":0,"begin_episode":1}""", media));
                using var service = Service(transport, out _);
                Assert.Throws<MoviePilotException>(() => Recognize(service));
                OnlyReads(transport, 1);
            });
        foreach (var context in new[] { "null", "[]", "{}", "{\"meta_info\":null,\"media_info\":" + Media() + "}" })
            Test("MP 本机识别：无有效 Context 可恢复拒绝 " + context, () =>
            {
                var transport = Answers(context);
                using var service = Service(transport, out _);
                Assert.Throws<MoviePilotException>(() => Recognize(service));
                OnlyReads(transport, 1);
            });
        foreach (var meta in new[]
        {
            "{\"begin_season\":0,\"begin_episode\":1,\"media_source\":\"douban\",\"media_id\":\"42\"}",
            "{\"begin_season\":0,\"begin_episode\":1,\"media_source\":\"themoviedb\",\"media_id\":\"99\"}",
            "{\"begin_season\":0,\"begin_episode\":1,\"type\":\"电影\"}"
        })
            Test("MP 本机识别：解析身份或类型与识别冲突拒绝 " + meta, () =>
            {
                var transport = Answers(Context(meta));
                using var service = Service(transport, out _);
                Assert.Throws<MoviePilotException>(() => Recognize(service));
                OnlyReads(transport, 1);
            });
        Test("MP 本机识别：详情身份冲突不会读取集列表", () =>
        {
            var transport = Answers(Context("""{"begin_season":0,"begin_episode":1}"""), Media(id: "99"));
            using var service = Service(transport, out _);
            Assert.Throws<MoviePilotException>(() => Recognize(service));
            OnlyReads(transport, 2);
        });
        foreach (var episodes in new[]
        {
            "null", "{}", "[]", "[{\"id\":701,\"season_number\":0,\"episode_number\":2}]",
            "[{\"id\":701,\"season_number\":1,\"episode_number\":1}]", "[{\"id\":701,\"episode_number\":1}]",
            "[{\"season_number\":0,\"episode_number\":1}]", "[{\"id\":0,\"season_number\":0,\"episode_number\":1}]",
            "[{\"id\":701,\"season_number\":0,\"episode_number\":1},{\"id\":702,\"season_number\":0,\"episode_number\":1}]"
        })
            Test("MP 本机识别：缺失、错季、错集或重复单集拒绝 " + episodes, () =>
            {
                var transport = Answers(Context("""{"begin_season":0,"begin_episode":1}"""), episodes: episodes);
                using var service = Service(transport, out _);
                Assert.Throws<MoviePilotException>(() => Recognize(service));
                OnlyReads(transport, 3);
            });
        Test("MP 本机识别：其他影视来源保留系列身份与季集，不伪造 TMDB 单集 ID", () =>
        {
            var media = Media(source: "douban");
            var transport = Answers(Context("""{"begin_season":0,"begin_episode":1}""", media), media);
            using var service = Service(transport, out _);
            var result = Recognize(service);
            Assert.Equal("douban", result.Detail.Media.MediaSource);
            Assert.Equal<int?>(0, result.Season);
            Assert.Equal<int?>(1, result.Episode);
            Assert.Null(result.EpisodeId);
            Assert.Null(result.EpisodeTitle);
            OnlyReads(transport, 2);
        });
        foreach (var path in new[] { "media/recognize?", "media/42?", "tmdb/42/" })
            Test("MP 本机识别：每阶段连接 A→B→A 都拒绝迟到结果 " + path, () =>
            {
                var transport = Answers(Context("""{"begin_season":0,"begin_episode":1}"""), episodes: """[{"id":701,"season_number":0,"episode_number":1}]""");
                using var service = Service(transport, out var settings);
                transport.When(path, () =>
                {
                    settings.MoviePilot.Url = "https://changed.invalid:3001";
                    settings.MoviePilot.Url = "https://fixture.invalid:3001";
                });
                Assert.Throws<OperationCanceledException>(() => Recognize(service));
                Assert.Equal(1, transport.SentTo(path).Count);
                OnlyReads(transport, path == "media/recognize?" ? 1 : path == "media/42?" ? 2 : 3);
            });
        Test("MP 本机识别：空文件名与事先取消不发请求", () =>
        {
            var transport = Answers("null");
            using var service = Service(transport, out _);
            Assert.Throws<MoviePilotException>(() => Recognize(service, "   "));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => Recognize(service, token: cancellation.Token));
            Assert.Equal(0, transport.Total);
        });
        Test("MP 本机识别：识别服务错误不继续详情和单集读取", () =>
        {
            var transport = Answers("null").Fail("media/recognize?", HttpStatusCode.ServiceUnavailable);
            using var service = Service(transport, out _);
            Assert.Throws<MoviePilotException>(() => Recognize(service));
            OnlyReads(transport, 1);
        });
        Test("MP 本机校正：真实搜索候选的 S00 单集复用详情核对，不重发识别", () =>
        {
            var transport = Answers("null", episodes: """[{"id":701,"season_number":0,"episode_number":1}]""").Answer("media/search?", "[" + Media() + "]");
            using var service = Service(transport, out _);
            var selected = service.SearchAsync("示例作品", default).GetAwaiter().GetResult().Single();
            var result = service.ResolveLocalMediaAsync(selected, 0, 1).GetAwaiter().GetResult();
            Assert.Equal("701", result.EpisodeId);
            Assert.Equal(0, transport.SentTo("media/recognize?").Count);
            Assert.True(service.IsCurrentConnection(result.Detail.Media.ConnectionStamp));
            OnlyReads(transport, 3);
        });
        Test("MP 本机校正：电影只读取详情，不接受季集编号", () =>
        {
            var movie = Media(type: "电影");
            var transport = Answers("null", movie).Answer("media/search?", "[" + movie + "]");
            using var service = Service(transport, out _);
            var selected = service.SearchAsync("示例作品", default).GetAwaiter().GetResult().Single();
            Assert.Throws<MoviePilotException>(() => service.ResolveLocalMediaAsync(selected, 0, 1).GetAwaiter().GetResult());
            var result = service.ResolveLocalMediaAsync(selected, null, null).GetAwaiter().GetResult();
            Assert.Null(result.Season);
            Assert.Null(result.Episode);
            OnlyReads(transport, 2);
        });
        foreach (var pair in new (int? Season, int? Episode)[] { (null, 1), (0, null), (-1, 1), (0, 0), (10001, 1) })
            Test("MP 本机校正：缺失或无效季集不读取详情 " + pair, () =>
            {
                var transport = Answers("null").Answer("media/search?", "[" + Media() + "]");
                using var service = Service(transport, out _);
                var selected = service.SearchAsync("示例作品", default).GetAwaiter().GetResult().Single();
                Assert.Throws<MoviePilotException>(() => service.ResolveLocalMediaAsync(selected, pair.Season, pair.Episode).GetAwaiter().GetResult());
                OnlyReads(transport, 1);
            });
        Test("MP 本机校正：候选旧连接和无来源候选拒绝，不借当前身份", () =>
        {
            var transport = Answers("null").Answer("media/search?", "[" + Media() + "]");
            using var service = Service(transport, out var settings);
            var selected = service.SearchAsync("示例作品", default).GetAwaiter().GetResult().Single();
            Assert.Throws<MoviePilotException>(() => service.ResolveLocalMediaAsync(selected with { MediaSource = "" }, 0, 1).GetAwaiter().GetResult());
            settings.MoviePilot.Url = "https://changed.invalid:3001";
            Assert.Throws<MoviePilotException>(() => service.ResolveLocalMediaAsync(selected, 0, 1).GetAwaiter().GetResult());
            OnlyReads(transport, 1);
        });
        foreach (var path in new[] { "media/42?", "tmdb/42/" })
            Test("MP 本机校正：详情和单集读取途中切换连接拒绝 " + path, () =>
            {
                var transport = Answers("null", episodes: """[{"id":701,"season_number":0,"episode_number":1}]""").Answer("media/search?", "[" + Media() + "]");
                using var service = Service(transport, out var settings);
                var selected = service.SearchAsync("示例作品", default).GetAwaiter().GetResult().Single();
                transport.When(path, () => settings.MoviePilot.Username = "changed");
                Assert.Throws<OperationCanceledException>(() => service.ResolveLocalMediaAsync(selected, 0, 1).GetAwaiter().GetResult());
                OnlyReads(transport, path == "media/42?" ? 2 : 3);
            });
    }
}

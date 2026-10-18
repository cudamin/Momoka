using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AniBtIntegrationTests
{
    private const string Target = "/library/Anime S01E01.mkv";
    private const string Session = """{"access_token":"fixture-token","super_user":true,"user_name":"fixture-user"}""";
    private const string Sources = """[{"name":"Bangumi","media_source":"bangumi","media_types":["电影","电视剧"]}]""";
    private const string Media = """{"title":"匿名动画","media_source":"bangumi","media_id":"42","type":"电视剧"}""";
    private static readonly AniBtAnime Anime = new("ani_fixture", 42, "匿名动画");

    public static void Register()
    {
        Test("AniBT 菜单：动画电影、剧、季、集仅在 MP 可用时出现", () =>
        {
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
            {
                var item = new EmbyItem { Type = type, Genres = ["动漫"] };
                Assert.True(ItemMenu.For(item, true).Any(row => row.Command == ItemCommand.AniBtScrape));
                var versions = ItemMenu.For(item, true).Single(row => row.Command == ItemCommand.MoviePilotVersions);
                Assert.Equal("搜索不同版本", versions.Label);
                Assert.True(versions.Children!.Select(row => row.Command!.Value)
                    .SequenceEqual(new[] { ItemCommand.MoviePilotVersions, ItemCommand.AniBtVersions }));
                Assert.False(ItemMenu.For(item).Any(row => row.Command == ItemCommand.AniBtScrape));
                item.Genres.Clear();
                Assert.Null(ItemMenu.For(item, true).Single(row => row.Command == ItemCommand.MoviePilotVersions).Children);
                Assert.False(ItemMenu.For(item, true).Any(row => row.Command == ItemCommand.AniBtScrape));
                Assert.True(ItemMenu.For(item, true, animeGenre: true).Any(row => row.Command == ItemCommand.AniBtScrape));
            }
        });

        Test("AniBT 搜索电影：Bangumi 回的电影类型优先，不凭缺失 format 猜电视剧", () =>
        {
            using var transport = BaseTransport().Answer("media/source", Sources)
                .Answer("media/42?", Media.Replace("电视剧", "电影", StringComparison.Ordinal));
            using var service = Service(transport);
            var row = service.AniBtResourcesAsync(Anime, [Release()], service.CaptureAniBtConnection(), CancellationToken.None)
                .GetAwaiter().GetResult().Single();
            Assert.Equal("电影", row.MediaType);
            Assert.Equal("电影", row.MediaInfo.GetProperty("type").GetString());
        });

        Test("AniBT 订阅入口：动画类型且确认日本来源才显示", () =>
        {
            MoviePilotMediaDetail Detail(string body, string source = "themoviedb") => new(
                new MoviePilotMedia { Title = "示例动画", Type = "电视剧", MediaSource = source, MediaId = "42" },
                Json(body));
            Assert.True(AniBtEligibility.IsJapaneseAnime(Detail(
                """{"genres":[{"name":"动画"}],"production_countries":[{"name":"日本"}]}""")));
            Assert.True(AniBtEligibility.IsJapaneseAnime(Detail(
                """{"genres":[{"name":"动画"}],"original_language":"ja"}""")));
            Assert.True(AniBtEligibility.IsJapaneseAnime(Detail(
                """{"genres":[{"name":"日本动画"}],"original_language":"jpn"}""")));
            Assert.True(AniBtEligibility.IsJapaneseAnime(Detail("""{"genres":[{"name":"Animation"}]}""", "bangumi")));
            Assert.False(AniBtEligibility.IsJapaneseAnime(Detail(
                """{"genres":[{"name":"动画"}],"production_countries":[{"name":"中国大陆"}],"original_language":"zh"}""")));
            Assert.False(AniBtEligibility.IsJapaneseAnime(Detail(
                """{"genres":[{"name":"剧情"}],"production_countries":[{"name":"日本"}]}""")));
            Assert.False(AniBtEligibility.IsJapaneseAnime(Detail("""{"genres":[{"name":"动画"}]}""")));
            Assert.False(AniBtEligibility.IsJapaneseAnime(null));
        });

        Test("AniBT 资格：只读类型，不把标题和标签当动画类型", () =>
        {
            var item = new EmbyItem { Type = EmbyItemType.Series, Name = "动画制作纪录片", Tags = ["动漫"] };
            Assert.False(AniBtEligibility.HasGenre(item));
            item.Genres.Add("日本动画");
            Assert.True(AniBtEligibility.HasGenre(item));
            Assert.True(AniBtEligibility.Supports(item));
            Assert.False(AniBtEligibility.Supports(new EmbyItem { Type = EmbyItemType.BoxSet, Genres = ["动画"] }));
        });

        Test("AniBT 源文件名：move 历史仍使用原 source，只发送文件名", () =>
        {
            var history = new MoviePilotTransferHistory
            {
                Id = 7,
                Mode = "move",
                Success = true,
                SourcePath = "/downloads/[Group] Original - 01.mkv",
                TargetPath = Target,
                TargetFile = Json("""{"storage":"local","type":"file","path":"/library/Anime S01E01.mkv"}""")
            };
            Assert.Equal(Target, history.TransferPath);
            Assert.Equal("[Group] Original - 01.mkv", AniBtService.SourceName(Target, [history]));
            Assert.Equal("Anime S01E01.mkv", AniBtService.SourceName(Target, []));
            Assert.Throws<MoviePilotOperationBlockedException>(() => AniBtService.SourceName(Target, [history, history with { Id = 8 }]));
        });

        Test("AniBT 下载身份：bgmId 交 Bangumi 详情，完整 media_in 交 download 而非 TMDB", () =>
        {
            using var transport = BaseTransport().Answer("media/source", Sources).Answer("media/42?", Media)
                .Answer("download/", """{"success":true,"data":{"download_id":"fixture-download"}}""");
            using var service = Service(transport);
            var row = service.AniBtResourcesAsync(Anime, [Release()], service.CaptureAniBtConnection(), CancellationToken.None)
                .GetAwaiter().GetResult().Single();
            service.DownloadAsync(row, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Contains("media_source=bangumi", transport.Only("media/42?").Url);
            var sent = transport.Only("download/");
            Assert.Equal("POST", sent.Method);
            Assert.DoesNotContain("download/add", sent.Url);
            var body = Json(sent.Body);
            Assert.Equal("bangumi", body.GetProperty("media_in").GetProperty("media_source").GetString());
            Assert.Equal("42", body.GetProperty("media_in").GetProperty("media_id").GetString());
            Assert.False(body.TryGetProperty("tmdb_id", out _));
            Assert.Equal("https://anibt.net/api/torrent/rel_fixture.torrent",
                body.GetProperty("torrent_in").GetProperty("enclosure").GetString());
        });

        Test("AniBT 下载身份：相同数字的 TMDB 回话不能冒充 Bangumi", () =>
        {
            using var transport = BaseTransport().Answer("media/source", Sources).Answer("media/42?",
                """{"title":"错误媒体","media_source":"themoviedb","media_id":"42","type":"电视剧"}""");
            using var service = Service(transport);
            Assert.Throws<MoviePilotException>(() => service.AniBtResourcesAsync(Anime, [Release()],
                service.CaptureAniBtConnection(), CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("download/").Count);
        });

        Test("AniBT 普通种子：无 Bangumi 模块也可提交公开种子，不伪造媒体身份", () =>
        {
            using var transport = BaseTransport().Answer("media/source", "[]")
                .Answer("download/add", """{"success":true,"data":{"download_id":"fixture-download"}}""");
            using var service = Service(transport);
            var row = service.AniBtResourcesAsync(Anime, [Release()], service.CaptureAniBtConnection(), CancellationToken.None)
                .GetAwaiter().GetResult().Single();
            service.DownloadAsync(row, CancellationToken.None).GetAwaiter().GetResult();
            var body = Json(transport.Only("download/add").Body);
            Assert.False(body.TryGetProperty("media_in", out _));
            Assert.False(body.TryGetProperty("media_source", out _));
            Assert.False(body.TryGetProperty("media_id", out _));
            Assert.Equal(0, transport.SentTo("media/42?").Count);
        });

        Test("AniBT 资源：RSS 发布与 MoviePilot 下载资源按序配对", () =>
        {
            using var transport = BaseTransport().Answer("media/source", "[]");
            using var service = Service(transport);
            using var feed = new RssTransport("""
                <rss xmlns:anibt="https://anibt.net/xmlns/rss/1.0/"><channel>
                  <item><title>匿名动画 - 01 [1080p]</title><guid>rel_one</guid><anibt:bgmId>42</anibt:bgmId>
                    <anibt:groupName>字幕组甲</anibt:groupName><anibt:resolution>1080p</anibt:resolution>
                    <anibt:episodeKey>01</anibt:episodeKey>
                    <enclosure url="https://anibt.net/api/torrent/rel_one.torrent" length="1024" />
                  </item>
                  <item><title>匿名动画 - 02 [720p]</title><guid>rel_two</guid><anibt:bgmId>42</anibt:bgmId>
                    <anibt:groupName>字幕组乙</anibt:groupName><anibt:resolution>720p</anibt:resolution>
                    <anibt:episodeKey>02</anibt:episodeKey>
                    <enclosure url="https://anibt.net/api/torrent/rel_two.torrent" length="2048" />
                  </item>
                </channel></rss>
                """);
            using var client = new AniBtClient(feed);
            var integration = new AniBtService(client, service);
            var rows = integration.ResourcesAsync(Anime, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(2, rows.Count);
            Assert.Equal("01", rows[0].Release.EpisodeKey);
            Assert.Equal("字幕组甲", rows[0].Release.GroupName);
            Assert.Equal("1080p", rows[0].Release.Resolution);
            Assert.Equal("匿名动画 - 01 [1080p]", rows[0].Resource.Title);
            Assert.Equal("02", rows[1].Release.EpisodeKey);
            Assert.Equal("字幕组乙", rows[1].Release.GroupName);
            Assert.Equal(1, feed.Requests);
        });

        Test("AniBT 候选：未返回的选择不能触发任何 MP 刮削准备", () =>
        {
            using var transport = BaseTransport();
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotOperationBlockedException>(() => integration.SubmitAsync(Plan(service, Target),
                [Anime with { BgmId = 99 }], CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("media/source").Count);
            Assert.Equal(0, transport.SentTo("media/scrape").Count);
        });

        Test("AniBT 批量：第二个目标不是精确文件时，第一集也不能先写", () =>
        {
            using var transport = ScrapeTransport().Sequence("storage/list",
                (HttpStatusCode.OK, FileReply(Target)),
                (HttpStatusCode.OK, """{"success":true,"data":[{"storage":"local","type":"dir","path":"/library/Anime S01E02.mkv"}]}"""));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotException>(() => integration.SubmitAsync(Plan(service, Target, "/library/Anime S01E02.mkv"),
                [Anime, Anime], CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(2, transport.SentTo("storage/list").Count);
            Assert.Equal(0, transport.SentTo("media/scrape").Count);
        });

        Test("AniBT 刮削：当前文件无集号时停止，不把 AniBT 季号当可写参数", () =>
        {
            using var transport = ScrapeTransport().Answer("media/recognize_file", """{"meta_info":{"begin_season":2}}""");
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotOperationBlockedException>(() => integration.SubmitAsync(Plan(service, Target),
                [Anime], CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("media/scrape").Count);
        });

        Test("AniBT 刮削防重：重开计划和改候选不能重复写同一目标", () =>
        {
            using var transport = ScrapeTransport();
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var first = integration.SubmitAsync(Plan(service, Target), [Anime], CancellationToken.None).GetAwaiter().GetResult();
            var second = integration.SubmitAsync(Plan(service, Target), [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(first.Single().Success);
            Assert.False(second.Single().Success);
            var sent = transport.Only("media/scrape");
            Assert.Equal("POST", sent.Method);
            Assert.Contains("media/scrape/local?media_source=bangumi&media_id=42", sent.Url);
            Assert.Equal("file", Json(sent.Body).GetProperty("type").GetString());
            Assert.Equal(Target, Json(sent.Body).GetProperty("path").GetString());
            Assert.Equal(0, transport.SentTo("transfer/manual").Count);
        });

        Test("AniBT 未知回执：连接恢复后新计划仍不能重发同一目标", () =>
        {
            using var transport = ScrapeTransport().Throw("media/scrape", new HttpRequestException("fixture failure"));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var first = integration.SubmitAsync(Plan(service, Target), [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(first.Single().Success);
            Assert.True(service.RefreshAvailabilityAsync().GetAwaiter().GetResult());
            var second = integration.SubmitAsync(Plan(service, Target), [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(second.Single().Success);
            Assert.Equal(1, transport.SentTo("media/scrape").Count);
        });

        Test("AniBT 连接代次：配置 A 切到 B 再切回 A，旧计划和资源仍失效", () =>
        {
            using var transport = BaseTransport().Answer("media/source", "[]");
            var settings = Settings();
            using var service = Service(transport, settings);
            var connection = service.CaptureAniBtConnection();
            var resource = service.AniBtResourcesAsync(Anime, [Release()], connection, CancellationToken.None)
                .GetAwaiter().GetResult().Single();
            settings.MoviePilot.Url = "192.0.2.11:3001";
            settings.MoviePilot.Url = "192.0.2.10:3001";
            Assert.True(service.RefreshAvailabilityAsync().GetAwaiter().GetResult());
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.CheckAniBtConnection(connection, CancellationToken.None));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("download/").Count);
        });

        Test("AniBT 批量中切换媒体库：保留已写回执，剩余文件不提交", () =>
        {
            const string next = "/library/Anime S01E02.mkv";
            var current = true;
            using var transport = ScrapeTransport().Sequence("storage/list",
                (HttpStatusCode.OK, FileReply(Target)), (HttpStatusCode.OK, FileReply(next)),
                (HttpStatusCode.OK, FileReply(Target))).When("media/scrape", () => current = false);
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var result = integration.SubmitAsync(Plan(service, Target, next), [Anime, Anime], CancellationToken.None, () => current)
                .GetAwaiter().GetResult();
            Assert.Equal(2, result.Count);
            Assert.True(result[0].Success);
            Assert.False(result[1].Success);
            Assert.Equal(1, transport.SentTo("media/scrape").Count);
        });
    }

    private static AniBtScrapePlan Plan(MoviePilotService service, params string[] paths) => new(
        paths.Select(path => new AniBtScrapeRow(path, MoviePilotTransfer.PathName(path),
            new([Anime], Anime, "auto", "high", "fixture", 1))).ToArray(),
        MoviePilotTransferRequest.TypeSeries, service.CaptureAniBtConnection());

    private static AniBtRelease Release() => new("rel_fixture", "匿名动画 - 01",
        new("https://anibt.net/api/torrent/rel_fixture.torrent"), new("https://anibt.net/resource/rel_fixture"), 1234);

    private static StubTransport BaseTransport() => new StubTransport().Answer("login/access-token", Session)
        .Answer("dashboard/system", """{"version":"v3.1.3"}""");

    private static StubTransport ScrapeTransport() => BaseTransport().Answer("media/source", Sources).Answer("media/42?", Media)
        .Answer("storage/list", FileReply(Target))
        .Answer("media/recognize_file", """{"meta_info":{"begin_season":1,"begin_episode":1}}""")
        .Answer("media/scrape", """{"success":true,"message":"fixture processed"}""");

    private static string FileReply(string path) => JsonSerializer.Serialize(new { success = true, data = new[] { new { storage = "local", type = "file", path } } });
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "192.0.2.10:3001";
        settings.MoviePilot.Username = "fixture-user";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(settings.MoviePilot, "fixture-password");
        return settings;
    }

    private static MoviePilotService Service(StubTransport transport, AppSettings? settings = null)
    {
        var service = new MoviePilotService(new MoviePilotClient(transport),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), settings ?? Settings());
        Assert.True(service.RefreshAvailabilityAsync().GetAwaiter().GetResult());
        return service;
    }

    /// <summary>只接 AniBT RSS 的假传输；StubTransport 固定回 JSON，XML 走这条。</summary>
    private sealed class RssTransport(string feed) : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(feed, Encoding.UTF8, "application/rss+xml")
            });
        }
    }
}

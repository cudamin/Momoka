using System.Net;
using System.Reflection;
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
    private const string Sources = """[{"name":"Bangumi","media_source":"bangumi","media_types":["电影","电视剧"]},{"name":"TheMovieDb","media_source":"themoviedb","media_types":["电影","电视剧"]}]""";
    private const string Media = """{"title":"匿名动画","media_source":"bangumi","media_id":"42","type":"电视剧"}""";
    private static readonly AniBtAnime Anime = new("ani_fixture", 42, "匿名动画", Year: 2026);

    public static void Register()
    {
        Test("AniBT 菜单：动画电影、剧、季、集仅在 MP 可用时出现", () =>
        {
            foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
            {
                var item = new EmbyItem { Type = type, Genres = ["动漫"] };
                var scrape = ItemMenu.For(item, true).Single(row => row.Command == ItemCommand.AniBtScrape);
                Assert.Equal("AniBT 识别", scrape.Label);
                Assert.DoesNotContain("重新刮削", scrape.Label);
                var versions = ItemMenu.For(item, true).Single(row => row.Command == ItemCommand.MoviePilotVersions);
                Assert.Equal("搜索不同版本", versions.Label);
                Assert.True(versions.Children!.Select(row => row.Label).SequenceEqual(new[] { "MoviePilot", "AniBT" }));
                Assert.True(versions.Children!.Select(row => row.Command!.Value)
                    .SequenceEqual(new[] { ItemCommand.MoviePilotVersions, ItemCommand.AniBtVersions }));
                var disabled = ItemMenu.For(item, true, aniBtEnabled: false);
                Assert.False(disabled.Any(row => row.Command == ItemCommand.AniBtScrape));
                var plainVersions = disabled.Single(row => row.Command == ItemCommand.MoviePilotVersions);
                Assert.Equal("搜索其他版本", plainVersions.Label);
                Assert.Null(plainVersions.Children);
                Assert.False(ItemMenu.For(item).Any(row => row.Command == ItemCommand.AniBtScrape));
                item.Genres.Clear();
                Assert.Null(ItemMenu.For(item, true).Single(row => row.Command == ItemCommand.MoviePilotVersions).Children);
                Assert.Equal(type != EmbyItemType.Movie,
                    ItemMenu.For(item, true).Any(row => row.Command == ItemCommand.AniBtScrape));
                Assert.True(ItemMenu.For(item, true, animeGenre: true).Any(row => row.Command == ItemCommand.AniBtScrape));
            }
        });

        foreach (var type in new[] { EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
        {
            Test($"AniBT 非动漫{type}：识别入口即时显示，搜索、可用性和来源限制不变", () =>
            {
                foreach (var genres in new List<string>[] { [], ["剧情"] })
                {
                    var item = new EmbyItem { Type = type, Genres = genres };
                    Assert.True(AniBtEligibility.CanIdentify(item));
                    Assert.False(AniBtEligibility.HasGenre(item));
                    foreach (var jellyfin in new[] { false, true })
                    {
                        var menu = ItemMenu.For(item, true, jellyfin: jellyfin);
                        Assert.Equal("AniBT 识别", menu.Single(row => row.Command == ItemCommand.AniBtScrape).Label);
                        Assert.Null(menu.Single(row => row.Command == ItemCommand.MoviePilotVersions).Children);
                        Assert.False(ItemMenu.For(item, jellyfin: jellyfin).Any(row => row.Command == ItemCommand.AniBtScrape));
                        Assert.False(ItemMenu.For(item, true, jellyfin: jellyfin, aniBtScrapeAvailable: false)
                            .Any(row => row.Command == ItemCommand.AniBtScrape));
                    }
                    Assert.False(ItemMenu.For(item, true, fnNas: true).Any(row => row.Command == ItemCommand.AniBtScrape));
                    Assert.False(ItemMenu.For(item, true, plex: true).Any(row => row.Command == ItemCommand.AniBtScrape));
                }
            });

            Test($"AniBT 非动漫{type}：接入既有文件识别，不先核对动漫分类且不写入", () =>
            {
                var id = type == EmbyItemType.Episode ? "episode-1" : "subject-1";
                var detail = JsonSerializer.Serialize(new
                {
                    Id = id,
                    Name = "误分类剧集",
                    Type = type.ToString(),
                    Genres = new[] { "剧情" },
                    Path = Target,
                    SeriesId = "series-1",
                    IndexNumber = 1,
                    ParentIndexNumber = 1
                });
                using var transport = BaseTransport()
                    .Answer("Users/user/Items/" + id, detail)
                    .Answer("Users/user/Items?", JsonSerializer.Serialize(new
                    {
                        Items = new[] { new { Id = "episode-1", Name = "第一集", Type = "Episode", Path = Target } },
                        TotalRecordCount = 1
                    }))
                    .Answer("history/transfer?", """{"list":[],"total":0}""");
                using var moviePilot = Service(transport);
                using var aniBtTransport = new StubTransport().Answer("api/animes/torrent-match",
                    """{"version":3,"candidates":[],"decision":{"kind":"manual","confidence":"low","reason":"fixture"}}""");
                using var client = new AniBtClient(aniBtTransport);
                var integration = new AniBtService(client, moviePilot);
                var item = new EmbyItem { Id = id, Name = "误分类剧集", Type = type, Genres = ["剧情"] };
                var plan = integration.PrepareAsync(Emby(transport), item, CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(MoviePilotTransferRequest.TypeSeries, plan.Type);
                var row = plan.Rows.Single();
                Assert.Equal(Target, row.TargetPath);
                Assert.Equal(MoviePilotTransfer.PathName(Target), row.SourceName);
                var match = aniBtTransport.SentTo("api/animes/torrent-match").Single();
                Assert.Equal(row.SourceName, Json(match.Body).GetProperty("rawTitle").GetString());
                Assert.Equal(0, transport.SentTo("Genres").Count);
                Assert.True(transport.SentTo("").All(request => request.Method == "GET"));
                Assert.True(item.Genres.SequenceEqual(["剧情"]));
            });
        }

        Test("AniBT 识别范围：非动漫电影和非媒体条目不扩大，动漫电影仍可识别", () =>
        {
            var movie = new EmbyItem { Type = EmbyItemType.Movie, Genres = ["剧情"] };
            Assert.False(AniBtEligibility.CanIdentify(movie));
            Assert.True(AniBtEligibility.CanIdentify(movie, animeGenre: true));
            movie.Genres.Add("动漫");
            Assert.True(AniBtEligibility.CanIdentify(movie));
            foreach (var type in new[] { EmbyItemType.BoxSet, EmbyItemType.CollectionFolder, EmbyItemType.Person, EmbyItemType.Video })
                Assert.False(AniBtEligibility.CanIdentify(new EmbyItem { Type = type, Genres = ["动画"] }));
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

        Test("AniBT 分组浏览用规范身份映射下载，完整集号不受预览条数限制", () =>
        {
            using var transport = BaseTransport().Answer("media/source", Sources)
                .Answer("media/123?", Media.Replace("\"42\"", "\"123\"", StringComparison.Ordinal));
            using var service = Service(transport);
            using var catalog = new StubTransport().Answer("api/public/v1/anime?bgmId=42", AniBtTests.CatalogDetail)
                .Answer("api/anime/groups?bgmId=123", AniBtTests.CatalogGroups);
            using var client = new AniBtClient(catalog);
            var integration = new AniBtService(client, service);
            var result = integration.BrowseAsync(Anime, default).GetAwaiter().GetResult();
            Assert.Equal(123L, result.Anime.BgmId);
            Assert.Equal(4, result.Groups.Single().PublishedEpisodes.Count);
            Assert.Equal(40, result.Groups.Single().TotalReleases);
            var resource = result.Resources.Single();
            Assert.Equal("123", resource.Resource.MediaId);
            Assert.Equal("rel_one", resource.Release.ReleaseId);
            Assert.Equal(resource.Release.Title, resource.Resource.Title);
            Assert.Equal("bangumi", resource.Resource.MediaSource);
            Assert.Equal(0, transport.SentTo("download/").Count);
            Assert.Equal(2, catalog.Total);
        });

        Test("AniBT 分组浏览和目录搜索在连接改变后拒绝迟到结果", () =>
        {
            foreach (var search in new[] { false, true })
            {
                var settings = Settings();
                using var transport = BaseTransport().Answer("media/source", Sources);
                using var service = Service(transport, settings);
                using var catalog = new StubTransport()
                    .Answer("api/public/v1/anime?bgmId=42", AniBtTests.CatalogDetail)
                    .Answer("api/anime/groups?bgmId=123", AniBtTests.CatalogGroups)
                    .Answer("api/animes/search?", """{"page":[],"isDone":true}""")
                    .When(search ? "api/animes/search?" : "api/anime/groups?bgmId=123", () => settings.MoviePilot.Url = "192.0.2.11:3001");
                using var client = new AniBtClient(catalog);
                var integration = new AniBtService(client, service);
                Assert.Throws<MoviePilotOperationBlockedException>(() =>
                {
                    if (search) integration.SearchCatalogAsync("动画", null, default).GetAwaiter().GetResult();
                    else integration.BrowseAsync(Anime, default).GetAwaiter().GetResult();
                });
                Assert.Equal(0, transport.SentTo("media/source").Count);
            }
        });

        Test("AniBT 分组无种子发布仍可浏览但不可交给 MoviePilot 下载", () =>
        {
            using var transport = BaseTransport().Answer("media/source", "[]");
            using var service = Service(transport);
            using var catalog = new StubTransport().Answer("api/public/v1/anime?bgmId=42", AniBtTests.CatalogDetail)
                .Answer("api/anime/groups?bgmId=123", AniBtTests.CatalogGroups.Replace("fixture.torrent", "", StringComparison.Ordinal));
            using var client = new AniBtClient(catalog);
            var integration = new AniBtService(client, service);
            var result = integration.BrowseAsync(Anime, default).GetAwaiter().GetResult();
            var item = result.Resources.Single();
            Assert.False(item.Release.HasTorrent);
            Assert.NotNull(item.Resource.DetailsUri);
            Assert.Equal("此发布未提供种子文件", item.Resource.DownloadProblem);
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(item.Resource, default).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("download/").Count);
        });

        Test("AniBT 候选：未返回的选择不能触发任何重新整理准备", () =>
        {
            using var transport = BaseTransport();
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotOperationBlockedException>(() => SubmitConfirmed(integration, Emby(transport),
                Plan(service, (Target, History(service, 7, Target))), [Anime with { BgmId = 99 }],
                CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("media/source").Count);
            Assert.Equal(0, transport.SentTo("transfer/manual").Count);
        });

        Test("AniBT 确认：按原整理记录预览并调用 MP 重新整理", () =>
        {
            using var transport = TransferTransport(Target, Target);
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var result = SubmitConfirmed(integration, Emby(transport), Plan(service, (Target, History(service, 7, Target))),
                [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(result.Single().Success);
            Assert.Contains("已重新整理到", result.Single().Message);
            Assert.Contains("扫描媒体库", result.Single().Message);
            var scan = transport.Only("Library/Refresh");
            Assert.Equal("POST", scan.Method);
            var calls = transport.SentTo("transfer/manual?");
            Assert.Equal(2, calls.Count);
            var preview = Json(calls[0].Body);
            Assert.True(preview.GetProperty("preview").GetBoolean());
            Assert.Equal(7, preview.GetProperty("logid").GetInt64());
            Assert.Equal("bangumi", preview.GetProperty("media_source").GetString());
            Assert.Equal("42", preview.GetProperty("media_id").GetString());
            Assert.Equal(MoviePilotTransferRequest.TypeSeries, preview.GetProperty("type_name").GetString());
            Assert.True(preview.GetProperty("reorganize").GetBoolean());
            Assert.False(preview.GetProperty("from_history").GetBoolean());
            Assert.False(preview.GetProperty("skip_success").GetBoolean());
            Assert.Equal("/library/匿名动画 (2026)", preview.GetProperty("target_path").GetString());
            Assert.Equal("move", preview.GetProperty("transfer_type").GetString());
            Assert.True(preview.GetProperty("scrape").GetBoolean());
            Assert.Equal(1, preview.GetProperty("season").GetInt32());
            Assert.Equal("1", preview.GetProperty("episode_detail").GetString());
            var submit = Json(calls[1].Body);
            Assert.False(submit.GetProperty("preview").GetBoolean());
            Assert.Contains("background=false", calls[1].Url);
            Assert.Equal(0, transport.SentTo("media/scrape").Count);
        });

        Test("AniBT 药屋少女：自动目标保留 S01E52，单次确认正确重新整理", () =>
        {
            const string sourceName = "[Nekomoe kissaten&LoliHouse] Kusuriya no Hitorigoto - 52 [WebRip 1080p HEVC-10bit AAC ASSx2].mkv";
            const string oldPath = "/library/文化圈 (1990)/Season 01/文化圈 S01E52 1080p.mkv";
            const string newPath = "/library/药屋少女的呢喃 (2023)/Season 01/药屋少女的呢喃 S01E52 1080p.mkv";
            var anime = new AniBtAnime("apothecary-fixture", 420628, "药屋少女的呢喃", "薬屋のひとりごと", Year: 2023, EpisodeCount: 24);
            using var transport = TransferTransport(oldPath)
                .Answer("media/420628?", """{"title":"药屋少女的呢喃","media_source":"bangumi","media_id":"420628","type":"电视剧"}""")
                .Answer("history/transfer", HistoriesJson((7, oldPath)))
                .Answer("storage/list", FileReply(oldPath))
                .Sequence("transfer/manual?",
                    (HttpStatusCode.OK, PreviewReply(oldPath, newPath)),
                    (HttpStatusCode.OK, SubmitReply(oldPath, newPath)));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var row = new AniBtScrapeRow(oldPath, sourceName, new([anime], null, "manual", "medium", "low_evidence", null), History(service, 7, oldPath));
            // 整剧入口没有整体季集，也必须按每个文件自己的编号准备。
            var plan = new AniBtScrapePlan([row], MoviePilotTransferRequest.TypeSeries,
                service.CaptureAniBtConnection(), IdentifyItem(), null, "");
            var prepared = integration.PrepareForConfirmationAsync(Emby(transport), plan, [anime], default).GetAwaiter().GetResult();
            Assert.Equal("药屋少女的呢喃 S01E52", prepared.Rows.Single().Heading);
            Assert.Contains(newPath, prepared.Rows.Single().Details);
            Assert.Equal(1, transport.SentTo("transfer/manual?").Count);
            var result = integration.SubmitAsync(Emby(transport), prepared, default).GetAwaiter().GetResult();
            Assert.True(result.Single().Success);
            Assert.Contains(newPath, result.Single().Message);
            var calls = transport.SentTo("transfer/manual?");
            Assert.Equal(2, calls.Count);
            foreach (var call in calls)
            {
                var body = Json(call.Body);
                Assert.Equal(1, body.GetProperty("season").GetInt32());
                Assert.Equal("52", body.GetProperty("episode_detail").GetString());
                Assert.Equal("420628", body.GetProperty("media_id").GetString());
                Assert.False(body.GetProperty("from_history").GetBoolean());
            }
            Assert.True(Json(calls[0].Body).GetProperty("preview").GetBoolean());
            Assert.False(Json(calls[1].Body).GetProperty("preview").GetBoolean());
        });

        Test("AniBT 同作品新一季：整理身份沿用主剧集 TMDB 编号，季集保留", () =>
        {
            const string oldPath = "/library/青之箱 第二季/Season 02/青之箱 第二季 S02E03.mkv";
            const string mainTarget = "/library/青之箱 (2024)/Season 02/青之箱 S02E03.mkv";
            const string mainReply = """
                {"success":true,"data":{"target_storage":"local","target_path":"/library/青之箱 (2024)",
                 "transfer_type":"softlink","scrape":true,"library_type_folder":false,"library_category_folder":false}}
                """;
            var anime = new AniBtAnime("ao-fixture", 548156, "青之箱 第二季", "アオのハコ Season 2", Year: 2026, EpisodeCount: 12);
            using var transport = BaseTransport()
                .Answer("media/source", Sources)
                .Answer("history/transfer", HistoriesJson((7, oldPath)))
                .Answer("transfer/manual/target-path", mainReply)
                .Answer("storage/list", FileReply(oldPath))
                .Sequence("transfer/manual?", (HttpStatusCode.OK, PreviewReply(oldPath, mainTarget)));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var row = new AniBtScrapeRow(oldPath, "青之箱 - 03.mkv",
                new([anime], anime, "auto", "high", "fixture", 2), History(service, 7, oldPath));
            // 库内主剧集是「青之箱」，这一集属于第二季；整理身份应是主剧集的 TMDB 207347，而不是 Bangumi 548156。
            var plan = new AniBtScrapePlan([row], MoviePilotTransferRequest.TypeSeries,
                service.CaptureAniBtConnection(), IdentifyItem(), 2, "3", "青之箱", "207347");
            var prepared = integration.PrepareForConfirmationAsync(Emby(transport), plan, [anime], default).GetAwaiter().GetResult();
            Assert.Equal("青之箱 S02E03", prepared.Rows.Single().Heading);
            Assert.Contains("沿用主剧集", prepared.Rows.Single().Method);
            Assert.Contains("自动刮削", prepared.Rows.Single().Method);
            var preview = Json(transport.SentTo("transfer/manual?").Single().Body);
            Assert.Equal("themoviedb", preview.GetProperty("media_source").GetString());
            Assert.Equal("207347", preview.GetProperty("media_id").GetString());
            Assert.Equal(2, preview.GetProperty("season").GetInt32());
            Assert.Equal("3", preview.GetProperty("episode_detail").GetString());
            Assert.True(preview.GetProperty("scrape").GetBoolean());
            Assert.Equal(0, transport.SentTo("media/548156?").Count);
            Assert.Equal(0, transport.SentTo("media/207347?").Count);
        });

        Test("AniBT 不同作品：不沿用主剧集，仍按所选 Bangumi 身份整理", () =>
        {
            const string oldPath = "/library/文化圈 (1990)/Season 01/文化圈 S01E52 1080p.mkv";
            const string newPath = "/library/药屋少女的呢喃 (2023)/Season 01/药屋少女的呢喃 S01E52 1080p.mkv";
            var anime = new AniBtAnime("apothecary-fixture", 420628, "药屋少女的呢喃", "薬屋のひとりごと", Year: 2023, EpisodeCount: 24);
            using var transport = BaseTransport()
                .Answer("media/source", Sources)
                .Answer("media/237029?", """{"title":"文化圈","original_title":"文化圈","names":["文化圈"],"media_source":"themoviedb","media_id":"237029","type":"电视剧"}""")
                .Answer("media/420628?", """{"title":"药屋少女的呢喃","media_source":"bangumi","media_id":"420628","type":"电视剧"}""")
                .Answer("history/transfer", HistoriesJson((7, oldPath)))
                .Answer("transfer/manual/target-path", TargetReply)
                .Answer("storage/list", FileReply(oldPath))
                .Sequence("transfer/manual?", (HttpStatusCode.OK, PreviewReply(oldPath, newPath)));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var row = new AniBtScrapeRow(oldPath, "Kusuriya no Hitorigoto - 52.mkv",
                new([anime], anime, "manual", "medium", "low_evidence", 1), History(service, 7, oldPath));
            var plan = new AniBtScrapePlan([row], MoviePilotTransferRequest.TypeSeries,
                service.CaptureAniBtConnection(), IdentifyItem(), 1, "52", "文化圈", "237029");
            var prepared = integration.PrepareForConfirmationAsync(Emby(transport), plan, [anime], default).GetAwaiter().GetResult();
            Assert.Contains("Bangumi 420628", prepared.Rows.Single().Method);
            var preview = Json(transport.SentTo("transfer/manual?").Single().Body);
            Assert.Equal("bangumi", preview.GetProperty("media_source").GetString());
            Assert.Equal("420628", preview.GetProperty("media_id").GetString());
            Assert.Equal(1, transport.SentTo("media/237029?").Count);
            Assert.Equal(1, transport.SentTo("media/420628?").Count);
        });

        Test("AniBT 空媒体详情：仅一次只读重查，不把空回话当错误身份", () =>
        {
            using var transport = TransferTransport(Target)
                .Sequence("media/42?", (HttpStatusCode.OK, """{"title":null,"media_source":null,"media_id":null,"type":null}"""), (HttpStatusCode.OK, Media));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var prepared = integration.PrepareForConfirmationAsync(Emby(transport), Plan(service, (Target, History(service, 7, Target))), [Anime], default).GetAwaiter().GetResult();
            Assert.True(prepared.CanSubmit);
            Assert.Equal(2, transport.SentTo("media/42?").Count);
            Assert.True(transport.SentTo("transfer/manual?").All(call => Json(call.Body).GetProperty("preview").GetBoolean()));
        });

        Test("AniBT 空媒体详情持续失败：重查有上限且零整理", () =>
        {
            using var transport = TransferTransport(Target).Answer("media/42?", "{}");
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotException>(() => integration.PrepareForConfirmationAsync(Emby(transport),
                Plan(service, (Target, History(service, 7, Target))), [Anime], default).GetAwaiter().GetResult());
            Assert.Equal(2, transport.SentTo("media/42?").Count);
            Assert.Equal(0, transport.SentTo("transfer/manual?").Count);
        });

        Test("AniBT 错误媒体身份：不重读、不放宽来源校验、零整理", () =>
        {
            using var transport = TransferTransport(Target).Answer("media/42?", Media.Replace("bangumi", "themoviedb", StringComparison.Ordinal));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotException>(() => integration.PrepareForConfirmationAsync(Emby(transport),
                Plan(service, (Target, History(service, 7, Target))), [Anime], default).GetAwaiter().GetResult());
            Assert.Equal(1, transport.SentTo("media/42?").Count);
            Assert.Equal(0, transport.SentTo("transfer/manual?").Count);
        });

        Test("AniBT 季集优先现有元数据，缺失才按文件与历史；编码不冒充集号", () =>
        {
            var row = new AniBtScrapeRow("/library/片名 S01E52.mkv", "片名 - 52 [1080p HEVC-10bit AAC ASSx2].mkv", new([], null, "manual", "", "", 3));
            Assert.Equal("S01E52", AniBtScrapeNumbering.Resolve(row, null, "").Label);
            Assert.Equal("S02E04", AniBtScrapeNumbering.Resolve(row, 2, "4").Label);
            Assert.Equal("S01E52", AniBtScrapeNumbering.Resolve(row with { TargetPath = "/library/无编号.mkv" }, 1, "").Label);
            Assert.Equal("", AniBtScrapeNumbering.Resolve(row with { TargetPath = "/library/无编号.mkv", SourceName = "片名 [1080p HEVC-10bit AAC ASSx2].mkv" }, 1, "").Label);
            Assert.Equal("S00E01", AniBtScrapeNumbering.Resolve(row with { TargetPath = "/library/片名 S00E01.mkv" }, null, "").Label);
        });

        Test("AniBT 重新整理成功但 Emby 扫描失败：整理仍算成功并提示手动扫描", () =>
        {
            using var transport = TransferTransport(Target).Fail("Library/Refresh", HttpStatusCode.Forbidden);
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var result = SubmitConfirmed(integration, Emby(transport), Plan(service, (Target, History(service, 7, Target))),
                [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(result.Single().Success);
            Assert.Contains("扫描请求失败", result.Single().Message);
            Assert.Contains("手动扫描媒体库", result.Single().Message);
        });

        Test("AniBT 批量：第二个目标不是精确文件时，第一集也不能先写", () =>
        {
            const string next = "/library/Anime S01E02.mkv";
            using var transport = BaseTransport().Answer("media/source", Sources).Answer("media/42?", Media)
                .Answer("transfer/manual/target-path", TargetReply)
                .Answer("history/transfer", HistoriesJson((7, Target), (8, next)))
                .Sequence("storage/list", (HttpStatusCode.OK, FileReply(Target)),
                    (HttpStatusCode.OK, """{"success":true,"data":[{"storage":"local","type":"dir","path":"/library/Anime S01E02.mkv"}]}"""))
                .Sequence("transfer/manual?", (HttpStatusCode.OK, PreviewReply(Target)));
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotException>(() => SubmitConfirmed(integration, Emby(transport),
                Plan(service, (Target, History(service, 7, Target)), (next, History(service, 8, next))),
                [Anime, Anime], CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(2, transport.SentTo("storage/list").Count);
            Assert.Equal(1, transport.SentTo("transfer/manual?").Count(response => Json(response.Body).GetProperty("preview").GetBoolean()));
            Assert.Equal(0, transport.SentTo("transfer/manual?").Count(response => !Json(response.Body).GetProperty("preview").GetBoolean()));
        });

        Test("AniBT 没有原整理记录：展示实际计划并确认后用 Emby 识别", () =>
        {
            using var transport = BaseTransport()
                .Answer("Items/RemoteSearch/Series",
                    """[{"Name":"匿名动画","ProductionYear":2026,"ProviderIds":{"Tmdb":"999"},"SearchProviderName":"TheMovieDb"}]""")
                .Answer("RemoteSearch/Apply", "")
                .Answer("Items/series-1/Refresh", "");
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var result = SubmitConfirmed(integration, Emby(transport), Plan(service, (Target, null)),
                [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(result.Single().Success);
            Assert.Contains("Emby 识别", result.Single().Message);
            var search = transport.Only("RemoteSearch/Series");
            Assert.Equal("POST", search.Method);
            var query = Json(search.Body).GetProperty("SearchInfo");
            Assert.Equal("匿名动画", query.GetProperty("Name").GetString());
            Assert.Equal("series-1", Json(search.Body).GetProperty("ItemId").GetString());
            Assert.Contains("ReplaceAllImages=true", transport.Only("RemoteSearch/Apply").Url);
            var refresh = transport.Only("Items/series-1/Refresh");
            Assert.Contains("ReplaceAllMetadata=true", refresh.Url);
            Assert.Contains("ReplaceAllImages=true", refresh.Url);
            Assert.Equal(0, transport.SentTo("transfer/manual").Count);
        });

        Test("AniBT 没有原整理记录：Emby 也找不到时停止，不写任何文件", () =>
        {
            using var transport = BaseTransport().Answer("Items/RemoteSearch/Series", "[]");
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            Assert.Throws<MoviePilotOperationBlockedException>(() => SubmitConfirmed(integration, Emby(transport),
                Plan(service, (Target, null)), [Anime], CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("RemoteSearch/Apply").Count);
            Assert.Equal(0, transport.SentTo("transfer/manual").Count);
        });

        Test("AniBT 多个文件识别成不同番剧：Emby 识别只落一部，拒绝整批", () =>
        {
            using var transport = BaseTransport();
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var plan = new AniBtScrapePlan(
                [
                    new(Target, MoviePilotTransfer.PathName(Target), Multiple, null),
                    new("/library/Anime S01E02.mkv", "Anime S01E02.mkv", Multiple, null)
                ], MoviePilotTransferRequest.TypeSeries, service.CaptureAniBtConnection(),
                IdentifyItem(), 1, "");
            Assert.Throws<MoviePilotOperationBlockedException>(() => SubmitConfirmed(integration, Emby(transport), plan,
                [Anime, OtherAnime], CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("RemoteSearch").Count);
        });

        Test("AniBT 重新整理防重：重开计划不能重复提交同一条原记录", () =>
        {
            using var transport = TransferTransport(Target, Target, Target);
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var first = SubmitConfirmed(integration, Emby(transport), Plan(service, (Target, History(service, 7, Target))),
                [Anime], CancellationToken.None).GetAwaiter().GetResult();
            var second = SubmitConfirmed(integration, Emby(transport), Plan(service, (Target, History(service, 7, Target))),
                [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(first.Single().Success);
            Assert.False(second.Single().Success);
            var calls = transport.SentTo("transfer/manual?");
            Assert.Equal(3, calls.Count);
            Assert.Equal(1, calls.Count(response => !Json(response.Body).GetProperty("preview").GetBoolean()));
        });

        Test("AniBT 未知回执：连接恢复后新计划仍不能重发同一原记录", () =>
        {
            var manualCalls = 0;
            using var transport = TransferTransport(Target, Target, Target)
                .When("transfer/manual?", () => { if (++manualCalls == 2) throw new HttpRequestException("fixture failure"); });
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var first = SubmitConfirmed(integration, Emby(transport), Plan(service, (Target, History(service, 7, Target))),
                [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(first.Single().Success);
            InstallAnonymousSession(service);
            Assert.True(service.Available);
            var second = SubmitConfirmed(integration, Emby(transport), Plan(service, (Target, History(service, 7, Target))),
                [Anime], CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(second.Single().Success);
            Assert.Equal(3, transport.SentTo("transfer/manual?").Count);
            Assert.Equal(1, transport.SentTo("transfer/manual?").Count(response => !Json(response.Body).GetProperty("preview").GetBoolean()));
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
            InstallAnonymousSession(service);
            Assert.True(service.Available);
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.CheckAniBtConnection(connection, CancellationToken.None));
            Assert.Throws<MoviePilotOperationBlockedException>(() => service.DownloadAsync(resource, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.SentTo("download/").Count);
        });

        Test("AniBT 批量中切换媒体库：保留已写回执，剩余文件不提交", () =>
        {
            const string next = "/library/Anime S01E02.mkv";
            var current = true;
            int manualCalls = 0;
            using var transport = BaseTransport().Answer("media/source", Sources).Answer("media/42?", Media)
                .Answer("transfer/manual/target-path", TargetReply)
                .Answer("history/transfer", HistoriesJson((7, Target), (8, next)))
                .Answer("Library/Refresh", "")
                .Sequence("storage/list", (HttpStatusCode.OK, FileReply(Target)), (HttpStatusCode.OK, FileReply(next)))
                .Sequence("transfer/manual?",
                    (HttpStatusCode.OK, PreviewReply(Target)), (HttpStatusCode.OK, PreviewReply(next)),
                    (HttpStatusCode.OK, SubmitReply(Target)), (HttpStatusCode.OK, SubmitReply(next)))
                .When("transfer/manual?", () => { if (++manualCalls == 3) current = false; });
            using var service = Service(transport);
            using var client = new AniBtClient(new StubTransport());
            var integration = new AniBtService(client, service);
            var result = SubmitConfirmed(integration, Emby(transport),
                Plan(service, (Target, History(service, 7, Target)), (next, History(service, 8, next))),
                [Anime, Anime], CancellationToken.None, () => current).GetAwaiter().GetResult();
            Assert.Equal(2, result.Count);
            Assert.True(result[0].Success);
            Assert.False(result[1].Success);
            Assert.Equal(1, transport.SentTo("transfer/manual?").Count(response => !Json(response.Body).GetProperty("preview").GetBoolean()));
        });
    }

    private const string TransferTarget = "/library/匿名动画 (2026)/Season 01/匿名动画 S01E01.mkv";
    private const string HistorySource = "/downloads/[Group] Original - 01.mkv";
    private const string TargetReply = """
        {"success":true,"data":{"target_storage":"local","target_path":"/library/匿名动画 (2026)",
         "transfer_type":"move","scrape":true,"library_type_folder":false,"library_category_folder":false}}
        """;
    private static readonly AniBtAnime OtherAnime = new("ani_other", 43, "另一部匿名动画");
    private static readonly AniBtMatch Multiple = new([Anime, OtherAnime], null, "manual", "low", "fixture", 1);

    private static AniBtScrapePlan Plan(MoviePilotService service,
        params (string Path, MoviePilotTransferHistory? History)[] rows) => new(
        rows.Select(row => new AniBtScrapeRow(row.Path, MoviePilotTransfer.PathName(row.Path),
            new([Anime], Anime, "auto", "high", "fixture", 1), row.History)).ToArray(),
        MoviePilotTransferRequest.TypeSeries, service.CaptureAniBtConnection(), IdentifyItem(), 1, "1");

    private static EmbyItem IdentifyItem() => new()
    { Id = "episode-1", Name = "第一集", Type = EmbyItemType.Episode, SeriesId = "series-1" };

    private static MoviePilotTransferHistory History(MoviePilotService service, long id, string target)
    {
        var record = MoviePilotTransferHistory.Parse(Json(HistoriesJson((id, target))).GetProperty("list")[0])!;
        return record with { ConnectionStamp = service.ConnectionStamp };
    }

    private static string HistoriesJson(params (long Id, string Target)[] records) => JsonSerializer.Serialize(new
    {
        list = records.Select(record => new
        {
            id = record.Id,
            title = "匿名动画",
            type = MoviePilotTransferRequest.TypeSeries,
            media_source = "bangumi",
            media_id = "42",
            seasons = "S01",
            episodes = "E01",
            mode = "move",
            status = true,
            date = "2026-10-01 12:00:00",
            src_fileitem = new { storage = "local", type = "file", path = HistorySource },
            dest_fileitem = new { storage = "local", type = "file", path = record.Target }
        }).ToArray(),
        total = records.Length
    });

    private static StubTransport TransferTransport(params string[] sources)
    {
        var answers = new List<(HttpStatusCode, string)>();
        foreach (var source in sources)
        {
            answers.Add((HttpStatusCode.OK, PreviewReply(source)));
            answers.Add((HttpStatusCode.OK, SubmitReply(source)));
        }
        return BaseTransport().Answer("media/source", Sources).Answer("media/42?", Media)
            .Answer("history/transfer", HistoriesJson((7, Target)))
            .Answer("transfer/manual/target-path", TargetReply)
            .Answer("storage/list", FileReply(Target))
            .Answer("Library/Refresh", "")
            .Sequence("transfer/manual?", [.. answers]);
    }

    private static string PreviewReply(string source, string? target = null) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            items = new[] { new { source, target = target ?? TransferTarget, title = "匿名动画", success = true, state = "preview" } },
            message = ""
        }
    });

    private static string SubmitReply(string source, string? target = null) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            items = new[] { new { source, target = target ?? TransferTarget, title = "匿名动画", success = true, state = "completed" } },
            message = ""
        }
    });

    private static AniBtRelease Release() => new("rel_fixture", "匿名动画 - 01",
        new("https://anibt.net/api/torrent/rel_fixture.torrent"), new("https://anibt.net/resource/rel_fixture"), 1234);

    private static StubTransport BaseTransport() => new StubTransport()
        .Answer("Users/user/Items/series-1", """{"Id":"series-1","Type":"Series","Name":"匿名库内整剧","ProductionYear":2024}""");

    private static EmbyClient Emby(StubTransport transport) => new(
        new EmbyHttp(transport),
        new EmbyConnection(new Uri("http://192.0.2.10:8096/"), "fixture-token", "user", "fixture-user", "Fixture",
            DeviceIdentity.Create("fixture-device", "1.0")));

    private static string FileReply(string path) => JsonSerializer.Serialize(new { success = true, data = new[] { new { storage = "local", type = "file", path } } });
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "192.0.2.10:3001";
        settings.MoviePilot.Username = "fixture-user";
        settings.MoviePilot.ProtectedPassword = "non-secret-anonymous-fixture";
        return settings;
    }

    private static MoviePilotService Service(StubTransport transport, AppSettings? settings = null)
    {
        var service = new MoviePilotService(new MoviePilotClient(transport),
            new MoviePilotCredentials(new RejectSecretAccess()), settings ?? Settings());
        InstallAnonymousSession(service);
        Assert.True(service.Available);
        return service;
    }

    // A test's explicit confirmation boundary. It executes both actual production phases;
    // Momoka.AniBtConfirmationTests separately asserts zero writes between them. No compatibility
    // write overload survives in the product.
    private static async Task<IReadOnlyList<AniBtScrapeResult>> SubmitConfirmed(AniBtService service, EmbyClient emby,
        AniBtScrapePlan plan, IReadOnlyList<AniBtAnime> choices, CancellationToken cancellationToken, Func<bool>? canContinue = null)
    {
        var prepared = await service.PrepareForConfirmationAsync(emby, plan, choices, cancellationToken, canContinue);
        Assert.Equal(plan.Rows.Count, prepared.Rows.Count);
        return await service.SubmitAsync(emby, prepared, cancellationToken, canContinue);
    }

    private static void InstallAnonymousSession(MoviePilotService service)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(service, null)!;
        var type = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var cached = Activator.CreateInstance(type, flags | BindingFlags.Public, null,
            [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "", "fixture-user", true)], null);
        typeof(MoviePilotService).GetField("_session", flags)!.SetValue(service, cached);
        typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(service, identity);
    }

    private sealed class RejectSecretAccess : ISecretProtector
    {
        public string Protect(string value) => throw new AssertionException("AniBT 夹具禁止保护凭据");
        public string Unprotect(string value) => throw new AssertionException("AniBT 夹具禁止解密或登录");
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

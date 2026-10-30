using System.Net;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotSeriesTransferTests
{
    private const string Session = """{"access_token":"fixture-token","token_type":"bearer","super_user":true,"user_name":"fixture"}""";

    public static void Register()
    {
        Test("剧重新整理：TMDB 筛选语法固定且拒绝缺失或注入编号", () =>
        {
            Assert.Equal("tmdbid=283938", MoviePilotSeriesTransfer.Query(" 0283938 "));
            foreach (var invalid in new[] { "", "0", "-1", "tmdbid=283938", "283938&title=other", "abc", "9223372036854775808" })
                Assert.Catch<MoviePilotException>(() => MoviePilotSeriesTransfer.Query(invalid));
        });
        Test("剧重新整理：同名电影、同号异来源及冲突 TMDB 身份不混入", () =>
        {
            var history = History(1);
            Assert.True(MoviePilotSeriesTransfer.Matches(history, "283938"));
            Assert.False(MoviePilotSeriesTransfer.Matches(history with { Type = "电影" }, "283938"));
            Assert.False(MoviePilotSeriesTransfer.Matches(history with { TmdbId = "999" }, "283938"));
            Assert.False(MoviePilotSeriesTransfer.Matches(history with { MediaId = "999" }, "283938"));
            Assert.False(MoviePilotSeriesTransfer.Matches(history with { MediaSource = "douban", TmdbId = "" }, "283938"));
            Assert.False(MoviePilotSeriesTransfer.Matches(history, ""));
        });
        Test("剧重新整理：跨来源只认独立 TMDB 交叉编号，兼容旧 TMDB 主身份", () =>
        {
            var history = History(1);
            Assert.True(MoviePilotSeriesTransfer.Matches(history with { MediaSource = "douban", MediaId = "30000001" }, "283938"));
            Assert.True(MoviePilotSeriesTransfer.Matches(history with { TmdbId = "" }, "283938"));
            Assert.True(MoviePilotSeriesTransfer.Matches(history with { MediaSource = "", MediaId = "" }, "283938"));
            Assert.False(MoviePilotSeriesTransfer.Matches(history with { MediaSource = "", TmdbId = "", MediaId = "283938" }, "283938"));
        });
        Test("剧重新整理：网页记录字段解析保留路径、存储、海报和字节大小", () =>
        {
            var history = MoviePilotTransferHistory.Parse(Json("""{"id":"12","type":"电视剧","title":"测试剧","tmdbid":283938,"media_source":"themoviedb","media_id":"283938","image":"https://image.invalid/poster.jpg","filesize":375000001,"src_storage":"local","dest_storage":"rclone","src":"/downloads/original.mkv","dest":"/library/episode.mkv","src_fileitem":{"storage":"local","type":"file","path":"/downloads/source.mkv"},"dest_fileitem":{"storage":"rclone","type":"file","path":"remote:/episode.mkv"}}"""))!;
            Assert.Equal("283938", history.TmdbId);
            Assert.Equal(375000001L, history.Size);
            Assert.Equal("local", history.SourceStorage);
            Assert.Equal("rclone", history.TargetStorage);
            Assert.Equal("/downloads/source.mkv", history.SourcePath);
            Assert.Equal("remote:/episode.mkv", history.TargetPath);
            Assert.Equal("https://image.invalid/poster.jpg", history.Image);
        });
        Test("剧重新整理：缺失或无效大小不伪造零字节，文件存储可从 FileItem 回退", () =>
        {
            foreach (var value in new[] { "null", "-1", "\"bad\"" })
            {
                var history = MoviePilotTransferHistory.Parse(Json("{\"id\":1,\"filesize\":" + value + ",\"src_fileitem\":{\"storage\":\"local\"},\"dest_fileitem\":{\"storage\":\"rclone\"}}"))!;
                Assert.Null(history.Size);
                Assert.Equal("local", history.SourceStorage);
                Assert.Equal("rclone", history.TargetStorage);
            }
            Assert.Equal(0L, MoviePilotTransferHistory.Parse(Json("""{"id":1,"filesize":"0"}"""))!.Size);
        });
        Test("剧重新整理：v3.2.1 的大小来自原 FileItem，主 TMDB 身份不依赖旧字段", () =>
        {
            var history = MoviePilotTransferHistory.Parse(Json("""{"id":6,"type":"电视剧","title":"测试剧","year":"2026","media_source":"themoviedb","media_id":"283938","src_storage":"local","dest_storage":"local","src_fileitem":{"storage":"local","type":"file","path":"/downloads/source.mkv","size":374980214},"dest_fileitem":{"storage":"local","type":"file","path":"/library/episode.mkv","size":1}}"""))!;
            Assert.Equal(374980214L, history.Size);
            Assert.Equal("2026", history.Year);
            Assert.True(MoviePilotSeriesTransfer.Matches(history, "283938"));
            var targetOnly = MoviePilotTransferHistory.Parse(Json("""{"id":7,"dest_fileitem":{"size":"123456"}}"""))!;
            Assert.Equal(123456L, targetOnly.Size);
        });
        Test("剧重新整理：只收剧身份，不读取或截断媒体库所有单集", () =>
        {
            using var transport = new StubTransport().Answer("Items/series", """{"Id":"series","Type":"Series","Name":"测试剧","Path":"/library/series","ProviderIds":{"Tmdb":"283938"}}""");
            using var http = new EmbyHttp(transport);
            var client = new EmbyClient(http, new EmbyConnection(new Uri("http://192.0.2.10:8096/"),
                "fixture-token", "fixture-user", "fixture", "fixture", DeviceIdentity.Create("fixture-device", "1.0")));
            var context = MoviePilotTransferCollect.CollectAsync(client,
                new EmbyItem { Id = "series", Type = EmbyItemType.Series }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(context.BrowseSeriesHistory);
            Assert.Equal("283938", context.MediaId);
            Assert.Equal("测试剧", context.Title);
            Assert.Equal(0, context.Files.Count);
            Assert.Null(context.Season);
            Assert.Equal("", context.Episode);
            Assert.Equal(1, transport.Total);
        });
        Test("剧重新整理：多选仅带勾选历史的真正源，不沿用媒体库文件或季集覆盖", () =>
        {
            var first = History(2);
            var moved = History(4) with { Mode = "move", Success = true };
            var original = Context() with { Files = [new("unselected", "/library/unselected.mkv")], Season = 4, Episode = "9" };
            var context = MoviePilotSeriesTransfer.CreateContext(original, [first, moved]);
            Assert.False(context.BrowseSeriesHistory);
            Assert.Equal(2, context.Files.Count);
            Assert.Equal(first.SourcePath, context.Files[0].Path);
            Assert.Equal(moved.TargetPath, context.Files[1].Path);
            Assert.Equal("283938", context.MediaId);
            Assert.Null(context.Season);
            Assert.Equal("", context.Episode);
            var request = Request(context, [first, moved]);
            var body = request.Body([first.TransferFile, moved.TransferFile], preview: true);
            Assert.Equal("2,4", string.Join(",", (long[])body["logids"]!));
            Assert.False(body.ContainsKey("fileitems"));
            Assert.False(body.ContainsKey("season"));
            Assert.False(body.ContainsKey("episode_detail"));
            Assert.Equal(true, body["preview"]);
        });
        Test("剧重新整理：单选仍用 logid，多集各自保留识别", () =>
        {
            var history = History(3);
            var context = MoviePilotSeriesTransfer.CreateContext(Context(), [history]);
            var body = Request(context, [history]).Body([history.TransferFile], preview: true);
            Assert.Equal(3L, body["logid"]);
            Assert.False(body.ContainsKey("logids"));
            Assert.Equal("283938", body["media_id"]);
        });
        Test("剧重新整理：超过旧 30 个媒体库文件限制仍可显式选择完整批次", () =>
        {
            var histories = Enumerable.Range(1, 35).Select(id => History(id)).ToList();
            var context = MoviePilotSeriesTransfer.CreateContext(Context(), histories);
            Assert.Equal(35, context.Files.Count);
            var body = Request(context, histories).Body(histories.Select(history => history.TransferFile).ToList(), preview: true);
            Assert.Equal(35, ((long[])body["logids"]!).Length);
        });
        Test("剧重新整理：空选择、错页面、另一部剧及重复历史整批拒绝", () =>
        {
            var history = History(1);
            Assert.Catch<MoviePilotException>(() => MoviePilotSeriesTransfer.CreateContext(Context(), []));
            Assert.Catch<MoviePilotException>(() => MoviePilotSeriesTransfer.CreateContext(Context() with { BrowseSeriesHistory = false }, [history]));
            Assert.Catch<MoviePilotException>(() => MoviePilotSeriesTransfer.CreateContext(Context(), [history, history]));
            Assert.Catch<MoviePilotException>(() => MoviePilotSeriesTransfer.CreateContext(Context(), [history, History(2) with { TmdbId = "999" }]));
        });
        Test("剧重新整理：相同源路径、无源文件、目录及非本地存储不能扩大范围", () =>
        {
            var first = History(1);
            Assert.Catch<MoviePilotException>(() => MoviePilotSeriesTransfer.CreateContext(Context(), [first, first with { Id = 2 }]));
            foreach (var file in new[] { Json("{}"), Json("""{"storage":"local","type":"dir","path":"/downloads/series"}"""),
                         Json("""{"storage":"rclone","type":"file","path":"remote:/source.mkv"}""") })
                Assert.Catch<MoviePilotException>(() => MoviePilotSeriesTransfer.CreateContext(Context(), [first with { SourceFile = file }]));
        });
        Test("剧重新整理：分页传自动 TMDB 查询，收齐后本地排除身份冲突记录", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session)
                .Sequence("history/transfer", (HttpStatusCode.OK, Page(4, History(1), History(2))),
                    (HttpStatusCode.OK, Page(4, History(3) with { TmdbId = "999" }, History(4) with { MediaSource = "douban", MediaId = "30000001" })));
            using var client = new MoviePilotClient(transport);
            using var service = Service(client);
            var histories = service.FindSeriesTransferHistoriesAsync("283938", CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("1,2,4", string.Join(",", histories.Select(history => history.Id)));
            Assert.Equal(2, transport.Count("history/transfer"));
            foreach (var sent in transport.SentTo("history/transfer"))
            {
                Assert.Equal("GET", sent.Method);
                Assert.Contains("title=tmdbid%3D283938", sent.Url);
                Assert.Contains("count=100", sent.Url);
            }
            Assert.Contains("page=2", transport.SentTo("history/transfer")[1].Url);
            Assert.Equal(0, transport.Count("transfer/manual"));
        });
        Test("剧重新整理：没有 TMDB 编号时零网络请求，空历史正常返回零资源", () =>
        {
            using var transport = new StubTransport().Answer("login/access-token", Session).Answer("history/transfer", Page(0));
            using var client = new MoviePilotClient(transport);
            using var service = Service(client);
            Assert.Catch<MoviePilotException>(() => service.FindSeriesTransferHistoriesAsync("", CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Total);
            Assert.Equal(0, service.FindSeriesTransferHistoriesAsync("283938", CancellationToken.None).GetAwaiter().GetResult().Count);
        });
        Test("剧重新整理：分页漏项或总数变动不展示不完整的可整理批次", () =>
        {
            foreach (var second in new[] { Page(2), Page(3, History(2)) })
            {
                using var transport = new StubTransport().Answer("login/access-token", Session)
                    .Sequence("history/transfer", (HttpStatusCode.OK, Page(2, History(1))), (HttpStatusCode.OK, second));
                using var client = new MoviePilotClient(transport);
                using var service = Service(client);
                Assert.Catch<MoviePilotException>(() => service.FindSeriesTransferHistoriesAsync("283938", CancellationToken.None).GetAwaiter().GetResult());
                Assert.Equal(0, transport.Count("transfer/manual"));
            }
        });
    }

    private static MoviePilotTransferContext Context() =>
        new("测试剧", "电视剧", "283938", null, "", []) { BrowseSeriesHistory = true };

    private static MoviePilotTransferRequest Request(MoviePilotTransferContext context, IReadOnlyList<MoviePilotTransferHistory> histories) =>
        new() { Files = context.Files, Histories = histories, Type = context.Type, MediaId = context.MediaId, Reorganize = true };

    private static MoviePilotTransferHistory History(long id)
    {
        var source = "/downloads/source-" + id + ".mkv";
        var target = "/library/S01E" + id + ".mkv";
        return new MoviePilotTransferHistory
        {
            Id = id,
            Title = "测试剧",
            Type = "电视剧",
            MediaSource = "themoviedb",
            MediaId = "283938",
            TmdbId = "283938",
            Seasons = "S01",
            Episodes = "E" + id,
            Mode = "softlink",
            Success = true,
            SourcePath = source,
            TargetPath = target,
            SourceFile = Json(JsonSerializer.Serialize(new { storage = "local", type = "file", path = source })),
            TargetFile = Json(JsonSerializer.Serialize(new { storage = "local", type = "file", path = target }))
        };
    }

    private static string Page(int total, params MoviePilotTransferHistory[] histories) => JsonSerializer.Serialize(new
    {
        success = true,
        data = new
        {
            total,
            list = histories.Select(history => new
            {
                id = history.Id,
                title = history.Title,
                type = history.Type,
                media_source = history.MediaSource,
                media_id = history.MediaId,
                tmdbid = history.TmdbId,
                src_fileitem = history.SourceFile,
                dest_fileitem = history.TargetFile,
                seasons = history.Seasons,
                episodes = history.Episodes,
                mode = history.Mode,
                status = history.Success
            })
        }
    });

    private static MoviePilotService Service(MoviePilotClient client)
    {
        var settings = new AppSettings();
        settings.MoviePilot.Enabled = true;
        settings.MoviePilot.Url = "192.0.2.10:3001";
        settings.MoviePilot.Username = "fixture";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(settings.MoviePilot, "fixture-password");
        return new MoviePilotService(client, credentials, settings);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}

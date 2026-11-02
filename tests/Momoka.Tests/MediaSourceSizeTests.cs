using System.Net;
using System.Text;
using System.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MediaSourceSizeTests
{
    public static void Register()
    {
        Test("版本体积：飞牛每个文件的数字与字符串字节数分别映射，不取条目合计", () =>
        {
            using var http = new EmbyHttp(new Replies("""
                {"code":0,"data":{"files":[
                  {"guid":"first","path":"/media/first.mkv","size":5368709120},
                  {"guid":"second","path":"/media/second.mkv","size":"2684354560"},
                  {"guid":"unknown","path":"/media/unknown.mkv"}],
                 "size":9999999999,"video_streams":[],"audio_streams":[],"subtitle_streams":[]}}
                """));
            var item = Read(http, MediaServerKind.FnNas);
            Assert.Equal(5L * 1024 * 1024 * 1024, item.MediaSources[0].Size);
            Assert.Equal(2560L * 1024 * 1024, item.MediaSources[1].Size);
            Assert.Equal("5 GB · first.mkv", new MediaVersion(item, item.MediaSources[0]).LabelFor(false));
            Assert.Equal("2.5 GB · second.mkv", new MediaVersion(item, item.MediaSources[1]).LabelFor(false));
            Assert.Null(item.MediaSources[2].Size);
            Assert.Equal("unknown.mkv", new MediaVersion(item, item.MediaSources[2]).LabelFor(false));
        });
        Test("版本体积：飞牛未知、非正、非有限、小数和溢出保持未知", () =>
        {
            foreach (var size in new[] { "null", "0", "-1", "\"NaN\"", "\"Infinity\"", "1.5", "9223372036854775808" })
            {
                using var http = new EmbyHttp(new Replies("{\"code\":0,\"data\":{\"files\":[{\"guid\":\"file\","
                    + "\"path\":\"/media/file.mkv\",\"size\":" + size + "}]}}"));
                var item = Read(http, MediaServerKind.FnNas);
                Assert.Null(item.MediaSources.Single().Size, size);
                Assert.Equal("file.mkv", new MediaVersion(item, item.MediaSources.Single()).LabelFor(false));
            }
        });
        Test("版本体积：飞牛保留超过 double 精确范围的整数，不截断字节", () =>
        {
            const long size = 9007199254740993;
            using var http = new EmbyHttp(new Replies("""
                {"code":0,"data":{"files":[{"guid":"file","path":"/media/file.mkv","size":9007199254740993}]}}
                """));
            Assert.Equal(size, Read(http, MediaServerKind.FnNas).MediaSources.Single().Size);
        });
        Test("版本体积：Emby 与 Jellyfin 直接保留各媒体源体积及字符串整数", () =>
        {
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            {
                using var http = new EmbyHttp(new Replies("""
                    {"Id":"movie","Type":"Movie","MediaSources":[
                      {"Id":"first","Name":"first.mkv","Size":5368709120},
                      {"Id":"second","Name":"second.mkv","Size":"2684354560"},
                      {"Id":"unknown","Name":"unknown.mkv"}]}
                    """));
                var item = Read(http, kind);
                Assert.Equal(5L * 1024 * 1024 * 1024, item.MediaSources[0].Size);
                Assert.Equal(2560L * 1024 * 1024, item.MediaSources[1].Size);
                Assert.Equal("5 GB · first.mkv", new MediaVersion(item, item.MediaSources[0]).LabelFor(false));
                Assert.Null(item.MediaSources[2].Size);
            }
        });
        Test("版本体积：Emby 条目级体积只补可确认的本条目文件", () =>
        {
            using var http = new EmbyHttp(new Replies("""
                {"Id":"movie","Type":"Movie","Size":1073741824,"Path":"/media/main.mkv","MediaSources":[
                  {"Id":"alternate","ItemId":"other","Path":"/media/other.mkv","Name":"other.mkv"},
                  {"Id":"main","ItemId":"movie","Path":"/media/main.mkv","Name":"main.mkv"}]}
                """));
            var item = Read(http, MediaServerKind.Emby);
            Assert.Null(item.MediaSources[0].Size, "本条目体积不能分给另一版本");
            Assert.Equal(1024L * 1024 * 1024, item.MediaSources[1].Size);
            Assert.Equal("1 GB · main.mkv", new MediaVersion(item, item.MediaSources[1]).LabelFor(false));
        });
        Test("版本体积：没有文件编号时用准确路径补本条目，多个未知版本不猜", () =>
        {
            using var matching = new EmbyHttp(new Replies("""
                {"Id":"movie","Type":"Movie","Size":5368709120,"Path":"/media/main.mkv","MediaSources":[
                  {"Id":"first","Name":"other.mkv","Path":"/media/other.mkv"},
                  {"Id":"second","Name":"main.mkv","Path":"/media/main.mkv"}]}
                """));
            var item = Read(matching, MediaServerKind.Emby);
            Assert.Null(item.MediaSources[0].Size);
            Assert.Equal(5L * 1024 * 1024 * 1024, item.MediaSources[1].Size);

            using var ambiguous = new EmbyHttp(new Replies("""
                {"Id":"movie","Type":"Movie","Size":5368709120,"MediaSources":[
                  {"Id":"first","Name":"first.mkv"},{"Id":"second","Name":"second.mkv"}]}
                """));
            Assert.True(Read(ambiguous, MediaServerKind.Emby).MediaSources.All(source => source.Size is null));
        });
        Test("版本体积：单来源条目体积可补，已有媒体源体积优先，未知保持未知", () =>
        {
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            {
                using var fallback = new EmbyHttp(new Replies("""
                    {"Id":"movie","Type":"Movie","Size":"1073741824","MediaSources":[{"Id":"file","Name":"file.mkv"}]}
                    """));
                Assert.Equal(1024L * 1024 * 1024, Read(fallback, kind).MediaSources.Single().Size);
                using var existing = new EmbyHttp(new Replies("""
                    {"Id":"movie","Type":"Movie","Size":5368709120,"MediaSources":[{"Id":"file","Name":"file.mkv","Size":2684354560}]}
                    """));
                Assert.Equal(2560L * 1024 * 1024, Read(existing, kind).MediaSources.Single().Size);
                using var unknown = new EmbyHttp(new Replies("""
                    {"Id":"movie","Type":"Movie","Size":0,"MediaSources":[{"Id":"file","Name":"file.mkv"}]}
                    """));
                Assert.Null(Read(unknown, kind).MediaSources.Single().Size);
            }
        });
        Test("版本体积：明确的版本编号或路径冲突时不借条目体积", () =>
        {
            foreach (var source in new[]
            {
                """{"Id":"file","ItemId":"other","Path":"/media/main.mkv"}""",
                """{"Id":"file","ItemId":"movie","Path":"/media/other.mkv"}"""
            })
            {
                using var http = new EmbyHttp(new Replies("{\"Id\":\"movie\",\"Type\":\"Movie\",\"Size\":1073741824,"
                    + "\"Path\":\"/media/main.mkv\",\"MediaSources\":[" + source + "]}"));
                Assert.Null(Read(http, MediaServerKind.Emby).MediaSources.Single().Size);
            }
        });
        Test("版本体积：文件夹或剧集的合计不补给媒体源", () =>
        {
            foreach (var type in new[] { EmbyItemType.Folder, EmbyItemType.Series })
            {
                using var http = new EmbyHttp(new Replies("{\"Id\":\"movie\",\"Type\":\"" + type + "\",\"Size\":1073741824,"
                    + "\"MediaSources\":[{\"Id\":\"file\",\"ItemId\":\"movie\"}]}"));
                Assert.Null(Read(http, MediaServerKind.Emby).MediaSources.Single().Size);
            }
        });
        Test("版本体积：Plex 每个 Part 的体积保留，缺失时不借另一 Part", () =>
        {
            using var http = new EmbyHttp(new Replies("""
                {"MediaContainer":{"Metadata":[{"ratingKey":"movie","type":"movie","Media":[{"Part":[
                  {"id":"first","key":"/library/parts/first/file.mkv","file":"/media/first.mkv","size":5368709120},
                  {"id":"second","key":"/library/parts/second/file.mkv","file":"/media/second.mkv","size":"2684354560"},
                  {"id":"unknown","key":"/library/parts/unknown/file.mkv","file":"/media/unknown.mkv"}]}]}]}}
                """));
            var item = Read(http, MediaServerKind.Plex);
            Assert.Equal(5L * 1024 * 1024 * 1024, item.MediaSources[0].Size);
            Assert.Equal(2560L * 1024 * 1024, item.MediaSources[1].Size);
            Assert.Null(item.MediaSources[2].Size);
            Assert.Equal("5 GB · first.mkv", new MediaVersion(item, item.MediaSources[0]).LabelFor(false));
            Assert.Equal("unknown.mkv", new MediaVersion(item, item.MediaSources[2]).LabelFor(false));
        });
        RegisterEmbySupplementary();
    }

    private static void RegisterEmbySupplementary()
    {
        Test("版本体积补读：备选条目缺大小时批量一次读取，重复编号去重且已有体积优先", () =>
        {
            using var replies = new BatchReplies("""
                {"Id":"movie","Type":"Movie","MediaSources":[
                  {"Id":"main","ItemId":"movie","Size":1073741824},
                  {"Id":"alternate","ItemId":"other","Path":"/media/other.mkv"},
                  {"Id":"duplicate","ItemId":"other","Path":"/media/other.mkv"},
                  {"Id":"unknown","Name":"unknown.mkv"}]}
                """, """
                {"Items":[{"Id":"other","Type":"Movie","Path":"/media/other.mkv","Size":2684354560},
                          {"Id":"movie","Type":"Movie","Size":9999999999}]}
                """);
            using var http = new EmbyHttp(replies);
            var item = Read(http, MediaServerKind.Emby);
            Assert.Equal(1, replies.BatchRequests.Count);
            var query = Query(replies.BatchRequests.Single());
            Assert.Equal("other", query["Ids"]);
            Assert.Equal("Path,Size", query["Fields"]);
            Assert.Equal(1024L * 1024 * 1024, item.MediaSources[0].Size);
            Assert.Equal(2560L * 1024 * 1024, item.MediaSources[1].Size);
            Assert.Equal(item.MediaSources[1].Size, item.MediaSources[2].Size);
            Assert.Null(item.MediaSources[3].Size);
            Assert.Equal("2.5 GB · other.mkv", new MediaVersion(item, item.MediaSources[1]).LabelFor(false));
        });
        Test("版本体积补读：新入库排序与体积共用一趟，仍按时间排序", () =>
        {
            using var replies = new BatchReplies("""
                {"Id":"movie","Type":"Movie","MediaSources":[
                  {"Id":"main","ItemId":"movie","Size":1073741824},
                  {"Id":"alternate","ItemId":"other","Path":"/media/other.mkv"}]}
                """, """
                {"Items":[{"Id":"movie","Type":"Movie","DateCreated":"2026-01-01T00:00:00Z"},
                          {"Id":"other","Type":"Movie","Path":"/media/other.mkv","Size":2684354560,
                           "DateCreated":"2026-02-01T00:00:00Z"}]}
                """);
            using var http = new EmbyHttp(replies);
            var item = Client(http, MediaServerKind.Emby).GetItemAsync("movie", default,
                order: MediaSourceOrder.NewestFirst).GetAwaiter().GetResult();
            Assert.Equal(1, replies.BatchRequests.Count);
            var query = Query(replies.BatchRequests.Single());
            Assert.Equal("movie,other", query["Ids"]);
            Assert.Equal("DateCreated,Path,Size", query["Fields"]);
            Assert.Equal("alternate", item.MediaSources[0].Id);
            Assert.Equal(2560L * 1024 * 1024, item.MediaSources[0].Size);
            Assert.Equal("main", item.MediaSources[1].Id);
        });
        Test("版本体积补读：已知大小、未知编号、非播放条目和 Jellyfin 不新增大小请求", () =>
        {
            foreach (var (kind, primary) in new[]
            {
                (MediaServerKind.Emby, """{"Id":"movie","Type":"Movie","MediaSources":[{"Id":"file","ItemId":"other","Size":100}]}"""),
                (MediaServerKind.Emby, """{"Id":"movie","Type":"Movie","MediaSources":[{"Id":"file"}]}"""),
                (MediaServerKind.Emby, """{"Id":"movie","Type":"Series","MediaSources":[{"Id":"file","ItemId":"other"}]}"""),
                (MediaServerKind.Jellyfin, """{"Id":"movie","Type":"Movie","MediaSources":[{"Id":"file","ItemId":"other"}]}""")
            })
            {
                using var replies = new BatchReplies(primary, "{\"Items\":[]}");
                using var http = new EmbyHttp(replies);
                _ = Read(http, kind);
                Assert.Equal(0, replies.BatchRequests.Count);
            }
        });
        Test("版本体积补读：错误条目编号、路径冲突和非播放合计不借给备选版本", () =>
        {
            foreach (var extra in new[]
            {
                """{"Id":"unrelated","Type":"Movie","Path":"/media/other.mkv","Size":100}""",
                """{"Id":"other","Type":"Movie","Path":"/media/different.mkv","Size":100}""",
                """{"Id":"other","Type":"Series","Path":"/media/other.mkv","Size":100}"""
            })
            {
                using var replies = new BatchReplies(MissingAlternate, "{\"Items\":[" + extra + "]}");
                using var http = new EmbyHttp(replies);
                Assert.Null(Read(http, MediaServerKind.Emby).MediaSources[0].Size);
                Assert.Equal(1, replies.BatchRequests.Count);
            }
        });
        Test("版本体积补读：不支持、拒绝或失败保留详情与未知体积且不重试", () =>
        {
            foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized,
                HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed,
                HttpStatusCode.NotImplemented, HttpStatusCode.ServiceUnavailable })
            {
                using var replies = new BatchReplies(MissingAlternate, "{}", status);
                using var http = new EmbyHttp(replies);
                var item = Read(http, MediaServerKind.Emby);
                Assert.Equal("movie", item.Id);
                Assert.Null(item.MediaSources[0].Size);
                Assert.Equal(1, replies.BatchRequests.Count);
            }
        });
        Test("版本体积补读：调用取消传播且不返回迟到补全", () =>
        {
            using var replies = new BatchReplies(MissingAlternate, "{\"Items\":[]}") { Hang = true };
            using var http = new EmbyHttp(replies);
            using var cancellation = new CancellationTokenSource();
            var read = Client(http, MediaServerKind.Emby).GetItemAsync("movie", cancellation.Token);
            replies.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => read.GetAwaiter().GetResult());
            Assert.Equal(1, replies.BatchRequests.Count);
        });
        Test("版本体积补读：挂起附属请求三秒退出，详情仍可用", () =>
        {
            using var replies = new BatchReplies(MissingAlternate, "{\"Items\":[]}") { Hang = true };
            using var http = new EmbyHttp(replies);
            var stopwatch = Stopwatch.StartNew();
            var item = Client(http, MediaServerKind.Emby).GetItemAsync("movie", default)
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.Equal(1, replies.BatchRequests.Count);
            Assert.Equal("movie", item.Id);
            Assert.Null(item.MediaSources[0].Size);
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(2) && stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        });
        Test("版本体积补读：全部大小已知的排序沿用日期请求且不再问大小", () =>
        {
            using var replies = new BatchReplies("""
                {"Id":"movie","Type":"Movie","MediaSources":[
                  {"Id":"main","ItemId":"movie","Size":10},{"Id":"alternate","ItemId":"other","Size":20}]}
                """, """
                {"Items":[{"Id":"movie","DateCreated":"2026-01-01T00:00:00Z"},
                          {"Id":"other","DateCreated":"2026-02-01T00:00:00Z"}]}
                """);
            using var http = new EmbyHttp(replies);
            var item = Client(http, MediaServerKind.Emby).GetItemAsync("movie", default,
                order: MediaSourceOrder.NewestFirst).GetAwaiter().GetResult();
            Assert.Equal(1, replies.BatchRequests.Count);
            Assert.Equal("DateCreated", Query(replies.BatchRequests.Single())["Fields"]);
            Assert.Equal("alternate", item.MediaSources[0].Id);
        });
        Test("版本体积补读：初始主条目路径已冲突时不被省略路径的补读覆盖", () =>
        {
            using var replies = new BatchReplies("""
                {"Id":"movie","Type":"Movie","Size":1073741824,"Path":"/media/main.mkv","MediaSources":[
                  {"Id":"file","ItemId":"movie","Path":"/media/different.mkv"}]}
                """, """{"Items":[{"Id":"movie","Type":"Movie","Size":1073741824}]}""");
            using var http = new EmbyHttp(replies);
            Assert.Null(Read(http, MediaServerKind.Emby).MediaSources.Single().Size);
            Assert.Equal(0, replies.BatchRequests.Count);
        });
        Test("版本体积补读：主条目缺大小但路径无冲突可按编号批补", () =>
        {
            using var replies = new BatchReplies("""
                {"Id":"movie","Type":"Movie","Path":"/media/main.mkv","MediaSources":[
                  {"Id":"file","ItemId":"movie","Path":"/media/main.mkv"}]}
                """, """{"Items":[{"Id":"movie","Type":"Movie","Size":1073741824}]}""");
            using var http = new EmbyHttp(replies);
            Assert.Equal(1024L * 1024 * 1024, Read(http, MediaServerKind.Emby).MediaSources.Single().Size);
            Assert.Equal(1, replies.BatchRequests.Count);
        });
    }

    private const string MissingAlternate = """
        {"Id":"movie","Type":"Movie","MediaSources":[{"Id":"alternate","ItemId":"other","Path":"/media/other.mkv"}]}
        """;

    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));

    private static EmbyItem Read(EmbyHttp http, MediaServerKind kind) =>
        Client(http, kind).GetItemAsync("movie", default).GetAwaiter().GetResult();

    private static EmbyClient Client(EmbyHttp http, MediaServerKind kind)
    {
        var connection = new EmbyConnection(new Uri("https://media.invalid/"), "NONSECRET", "user", "User", "Server",
            new DeviceIdentity("Momoka", "Fixture", "source-size", "1"))
        { ServerKind = kind };
        return new EmbyClient(http, connection);
    }

    private sealed class BatchReplies(string primary, string extra, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> BatchRequests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Hang { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var uri = request.RequestUri!;
            var batch = uri.AbsolutePath == "/Items";
            if (batch)
            {
                BatchRequests.Add(uri);
                Started.TrySetResult();
                if (Hang) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new HttpResponseMessage(batch ? status : HttpStatusCode.OK)
            { Content = new StringContent(batch ? extra : primary, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Replies(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/play/info", StringComparison.Ordinal))
                Assert.Equal(HttpMethod.Post, request.Method, "飞牛只读进度接口使用既有 POST");
            else Assert.Equal(HttpMethod.Get, request.Method, "体积读取不能写服务器或起播");
            var body = path.StartsWith("/api/v1/item/", StringComparison.Ordinal)
                ? "{\"code\":0,\"data\":{\"guid\":\"movie\",\"type\":\"Movie\"}}"
                : path.EndsWith("/play/info", StringComparison.Ordinal) ? "{\"code\":0,\"data\":{}}" : json;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

using System.Net;
using System.Text;
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
    }

    private static EmbyItem Read(EmbyHttp http, MediaServerKind kind)
    {
        var connection = new EmbyConnection(new Uri("https://media.invalid/"), "NONSECRET", "user", "User", "Server",
            new DeviceIdentity("Momoka", "Fixture", "source-size", "1"))
        { ServerKind = kind };
        return new EmbyClient(http, connection).GetItemAsync("movie", default).GetAwaiter().GetResult();
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

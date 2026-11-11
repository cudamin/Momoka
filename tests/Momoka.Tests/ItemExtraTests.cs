using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ItemExtraTests
{
    public static void Register()
    {
        Test("花絮：Emby 视频不依赖数量，Jellyfin 按服务器数量决定入口", () =>
        {
            var video = Item("""{"Id":"film","Type":"Movie","MediaType":"Video"}""");
            Assert.True(CanRead(video, MediaServerKind.Emby));
            Assert.False(CanRead(video, MediaServerKind.Jellyfin));
            var counted = Item("""{"Id":"series","Type":"Series","IsFolder":true,"SpecialFeatureCount":2}""");
            Assert.True(CanRead(counted, MediaServerKind.Jellyfin));
            Assert.False(CanRead(counted, MediaServerKind.Emby));
            foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex, (MediaServerKind)100 })
                Assert.False(CanRead(counted, kind));
        });
        Test("花絮：Emby 文件夹、直播、音频和未知媒体不读取", () =>
        {
            foreach (var json in new[]
            {
                """{"Type":"Movie"}""", """{"Type":"Video","MediaType":"Audio"}""",
                """{"Type":"TvChannel","MediaType":"Video"}""", """{"Type":"Program","MediaType":"Video"}""",
                """{"Type":"Movie","MediaType":"Video","IsFolder":true}"""
            }) Assert.False(CanRead(Item(json), MediaServerKind.Emby));
            foreach (var count in new[] { "null", "0", "-1" })
                Assert.False(CanRead(Item($$"""{"SpecialFeatureCount":{{count}}}"""), MediaServerKind.Jellyfin));
        });
        Test("花絮：不可播放媒体、离线虚拟项和明确播放拒绝被过滤", () =>
        {
            foreach (var json in new[]
            {
                """{"Id":"x","Type":"Series"}""", """{"Id":"x","Type":"Audio"}""",
                """{"Id":"x","Type":"Video","MediaType":"Audio"}""",
                """{"Id":"x","Type":"Video","LocationType":"Virtual"}""",
                """{"Id":"x","Type":"Video","LocationType":"Offline"}""",
                """{"Id":"x","Type":"Video","PlayAccess":"None"}""",
                """{"Id":"x","Type":"Video","IsFolder":true}"""
            }) Assert.False(Playable(Item(json)));
            Assert.True(Playable(Item("""{"Id":"x","Type":"Video","MediaType":"Video","PlayAccess":"Full","LocationType":"FileSystem","ExtraType":"BehindTheScenes"}""")));
            Assert.True(Playable(Item("""{"Id":"x","Type":"Video"}""")));
        });
        foreach (var (kind, version, path, query) in new[]
        {
            (MediaServerKind.Emby, "4.10.1.0", "/media/Users/user/Items/film/SpecialFeatures", "?Fields=MediaSources%2CPath"),
            (MediaServerKind.Jellyfin, "10.10.7", "/media/Items/film/SpecialFeatures", "?UserId=user"),
            (MediaServerKind.Jellyfin, "12.4.0", "/media/Items/film/SpecialFeatures", "?UserId=user")
        })
        {
            Test($"花絮：{kind} {version} 独立路由、裸数组与鉴权", () =>
            {
                using var handler = new Transport("""[{"Id":"extra","Name":"幕后花絮","Type":"Video","MediaType":"Video","ExtraType":"BehindTheScenes","SpecialFeatureCount":null,"PlayAccess":"Full"}]""");
                using var http = new EmbyHttp(handler);
                var items = Read(Client(http, kind, version), "film", default);
                Assert.Equal("extra", items.Single().Id);
                Assert.True(Playable(items.Single()));
                Assert.Equal(path, handler.Uri!.AbsolutePath);
                Assert.Equal(query, handler.Uri.Query);
                Assert.Equal(kind == MediaServerKind.Emby, handler.LegacyToken);
                Assert.Equal(kind == MediaServerKind.Jellyfin, handler.Authorization);
                Assert.DoesNotContain("placeholder-only", handler.Uri.AbsoluteUri);
            });
        }
        Test("花絮：未支持来源与非法路由编号零请求", () =>
        {
            using var handler = new Transport("[]");
            using var http = new EmbyHttp(handler);
            foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex, (MediaServerKind)100 })
                Assert.Throws<NotSupportedException>(() => Read(Client(http, kind), "film", default));
            foreach (var id in new[] { "", " ", ".", "..", "film/other", "film\\other", "film?query", "film#fragment", "film\n" })
                Assert.Throws<ArgumentException>(() => Read(Client(http, MediaServerKind.Emby), id, default));
            Assert.Equal(0, handler.Requests);
        });
        Test("花絮：空数组、转义与取消", () =>
        {
            using var handler = new Transport("[]");
            using var http = new EmbyHttp(handler);
            Assert.Equal(0, Read(Client(http, MediaServerKind.Emby), "film a", default).Count);
            Assert.Contains("film%20a/SpecialFeatures", handler.Uri!.AbsoluteUri);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => Read(Client(http, MediaServerKind.Emby), "film", cancelled.Token));
            Assert.Equal(1, handler.Requests);
        });
        Test("花絮：不是媒体数组的成功响应不能伪装为空态", () =>
        {
            using var handler = new Transport("""{"Items":[]}""");
            using var http = new EmbyHttp(handler);
            Assert.Throws<EmbyApiException>(() => Read(Client(http, MediaServerKind.Emby), "film", default));
        });
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable })
            Test($"花絮：{status} 原样报告失败而非空结果", () =>
            {
                using var handler = new Transport("[]") { Status = status };
                using var http = new EmbyHttp(handler);
                Assert.Throws<EmbyApiException>(() => Read(Client(http, MediaServerKind.Emby), "film", default));
            });
    }

    private static EmbyItem Item(string json) => JsonSerializer.Deserialize<EmbyItem>(json)!;

    private static bool CanRead(EmbyItem item, MediaServerKind kind) => (bool)Rule("CanRead", item, kind)!;
    private static bool Playable(EmbyItem item) => (bool)Rule("IsPlayable", item)!;
    private static object? Rule(string method, params object?[] args)
    {
        var rules = typeof(EmbyItem).Assembly.GetType("Momoka.Emby.ItemExtras");
        Assert.NotNull(rules, "尚未实现花絮协议与可用性规则");
        return Invoke(rules!.GetMethod(method)!, null, args);
    }

    private static List<EmbyItem> Read(EmbyClient client, string id, CancellationToken token)
    {
        var method = typeof(EmbyClient).GetMethod("GetSpecialFeaturesAsync");
        Assert.NotNull(method, "尚未实现花絮读取入口");
        return ((Task<List<EmbyItem>>)Invoke(method!, client, [id, token])!).GetAwaiter().GetResult();
    }

    private static object? Invoke(MethodInfo method, object? target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static EmbyClient Client(EmbyHttp http, MediaServerKind kind, string version = "4.10.1.0") =>
        new(http, new EmbyConnection(new Uri("https://extras.invalid/media/"), "placeholder-only", "user", "user", "fixture",
            new DeviceIdentity("fixture", "fixture", "fixture", "0")) { ServerKind = kind, ServerVersion = version });

    private sealed class Transport(string body) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public bool LegacyToken { get; private set; }
        public bool Authorization { get; private set; }
        public int Requests { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("extras.invalid", request.RequestUri!.Host);
            Requests++;
            Uri = request.RequestUri;
            LegacyToken = request.Headers.Contains("X-Emby-Token");
            Authorization = request.Headers.TryGetValues("Authorization", out var headers)
                && headers.Any(header => header.Contains("Token=\"placeholder-only\"", StringComparison.Ordinal));
            return Task.FromResult(new HttpResponseMessage(Status)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

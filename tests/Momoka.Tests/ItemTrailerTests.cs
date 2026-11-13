using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ItemTrailerTests
{
    public static void Register()
    {
        Test("预告片：详情读取服务器的本地数量与远程链接", () =>
        {
            var item = JsonSerializer.Deserialize<EmbyItem>("""
                {"Id":"film","LocalTrailerCount":2,"RemoteTrailers":[{"Url":"https://example.invalid/watch?v=one","Name":"正式预告"}]}
                """)!;
            Assert.Equal(2, item.LocalTrailerCount);
            var remote = item.RemoteTrailers!.Single();
            Assert.Equal("https://example.invalid/watch?v=one", remote.Url);
            Assert.Equal("正式预告", remote.Name);
        });
        Test("预告片：仅 Emby 与 Jellyfin 提供预告入口", () =>
        {
            foreach (var kind in Enum.GetValues<MediaServerKind>())
                Assert.Equal(kind is MediaServerKind.Emby or MediaServerKind.Jellyfin, ItemTrailers.Supports(kind));
            Assert.False(ItemTrailers.Supports((MediaServerKind)100));
        });
        Test("预告片：数量与安全远程链接共同决定入口", () =>
        {
            foreach (var count in new string[] { "null", "-1", "0" })
                Assert.False(ItemTrailers.HasAny(Item($$"""{"LocalTrailerCount":{{count}},"RemoteTrailers":null}"""), MediaServerKind.Emby));
            var local = Item("""{"LocalTrailerCount":1}""");
            Assert.True(ItemTrailers.HasAny(local, MediaServerKind.Emby));
            Assert.False(ItemTrailers.HasAny(local, MediaServerKind.Plex));
            var remote = Item("""{"RemoteTrailers":[{"Url":"https://watch.invalid/one"}]}""");
            Assert.True(ItemTrailers.HasAny(remote, MediaServerKind.Jellyfin));
            Assert.False(ItemTrailers.HasAny(Item("""{"RemoteTrailers":[{"Url":"file:///one"}]}"""), MediaServerKind.Emby));
        });
        Test("预告片：远程 URL 保留路径查询大小写、去重及原始 DTO", () =>
        {
            var item = Item("""{"Name":"影片","RemoteTrailers":[{"Url":" HTTPS://WATCH.INVALID:443/Trailer?Video=ABC ","Name":" 首发预告 "},{"Url":"https://watch.invalid/Trailer?Video=ABC","Name":"重复"},{"Url":"https://watch.invalid/trailer?Video=ABC"},{"Url":"https://watch.invalid/Trailer?Video=abc"}]}""");
            var result = ItemTrailers.Remote(item);
            Assert.Equal(3, result.Count);
            Assert.Equal("https://watch.invalid/Trailer?Video=ABC", result[0].Url);
            Assert.Equal("首发预告", result[0].Name);
            Assert.Equal("影片 · 预告片", result[1].Name);
            Assert.Equal(" HTTPS://WATCH.INVALID:443/Trailer?Video=ABC ", item.RemoteTrailers![0].Url);
        });
        Test("预告片：拒绝协议、用户信息、控制字符和凭据查询", () =>
        {
            var urls = new string?[]
            {
                null, "", "  ", "file:///movie.mp4", "javascript:alert(1)", "//watch.invalid/one",
                "ftp://watch.invalid/one", "https://user:secret@watch.invalid/one", "https://watch.invalid/\none",
                "https://watch.invalid/%0Aone", "https://watch.invalid/?api_key=placeholder",
                "https://watch.invalid/?X-Emby-Token=placeholder", "https://watch.invalid/?x-plex-token=placeholder",
                "https://watch.invalid/?ACCESS_TOKEN=placeholder", "https://watch.invalid/?%61pi_key=placeholder"
            };
            var item = Item(JsonSerializer.Serialize(new { RemoteTrailers = urls.Select(url => new { Url = url }) }));
            Assert.Equal(0, ItemTrailers.Remote(item).Count);
            Assert.Equal(0, ItemTrailers.Remote(Item("{}")).Count);
            Assert.Equal(0, ItemTrailers.Remote(Item("""{"RemoteTrailers":[null]}""")).Count);
        });
        Test("预告片：浏览器收到的片段也不能带标准凭据", () =>
        {
            var item = Item("""{"RemoteTrailers":[{"Url":"https://watch.invalid/#access_token=placeholder"},{"Url":"https://watch.invalid/#!/watch?api_key=placeholder"}]}""");
            Assert.Equal(0, ItemTrailers.Remote(item).Count);
        });
        foreach (var (kind, version, path, query) in new[]
        {
            (MediaServerKind.Emby, "4.10.1.0", "/media/Users/user/Items/film/LocalTrailers", ""),
            (MediaServerKind.Jellyfin, "10.10.7", "/media/Users/user/Items/film/LocalTrailers", ""),
            (MediaServerKind.Jellyfin, "12.4.0", "/media/Items/film/LocalTrailers", "?UserId=user")
        })
        {
            Test($"预告片：{kind} {version} 本地裸数组与鉴权路径", () =>
            {
                using var handler = new Transport("""[{"Id":"trailer","Name":"预告","Type":"Trailer"}]""");
                using var http = new EmbyHttp(handler);
                var result = Client(http, kind, version).GetLocalTrailersAsync("film", default).GetAwaiter().GetResult();
                Assert.Equal("trailer", result.Single().Id);
                Assert.Equal(path, handler.Uri!.AbsolutePath);
                Assert.Equal(query, handler.Uri.Query);
                Assert.DoesNotContain("placeholder-only", handler.Uri.AbsoluteUri);
                Assert.Equal(kind == MediaServerKind.Emby, handler.LegacyToken);
                Assert.Equal(kind == MediaServerKind.Jellyfin, handler.Authorization);
            });
        }
        Test("预告片：未支持来源与空编号不发送请求", () =>
        {
            using var handler = new Transport("[]");
            using var http = new EmbyHttp(handler);
            foreach (var kind in new[] { MediaServerKind.Plex, MediaServerKind.FnNas, (MediaServerKind)100 })
                Assert.Throws<NotSupportedException>(() => Client(http, kind).GetLocalTrailersAsync("film", default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => Client(http, MediaServerKind.Emby).GetLocalTrailersAsync(" ", default).GetAwaiter().GetResult());
            Assert.Equal(0, handler.Requests);
        });
        Test("预告片：空数组、条目转义和取消", () =>
        {
            using var handler = new Transport("[]");
            using var http = new EmbyHttp(handler);
            Assert.Equal(0, Client(http, MediaServerKind.Emby).GetLocalTrailersAsync("film a", default).GetAwaiter().GetResult().Count);
            Assert.Contains("film%20a/LocalTrailers", handler.Uri!.AbsoluteUri);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => Client(http, MediaServerKind.Emby).GetLocalTrailersAsync("film", cancelled.Token).GetAwaiter().GetResult());
            Assert.Equal(1, handler.Requests);
        });
    }

    private static EmbyItem Item(string json) => JsonSerializer.Deserialize<EmbyItem>(json)!;

    private static EmbyClient Client(EmbyHttp http, MediaServerKind kind, string version = "4.10.1.0") =>
        new(http, new EmbyConnection(new Uri("https://trailers.invalid/media/"), "placeholder-only", "user", "user", "fixture",
            new DeviceIdentity("fixture", "fixture", "fixture", "0"))
        { ServerKind = kind, ServerVersion = version });

    private sealed class Transport(string body) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public bool LegacyToken { get; private set; }
        public bool Authorization { get; private set; }
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("trailers.invalid", request.RequestUri!.Host);
            Requests++;
            Uri = request.RequestUri;
            LegacyToken = request.Headers.Contains("X-Emby-Token");
            Authorization = request.Headers.TryGetValues("Authorization", out var headers)
                && headers.Any(header => header.Contains("Token=\"placeholder-only\"", StringComparison.Ordinal));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}

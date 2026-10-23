using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// The Plex protocol layer on its own: address normalisation, what may escape the server, where the
/// token is allowed to travel, redirects, the account exchange and the shape of a browse request.
/// <para>
/// Every claim here is checked against a fake transport, so it says what this client sends — not what a
/// real Plex server answers. No live server has been used to write or verify these cases.
/// </para>
/// </summary>
internal static partial class PlexProtocolTests
{
    private const string Secret = "fixture-plex-token";
    private static readonly Uri Base = new("http://plex.invalid:32400/");
    private static readonly PlexConnection Connection = new(Base, Secret, "7", "fixture", "fixture-client", "1.0");

    public static void Register()
    {
        RegisterEndpoints();
        Test("Plex 地址：默认端口、https 保留 443、Web 路径与账户信息", () =>
        {
            Assert.Equal("http://nas.invalid:32400/", PlexEndpoint.Normalize("nas.invalid").AbsoluteUri);
            Assert.Equal("http://nas.invalid:32400/", PlexEndpoint.Normalize("http://nas.invalid").AbsoluteUri);
            Assert.Equal("http://nas.invalid:32401/", PlexEndpoint.Normalize("nas.invalid:32401").AbsoluteUri);
            // An https address with no port is a reverse proxy, not a server on its own default.
            Assert.Equal("https://plex.invalid/", PlexEndpoint.Normalize("https://plex.invalid").AbsoluteUri);
            Assert.Equal("https://plex.invalid/plex/", PlexEndpoint.Normalize("https://plex.invalid/plex/web/index.html#!").AbsoluteUri);
            Assert.Equal("http://plex.invalid:32400/", PlexEndpoint.Normalize("http://plex.invalid:32400/web").AbsoluteUri);
            Assert.False(PlexEndpoint.TryNormalize("https://user:password@plex.invalid/", out _, out _));
            Assert.False(PlexEndpoint.TryNormalize("file:///private", out _, out _));
            Assert.False(PlexEndpoint.TryNormalize("", out _, out _));
        });
        Test("Plex 地址：条目标识、媒体地址与 /:/ 端点都不能逃逸服务器", () =>
        {
            foreach (var id in new[] { "..", "a/b", "a\\b", "a?token", "a#b", "%2f", "\r\n" })
                Assert.Throws<ArgumentException>(() => PlexEndpoint.Segment(id));
            Assert.Throws<ArgumentException>(() => PlexEndpoint.Api(Base, "../escape"));
            Assert.Throws<ArgumentException>(() => PlexEndpoint.Api(Base, "https://other.invalid/"));
            Assert.Throws<ArgumentException>(() => PlexEndpoint.Special(Base, ":/timeline", "?a=b#fragment"));
            Assert.Equal("http://plex.invalid:32400/:/timeline?ratingKey=1", PlexEndpoint.Special(Base, ":/timeline", "?ratingKey=1").AbsoluteUri);
            // The part key a timeline report needs is read back off the end of the media address.
            Assert.Equal("/library/parts/20/1/file.mkv",
                PlexEndpoint.RelativeKey(Base, new Uri("http://plex.invalid:32400/library/parts/20/1/file.mkv")));
            Assert.DoesNotContain(Secret, Connection.ToString());
        });
        Test("Plex 传输：令牌只在请求头，/identity 不带令牌", () =>
        {
            using var transport = new FixtureTransport(request => request.Uri.AbsolutePath == "/identity"
                ? Reply("{\"machineIdentifier\":\"machine\",\"version\":\"1.41.2\"}")
                : Reply("{\"friendlyName\":\"客厅\"}"));
            using var http = new PlexHttp(transport, disposeHandler: false);
            var client = new PlexClient(http);

            var identity = client.GetIdentityAsync(Base, PlexCall.Anonymous).GetAwaiter().GetResult();
            Assert.Equal("machine", identity["machineIdentifier"]!.GetValue<string>());
            Assert.False(transport.Requests[0].Headers.ContainsKey("X-Plex-Token"));

            client.GetServerAsync(Connection).GetAwaiter().GetResult();
            Assert.Equal(Secret, transport.Requests[1].Headers["X-Plex-Token"]);
            Assert.DoesNotContain(Secret, transport.Requests[1].Uri.AbsoluteUri);
            Assert.Equal("fixture-client", transport.Requests[1].Headers["X-Plex-Client-Identifier"]);
        });
        Test("Plex 传输：401 判为令牌失效且不回显服务端正文", () =>
        {
            using var http = new PlexHttp(new FixtureTransport(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            { Content = new StringContent(Secret), ReasonPhrase = Secret }));
            var error = Assert.Catch<PlexApiException>(() =>
                new PlexClient(http).GetServerAsync(Connection).GetAwaiter().GetResult());
            Assert.True(error.IsTokenExpired);
            Assert.DoesNotContain(Secret, error.ToString());
        });
        Test("Plex 传输：跨来源重定向不发送第二次请求", () =>
        {
            using var external = new FixtureTransport(_ => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            { Headers = { Location = new Uri("https://external.invalid/data") } });
            using var http = new PlexHttp(external, disposeHandler: false);
            Assert.Throws<PlexApiException>(() => new PlexClient(http).GetServerAsync(Connection).GetAwaiter().GetResult());
            Assert.Equal(1, external.Requests.Count);
        });
        Test("Plex 账号：密码走正文、客户端标识走头部，地址是 plex.tv", () =>
        {
            using var transport = new FixtureTransport(request =>
            {
                Assert.Equal("plex.tv", request.Uri.Host);
                Assert.Equal("/api/v2/users/signin", request.Uri.AbsolutePath);
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.DoesNotContain("秘密口令", request.Uri.AbsoluteUri);
                var body = JsonNode.Parse(request.Body)!;
                Assert.Equal("秘密口令", body["password"]!.GetValue<string>());
                Assert.Equal("测试账号", body["login"]!.GetValue<string>());
                Assert.Equal("fixture-client", request.Headers["X-Plex-Client-Identifier"]);
                return Raw("{\"user\":{\"id\":\"7\",\"username\":\"fixture\"},\"authToken\":\"fixture-plex-token\"}");
            });
            using var http = new PlexHttp(transport, disposeHandler: false);
            var result = new PlexClient(http)
                .SignInToAccountAsync(" 测试账号 ", "秘密口令", PlexCall.For(null, "fixture-client", "1.0"))
                .GetAwaiter().GetResult();
            Assert.Equal("fixture-plex-token", result["authToken"]!.GetValue<string>());
            Assert.Equal(1, transport.Requests.Count);
        });
        Test("Plex 浏览：分页走容器头，排序与类型走查询串", () =>
        {
            using var transport = new FixtureTransport(_ => Reply("{\"size\":0,\"totalSize\":0}"));
            using var http = new PlexHttp(transport, disposeHandler: false);
            var client = new PlexClient(http);
            var query = PlexMediaAdapter.Query(new ItemQuery
            {
                IncludeItemTypes = [EmbyItemType.Movie],
                SortBy = EmbySortBy.DateAdded,
                Descending = true,
                IsPlayed = false
            });
            client.GetSectionItemsAsync(Connection, "1", query, start: 20, size: 40).GetAwaiter().GetResult();

            var sent = transport.Requests.Single();
            Assert.Equal("/library/sections/1/all", sent.Uri.AbsolutePath);
            Assert.Contains("sort=addedAt%3Adesc", sent.Uri.Query);
            Assert.Contains("type=1", sent.Uri.Query);
            Assert.Contains("unwatched=1", sent.Uri.Query);
            Assert.Equal("20", sent.Headers["X-Plex-Container-Start"]);
            Assert.Equal("40", sent.Headers["X-Plex-Container-Size"]);
        });
        Test("Plex 上报：scrobble 与 timeline 带上条目号，时间以毫秒计", () =>
        {
            using var transport = new FixtureTransport(_ => Reply("{\"size\":0}"));
            using var http = new PlexHttp(transport, disposeHandler: false);
            var client = new PlexClient(http);

            client.SetWatchedAsync(Connection, "10", watched: true).GetAwaiter().GetResult();
            Assert.Equal("/:/scrobble", transport.Requests[0].Uri.AbsolutePath);
            Assert.Contains("key=10", transport.Requests[0].Uri.Query);
            Assert.Contains("identifier=com.plexapp.plugins.library", transport.Requests[0].Uri.Query);

            client.ReportTimelineAsync(Connection, "10", "/library/parts/20/1/file.mkv", "playing",
                30 * TimeSpan.TicksPerSecond, 90 * TimeSpan.TicksPerSecond).GetAwaiter().GetResult();
            var timeline = transport.Requests[1];
            Assert.Equal("/:/timeline", timeline.Uri.AbsolutePath);
            Assert.Contains("state=playing", timeline.Uri.Query);
            Assert.Contains("time=30000", timeline.Uri.Query);
            Assert.Contains("duration=90000", timeline.Uri.Query);
            Assert.Equal(Secret, timeline.Headers["X-Plex-Token"]);
        });
        Test("Plex 播放：原始文件地址不含令牌，凭据只交给请求头", () =>
        {
            var url = new PlexConnection(Base, Secret).Call;
            Assert.Equal(Secret, url.AccessToken);
            Assert.Equal("http://plex.invalid:32400/library/parts/20/1/file.mkv",
                new PlexClient(new PlexHttp()).GetMediaUrl(Connection, "/library/parts/20/1/file.mkv").AbsoluteUri);
            Assert.DoesNotContain(Secret, new PlexClient(new PlexHttp())
                .GetMediaUrl(Connection, "/library/parts/20/1/file.mkv").AbsoluteUri);
            Assert.Equal(Secret, new PlexClient(new PlexHttp()).GetMediaHeaders(Connection)
                .Single(header => header.Key == "X-Plex-Token").Value);
            Assert.Throws<ArgumentException>(() => new PlexClient(new PlexHttp()).GetMediaUrl(Connection, "../private"));
        });
        Test("Plex 传输：调用者取消保留取消语义，共享 handler 不被提前释放", () =>
        {
            using var transport = new FixtureTransport(_ => Reply("{\"size\":0}"));
            using (var http = new PlexHttp(transport, disposeHandler: false))
            {
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                Assert.Throws<OperationCanceledException>(() =>
                    new PlexClient(http).GetServerAsync(Connection, cancelled.Token).GetAwaiter().GetResult());
            }
            Assert.False(transport.Disposed);
        });
    }

    private static HttpResponseMessage Reply(string container) => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"MediaContainer\":" + container + "}") };

    /// <summary>The account service answers with a bare object; only the server wraps in MediaContainer.</summary>
    private static HttpResponseMessage Raw(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string Body, Dictionary<string, string> Headers);

    private sealed class FixtureTransport(Func<SentRequest, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        public bool Disposed { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sent = new SentRequest(request.Method, request.RequestUri!,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase));
            Requests.Add(sent);
            return reply(sent);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}

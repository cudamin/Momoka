using System.Net;
using System.Text.Json.Nodes;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnNasProtocolTests
{
    private const string Secret = "fixture-secret-token";
    private static readonly Uri Base = new("https://fnnas.invalid/v/");
    private static readonly FnNasConnection Connection = new(Base, Secret);

    public static void Register()
    {
        Test("飞牛地址：登录页、反向代理前缀和无协议地址归一", () =>
        {
            Assert.Equal("http://nas.invalid:5666/v/", FnNasEndpoint.Normalize("nas.invalid:5666").AbsoluteUri);
            Assert.Equal("https://nas.invalid/proxy/v/", FnNasEndpoint.Normalize("https://nas.invalid/proxy/v/login?redirect_uri=unused#fragment").AbsoluteUri);
            Assert.Equal("https://nas.invalid/proxy/v/", FnNasEndpoint.Normalize("https://nas.invalid/proxy/v/").AbsoluteUri);
            Assert.False(FnNasEndpoint.TryNormalize("https://user:password@nas.invalid/v/", out _, out _));
            Assert.False(FnNasEndpoint.TryNormalize("file:///private/v", out _, out _));
        });
        Test("飞牛地址：条目标识和图片不能逃逸服务器", () =>
        {
            foreach (var id in new[] { "..", "a/b", "a\\b", "a?token", "a#b", "%2f", "\r\n" })
                Assert.Throws<ArgumentException>(() => FnNasEndpoint.Segment(id));
            Assert.Throws<ArgumentException>(() => FnNasEndpoint.Api(Base, "../escape"));
            Assert.Throws<ArgumentException>(() => FnNasEndpoint.Api(Base, "https://other.invalid/"));
            Assert.DoesNotContain(Secret, Connection.ToString());
        });
        Test("飞牛签名：官方空正文和中文 POST 黄金向量", () =>
        {
            Assert.Equal("nonce=123456&timestamp=1700000000000&sign=1821ce9246b6733f3ddc1345143cd2e2",
                FnNasSigner.Create(HttpMethod.Get, new Uri(Base, "api/v1/sys/config"), null, "123456", 1700000000000));
            Assert.Equal("nonce=123456&timestamp=1700000000000&sign=985dd1ea1785b7eded80cd4cecebebc7",
                FnNasSigner.Create(HttpMethod.Post, new Uri(Base, "api/v1/item/list"), "{\"title\":\"中文\",\"page\":1}", "123456", 1700000000000));
        });
        Test("飞牛签名：GET 排序、解码、重复键和空值与官方一致", () =>
        {
            var uri = new Uri("https://fnnas.invalid/proxy/v/api/v1/search/list?z=a%2Bb&a=first&q=%E8%93%9D+%E5%85%89&a=%E6%9C%AB%E5%80%BC&omit=null");
            Assert.Equal("a=末值&q=蓝 光&z=a+b", FnNasSigner.CanonicalQuery(uri));
            Assert.Equal("nonce=123456&timestamp=1700000000000&sign=170d87579df634d4583bf3b8f359bd69",
                FnNasSigner.Create(HttpMethod.Get, uri, null, "123456", 1700000000000));
            // The official middleware filters a value before assigning it to the duplicate-key map.
            Assert.Equal("a=old&blank=", FnNasSigner.CanonicalQuery(new Uri(Base, "api/v1/test?a=old&a=null&blank=&drop=undefined")));
        });
        Test("飞牛登录：v2 只传 SHA256、请求签名覆盖真正发出的 UTF8 正文", () =>
        {
            using var transport = new FixtureTransport(request =>
            {
                Assert.Equal("/v/api/v2/user/loginByPassword", request.Uri.AbsolutePath);
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.False(request.Headers.ContainsKey("Authorization"));
                Assert.DoesNotContain("秘密口令", request.Body);
                var body = JsonNode.Parse(request.Body)!;
                Assert.Equal(FnNasSigner.HashPassword("秘密口令"), body["password"]!.GetValue<string>());
                Assert.Equal("trimemedia-web", body["app_name"]!.GetValue<string>());
                Assert.Equal("测试用户", body["username"]!.GetValue<string>());
                CheckSignature(request);
                return Reply("{\"token\":\"fixture-login-token\"}");
            });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            Assert.Equal("fixture-login-token", client.LoginAsync(Base, " 测试用户 ", "秘密口令").GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
        });
        Test("飞牛帐号：改密码走 v2 传 SHA256 并交回新令牌，播放偏好只提交两格语言", () =>
        {
            using var transport = new FixtureTransport(request =>
            {
                Assert.Equal(Secret, request.Headers["Authorization"]);
                CheckSignature(request);
                var body = JsonNode.Parse(request.Body)!;
                if (request.Uri.AbsolutePath.EndsWith("/user/passwd", StringComparison.Ordinal))
                {
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal("/v/api/v2/user/passwd", request.Uri.AbsolutePath);
                    Assert.Equal(FnNasSigner.HashPassword("新口令"), body["password"]!.GetValue<string>());
                    Assert.DoesNotContain("新口令", request.Body);
                    return Reply("\"fixture-new-token\"");
                }
                Assert.Equal("/v/api/v1/user/info", request.Uri.AbsolutePath);
                Assert.Equal("", body["lan"]!.GetValue<string>());
                Assert.Equal("ja-JP", body["audio_lan"]!.GetValue<string>());
                return Reply("true");
            });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            Assert.Equal("fixture-new-token", client.ChangePasswordAsync(Connection, "新口令").GetAwaiter().GetResult());
            client.UpdateUserInfoAsync(Connection, new JsonObject { ["lan"] = "", ["audio_lan"] = "ja-JP" }).GetAwaiter().GetResult();
            Assert.Equal(2, transport.Requests.Count);
            foreach (var request in transport.Requests) Assert.DoesNotContain(Secret, request.Uri.AbsoluteUri);
        });
        Test("飞牛错误：HTTP 200 业务失败及 HTTP 错误都不回显服务端凭据", () =>
        {
            foreach (var response in new[]
            {
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":-2,\"msg\":\"" + Secret + "\"}") },
                new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(Secret), ReasonPhrase = Secret }
            })
            {
                using var http = new FnNasHttp(new FixtureTransport(_ => response));
                var error = Assert.Catch<FnNasApiException>(() => new FnNasClient(http).GetUserAsync(Connection).GetAwaiter().GetResult());
                Assert.DoesNotContain(Secret, error.ToString());
                if (response.StatusCode == HttpStatusCode.OK) Assert.True(error.IsTokenExpired);
                else Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            }
        });
        Test("飞牛重定向：跨来源不发送第二次请求，同源按新路径重签", () =>
        {
            using var external = new FixtureTransport(_ => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            { Headers = { Location = new Uri("https://external.invalid/data") } });
            using var first = new FnNasHttp(external, disposeHandler: false);
            Assert.Throws<FnNasApiException>(() => new FnNasClient(first).GetUserAsync(Connection).GetAwaiter().GetResult());
            Assert.Equal(1, external.Requests.Count);

            using var same = new FixtureTransport(request => request.Uri.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("/v/api/v1/user/current", UriKind.Relative) } }
                : Reply("{\"is_admin\":1}"));
            using var second = new FnNasHttp(same, disposeHandler: false);
            new FnNasClient(second).GetUserAsync(Connection).GetAwaiter().GetResult();
            Assert.Equal(2, same.Requests.Count);
            CheckSignature(same.Requests[1]);
            Assert.Equal(Secret, same.Requests[1].Headers["Authorization"]);
        });
        Test("飞牛播放：range 地址无账号令牌，凭据只交给请求头", () =>
        {
            using var http = new FnNasHttp(new FixtureTransport(_ => Reply("true")));
            var client = new FnNasClient(http);
            Assert.Equal("https://fnnas.invalid/v/api/v1/media/range/media-guid", client.GetMediaUrl(Connection, "media-guid").AbsoluteUri);
            Assert.DoesNotContain(Secret, client.GetMediaUrl(Connection, "media-guid").AbsoluteUri);
            Assert.Equal(Secret, client.GetMediaHeaders(Connection).Single(header => header.Key == "Authorization").Value);
            Assert.Throws<ArgumentException>(() => client.GetMediaUrl(Connection, "../private"));
        });
        Test("飞牛传输：调用者取消保留取消语义，共享 handler 不被提前释放", () =>
        {
            using var transport = new FixtureTransport(_ => Reply("true"));
            using (var http = new FnNasHttp(transport, disposeHandler: false))
            {
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                Assert.Throws<OperationCanceledException>(() => new FnNasClient(http).GetUserAsync(Connection, cancelled.Token).GetAwaiter().GetResult());
            }
            Assert.False(transport.Disposed);
        });
        Test("飞牛更多菜单：版本、匹配、刷新、编辑与删除走官方路径和正文", () =>
        {
            using var transport = new FixtureTransport(request =>
            {
                CheckSignature(request);
                return request.Uri.AbsolutePath switch
                {
                    "/v/api/v1/media/itemfile/m" => Reply("""[{"media_guid":"file","file_path":"/a.mkv"}]"""),
                    "/v/api/v1/item/getEditDetail" => Reply("""{"title":"旧名"}"""),
                    "/v/api/v1/scrap/search" => Reply("[]"),
                    _ => Reply("true")
                };
            });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);

            Assert.Equal("file", client.GetItemFilesAsync(Connection, "m").GetAwaiter().GetResult().Single()["media_guid"]!.GetValue<string>());
            var detail = client.GetItemEditDetailAsync(Connection, "m").GetAwaiter().GetResult();
            client.SaveItemEditDetailAsync(Connection, detail).GetAwaiter().GetResult();
            client.RefreshItemAsync(Connection, FnNasItemActions.Refresh("m", FnNasItemActions.RefreshMissing)).GetAwaiter().GetResult();
            client.SearchScrapAsync(Connection, FnNasItemActions.SearchScrap("m", FnNasItemActions.SearchByTitle, "片名", "zh-CN"))
                .GetAwaiter().GetResult();
            client.RescrapAsync(Connection, FnNasItemActions.Rescrap(
                    new Momoka.Emby.EmbyItem { Id = "m", Type = Momoka.Emby.EmbyItemType.Movie },
                    new FnNasScrapResult("片名", "tmdb", "42", "Movie", ""),
                    [new FnNasVersion("file", "/a.mkv", 1, "1080p")]))
                .GetAwaiter().GetResult();
            client.UnscrapAsync(Connection, "m").GetAwaiter().GetResult();
            client.DeleteItemAsync(Connection, "m", FnNasItemActions.Delete(true, ["file"])).GetAwaiter().GetResult();

            var sent = transport.Requests.Select(request =>
                $"{request.Method.Method} {request.Uri.AbsolutePath} {request.Body}").ToArray();
            Assert.Equal("GET /v/api/v1/media/itemfile/m ", sent[0]);
            Assert.Equal("POST /v/api/v1/item/getEditDetail {\"item_guid\":\"m\"}", sent[1]);
            Assert.Equal("POST /v/api/v1/item/saveEditDetail {\"title\":\"旧名\"}", sent[2]);
            Assert.Equal("POST /v/api/v1/item/refresh {\"item_guid\":\"m\",\"refresh_mode\":1}", sent[3]);
            Assert.Equal("POST /v/api/v1/scrap/search {\"item_guid\":\"m\",\"source\":\"title\",\"source_keyword\":\"片名\",\"lan\":\"zh-CN\"}", sent[4]);
            Assert.DoesNotContain("\"scrap_tv_type\"", sent[5]);
            Assert.Contains("\"media_guids\":[\"file\"]", sent[5]);
            Assert.Equal("DELETE /v/api/v1/scrap/m ", sent[6]);
            Assert.Equal("DELETE /v/api/v1/item/m {\"delete_file\":1,\"media_guids\":[\"file\"]}", sent[7]);
        });
    }

    private static void CheckSignature(SentRequest request)
    {
        var header = request.Headers["authx"];
        var parts = header.Split('&').ToDictionary(part => part.Split('=', 2)[0], part => part.Split('=', 2)[1]);
        Assert.Equal(header, FnNasSigner.Create(request.Method, request.Uri, request.Body, parts["nonce"], long.Parse(parts["timestamp"], System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static HttpResponseMessage Reply(string data) => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"code\":0,\"data\":" + data + "}") };

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string Body, Dictionary<string, string> Headers);

    private sealed class FixtureTransport(Func<SentRequest, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        public bool Disposed { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sent = new SentRequest(request.Method, request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase));
            Requests.Add(sent);
            return reply(sent);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}

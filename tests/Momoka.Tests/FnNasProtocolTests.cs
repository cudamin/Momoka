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
        Test("飞牛管理：普通用户被客户端拒绝，不能发出写操作", () =>
        {
            using var transport = new FixtureTransport(_ => Reply("{\"is_admin\":0}"));
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var error = Assert.Catch<FnNasApiException>(() => new FnNasClient(http)
                .SetScheduledTasksEnabledAsync(Connection, true).GetAwaiter().GetResult());
            Assert.Equal(-5, error.Code);
            Assert.Equal(1, transport.Requests.Count);
            Assert.Equal(HttpMethod.Get, transport.Requests[0].Method);
        });
        Test("飞牛设置：只保存支持的字段，忽略转码和网盘字段编辑", () =>
        {
            using var transport = new FixtureTransport(request => request.Uri.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)
                ? Reply("{\"is_admin\":1}") : request.Method == HttpMethod.Get
                    ? Reply("{\"name\":\"旧名\",\"gpu_acc\":1,\"direct_link_enable\":1,\"direct_link_allowed_level\":0,\"future\":{\"kept\":true},\"mediasrv_cache_vol_index\":1}")
                    : Reply("true"));
            using var http = new FnNasHttp(transport, disposeHandler: false);
            new FnNasClient(http).UpdateServerSettingsAsync(Connection,
                new JsonObject
                {
                    ["name"] = "新名",
                    ["gpu_acc"] = 0,
                    ["direct_link_enable"] = 0,
                    ["direct_link_allowed_level"] = 1,
                    ["mediasrv_cache_vol_index"] = 2
                }).GetAwaiter().GetResult();
            var body = JsonNode.Parse(transport.Requests[^1].Body)!;
            Assert.Equal("新名", body["name"]!.GetValue<string>());
            Assert.Equal(1, body["gpu_acc"]!.GetValue<int>());
            Assert.Equal(1, body["direct_link_enable"]!.GetValue<int>());
            Assert.Equal(0, body["direct_link_allowed_level"]!.GetValue<int>());
            Assert.True(body["future"]!["kept"]!.GetValue<bool>());
            Assert.Equal(2, body["mediasrv_cache_vol_index"]!.GetValue<int>());
        });
        Test("飞牛用户：新建强制普通用户且哈希密码，编辑空密码按官方表示保留", () =>
        {
            using var transport = new FixtureTransport(request => Reply(request.Method == HttpMethod.Get ? "{\"is_admin\":1}" : "true"));
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            var draft = new JsonObject { ["username"] = "fixture", ["password"] = "new-password", ["is_admin"] = 1 };
            client.CreateUserAsync(Connection, draft).GetAwaiter().GetResult();
            var created = transport.Requests[^1];
            Assert.Equal("/v/api/v2/manager/user/create", created.Uri.AbsolutePath);
            Assert.Equal(HttpMethod.Put, created.Method);
            var body = JsonNode.Parse(created.Body)!;
            Assert.Equal(0, body["is_admin"]!.GetValue<int>());
            Assert.Equal(FnNasSigner.HashPassword("new-password"), body["password"]!.GetValue<string>());
            Assert.Equal("new-password", draft["password"]!.GetValue<string>());
            client.UpdateUserAsync(Connection, "user-guid", new JsonObject { ["username"] = "changed", ["password"] = "", ["guid"] = "untrusted" }).GetAwaiter().GetResult();
            Assert.Equal("/v/api/v2/manager/user/user-guid", transport.Requests[^1].Uri.AbsolutePath);
            var updated = JsonNode.Parse(transport.Requests[^1].Body)!.AsObject();
            Assert.False(updated.ContainsKey("guid"));
            Assert.Equal("", updated["password"]!.GetValue<string>());
        });
        Test("飞牛任务：启停使用字符串 1 与 2，非数值或布尔", () =>
        {
            using var transport = new FixtureTransport(request => Reply(request.Method == HttpMethod.Get ? "{\"is_admin\":1}" : "true"));
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            client.SetScheduledTasksEnabledAsync(Connection, true).GetAwaiter().GetResult();
            Assert.Equal("{\"task_switch\":\"1\"}", transport.Requests[^1].Body);
            client.SetScheduledTasksEnabledAsync(Connection, false).GetAwaiter().GetResult();
            Assert.Equal("{\"task_switch\":\"2\"}", transport.Requests[^1].Body);
        });
        Test("飞牛管理：权限查询期间切换身份不再发送后续写入", () =>
        {
            var current = true;
            using var transport = new FixtureTransport(_ =>
            {
                current = false;
                return Reply("{\"is_admin\":1}");
            });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var guarded = Connection with { IsCurrent = () => current };
            Assert.Throws<OperationCanceledException>(() => new FnNasClient(http)
                .SetScheduledTasksEnabledAsync(guarded, true).GetAwaiter().GetResult());
            Assert.Equal(1, transport.Requests.Count);
            Assert.Equal(HttpMethod.Get, transport.Requests.Single().Method);
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

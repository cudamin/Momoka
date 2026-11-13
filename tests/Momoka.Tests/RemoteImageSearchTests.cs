using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>执行实际图片搜索客户端和作用域服务；所有请求由匿名内存传输回答。</summary>
internal static class RemoteImageSearchTests
{
    private const string ItemId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PlaceholderToken = "remote-image-fixture-token";
    private const string Source = "FanArt / 中文 & name=other?#%+";
    private const string Result = """
        {"Images":[{"Url":"https://images.invalid/art.jpg","ProviderName":"FanArt","Language":"zh"}],
         "Providers":["FanArt","TheTVDB"],"TotalRecordCount":75}
        """;
    private static readonly (MediaServerKind Kind, string Version)[] Servers =
    [
        (MediaServerKind.Emby, "4.10.1.0"),
        (MediaServerKind.Jellyfin, "10.10.7"),
        (MediaServerKind.Jellyfin, "12.2.0")
    ];

    public static void Register()
    {
        foreach (var (kind, version) in Servers)
        {
            foreach (var allLanguages in new[] { true, false })
                Test($"图片搜索：{kind} {version} 来源转义、语言{allLanguages}和60张上限", () =>
                {
                    using var fixture = new ClientFixture(kind, version);
                    var found = Wait(fixture.Client.GetRemoteImagesAsync(ItemId, "Disc", new(Source, allLanguages), default));
                    var sent = fixture.Transport.Sent.Single();
                    AssertRequest(sent, kind, ItemId, "Disc", allLanguages);
                    Assert.Equal(Source, sent.Query["ProviderName"]);
                    Assert.Equal(4, sent.Query.Count, "来源字符不能形成额外查询参数");
                    Assert.Contains(Uri.EscapeDataString(Source), sent.Uri.AbsoluteUri);
                    Assert.Equal(1, found.Images.Count);
                    Assert.Equal("zh", found.Images[0].Language);
                    Assert.Equal("FanArt,TheTVDB", string.Join(',', found.Providers));
                    Assert.Equal(75, found.TotalRecordCount, "保留总数，不把当前60张上限当作总数");
                });

            Test($"图片搜索：{kind} {version} 全部来源省略参数并支持关闭语言", () =>
            {
                using var fixture = new ClientFixture(kind, version);
                foreach (var source in new string?[] { null, "", " \t\r\n" })
                    Wait(fixture.Client.GetRemoteImagesAsync(ItemId, "Thumb", new(source, false), default));
                Assert.Equal(3, fixture.Transport.Sent.Count);
                foreach (var sent in fixture.Transport.Sent)
                {
                    AssertRequest(sent, kind, ItemId, "Thumb", false);
                    Assert.False(sent.Query.ContainsKey("ProviderName"));
                    Assert.Equal(3, sent.Query.Count);
                }
            });

            Test($"图片搜索：{kind} {version} 原三参数入口保留全部语言默认", () =>
            {
                using var fixture = new ClientFixture(kind, version);
                Wait(fixture.Client.GetRemoteImagesAsync(ItemId, "Primary", default));
                var sent = fixture.Transport.Sent.Single();
                AssertRequest(sent, kind, ItemId, "Primary", true);
                Assert.False(sent.Query.ContainsKey("ProviderName"));
            });

            Test($"图片搜索：{kind} {version} 非法条目标识零请求", () =>
            {
                using var fixture = new ClientFixture(kind, version);
                foreach (var id in new string?[] { null, "", " ", ".", "..", "a/b", "a\\b", "a?x", "a#x" })
                    Assert.Throws<ArgumentException>(() => Wait(fixture.Client.GetRemoteImagesAsync(id!, "Disc", new(), default)));
                Assert.Equal(0, fixture.Transport.Sent.Count);
            });

            Test($"图片搜索：{kind} {version} 预取消零请求", () =>
            {
                using var fixture = new ClientFixture(kind, version);
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                Assert.Throws<OperationCanceledException>(() => Wait(fixture.Client.GetRemoteImagesAsync(ItemId, "Disc", new(), canceled.Token)));
                Assert.Equal(0, fixture.Transport.Sent.Count);
            });

            Test($"图片搜索：{kind} {version} 请求中取消不返回候选", () =>
            {
                using var fixture = new ClientFixture(kind, version);
                using var canceled = new CancellationTokenSource();
                fixture.Transport.AfterCapture = canceled.Cancel;
                Assert.Throws<OperationCanceledException>(() => Wait(fixture.Client.GetRemoteImagesAsync(ItemId, "Disc", new(), canceled.Token)));
                Assert.Equal(1, fixture.Transport.Sent.Count);
            });
        }

        Test("图片搜索：失败响应不得降成空候选", () =>
        {
            using var fixture = new ClientFixture(MediaServerKind.Emby, "4.10.1.0");
            fixture.Transport.Status = HttpStatusCode.Forbidden;
            var error = Assert.Catch<EmbyApiException>(() => Wait(fixture.Client.GetRemoteImagesAsync(ItemId, "Disc", new(), default)));
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            Assert.Equal(1, fixture.Transport.Sent.Count);
        });

        Test("图片搜索服务：新旧重载均固定条目并发送当前筛选", () =>
        {
            using var fixture = new ScopeFixture();
            var artwork = new EmbyLibraryArtwork(fixture.Scope, fixture.Item, default);
            Wait(artwork.FindAsync("Primary"));
            Wait(artwork.FindAsync("Disc", new(Source, false)));
            Assert.Equal(2, fixture.Transport.Sent.Count);
            AssertRequest(fixture.Transport.Sent[0], MediaServerKind.Emby, ItemId, "Primary", true);
            AssertRequest(fixture.Transport.Sent[1], MediaServerKind.Emby, ItemId, "Disc", false);
            Assert.Equal(Source, fixture.Transport.Sent[1].Query["ProviderName"]);
        });

        Test("图片搜索服务：同身份更新令牌后用最新客户端", () =>
        {
            using var fixture = new ScopeFixture();
            var artwork = new EmbyLibraryArtwork(fixture.Scope, fixture.Item, default);
            fixture.Session.ReplaceClient(fixture.Scope, fixture.Scope.CurrentConnection with { AccessToken = "updated-fixture-token" });
            Wait(artwork.FindAsync("Thumb", new("TheTVDB")));
            Assert.Equal("updated-fixture-token", fixture.Transport.Sent.Single().Header("X-Emby-Token"));
            Assert.DoesNotContain("updated-fixture-token", fixture.Transport.Sent.Single().Uri.AbsoluteUri);
        });

        foreach (var duringRequest in new[] { false, true })
        {
            Test($"图片搜索服务：页面{(duringRequest ? "请求中" : "请求前")}取消拒绝结果", () =>
            {
                using var fixture = new ScopeFixture();
                using var page = new CancellationTokenSource();
                var artwork = new EmbyLibraryArtwork(fixture.Scope, fixture.Item, page.Token);
                if (duringRequest) fixture.Transport.AfterCapture = page.Cancel;
                else page.Cancel();
                Assert.Throws<OperationCanceledException>(() => Wait(artwork.FindAsync("Disc", new())));
                Assert.Equal(duringRequest ? 1 : 0, fixture.Transport.Sent.Count);
            });

            Test($"图片搜索服务：身份{(duringRequest ? "请求中" : "请求前")}切换拒绝结果", () =>
            {
                using var fixture = new ScopeFixture();
                var artwork = new EmbyLibraryArtwork(fixture.Scope, fixture.Item, default);
                if (duringRequest) fixture.Transport.AfterCapture = fixture.ClearCurrent;
                else fixture.ClearCurrent();
                Assert.True(fixture.Scope.IsAvailable, "来源仍在线，单独核对默认身份边界");
                Assert.Throws<OperationCanceledException>(() => Wait(artwork.FindAsync("Disc", new())));
                Assert.Equal(duringRequest ? 1 : 0, fixture.Transport.Sent.Count);
            });

            Test($"图片搜索服务：来源{(duringRequest ? "请求中" : "请求前")}退出拒绝结果", () =>
            {
                using var fixture = new ScopeFixture();
                var artwork = new EmbyLibraryArtwork(fixture.Scope, fixture.Item, default);
                if (duringRequest) fixture.Transport.AfterCapture = fixture.Revoke;
                else fixture.Revoke();
                Assert.Throws<OperationCanceledException>(() => Wait(artwork.FindAsync("Disc", new())));
                Assert.Equal(duringRequest ? 1 : 0, fixture.Transport.Sent.Count);
            });
        }
    }

    private static void AssertRequest(Sent sent, MediaServerKind kind, string id, string type, bool allLanguages)
    {
        Assert.Equal("GET", sent.Method);
        Assert.Equal("https", sent.Uri.Scheme);
        Assert.Equal("remote-image.invalid", sent.Uri.Host);
        Assert.Equal(kind == MediaServerKind.Emby ? $"/media/emby/Items/{id}/RemoteImages" : $"/media/Items/{id}/RemoteImages", sent.Uri.AbsolutePath);
        Assert.Equal(type, sent.Query["Type"]);
        Assert.Equal(allLanguages ? "true" : "false", sent.Query["IncludeAllLanguages"]);
        Assert.Equal("60", sent.Query["Limit"]);
        Assert.Equal("", sent.Body);
        Assert.DoesNotContain(PlaceholderToken, sent.Uri.AbsoluteUri);
        Assert.False(sent.Query.Keys.Any(key => key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Equals("api_key", StringComparison.OrdinalIgnoreCase)));
        if (kind == MediaServerKind.Jellyfin)
        {
            Assert.Contains("MediaBrowser Client=", sent.Header("Authorization"));
            Assert.Contains($"Token=\"{PlaceholderToken}\"", sent.Header("Authorization"));
            Assert.Equal("", sent.Header("X-Emby-Token"));
            Assert.Equal("", sent.Header("X-Emby-Authorization"));
        }
        else
        {
            Assert.Equal(PlaceholderToken, sent.Header("X-Emby-Token"));
            Assert.Contains("MediaBrowser Client=", sent.Header("X-Emby-Authorization"));
            Assert.Equal("", sent.Header("Authorization"));
        }
    }

    private static EmbyConnection Connection(MediaServerKind kind, string version) => new(
        new Uri(kind == MediaServerKind.Emby ? "https://remote-image.invalid/media/emby/" : "https://remote-image.invalid/media/"),
        PlaceholderToken, "fixture-user", "anonymous", "anonymous", DeviceIdentity.Create("remote-image-fixture", "1"))
    { ServerKind = kind, ServerVersion = version, ServerId = "fixture-server" };

    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private sealed record Sent(string Method, Uri Uri, string Body, Dictionary<string, string> Headers)
    {
        public Dictionary<string, string> Query => Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).ToDictionary(pair => System.Uri.UnescapeDataString(pair[0]),
                pair => System.Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : ""), StringComparer.OrdinalIgnoreCase);
        public string Header(string name) => Headers.GetValueOrDefault(name, "");
    }

    private sealed class Transport : HttpMessageHandler
    {
        public List<Sent> Sent { get; } = [];
        public Action? AfterCapture { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("remote-image.invalid", request.RequestUri!.Host, "仅登记的匿名请求可以进入夹具");
            Sent.Add(new(request.Method.Method, request.RequestUri,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase)));
            AfterCapture?.Invoke();
            return new(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? Result : "fixture denied") };
        }
    }

    private sealed class ClientFixture : IDisposable
    {
        private readonly EmbyHttp _http;
        public Transport Transport { get; } = new();
        public EmbyClient Client { get; }

        public ClientFixture(MediaServerKind kind, string version)
        {
            _http = new(Transport);
            Client = new(_http, Connection(kind, version));
        }
        public void Dispose() => _http.Dispose();
    }

    private sealed class ScopeFixture : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        public Transport Transport { get; } = new();
        public EmbySession Session { get; }
        public EmbySessionScope Scope { get; }
        public EmbyItem Item { get; }

        public ScopeFixture()
        {
            var secret = new DenySecrets();
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "remote-image-no-IO|forbidden"));
            Session = new(new AppSettings(), new SettingsStore(paths, secret), new CredentialVault(secret),
                DeviceIdentity.Create("remote-image-fixture", "1"), Transport);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
            var connection = Connection(MediaServerKind.Emby, "4.10.1.0");
            var identity = new EmbySessionScope.Identity(new(http, connection),
                new ServerProfile { Id = "fixture-server", Url = connection.ApiBase.AbsoluteUri, ServerKind = MediaServerKind.Emby },
                new AccountProfile { Id = "fixture-account", UserId = connection.UserId });
            Scope = new(Session, identity);
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
            sources.Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, Scope);
            Item = Scope.Bind(new EmbyItem { Id = ItemId, Name = "匿名图片条目", Type = EmbyItemType.Movie });
        }

        public void ClearCurrent() => typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, null);
        public void Revoke() => Scope.IdentityState.Lifetime.Cancel();
        public void Dispose() => Session.Dispose();
    }

    private sealed class DenySecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("图片夹具禁止凭据与设置IO");
        public string Unprotect(string value) => throw new InvalidOperationException("图片夹具禁止凭据与设置IO");
    }
}

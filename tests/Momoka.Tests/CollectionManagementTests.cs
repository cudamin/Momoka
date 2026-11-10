using System.Net;
using System.Reflection;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CollectionManagementTests
{
    private static readonly BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Register()
    {
        Test("合集权限：Jellyfin普通用户的授权贯穿策略和菜单，Emby不借用该授权", () =>
        {
            var policy = JsonSerializer.Deserialize<EmbyUserPolicy>("""{"IsAdministrator":false,"EnableCollectionManagement":true}""")!;
            var jellyfin = policy.ApplyTo(Connection(MediaServerKind.Jellyfin));
            Assert.True(Permission(jellyfin), "授权字段不能在策略映射时丢失");
            var permissions = Permissions(jellyfin);
            var movie = new EmbyItem { Id = "movie", Type = EmbyItemType.Movie };
            Assert.True(ItemMenu.For(movie, jellyfin: true, permissions: permissions).Any(row => row.Command == ItemCommand.AddToCollection));
            Assert.False(ItemMenu.For(movie, permissions: Permissions(policy.ApplyTo(Connection(MediaServerKind.Emby)))).Any(row => row.Command == ItemCommand.AddToCollection));
        });

        Test("合集权限：明确拒绝收起合集操作，未知保持既有服务器裁决", () =>
        {
            var movie = new EmbyItem { Id = "movie", Type = EmbyItemType.Movie };
            var denied = JsonSerializer.Deserialize<EmbyUserPolicy>("""{"IsAdministrator":false,"EnableCollectionManagement":false}""")!;
            Assert.False(ItemMenu.For(movie, jellyfin: true, permissions: Permissions(denied.ApplyTo(Connection(MediaServerKind.Jellyfin)))).Any(row => row.Command == ItemCommand.AddToCollection));
            Assert.True(ItemMenu.For(movie, jellyfin: true).Any(row => row.Command == ItemCommand.AddToCollection));
        });

        RegisterScopeOperations();

        Test("Emby条目授权：新鲜普通账号授权可用，降权后旧卡不能借用", () =>
        {
            using var fixture = new CollectionFixture(MediaServerKind.Emby);
            fixture.Movie.CanEditItems = true;
            Service(fixture).AddAsync("set", fixture.Movie, default).GetAwaiter().GetResult();
            fixture.Replace(false);
            Assert.False(ItemMenu.For(fixture.Movie, permissions: fixture.Capabilities.PermissionsFor(fixture.Source.Key))
                .Any(row => row.Command == ItemCommand.AddToCollection));
            Assert.Throws<UnauthorizedAccessException>(() => Service(fixture).RemoveAsync(Membership(fixture, "set", "movie"), fixture.Movie, default).GetAwaiter().GetResult());
            Assert.Throws<UnauthorizedAccessException>(() => Service(fixture).AddAsync("set", fixture.Movie, default).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Transport.Count("Collections/set/Items"));
            var fresh = fixture.Source.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, CanEditItems = true });
            Service(fixture).AddAsync("set", fresh, default).GetAwaiter().GetResult();
            Assert.Equal(2, fixture.Transport.Count("Collections/set/Items"));
        });
        Test("Emby条目授权：401降权后的认证重试拒绝旧条目授权", () =>
        {
            using var fixture = new CollectionFixture(MediaServerKind.Emby);
            fixture.Movie.CanEditItems = true;
            fixture.Transport.Fail("Collections/set/Items", HttpStatusCode.Unauthorized)
                .When("Collections/set/Items", () => fixture.Replace(false));
            Assert.Throws<UnauthorizedAccessException>(() => Service(fixture).RemoveAsync(Membership(fixture, "set", "movie"), fixture.Movie, default).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Transport.Count("Collections/set/Items"));
        });
        Test("Emby条目授权：读取期间降权，迟到条目不标成新鲜授权", () =>
        {
            using var fixture = new CollectionFixture(MediaServerKind.Emby);
            var before = fixture.Source.PermissionRevision;
            fixture.Transport.Answer("Items/movie", """{"Id":"movie","Type":"Movie","CanEditItems":true}""")
                .When("Items/movie", () => fixture.Replace(false));
            var read = fixture.Source.ExecuteAsync((client, token) => client.GetItemAsync("movie", token), default).GetAwaiter().GetResult();
            Assert.Equal(before, read.PermissionReadRevision);
            Assert.False(ItemCollections.CanManage(MediaServerKind.Emby, fixture.Capabilities.PermissionsFor(fixture.Source.Key), read));
            var fresh = fixture.Source.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, CanEditItems = true });
            Assert.False(ItemFingerprint.Same(read, fresh), "同授权值但不同权限代次不能保留旧卡");
        });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"合集成员移除：{kind}使用关系端点而不删除媒体", () =>
            {
                var transport = new StubTransport().Answer("Collections/set/Items", "");
                var client = new EmbyClient(new EmbyHttp(transport), Connection(kind));
                client.RemoveFromCollectionAsync("set", ["movie"], CancellationToken.None).GetAwaiter().GetResult();
                var sent = transport.Only("Collections/set/Items");
                Assert.Equal(kind == MediaServerKind.Emby ? "POST" : "DELETE", sent.Method);
                Assert.Equal(kind == MediaServerKind.Emby ? "https://collection.invalid/emby/Collections/set/Items/Delete?Ids=movie" : "https://collection.invalid/Collections/set/Items?Ids=movie", sent.Url);
                Assert.Equal(1, transport.Total);
                Assert.Equal("", sent.Body);
            });
        }

        Test("Emby合集权限：条目CanEditItems明确拒绝优先于管理员", () =>
        {
            var item = JsonSerializer.Deserialize<EmbyItem>("""{"Id":"movie","Type":"Movie","CanEditItems":false}""")!;
            Assert.False(ItemMenu.For(item, permissions: new(IsAdministrator: true)).Any(row => row.Command == ItemCommand.AddToCollection));
        });

        Test("Emby合集权限：条目CanEditItems授权可独立于管理员", () =>
        {
            var item = JsonSerializer.Deserialize<EmbyItem>("""{"Id":"movie","Type":"Movie","CanEditItems":true}""")!;
            Assert.True(ItemMenu.For(item, permissions: new(IsAdministrator: false)).Any(row => row.Command == ItemCommand.AddToCollection));
        });
    }

    private static void RegisterScopeOperations()
    {
        Test("合集成员移除：捕获的合集和成员身份不能被后来页面或默认来源替换", () =>
        {
            using var fixture = new CollectionFixture();
            var operation = Service(fixture);
            var membership = Membership(fixture, "set", "movie");
            operation.RemoveAsync(membership, fixture.Movie, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("DELETE", fixture.Transport.Only("Collections/set/Items").Method);
            Assert.Equal(0, fixture.Transport.Count("Items/movie"), "成员移除不能走媒体DELETE");
        });

        Test("合集成员移除：页面上下文取消后不再发请求", () =>
        {
            using var fixture = new CollectionFixture();
            using var page = new CancellationTokenSource();
            var membership = Membership(fixture, "set", "movie", page.Token);
            page.Cancel();
            Assert.Throws<OperationCanceledException>(() => Service(fixture).RemoveAsync(membership, fixture.Movie, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Total);
        });

        Test("合集成员移除：上下文不能移除另一张卡或另一来源", () =>
        {
            using var fixture = new CollectionFixture();
            var membership = Membership(fixture, "set", "movie");
            var other = fixture.Source.Bind(new EmbyItem { Id = "other", Type = EmbyItemType.Movie });
            Assert.Throws<InvalidOperationException>(() => Service(fixture).RemoveAsync(membership, other, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Total);
        });

        Test("合集权限：同身份快照变化后来源与能力读当前授权", () =>
        {
            using var fixture = new CollectionFixture();
            Assert.Equal(true, fixture.Source.CanManageCollections);
            fixture.Replace(false);
            Assert.Equal(false, fixture.Source.CanManageCollections);
            Assert.Equal(false, fixture.Capabilities.PermissionsFor(fixture.Source.Key).CanManageCollections);
            Assert.True(fixture.Source.Connection.CanManageCollections == true, "捕获连接仍保持旧值，当前权限必须独立读");
        });

        Test("合集权限：已撤销授权阻止成员移除及已有合集追加", () =>
        {
            using var fixture = new CollectionFixture();
            var membership = Membership(fixture, "set", "movie");
            var service = Service(fixture);
            fixture.Replace(false);
            Assert.Throws<UnauthorizedAccessException>(() => service.RemoveAsync(membership, fixture.Movie, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<UnauthorizedAccessException>(() => service.AddAsync("set", fixture.Movie, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Total);
        });

        Test("合集权限：身份退出后旧操作不能借新来源执行", () =>
        {
            using var fixture = new CollectionFixture();
            var membership = Membership(fixture, "set", "movie");
            fixture.Revoke();
            Assert.Throws<OperationCanceledException>(() => Service(fixture).RemoveAsync(membership, fixture.Movie, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Total);
        });

        Test("合集权限：认证替换期间撤销授权，重试不得再发关系写入", () =>
        {
            using var fixture = new CollectionFixture();
            fixture.Transport.Fail("Collections/set/Items", HttpStatusCode.Unauthorized)
                .When("Collections/set/Items", () => fixture.Replace(false));
            Assert.Throws<UnauthorizedAccessException>(() => Service(fixture).RemoveAsync(Membership(fixture, "set", "movie"), fixture.Movie, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Transport.Count("Collections/set/Items"), "旧请求失败后不能用降权客户端再写一次");
        });

        Test("合集权限：认证替换仍获授权，重试只发原合集原成员", () =>
        {
            using var fixture = new CollectionFixture();
            fixture.Transport.Sequence("Collections/set/Items", (HttpStatusCode.Unauthorized, ""), (HttpStatusCode.OK, ""))
                .When("Collections/set/Items", () => fixture.Replace(true));
            Service(fixture).RemoveAsync(Membership(fixture, "set", "movie"), fixture.Movie, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(2, fixture.Transport.Count("Collections/set/Items"));
            Assert.True(fixture.Transport.SentTo("Collections/set/Items").All(sent => sent.Url.EndsWith("?Ids=movie", StringComparison.Ordinal)));
        });

        Test("合集接口：非法路径、空成员和不支持产品在HTTP之前拒绝", () =>
        {
            using var transport = new StubTransport();
            using var http = new EmbyHttp(transport);
            var client = new EmbyClient(http, Connection(MediaServerKind.Emby));
            Assert.Throws<ArgumentException>(() => client.RemoveFromCollectionAsync("../set", ["movie"], default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.RemoveFromCollectionAsync("set", [], default).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.RemoveFromCollectionAsync("set", ["movie,other"], default).GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => new EmbyClient(http, Connection(MediaServerKind.Plex)).RemoveFromCollectionAsync("set", ["movie"], default).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Total);
        });

        Test("合集权限：旧能力探测结果不能覆盖读取期间更新的授权", () =>
        {
            using var fixture = new CollectionFixture();
            fixture.Transport.Answer("Users/Me", """{"Id":"user","Policy":{"IsAdministrator":false,"EnableCollectionManagement":true}}""")
                .Answer("System/Info/Public", """{"ServerName":"anonymous","Version":"10.10.7"}""")
                .When("Users/Me", () => fixture.Replace(false));
            fixture.Capabilities.ProbeAsync().GetAwaiter().GetResult();
            Assert.Equal(false, fixture.Source.CanManageCollections);
            Assert.Equal(false, fixture.Capabilities.PermissionsFor(fixture.Source.Key).CanManageCollections,
                "旧GET权限不能标记成新revision缓存");
        });

        Test("合集权限：条目编辑授权变化必须使卡片快照失效", () =>
        {
            var first = new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, CanEditItems = true };
            var denied = new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, CanEditItems = false };
            Assert.False(ItemFingerprint.Same(first, denied));
        });

        Test("合集权限：能力探测的普通用户授权同时进入scope当前快照", () =>
        {
            using var fixture = new CollectionFixture();
            fixture.Replace(false);
            fixture.Transport.Answer("Users/Me", """{"Id":"user","Policy":{"IsAdministrator":false,"EnableCollectionManagement":true}}""")
                .Answer("System/Info/Public", """{"ServerName":"anonymous","Version":"10.10.7"}""");
            fixture.Capabilities.ProbeAsync().GetAwaiter().GetResult();
            Assert.Equal(true, fixture.Source.CanManageCollections);
            Assert.Equal(true, fixture.Capabilities.PermissionsFor(fixture.Source.Key).CanManageCollections);
        });
        Test("合集权限：内部确认通知时系统管理员缓存与来源权限已经一致", () =>
        {
            using var fixture = RestrictedEmbyFixture();
            var notifications = 0;
            bool? system = null;
            ItemMenuPermissions observed = default;
            var locksReleased = false;
            var capabilityGate = typeof(ServerCapabilities).GetField("_gate", Members)!.GetValue(fixture.Capabilities)!;
            var lifecycleGate = typeof(EmbySession).GetField("_lifecycleGate", Members)!.GetValue(fixture.Session)!;
            fixture.Session.PermissionsChanged += (_, source) =>
            {
                notifications++;
                system = fixture.Capabilities.IsAdministrator;
                observed = fixture.Capabilities.PermissionsFor(source.Key);
                locksReleased = !Monitor.IsEntered(capabilityGate) && !Monitor.IsEntered(lifecycleGate);
            };
            fixture.Capabilities.ProbeAsync().GetAwaiter().GetResult();
            Assert.Equal(1, notifications);
            Assert.Equal(false, system);
            Assert.Equal(false, observed.IsAdministrator);
            Assert.Equal(false, observed.CanManageCollections);
            Assert.True(locksReleased, "用户事件不能在能力锁或会话锁内执行");
            Assert.Equal(false, fixture.Capabilities.IsAdministrator);
            Assert.Equal(new Version(4, 10, 1, 0), fixture.Capabilities.ServerVersion);
        });
        Test("合集权限：内部确认通知重入显式升权，旧Probe不覆盖新快照且版本保留", () =>
        {
            using var fixture = RestrictedEmbyFixture();
            var notifications = 0;
            bool? beforeSystem = null;
            bool? beforeSource = null;
            fixture.Session.PermissionsChanged += (_, source) =>
            {
                notifications++;
                beforeSystem = fixture.Capabilities.IsAdministrator;
                beforeSource = fixture.Capabilities.PermissionsFor(source.Key).IsAdministrator;
                fixture.Session.ReplaceClient(source, source.CurrentConnection with { IsAdministrator = true, CanManageCollections = true });
            };
            fixture.Capabilities.ProbeAsync().GetAwaiter().GetResult();
            Assert.Equal(1, notifications);
            Assert.Equal(false, beforeSystem);
            Assert.Equal(false, beforeSource);
            Assert.Equal(true, fixture.Capabilities.IsAdministrator);
            Assert.Equal(true, fixture.Capabilities.PermissionsFor(fixture.Source.Key).IsAdministrator);
            Assert.Equal(true, fixture.Source.CanManageCollections);
            Assert.Equal(new Version(4, 10, 1, 0), fixture.Capabilities.ServerVersion);
        });
        Test("合集权限：新探测同值拒绝先完成，旧探测不能恢复授权", async () =>
        {
            using var transport = new ProbeOrderTransport();
            using var fixture = new CollectionFixture(handler: transport);
            fixture.Replace(false);
            var oldProbe = fixture.Capabilities.ProbeAsync();
            await transport.OldEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Capabilities.ProbeAsync();
            transport.ReleaseOld.TrySetResult();
            await oldProbe;
            Assert.Equal(false, fixture.Source.CanManageCollections);
            Assert.Equal(false, fixture.Capabilities.PermissionsFor(fixture.Source.Key).CanManageCollections);
        });

        Test("合集写入：已有合集追加的Emby条目授权不绕过另一个目标", () =>
        {
            using var fixture = new CollectionFixture();
            var service = Service(fixture);
            Assert.Throws<ArgumentException>(() => service.AddAsync("../set", fixture.Movie, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Total);
        });

        Test("合集关系上下文：账号相同但合集和媒体id不能自指", () =>
        {
            using var fixture = new CollectionFixture();
            var membership = Membership(fixture, "movie", "movie");
            Assert.Throws<InvalidOperationException>(() => Service(fixture).RemoveAsync(membership, fixture.Movie, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Total);
        });

        Test("合集菜单：只有有效当前合集成员才能看到移除关系", () =>
        {
            using var fixture = new CollectionFixture();
            var membership = Membership(fixture, "set", "movie");
            var rows = ItemMenu.For(fixture.Movie, jellyfin: true,
                permissions: fixture.Capabilities.PermissionsFor(fixture.Source.Key), collectionMembership: membership);
            Assert.True(rows.Any(row => row.Command == ItemCommand.RemoveFromCollection));
            fixture.Revoke();
            rows = ItemMenu.For(fixture.Movie, jellyfin: true,
                permissions: fixture.Capabilities.PermissionsFor(fixture.Source.Key), collectionMembership: membership);
            Assert.False(rows.Any(row => row.Command == ItemCommand.RemoveFromCollection));
        });
    }

    private static CollectionManagement Service(CollectionFixture fixture)
    {
        return new CollectionManagement(fixture.Source, () => fixture.Capabilities.PermissionsFor(fixture.Source.Key));
    }

    private static CollectionFixture RestrictedEmbyFixture()
    {
        var fixture = new CollectionFixture(MediaServerKind.Emby);
        fixture.Session.ReplaceClient(fixture.Source, fixture.Source.CurrentConnection with { IsAdministrator = true, CanManageCollections = null });
        fixture.Transport.Answer("System/Info/Public", """{"ServerName":"anonymous","Version":"4.10.1.0"}""")
            .Fail("System/Info", HttpStatusCode.Forbidden)
            .Answer("Users/user", """{"Id":"user","Policy":{"IsAdministrator":false}}""");
        return fixture;
    }

    private static CollectionMembership Membership(CollectionFixture fixture, string collectionId, string itemId, CancellationToken page = default)
    {
        return new CollectionMembership(fixture.Source, collectionId, itemId, page);
    }

    private sealed class CollectionFixture : IDisposable
    {
        private readonly EmbyHttp _http;
        private readonly Dictionary<string, EmbySessionScope> _sources;
        public EmbySession Session { get; }
        public EmbySessionScope Source { get; }
        public ServerCapabilities Capabilities { get; }
        public StubTransport Transport { get; } = new StubTransport().Answer("Collections/set/Items", "");
        public EmbyItem Movie { get; }

        public CollectionFixture(MediaServerKind kind = MediaServerKind.Jellyfin, HttpMessageHandler? handler = null)
        {
            var secret = new DenySecrets();
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "collection-no-IO|forbidden"));
            Session = new EmbySession(new AppSettings(), new SettingsStore(paths, secret), new CredentialVault(secret), DeviceIdentity.Create("collection", "0"), handler ?? Transport);
            _http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
            _sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
            var server = new ServerProfile { Id = "server", Name = "anonymous", Url = "https://collection.invalid/", ServerKind = kind };
            var account = new AccountProfile { Id = "account", UserId = "user", Username = "anonymous" };
            var connection = Connection(kind) with { IsAdministrator = false, CanManageCollections = true };
            var state = new EmbySessionScope.Identity(new EmbyClient(_http, connection), server, account);
            Source = new EmbySessionScope(Session, state);
            _sources.Add(Source.Key, Source);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, Source);
            Capabilities = new ServerCapabilities(Session);
            Movie = Source.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie });
        }

        public void Replace(bool permission) => Session.ReplaceClient(Source, Source.CurrentConnection with { CanManageCollections = permission });
        public void Revoke()
        {
            _sources.Remove(Source.Key);
            Source.IdentityState.Lifetime.Cancel();
        }
        public void Dispose() => Session.Dispose();
    }

    private sealed class ProbeOrderTransport : HttpMessageHandler
    {
        private int _users;
        public TaskCompletionSource OldEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("collection.invalid", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return Reply("""{"ServerName":"anonymous","Version":"10.10.7"}""");
            if (request.RequestUri.AbsolutePath.EndsWith("/Users/Me", StringComparison.Ordinal))
            {
                // Jellyfin's system capability read also asks Users/Me. Hold the second read,
                // which is the original probe's per-source permission request.
                var first = Interlocked.Increment(ref _users) == 2;
                if (first)
                {
                    OldEntered.TrySetResult();
                    await ReleaseOld.Task.WaitAsync(cancellationToken);
                }
                return Reply("{\"Id\":\"user\",\"Policy\":{\"IsAdministrator\":false,\"EnableCollectionManagement\":" + (first ? "true" : "false") + "}}");
            }
            throw new AssertionException("未登记匿名权限端点 " + request.RequestUri.AbsolutePath);
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }

    private sealed class DenySecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("fixture forbids secrets");
        public string Unprotect(string value) => throw new InvalidOperationException("fixture forbids secrets");
    }

    private static bool Permission(EmbyConnection connection)
    {
        return connection.CanManageCollections == true;
    }

    private static ItemMenuPermissions Permissions(EmbyConnection connection) => new(
        connection.IsAdministrator, connection.CanDownload, connection.CanDeleteContent,
        connection.CanManageSubtitles, connection.CanManageCollections);

    private static EmbyConnection Connection(MediaServerKind kind) => new(
        new Uri(kind == MediaServerKind.Emby ? "https://collection.invalid/emby/" : "https://collection.invalid/"),
        "placeholder", "user", "anonymous", "anonymous", DeviceIdentity.Create("collection-fixture", "0"))
    { ServerKind = kind, ServerVersion = kind == MediaServerKind.Jellyfin ? "10.10.7" : "4.10.1.0" };
}

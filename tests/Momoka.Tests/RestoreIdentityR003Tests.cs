using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// R-003 executes the real restore/session/gateway and settings writer with an anonymous transport.
/// The protector accepts only this fixture's placeholders; no server, saved user profile or DPAPI
/// endpoint is used. Request bodies/headers stay in memory and are never included in assertions.
/// </summary>
internal static class RestoreIdentityR003Tests
{
    private const string ServerA = "r003-server-a";
    private const string ServerB = "r003-server-b";
    private const string UserId = "r003-user";
    private const string Username = "r003-anonymous";
    private const string OldToken = "r003-placeholder-old";
    private const string FreshToken = "r003-placeholder-fresh";
    private const string NewerToken = "r003-placeholder-newer";
    private const string Password = "r003-placeholder-password";
    private static readonly CancellationToken None = CancellationToken.None;

    public static void Register()
    {
        RegisterFinalIdentity();
        RegisterLegacyCorrection();
        RegisterIdentityDimensions();
        RegisterAwaitGuards();
        RegisterConcurrentRestore();
    }

    private static void Case(string name, Func<Task> run) =>
        Test("R003 " + name, () => run().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());

    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void RegisterFinalIdentity()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            foreach (var hasToken in new[] { true, false })
            {
                var branch = hasToken ? "保存令牌401后密码恢复" : "只有密码恢复";
                foreach (var returnedId in new string?[] { ServerB, null, "" })
                {
                    var result = returnedId == ServerB ? "AuthB同用户" : returnedId is null ? "Auth缺ServerId再PublicB" : "Auth空ServerId再PublicB";
                    Case($"{kind} {branch}：{result}拒绝且不存新凭据、不采用来源", async () =>
                    {
                        using var fixture = new Fixture(kind, hasToken);
                        fixture.AuthenticationServerId = returnedId;
                        fixture.PostLoginServerId = ServerB;
                        Assert.False(await fixture.Restore(), "密码恢复不得采用另一服务器的同名用户");
                        fixture.AssertRejected(hasToken);
                        fixture.AssertRequestBoundary(hasToken);
                    });
                }

                Case($"{kind} {branch}：认证用户ID错配拒绝且保留原档案", async () =>
                {
                    using var fixture = new Fixture(kind, hasToken) { AuthenticationUserId = "r003-other-user" };
                    Assert.False(await fixture.Restore());
                    fixture.AssertRejected(hasToken);
                    fixture.AssertRequestBoundary(hasToken);
                });

                Case($"{kind} {branch}：后登录产品与API基址错配拒绝", async () =>
                {
                    using var fixture = new Fixture(kind, hasToken)
                    {
                        PostLoginKind = kind == MediaServerKind.Emby ? MediaServerKind.Jellyfin : MediaServerKind.Emby
                    };
                    Assert.False(await fixture.Restore(), "同一ServerId和用户不能跨产品或API入口恢复");
                    fixture.AssertRejected(hasToken);
                    fixture.AssertRequestBoundary(hasToken);
                });

                foreach (var returnedId in new string?[] { ServerA, null })
                    Case($"{kind} {branch}：{(returnedId is null ? "Auth缺ID由PublicA确认" : "匹配A")}换令牌成功", async () =>
                    {
                        using var fixture = new Fixture(kind, hasToken) { AuthenticationServerId = returnedId };
                        Assert.True(await fixture.Restore());
                        fixture.AssertRestored();
                        fixture.AssertRequestBoundary(hasToken);
                    });
            }

            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable })
                Case($"{kind} 保存令牌探针{(int)status}不回退密码、不清令牌", async () =>
                {
                    using var fixture = new Fixture(kind) { TokenProbeStatus = status };
                    Assert.False(await fixture.Restore());
                    fixture.AssertRejected(false);
                    Assert.Equal(0, fixture.Transport.AuthenticationCount);
                    Assert.True(fixture.Transport.Sent.Where(sent => sent.IsPublic).All(sent => !sent.IsAuthenticated));
                });
        }

        foreach (var hasToken in new[] { true, false })
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable })
                Case($"Jellyfin 密码认证{(int)status}拒绝不改原密码、服务器或来源（有令牌={hasToken}）", async () =>
                {
                    using var fixture = new Fixture(MediaServerKind.Jellyfin, hasToken) { AuthenticationStatus = status };
                    Assert.False(await fixture.Restore());
                    fixture.AssertRejected(hasToken);
                    Assert.Equal(1, fixture.Transport.AuthenticationCount);
                });

        Case("匿名预探测发现ServerB时不发原令牌或密码", async () =>
        {
            using var fixture = new Fixture { InitialServerId = ServerB };
            Assert.False(await fixture.Restore());
            fixture.AssertRejected(false);
            Assert.Equal(0, fixture.Transport.AuthenticationCount);
            Assert.True(fixture.Transport.Sent.All(sent => sent.IsPublic && !sent.IsAuthenticated));
        });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            foreach (var hasToken in new[] { true, false })
                foreach (var initialId in new string?[] { "", null })
                {
                    Case($"{kind} 初公开ID{(initialId is null ? "缺失" : "空串")}保留档案A，AuthB不能采用（有令牌={hasToken}）", async () =>
                    {
                        using var fixture = new Fixture(kind, hasToken)
                        {
                            InitialServerId = initialId,
                            AuthenticationServerId = ServerB,
                            PostLoginServerId = ServerB
                        };
                        Assert.False(await fixture.Restore(), "空公开信息不得擦除原档案的已知服务器身份");
                        fixture.AssertRejected(hasToken);
                        fixture.AssertRequestBoundary(hasToken);
                    });

                    Case($"{kind} 初公开ID{(initialId is null ? "缺失" : "空串")}保留档案A，匹配A仍恢复（有令牌={hasToken}）", async () =>
                    {
                        using var fixture = new Fixture(kind, hasToken) { InitialServerId = initialId };
                        Assert.True(await fixture.Restore(), "原已知身份相符时不能被空公开信息误拒绝");
                        fixture.AssertRestored();
                        fixture.AssertRequestBoundary(hasToken);
                    });
                }

        Case("原档案没有ServerId时也守住刚确认的PublicA，不被PublicB替换", async () =>
        {
            using var fixture = new Fixture { AuthenticationServerId = null, PostLoginServerId = ServerB };
            fixture.Server.ServerId = null;
            fixture.SaveCheckpoint();
            Assert.False(await fixture.Restore());
            fixture.AssertRejected(true);
        });

        Case("匹配A允许名称、版本和令牌变化，不把非身份字段当成另一个人", async () =>
        {
            using var fixture = new Fixture
            {
                AuthenticationUsername = "r003-renamed-anonymous",
                PostLoginName = "R003 renamed server",
                PostLoginVersion = "12.3.0"
            };
            Assert.True(await fixture.Restore());
            fixture.AssertRestored();
            Assert.Equal("r003-renamed-anonymous", fixture.Account.Username);
            Assert.Equal("12.3.0", fixture.Session.Connection!.ServerVersion);
        });
    }

    private static void RegisterLegacyCorrection()
    {
        foreach (var address in new[] { "https://r003.invalid/media/", "https://r003.invalid/media/emby/" })
            foreach (var hasToken in new[] { true, false })
            {
                Case($"旧Emby→Jellyfin合法纠正 {new Uri(address).AbsolutePath} 有令牌={hasToken} 密码恢复仍成功", async () =>
                {
                    using var fixture = new Fixture(MediaServerKind.Emby, hasToken, MediaServerKind.Jellyfin, address);
                    Assert.True(await fixture.Restore(), "最终身份应比较纠正后的预期连接，不是旧Emby标签");
                    fixture.AssertRestored();
                    fixture.AssertRequestBoundary(hasToken);
                    Assert.Equal(address.TrimEnd('/'), fixture.Server.Url);
                });

                Case($"旧Emby→Jellyfin纠正后仍拒绝PublicB {new Uri(address).AbsolutePath} 有令牌={hasToken}", async () =>
                {
                    using var fixture = new Fixture(MediaServerKind.Emby, hasToken, MediaServerKind.Jellyfin, address)
                    {
                        AuthenticationServerId = null,
                        PostLoginServerId = ServerB
                    };
                    Assert.False(await fixture.Restore());
                    fixture.AssertRejected(hasToken);
                    fixture.AssertRequestBoundary(hasToken);
                });
            }
    }

    private static void RegisterIdentityDimensions()
    {
        var expected = new EmbyConnection(new Uri("https://r003.invalid/media/"), OldToken, UserId, Username,
            "R003 server", DeviceIdentity.Create("r003-anonymous-device", "1"))
        {
            ServerKind = MediaServerKind.Jellyfin,
            ServerId = ServerA
        };
        // These exercise the existing production predicate independently: the real gateway's current
        // product correction also rebases, so its product/base mismatch cases cannot isolate the base.
        foreach (var address in new[] { "https://r003-other.invalid/media/", "https://r003.invalid/other/",
            "http://r003.invalid/media/", "https://r003.invalid/Media/" })
            Test("R003 同身份判据：独立API基址错配 " + address, () =>
                Assert.False(expected.IsSameIdentityAs(expected with { ApiBase = new Uri(address) })));

        Test("R003 同身份判据：规范等价API基址及新令牌、名称仍是原身份", () =>
            Assert.True(expected.IsSameIdentityAs(expected with
            {
                ApiBase = new Uri("https://R003.invalid:443/media/"),
                AccessToken = FreshToken,
                UserName = "r003-renamed-anonymous",
                ServerName = "R003 renamed server",
                ServerVersion = "12.3.0"
            })));
    }

    private static void RegisterAwaitGuards()
    {
        foreach (var stage in new[] { "认证", "后登录公开信息" })
            foreach (var hasToken in new[] { true, false })
            {
                Case($"{stage}await期间取消不落盘、不复活来源（有令牌={hasToken}）", async () =>
                {
                    using var fixture = new Fixture(MediaServerKind.Jellyfin, hasToken);
                    using var cancellation = new CancellationTokenSource();
                    var (entered, reply) = Block(fixture, stage);
                    var restore = fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, cancellation.Token);
                    await entered.Task;
                    cancellation.Cancel();
                    reply.SetResult(stage == "认证" ? fixture.AuthenticationReply() : fixture.PublicReply(true));
                    await ExpectCancelled(restore);
                    fixture.AssertRejected(hasToken);
                });

                Case($"{stage}await期间退出改变代次，迟到密码恢复不得覆盖（有令牌={hasToken}）", async () =>
                {
                    using var fixture = new Fixture(MediaServerKind.Jellyfin, hasToken);
                    var (entered, reply) = Block(fixture, stage);
                    var restore = fixture.Restore();
                    await entered.Task;
                    fixture.Session.SignOut(fixture.Server, fixture.Account);
                    fixture.SaveCheckpoint();
                    reply.SetResult(stage == "认证" ? fixture.AuthenticationReply() : fixture.PublicReply(true));
                    Assert.False(await restore);
                    fixture.AssertRejected(false);
                    Assert.Equal(false, fixture.Account.AutoConnect);
                });

                foreach (var address in new[] { "https://r003-moved.invalid/media/", "https://r003.invalid/other/" })
                    Case($"{stage}await期间原地址变为{new Uri(address).AbsolutePath}/{new Uri(address).Host} 拒绝旧结果（有令牌={hasToken}）", async () =>
                    {
                        using var fixture = new Fixture(MediaServerKind.Jellyfin, hasToken);
                        var (entered, reply) = Block(fixture, stage);
                        var restore = fixture.Restore();
                        await entered.Task;
                        fixture.Server.Url = address;
                        fixture.SaveCheckpoint();
                        reply.SetResult(stage == "认证" ? fixture.AuthenticationReply() : fixture.PublicReply(true));
                        Assert.False(await restore);
                        fixture.AssertRejected(false);
                    });
            }
    }

    private static (TaskCompletionSource<bool> Entered, TaskCompletionSource<HttpResponseMessage> Reply) Block(Fixture fixture, string stage)
    {
        var entered = Signal<bool>();
        var reply = Signal<HttpResponseMessage>();
        fixture.Transport.Reply = (sent, _) =>
        {
            if (stage == "认证" ? sent.IsAuthentication : sent.IsPublic && sent.AuthenticationCount > 0)
            {
                entered.TrySetResult(true);
                // Deliberately ignore cancellation at the endpoint: the actual session must guard
                // the late successful result as well as a normally cancelled HTTP request.
                return reply.Task;
            }
            return Task.FromResult(fixture.StandardReply(sent));
        };
        return (entered, reply);
    }

    private static async Task ExpectCancelled(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { return; }
        throw new AssertionException("期望原调用取消，不能返回密码恢复成功");
    }

    private static void RegisterConcurrentRestore()
    {
        foreach (var oldServerId in new[] { ServerA, ServerB })
            Case($"同账号并发恢复逆序返回：迟到{oldServerId}不覆盖新代次同身份来源或令牌", async () =>
            {
                using var fixture = new Fixture();
                var entered = Signal<bool>();
                var oldReply = Signal<HttpResponseMessage>();
                fixture.Transport.Reply = (sent, _) =>
                {
                    if (sent.IsAuthentication && sent.AuthenticationCount == 1)
                    {
                        entered.TrySetResult(true);
                        return oldReply.Task;
                    }
                    return Task.FromResult(sent.IsAuthentication
                        ? Authentication(ServerA, UserId, Username, NewerToken)
                        : fixture.StandardReply(sent));
                };
                var old = fixture.Restore();
                await entered.Task;
                Assert.True(await fixture.Restore());
                var current = fixture.Session.Capture();
                var checkpoint = fixture.ProfileState();
                oldReply.SetResult(Authentication(oldServerId, UserId, Username, FreshToken));
                Assert.False(await old, "旧代次成功也不能再次保存或替换在线身份");
                Assert.True(current.IsAvailable && current.IsCurrent);
                Assert.Equal(1, fixture.Session.Sources.Count);
                Assert.True(fixture.ProfileState() == checkpoint, "旧结果不得覆盖较新恢复保存的档案");
                Assert.True(fixture.Vault.GetAccessToken(fixture.Account) == NewerToken, "保留新代次已确认令牌");
                Assert.Equal(0, fixture.Protector.FreshTokenWrites);
                Assert.Equal(1, fixture.Protector.NewerTokenWrites);
                Assert.True(fixture.Session.Connection!.IsSameIdentityAs(current.Connection));
                fixture.AssertDiskMatchesMemory();
            });
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Authentication(string? serverId, string userId, string username, string token) =>
        Json(new { AccessToken = token, ServerId = serverId, User = new { Id = userId, Name = username, Policy = new { IsAdministrator = true } } });

    private sealed record Sent(Uri Url, string Method, string Body, string LegacyToken, string Authorization, int AuthenticationCount)
    {
        public bool IsPublic => Url.AbsolutePath.EndsWith("/System/Info/Public", StringComparison.Ordinal);
        public bool IsAuthentication => Url.AbsolutePath.EndsWith("/Users/AuthenticateByName", StringComparison.Ordinal);
        public bool IsCurrentUser => Url.AbsolutePath.EndsWith("/Users/Me", StringComparison.Ordinal);
        public bool IsViews => Url.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal) || Url.AbsolutePath.EndsWith("/UserViews", StringComparison.Ordinal);
        public bool IsAuthenticated => LegacyToken.Length > 0 || Authorization.Contains("Token=", StringComparison.Ordinal);
    }

    private sealed class Transport : HttpMessageHandler
    {
        private int _authenticationCount;
        public int AuthenticationCount => Volatile.Read(ref _authenticationCount);
        public ConcurrentQueue<Sent> Sent { get; } = new();
        public Func<Sent, CancellationToken, Task<HttpResponseMessage>> Reply { get; set; } =
            (_, _) => throw new InvalidOperationException("R003 fixture transport has no response script");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var auth = request.RequestUri!.AbsolutePath.EndsWith("/Users/AuthenticateByName", StringComparison.Ordinal);
            if (auth) Interlocked.Increment(ref _authenticationCount);
            var sent = new Sent(request.RequestUri, request.Method.Method, body,
                request.Headers.TryGetValues("X-Emby-Token", out var legacy) ? legacy.Single() : "",
                request.Headers.TryGetValues("Authorization", out var authorization) ? authorization.Single() : "",
                AuthenticationCount);
            Sent.Enqueue(sent);
            return await Reply(sent, cancellationToken);
        }
    }

    private sealed class PlaceholderProtector : ISecretProtector
    {
        private const string Prefix = "r003-test-wrapped:";
        public int FreshTokenWrites { get; private set; }
        public int NewerTokenWrites { get; private set; }
        public int Writes { get; private set; }

        public string Protect(string plainText)
        {
            if (plainText is not (OldToken or FreshToken or NewerToken or Password))
                throw new InvalidOperationException("R003 protector accepts only fixture placeholders");
            Writes++;
            if (plainText == FreshToken) FreshTokenWrites++;
            if (plainText == NewerToken) NewerTokenWrites++;
            return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }

        public string Unprotect(string cipherText)
        {
            if (!cipherText.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("R003 protector refuses non-fixture profiles");
            return Encoding.UTF8.GetString(Convert.FromBase64String(cipherText[Prefix.Length..]));
        }

        public void ResetWrites() { Writes = 0; FreshTokenWrites = 0; NewerTokenWrites = 0; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(WorkspaceRoot(), "artifacts", "staged-repair", "batch07", "r003", "fixtures", Guid.NewGuid().ToString("N"));
        private string _serverCheckpoint = "";
        private string _accountCheckpoint = "";
        private string _tokenCheckpoint = "";
        private readonly string _originalUrl;
        public PlaceholderProtector Protector { get; } = new();
        public Transport Transport { get; } = new();
        public AppSettings Settings { get; } = new() { AggregateMode = true };
        public SettingsStore Store { get; }
        public CredentialVault Vault { get; }
        public EmbySession Session { get; }
        public ServerProfile Server { get; }
        public AccountProfile Account { get; }
        public MediaServerKind InitialKind { get; }
        public string? InitialServerId { get; set; } = ServerA;
        public string? AuthenticationServerId { get; set; } = ServerA;
        public string? PostLoginServerId { get; set; } = ServerA;
        public string AuthenticationUserId { get; set; } = UserId;
        public string AuthenticationUsername { get; set; } = Username;
        public string PostLoginName { get; set; } = "R003 confirmed A";
        public string PostLoginVersion { get; set; } = "12.2.0";
        public MediaServerKind PostLoginKind { get; set; }
        public HttpStatusCode TokenProbeStatus { get; set; } = HttpStatusCode.Unauthorized;
        public HttpStatusCode AuthenticationStatus { get; set; } = HttpStatusCode.OK;
        public Uri ExpectedBase => EmbyServerAddress.Normalize(_originalUrl, InitialKind);

        public Fixture(MediaServerKind savedKind = MediaServerKind.Jellyfin, bool hasToken = true,
            MediaServerKind? initialKind = null, string address = "https://r003.invalid/media/")
        {
            _originalUrl = address;
            InitialKind = initialKind ?? savedKind;
            PostLoginKind = InitialKind;
            Directory.CreateDirectory(_root);
            Server = new ServerProfile
            {
                Id = "r003-profile",
                Url = address,
                ServerKind = savedKind,
                ServerId = ServerA,
                ServerVersion = "12.2.0",
                Name = ServerProfile.DefaultName
            };
            Account = new AccountProfile
            {
                Id = "r003-account",
                UserId = UserId,
                Username = Username,
                AutoConnect = true,
                LastSignedIn = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
            };
            Vault = new CredentialVault(Protector);
            Vault.SetPassword(Account, Password, true);
            if (hasToken) Vault.SetAccessToken(Account, OldToken);
            Server.Accounts.Add(Account);
            Settings.Servers.Clear();
            Settings.Servers.Add(Server);
            Settings.Remember(Server, Account);
            Store = new SettingsStore(new AppPaths(_root), Protector);
            SaveCheckpoint();
            Protector.ResetWrites();
            Session = new EmbySession(Settings, Store, Vault, DeviceIdentity.Create("r003-anonymous-device", "1"), Transport);
            Transport.Reply = (sent, _) => Task.FromResult(StandardReply(sent));
        }

        public Task<bool> Restore() => Session.TryRestoreAsync(Server, Account, None);
        public HttpResponseMessage AuthenticationReply() => AuthenticationStatus == HttpStatusCode.OK
            ? Authentication(AuthenticationServerId, AuthenticationUserId, AuthenticationUsername, FreshToken)
            : Json(new { }, AuthenticationStatus);
        public HttpResponseMessage PublicReply(bool afterLogin) => Json(new
        {
            ProductName = (afterLogin ? PostLoginKind : InitialKind).ToString(),
            Id = afterLogin ? PostLoginServerId : InitialServerId,
            ServerName = afterLogin ? PostLoginName : "R003 confirmed A",
            Version = afterLogin ? PostLoginVersion : "12.2.0"
        });

        public HttpResponseMessage StandardReply(Sent sent)
        {
            if (sent.IsPublic) return PublicReply(sent.AuthenticationCount > 0);
            if (sent.IsAuthentication) return AuthenticationReply();
            if (sent.IsCurrentUser || sent.IsViews) return Json(new { }, TokenProbeStatus);
            throw new InvalidOperationException("R003 fixture received an unplanned request path");
        }

        public void SaveCheckpoint()
        {
            Store.Save(Settings);
            _serverCheckpoint = ServerState(Server);
            _accountCheckpoint = AccountState(Account);
            _tokenCheckpoint = Account.ProtectedAccessToken;
        }

        private static string ServerState(ServerProfile server) => JsonSerializer.Serialize(new
        {
            server.Id,
            server.Url,
            server.ServerKind,
            server.ServerId,
            server.ServerVersion,
            server.Name
        });
        private static string AccountState(AccountProfile account) => JsonSerializer.Serialize(new
        {
            account.Id,
            account.UserId,
            account.Username,
            account.LastSignedIn,
            account.AutoConnect,
            account.RememberPassword,
            account.ProtectedPassword
        });
        public string ProfileState() => JsonSerializer.Serialize(Server);

        public void AssertRejected(bool invalidatedToken)
        {
            Assert.False(Session.IsSignedIn, "拒绝最终身份后不能创建在线来源");
            Assert.Equal(0, Session.Sources.Count);
            Assert.Null(Session.Connection);
            Assert.True(ReferenceEquals(Server, Settings.Servers.Single()));
            Assert.True(ReferenceEquals(Account, Server.Accounts.Single()));
            Assert.True(ServerState(Server) == _serverCheckpoint, "保留原ServerId、产品、地址、名称和版本");
            Assert.True(AccountState(Account) == _accountCheckpoint, "不更换原用户、密码、自动连接或登录时间");
            Assert.True(Account.ProtectedAccessToken == (invalidatedToken ? "" : _tokenCheckpoint),
                "仅保存令牌已确认401时清除旧令牌，绝不写入被拒身份的新令牌");
            Assert.Equal(0, Protector.Writes, "拒绝结果不得调用凭据保护写入");
            AssertDiskMatchesMemory();
        }

        public void AssertRestored()
        {
            Assert.True(Session.IsSignedIn);
            Assert.Equal(1, Session.Sources.Count);
            var connection = Session.Connection!;
            Assert.Equal(ServerA, connection.ServerId);
            Assert.Equal(UserId, connection.UserId);
            Assert.Equal(InitialKind, connection.ServerKind);
            Assert.Equal(ExpectedBase, connection.ApiBase);
            Assert.True(connection.AccessToken == FreshToken, "同身份成功才采用新令牌");
            Assert.True(Vault.GetAccessToken(Account) == FreshToken, "同身份成功才保存新令牌");
            Assert.True(Vault.GetPassword(Account) == Password, "保留原保存密码");
            Assert.Equal(1, Protector.FreshTokenWrites);
            Assert.Equal(2, Protector.Writes);
            Assert.Equal(ServerA, Server.ServerId);
            Assert.Equal(InitialKind, Server.ServerKind);
            Assert.Null(Session.TakeRestoredViews(), "密码恢复没有伪造已恢复媒体库");
            AssertDiskMatchesMemory();
        }

        public void AssertRequestBoundary(bool hasToken)
        {
            Assert.Equal(1, Transport.AuthenticationCount);
            var sent = Transport.Sent.ToArray();
            Assert.True(sent.All(request => request.Url.Host == "r003.invalid"), "只请求匿名测试来源");
            Assert.True(sent.All(request => !request.Url.Query.Contains("api_key", StringComparison.OrdinalIgnoreCase)
                && !request.Url.AbsoluteUri.Contains("r003-placeholder", StringComparison.Ordinal)), "URL不携带凭据");
            Assert.True(sent.Where(request => request.IsPublic).All(request => !request.IsAuthenticated), "前后公开信息均匿名");
            var login = sent.Single(request => request.IsAuthentication);
            Assert.Equal(new Uri(ExpectedBase, "Users/AuthenticateByName"), login.Url);
            Assert.False(login.IsAuthenticated, "密码登录不携带保存的旧令牌");
            using var body = JsonDocument.Parse(login.Body);
            Assert.True(body.RootElement.GetProperty("Pw").GetString() == Password, "只发送本夹具占位密码");
            var probes = sent.Where(request => request.IsAuthenticated).ToArray();
            Assert.Equal(hasToken ? 1 : 0, probes.Length);
            if (hasToken)
            {
                Assert.True(probes[0].Url.AbsolutePath.StartsWith(ExpectedBase.AbsolutePath, StringComparison.Ordinal));
                if (InitialKind == MediaServerKind.Jellyfin)
                    Assert.True(probes[0].LegacyToken.Length == 0 && probes[0].Authorization.Contains("Token=", StringComparison.Ordinal));
                else Assert.True(probes[0].LegacyToken == OldToken);
            }
        }

        public void AssertDiskMatchesMemory()
        {
            var saved = Store.LoadStrict().Servers.Single();
            Assert.True(JsonSerializer.Serialize(saved) == ProfileState(), "实际隔离设置文件应与内存档案一致");
            Assert.Equal(Server.Id, Settings.LastServerId);
            Assert.Equal(Account.Id, Settings.LastAccountId);
        }

        public void Dispose()
        {
            Session.Dispose();
            var full = Path.GetFullPath(_root);
            var boundary = Path.GetFullPath(Path.Combine(WorkspaceRoot(), "artifacts", "staged-repair", "batch07", "r003", "fixtures")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("R003 fixture cleanup escaped its workspace boundary");
            Directory.Delete(full, true);
        }
    }

    private static string WorkspaceRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Momoka.sln"))) return current.FullName;
        throw new InvalidOperationException("R003 fixture cannot locate its current workspace");
    }
}

using System.Net;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// R-005: real Core session and Plex transport boundary, with synthetic v2 account responses.
/// Official static auth-form code passes the bare signin object through and consumes its id/authToken.
/// No request can escape this handler, and all persistence stays under this worktree's artifacts.
/// This is not live plex.tv, PMS, DPAPI, two-factor or PIN verification.
/// </summary>
internal static class PlexV2LoginTests
{
    private const string Username = "fixture-user";
    private const string Password = "fixture-password";
    private const string Token = "fixture-r005-token";
    private const string ServerId = "fixture-machine";
    private const string Valid = "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}";

    public static void Register()
    {
        Test("Plex v2 R005：顶层账号对象到目标 PMS 核对后才持久化", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.AtServer = () => fixture.AssertNotAdopted();
            fixture.Login();
            Assert.Equal(1, fixture.Transport.AccountCalls);
            Assert.Equal(1, fixture.Transport.ServerCalls);
            Assert.Equal("7", fixture.Account.UserId);
            Assert.Equal(Username, fixture.Account.Username);
            Assert.Equal(Token, fixture.Vault.GetAccessToken(fixture.Account));
            Assert.Equal(Password, fixture.Vault.GetPassword(fixture.Account));
            Assert.True(fixture.Session.IsSignedIn);
            Assert.Equal(1, fixture.Session.Sources.Count);
            var connection = fixture.Session.Capture().Connection;
            Assert.True(connection.IsPlex);
            Assert.Equal("7", connection.UserId);
            Assert.Equal(Username, connection.UserName);
            Assert.Equal(ServerId, connection.ServerId);
            Assert.Equal("fixture-server", connection.ServerName);
            Assert.True(File.Exists(fixture.Paths.SettingsFile));
            var saved = fixture.Store.Load().Servers.Single().Accounts.Single();
            Assert.Equal("7", saved.UserId);
            Assert.Equal(Username, saved.Username);
            Assert.Equal(Token, fixture.Vault.GetAccessToken(saved));
        });
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        {
            Test($"Plex v2 R005：目标 PMS {(int)status} 不 Adopt、不持久化", () =>
            {
                using var fixture = new Fixture();
                fixture.Transport.ServerStatus = status;
                var error = Assert.Catch<Exception>(() => fixture.Login());
                Assert.True(error is EmbyAuthenticationException or PlexApiException,
                    "合法 v2 账号响应必须先到达目标 PMS，而不是在账号解析中失败");
                Assert.Equal(1, fixture.Transport.AccountCalls);
                Assert.Equal(1, fixture.Transport.ServerCalls);
                fixture.AssertNotAdopted();
                Assert.DoesNotContain(Token, error.ToString());
                Assert.DoesNotContain(Password, error.ToString());
            });
            Test($"Plex v2 R005：账号服务 {(int)status} 不访问 PMS、不持久化", () =>
            {
                using var fixture = new Fixture();
                fixture.Transport.AccountStatus = status;
                var error = Assert.Catch<EmbyAuthenticationException>(() => fixture.Login());
                Assert.Equal(1, fixture.Transport.AccountCalls);
                Assert.Equal(0, fixture.Transport.ServerCalls);
                fixture.AssertNotAdopted();
                Assert.DoesNotContain(Token, error.ToString());
                Assert.DoesNotContain(Password, error.ToString());
            });
        }
        foreach (var (label, json) in new (string, string)[]
        {
            ("缺少 id", "{\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("缺少 token", "{\"id\":7,\"username\":\"fixture-user\"}"),
            ("null id", "{\"id\":null,\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("null token", "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":null}"),
            ("布尔 id", "{\"id\":true,\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("对象 id", "{\"id\":{},\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("数组 id", "{\"id\":[],\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("小数 id", "{\"id\":7.5,\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("零 id", "{\"id\":0,\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("负 id", "{\"id\":-7,\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}"),
            ("数字 token", "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":1}"),
            ("对象 token", "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":{}}"),
            ("数组 token", "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":[]}"),
            ("空 token", "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":\"\"}"),
            ("空白 token", "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":\" \"}"),
            ("控制符 token", "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":\"fixture\\ncredential\"}"),
            ("数字 username", "{\"id\":7,\"username\":123,\"authToken\":\"fixture-r005-token\"}"),
            ("对象 username", "{\"id\":7,\"username\":{},\"authToken\":\"fixture-r005-token\"}"),
            ("数组 username", "{\"id\":7,\"username\":[],\"authToken\":\"fixture-r005-token\"}"),
            ("非对象成功 JSON", "[]"),
            ("无效成功 JSON", "not-json"),
            ("未证实的嵌套 user shape", "{\"user\":{\"id\":7,\"username\":\"fixture-user\"},\"authToken\":\"fixture-r005-token\"}"),
            ("顶层账号不借嵌套 token", "{\"id\":7,\"username\":\"fixture-user\",\"user\":{\"authToken\":\"fixture-r005-token\"}}"),
            ("字符串 id 契约未获官方证实", "{\"id\":\"7\",\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-token\"}")
        })
        {
            Test("Plex v2 R005：拒绝 " + label, () =>
            {
                using var fixture = new Fixture();
                fixture.Transport.AccountJson = json;
                var error = Assert.Catch<Exception>(() => fixture.Login());
                Assert.True(error is EmbyApiException or PlexApiException);
                Assert.Equal(1, fixture.Transport.AccountCalls);
                Assert.Equal(0, fixture.Transport.ServerCalls);
                fixture.AssertNotAdopted();
                Assert.DoesNotContain(Token, error.ToString());
            });
        }
        Test("Plex v2 R005：账号 id 不截为 Int32、username 取同一顶层", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.AccountJson = "{\"id\":2147483648,\"username\":\"官方账号名\",\"title\":\"展示名\",\"authToken\":\"fixture-r005-token\",\"user\":{\"id\":99,\"username\":\"另一账号\"}}";
            fixture.Login();
            Assert.Equal("2147483648", fixture.Account.UserId);
            Assert.Equal("官方账号名", fixture.Account.Username);
            Assert.Equal("2147483648", fixture.Session.Capture().Connection.UserId);
        });
        Test("Plex v2 R005：缺少 username 可读同一对象的 title，不借提交登录名", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.AccountJson = "{\"id\":7,\"title\":\"显示账号名\",\"authToken\":\"fixture-r005-token\"}";
            fixture.Login();
            Assert.Equal("显示账号名", fixture.Account.Username);
            Assert.Equal("7", fixture.Account.UserId);
        });
        Test("Plex v2 R005：缺少显示名不放弃已核实的账号 id", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.AccountJson = "{\"id\":7,\"authToken\":\"fixture-r005-token\"}";
            fixture.Login();
            Assert.Equal("", fixture.Account.Username);
            Assert.Equal("7", fixture.Account.UserId);
        });
        Test("Plex v2 R005：账号 307 同源跳转保持 POST 正文、token 不串到登录头", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.RedirectAccount = new Uri("https://plex.tv/api/v2/users/signin/fixture");
            fixture.Login();
            Assert.Equal(2, fixture.Transport.AccountCalls);
            Assert.Equal(1, fixture.Transport.ServerCalls);
        });
        Test("Plex v2 R005：账号跨源重定向不重放密码，不 Adopt", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.RedirectAccount = new Uri("https://external.invalid/signin");
            Assert.Throws<PlexApiException>(() => fixture.Login());
            Assert.Equal(1, fixture.Transport.AccountCalls);
            Assert.Equal(0, fixture.Transport.ServerCalls);
            fixture.AssertNotAdopted();
        });
        Test("Plex v2 R005：PMS 跨源重定向不重放 token，不 Adopt", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.RedirectServer = new Uri("https://external.invalid/");
            Assert.Throws<PlexApiException>(() => fixture.Login());
            Assert.Equal(1, fixture.Transport.AccountCalls);
            Assert.Equal(1, fixture.Transport.ServerCalls);
            fixture.AssertNotAdopted();
        });
        Test("Plex v2 R005：登录前取消不发送请求", () =>
        {
            using var fixture = new Fixture();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Login(cancelled.Token));
            Assert.Equal(0, fixture.Transport.Requests.Count);
            fixture.AssertNotAdopted();
        });
        Test("Plex v2 R005：目标 PMS 返回期间取消不 Adopt、不持久化", () =>
        {
            using var fixture = new Fixture();
            using var cancelled = new CancellationTokenSource();
            fixture.Transport.AtServer = cancelled.Cancel;
            Assert.Throws<OperationCanceledException>(() => fixture.Login(cancelled.Token));
            Assert.Equal(1, fixture.Transport.AccountCalls);
            Assert.Equal(1, fixture.Transport.ServerCalls);
            fixture.AssertNotAdopted();
        });
        Test("Plex v2 R005：登录已被退出时迟到成功结果不 Adopt", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.AtServer = fixture.Session.SignOut;
            Assert.Throws<OperationCanceledException>(() => fixture.Login());
            Assert.Equal(1, fixture.Transport.AccountCalls);
            Assert.Equal(1, fixture.Transport.ServerCalls);
            fixture.AssertNotAdopted();
        });
        Test("Plex v2 R005：同 id 换 token 仍恢复原来源并重放只读请求", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            fixture.Transport.AccountJson = "{\"id\":7,\"username\":\"fixture-user\",\"authToken\":\"fixture-r005-renewed\"}";
            fixture.Transport.ExpireSections = true;
            var views = scope.ExecuteAsync((client, ct) => client.GetViewsAsync(ct), default).GetAwaiter().GetResult();
            Assert.Equal(1, views.Count);
            Assert.Equal(2, fixture.Transport.AccountCalls);
            Assert.Equal(2, fixture.Transport.ServerCalls);
            Assert.Equal(2, fixture.Transport.SectionCalls);
            Assert.True(scope.IsCurrent);
            Assert.Equal("7", scope.Connection.UserId);
            Assert.Equal("fixture-r005-renewed", fixture.Vault.GetAccessToken(fixture.Account));
            Assert.Equal("fixture-r005-renewed", fixture.Session.Capture().Connection.AccessToken);
        });
        Test("Plex v2 R005：重认证换账号 id 不采用新身份，不重放原请求", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            fixture.Transport.AccountJson = "{\"id\":8,\"username\":\"other-user\",\"authToken\":\"fixture-r005-other\"}";
            fixture.Transport.ExpireSections = true;
            Assert.Throws<EmbyTokenExpiredException>(() =>
                scope.ExecuteAsync((client, ct) => client.GetViewsAsync(ct), default).GetAwaiter().GetResult());
            Assert.Equal(2, fixture.Transport.AccountCalls);
            Assert.Equal(2, fixture.Transport.ServerCalls);
            Assert.Equal(1, fixture.Transport.SectionCalls);
            Assert.False(scope.IsCurrent);
            Assert.False(fixture.Session.IsSignedIn);
            Assert.Equal("7", fixture.Account.UserId);
            Assert.Equal(Username, fixture.Account.Username);
            Assert.Equal("", fixture.Vault.GetAccessToken(fixture.Account));
        });
        Test("Plex v2 R005：服务器身份变化不跨服务器重放原请求", () =>
        {
            using var fixture = new Fixture();
            fixture.Login();
            var scope = fixture.Session.Capture();
            fixture.Transport.ServerMachine = "fixture-other-machine";
            fixture.Transport.ExpireSections = true;
            Assert.Throws<EmbyTokenExpiredException>(() =>
                scope.ExecuteAsync((client, ct) => client.GetViewsAsync(ct), default).GetAwaiter().GetResult());
            Assert.Equal(2, fixture.Transport.AccountCalls);
            Assert.Equal(2, fixture.Transport.ServerCalls);
            Assert.Equal(1, fixture.Transport.SectionCalls);
            Assert.False(scope.IsCurrent);
            Assert.False(fixture.Session.IsSignedIn);
            Assert.Equal(ServerId, fixture.Server.ServerId);
        });
        Test("Plex v2 R005：保存 token 恢复保留账号 id 且不请求 signin", () =>
        {
            using var fixture = new Fixture();
            fixture.Account.UserId = "7";
            fixture.Account.Username = Username;
            fixture.Vault.SetAccessToken(fixture.Account, Token);
            Assert.True(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.AccountCalls);
            Assert.Equal(1, fixture.Transport.ServerCalls);
            Assert.Equal(1, fixture.Transport.SectionCalls);
            Assert.Equal("7", fixture.Session.Capture().Connection.UserId);
            Assert.Equal(Username, fixture.Session.Capture().Connection.UserName);
            Assert.Equal(1, fixture.Session.TakeRestoredViews()!.Count);
        });
        Test("Plex v2 R005：保存密码恢复拒绝新账号 id", () =>
        {
            using var fixture = new Fixture();
            fixture.Account.UserId = "7";
            fixture.Vault.SetPassword(fixture.Account, Password, true);
            fixture.Transport.AccountJson = "{\"id\":8,\"username\":\"other-user\",\"authToken\":\"fixture-r005-other\"}";
            Assert.False(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Transport.AccountCalls);
            Assert.Equal(1, fixture.Transport.ServerCalls);
            Assert.False(fixture.Session.IsSignedIn);
            Assert.Equal("7", fixture.Account.UserId);
            Assert.Equal("", fixture.Vault.GetAccessToken(fixture.Account));
            Assert.False(File.Exists(fixture.Paths.SettingsFile));
        });
    }

    public static int Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Register();
        return TestHarness.Run();
    }

    internal static AppPaths NewFixturePaths()
    {
        // Resolve from this assembly rather than the host: the offline driver may load it in pwsh.
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(PlexV2LoginTests).Assembly.Location)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Cannot locate current worktree for fixture output");
        return new AppPaths(Path.Combine(directory.FullName, "artifacts", "staged-repair", "batch07", "r005", "fixtures", Guid.NewGuid().ToString("N")));
    }

    private sealed class Fixture : IDisposable
    {
        public AppPaths Paths { get; }
        public SettingsStore Store { get; }
        public CredentialVault Vault { get; } = new(PassthroughSecretProtector.Instance);
        public Transport Transport { get; } = new();
        public AppSettings Settings { get; } = new();
        public ServerProfile Server { get; } = new()
        {
            ServerKind = MediaServerKind.Plex,
            Url = "http://plex.invalid:32400",
            Name = "Plex",
            ServerId = PlexV2LoginTests.ServerId,
            ServerVersion = "1.41.2"
        };
        public AccountProfile Account { get; } = new() { Username = PlexV2LoginTests.Username };
        public EmbySession Session { get; }

        public Fixture()
        {
            Paths = NewFixturePaths();
            Store = new SettingsStore(Paths, PassthroughSecretProtector.Instance);
            Server.Accounts.Add(Account);
            Settings.Servers.Add(Server);
            Session = new EmbySession(Settings, Store, Vault, DeviceIdentity.Create("fixture-r005-client", "test"), Transport);
        }

        public void Login(CancellationToken ct = default) => Session.SignInAsync(Server, Account, Password, Username, true, ct).GetAwaiter().GetResult();

        public void AssertNotAdopted()
        {
            Assert.False(Session.IsSignedIn);
            Assert.Equal(0, Session.Sources.Count);
            Assert.Equal("", Account.UserId);
            Assert.Equal("", Account.ProtectedAccessToken);
            Assert.Equal("", Account.ProtectedPassword);
            Assert.Equal(Username, Account.Username);
            Assert.True(Account.LastSignedIn is null);
            Assert.False(File.Exists(Paths.SettingsFile));
        }

        public void Dispose() => Session.Dispose();
    }

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string Body, IReadOnlyDictionary<string, string> Headers);

    private sealed class Transport : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        public string AccountJson { get; set; } = Valid;
        public HttpStatusCode AccountStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode ServerStatus { get; set; } = HttpStatusCode.OK;
        public string ServerMachine { get; set; } = ServerId;
        public Uri? RedirectAccount { get; set; }
        public Uri? RedirectServer { get; set; }
        public Action? AtServer { get; set; }
        public bool ExpireSections { get; set; }
        public int AccountCalls { get; private set; }
        public int ServerCalls { get; private set; }
        public int SectionCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            if (uri.Host is not ("plex.tv" or "plex.invalid")) throw new InvalidOperationException("Unexpected anonymous fixture host");
            var sent = new SentRequest(request.Method, uri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct),
                request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase));
            Requests.Add(sent);
            Assert.Equal("", uri.Query);
            Assert.Equal("", uri.UserInfo);
            Assert.Equal("", uri.Fragment);
            if (uri.Host == "plex.tv")
            {
                Assert.True(uri.AbsolutePath is "/api/v2/users/signin" or "/api/v2/users/signin/fixture");
                Assert.Equal("https", uri.Scheme);
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.False(sent.Headers.ContainsKey("X-Plex-Token"));
                Assert.True(sent.Headers.ContainsKey("X-Plex-Client-Identifier"));
                var body = JsonNode.Parse(sent.Body)!;
                Assert.Equal(Username, body["login"]!.GetValue<string>());
                Assert.Equal(Password, body["password"]!.GetValue<string>());
                AccountCalls++;
                if (RedirectAccount is { } redirect && AccountCalls == 1)
                    return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = redirect } };
                return new HttpResponseMessage(AccountStatus)
                {
                    Content = new StringContent(AccountStatus == HttpStatusCode.OK ? AccountJson : Token + Password),
                    ReasonPhrase = AccountStatus == HttpStatusCode.OK ? "OK" : Token + Password
                };
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            if (uri.AbsolutePath is "/System/Info/Public" or "/emby/System/Info/Public" or "/v/api/v1/sys/version")
            {
                Assert.False(sent.Headers.ContainsKey("X-Plex-Token"));
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (uri.AbsolutePath == "/identity")
            {
                Assert.False(sent.Headers.ContainsKey("X-Plex-Token"));
                return Reply("{\"machineIdentifier\":\"fixture-machine\",\"version\":\"1.41.2\"}");
            }
            Assert.True(sent.Headers.TryGetValue("X-Plex-Token", out var token));
            Assert.True(token is Token or "fixture-r005-renewed" or "fixture-r005-other");
            Assert.True(sent.Headers.ContainsKey("X-Plex-Client-Identifier"));
            if (uri.AbsolutePath == "/")
            {
                ServerCalls++;
                AtServer?.Invoke();
                if (RedirectServer is { } redirect)
                    return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = redirect } };
                return new HttpResponseMessage(ServerStatus)
                {
                    Content = new StringContent(ServerStatus == HttpStatusCode.OK
                        ? "{\"MediaContainer\":{\"friendlyName\":\"fixture-server\",\"version\":\"1.41.2\",\"machineIdentifier\":\"" + ServerMachine + "\"}}"
                        : Token + Password),
                    ReasonPhrase = ServerStatus == HttpStatusCode.OK ? "OK" : Token + Password
                };
            }
            if (uri.AbsolutePath == "/library/sections")
            {
                SectionCalls++;
                if (ExpireSections)
                {
                    ExpireSections = false;
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                return Reply("{\"Directory\":[{\"key\":\"1\",\"title\":\"fixture-library\",\"type\":\"movie\"}]}");
            }
            throw new InvalidOperationException("Unexpected anonymous fixture path");
        }

        private static HttpResponseMessage Reply(string container) => new(HttpStatusCode.OK)
        { Content = new StringContent("{\"MediaContainer\":" + container + "}") };
    }
}

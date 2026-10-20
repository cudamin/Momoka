using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class JellyfinProtocolTests
{
    private static readonly DeviceIdentity Device = DeviceIdentity.Create("jellyfin-fixture", "1");
    private static readonly Uri ApiBase = new("https://jellyfin.invalid/media/");
    private const string Public = """{"ProductName":"Jellyfin","ServerName":"Fixture","Version":"12.2.0","Id":"server-id"}""";

    public static void Register()
    {
        RegisterRestoreCorrections();
        Test("Jellyfin：反向代理基址和显示地址往返保留路径", () =>
        {
            var address = EmbyServerAddress.Normalize("https://jellyfin.invalid/media/?x=1#view", MediaServerKind.Jellyfin);
            Assert.Equal(ApiBase, address);
            Assert.Equal(address, EmbyServerAddress.Normalize(EmbyServerAddress.ToDisplayString(address, MediaServerKind.Jellyfin), MediaServerKind.Jellyfin));
            Assert.Equal("https://jellyfin.invalid/media/emby/", EmbyServerAddress.Normalize("https://jellyfin.invalid/media").AbsoluteUri);
            Assert.False(EmbySignInIdentity.SameAddress("https://jellyfin.invalid/media", "https://jellyfin.invalid/media/emby", MediaServerKind.Jellyfin));
        });
        Test("Jellyfin：公开信息里的 \"Jellyfin Server\" 也要认成 Jellyfin", () =>
        {
            var info = JsonSerializer.Deserialize<PublicSystemInfo>(
                """{"ProductName":"Jellyfin Server","ServerName":"jellyfin","Version":"10.10.7","Id":"c1"}""")!;
            Assert.Equal(MediaServerKind.Jellyfin, info.ServerKind);
            Assert.Equal(MediaServerKind.Jellyfin,
                JsonSerializer.Deserialize<PublicSystemInfo>("""{"ProductName":"Jellyfin"}""")!.ServerKind);
            Assert.Equal(MediaServerKind.Emby,
                JsonSerializer.Deserialize<PublicSystemInfo>("""{"ProductName":"Emby Server"}""")!.ServerKind);
        });
        Case("Jellyfin：匿名探测先识别产品再构建登录正文与鉴权头", async () =>
        {
            using var handler = new Transport();
            handler.Reply = sent => sent.Path.EndsWith("AuthenticateByName", StringComparison.Ordinal)
                ? Json("""{"AccessToken":"fixture-token","User":{"Id":"u1","Name":"user","Policy":{"IsAdministrator":false}}}""") : Json(Public);
            using var http = new EmbyHttp(handler);
            var gateway = new EmbyServerGateway(http, Device);
            var detected = await gateway.DetectAsync(ApiBase.AbsoluteUri, default);
            Assert.Equal(MediaServerKind.Jellyfin, detected.ServerKind);
            Assert.Equal(ApiBase, detected.ApiBase);
            var connection = await gateway.AuthenticateAsync(detected.ApiBase!, " user ", "fixture-password", default, detected.ServerKind, detected.Version, detected.Id);
            Assert.True(connection.UsesJellyfin12Routes);
            Assert.Equal(false, connection.IsAdministrator);
            var login = handler.Sent.Single(sent => sent.Path.EndsWith("AuthenticateByName", StringComparison.Ordinal));
            Assert.Contains("MediaBrowser Client=", login.Authorization);
            Assert.Equal("", login.LegacyToken);
            using var json = JsonDocument.Parse(login.Body);
            Assert.Equal("user", json.RootElement.GetProperty("Username").GetString());
            Assert.Equal("fixture-password", json.RootElement.GetProperty("Pw").GetString());
            Assert.True(handler.Sent.All(sent => !sent.Url.Contains("fixture-token", StringComparison.Ordinal)));
        });
        Case("Jellyfin：直接登录后纠正产品但保留成功入口，只有确认地址才换基址", async () =>
        {
            using var handler = new Transport();
            handler.Reply = sent => sent.Path.EndsWith("AuthenticateByName", StringComparison.Ordinal)
                ? Json("""{"AccessToken":"fixture-token","User":{"Id":"u1","Name":"user","Policy":{"IsAdministrator":true}}}""")
                : Json(Public);
            using var http = new EmbyHttp(handler);
            var gateway = new EmbyServerGateway(http, Device);
            var assumed = new Uri("https://jellyfin.invalid/media/emby/");
            var connection = await gateway.AuthenticateAsync(assumed, "user", "fixture-password", default,
                MediaServerKind.Emby);
            Assert.Equal(MediaServerKind.Jellyfin, connection.ServerKind);
            Assert.Equal(assumed, connection.ApiBase, "直接登录时保留已成功请求的完整路径");
            Assert.True(connection.IsJellyfin && connection.IsAdministrator == true);
            Assert.Equal(assumed, EmbyServerAddress.Rebase(assumed, MediaServerKind.Emby, MediaServerKind.Jellyfin));
            Assert.Equal(ApiBase, EmbyServerAddress.Rebase(assumed, MediaServerKind.Emby, MediaServerKind.Jellyfin, ApiBase));
            Assert.Equal(assumed,
                EmbyServerAddress.Rebase(ApiBase, MediaServerKind.Jellyfin, MediaServerKind.Emby),
                "反过来也要把 /emby/ 补回来");
        });
        Case("Jellyfin：保存类型误作 Emby 时，恢复后按公开信息自愈并写回配置", async () =>
        {
            using var handler = new Transport
            {
                Reply = sent => Json(sent.Path.EndsWith("Info/Public", StringComparison.Ordinal)
                    ? Public : sent.Path.EndsWith("Users/Me", StringComparison.Ordinal)
                        ? "{\"Id\":\"u1\",\"Name\":\"user\",\"Policy\":{\"IsAdministrator\":true}}" : "{\"Items\":[]}")
            };
            var settings = new AppSettings();
            var profile = new ServerProfile
            {
                Url = "https://jellyfin.invalid/media",
                ServerKind = MediaServerKind.Emby,
                ServerVersion = "10.10.7"
            };
            var account = new AccountProfile
            {
                UserId = "u1",
                Username = "user",
                ProtectedAccessToken = PassthroughSecretProtector.Instance.Protect("fixture-token")
            };
            profile.Accounts.Add(account);
            settings.Servers.Add(profile);
            var directory = Path.Combine(Path.GetTempPath(), "momoka-jellyfin-heal-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var session = new EmbySession(settings,
                    new SettingsStore(new AppPaths(directory), PassthroughSecretProtector.Instance),
                    new CredentialVault(PassthroughSecretProtector.Instance), Device, handler);
                Assert.True(await session.TryRestoreAsync(profile, account, default));
                Assert.Equal(MediaServerKind.Jellyfin, profile.ServerKind);
                Assert.Equal(MediaServerKind.Jellyfin, session.Connection!.ServerKind);
                Assert.Equal(ApiBase, session.Connection.ApiBase);
                Assert.Equal("https://jellyfin.invalid/media", profile.Url);
                Assert.Equal(ServerInterfaceFlavor.Jellyfin, new ServerCapabilities(session).InterfaceFlavor);
                Assert.Equal(true, session.Connection.IsAdministrator);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        Case("Jellyfin：新版用户路由、字段与字幕路径符合当前契约", async () =>
        {
            using var handler = new Transport { Reply = sent => Json(sent.Path.EndsWith("Latest", StringComparison.Ordinal) ? "[]" : "{\"Items\":[]}") };
            using var http = new EmbyHttp(handler);
            var client = Client(http);
            await client.GetViewsAsync(default);
            await client.GetItemsAsync(new ItemQuery(), default);
            await client.GetItemAsync("item", default);
            await client.GetLatestAsync(null, 4, default);
            await client.QueryResumeAsync(0, 4, default);
            await client.MarkPlayedAsync("item", default);
            await client.SetFavoriteAsync("item", true, default);
            await client.DeleteSubtitleAsync("item", 2, default);
            var expected = new[] { "/media/UserViews", "/media/Items", "/media/Items/item", "/media/Items/Latest", "/media/UserItems/Resume", "/media/UserPlayedItems/item", "/media/UserFavoriteItems/item", "/media/Videos/item/Subtitles/2" };
            Assert.Equal(string.Join('|', expected), string.Join('|', handler.Sent.Select(sent => sent.Path)));
            Assert.DoesNotContain("ProductionYear", handler.Sent[1].Url);
            Assert.DoesNotContain("PremiereDate", handler.Sent[1].Url);
            Assert.Contains("UserId=u1", handler.Sent[1].Url);
            Assert.True(handler.Sent.All(sent => sent.Authorization.Contains("Token=\"fixture-token\"", StringComparison.Ordinal) && sent.LegacyToken.Length == 0));
        });
        Case("Jellyfin：旧版保留用户路由但使用标准认证", async () =>
        {
            using var handler = new Transport();
            using var http = new EmbyHttp(handler);
            var client = Client(http, "10.11.0");
            await client.GetViewsAsync(default);
            await client.MarkUnplayedAsync("item", default);
            Assert.Equal("/media/Users/u1/Views", handler.Sent[0].Path);
            Assert.Equal("/media/Users/u1/PlayedItems/item", handler.Sent[1].Path);
            Assert.Contains("Token=", handler.Sent[0].Authorization);
        });
        Case("Jellyfin：普通账号权限由用户 Policy 决定且不请求管理员详情", async () =>
        {
            using var handler = new Transport
            {
                Reply = sent => Json(sent.Path.EndsWith("Users/Me", StringComparison.Ordinal)
                ? """{"Id":"u1","Policy":{"IsAdministrator":false}}""" : Public)
            };
            using var http = new EmbyHttp(handler);
            var info = await Client(http).GetSystemInfoAsync(default);
            Assert.True(info.IsRestricted);
            Assert.False(handler.Sent.Any(sent => sent.Path == "/media/System/Info"));
        });
        Test("Jellyfin：不支持的写操作和通知在请求前拒绝", () =>
        {
            using var handler = new Transport();
            using var http = new EmbyHttp(handler);
            var client = Client(http);
            Assert.Throws<NotSupportedException>(() => client.ValidateItemRefreshAsync("item", default).GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => client.ResetMetadataAsync("item", default).GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => client.HideFromResumeAsync("item", true, default).GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => client.GetNotificationUsersAsync(default).GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => client.GetNotificationsAsync(default).GetAwaiter().GetResult());
            Assert.Equal(0, handler.Sent.Count);
        });
        Case("Jellyfin：重定向离开来源后移除标准认证", async () =>
        {
            using var handler = new Transport();
            handler.Reply = _ => handler.Sent.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://cdn.invalid/file") } }
                : Json("{}");
            using var http = new EmbyHttp(handler);
            await http.GetBytesAsync(ApiBase, new(Device, "fixture-token", ServerKind: MediaServerKind.Jellyfin), default);
            Assert.Contains("Token=", handler.Sent[0].Authorization);
            Assert.Equal("", handler.Sent[1].Authorization);
            Assert.Equal("", handler.Sent[1].LegacyToken);
        });
        Case("Jellyfin：恢复保存令牌先刷新版本与权限再读取媒体库", async () =>
        {
            using var handler = new Transport
            {
                Reply = sent => Json(sent.Path.EndsWith("Info/Public", StringComparison.Ordinal) ? Public
                : sent.Path.EndsWith("Users/Me", StringComparison.Ordinal) ? """{"Id":"u1","Policy":{"IsAdministrator":true}}""" : "{\"Items\":[]}")
            };
            var settings = new AppSettings();
            var profile = new ServerProfile { Url = ApiBase.AbsoluteUri, ServerKind = MediaServerKind.Jellyfin, ServerVersion = "10.11.0" };
            var account = new AccountProfile { UserId = "u1", Username = "user", ProtectedAccessToken = PassthroughSecretProtector.Instance.Protect("fixture-token") };
            profile.Accounts.Add(account);
            settings.Servers.Add(profile);
            var directory = Path.Combine(Path.GetTempPath(), "momoka-jellyfin-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var session = new EmbySession(settings, new SettingsStore(new AppPaths(directory), PassthroughSecretProtector.Instance),
                    new CredentialVault(PassthroughSecretProtector.Instance), Device, handler);
                Assert.True(await session.TryRestoreAsync(profile, account, default));
                Assert.Equal("/media/System/Info/Public", handler.Sent[0].Path);
                Assert.Equal("/media/Users/Me", handler.Sent[1].Path);
                Assert.Equal("/media/UserViews", handler.Sent[2].Path);
                Assert.Equal("12.2.0", profile.ServerVersion);
                Assert.Equal(true, session.Connection!.IsAdministrator);
                Assert.Equal(MediaServerKind.Jellyfin, new ServerCapabilities(session).ServerKind);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        Case("Jellyfin：封面上传使用 Base64 和正确图片路径", async () =>
        {
            var handler = new Transport();
            using var http = new EmbyHttp(handler);
            await Client(http).UploadImageAsync("item", "Primary", [0, 1, 2, 255], "image/png", default);
            Assert.Equal("AAEC/w==", handler.Sent.Single().Body);
            Assert.Equal("/media/Items/item/Images/Primary", handler.Sent.Single().Path);
        });
        Test("Jellyfin：不兼容排序有显隐判据且保留身份边界", () =>
        {
            Assert.True(JellyfinQuery.SupportsSort(EmbySortBy.DateAdded));
            Assert.False(JellyfinQuery.SupportsSort("TotalBitrate,SortName"));
            var original = Connection();
            Assert.False(original.IsSameIdentityAs(original with { ServerKind = MediaServerKind.Emby }));
            Assert.False(original.IsSameIdentityAs(original with { ServerId = "other-server" }));
            Assert.True(original.IsSameIdentityAs(original with { AccessToken = "replacement" }));
        });
    }

    private static EmbyConnection Connection(string version = "12.2.0") =>
        new(ApiBase, "fixture-token", "u1", "user", "Fixture", Device)
        { ServerKind = MediaServerKind.Jellyfin, ServerVersion = version, ServerId = "server-id" };

    private static EmbyClient Client(EmbyHttp http, string version = "12.2.0") => new(http, Connection(version));
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private static void Case(string name, Func<Task> action) => Test(name, () => action().GetAwaiter().GetResult());

    private sealed record SentRequest(string Url, string Path, string Authorization, string LegacyToken, string Body);
    private sealed class Transport : HttpMessageHandler
    {
        public List<SentRequest> Sent { get; } = [];
        public Func<SentRequest, HttpResponseMessage> Reply { get; set; } = _ => Json("{\"Items\":[]}");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = new SentRequest(request.RequestUri!.AbsoluteUri, request.RequestUri.AbsolutePath,
                request.Headers.TryGetValues("Authorization", out var authorization) ? string.Join("", authorization) : "",
                request.Headers.TryGetValues("X-Emby-Token", out var legacy) ? string.Join("", legacy) : "",
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            Sent.Add(sent);
            return Reply(sent);
        }
    }
}

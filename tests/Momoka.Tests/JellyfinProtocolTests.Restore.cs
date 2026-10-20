using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class JellyfinProtocolTests
{
    private static void RegisterRestoreCorrections()
    {
        foreach (var prefix in new[] { "/media/", "/media/emby/" })
            foreach (var version in new[] { "10.10.7", "12.2.0" })
                Case($"Jellyfin 恢复纠正：旧 Emby 配置 {prefix} / {version} 按确认入口验证令牌和权限", async () =>
                {
                    using var fixture = new RestoreFixture(prefix, version);
                    Assert.True(await fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default));
                    var connection = fixture.Session.Connection!;
                    Assert.Equal(MediaServerKind.Jellyfin, connection.ServerKind);
                    Assert.Equal("https://jellyfin.invalid" + prefix, connection.ApiBase.AbsoluteUri);
                    Assert.Equal(true, connection.IsAdministrator);
                    Assert.Equal("u1", connection.UserId);
                    Assert.Equal("server-id", connection.ServerId);
                    Assert.Equal(1, fixture.Session.TakeRestoredViews()!.Count);
                    var authenticated = fixture.Transport.Sent.Where(sent => sent.Authorization.Contains("Token=", StringComparison.Ordinal)
                        || sent.LegacyToken.Length > 0).ToArray();
                    Assert.True(authenticated.Length >= 2);
                    Assert.True(authenticated.All(sent => sent.Path.StartsWith(prefix, StringComparison.Ordinal)
                        && sent.LegacyToken.Length == 0 && sent.Authorization.Contains("Token=\"fixture-token\"", StringComparison.Ordinal)));
                    Assert.False(fixture.Transport.Sent.Any(sent => sent.Path.EndsWith("AuthenticateByName", StringComparison.Ordinal)));
                    Assert.Equal(prefix.TrimEnd('/'), new Uri(fixture.Server.Url).AbsolutePath);
                });

        foreach (var fault in new[] { "server-id", "user-id", "forbidden", "unavailable" })
            Case($"Jellyfin 恢复纠正：{fault} 不清令牌、不回退密码且不采用错误身份", async () =>
            {
                using var fixture = new RestoreFixture("/media/", "12.2.0");
                if (fault == "server-id") fixture.Server.ServerId = "different-server";
                fixture.Transport.Reply = sent => sent.Path.EndsWith("Users/Me", StringComparison.Ordinal)
                    ? fault switch
                    {
                        "user-id" => Json("{\"Id\":\"other\",\"Name\":\"other\"}"),
                        "forbidden" => new HttpResponseMessage(HttpStatusCode.Forbidden),
                        "unavailable" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                        _ => fixture.Reply(sent)
                    } : fixture.Reply(sent);
                Assert.False(await fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default));
                Assert.False(fixture.Session.IsSignedIn);
                Assert.Equal("fixture-token", fixture.Account.ProtectedAccessToken);
                Assert.Equal("fixture-password", fixture.Account.ProtectedPassword);
                Assert.False(fixture.Transport.Sent.Any(sent => sent.Path.EndsWith("AuthenticateByName", StringComparison.Ordinal)));
                if (fault == "server-id") Assert.True(fixture.Transport.Sent.All(sent => sent.LegacyToken.Length == 0
                    && !sent.Authorization.Contains("Token=", StringComparison.Ordinal)));
            });

        Case("Jellyfin 恢复纠正：只有确认入口的 401 回退保存密码，登录仍使用确认入口", async () =>
        {
            using var fixture = new RestoreFixture("/media/", "12.2.0");
            fixture.Transport.Reply = sent => sent.Path.EndsWith("Users/Me", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : sent.Path.EndsWith("AuthenticateByName", StringComparison.Ordinal)
                    ? Json("{\"AccessToken\":\"fresh-token\",\"ServerId\":\"server-id\",\"User\":{\"Id\":\"u1\",\"Name\":\"user\",\"Policy\":{\"IsAdministrator\":true}}}")
                    : fixture.Reply(sent);
            Assert.True(await fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default));
            var login = fixture.Transport.Sent.Single(sent => sent.Path.EndsWith("AuthenticateByName", StringComparison.Ordinal));
            Assert.Equal("/media/Users/AuthenticateByName", login.Path);
            Assert.Contains("\"Username\":\"user\"", login.Body);
            Assert.Equal("fresh-token", fixture.Account.ProtectedAccessToken);
            Assert.Equal(true, fixture.Session.Connection!.IsAdministrator);
        });

        Test("Jellyfin 入口纠正：未确认不剥真实 /emby/，确认入口不能跨来源", () =>
        {
            var proxy = new Uri("https://jellyfin.invalid/media/emby/");
            Assert.Equal(proxy, EmbyServerAddress.Rebase(proxy, MediaServerKind.Emby, MediaServerKind.Jellyfin));
            Assert.Throws<ArgumentException>(() => EmbyServerAddress.Rebase(proxy, MediaServerKind.Emby, MediaServerKind.Jellyfin,
                new Uri("https://other.invalid/media/")));
        });
    }

    private sealed class RestoreFixture : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "jellyfin-restore", Guid.NewGuid().ToString("N"));
        private readonly string _prefix;
        private readonly string _public;
        public Transport Transport { get; } = new();
        public EmbySession Session { get; }
        public ServerProfile Server { get; }
        public AccountProfile Account { get; } = new()
        {
            UserId = "u1", Username = "user", ProtectedAccessToken = "fixture-token", ProtectedPassword = "fixture-password"
        };

        public RestoreFixture(string prefix, string version)
        {
            _prefix = prefix;
            _public = "{\"ProductName\":\"Jellyfin\",\"ServerName\":\"Fixture\",\"Version\":\"" + version + "\",\"Id\":\"server-id\"}";
            Server = new ServerProfile { Url = "https://jellyfin.invalid" + prefix, ServerKind = MediaServerKind.Emby, ServerId = "server-id" };
            Server.Accounts.Add(Account);
            var settings = new AppSettings { AggregateMode = true };
            settings.Servers.Clear(); settings.Servers.Add(Server);
            Transport.Reply = Reply;
            Directory.CreateDirectory(_root);
            Session = new EmbySession(settings, new SettingsStore(new AppPaths(_root), PassthroughSecretProtector.Instance),
                new CredentialVault(PassthroughSecretProtector.Instance), Device, Transport);
        }

        public HttpResponseMessage Reply(SentRequest sent)
        {
            var path = sent.Path;
            if (path == _prefix + "System/Info/Public") return Json(_public);
            if (path == _prefix + "Users/Me") return Json("{\"Id\":\"u1\",\"Name\":\"user\",\"Policy\":{\"IsAdministrator\":true}}");
            if (path == _prefix + "UserViews" || path == _prefix + "Users/u1/Views")
                return Json("{\"Items\":[{\"Id\":\"library\",\"Name\":\"Library\"}]}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public void Dispose() { Session.Dispose(); Directory.Delete(_root, true); }
    }
}

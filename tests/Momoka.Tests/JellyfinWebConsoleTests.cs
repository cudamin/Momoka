using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class JellyfinWebConsoleTests
{
    private static EmbyConnection Connection => new(new Uri("https://fixture.invalid/emby/"),
        "fixture-token", "fixture-user", "测试用户", "测试 Jellyfin", new DeviceIdentity("Momoka", "Fixture", "device", "1"))
    {
        ServerKind = MediaServerKind.Jellyfin,
        ServerVersion = "12.2.0",
        ServerId = "fixture-server"
    };

    public static void Register()
    {
        Test("Jellyfin 网页：保留代理子路径并使用自身 dashboard 路由", () =>
        {
            Assert.Equal("https://fixture.invalid/emby/web/index.html#/dashboard", EmbyWebConsole.Url(Connection));
            Assert.Equal("https://fixture.invalid/emby/web/index.html#/configurationpage?name=plugin",
                EmbyWebConsole.PageUrl(Connection, "#!/configurationpage?name=plugin"));
            Assert.Throws<ArgumentException>(() => EmbyWebConsole.PageUrl(Connection, "https://other.invalid/web/index.html"));
            Assert.Throws<ArgumentException>(() => EmbyWebConsole.PageUrl(Connection, "//other.invalid"));
            Assert.Throws<ArgumentException>(() => EmbyWebConsole.PageUrl(Connection, "../index.html"));
            Assert.DoesNotContain(Connection.AccessToken, EmbyWebConsole.Url(Connection));
        });

        Test("Jellyfin 网页：凭据格式、来源和目录边界", () =>
        {
            var script = EmbyWebConsole.SignInScript(Connection);
            Assert.Contains("jellyfin_credentials", script);
            Assert.Contains("\"Id\":\"fixture-server\"", script);
            Assert.Contains("\"manualAddressOnly\":true", script);
            Assert.Contains("location.origin", script);
            Assert.Contains("location.pathname", script);
            Assert.Contains("/emby/web/", script);
            Assert.DoesNotContain("server.Users", script);
            Assert.DoesNotContain("servercredentials3", script);
            Assert.Throws<ArgumentException>(() => EmbyWebConsole.SignInScript(Connection with { ServerId = null }));
            Assert.DoesNotContain("</script>", EmbyWebConsole.SignInScript(Connection with { AccessToken = "\"</script>" }));
        });

        Test("Jellyfin 网页：切用户与切服隔离，刷新令牌保留浏览器目录", () =>
        {
            var path = EmbyWebConsole.ProfileDirectory(Connection, "profiles");
            Assert.Equal(path, EmbyWebConsole.ProfileDirectory(Connection with { AccessToken = "fresh-token" }, "profiles"));
            Assert.False(path == EmbyWebConsole.ProfileDirectory(Connection with { UserId = "other-user" }, "profiles"));
            Assert.False(path == EmbyWebConsole.ProfileDirectory(Connection with { ApiBase = new Uri("https://fixture.invalid/other/") }, "profiles"));
            Assert.DoesNotContain(Connection.AccessToken, path);
            Assert.DoesNotContain(Connection.UserId, path);
            Assert.Equal("profiles", EmbyWebConsole.ProfileDirectory(Connection with { ServerKind = MediaServerKind.Emby }, "profiles"));
        });
    }
}

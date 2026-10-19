using Momoka.Emby;
using Momoka.Theming;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 服务器控制台左栏那张目录：三个分区（Emby Server／设备／高级）、固定条目次序、服务器插件页插在哪一段，
/// 以及地址从网页端路由换成内嵌地址再换回来。全部离线，假传输层只回一条配置页列表。
/// </summary>
internal static class ServerSettingsTests
{
    private static readonly Uri ApiBase = new("http://192.0.2.10:8896/emby/");

    private static EmbyClient Client(StubTransport stub, MediaServerKind kind = MediaServerKind.Emby) =>
        new(new EmbyHttp(stub),
            new EmbyConnection(ApiBase, "token-1", "u1", "docuser", "果服",
                DeviceIdentity.Create("device-1", "3.0.0"))
            {
                ServerKind = kind
            });

    private static EmbyConnection JellyfinConnection => new(new Uri("https://fixture.invalid/emby/"),
        "fixture-token", "fixture-user", "测试用户", "测试 Jellyfin",
        DeviceIdentity.Create("device-1", "1.0.0"))
    {
        ServerKind = MediaServerKind.Jellyfin,
        ServerVersion = "12.2.0",
        ServerId = "fixture-server"
    };

    public static void Register()
    {
        RegisterFixedMenu();
        RegisterPluginPlacement();
        RegisterHiddenPlugins();
        RegisterPluginRoutes();
        RegisterBadPluginPages();
        RegisterAddresses();
        RegisterMatch();
        RegisterRequest();
        RegisterHideDrawerScript();
    }

    private static string Labels(IEnumerable<ServerSettingsMenuEntry> entries) =>
        string.Join(",", entries.Select(entry => entry.Label));

    private static ServerSettingsPluginPage Plugin(
        string name, string? displayName = null, string? href = null, string? section = null,
        string? icon = null, bool enabled = true) => new()
        {
            Name = name,
            DisplayName = displayName,
            Href = href,
            MenuSection = section,
            MenuIcon = icon,
            EnableInMainMenu = enabled
        };

    private static void RegisterFixedMenu()
    {
        Test("服务器控制台菜单：三个分区与固定条目照网页端", () =>
        {
            var groups = ServerSettingsMenu.Build();

            Assert.Equal(3, groups.Count);
            Assert.Equal("Emby Server,设备,高级", string.Join(",", groups.Select(group => group.Label)));
            Assert.Equal(
                "控制台,通用,用户,Emby Premiere,媒体库,电视直播,网络,转码,数据库,转换,计划任务,日志",
                Labels(groups[0].Entries),
                "「Emby Server」这一段就是截图里的十二项");
            Assert.Equal("设备,下载,相机上传", Labels(groups[1].Entries));
            Assert.Equal("插件,API 密钥,元数据管理器", Labels(groups[2].Entries));
            Assert.Equal("dashboard", groups[0].Entries[0].Route);
            Assert.Equal("dashboard/settings", groups[0].Entries[1].Route);
            Assert.Equal("conversions?mode=convert", groups[0].Entries[9].Route);
            Assert.False(groups.SelectMany(group => group.Entries).Any(entry => entry.FromPlugin));
        });
    }

    private static void RegisterPluginPlacement()
    {
        Test("服务器控制台菜单：插件页按网页端的三个位置插入", () =>
        {
            var groups = ServerSettingsMenu.Build(
            [
                Plugin("cinemaintros", "Cinema Intros", "configurationpage?name=cinemaintros", "server", "movie"),
                Plugin("customcss", "CustomCssJS Provider", "/configurationpage?name=customcss", "server", "code"),
                Plugin("dlna", "DLNA", "/configurationpage?name=dlna", "devices", "devices"),
                Plugin("advanced", "Advanced Thing", "configurationpage?name=advanced", "advanced"),
                Plugin("backup", "Backup & Restore", "configurationpage?name=backup"),
                Plugin("opensubs", "Open Subtitles", "configurationpage?name=opensubs", null)
            ]);

            Assert.Equal(
                "控制台,通用,用户,Emby Premiere,媒体库,电视直播,网络,转码,数据库,转换,计划任务,日志,Cinema Intros",
                Labels(groups[0].Entries),
                "服务器插件页接在日志后面；CustomCssJS 不进来");
            Assert.Equal("设备,下载,相机上传,DLNA", Labels(groups[1].Entries), "DLNA 在设备组末尾");
            Assert.Equal(
                "Advanced Thing,插件,API 密钥,元数据管理器,Backup & Restore,Open Subtitles",
                Labels(groups[2].Entries),
                "标了 advanced 的在前；没标分区的两条接在元数据管理器后面");
            Assert.Equal("movie", groups[0].Entries[^1].Icon);
            Assert.False(groups.SelectMany(group => group.Entries)
                    .Any(entry => entry.Label.Contains("CustomCssJS", StringComparison.OrdinalIgnoreCase)),
                "CustomCssJS 插件页不该进左栏");
            Assert.True(groups[0].Entries[^1].FromPlugin && groups[2].Entries[^1].FromPlugin, "插件页要能认出来");
        });
    }

    private static void RegisterHiddenPlugins()
    {
        Test("服务器控制台菜单：CustomCssJS 插件页无论名字怎么写都不进左栏", () =>
        {
            var groups = ServerSettingsMenu.Build(
            [
                Plugin("customcssjs", "CustomCssJS Provider", "configurationpage?name=customcssjs", "server"),
                Plugin("custom-css-js", null, "configurationpage?name=custom-css-js", "advanced"),
                Plugin("CustomCssJs", "自定义样式", null, "server"),
                Plugin("cinemaintros", "Cinema Intros", "configurationpage?name=cinemamode", "server")
            ]);

            Assert.False(groups.SelectMany(group => group.Entries)
                    .Any(entry => entry.Label.Contains("CustomCssJS", StringComparison.OrdinalIgnoreCase)
                        || entry.Route.Contains("customcss", StringComparison.OrdinalIgnoreCase)),
                "CustomCssJS 插件页无论名字怎么写都不该进左栏");
            Assert.True(groups[0].Entries.Any(entry => entry.Label == "Cinema Intros"),
                "别的插件页应保留");
        });
    }

    private static void RegisterPluginRoutes()
    {
        Test("服务器控制台菜单：插件页缺 href 时回落到 configuration page 地址", () =>
        {
            var groups = ServerSettingsMenu.Build(
            [
                Plugin("Nfo Metadata", "NFO 元数据", null, "advanced"),
                Plugin("Trim", "Trim", "/configurationpage?name=trim", "advanced")
            ]);

            var entries = groups[2].Entries;
            var fallback = entries.Single(entry => entry.Label == "NFO 元数据");
            Assert.Equal("configurationpage?name=Nfo%20Metadata", fallback.Route, "名字要转义成地址");
            Assert.Equal("configurationpage?name=trim", entries.Single(entry => entry.Label == "Trim").Route,
                "服务器给的相对地址去掉开头的斜杠");
        });
    }

    private static void RegisterBadPluginPages()
    {
        Test("服务器控制台菜单：不安全地址、未知分区与重复项都不进左栏", () =>
        {
            var groups = ServerSettingsMenu.Build(
            [
                Plugin("absolute", "Absolute", "https://evil.invalid/page"),
                Plugin("protocol", "Protocol Relative", "//evil.invalid/page"),
                Plugin("slash", "Backslash", "configurationpage?name=a\\b"),
                Plugin("script", "Script", "javascript:alert(1)"),
                Plugin("other", "Other Section", "configurationpage?name=other", "other"),
                Plugin("disabled", "Disabled", "configurationpage?name=disabled", "server", enabled: false),
                Plugin("twice", "First", "configurationpage?name=twice", "server"),
                Plugin("twice-again", "Second", "configurationpage?name=twice", "server")
            ]);

            Assert.Equal(
                "控制台,通用,用户,Emby Premiere,媒体库,电视直播,网络,转码,数据库,转换,计划任务,日志,First",
                Labels(groups[0].Entries),
                "不安全的地址和被关掉的条目都不出现，同一地址只留第一条");
            Assert.Equal("设备,下载,相机上传", Labels(groups[1].Entries));
            Assert.Equal("插件,API 密钥,元数据管理器", Labels(groups[2].Entries));
        });
    }

    private static void RegisterAddresses()
    {
        Test("服务器控制台地址：Emby 用 #!，Jellyfin 用 #/，非法路由拒绝", () =>
        {
            Assert.Equal("http://192.0.2.10:8896/web/index.html#!/dashboard",
                EmbyWebConsole.RouteUrl(ApiBase, "dashboard"));
            Assert.Equal("http://192.0.2.10:8896/web/index.html#!/dashboard/settings",
                EmbyWebConsole.RouteUrl(ApiBase, "/dashboard/settings"));
            Assert.Equal("http://192.0.2.10:8896/web/index.html#!/configurationpage?name=A%20B",
                EmbyWebConsole.RouteUrl(ApiBase, "configurationpage?name=A%20B"));
            Assert.Equal("https://fixture.invalid/emby/web/index.html#/dashboard/settings",
                EmbyWebConsole.RouteUrl(JellyfinConnection, "dashboard/settings"));
            Assert.DoesNotContain("fixture-token", EmbyWebConsole.RouteUrl(JellyfinConnection, "dashboard/settings"),
                "凭据不进地址");

            foreach (var route in new[] { "https://evil.invalid/x", "//evil.invalid/x", "a\\b", "javascript:alert(1)", "" })
                Assert.Throws<ArgumentException>(() => EmbyWebConsole.RouteUrl(ApiBase, route), $"“{route}”不该能拼成控制台地址");
        });

        Test("服务器控制台地址：从网页地址认回井号后面那段路由", () =>
        {
            Assert.Equal("devices/device.html?id=1",
                ServerSettingsMenu.RouteOf("http://host/web/index.html#!/devices/device.html?id=1"));
            Assert.Equal("users", ServerSettingsMenu.RouteOf("http://host/web/index.html#/users"));
            Assert.Equal("dashboard", ServerSettingsMenu.RouteOf("http://host/web/index.html#!/dashboard"));
            Assert.Null(ServerSettingsMenu.RouteOf("http://host/web/index.html"));
            Assert.Null(ServerSettingsMenu.RouteOf("http://host/web/index.html#"));
            Assert.Null(ServerSettingsMenu.RouteOf(null));
        });
    }

    private static void RegisterMatch()
    {
        Test("服务器控制台菜单：网页内部跳转仍亮着对应的左栏项", () =>
        {
            var groups = ServerSettingsMenu.Build(
            [
                Plugin("cinemaintros", "Cinema Intros", "configurationpage?name=cinemaintros", "server")
            ]);

            Assert.Equal("控制台", ServerSettingsMenu.Match(groups, "dashboard")?.Label);
            Assert.Equal("通用", ServerSettingsMenu.Match(groups, "#!/dashboard/settings")?.Label);
            Assert.Equal("通用", ServerSettingsMenu.Match(groups, "dashboard/settings/extra")?.Label,
                "更长的地址优先，别落到「控制台」上");
            Assert.Equal("设备", ServerSettingsMenu.Match(groups, "devices/device.html?id=1")?.Label);
            Assert.Equal("设备", ServerSettingsMenu.Match(groups, "devices?x=1")?.Label);
            Assert.Equal("Cinema Intros",
                ServerSettingsMenu.Match(groups, "configurationpage?name=cinemaintros")?.Label);
            Assert.Null(ServerSettingsMenu.Match(groups, "unknown"));
            Assert.Null(ServerSettingsMenu.Match(groups, ""));
        });
    }

    private static void RegisterRequest()
    {
        Test("服务器控制台插件页：请求形状与解析照网页端", () =>
        {
            const string sample = """
                [
                  {
                    "Name": "cinemaintros", "DisplayName": "Cinema Intros",
                    "Href": "configurationpage?name=cinemaintros", "MenuSection": "server",
                    "MenuIcon": "movie", "PluginId": "p1", "EnableInMainMenu": true
                  },
                  {
                    "Name": "customcss", "DisplayName": "CustomCssJS Provider",
                    "MenuSection": "server", "PluginId": "p2", "EnableInMainMenu": true
                  },
                  {
                    "Name": "dlna", "DisplayName": "DLNA",
                    "Href": "/configurationpage?name=dlna", "MenuSection": "devices",
                    "PluginId": "p3", "EnableInMainMenu": true
                  }
                ]
                """;

            var stub = new StubTransport().Answer("web/ConfigurationPages", sample);
            var pages = Client(stub).GetServerSettingsPagesAsync(CancellationToken.None).GetAwaiter().GetResult();

            var sent = stub.Only("web/ConfigurationPages");
            Assert.Equal("GET", sent.Method);
            Assert.Contains("web/ConfigurationPages?PageType=PluginConfiguration&UserId=u1&EnableInMainMenu=true",
                sent.Url, "参数就是官方网页端 getConfigurationPages 那一组");
            Assert.Equal(3, pages.Count);
            Assert.Equal("Cinema Intros", pages[0].DisplayName);
            Assert.Equal("server", pages[0].MenuSection);
            Assert.Null(pages[1].Href);
            Assert.True(pages[2].EnableInMainMenu, "布尔字段要真的读进来");
        });

        Test("服务器控制台插件页：Jellyfin 没有这套菜单，不发请求", () =>
        {
            var stub = new StubTransport();
            var client = Client(stub, MediaServerKind.Jellyfin);
            Assert.Throws<NotSupportedException>(() =>
                client.GetServerSettingsPagesAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.Equal(0, stub.Total, "不该碰网络");
        });
    }

    private static void RegisterHideDrawerScript()
    {
        Test("服务器控制台：收起网页端自己的抽屉，不碰别的东西", () =>
        {
            var script = EmbyWebConsole.HideDrawerScript();
            Assert.Contains(".mainDrawer", script);
            Assert.Contains(".drawer-backdrop", script);
            Assert.Contains(".skinHeader", script);
            Assert.Contains(".appfooter", script);
            Assert.Contains("btnOpenNavDrawer", script);
            Assert.Contains("NavDrawer", script);
            Assert.Contains(".padded-top-page", script);
            Assert.DoesNotContain("token", script, "脚本里不该有凭据");
            Assert.DoesNotContain("http", script, "脚本只认网页端自己的类名，不需要服务器地址");
        });

        Test("服务器控制台：Momoka 主题换算成网页的颜色变量", () =>
        {
            var mint = ThemeColor.Parse("#6EE7C5");
            var hsl = mint.ToHsl();
            Assert.True(hsl.Hue is > 160 and < 175, $"薄荷绿的色相不对：{hsl.Hue:0.##}");
            Assert.True(hsl.Saturation is > 65 and < 75, $"薄荷绿的饱和度不对：{hsl.Saturation:0.##}");
            Assert.True(hsl.Lightness is > 62 and < 72, $"薄荷绿的亮度不对：{hsl.Lightness:0.##}");
            Assert.Equal((0.0, 100.0, 50.0), ThemeColor.Parse("#FF0000").ToHsl());
            Assert.Equal((0.0, 0.0, 100.0), ThemeColor.Parse("#FFFFFF").ToHsl());

            var script = EmbyWebConsole.ThemeVariablesScript(
                ThemeColor.Parse("#141B22"), ThemeColor.Parse("#F2F6FA"), mint, ThemeColor.Parse("#0C1116"));
            Assert.Contains("--background-hue", script);
            Assert.Contains("--theme-text-color-hue", script);
            Assert.Contains("--theme-primary-color-hue", script);
            Assert.Contains("#6EE7C5", script, "强调色要真的进样式");
            Assert.Contains(".button-submit", script, "强调色按钮的文字要跟着主题的 on-accent");
            Assert.Contains("#0C1116", script);
            Assert.Contains("!important", script);
            // 文档开始脚本跑在 <html> 建出来之前；第一次 apply 必须能空转，否则后面的重试全被异常吃掉。
            Assert.Contains("if (!root) return", script);
            Assert.Contains("DOMContentLoaded", script);
            Assert.Contains("window.addEventListener(\"load\"", script);
            Assert.DoesNotContain("token", script, "脚本里不该有凭据");

            var skin = EmbyWebConsole.NativeSkinScript(UiThemes.Default.Colors);
            Assert.Contains("Segoe UI Variable Text", skin, "网页要换成 WinUI 的字体");
            Assert.Contains("--momoka-accent", skin);
            Assert.Contains("#6EE7C5", skin, "皮肤要带当前主题的强调色");
            Assert.Contains("fieldset", skin, "设置页的分组要变成卡片");
            Assert.Contains(".emby-input", skin);
            Assert.Contains(".toggleSwitch", skin);
            Assert.Contains(".button-submit", skin);
            Assert.Contains("gap: 12px", skin, "卡片之间要紧凑");
            Assert.Contains("border: 0 !important", skin, "卡片不要白边");
            Assert.Contains("::-webkit-scrollbar", skin);
            Assert.Contains("if (!root) return", skin);
            Assert.Contains("DOMContentLoaded", skin);
            Assert.DoesNotContain("token", skin, "脚本里不该有凭据");

            var route = EmbyWebConsole.RouteScript(
                "configurationpage?name=cinemamode",
                "http://server/web/index.html#!/configurationpage?name=cinemamode");
            Assert.Contains("Emby.Page.show", route, "切页要走网页自己的路由，而不是整页重载");
            Assert.Contains("configurationpage?name=cinemamode", route);
            Assert.Contains("location.href", route, "路由失败要能落回整页地址");
            Assert.Contains("return false", route);
            Assert.DoesNotContain("token", route, "脚本里不该有凭据");
        });
    }
}

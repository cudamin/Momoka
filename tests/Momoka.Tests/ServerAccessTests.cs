using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 由在线来源算出的界面风味、设置迁移，以及 Jellyfin 控制台目录。会话恢复全部账号那一条在
/// <c>IdentityRegressionTests</c> 里，用假 HTTP 走真的恢复路径。
/// </summary>
internal static class ServerAccessTests
{
    private static readonly ISecretProtector Protector = PassthroughSecretProtector.Instance;

    public static void Register()
    {
        RegisterFlavors();
        RegisterKindLabels();
        RegisterJellyfinMenu();
        RegisterJellyfinConsole();
        RegisterMigration();
    }

    private static void RegisterFlavors()
    {
        Test("界面风味：只有一台服务器时用那台的界面，多台或没有时是通用界面", () =>
        {
            Assert.Equal(ServerInterfaceFlavor.Generic, ServerAccess.InterfaceFor(0, []));
            Assert.Equal(ServerInterfaceFlavor.Emby,
                ServerAccess.InterfaceFor(1, [MediaServerKind.Emby]));
            Assert.Equal(ServerInterfaceFlavor.Emby,
                ServerAccess.InterfaceFor(1, [MediaServerKind.Emby, MediaServerKind.Emby]),
                "同一台服务器的多个账号仍然是这一台");
            Assert.Equal(ServerInterfaceFlavor.Jellyfin,
                ServerAccess.InterfaceFor(1, [MediaServerKind.Jellyfin]));
            Assert.Equal(ServerInterfaceFlavor.Generic,
                ServerAccess.InterfaceFor(2, [MediaServerKind.Emby, MediaServerKind.Jellyfin]),
                "同时连着 Emby 与 Jellyfin 没有唯一的一套界面");
            Assert.Equal(ServerInterfaceFlavor.Generic,
                ServerAccess.InterfaceFor(2, [MediaServerKind.Emby, MediaServerKind.Emby]),
                "两台同类服务器也是多服聚合，没有唯一的服务端入口");
            Assert.Equal(ServerInterfaceFlavor.FnNas,
                ServerAccess.InterfaceFor(1, [MediaServerKind.FnNas]),
                "飞牛影视作为唯一在线服务器时用它自己的设置目录");
            Assert.Equal(ServerInterfaceFlavor.Generic,
                ServerAccess.InterfaceFor(1, [MediaServerKind.Plex]),
                "Plex 先用通用界面");
            Assert.Equal(ServerInterfaceFlavor.Jellyfin,
                ServerAccess.InterfaceFor(MediaServerKind.Jellyfin));
            Assert.Equal(ServerInterfaceFlavor.FnNas,
                ServerAccess.InterfaceFor(MediaServerKind.FnNas));
        });

        Test("界面风味：Emby、Jellyfin 与飞牛影视各有服务端管理入口", () =>
        {
            Assert.True(ServerAccess.ShowsServerAdministration(ServerInterfaceFlavor.Emby));
            Assert.True(ServerAccess.ShowsServerAdministration(ServerInterfaceFlavor.Jellyfin));
            Assert.True(ServerAccess.ShowsServerAdministration(ServerInterfaceFlavor.FnNas));
            Assert.False(ServerAccess.ShowsServerAdministration(ServerInterfaceFlavor.Generic),
                "通用界面不知道管理入口该指向哪台服务器");
            Assert.Equal("Emby 界面", ServerAccess.InterfaceName(ServerInterfaceFlavor.Emby));
            Assert.Equal("Jellyfin 界面", ServerAccess.InterfaceName(ServerInterfaceFlavor.Jellyfin));
            Assert.Equal("飞牛影视界面", ServerAccess.InterfaceName(ServerInterfaceFlavor.FnNas));
            Assert.Equal("通用界面", ServerAccess.InterfaceName(ServerInterfaceFlavor.Generic));
        });
    }

    private static void RegisterKindLabels()
    {
        Test("服务器卡片的产品标签", () =>
        {
            Assert.Equal("Emby", ServerAccess.KindName(MediaServerKind.Emby));
            Assert.Equal("Jellyfin", ServerAccess.KindName(MediaServerKind.Jellyfin));
            Assert.Equal("飞牛影视", ServerAccess.KindName(MediaServerKind.FnNas));
            Assert.Equal("Plex", ServerAccess.KindName(MediaServerKind.Plex));
        });
    }

    private static string Labels(IEnumerable<ServerSettingsMenuEntry> entries) =>
        string.Join(",", entries.Select(entry => entry.Label));

    private static void RegisterJellyfinMenu()
    {
        Test("Jellyfin 控制台菜单：照 10.10 网页端管理抽屉分五区", () =>
        {
            var groups = JellyfinServerSettingsMenu.Build();

            Assert.Equal("服务器,设备,电视直播,插件,高级",
                string.Join(",", groups.Select(group => group.Label)));
            Assert.Equal(
                "控制台,常规,用户,媒体库,显示,元数据,NFO 设置,转码,继续观看,流媒体,特技播放",
                Labels(groups[0].Entries));
            Assert.Equal("设备,活动", Labels(groups[1].Entries));
            Assert.Equal("电视直播,数字录像机", Labels(groups[2].Entries));
            Assert.Equal("我的插件,目录", Labels(groups[3].Entries));
            Assert.Equal("网络,API 密钥,日志,计划任务,媒体资料管理器", Labels(groups[4].Entries));
            Assert.Equal("dashboard", groups[0].Entries[0].Route);
            Assert.Equal("dashboard/settings", groups[0].Entries[1].Route);
            Assert.Equal("dashboard/recordings", groups[2].Entries[^1].Route);
            Assert.Equal("metadata", groups[4].Entries[^1].Route);
            Assert.True(groups.SelectMany(group => group.Entries)
                .All(entry => ServerSettingsMenu.IsSafeRoute(entry.Route)), "每条都必须是留在服务器网页里的相对路由");
        });

        Test("Jellyfin 控制台菜单：插件页接在插件分区末尾，CustomCssJS 不进来", () =>
        {
            var groups = JellyfinServerSettingsMenu.Build(
            [
                new ServerSettingsPluginPage
                {
                    Name = "customcssjs", DisplayName = "CustomCssJS Provider",
                    Href = "configurationpage?name=customcssjs", EnableInMainMenu = true
                },
                new ServerSettingsPluginPage
                {
                    Name = "thing", DisplayName = "Jellyfin 插件页",
                    Href = "/configurationpage?name=thing", MenuIcon = "extension",
                    EnableInMainMenu = true
                },
                new ServerSettingsPluginPage
                {
                    Name = "hidden", DisplayName = "不在主菜单", EnableInMainMenu = false
                }
            ]);

            Assert.Equal("我的插件,目录,Jellyfin 插件页", Labels(groups[3].Entries));
            Assert.Equal("configurationpage?name=thing", groups[3].Entries[^1].Route);
            Assert.True(groups[3].Entries[^1].FromPlugin);
            Assert.False(groups.SelectMany(group => group.Entries)
                .Any(entry => entry.Label.Contains("CustomCssJS", StringComparison.OrdinalIgnoreCase)
                    || entry.Label == "不在主菜单"),
                "被隐藏或不在主菜单里的插件页不该出现");
        });

        Test("Jellyfin 控制台菜单：按产品取目录与默认页", () =>
        {
            Assert.Equal(11, JellyfinServerSettingsMenu.For(MediaServerKind.Jellyfin)![0].Entries.Count);
            Assert.Equal(JellyfinServerSettingsMenu.DefaultRoute,
                JellyfinServerSettingsMenu.DefaultRouteFor(MediaServerKind.Jellyfin));
            Assert.Equal(ServerSettingsMenu.DefaultRoute,
                JellyfinServerSettingsMenu.DefaultRouteFor(MediaServerKind.Emby));
            Assert.Null(JellyfinServerSettingsMenu.For(MediaServerKind.FnNas));
            Assert.Null(JellyfinServerSettingsMenu.For(MediaServerKind.Plex));
        });

        Test("Jellyfin 控制台菜单：网页路由能被左栏认出来", () =>
        {
            var groups = JellyfinServerSettingsMenu.Build();
            Assert.Equal("媒体库", ServerSettingsMenu.Match(groups, "#/dashboard/libraries")?.Label);
            Assert.Equal("显示", ServerSettingsMenu.Match(groups, "#/dashboard/libraries/display")?.Label,
                "子页自己也有一条时，点亮的是子页这一条");
            Assert.Equal("控制台", ServerSettingsMenu.Match(groups, "#/dashboard")?.Label);
            Assert.Equal("数字录像机", ServerSettingsMenu.Match(groups, "#/dashboard/recordings")?.Label);
            Assert.Equal("目录", ServerSettingsMenu.Match(groups, "#/dashboard/plugins/catalog/extra")?.Label);
            Assert.Null(ServerSettingsMenu.Match(groups, "#/home"));
        });
    }

    private static void RegisterJellyfinConsole()
    {
        Test("Jellyfin 网页控制台：路由写法与抽屉脚本", () =>
        {
            var connection = new EmbyConnection(new Uri("https://fixture.invalid/jf/"), "fixture-token", "u1", "user",
                "Jellyfin", DeviceIdentity.Create("device-1", "1.0.0"))
            {
                ServerKind = MediaServerKind.Jellyfin,
                ServerId = "server-1",
                ServerVersion = "12.2.0"
            };

            var url = EmbyWebConsole.RouteUrl(connection, JellyfinServerSettingsMenu.DefaultRoute);
            Assert.Equal("https://fixture.invalid/jf/web/index.html#/dashboard", url);
            Assert.DoesNotContain("fixture-token", url);

            var script = EmbyWebConsole.HideJellyfinDrawerScript();
            Assert.Contains("MuiDrawer-root", script);
            Assert.Contains("dashboardDocument", script);
            Assert.Contains("MuiAppBar-root", script);
            Assert.DoesNotContain("AccessToken", script);
            Assert.DoesNotContain("fixture-token", script);
        });
    }

    private static void RegisterMigration()
    {
        Test("设置迁移：旧独占键被忽略，服务器与账号原样保留", () =>
        {
            const string json = """
            {
              "SchemaVersion": 24,
              "DeviceId": "fixture-device",
              "Servers": [
                {
                  "Id": "s-a",
                  "Name": "A",
                  "Url": "http://a.invalid",
                  "Accounts": [
                    { "Id": "acc-a", "Username": "甲", "UserId": "u-a", "AutoConnect": true }
                  ]
                }
              ],
              "ServerAccess": { "Mode": 1 }
            }
            """;

            var settings = SettingsMigration.FromJson(json, Protector);

            Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.Equal(1, settings.Servers.Count);
            Assert.Equal("甲", settings.Servers[0].Accounts[0].Username,
                "旧模式键不能让保存的账号消失");
            Assert.True(settings.Servers[0].Accounts[0].AutoConnect == true,
                "升级后的账号仍应参与自动恢复");

            var saved = JsonSerializer.Serialize(settings, SettingsSerializer.WriteOptions);
            Assert.False(saved.Contains("ServerAccess", StringComparison.Ordinal),
                "保存的新文档不再写已退役的连接模式");
        });
    }
}

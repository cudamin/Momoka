namespace Momoka.Emby;

/// <summary>
/// Jellyfin 网页控制台左栏的目录 —— 照 Jellyfin Web 10.10 的管理抽屉
/// （<c>apps/dashboard/components/drawer</c>，2026-10-19 读取运行中的 10.10.7）抄的五个分区与路由。
/// Emby 那份在 <see cref="ServerSettingsMenu"/>，两者共用 <see cref="ServerSettingsMenuGroup"/> 和路由匹配，
/// 页面那一头只换一份目录。
/// <para>
/// 媒体库与播放各自有子页（显示／元数据／NFO 设置；转码／继续观看／流媒体／特技播放），网页端把它们藏在
/// 两个可展开的分组里。这一栏沿用 Emby 那一份的平铺写法：父项本身在网页端不是一个页面（点它只展开），
/// 所以子页按网页端次序排进「服务器」分区 —— 每一条都真的打得到页面。
/// </para>
/// <para>
/// 插件配置页由服务器贡献，照网页端的做法全部接在「插件」分区末尾；「媒体资料管理器」是 Jellyfin 管理员
/// 菜单里的那一页（路由 <c>metadata</c>），网页抽屉里没有，但用户点名要它，因此排在「高级」末尾。
/// </para>
/// </summary>
public static class JellyfinServerSettingsMenu
{
    public const string DefaultRoute = "dashboard";
    public const string MetadataManagerLabel = "媒体资料管理器";
    public const string MetadataManagerRoute = "metadata";

    public const string ServerGroupLabel = "服务器";
    public const string DevicesGroupLabel = "设备";
    public const string LiveTvGroupLabel = "电视直播";
    public const string PluginsGroupLabel = "插件";
    public const string AdvancedGroupLabel = "高级";

    private static readonly ServerSettingsMenuEntry[] ServerFixed =
    [
        new("控制台", "dashboard", ServerSettingsSection.Server, "dashboard"),
        new("常规", "dashboard/settings", ServerSettingsSection.Server, "settings"),
        new("用户", "dashboard/users", ServerSettingsSection.Server, "users"),
        new("媒体库", "dashboard/libraries", ServerSettingsSection.Server, "library"),
        new("显示", "dashboard/libraries/display", ServerSettingsSection.Server, "display"),
        new("元数据", "dashboard/libraries/metadata", ServerSettingsSection.Server, "metadata"),
        new("NFO 设置", "dashboard/libraries/nfo", ServerSettingsSection.Server, "nfo"),
        new("转码", "dashboard/playback/transcoding", ServerSettingsSection.Server, "transcode"),
        new("继续观看", "dashboard/playback/resume", ServerSettingsSection.Server, "resume"),
        new("流媒体", "dashboard/playback/streaming", ServerSettingsSection.Server, "streaming"),
        new("特技播放", "dashboard/playback/trickplay", ServerSettingsSection.Server, "trickplay")
    ];

    private static readonly ServerSettingsMenuEntry[] DevicesFixed =
    [
        new("设备", "dashboard/devices", ServerSettingsSection.Devices, "devices"),
        new("活动", "dashboard/activity", ServerSettingsSection.Devices, "activity")
    ];

    private static readonly ServerSettingsMenuEntry[] LiveTvFixed =
    [
        new("电视直播", "dashboard/livetv", ServerSettingsSection.LiveTv, "livetv"),
        new("数字录像机", "dashboard/recordings", ServerSettingsSection.LiveTv, "dvr")
    ];

    private static readonly ServerSettingsMenuEntry[] PluginsFixed =
    [
        new("我的插件", "dashboard/plugins", ServerSettingsSection.Plugins, "plugins"),
        new("目录", "dashboard/plugins/catalog", ServerSettingsSection.Plugins, "catalog")
    ];

    private static readonly ServerSettingsMenuEntry[] AdvancedFixed =
    [
        new("网络", "dashboard/networking", ServerSettingsSection.Advanced, "network"),
        new("API 密钥", "dashboard/keys", ServerSettingsSection.Advanced, "apikey"),
        new("日志", "dashboard/logs", ServerSettingsSection.Advanced, "logs"),
        new("计划任务", "dashboard/tasks", ServerSettingsSection.Advanced, "tasks"),
        new(MetadataManagerLabel, MetadataManagerRoute, ServerSettingsSection.Advanced, "edit")
    ];

    /// <summary>
    /// 五个分区，次序照网页端。插件页按网页端的做法接在「插件」分区末尾，缺地址时回落到
    /// <c>configurationpage?name=…</c>；同名的只留第一条。
    /// </summary>
    public static IReadOnlyList<ServerSettingsMenuGroup> Build(IEnumerable<ServerSettingsPluginPage>? pages = null)
    {
        var server = new List<ServerSettingsMenuEntry>(ServerFixed);
        var devices = new List<ServerSettingsMenuEntry>(DevicesFixed);
        var liveTv = new List<ServerSettingsMenuEntry>(LiveTvFixed);
        var plugins = new List<ServerSettingsMenuEntry>(PluginsFixed);
        var advanced = new List<ServerSettingsMenuEntry>(AdvancedFixed);

        var labels = new HashSet<string>(
            server.Concat(devices).Concat(liveTv).Concat(plugins).Concat(advanced).Select(entry => entry.Label),
            StringComparer.Ordinal);
        var routes = new HashSet<string>(
            server.Concat(devices).Concat(liveTv).Concat(plugins).Concat(advanced).Select(entry => entry.Route),
            StringComparer.Ordinal);

        foreach (var page in pages ?? [])
        {
            if (page is null || !page.EnableInMainMenu) continue;
            if (ServerSettingsMenu.IsHiddenPlugin(page)) continue;

            var label = string.IsNullOrWhiteSpace(page.DisplayName) ? page.Name?.Trim() : page.DisplayName.Trim();
            var route = ServerSettingsMenu.PluginRoute(page);
            if (string.IsNullOrEmpty(label) || route is null) continue;
            if (!labels.Add(label) || !routes.Add(route)) continue;

            plugins.Add(new(label, route, ServerSettingsSection.Plugins,
                string.IsNullOrWhiteSpace(page.MenuIcon) ? "plugin" : page.MenuIcon.Trim(), FromPlugin: true));
        }

        return
        [
            new(ServerGroupLabel, ServerSettingsSection.Server, server),
            new(DevicesGroupLabel, ServerSettingsSection.Devices, devices),
            new(LiveTvGroupLabel, ServerSettingsSection.LiveTv, liveTv),
            new(PluginsGroupLabel, ServerSettingsSection.Plugins, plugins),
            new(AdvancedGroupLabel, ServerSettingsSection.Advanced, advanced)
        ];
    }

    /// <summary>按产品取控制台目录；两套之外的产品（飞牛影视／Plex）没有原生目录，交给网页自己。</summary>
    public static IReadOnlyList<ServerSettingsMenuGroup>? For(MediaServerKind kind) => kind switch
    {
        MediaServerKind.Emby => ServerSettingsMenu.Build(),
        MediaServerKind.Jellyfin => Build(),
        _ => null
    };

    /// <summary>按产品取默认页。Jellyfin 和 Emby 都叫 <c>dashboard</c>，但两边的拼法不同，由网页地址那一头处理。</summary>
    public static string DefaultRouteFor(MediaServerKind kind) => kind switch
    {
        MediaServerKind.Emby => ServerSettingsMenu.DefaultRoute,
        MediaServerKind.Jellyfin => DefaultRoute,
        _ => "dashboard"
    };
}

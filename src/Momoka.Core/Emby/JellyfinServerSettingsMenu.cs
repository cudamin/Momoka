namespace Momoka.Emby;

/// <summary>
/// Jellyfin 网页控制台左栏的目录 —— 照 Jellyfin Web 自己的管理抽屉（<c>apps/dashboard/components/drawer</c>，
/// 2026-10-19 读取 master）抄的五个分区与路由。Emby 那份在 <see cref="ServerSettingsMenu"/>，两者共用
/// <see cref="ServerSettingsMenuGroup"/> 和路由匹配，页面那一头只换一份目录。
/// <para>
/// 媒体库与播放各自有子页（显示／元数据／NFO 设置；转码／继续观看／流媒体／特技播放），网页端把它们藏在
/// 两个可展开的分组里。这一栏沿用 Emby 那一份的平铺写法：父项本身在网页端不是一个页面（点它只展开），
/// 所以父项不出现，子页按网页端次序排在分区里 —— 每一条都真的打得到页面。
/// </para>
/// <para>
/// 插件配置页不从服务器读：Jellyfin 的插件配置页地址规则与 Emby 的 <c>Web/ConfigurationPages</c> 不同，
/// 这一轮先只给固定条目；网页自己那块「插件」页仍在。
/// </para>
/// </summary>
public static class JellyfinServerSettingsMenu
{
    public const string DefaultRoute = "dashboard";

    public const string ServerGroupLabel = "服务器";
    public const string DevicesGroupLabel = "设备";
    public const string LiveTvGroupLabel = "电视直播";
    public const string PluginsGroupLabel = "插件";
    public const string AdvancedGroupLabel = "高级";

    private static readonly ServerSettingsMenuGroup[] Groups =
    [
        new(ServerGroupLabel, ServerSettingsSection.Server,
        [
            new("控制台", "dashboard", ServerSettingsSection.Server, "dashboard"),
            new("常规", "dashboard/settings", ServerSettingsSection.Server, "settings"),
            new("品牌", "dashboard/branding", ServerSettingsSection.Server, "palette"),
            new("用户", "dashboard/users", ServerSettingsSection.Server, "users"),
            new("媒体库", "dashboard/libraries", ServerSettingsSection.Server, "library"),
            new("显示", "dashboard/libraries/display", ServerSettingsSection.Server, "display"),
            new("元数据", "dashboard/libraries/metadata", ServerSettingsSection.Server, "metadata"),
            new("NFO 设置", "dashboard/libraries/nfo", ServerSettingsSection.Server, "nfo"),
            new("转码", "dashboard/playback/transcoding", ServerSettingsSection.Server, "transcode"),
            new("继续观看", "dashboard/playback/resume", ServerSettingsSection.Server, "resume"),
            new("流媒体", "dashboard/playback/streaming", ServerSettingsSection.Server, "streaming"),
            new("特技播放", "dashboard/playback/trickplay", ServerSettingsSection.Server, "trickplay")
        ]),
        new(DevicesGroupLabel, ServerSettingsSection.Devices,
        [
            new("设备", "dashboard/devices", ServerSettingsSection.Devices, "devices"),
            new("活动", "dashboard/activity", ServerSettingsSection.Devices, "activity")
        ]),
        new(LiveTvGroupLabel, ServerSettingsSection.LiveTv,
        [
            new("电视直播", "dashboard/livetv", ServerSettingsSection.LiveTv, "livetv"),
            new("DVR", "dashboard/livetv/recordings", ServerSettingsSection.LiveTv, "dvr")
        ]),
        new(PluginsGroupLabel, ServerSettingsSection.Plugins,
        [
            new("插件", "dashboard/plugins", ServerSettingsSection.Plugins, "plugins")
        ]),
        new(AdvancedGroupLabel, ServerSettingsSection.Advanced,
        [
            new("网络", "dashboard/networking", ServerSettingsSection.Advanced, "network"),
            new("API 密钥", "dashboard/keys", ServerSettingsSection.Advanced, "apikey"),
            new("备份", "dashboard/backups", ServerSettingsSection.Advanced, "backup"),
            new("日志", "dashboard/logs", ServerSettingsSection.Advanced, "logs"),
            new("计划任务", "dashboard/tasks", ServerSettingsSection.Advanced, "tasks")
        ])
    ];

    public static IReadOnlyList<ServerSettingsMenuGroup> Build() => Groups;

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

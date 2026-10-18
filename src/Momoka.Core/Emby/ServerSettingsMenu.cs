namespace Momoka.Emby;

/// <summary>
/// 管理员左栏的三个分区。次序与分组照 Emby 网页端 <c>modules/navdrawer/navdrawercontent.js</c> 里
/// <c>getAdminMenuItems</c> 的那一段：服务器固定条目之后接插件页，设备组接设备类插件页，高级组里
/// 先放标了 advanced 的插件页，再放 插件／API 密钥／元数据管理器，最后放没有标分区的插件页。
/// </summary>
public enum ServerSettingsSection
{
    Server,
    Devices,
    Advanced
}

/// <summary>左栏里的一个页面。图标是一个键，由 Shell 换成 Segoe 字形；Core 不认识字体。</summary>
public sealed record ServerSettingsMenuEntry(
    string Label,
    string Route,
    ServerSettingsSection Section,
    string Icon,
    bool FromPlugin = false);

public sealed record ServerSettingsMenuGroup(
    string Label,
    ServerSettingsSection Section,
    IReadOnlyList<ServerSettingsMenuEntry> Entries);

/// <summary>
/// <c>GET web/ConfigurationPages?PageType=PluginConfiguration&amp;UserId=…&amp;EnableInMainMenu=true</c>
/// 的一条。字段名就是服务器 JSON 的名字；只有网页端左栏用得上的那几个。
/// </summary>
public sealed class ServerSettingsPluginPage
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Href { get; set; }
    public string? MenuSection { get; set; }
    public string? MenuIcon { get; set; }
    public string? PluginId { get; set; }
    public bool EnableInMainMenu { get; set; }
}

/// <summary>
/// 服务器控制台左栏的目录。固定条目照网页端抄，插件页按 <see cref="ServerSettingsPluginPage.MenuSection"/>
/// 插进对应分区 —— Cinema Intros 与 CustomCssJS Provider 在「Emby Server」末尾，DLNA 在「设备」末尾，
/// Backup &amp; Restore 与 Open Subtitles 在「高级」末尾，正好是用户截图里的次序。
/// <para>
/// 排序是业务规则，放在 Core 里用测试钉住；页面只负责把结果画出来。插件页的地址来自服务器，可能缺
/// <c>Href</c>，那时按网页端的规矩回落到 <c>configurationpage?name=…</c>；绝对地址、协议相对地址和
/// 带反斜杠的地址一律丢掉，避免内嵌浏览器被带到别的站点。
/// </para>
/// </summary>
public static class ServerSettingsMenu
{
    public const string DefaultRoute = "dashboard";
    public const string ServerGroupLabel = "Emby Server";
    public const string DevicesGroupLabel = "设备";
    public const string AdvancedGroupLabel = "高级";

    private static readonly ServerSettingsMenuEntry[] ServerFixed =
    [
        new("控制台", "dashboard", ServerSettingsSection.Server, "dashboard"),
        new("通用", "dashboard/settings", ServerSettingsSection.Server, "settings"),
        new("用户", "users", ServerSettingsSection.Server, "users"),
        new("Emby Premiere", "embypremiere", ServerSettingsSection.Server, "premiere"),
        new("媒体库", "librarysetup", ServerSettingsSection.Server, "library"),
        new("电视直播", "livetvsetup", ServerSettingsSection.Server, "livetv"),
        new("网络", "network", ServerSettingsSection.Server, "network"),
        new("转码", "transcoding", ServerSettingsSection.Server, "transcode"),
        new("数据库", "database", ServerSettingsSection.Server, "database"),
        new("转换", "conversions?mode=convert", ServerSettingsSection.Server, "conversions"),
        new("计划任务", "scheduledtasks", ServerSettingsSection.Server, "tasks"),
        new("日志", "logs", ServerSettingsSection.Server, "logs")
    ];

    private static readonly ServerSettingsMenuEntry[] DevicesFixed =
    [
        new("设备", "devices", ServerSettingsSection.Devices, "devices"),
        new("下载", "serverdownloads", ServerSettingsSection.Devices, "downloads"),
        new("相机上传", "devices/cameraupload.html", ServerSettingsSection.Devices, "camera")
    ];

    private static readonly ServerSettingsMenuEntry[] AdvancedFixed =
    [
        new("插件", "plugins", ServerSettingsSection.Advanced, "plugins"),
        new("API 密钥", "apikeys", ServerSettingsSection.Advanced, "apikey"),
        new("元数据管理器", "metadatamanager", ServerSettingsSection.Advanced, "metadata")
    ];

    /// <summary>
    /// 三个分区，次序固定。没有插件页时三个分区仍然都在：截图里的固定条目本来就是三组。
    /// </summary>
    public static IReadOnlyList<ServerSettingsMenuGroup> Build(IEnumerable<ServerSettingsPluginPage>? pages = null)
    {
        var server = new List<ServerSettingsMenuEntry>(ServerFixed);
        var devices = new List<ServerSettingsMenuEntry>(DevicesFixed);
        var advanced = new List<ServerSettingsMenuEntry>();
        var seen = new HashSet<string>(
            ServerFixed.Concat(DevicesFixed).Concat(AdvancedFixed).Select(entry => entry.Route),
            StringComparer.Ordinal);

        var pluginPages = new List<(Placement Placement, ServerSettingsMenuEntry Entry)>();
        foreach (var page in pages ?? [])
        {
            if (page is null || !page.EnableInMainMenu) continue;

            var placement = PlacementOf(page.MenuSection);
            if (placement == Placement.None) continue;
            if (PluginEntry(page) is not { } entry) continue;
            if (!seen.Add(entry.Route)) continue;

            pluginPages.Add((placement, entry));
        }

        server.AddRange(pluginPages.Where(page => page.Placement == Placement.Server).Select(page => page.Entry));
        devices.AddRange(pluginPages.Where(page => page.Placement == Placement.Devices).Select(page => page.Entry));

        advanced.AddRange(pluginPages.Where(page => page.Placement == Placement.AdvancedBefore).Select(page => page.Entry));
        advanced.AddRange(AdvancedFixed);
        advanced.AddRange(pluginPages.Where(page => page.Placement == Placement.AdvancedAfter).Select(page => page.Entry));

        return
        [
            new(ServerGroupLabel, ServerSettingsSection.Server, server),
            new(DevicesGroupLabel, ServerSettingsSection.Devices, devices),
            new(AdvancedGroupLabel, ServerSettingsSection.Advanced, advanced)
        ];
    }

    /// <summary>
    /// 网页地址里的路由（<c>page</c> 给的 <c>location.hash</c>）落在哪一项上。先认完全相同的，再认
    /// 「设备」这种前缀：网页里点进一台设备后路由是 <c>devices/device.html?id=…</c>，左栏仍该亮着「设备」。
    /// </summary>
    public static ServerSettingsMenuEntry? Match(IReadOnlyList<ServerSettingsMenuGroup> groups, string? route)
    {
        var normalized = NormalizeRoute(route);
        if (normalized is null) return null;

        ServerSettingsMenuEntry? prefix = null;
        foreach (var entry in groups.SelectMany(group => group.Entries))
        {
            if (string.Equals(entry.Route, normalized, StringComparison.OrdinalIgnoreCase)) return entry;

            var under = normalized.StartsWith(entry.Route + "/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(entry.Route + "?", StringComparison.OrdinalIgnoreCase);
            if (under && (prefix is null || entry.Route.Length > prefix.Route.Length)) prefix = entry;
        }
        return prefix;
    }

    /// <summary>
    /// 服务器地址后面那段 <c>#!</c> 路由。只取井号后面的部分，不解释、不查询字符串，避免把网页自己
    /// 记在地址里的东西当成路径。
    /// </summary>
    public static string? RouteOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var hash = url.IndexOf('#');
        if (hash < 0) return null;

        var fragment = url[(hash + 1)..];
        if (fragment.StartsWith('!')) fragment = fragment[1..];
        fragment = fragment.TrimStart('/');
        return fragment.Length > 0 ? fragment : null;
    }

    /// <summary>
    /// 路由的写法归一：去掉 <c>#!</c>、<c>#/</c> 和开头的斜杠。网页端的 href 两种写法都有，
    /// 拼地址之前只留一份。
    /// </summary>
    public static string? NormalizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return null;

        var value = route.Trim();
        if (value.StartsWith("#!", StringComparison.Ordinal)) value = value[2..];
        else if (value.StartsWith("#/", StringComparison.Ordinal)) value = value[2..];
        else if (value.StartsWith('#')) value = value[1..];

        // 两个斜杠开头是协议相对地址，留给 IsSafeRoute 拒掉；只把正常的单斜杠前缀去掉。
        if (!value.StartsWith("//", StringComparison.Ordinal)) value = value.TrimStart('/');
        value = value.Replace(" ", "%20", StringComparison.Ordinal);
        return value.Length > 0 ? value : null;
    }

    /// <summary>
    /// 只允许留在服务器网页里的相对路由。绝对地址、协议相对地址、反斜杠、控制字符和 <c>javascript:</c>
    /// 一类带 scheme 的串都不进内嵌浏览器；插件页给错了就整条不显示，不把控制台带到别处。
    /// </summary>
    public static bool IsSafeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return false;
        if (route.StartsWith("//", StringComparison.Ordinal)) return false;
        if (route.Contains("://", StringComparison.Ordinal)) return false;
        if (route.Contains('\\') || route.Contains('#')) return false;
        if (route.Any(char.IsControl)) return false;

        var colon = route.IndexOf(':');
        if (colon > 0)
        {
            var scheme = route[..colon];
            if (scheme.All(character => char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.'))
                return false;
        }
        return true;
    }

    private enum Placement
    {
        Server,
        Devices,
        AdvancedBefore,
        AdvancedAfter,
        None
    }

    private static Placement PlacementOf(string? section)
    {
        if (string.IsNullOrWhiteSpace(section)) return Placement.AdvancedAfter;
        if (section.Trim().Equals("server", StringComparison.OrdinalIgnoreCase)) return Placement.Server;
        if (section.Trim().Equals("devices", StringComparison.OrdinalIgnoreCase)) return Placement.Devices;
        if (section.Trim().Equals("advanced", StringComparison.OrdinalIgnoreCase)) return Placement.AdvancedBefore;
        return Placement.None;
    }

    private static ServerSettingsMenuEntry? PluginEntry(ServerSettingsPluginPage page)
    {
        var label = FirstValue(page.DisplayName, page.Name);
        if (label is null) return null;

        var route = PluginRoute(page);
        if (route is null) return null;

        var icon = string.IsNullOrWhiteSpace(page.MenuIcon) ? "plugin" : page.MenuIcon.Trim();
        return new(label, route, SectionOf(page.MenuSection), icon, FromPlugin: true);
    }

    private static string? PluginRoute(ServerSettingsPluginPage page)
    {
        var href = page.Href?.Trim();
        if (string.IsNullOrEmpty(href))
        {
            var name = page.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            href = "configurationpage?name=" + Uri.EscapeDataString(name);
        }

        var route = NormalizeRoute(href);
        return route is not null && IsSafeRoute(route) ? route : null;
    }

    private static ServerSettingsSection SectionOf(string? section) => PlacementOf(section) switch
    {
        Placement.Server => ServerSettingsSection.Server,
        Placement.Devices => ServerSettingsSection.Devices,
        _ => ServerSettingsSection.Advanced
    };

    private static string? FirstValue(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
        if (!string.IsNullOrWhiteSpace(second)) return second.Trim();
        return null;
    }
}

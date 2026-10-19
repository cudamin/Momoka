using Momoka.Configuration;

namespace Momoka.Emby;

/// <summary>
/// 主界面用的是哪一套服务端界面。同时连着多台服务器时没有唯一的一套，退回通用界面；
/// 只有一台服务器时用那一台的界面。飞牛影视只有一台在线时也用它自己的界面 —— 至少设置左栏照它的目录分成
/// 「帐号」与「影视服务器」两组；Plex 仍走通用界面（2026-10-19 用户令）。
/// </summary>
public enum ServerInterfaceFlavor
{
    Generic,
    Emby,
    Jellyfin,
    FnNas
}

/// <summary>
/// 界面风味的纯规则：一台在线服务器决定界面，多台退回通用。所有已登录账号都同时在线，
/// 因此多账号本身不改变产品界面，只有不同服务器共存才退回通用。
/// <para>
/// 放在 Core 里而不是页面里，因为设置页（哪些服务端入口还在）与外壳（账号菜单的界面读数）问的是同一件事，
/// 而这两处任一处写错在屏幕上都不显眼。界面风味只看<b>在线来源</b>，不看保存了多少服务器。
/// </para>
/// </summary>
public static class ServerAccess
{
    /// <summary>
    /// 由在线服务器决定界面：只有一台时用那台的产品界面，没有服务器、或连着两台以上时一律通用
    /// —— 两台同为 Emby 的服务器也没有唯一的一套服务端入口，「只有一台」是用户的原话。
    /// 同一台服务器的多个账号不影响结论。
    /// </summary>
    public static ServerInterfaceFlavor InterfaceFor(int connectedServerCount, IEnumerable<MediaServerKind> onlineKinds)
    {
        if (connectedServerCount != 1) return ServerInterfaceFlavor.Generic;
        var kinds = onlineKinds.Distinct().Take(2).ToArray();
        return kinds.Length == 1 ? InterfaceFor(kinds[0]) : ServerInterfaceFlavor.Generic;
    }

    public static ServerInterfaceFlavor InterfaceFor(MediaServerKind kind) => kind switch
    {
        MediaServerKind.Emby => ServerInterfaceFlavor.Emby,
        MediaServerKind.Jellyfin => ServerInterfaceFlavor.Jellyfin,
        MediaServerKind.FnNas => ServerInterfaceFlavor.FnNas,
        _ => ServerInterfaceFlavor.Generic
    };

    /// <summary>这一套界面叫什么，给设置页和账号菜单读。</summary>
    public static string InterfaceName(ServerInterfaceFlavor flavor) => flavor switch
    {
        ServerInterfaceFlavor.Emby => "Emby 界面",
        ServerInterfaceFlavor.Jellyfin => "Jellyfin 界面",
        ServerInterfaceFlavor.FnNas => "飞牛影视界面",
        _ => "通用界面"
    };

    /// <summary>
    /// 服务端自己的管理功能（通知、网页控制台）只在界面能确定是哪一台服务器时出现。
    /// 通用界面下这些入口指向哪台服务器没有答案，整组收起。
    /// </summary>
    public static bool ShowsServerAdministration(ServerInterfaceFlavor flavor) =>
        flavor != ServerInterfaceFlavor.Generic;

    /// <summary>
    /// 这个账号能不能管这台服务器的库与设置。Jellyfin 与飞牛要求服务器明确答「是」；
    /// Emby 保留未知权限时的既有回退（不把「还没问到」当成「没有权限」），Plex 没有这套管理面。
    /// </summary>
    public static bool CanAdminister(MediaServerKind kind, bool? isAdministrator) => kind switch
    {
        MediaServerKind.Emby => isAdministrator != false,
        MediaServerKind.Jellyfin or MediaServerKind.FnNas => isAdministrator == true,
        _ => false
    };

    /// <summary>产品名的中文写法，服务器卡片上那颗小标签用。</summary>
    public static string KindName(MediaServerKind kind) => kind switch
    {
        MediaServerKind.Jellyfin => "Jellyfin",
        MediaServerKind.FnNas => "飞牛影视",
        MediaServerKind.Plex => "Plex",
        _ => "Emby"
    };
}

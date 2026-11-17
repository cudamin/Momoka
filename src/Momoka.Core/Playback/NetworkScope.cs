using System.Net;
using System.Net.Sockets;

namespace Momoka.Playback;

/// <summary>服务器地址落在本机／局域网，还是在外网。决定用哪一组网络缓冲。</summary>
public enum NetworkScope
{
    Lan,
    Wan
}

/// <summary>
/// 按服务器地址判定 <see cref="NetworkScope"/> —— 设置页「缓冲」卡里内网、外网两组数就靠它选。
/// <para>
/// <b>为什么按地址而不是让用户自己选：</b>同一台服务器在家里走局域网、在外面走公网，是同一个设置文件、
/// 同一条登录；多一个「当前网络」开关就等于每次换地方都要记得去拨，而拨错的那一次屏上什么都不会说。
/// 地址是唯一在起播那一刻已经知道、又不靠用户记性的判据。
/// </para>
/// <para>
/// 判成内网的是：本机与回环、IPv4 私有段（10/8、172.16/12、192.168/16）、链路本地（169.254/16）、运营商级
/// NAT 段（100.64/10，Tailscale 一类组网也用这一段）、IPv6 回环／链路本地／唯一本地地址，以及
/// <c>localhost</c>、<c>.local</c>／<c>.lan</c> 这类局域网域名和不带点的主机名（<c>nas</c>、<c>emby</c>）。
/// 其余一律按外网 —— 宁可多缓冲一点，也不要让外网片源按内网那组的小缓冲起播。
/// </para>
/// <para>
/// 不知道地址时（null、相对地址）也按外网：那是「判不出来」，不是「就在本机」。
/// </para>
/// </summary>
public static class NetworkScopeRule
{
    /// <summary>局域网专用的域名后缀。公网域名不会用这些，而它们解析出来的地址通常就是本机或家里那台机器。</summary>
    private static readonly string[] LanSuffixes = [".local", ".localdomain", ".lan", ".home", ".home.arpa", ".internal", ".intranet"];

    /// <summary>这条服务器地址该用哪一组缓冲。</summary>
    public static NetworkScope Classify(Uri? address)
    {
        if (address is null || !address.IsAbsoluteUri) return NetworkScope.Wan;
        return ClassifyHost(address.DnsSafeHost);
    }

    /// <summary>
    /// 主机名或 IP 字面量该用哪一组缓冲。单独拆出来是为了能直接钉住每一条判据 ——
    /// <see cref="Uri"/> 在解析时会把 IPv6 的方括号去掉、把 IDN 转成 punycode，测试里绕开它更省事。
    /// </summary>
    public static NetworkScope ClassifyHost(string? host)
    {
        var name = (host ?? "").Trim().Trim('[', ']');
        if (name.Length == 0) return NetworkScope.Wan;

        if (IPAddress.TryParse(name, out var address)) return IsLanAddress(address) ? NetworkScope.Lan : NetworkScope.Wan;

        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return NetworkScope.Lan;

        // 不带点的主机名在 Windows 上走的是本机／NetBIOS 名字解析，出不了公网。
        if (!name.Contains('.')) return NetworkScope.Lan;

        foreach (var suffix in LanSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return NetworkScope.Lan;
        }

        return NetworkScope.Wan;
    }

    private static bool IsLanAddress(IPAddress address)
    {
        // ::ffff:192.168.1.5 这种映射地址按里面那个 IPv4 判。
        if (address.IsIPv4MappedToIPv6) return IsLanAddress(address.MapToIPv4());
        if (IPAddress.IsLoopback(address)) return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                10 => true,                                            // 10/8
                127 => true,                                           // 回环（IsLoopback 已覆盖，留作显式）
                169 when bytes[1] == 254 => true,                      // 169.254/16 链路本地
                172 when bytes[1] is >= 16 and <= 31 => true,           // 172.16/12
                192 when bytes[1] == 168 => true,                      // 192.168/16
                100 when bytes[1] is >= 64 and <= 127 => true,          // 100.64/10 运营商级 NAT／组网
                _ => false
            };
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;

        // fe80::/10 链路本地、fec0::/10 站点本地（已废弃但仍是内网语义）、fc00::/7 唯一本地地址。
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC;
    }
}

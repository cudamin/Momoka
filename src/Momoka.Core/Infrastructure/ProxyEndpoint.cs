using System.Net;

namespace Momoka.Infrastructure;

/// <summary>
/// 设置 → 关于 → 网络 那一行「代理服务器」的解析与归一化。
/// <para>
/// 同一个值有两个消费者，形状却不一样：<c>TmdbClient</c> 要一个 <see cref="IWebProxy"/>，
/// WebView2 要一条 Chromium 的 <c>--proxy-server</c> 启动参数。两边各解析一遍就会在「socks5h 算不算
/// socks5」「没写端口怎么办」这些地方悄悄走岔，所以判断只在这里做一次，两处都从这里取。
/// </para>
/// <para>
/// 接受 <c>http(s)://</c>、<c>socks5://</c>、<c>socks5h://</c> 三种写法；不写 scheme 的
/// <c>127.0.0.1:8686</c> 按 http 处理。带账号、路径、查询串或空白的值一律不认 —— 这个字符串最后会被拼进
/// WebView2 的浏览器参数，换行和空格能把一条参数拆成两条，所以字符范围也一并卡死。
/// </para>
/// <para>
/// 空串是「不设置」：两个消费者都不拿到代理，.NET 与 Chromium 各自回到系统的代理设置 —— 与这一行出现之前
/// 的行为完全一样。
/// </para>
/// </summary>
public static class ProxyEndpoint
{
    /// <summary>认得出且形如 <c>scheme://host:port</c> 就交回那个地址；空串或认不出的值返回 false。</summary>
    public static bool TryParse(string? text, out Uri endpoint)
    {
        endpoint = null!;
        var value = (text ?? "").Trim();
        if (value.Length == 0) return false;

        // 代理地址里不该出现空白、引号和控制字符：它们会把 WebView2 的一条启动参数拆成两条。
        foreach (var character in value)
            if (character is <= ' ' or >= '\u007f') return false;

        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;

        if (uri.Scheme is not ("http" or "https" or "socks5" or "socks5h")) return false;
        if (uri.Host.Length == 0 || uri.HostNameType == UriHostNameType.Unknown) return false;
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
        if (uri.AbsolutePath is not ("" or "/")) return false;

        // http/https 不写端口时 Uri 会补 80/443；socks5 没有默认端口，必须由用户写明。
        if (uri.Port is < 1 or > 65535) return false;

        endpoint = uri;
        return true;
    }

    /// <summary>
    /// 落盘用的写法：认得出的地址归一成 <c>scheme://host:port</c>（scheme 与主机小写，端口写全）；
    /// 认不出的原样去空白交回 —— 用户可能正打到一半，下一次提交再归一。
    /// </summary>
    public static string Normalize(string? text)
    {
        var value = (text ?? "").Trim();
        return TryParse(value, out var endpoint) ? Canonical(endpoint) : value;
    }

    /// <summary>
    /// 交给 WebView2 的那一条启动参数；不设置或认不出时返回 null，浏览器参数里就一个字都不加。
    /// <para>
    /// Chromium 只认 <c>socks5</c>，没有 <c>socks5h</c> 这个名字（后者是 curl 的写法，意思是 DNS 也走代理）；
    /// 它的 SOCKS5 本来就由代理解析域名，所以这里换算成 socks5，行为与用户的意图一致。
    /// </para>
    /// </summary>
    public static string? BrowserArgument(string? text)
    {
        if (!TryParse(text, out var endpoint)) return null;
        return $"--proxy-server={BrowserScheme(endpoint)}://{HostAndPort(endpoint)}";
    }

    /// <summary>交给 <see cref="HttpClient"/> 的代理；不设置或认不出时返回 null（null 就是跟随系统）。</summary>
    public static IWebProxy? CreateWebProxy(string? text)
    {
        if (!TryParse(text, out var endpoint)) return null;
        return new WebProxy($"{BrowserScheme(endpoint)}://{HostAndPort(endpoint)}");
    }

    private static string BrowserScheme(Uri endpoint) => endpoint.Scheme == "socks5h" ? "socks5" : endpoint.Scheme;

    private static string Canonical(Uri endpoint) =>
        $"{endpoint.Scheme.ToLowerInvariant()}://{HostAndPort(endpoint)}";

    private static string HostAndPort(Uri endpoint)
    {
        var host = endpoint.Host.ToLowerInvariant();
        // Uri 交给我们的 IPv6 主机不带方括号，拼回地址时要自己加。
        if (host.Contains(':')) host = "[" + host + "]";
        return $"{host}:{endpoint.Port}";
    }
}

namespace Momoka.Plex;

/// <summary>
/// Plex's own origin. Unlike Emby, the server answers its API at the root of the address the user
/// typed, so this normalises the address and adds nothing — the same rule Jellyfin follows, plus
/// Plex's own port.
/// <para>
/// The port is the one place Plex differs from every other backend here. A bare host is taken to mean
/// <see cref="DefaultPort"/> because that is where a Plex server binds and what its own clients assume;
/// but that default is only applied to plain <c>http</c>. An <c>https</c> address with no port keeps the
/// scheme's 443, because a Plex server reached over TLS by a name rather than an address is almost
/// always behind a reverse proxy, and silently re-pointing it at 32400 would break exactly that setup.
/// </para>
/// </summary>
public static class PlexEndpoint
{
    /// <summary>The port the Plex Media Server binds by default.</summary>
    public const int DefaultPort = 32400;

    public static Uri Normalize(string input)
    {
        if (!TryNormalize(input, out var address, out var error))
            throw new ArgumentException(error, nameof(input));
        return address;
    }

    public static bool TryNormalize(string input, out Uri address, out string error)
    {
        address = null!;
        error = "";
        var text = (input ?? "").Trim();
        if (text.Length == 0) { error = "请填写 Plex 服务器地址"; return false; }
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
        { error = "Plex 服务器地址格式不正确"; return false; }
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        { error = "服务器地址只支持 http 或 https"; return false; }
        if (parsed.UserInfo.Length != 0)
        { error = "请在账户输入框填写凭据，不要写在服务器地址中"; return false; }

        // Read port presence from the original authority: Uri.Authority drops an explicit HTTP :80.
        // Uri still validates the syntax and supplies the numeric port; IPv6 Host already has brackets.
        var port = HasExplicitPort(text) || parsed.Scheme == Uri.UriSchemeHttps ? parsed.Port : DefaultPort;

        // Pasting the address out of a browser usually brings the Web UI path with it: Plex Web lives
        // under /web, and a user who copies the address bar has /web/index.html on the end of it.
        var parts = parsed.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var web = Array.FindIndex(parts, part => part.Equals("web", StringComparison.OrdinalIgnoreCase));
        if (web >= 0 && parts.Skip(web + 1).All(part => part.Equals("index.html", StringComparison.OrdinalIgnoreCase)))
            parts = parts[..web];
        var path = string.Join('/', parts);

        address = new UriBuilder(parsed.Scheme, parsed.Host, port)
        {
            Path = path + "/",
            Query = "",
            Fragment = ""
        }.Uri;
        return true;
    }

    private static bool HasExplicitPort(string text)
    {
        var start = text.IndexOf("://", StringComparison.Ordinal) + 3;
        var end = text.IndexOfAny(['/', '\\', '?', '#'], start);
        var authority = end < 0 ? text[start..] : text[start..end];
        var colon = authority.LastIndexOf(':');
        var hostEnd = authority.StartsWith('[') ? authority.IndexOf(']') : -1;
        return colon > hostEnd && colon + 1 < authority.Length;
    }

    /// <summary>The display address, retaining HTTP :80 so normalizing it again does not select 32400.</summary>
    public static string ToDisplayString(Uri apiBase)
    {
        if (apiBase.Scheme != Uri.UriSchemeHttp || apiBase.Port != 80)
            return apiBase.GetLeftPart(UriPartial.Path).TrimEnd('/');

        var authority = apiBase.GetComponents(UriComponents.StrongAuthority, UriFormat.UriEscaped);
        return (apiBase.Scheme + "://" + authority + apiBase.AbsolutePath).TrimEnd('/');
    }

    public static Uri Api(Uri apiBase, string relativePath)
    {
        Validate(apiBase);
        if (relativePath.StartsWith('/') || relativePath.Contains('\\') || relativePath.Contains('#') ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _) ||
            relativePath.Split('?', 2)[0].Split('/').Any(part => part is "." or ".."))
            throw new ArgumentException("Plex 接口路径无效", nameof(relativePath));
        var result = new Uri(apiBase, relativePath);
        if (!SameOrigin(apiBase, result) || !result.AbsolutePath.StartsWith(apiBase.AbsolutePath, StringComparison.Ordinal))
            throw new ArgumentException("Plex 接口不能离开当前服务器", nameof(relativePath));
        return result;
    }

    /// <summary>
    /// Plex's 「special」 endpoints live under <c>/:/</c>, which a relative <see cref="Uri"/> cannot
    /// express at all — the leading colon reads as a scheme and <see cref="Uri"/> refuses the reference.
    /// They are assembled from the base text instead, under the same origin and prefix check
    /// <see cref="Api"/> applies, so nothing else about them is looser.
    /// </summary>
    /// <param name="query">Already-escaped query text including its leading <c>?</c>, or empty.</param>
    public static Uri Special(Uri apiBase, string relativePath, string query)
    {
        Validate(apiBase);
        if (!relativePath.StartsWith(':') || relativePath.Contains('\\') || relativePath.Contains('#')
            || relativePath.Split('/').Any(part => part is "." or "..") || query.Contains('#'))
            throw new ArgumentException("Plex 接口路径无效", nameof(relativePath));
        var text = apiBase.AbsoluteUri.TrimEnd('/') + "/" + relativePath + query;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var result))
            throw new ArgumentException("Plex 接口路径无效", nameof(relativePath));
        if (!SameOrigin(apiBase, result) || !result.AbsolutePath.StartsWith(apiBase.AbsolutePath, StringComparison.Ordinal))
            throw new ArgumentException("Plex 接口不能离开当前服务器", nameof(relativePath));
        return result;
    }

    /// <summary>
    /// The part key a playback report needs, recovered from the address the file is fetched from.
    /// <c>/:/timeline</c> wants the key exactly as the server wrote it in the <c>Part</c> element, and
    /// that is what <see cref="Api"/> put after the base — so it is read back off the end of the path.
    /// </summary>
    public static string RelativeKey(Uri apiBase, Uri url)
    {
        var path = url.AbsolutePath;
        if (path.StartsWith(apiBase.AbsolutePath, StringComparison.Ordinal)) path = path[apiBase.AbsolutePath.Length..];
        return "/" + path.Trim('/');
    }

    public static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.IndexOfAny(['/', '\\', '?', '#', '%']) >= 0 || value.Any(char.IsControl))
            throw new ArgumentException("Plex 条目标识无效", nameof(value));
        return Uri.EscapeDataString(value);
    }

    internal static void Validate(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Plex 请求地址无效", nameof(uri));
    }

    internal static bool SameOrigin(Uri first, Uri second) =>
        first.Scheme.Equals(second.Scheme, StringComparison.OrdinalIgnoreCase) &&
        first.Host.Equals(second.Host, StringComparison.OrdinalIgnoreCase) && first.Port == second.Port;
}

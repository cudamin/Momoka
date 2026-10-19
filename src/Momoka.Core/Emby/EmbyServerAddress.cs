namespace Momoka.Emby;

/// <summary>
/// Turns whatever the user typed into a usable API base address. Accepts
/// "192.168.1.5:8896", "http://host/emby", "https://host/media/" and so on.
/// </summary>
public static class EmbyServerAddress
{
    /// <summary>The API base with a trailing slash; only Emby adds its "/emby/" API prefix.</summary>
    public static Uri Normalize(string input, MediaServerKind kind = MediaServerKind.Emby)
    {
        if (!TryNormalize(input, out var address, out var error, kind)) throw new ArgumentException(error, nameof(input));
        return address;
    }

    public static bool TryNormalize(string input, out Uri address, out string error, MediaServerKind kind = MediaServerKind.Emby)
    {
        if (kind == MediaServerKind.FnNas) return Momoka.FnNas.FnNasEndpoint.TryNormalize(input, out address, out error);
        if (kind == MediaServerKind.Plex) return Momoka.Plex.PlexEndpoint.TryNormalize(input, out address, out error);
        address = null!;
        error = "";

        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            error = "请填写服务器地址";
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            error = "服务器地址格式不正确";
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            error = "服务器地址只支持 http 或 https";
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Host))
        {
            error = "服务器地址缺少主机名";
            return false;
        }

        var path = parsed.AbsolutePath.TrimEnd('/');
        // A user who pastes the address out of a browser usually includes /emby already.
        if (kind == MediaServerKind.Emby && path.EndsWith("/emby", StringComparison.OrdinalIgnoreCase)) path = path[..^5];

        var builder = new UriBuilder(parsed.Scheme, parsed.Host, parsed.Port)
        {
            Path = path + (kind == MediaServerKind.Jellyfin ? "/" : "/emby/"),
            Query = "",
            Fragment = ""
        };

        address = builder.Uri;
        return true;
    }

    /// <summary>The display address; Jellyfin and Plex preserve the complete reverse-proxy path.</summary>
    public static string ToDisplayString(Uri apiBase, MediaServerKind kind = MediaServerKind.Emby)
    {
        var text = apiBase.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (kind == MediaServerKind.FnNas || kind == MediaServerKind.Plex) return text;
        return kind == MediaServerKind.Emby && text.EndsWith("/emby", StringComparison.OrdinalIgnoreCase) ? text[..^5] : text;
    }

    /// <summary>
    /// Carry a working address over to the product the server actually turned out to be. A profile saved
    /// while the client assumed Emby may have been normalized with the <c>/emby/</c> API prefix; once the
    /// public information says Jellyfin, the same server lives at the root (or the proxy sub-path), so the
    /// prefix has to come off before the web console can be found. The reverse case puts it back.
    /// </summary>
    public static Uri Rebase(Uri apiBase, MediaServerKind current, MediaServerKind detected)
    {
        if (current == detected) return apiBase;
        var display = ToDisplayString(apiBase, current);
        return TryNormalize(display, out var address, out _, detected) ? address : apiBase;
    }
}

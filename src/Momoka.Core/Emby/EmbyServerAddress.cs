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
        // Plex must retain an explicit HTTP :80; its default port for a portless host is 32400.
        if (kind == MediaServerKind.Plex) return Momoka.Plex.PlexEndpoint.ToDisplayString(apiBase);
        var text = apiBase.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (kind == MediaServerKind.FnNas) return text;
        return kind == MediaServerKind.Emby && text.EndsWith("/emby", StringComparison.OrdinalIgnoreCase) ? text[..^5] : text;
    }

    /// <summary>
    /// 换产品时采用匿名探测成功的同源入口；没有入口证据时保留已成功请求的路径，
    /// 不把真实反代路径末尾的 /emby/ 猜成客户端追加的前缀后剥掉。
    /// </summary>
    public static Uri Rebase(Uri apiBase, MediaServerKind current, MediaServerKind detected, Uri? confirmedBase = null)
    {
        if (confirmedBase is not null)
        {
            EmbyHttpRedirect.Validate(confirmedBase);
            if (!EmbyHttpRedirect.SameOrigin(apiBase, confirmedBase))
                throw new ArgumentException("确认的服务器入口必须与原地址同源", nameof(confirmedBase));
            return Normalize(confirmedBase.GetLeftPart(UriPartial.Path), detected);
        }
        return current == detected ? apiBase : Normalize(apiBase.GetLeftPart(UriPartial.Path), detected);
    }
}

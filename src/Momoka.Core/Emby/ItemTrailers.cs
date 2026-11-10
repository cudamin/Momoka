namespace Momoka.Emby;

/// <summary>详情预告入口及安全的远程候选；不修改服务器返回的原始 DTO。</summary>
public static class ItemTrailers
{
    private static readonly HashSet<string> CredentialKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key", "X-Emby-Token", "X-Plex-Token", "access_token"
    };

    public static bool Supports(MediaServerKind kind) => kind is MediaServerKind.Emby or MediaServerKind.Jellyfin;

    public static bool HasAny(EmbyItem item, MediaServerKind kind) =>
        Supports(kind) && (item.LocalTrailerCount > 0 || Remote(item).Count > 0);

    public static IReadOnlyList<MediaTrailerUrl> Remote(EmbyItem item)
    {
        var result = new List<MediaTrailerUrl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in item.RemoteTrailers ?? [])
        {
            if (candidate?.Url is not { } text || text.Any(char.IsControl)
                || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme is not "http" and not "https" || uri.UserInfo.Length > 0
                || Uri.UnescapeDataString(uri.AbsoluteUri).Any(char.IsControl)
                || HasCredentials(uri.Query) || HasCredentials(uri.Fragment) || !seen.Add(uri.AbsoluteUri)) continue;

            var name = CleanName(candidate.Name);
            if (name.Length == 0)
            {
                var title = CleanName(item.Name);
                name = title.Length == 0 ? "预告片" : title + " · 预告片";
            }
            result.Add(new MediaTrailerUrl { Url = uri.AbsoluteUri, Name = name });
        }
        return result;
    }

    private static bool HasCredentials(string component) => component.TrimStart('?', '#').Split('&', ';', '?').Any(part =>
        CredentialKeys.Contains(Uri.UnescapeDataString(part.Split('=', 2)[0]).Replace('+', ' ')));

    private static string CleanName(string? text) =>
        text is null ? "" : new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
}

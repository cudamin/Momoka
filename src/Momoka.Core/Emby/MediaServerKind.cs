namespace Momoka.Emby;

public enum MediaServerKind
{
    Emby,
    Jellyfin,
    FnNas,

    /// <summary>
    /// Plex Media Server. Its own protocol rather than a dialect of Emby's: the token travels in
    /// <c>X-Plex-Token</c>, an account is authenticated by Plex's account service rather than by the
    /// server, and browsing goes through <c>/library/sections</c>. See <c>src/Momoka.Core/Plex/</c>.
    /// </summary>
    Plex
}

/// <summary>Shared REST and playback authentication; credentials never enter a URL or launch arguments.</summary>
public static class MediaServerHeaders
{
    public static List<KeyValuePair<string, string>> Create(DeviceIdentity device, string? accessToken, MediaServerKind kind)
    {
        // 原始媒体 Range 请求只需 Authorization。管理和浏览 API 的 authx 由 FnNasHttp 负责。
        if (kind == MediaServerKind.FnNas)
        {
            if (string.IsNullOrEmpty(accessToken) || accessToken.Any(char.IsControl))
                throw new InvalidOperationException("飞牛影视登录凭据无效");
            return [new("Authorization", accessToken)];
        }
        // Plex 的令牌走 X-Plex-Token，客户端标识随行 —— 服务器用它把这条流归到本机这个「设备」上，
        // 而不是每起播一次就多出一个匿名播放器。两者都只在请求头里。
        if (kind == MediaServerKind.Plex)
        {
            if (string.IsNullOrEmpty(accessToken) || accessToken.Any(char.IsControl))
                throw new InvalidOperationException("Plex 登录凭据无效");
            return
            [
                new("X-Plex-Token", accessToken),
                new("X-Plex-Client-Identifier", DeviceIdentity.Sanitize(device.DeviceId))
            ];
        }
        if (kind == MediaServerKind.Jellyfin)
        {
            var authorization = device.ToAuthorizationHeader();
            if (!string.IsNullOrEmpty(accessToken)) authorization += $", Token=\"{Uri.EscapeDataString(accessToken)}\"";
            return [new("Authorization", authorization)];
        }

        var headers = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrEmpty(accessToken)) headers.Add(new("X-Emby-Token", accessToken));
        headers.Add(new("X-Emby-Authorization", device.ToAuthorizationHeader()));
        return headers;
    }
}

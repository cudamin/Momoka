namespace Momoka.Emby;

public enum MediaServerKind
{
    Emby,
    Jellyfin,
    FnNas
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

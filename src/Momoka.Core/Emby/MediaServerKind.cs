namespace Momoka.Emby;

public enum MediaServerKind
{
    Emby,
    Jellyfin
}

/// <summary>Shared REST and playback authentication; credentials never enter a URL or launch arguments.</summary>
public static class MediaServerHeaders
{
    public static List<KeyValuePair<string, string>> Create(DeviceIdentity device, string? accessToken, MediaServerKind kind)
    {
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

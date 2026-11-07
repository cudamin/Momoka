namespace Momoka.Emby;

/// <summary>
/// What this app calls itself when talking to Emby. A stable <see cref="DeviceId"/>
/// lets the server recognise the same client across restarts instead of piling up
/// one "device" per launch.
/// </summary>
public sealed record DeviceIdentity(string Client, string DeviceName, string DeviceId, string Version)
{
    public const string ClientName = "Momoka";

    public static DeviceIdentity Create(string deviceId, string version) =>
        new(ClientName, Environment.MachineName, deviceId, version);

    /// <summary>
    /// Emby parses this header by splitting on quotes. Keep delimiter protection and
    /// printable ASCII: the default HTTP transport rejects Unicode header values.
    /// A non-ASCII-only display name falls back to "unknown"; the stable device ID stays separate.
    /// </summary>
    public string ToAuthorizationHeader()
    {
        return $"MediaBrowser Client=\"{Sanitize(Client)}\", Device=\"{Sanitize(DeviceName)}\", " +
               $"DeviceId=\"{Sanitize(DeviceId)}\", Version=\"{Sanitize(Version)}\"";
    }

    internal static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var buffer = new char[value.Length];
        var length = 0;
        foreach (var character in value)
        {
            if (character is '"' or '\\' or ',' or ';') continue;
            if (character is < ' ' or > '~') continue;
            buffer[length++] = character;
        }

        var result = new string(buffer, 0, length).Trim();
        return result.Length == 0 ? "unknown" : result;
    }
}

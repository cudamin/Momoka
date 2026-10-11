using System.Globalization;

namespace Momoka.MoviePilot;

/// <summary>
/// Where a MoviePilot service lives, as the settings page can express it.
/// <para>
/// Modelled on <see cref="Emby.EmbyServerAddress"/> and for the same reason: the address a user types and the
/// address an API call wants are two different things, and the conversion has exactly one right answer per
/// input, so it belongs in Core where a test can hold it down rather than in a settings row.
/// </para>
/// <para>
/// MoviePilot serves two ports — the web UI and the API are separate listeners (3000 and 3001 by default).
/// Only the API one is any use here, and users reasonably paste the address they have in the browser. So a
/// bare host, a path, or the UI port all have to be tidied into something callable, and
/// <see cref="TryNormalize"/> is that tidying.
/// </para>
/// </summary>
public static class MoviePilotAddress
{
    /// <summary>The port MoviePilot's API listens on when nothing says otherwise.</summary>
    public const int DefaultApiPort = 3001;

    /// <summary>
    /// The scheme assumed when the user types a bare <c>host:port</c>. Plain HTTP is the right guess rather
    /// than HTTPS: this is a service on a home LAN, and assuming TLS would fail every call against a server
    /// that never offered it.
    /// </summary>
    private const string DefaultScheme = "http";

    /// <summary>
    /// Turns what a user typed into the base URI every request is built on, or explains why it cannot be.
    /// <para>
    /// A blank string is accepted and yields <c>null</c> rather than an error, because 「not configured yet」
    /// is the normal state of this setting and the row that shows it must be perfectly happy with it.
    /// </para>
    /// </summary>
    public static bool TryNormalize(string? text, out Uri? address, out string error)
    {
        address = null;
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            // Not a failure: it means the user has not filled this in, and there is nothing to call.
            return true;
        }

        var trimmed = text.Trim();

        // A scheme is required for Uri to treat this as absolute; anything without one gets http://, which is
        // what lets "192.0.2.10:3001" parse as host plus port instead of as a relative path.
        if (!trimmed.Contains("://", StringComparison.Ordinal)) trimmed = DefaultScheme + "://" + trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            error = "地址看不出来是一个网址，像这样写：192.168.1.5:3001";
            return false;
        }

        if (parsed.Scheme is not ("http" or "https"))
        {
            error = $"只认 http 和 https，不认 {parsed.Scheme}";
            return false;
        }

        if (string.IsNullOrEmpty(parsed.Host))
        {
            error = "地址里没有主机名";
            return false;
        }

        address = new UriBuilder(parsed.Scheme, parsed.Host, Port(parsed)).Uri;
        return true;
    }

    /// <summary>
    /// The port to actually call. An explicitly typed port is kept as-is — including 3000, which is the web
    /// UI's port: someone running the API there on purpose is a configuration this has no business
    /// overriding, and the connection test is what tells him he guessed wrong.
    /// <para>
    /// Only a port nobody typed moves, which is what <see cref="Uri.IsDefaultPort"/> reports.
    /// </para>
    /// </summary>
    private static int Port(Uri parsed) => parsed.IsDefaultPort ? DefaultApiPort : parsed.Port;

    /// <summary>
    /// The three boxes the settings card shows for one stored address: the http/https pick, the host, and
    /// the port (empty when none is set). See <see cref="Split"/> and <see cref="Join"/>.
    /// </summary>
    public readonly record struct Parts(string Scheme, string Host, string Port);

    /// <summary>
    /// Pulls a stored address apart into the card's three boxes — the inverse of <see cref="Join"/>. Lives
    /// here, beside the composing half and the port resolver, so the one thing that knows what a MoviePilot
    /// address is stays one thing.
    /// <para>
    /// The port box is left empty when the stored address names no explicit port: that is 「用默认 3001」, and
    /// a box pre-filled with 3001 would turn the next save into a value the user never typed.
    /// <see cref="TryNormalize"/> is still what fills the default in on the way out, so an empty box and a box
    /// reading 3001 call the same server. A blank or unparseable string hands the host box back whatever text
    /// there is rather than silently blanking it; the 测试连接 button rejects it with the same message as before.
    /// </para>
    /// </summary>
    public static Parts Split(string? text)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return new Parts(DefaultScheme, "", "");

        var hasScheme = trimmed.Contains("://", StringComparison.Ordinal);
        var candidate = hasScheme ? trimmed : DefaultScheme + "://" + trimmed;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
            && parsed.Host.Length > 0)
        {
            // 3001 is never a scheme's default port, so IsDefaultPort is exactly 「no port was written」 —
            // which is what keeps the box empty for a bare host and fills it only for a port the user gave.
            var port = parsed.IsDefaultPort ? "" : parsed.Port.ToString(CultureInfo.InvariantCulture);
            return new Parts(parsed.Scheme, parsed.Host, port);
        }

        var scheme = hasScheme && trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : DefaultScheme;
        var host = hasScheme ? trimmed[(trimmed.IndexOf("://", StringComparison.Ordinal) + 3)..] : trimmed;
        return new Parts(scheme, host, "");
    }

    /// <summary>
    /// Composes the stored address out of the card's three boxes — the inverse of <see cref="Split"/>. A blank
    /// host yields the empty string（「还没填」, the normal fresh state）rather than a scheme with nothing after
    /// it; the port rides on <see cref="TryNormalize"/>'s default whenever the user left it empty.
    /// </summary>
    public static string Join(string? scheme, string? host, string? port)
    {
        host = (host ?? "").Trim();
        if (host.Length == 0) return "";

        var normalisedScheme = string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : DefaultScheme;
        port = (port ?? "").Trim();
        return port.Length == 0 ? $"{normalisedScheme}://{host}" : $"{normalisedScheme}://{host}:{port}";
    }

    /// <summary>Builds the absolute URI for one API path, e.g. <c>api/v1/dashboard/system</c>.</summary>
    public static Uri Combine(Uri apiBase, string path)
    {
        var trimmed = path.TrimStart('/');
        return new Uri(apiBase, trimmed);
    }
}

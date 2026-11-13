namespace Momoka.Emby;

public static class TrailerPreview
{
    public static string? YouTubeId(IEnumerable<MediaTrailerUrl> trailers)
    {
        foreach (var trailer in trailers)
        {
            if (!Uri.TryCreate(trailer.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort) continue;
            string? id = null;
            if (uri.Host == "youtu.be" && uri.AbsolutePath.Count(character => character == '/') == 1)
                id = uri.AbsolutePath[1..];
            else if (uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com" && uri.AbsolutePath == "/watch")
            {
                var values = uri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("v=", StringComparison.Ordinal)).ToArray();
                if (values.Length == 1) id = values[0][2..];
            }
            if (id is { Length: 11 } && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return id;
        }
        return null;
    }
}

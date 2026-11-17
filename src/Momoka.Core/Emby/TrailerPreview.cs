namespace Momoka.Emby;

public static class TrailerPreview
{
    /// <summary>预告只装饰顶部右侧，保持16:9，不铺到左栏或底部正文。</summary>
    public static double BackgroundWidth(double width) =>
        Math.Min((double.IsFinite(width) && width > 0 ? width : 1280) * 0.60, 920);

    public static double BackgroundHeight(double width) => BackgroundWidth(width) * 9 / 16;

    public static string? YouTubeId(IEnumerable<MediaTrailerUrl> trailers)
    {
        foreach (var trailer in trailers)
        {
            if (YouTubeId(trailer.Url) is { } id) return id;
        }
        return null;
    }

    /// <summary>
    /// 条目自己的 YouTube 媒体地址。Emby 的 Trailer channel 把预告直接存成 <c>watch?v=</c> 的
    /// <see cref="MediaSource.Path"/>，没有 RemoteTrailers；这类条目也要能走详情页右上角的 YouTube 预览，
    /// 否则登录了也没有播放入口。只认 Trailer／Video，避免把正片的异常地址当成预告。
    /// </summary>
    public static string? YouTubeId(EmbyItem item)
    {
        var remote = YouTubeId(ItemTrailers.Remote(item));
        if (remote is not null) return remote;
        if (item.Type is not (EmbyItemType.Trailer or EmbyItemType.Video)) return null;
        return YouTubeId(item.DefaultMediaSource?.Path);
    }

    public static string? YouTubeId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort) return null;
        string? id = null;
        if (uri.Host == "youtu.be" && uri.AbsolutePath.Count(character => character == '/') == 1)
            id = uri.AbsolutePath[1..];
        else if (uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
        {
            if (uri.AbsolutePath == "/watch")
            {
                var values = uri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("v=", StringComparison.Ordinal)).ToArray();
                if (values.Length == 1) id = values[0][2..];
            }
            else if (uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal)
                || uri.AbsolutePath.StartsWith("/shorts/", StringComparison.Ordinal))
            {
                var segment = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
                if (segment.Length > 0) id = segment;
            }
        }
        return id is { Length: 11 } && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ? id : null;
    }
}

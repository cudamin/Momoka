using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Plex;

/// <summary>
/// The item metadata key and whole-version duration used by Plex timeline reports.
/// Part keys identify files to fetch; official clients report the item key and a global position.
/// Credentials remain in transport headers.
/// </summary>
public sealed record PlexPlaybackSelection(string RatingKey, string MetadataKey, long DurationTicks)
{
    public static PlexPlaybackSelection FromSource(string itemId, Uri apiBase, MediaSource source, long duration)
    {
        MediaUrl(apiBase, source);
        MediaParts(apiBase, source);
        return new(itemId, "/library/metadata/" + PlexEndpoint.Segment(itemId), duration);
    }

    public static IReadOnlyList<PlaybackPart> MediaParts(Uri apiBase, MediaSource source)
    {
        foreach (var part in source.MediaParts)
        {
            ValidateMediaUrl(apiBase, part.MediaUrl);
            if (part.DurationTicks <= 0) throw new InvalidOperationException("Plex 分段媒体缺少有效时长");
        }
        return source.MediaParts;
    }

    /// <summary>
    /// The original file, or a refusal. The address has to be on the server's own origin, carry no
    /// credentials of its own and no query or fragment — a media address that fails any of those is one
    /// this client will not hand to mpv, because it cannot say where it really goes.
    /// </summary>
    public static Uri MediaUrl(Uri apiBase, MediaSource? source)
    {
        if (source?.DirectStreamUrl is not { } address)
            throw new InvalidOperationException("Plex 未提供可安全播放的原始媒体地址");
        ValidateMediaUrl(apiBase, address);
        return address;
    }

    private static void ValidateMediaUrl(Uri apiBase, Uri address)
    {
        if (!PlexEndpoint.SameOrigin(apiBase, address) || address.UserInfo.Length != 0
            || address.Query.Length != 0 || address.Fragment.Length != 0)
            throw new InvalidOperationException("Plex 未提供可安全播放的原始媒体地址");
    }

    /// <summary>An external subtitle, by the path Plex put on the stream itself.</summary>
    public static Uri SubtitleUrl(Uri apiBase, MediaStream stream)
    {
        if (stream.DeliveryUrl is not { Length: > 0 } path)
            throw new NotSupportedException("该字幕轨不是 Plex 可单独下载的文件");
        return PlexEndpoint.Api(apiBase, path.TrimStart('/'));
    }
}

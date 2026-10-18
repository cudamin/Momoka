using Momoka.Emby;

namespace Momoka.Plex;

/// <summary>
/// What a Plex playback report needs beyond the position: which item, which file, and how long that file
/// is.
/// <para>
/// Plex reports progress against the <b>part</b> key rather than the item id — the same path the file is
/// fetched from — which is the one thing about its reporting that has no Emby equivalent and the reason
/// this type exists at all. It carries no credentials: the token travels in the request headers.
/// </para>
/// </summary>
public sealed record PlexPlaybackSelection(string RatingKey, string PartKey, long DurationTicks)
{
    public static PlexPlaybackSelection FromSource(string itemId, Uri apiBase, MediaSource source, long duration) =>
        new(itemId, PlexEndpoint.RelativeKey(apiBase, MediaUrl(apiBase, source)), duration);

    /// <summary>
    /// The original file, or a refusal. The address has to be on the server's own origin, carry no
    /// credentials of its own and no query or fragment — a media address that fails any of those is one
    /// this client will not hand to mpv, because it cannot say where it really goes.
    /// </summary>
    public static Uri MediaUrl(Uri apiBase, MediaSource? source)
    {
        if (source?.DirectStreamUrl is not { } address || !PlexEndpoint.SameOrigin(apiBase, address)
            || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0)
            throw new InvalidOperationException("Plex 未提供可安全播放的原始媒体地址");
        return address;
    }

    /// <summary>An external subtitle, by the path Plex put on the stream itself.</summary>
    public static Uri SubtitleUrl(Uri apiBase, MediaStream stream)
    {
        if (stream.DeliveryUrl is not { Length: > 0 } path)
            throw new NotSupportedException("该字幕轨不是 Plex 可单独下载的文件");
        return PlexEndpoint.Api(apiBase, path.TrimStart('/'));
    }
}

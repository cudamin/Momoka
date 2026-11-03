using Momoka.Infrastructure;

namespace Momoka.Emby;

/// <summary>
/// Jellyfin 10.10 起的媒体段：插件（Intro Skipper、官方章节段提供器、SkipMe.db 等）把「这一集哪里可以跳」
/// 写进服务器，客户端按条目问一次就拿到。接口是 <c>GET /MediaSegments/{itemId}</c>，回答包一层
/// <c>Items</c>。
/// <para>
/// 它和章节是两套数据：章节只有起点、没有类型；媒体段有类型也有终点。Jellyfin 的
/// <c>ChapterInfo</c> 里没有 Emby 那个 <c>MarkerType</c>，所以片头片尾只能从这条路读，不能指望
/// <c>Fields=Chapters</c> 带出来。
/// </para>
/// </summary>
public sealed class MediaSegmentsResult
{
    public List<MediaSegment> Items { get; set; } = [];
}

/// <summary>One Jellyfin media segment; ticks are 100-nanosecond units, as everywhere else.</summary>
public sealed class MediaSegment
{
    /// <summary><c>Intro</c>, <c>Outro</c>, <c>Recap</c>, <c>Preview</c>, <c>Commercial</c> or <c>Unknown</c>.</summary>
    public string? Type { get; set; }

    public long StartTicks { get; set; }

    public long EndTicks { get; set; }
}

/// <summary>媒体段折成跳过区间：只认片头与片尾，别让一段广告弹出「跳过片头」。</summary>
public static class MediaSegments
{
    /// <summary>
    /// Every <c>Intro</c>/<c>Outro</c> segment as a server skip range, in playing order. Other kinds are
    /// dropped: this client has one action (jump over it) and it does not apply to commercials or recaps.
    /// </summary>
    public static List<ServerSkipMarker> ToSkipMarkers(MediaSegmentsResult? result)
    {
        var markers = new List<ServerSkipMarker>();
        foreach (var segment in result?.Items ?? [])
        {
            var kind = segment.Type?.Trim() switch
            {
                var type when string.Equals(type, "Intro", StringComparison.OrdinalIgnoreCase) => ServerSkipMarkerKind.Opening,
                var type when string.Equals(type, "Outro", StringComparison.OrdinalIgnoreCase) => ServerSkipMarkerKind.Ending,
                _ => (ServerSkipMarkerKind?)null
            };
            if (kind is not { } found) continue;
            // 先按刻判：TimeFormat.ToSeconds 把非正值折成 0，负起点会在换算之后伪装成一个合法区间。
            if (segment.StartTicks < 0 || segment.EndTicks <= segment.StartTicks) continue;

            var start = TimeFormat.ToSeconds(segment.StartTicks);
            var end = TimeFormat.ToSeconds(segment.EndTicks);
            if (!double.IsFinite(start) || !double.IsFinite(end)) continue;
            markers.Add(new ServerSkipMarker(found, start, end));
        }

        markers.Sort((left, right) => left.StartSeconds.CompareTo(right.StartSeconds));
        return markers;
    }
}

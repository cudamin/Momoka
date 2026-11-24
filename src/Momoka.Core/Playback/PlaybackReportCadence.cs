using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>
/// 播放进度上报的固定节奏。2026-11-24 用户令删掉「进度上报间隔」这一格，改按各家官方网页播放器自己的间隔走。
/// <para>
/// Emby Web 4.10.1.0 的 <c>startPlaybackProgressTimer</c> 是 10 秒，<c>ApiClient.shouldSkipProgressReport</c>
/// 还把 <c>timeupdate</c> 压到距上次 ≥10 秒（位置跳变 ≥5 秒才提前发）；Jellyfin Web 10.10.7 的定时器同为
/// 10 秒，<c>jellyfin-apiclient</c> 1.11.0 对 <c>timeupdate</c> 也设了 10 秒下限；飞牛影视官方前端 0.9.8 的
/// <c>PLAYER_RECORD_INTERVAL</c> 是 15 秒。Plex 网页端是闭源的压缩产物，暂按用户令取 10 秒，这不是对
/// Plex 网页端的源码结论。飞牛音乐的进度上报在客户端是空操作，取 10 秒只为映射完整。
/// </para>
/// </summary>
public static class PlaybackReportCadence
{
    public static TimeSpan For(MediaServerKind kind) => kind switch
    {
        MediaServerKind.FnNas => TimeSpan.FromSeconds(15),
        MediaServerKind.Plex => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(10)
    };
}

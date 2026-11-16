namespace Momoka.Emby;

/// <summary>The video's single-item playlist command does not expand folders or the shared music domain.</summary>
public static class ServerPlaylistItems
{
    public static bool CanAddVideo(EmbyItem item) => !item.IsDisplayOnly && !string.IsNullOrWhiteSpace(item.Id)
        && item.MediaType != "Game"
        && item.IsPlayable && item.Type is EmbyItemType.Movie or EmbyItemType.Episode or EmbyItemType.Video
            or EmbyItemType.MusicVideo or EmbyItemType.Trailer;

    /// <summary>
    /// Emby 的播放列表接口会展开剧、季、文件夹、合集、播放列表和媒体库本身；单个视频照旧直接收 ——
    /// 网页端 <c>canAddToPlaylist</c> 对它们同样放行，只排除直播、播放列表库、游戏和没有 id 的存根。
    /// Jellyfin 那一侧不要用这一条：它仍按 <see cref="CanAddVideo"/> 只收单个视频。
    /// </summary>
    public static bool CanAddContainer(EmbyItem item) => !item.IsDisplayOnly && !string.IsNullOrWhiteSpace(item.Id)
        && item.CollectionType is not ("livetv" or "playlists") && item.MediaType != "Game"
        && (item.IsPlayable || item.Type is EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Folder
            or EmbyItemType.BoxSet or EmbyItemType.Playlist or EmbyItemType.CollectionFolder);
}

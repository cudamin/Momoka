namespace Momoka.Emby;

/// <summary>The video's single-item playlist command does not expand folders or the shared music domain.</summary>
public static class ServerPlaylistItems
{
    public static bool CanAddVideo(EmbyItem item) => !item.IsDisplayOnly && !string.IsNullOrWhiteSpace(item.Id)
        && item.IsPlayable && item.Type is EmbyItemType.Movie or EmbyItemType.Episode or EmbyItemType.Video
            or EmbyItemType.MusicVideo or EmbyItemType.Trailer;
}

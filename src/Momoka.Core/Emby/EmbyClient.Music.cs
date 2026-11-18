using Momoka.Music;

namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>
    /// 服务器自带的歌词。飞牛音乐把内嵌、同名 .lrc 与手工歌词统一放在 <c>lyric/list</c>；
    /// 其他服务器没有核对过的歌词接口，返回 null 让调用方退回文件标签，不发无谓请求。
    /// </summary>
    public Task<MusicLyrics?> GetLyricsAsync(string itemId, CancellationToken cancellationToken) =>
        Connection.IsFnMusic
            ? FnMusicMedia.GetLyricsAsync(itemId, cancellationToken)
            : Task.FromResult<MusicLyrics?>(null);

    /// <summary>Artists are a named index, not descendants; artist songs use an association filter.</summary>
    private Task<ItemsResult>? MusicItems(ItemQuery query, CancellationToken cancellationToken)
    {
        if (query.IncludeItemTypes.SequenceEqual([EmbyItemType.MusicArtist]))
        {
            // Jellyfin ArtistsController.GetArtists; Emby's JS client getArtists uses the same index.
            var parameters = query.ToParameters().Where(pair => pair.Key != "IncludeItemTypes").ToList();
            parameters.Add(("UserId", Connection.UserId));
            return http.GetJsonAsync<ItemsResult>(Url("Artists", [.. parameters]), Context, cancellationToken);
        }
        if (query.ParentItemType == EmbyItemType.MusicArtist)
        {
            var parameters = query.ToParameters().Where(pair => pair.Key != "ParentId").ToArray();
            return http.GetJsonAsync<ItemsResult>(Url($"Users/{Connection.UserId}/Items", parameters), Context, cancellationToken);
        }
        if (query.ParentItemType == EmbyItemType.Playlist && query.IncludeItemTypes.SequenceEqual([EmbyItemType.Audio]))
        {
            var parameters = query.ToParameters().Where(pair => pair.Key is not ("ParentId" or "Recursive" or "SortBy" or "SortOrder")).ToList();
            parameters.Add(("UserId", Connection.UserId));
            return http.GetJsonAsync<ItemsResult>(Url($"Playlists/{Uri.EscapeDataString(query.ParentId!)}/Items", [.. parameters]), Context, cancellationToken);
        }
        return null;
    }
}

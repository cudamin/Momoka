using Momoka.Emby;

namespace Momoka.Plex;

public sealed partial class PlexMediaAdapter
{
    private static bool IsMusicQuery(ItemQuery query) => query.IncludeItemTypes.Any(type =>
        type is EmbyItemType.Audio or EmbyItemType.MusicAlbum or EmbyItemType.MusicArtist or EmbyItemType.Playlist)
        || query.ParentItemType is EmbyItemType.MusicAlbum or EmbyItemType.MusicArtist or EmbyItemType.Playlist;

    private async Task<ItemsResult> GetMusicItemsAsync(ItemQuery query, CancellationToken ct)
    {
        if (query.IncludeItemTypes.Count > 1)
            throw new NotSupportedException("Plex 音乐浏览一次需要选择一种条目类型");
        var parentId = query.ParentId!;
        var parentType = query.ParentItemType;
        if (string.IsNullOrEmpty(parentType))
        {
            // Both section key and ratingKey are integers. Without the caller's captured parent type,
            // first settle the namespace from actual sections, never infer it from a numeric ID.
            var sections = await client.GetSectionsAsync(connection, ct).ConfigureAwait(false);
            parentType = Items(sections).Any(section => Text(section, "key") == parentId)
                ? EmbyItemType.CollectionFolder
                : Item(First(await client.GetMetadataAsync(connection, parentId, ct).ConfigureAwait(false))).Type;
        }

        // Official plex-for-kodi 97191f2: audio.py artist/album children and artist allLeaves;
        // plexlibrary.py Playlist.items/PlaylistHub.extend. Preserve server disc/playlist ordering.
        var page = parentType switch
        {
            EmbyItemType.CollectionFolder when query.IncludeItemTypes.SequenceEqual([EmbyItemType.Playlist]) =>
                await client.GetAudioPlaylistsAsync(connection, query.StartIndex, query.Limit, ct).ConfigureAwait(false),
            EmbyItemType.CollectionFolder => await client.GetSectionItemsAsync(connection, parentId, Query(query),
                query.StartIndex, query.Limit, ct).ConfigureAwait(false),
            EmbyItemType.MusicArtist when query.IncludeItemTypes.SequenceEqual([EmbyItemType.Audio]) =>
                await client.GetAllLeavesAsync(connection, parentId, query.StartIndex, query.Limit, ct).ConfigureAwait(false),
            EmbyItemType.MusicArtist or EmbyItemType.MusicAlbum =>
                await client.GetChildrenAsync(connection, parentId, query.StartIndex, query.Limit, ct).ConfigureAwait(false),
            EmbyItemType.Playlist =>
                await client.GetPlaylistItemsAsync(connection, parentId, query.StartIndex, query.Limit, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException("Plex 的这个条目不是音乐媒体库或音乐容器")
        };
        var values = Items(page).Select(Item).ToList();
        return new ItemsResult { Items = values, TotalRecordCount = Integer(page, "totalSize") ?? values.Count };
    }
}

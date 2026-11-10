using Momoka.Emby;

namespace Momoka.Music;

public enum MusicSection { Tracks, Albums, Artists, Playlists, Favorites }

/// <summary>Music-specific queries do not change the existing video search and recommendation scopes.</summary>
public static class MusicCatalog
{
    public const int PageSize = 80;

    public static bool IsAudio(string? type) => string.Equals(type, EmbyItemType.Audio, StringComparison.OrdinalIgnoreCase);
    public static bool IsLibrary(string? type) => string.Equals(type, "music", StringComparison.OrdinalIgnoreCase);
    public static bool IsContainer(string? type) => type is EmbyItemType.MusicAlbum or EmbyItemType.MusicArtist;

    public static ItemQuery Query(string? rootId, string? containerId, string? containerType,
        MusicSection section, int start, string? search)
    {
        var artist = containerType == EmbyItemType.MusicArtist;
        var type = section switch
        {
            MusicSection.Albums => EmbyItemType.MusicAlbum,
            MusicSection.Artists => EmbyItemType.MusicArtist,
            MusicSection.Playlists => EmbyItemType.Playlist,
            _ => EmbyItemType.Audio
        };
        return new ItemQuery
        {
            ParentId = containerId ?? rootId,
            ParentItemType = containerType ?? EmbyItemType.CollectionFolder,
            IncludeItemTypes = [type],
            ArtistIds = artist && containerId is { Length: > 0 }
                ? [containerId.StartsWith("fnmusic:artist:", StringComparison.Ordinal) ? containerId["fnmusic:artist:".Length..] : containerId] : [],
            Recursive = true,
            StartIndex = Math.Max(0, start),
            Limit = PageSize,
            SearchTerm = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            IsFavorite = section == MusicSection.Favorites,
            SortBy = containerType == EmbyItemType.MusicAlbum ? EmbySortBy.Number
                : containerType == EmbyItemType.Playlist ? "" : EmbySortBy.Name,
            Fields = EmbyFields.Browse + ",MediaSources,ParentId"
        };
    }

    public static string Subtitle(EmbyItem item) => string.Join(" · ",
        new[] { string.Join(" / ", item.Artists), item.Album }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

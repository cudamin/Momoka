namespace Momoka.Emby;

public enum LibrarySection
{
    Items, Recommended, Trailers, Collections, Genres, Favorites, Studios, Episodes, Folders
}

public sealed record LibrarySectionEntry(LibrarySection Section, string Label);

/// <summary>Library identity stays in every section query, including genre/studio drill-downs.</summary>
public static class LibraryBrowse
{
    public static IReadOnlyList<LibrarySectionEntry> Sections(string? collectionType, MediaServerKind serverKind)
    {
        var movies = collectionType == "movies";
        if (!movies && collectionType != "tvshows") return [];
        List<LibrarySectionEntry> entries = movies
            ? [new(LibrarySection.Items, "影片"), new(LibrarySection.Recommended, "推荐"),
                new(LibrarySection.Trailers, "预告片"), new(LibrarySection.Collections, "合集"),
                new(LibrarySection.Genres, "类型"), new(LibrarySection.Favorites, "收藏"),
                new(LibrarySection.Folders, "文件夹")]
            : [new(LibrarySection.Items, "节目"), new(LibrarySection.Recommended, "推荐"),
                new(LibrarySection.Trailers, "预告"), new(LibrarySection.Favorites, "收藏"),
                new(LibrarySection.Genres, "类型"), new(LibrarySection.Studios, "播出平台"),
                new(LibrarySection.Episodes, "集"), new(LibrarySection.Folders, "文件夹")];
        // Jellyfin Suggestions has no ParentId: it cannot recommend within this library.
        // Other adapters don't implement the named indexes or physical folder hierarchy.
        return [.. entries.Where(entry => serverKind switch
        {
            MediaServerKind.Emby => true,
            MediaServerKind.Jellyfin => entry.Section != LibrarySection.Recommended,
            MediaServerKind.FnNas => entry.Section is LibrarySection.Items or LibrarySection.Favorites,
            MediaServerKind.Plex => entry.Section == LibrarySection.Items,
            _ => entry.Section == LibrarySection.Items
        })];
    }

    public static bool IsIndex(LibrarySection section, string? group) =>
        group is null && section is LibrarySection.Genres or LibrarySection.Studios;

    public static string? SortPreset(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Episodes => EmbySortPreset.Episode,
        LibrarySection.Collections => EmbySortPreset.BoxSet,
        LibrarySection.Trailers => EmbySortPreset.Trailer,
        LibrarySection.Folders => null,
        _ => EmbySortPreset.ForCollection(collectionType)
    };

    public static ItemQuery Query(string collectionType, string parentId, LibrarySection section,
        int start, int limit, string sort, bool descending, ItemFilters filters, string? group = null)
    {
        var index = IsIndex(section, group);
        return new ItemQuery
        {
            ParentId = parentId,
            Recursive = section != LibrarySection.Folders,
            IncludeItemTypes = section switch
            {
                LibrarySection.Episodes => [EmbyItemType.Episode],
                LibrarySection.Collections => [EmbyItemType.BoxSet],
                LibrarySection.Trailers => [EmbyItemType.Trailer],
                LibrarySection.Folders => [],
                _ => collectionType == "movies" ? [EmbyItemType.Movie, EmbyItemType.Video] : [EmbyItemType.Series, EmbyItemType.Video]
            },
            SortBy = index ? EmbySortBy.Name : section == LibrarySection.Recommended ? "" : sort,
            Descending = !index && descending,
            StartIndex = start,
            Limit = limit,
            IsFavorite = section == LibrarySection.Favorites,
            Genre = section == LibrarySection.Genres ? group : null,
            Studio = section == LibrarySection.Studios ? group : null,
            Filters = index ? new ItemFilters() : filters.Clone()
        };
    }

    public static string Path(LibrarySection section, string? group, string userId) => section switch
    {
        LibrarySection.Recommended => $"Users/{userId}/Suggestions",
        LibrarySection.Trailers => "Trailers",
        LibrarySection.Genres when group is null => "Genres",
        LibrarySection.Studios when group is null => "Studios",
        _ => $"Users/{userId}/Items"
    };
}

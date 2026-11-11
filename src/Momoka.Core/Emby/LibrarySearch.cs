namespace Momoka.Emby;

public enum LibrarySearchCategory
{
    TopResults,
    Series,
    Movies,
    Episodes,
    People,
    Collections,
    Playlists
}

/// <summary>搜索的分类独立于媒体库浏览的排序和筛选，保留服务器的相关性顺序。</summary>
public static class LibrarySearch
{
    public static bool SupportsCategory(MediaServerKind kind, LibrarySearchCategory category) =>
        Enum.IsDefined(category) && (category <= LibrarySearchCategory.Episodes
            || kind is MediaServerKind.Emby or MediaServerKind.Jellyfin);

    public static ItemQuery? Query(string? term, LibrarySearchCategory category, int start, int limit,
        MediaServerKind kind = MediaServerKind.Emby)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!SupportsCategory(kind, category)) throw new NotSupportedException("当前服务器不支持此搜索分类");
        return new ItemQuery
        {
            SearchTerm = term.Trim(),
            Recursive = true,
            IncludeItemTypes = category switch
            {
                LibrarySearchCategory.Series => [EmbyItemType.Series],
                LibrarySearchCategory.Movies => [EmbyItemType.Movie],
                LibrarySearchCategory.Episodes => [EmbyItemType.Episode],
                LibrarySearchCategory.People => [EmbyItemType.Person],
                LibrarySearchCategory.Collections => [EmbyItemType.BoxSet],
                LibrarySearchCategory.Playlists => [EmbyItemType.Playlist],
                _ when kind == MediaServerKind.Emby => [.. ItemQuery.SweptTypes, EmbyItemType.Person, EmbyItemType.BoxSet, EmbyItemType.Playlist],
                _ when kind == MediaServerKind.Jellyfin => [.. ItemQuery.SweptTypes, EmbyItemType.BoxSet, EmbyItemType.Playlist],
                _ => ItemQuery.SweptTypes
            },
            SortBy = "",
            StartIndex = start,
            Limit = limit
        };
    }
}

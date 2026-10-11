namespace Momoka.Emby;

public enum LibrarySearchCategory
{
    TopResults,
    Series,
    Movies,
    Episodes
}

/// <summary>搜索的分类独立于媒体库浏览的排序和筛选，保留服务器的相关性顺序。</summary>
public static class LibrarySearch
{
    public static ItemQuery? Query(string? term, LibrarySearchCategory category, int start, int limit)
    {
        if (string.IsNullOrWhiteSpace(term)) return null;
        return new ItemQuery
        {
            SearchTerm = term.Trim(),
            Recursive = true,
            IncludeItemTypes = category switch
            {
                LibrarySearchCategory.Series => [EmbyItemType.Series],
                LibrarySearchCategory.Movies => [EmbyItemType.Movie],
                LibrarySearchCategory.Episodes => [EmbyItemType.Episode],
                _ => ItemQuery.SweptTypes
            },
            SortBy = "",
            StartIndex = start,
            Limit = limit
        };
    }
}

namespace Momoka.Emby;

/// <summary>
/// 主页库排右端那个读数（「43 项」）该向服务器问什么。读数说的是这个库一共有多少内容，不是货架装了几张卡 ——
/// 货架上限 24，拿它当读数会把大库写成「24 项」（2026-11-01 用户报「主页的电影和电视剧都是 24 项，明显是不对的」）。
/// <para>
/// 口径与点进库后第一页的「共 N 项」一致：Emby 按库的类型递归数电影／剧集；Jellyfin 的 list 页
/// （没有 CollectionType 的混合库，以及它下面的文件夹）网页端只列直接子项，这里也数直接子项。
/// 飞牛走 <c>mediadb/sum</c>（<see cref="EmbyItem.RecursiveItemCount"/>），Plex 尚未核对，两家都不在这里问。
/// </para>
/// <para>
/// 只问一条 <c>Limit=1</c> 的空查询：要的是回包里的 <c>TotalRecordCount</c>，不是行。
/// </para>
/// </summary>
public static class LibraryTotal
{
    /// <summary>这个库的总数该怎么问；问不出（飞牛／Plex／音乐库这类主页不展示的库）时返回 null。</summary>
    public static ItemQuery? Query(EmbyItem library)
    {
        if (string.IsNullOrEmpty(library.Id)) return null;

        var connection = library.Source?.Connection;
        if (connection is null || connection.IsFnNas || connection.IsPlex) return null;

        if (connection.IsJellyfin && JellyfinLibrary.IsListPage(library.CollectionType))
            return new ItemQuery
            {
                ParentId = library.Id,
                Recursive = false,
                IncludeItemTypes = [],
                Fields = "",
                Limit = 1
            };

        // 与库页「节目／影片／电影和节目」第一页同一份类型表（LibraryBrowse.Query），不用 FlattenedTypes：
        // 后者带着「合集」，而电影库的影片那一页本来就不数合集。
        IReadOnlyList<string> types = library.CollectionType switch
        {
            "movies" => [EmbyItemType.Movie, EmbyItemType.Video],
            "tvshows" => [EmbyItemType.Series, EmbyItemType.Video],
            "mixed" => [EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Video],
            _ => []
        };

        return types.Count == 0 ? null : new ItemQuery
        {
            ParentId = library.Id,
            Recursive = true,
            IncludeItemTypes = types,
            Fields = "",
            Limit = 1
        };
    }
}

namespace Momoka.Emby;

public enum LibrarySection
{
    Items, Recommended, Trailers, Genres, Favorites, Studios, Episodes, Folders, Upcoming
}

/// <summary>
/// 索引页是哪一种：类型、播出平台，或者根本不是索引页。
/// <para>
/// 存在的理由是这两页的卡片长得不一样，而这份差别被四处读：卡片宽高（<see cref="LibraryCardMetrics"/>）、
/// 显示字段（<see cref="LibraryDisplayCatalog"/>）、设置面板摆哪几行、以及「这一页画不画图」。写成一个枚举而不是
/// 两个布尔，是因为「是索引页」与「是哪一种索引页」永远同时被问到 —— 两个布尔会有第三种真值（都是真的）。
/// </para>
/// <para>
/// **只有没下钻的那一层算索引页。** 点进「类型 → 动画」之后 <see cref="LibrarySection"/> 还是
/// <see cref="LibrarySection.Genres"/>，但那一页装的是剧集，卡片就是普通海报 —— 判据必须同时看
/// <c>BrowseGroup</c>，否则下钻页会被当成索引页之一（2026-10-18 那一轮把它当成了索引页，字段只剩「标题」）。
/// </para>
/// </summary>
public enum LibraryIndexKind
{
    None,

    /// <summary>「类型」那一页：方形卡片，图是服务器按类型拼出来的四格封面。</summary>
    Genre,

    /// <summary>「播出平台」那一页：16:9 宽卡，图是台标（服务器存的是 Thumb）。</summary>
    Studio
}

public sealed record LibrarySectionEntry(LibrarySection Section, string Label);

/// <summary>Library identity stays in every section query, including genre/studio drill-downs.</summary>
public static class LibraryBrowse
{
    /// <summary>
    /// 这个库的分类栏，按网页端的顺序。**三套名单互不通用**：电影库没有「合集」，混合内容库没有推荐、
    /// 预告与收藏，电视剧库也没有「合集」。名单照用户给的网页截图定，不因为别的库有这一项就补进来 ——
    /// 补一项就是给这一页装一个网页端没有的分区。
    /// </summary>
    public static IReadOnlyList<LibrarySectionEntry> Sections(string? collectionType, MediaServerKind serverKind)
    {
        List<LibrarySectionEntry> entries = collectionType switch
        {
            "movies" =>
            [
                new(LibrarySection.Items, "影片"), new(LibrarySection.Recommended, "推荐"),
                new(LibrarySection.Trailers, "预告片"), new(LibrarySection.Genres, "类型"),
                new(LibrarySection.Favorites, "收藏"), new(LibrarySection.Folders, "文件夹")
            ],
            "tvshows" =>
            [
                new(LibrarySection.Items, "节目"), new(LibrarySection.Recommended, "推荐"),
                new(LibrarySection.Upcoming, "预告"), new(LibrarySection.Favorites, "收藏"),
                new(LibrarySection.Genres, "类型"), new(LibrarySection.Studios, "播出平台"),
                new(LibrarySection.Episodes, "集"), new(LibrarySection.Folders, "文件夹")
            ],
            "mixed" =>
            [
                new(LibrarySection.Items, "电影和节目"), new(LibrarySection.Genres, "类型"),
                new(LibrarySection.Folders, "文件夹")
            ],
            _ => []
        };
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

    /// <summary>
    /// 这一页是哪一种索引页；<see cref="LibraryIndexKind.None"/> 表示不是（含下钻后的「类型 → 动画」）。
    /// 判据与 <see cref="IsIndex"/> 是同一句话，只是把两种分开了 —— 别在这里另写一条，两处会漂。
    /// </summary>
    public static LibraryIndexKind IndexKind(LibrarySection section, string? group) =>
        !IsIndex(section, group) ? LibraryIndexKind.None
        : section == LibrarySection.Genres ? LibraryIndexKind.Genre : LibraryIndexKind.Studio;

    public static string? SortPreset(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Episodes or LibrarySection.Upcoming => EmbySortPreset.Episode,
        // 电影库的「预告片」在网页端用电影那份排序菜单（十九条），不是 Trailer 类型那份。
        LibrarySection.Trailers => EmbySortPreset.ForCollection(collectionType),
        LibrarySection.Folders => null,
        _ => EmbySortPreset.ForCollection(collectionType)
    };

    /// <summary>
    /// 筛选面板按「这一页装的是什么条目」取预设，和排序菜单不是同一件事：电影库的「预告片」用电影的
    /// 排序菜单，但筛选用的是 Trailer 那一份（网页端只剩播放状态、收藏、分辨率、3D、字幕与元数据）；
    /// 混合内容库排序走文件夹那份，筛选走 <see cref="EmbySortPreset.Mixed"/> 那一份。
    /// </summary>
    public static string? FilterPreset(string? collectionType, LibrarySection section) => section switch
    {
        LibrarySection.Trailers => EmbySortPreset.Trailer,
        LibrarySection.Episodes or LibrarySection.Upcoming => EmbySortPreset.Episode,
        LibrarySection.Folders => null,
        LibrarySection.Items or LibrarySection.Genres or LibrarySection.Studios when collectionType == "mixed"
            => EmbySortPreset.Mixed,
        _ => EmbySortPreset.ForCollection(collectionType)
    };

    public static ItemQuery Query(string collectionType, string parentId, LibrarySection section,
        int start, int limit, string sort, bool descending, ItemFilters filters, string? group = null,
        LibraryDisplay? display = null)
    {
        if (section == LibrarySection.Upcoming) return LibraryUpcoming.Query(parentId, start, limit);
        var index = IsIndex(section, group);
        var indexKind = IndexKind(section, group);
        return new ItemQuery
        {
            ParentId = parentId,
            Recursive = section != LibrarySection.Folders,
            // 只问这一面墙要画的字段。导演要 People、概要要 Overview，勾上才要，没勾就不必替每一页
            // 多拖几十 KB；一条都没勾时退回列表的底座（图、年份、类型）。别的页独有的字段（文件夹页的
            // 文件名、集页的集号）在这里就被滤掉，不会跟着跑到这一页的请求里。
            // 索引页的字段表只有「标题」，所以勾过导演、概要也不会在这两页上问 —— 那两页卡片只写一个名字。
            Fields = display is null
                ? EmbyFields.Browse
                : LibraryDisplayCatalog.RequestFields(
                        LibraryDisplayCatalog.Applied(display.Fields, section, display.View, indexKind, collectionType))
                    + (section == LibrarySection.Folders ? ",Status,EndDate" : ""),
            // 「将项目分组到合集中」只有 Items 那条接口认；推荐走 Suggestions，它不声明这个参数，
            // 发过去只会被丢掉，所以那一支明确不谈这件事。
            GroupItemsIntoCollections = display is null || section == LibrarySection.Recommended
                ? null : display.GroupIntoCollections,
            IncludeItemTypes = section switch
            {
                LibrarySection.Episodes => [EmbyItemType.Episode],
                LibrarySection.Trailers => [EmbyItemType.Trailer],
                LibrarySection.Folders => [],
                // 「电影和节目」是同一格里并排的两种条目；电影库与电视剧库各自只要自己那一类。
                _ => collectionType switch
                {
                    "movies" => [EmbyItemType.Movie, EmbyItemType.Video],
                    "mixed" => [EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Video],
                    _ => [EmbyItemType.Series, EmbyItemType.Video]
                }
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
        LibrarySection.Upcoming => "Shows/Upcoming",
        LibrarySection.Genres when group is null => "Genres",
        LibrarySection.Studios when group is null => "Studios",
        _ => $"Users/{userId}/Items"
    };
}

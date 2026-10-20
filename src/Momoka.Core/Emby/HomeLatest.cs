namespace Momoka.Emby;

/// <summary>
/// 主页「最近添加」那几排该向服务器要什么。Emby、飞牛与 Plex 继续走 <c>Items/Latest</c>；
/// Jellyfin 的剧集库不能走 —— 它的 Latest 只收可播放条目（单集、电影），一个只有剧集、还没扫出
/// 单集的库会交回空表，那一排就被主页按「空排不显示」摘掉（2026-10-20 用户报「有些最近添加会被隐藏」）。
/// <para>
/// 剧集库改走通用条目查询：直接列剧集、按最近有新内容入库倒序（<c>DateLastContentAdded</c>，与推荐页
/// 「最新剧集」同一口径）。原来的 <c>Items/Latest</c> 按剧合并时也是这个次序，换查询不改先后。
/// </para>
/// </summary>
public static class HomeLatest
{
    /// <summary>这个库的最近添加要不要改走剧集查询。</summary>
    public static bool UsesSeriesQuery(EmbyItem library) =>
        library.Source?.Connection.IsJellyfin == true
        && string.Equals(library.CollectionType, "tvshows", StringComparison.OrdinalIgnoreCase);

    /// <summary>剧集库的最近添加查询；只用于 <see cref="UsesSeriesQuery"/> 为真的库。</summary>
    public static ItemQuery SeriesQuery(string parentId, int limit) => new()
    {
        ParentId = parentId,
        Recursive = true,
        IncludeItemTypes = [EmbyItemType.Series],
        SortBy = "DateLastContentAdded,SortName",
        Descending = true,
        Limit = limit
    };
}

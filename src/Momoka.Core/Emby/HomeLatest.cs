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

    /// <summary>
    /// 飞牛的主页货架要按库的类型问，不能空手问。飞牛的 <c>item/list</c> 没有 Emby 那个 <c>GroupItems</c>
    /// —— 不带类型时它把剧集、单集、季一起发回来，货架上于是并排摆着「JOJO的奇妙冒险」和
    /// 「暗蓝之月 S02E06」两张卡（2026-10-25 用户令「飞牛影视库不要再显示单集封面」）。
    /// 带上库自己的类型之后，剧集库只回剧集，与库页问的是同一份。
    /// <para>
    /// 其余服务器返回 <see langword="null"/>（不加类型参数，照旧）。
    /// </para>
    /// </summary>
    public static string? LibraryTypes(EmbyItem library)
    {
        if (library.Source?.Connection.IsFnNas != true) return null;

        var types = ItemQuery.FlattenedTypes(library.CollectionType);
        return types.Count == 0 ? null : string.Join(',', types);
    }
}

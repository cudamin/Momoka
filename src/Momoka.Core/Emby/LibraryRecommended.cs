namespace Momoka.Emby;

/// <summary>
/// 推荐页（网页端电视剧库「推荐」那一格）的三排，<see cref="LibraryRecommended"/> 按这个次序排。
/// </summary>
public enum RecommendedShelf
{
    /// <summary>这一本账号看到一半的东西，只算本库里的。</summary>
    ContinueWatching,

    /// <summary>本库里最新有内容的剧集本身 —— 竖版海报、右上角带未看集数。</summary>
    LatestEpisodes,

    /// <summary>按播出日期排的最新单集 —— 横版剧照、卡片底下写「SxxExx - 集名」。</summary>
    RecentlyReleased
}

/// <summary>推荐页上的一排：认哪一条数据、牌子写什么、卡片是横是竖。</summary>
public sealed record RecommendedRow(RecommendedShelf Shelf, string Title, bool Wide);

/// <summary>
/// 网页端电视剧库「推荐」那一页：三排横着走的货架，而不是一面卡片墙。
/// <para>
/// 2026-10-17 用户令「复刻网页端的推荐页面，三个分区 继续观看、最新集、最近发布的剧集」，附网页端截图。
/// 这一轮之前这一格是平铺网格（走 <c>Users/{id}/Suggestions</c>），网页端那三行另起一轮，就是这里。
/// </para>
/// <para>
/// **三排的口径**（对着截图定，不是猜的）：继续观看是「本库里看到一半的」，最新集是「本库里最近有内容的
/// 剧集」，最近发布的剧集是「本库里按播出日期最新的单集」。三排各自带 <c>ParentId</c>，所以它们说的是这一个
/// 库，不是整台服务器 —— 这也是它们不能借主页那几排的原因：主页那几排是整服的。
/// </para>
/// <para>
/// **只给电视剧库**。电影库网页端也是「推荐」两个字，但装的是另一组行（继续观看、最新电影、最近发布），
/// 本轮没有做，那一格照旧是网格 —— 与其把电视剧的三排硬套上去，不如让它维持现状。
/// </para>
/// <para>
/// 判据与文案放在 Core，是因为它们是这一页的「说什么」：查询怎么发、卡片底下写什么、哪几种条目不参加，
/// 都要能在没有窗口的情况下验。
/// </para>
/// </summary>
public static class LibraryRecommended
{
    /// <summary>每一排问服务器要多少条。网页端那三排也是先给一屏、用箭头翻页。</summary>
    public const int Size = 24;

    /// <summary>
    /// 这个库该有哪几排。电视剧库三排，别的库一排都没有（<c>[]</c> ＝ 这一页不是货架页，走原来的网格）。
    /// </summary>
    public static IReadOnlyList<RecommendedRow> Rows(string? collectionType) =>
        collectionType == "tvshows"
            ?
            [
                new(RecommendedShelf.ContinueWatching, "继续观看", Wide: true),
                new(RecommendedShelf.LatestEpisodes, "最新集", Wide: false),
                new(RecommendedShelf.RecentlyReleased, "最近发布的剧集", Wide: true)
            ]
            : [];

    /// <summary>
    /// 这一排问服务器要什么。继续观看不走这里 —— 它整份交给 Resume 那条接口（见
    /// <see cref="EmbyClient.QueryResumeAsync"/>），通用查询拼不出「有没有播放进度」这个条件。
    /// </summary>
    /// <remarks>
    /// 排序用的两个键都是 Emby 自己的：<c>DateLastContentAdded</c> 是「这一部剧最近有新内容入库」，
    /// <c>PremiereDate</c> 是播出日期 —— 前者回答「库里最新」，后者回答「电视上最新」，网页端那两排
    /// 正是这两件事。两处都带上 <c>SortName</c> 做次级键，否则同一天入库的一批会按服务器内部次序乱排。
    /// </remarks>
    public static ItemQuery Query(string parentId, RecommendedShelf shelf, int limit = Size) => new()
    {
        ParentId = parentId,
        Recursive = true,
        IncludeItemTypes = shelf switch
        {
            RecommendedShelf.LatestEpisodes => [EmbyItemType.Series],
            RecommendedShelf.RecentlyReleased => [EmbyItemType.Episode],
            _ => []
        },
        SortBy = shelf switch
        {
            RecommendedShelf.LatestEpisodes => "DateLastContentAdded,SortName",
            RecommendedShelf.RecentlyReleased => "PremiereDate,SortName",
            _ => ""
        },
        Descending = true,
        Limit = limit
    };

    /// <summary>
    /// 卡片底下那一行字，照网页端：单集是「S01E07 - 集名」，剧集是「2006 - 2007」或「2026 - 现在」。
    /// <para>
    /// 交回 null 的条目（电影、文件夹、没有年份的剧集）由卡片自己的副标题接手 —— 这一页目前只会出现单集
    /// 和剧集，但「认不出来的就用原来那一句」比留一行空白强。
    /// </para>
    /// <para>
    /// 和 <see cref="EmbyItem.CardSubtitle"/> 的差别是**故意**的，两处说的不是一件事：库墙上的单集卡写
    /// 「S01E07 · 剧名」（在一面墙里找剧），推荐页这两排写「S01E07 - 集名」（这一集演的是什么）。分隔符也
    /// 照网页端用「 - 」，不用卡片那套「  ·  」。
    /// </para>
    /// </summary>
    public static string? Subtitle(EmbyItem item)
    {
        if (item.Type == EmbyItemType.Episode)
        {
            var code = item.EpisodeCode;
            var name = item.Name?.Trim() ?? "";
            if (code.Length == 0) return name.Length > 0 ? name : null;
            return name.Length > 0 ? $"{code} - {name}" : code;
        }

        if (item.Type != EmbyItemType.Series) return null;

        if (item.ProductionYear is not { } start || start <= 0) return null;
        if (string.Equals(item.Status, "Continuing", StringComparison.OrdinalIgnoreCase)) return $"{start} - 现在";

        // 结局那一年没有单独给出来时（有的刮削源只有 ProductionYear）只写开头那一年，不编一个结束年份。
        return item.EndDate?.ToLocalTime().Year is { } until && until > start ? $"{start} - {until}" : $"{start}";
    }
}

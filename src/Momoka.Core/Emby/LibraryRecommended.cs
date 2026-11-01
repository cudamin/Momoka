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
    RecentlyReleased,

    /// <summary>Jellyfin 电影建议的第一排：本库最近入库的电影。</summary>
    LatestMovies,

    /// <summary>Jellyfin 剧集建议的第三排：每部剧接下来该看的一集。</summary>
    NextUp,

    /// <summary>服务器算出的相似影片：Jellyfin 的电影建议把它压成一排，Emby 每个分类各一排。</summary>
    MovieRecommendations
}

/// <summary>推荐页上的一排：认哪一条数据、牌子写什么、卡片是横是竖。</summary>
public sealed record RecommendedRow(RecommendedShelf Shelf, string Title, bool Wide);

/// <summary>
/// 服务器推荐分类成出来的一排：牌子与卡片形状是 <see cref="Row"/>，<see cref="Items"/> 是那一排的条目。
/// </summary>
public sealed record LibraryRecommendationRow(RecommendedRow Row, IReadOnlyList<EmbyItem> Items);

/// <summary>
/// 网页端「推荐」那一页：几排横着走的货架，而不是一面卡片墙。
/// <para>
/// 2026-10-17 用户令「复刻网页端的推荐页面，三个分区 继续观看、最新集、最近发布的剧集」，附网页端截图。
/// 这一轮之前这一格是平铺网格（走 <c>Users/{id}/Suggestions</c>），网页端那三行另起一轮，就是这里。
/// </para>
/// <para>
/// **电视剧库的三排**（对着截图定，不是猜的）：继续观看是「本库里看到一半的」，最新集是「本库里最近有内容的
/// 剧集」，最近发布的剧集是「本库里按播出日期最新的单集」。三排各自带 <c>ParentId</c>，所以它们说的是这一个
/// 库，不是整台服务器 —— 这也是它们不能借主页那几排的原因：主页那几排是整服的。Jellyfin 的电视剧第三排换成
/// 「接下来看」。
/// </para>
/// <para>
/// **电影库照网页端**（<c>videos/moviesuggestions.js</c> 的 <c>fetchSections</c>）：Emby 是「继续观看 + 最新影片」
/// 两排固定货架，服务器还会按推荐理由（看过／收藏过的片子、同一执导、同一出演）再给几组，每组自带基准片名，
/// 由 <see cref="RecommendationRows"/> 接在后面各成一排。Jellyfin 沿用已经做好的那三排（继续观看、最新电影、
/// 为你推荐），它把服务器的分组压成一排。
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
    /// 电影推荐页每排问服务器要多少条：网页端 <c>fetchSections</c> 的纵向布局是
    /// <c>ItemLimit=12</c>（横向布局才是 6，那是电视遥控器上的一屏）。
    /// </summary>
    public const int EmbyMovieRecommendationSize = 12;

    /// <summary>
    /// 这个库该有哪几排。Emby 电影库是「继续观看 + 最新影片」两排，后面接着服务器推荐的那几组
    /// （见 <see cref="RecommendationRows"/>）；Emby 电视剧库是截图里那三排；Jellyfin 的电影库也有建议页
    /// （继续观看、最新电影、服务器推荐），电视剧库的第三排则是「接下来看」而不是按播出日期排的单集。
    /// </summary>
    public static IReadOnlyList<RecommendedRow> Rows(string? collectionType, MediaServerKind serverKind = MediaServerKind.Emby) =>
        collectionType switch
        {
            "tvshows" when serverKind == MediaServerKind.Jellyfin =>
            [
                new(RecommendedShelf.ContinueWatching, "继续观看", Wide: true),
                new(RecommendedShelf.LatestEpisodes, "最新剧集", Wide: true),
                new(RecommendedShelf.NextUp, "接下来看", Wide: true)
            ],
            "tvshows" =>
            [
                new(RecommendedShelf.ContinueWatching, "继续观看", Wide: true),
                new(RecommendedShelf.LatestEpisodes, "最新集", Wide: false),
                new(RecommendedShelf.RecentlyReleased, "最近发布的剧集", Wide: true)
            ],
            "movies" when serverKind == MediaServerKind.Jellyfin =>
            [
                new(RecommendedShelf.ContinueWatching, "继续观看", Wide: true),
                new(RecommendedShelf.LatestMovies, "最新电影", Wide: false),
                new(RecommendedShelf.MovieRecommendations, "为你推荐", Wide: false)
            ],
            "movies" when serverKind == MediaServerKind.Emby =>
            [
                new(RecommendedShelf.ContinueWatching, "继续观看", Wide: true),
                new(RecommendedShelf.LatestMovies, "最新影片", Wide: false)
            ],
            _ => []
        };

    /// <summary>
    /// 服务器那几组推荐各成一排，照网页端 <c>fetchSections</c>：标题由
    /// <see cref="RecommendationTitle"/> 合成，卡片是竖版海报。没有条目、认不出类型、或者音乐条目之外一条
    /// 不剩的分类不占屏幕。同名标题（同一部基准片被两种理由推荐）并成一排，条目按
    /// <see cref="EmbyItem.IdentityKey"/> 去重、保留服务器给的先后次序。
    /// </summary>
    public static IReadOnlyList<LibraryRecommendationRow> RecommendationRows(IEnumerable<MovieRecommendation> recommendations)
    {
        var rows = new List<LibraryRecommendationRow>();
        var known = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var recommendation in recommendations)
        {
            if (RecommendationTitle(recommendation) is not { } title) continue;

            var items = recommendation.Items
                .Where(item => !EmbyItemType.IsMusic(item.Type))
                .DistinctBy(item => item.IdentityKey)
                .ToArray();
            if (items.Length == 0) continue;

            if (known.TryGetValue(title, out var index))
            {
                rows[index] = rows[index] with
                {
                    Items = [.. rows[index].Items.Concat(items).DistinctBy(item => item.IdentityKey)]
                };
                continue;
            }

            known[title] = rows.Count;
            rows.Add(new LibraryRecommendationRow(
                new RecommendedRow(RecommendedShelf.MovieRecommendations, title, Wide: false), items));
        }

        return rows;
    }

    /// <summary>
    /// 一排推荐的牌子，照网页端那四条分支：<c>SimilarToRecentlyPlayed</c>「类似看过：X」、
    /// <c>SimilarToLikedItem</c>「类似收藏：X」、导演那两种「同一执导：X」、演员那两种「同一出演：X」。
    /// <para>
    /// 译文取自真实 Emby 服务器网页语言包（<c>BecauseYouWatchedValue</c> 等四条）。基准片名缺失时退回
    /// Jellyfin 那一排用的「为你推荐」——「同一执导：」后面空着比换一句更糟。认不出的类型交回 null，那一排
    /// 不生成：网页端那一下会摆出一排没有牌子的卡。
    /// </para>
    /// </summary>
    public static string? RecommendationTitle(MovieRecommendation recommendation)
    {
        var prefix = recommendation.RecommendationType switch
        {
            "SimilarToRecentlyPlayed" => "类似看过：",
            "SimilarToLikedItem" => "类似收藏：",
            "HasDirectorFromRecentlyPlayed" or "HasLikedDirector" => "同一执导：",
            "HasActorFromRecentlyPlayed" or "HasLikedActor" => "同一出演：",
            _ => null
        };
        if (prefix is null) return null;

        var baseline = recommendation.BaselineItemName?.Trim() ?? "";
        return baseline.Length > 0 ? prefix + baseline : "为你推荐";
    }

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
            RecommendedShelf.LatestMovies => [EmbyItemType.Movie],
            _ => []
        },
        SortBy = shelf switch
        {
            RecommendedShelf.LatestEpisodes => "DateLastContentAdded,SortName",
            RecommendedShelf.RecentlyReleased => "PremiereDate,SortName",
            RecommendedShelf.LatestMovies => "DateCreated,SortName",
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

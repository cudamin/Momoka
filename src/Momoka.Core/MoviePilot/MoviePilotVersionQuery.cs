using System.Globalization;
using Momoka.Emby;

namespace Momoka.MoviePilot;

/// <summary>
/// 从一个 Emby 条目算出「去 MoviePilot 找它的其他版本」时该发的搜索词 —— 一个输入一个输出，放 Core 配测试。
/// <para>
/// 为什么住 Core：这几行是「电影发片名、季发『剧名 S01』」这种写错了屏上也看着正常的判断（少个空格、季号取错成
/// 集号、季页拿了『第 1 季』当剧名），和 <see cref="Emby.ItemMenu"/> 同一类，靠单测一条条钉住。用户定的对应：
/// 剧→<c>剧名</c>；季→<c>剧名 S0x</c>；电影→<c>电影名</c>；集 2026-09-24 定为 <c>剧名 S0x E0x</c>，2026-10-11
/// 改为发「剧名 年份 S0x」、结果由 <see cref="MoviePilotEpisodePattern"/> 按集号筛（见
/// <see cref="Keyword(EmbyItem)"/>）—— 订阅与遗漏集那两支没有结果页可筛，仍是 <c>剧名 S0x E0x</c>。
/// </para>
/// </summary>
public static class MoviePilotVersionQuery
{
    /// <summary>
    /// 能不能「搜版本」：只有电影、剧、季、单集这四种在 MoviePilot 上对得上一部片。合集是一篮子横跨多片的东西、
    /// 媒体库是货架、演职人员是个人名 —— 都没有「对应的一部片」可搜，所以「更多」菜单也不给它们这一条。
    /// </summary>
    public static bool Supports(EmbyItem item) =>
        item.Type is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    /// <summary>
    /// 集的版本搜索要把哪一集筛回来（配套 <see cref="Keyword(EmbyItem)"/> 的集写法）：集条目用自己的集号，
    /// 电影／剧／季没有单集可筛，给空。
    /// </summary>
    public static int? TargetEpisode(EmbyItem item) =>
        item.Type == EmbyItemType.Episode ? item.IndexNumber : null;

    /// <summary>
    /// 发给 MoviePilot 关键词搜索（<c>search/title</c>）的那一串。季在剧名后补 <c>S0x</c>，让站点搜索把范围收到
    /// 那一季；剧和电影就发名字（整部剧 / 整片）。集发「剧名 年份 S0x」、不发集号：把集号写进词里站点常搜空，
    /// 改搜整个季，再由 <see cref="MoviePilotEpisodePattern"/> 把目标集从结果里筛回来（版本窗口搜完自动挂筛选）。
    /// </summary>
    public static string Keyword(EmbyItem item) => item.Type switch
    {
        // 季自己的 IndexNumber 就是季号；剧名取父级的 SeriesName。
        EmbyItemType.Season => Join(SeriesTitle(item), Season(item.IndexNumber)),

        // 集的季号在 ParentIndexNumber。年份取条目自己的 ProductionYear（集页面上显示的那个）；缺失就退回
        // 「剧名 S0x」，同季号缺失退回只有剧名是一个规矩。
        EmbyItemType.Episode => Join(SeriesTitle(item), Year(item.ProductionYear), Season(item.ParentIndexNumber)),

        // 电影、剧：整片 / 整部剧，只发名字。
        _ => item.Name.Trim()
    };

    /// <summary>
    /// 订阅里某一集的搜索词：剧名 + <c>S0x</c> + <c>E0x</c>。订阅详情里「搜索这一集的资源」和遗漏集表上的
    /// 「搜索这一集」用它拼词 —— 那边只有剧名、季号和集号（没有 Emby 条目可算年份），也没有结果页可挂筛选，
    /// 所以集号仍留在词里，不跟 <see cref="Keyword(EmbyItem)"/> 的集同一种写法。
    /// </summary>
    public static string Keyword(string seriesTitle, int? season, int? episode) =>
        Join(seriesTitle.Trim(), Season(season), Episode(episode));

    /// <summary>遗漏集可能没有 SeriesName，剧名从打开清单的剧或季取得；虚拟集不必再按 ID 读详情。</summary>
    public static string Keyword(EmbyItem episode, EmbyItem owner) => Keyword(
        new[] { episode.SeriesName, owner.SeriesName, owner.Name }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
        episode.ParentIndexNumber ?? (owner.Type == EmbyItemType.Season ? owner.IndexNumber : null), episode.IndexNumber);

    /// <summary>季/集拿剧名当词头；服务器偶尔没回剧名时退到条目自己的名字，总比空词强。</summary>
    private static string SeriesTitle(EmbyItem item) =>
        !string.IsNullOrWhiteSpace(item.SeriesName) ? item.SeriesName!.Trim() : item.Name.Trim();

    /// <summary>「S01」。季号缺失（未编号的特别篇之类）时给空串，让关键词退回只有剧名，而不是发个「S」出去。</summary>
    private static string Season(int? number) => number is { } n and >= 0 ? $"S{n:00}" : "";

    /// <summary>「1995」。年份缺失给空串，让集的关键词退回「剧名 S0x」。</summary>
    private static string Year(int? year) => year is { } y and > 0 ? y.ToString(CultureInfo.InvariantCulture) : "";

    /// <summary>「E01」。同上，缺失给空串。</summary>
    private static string Episode(int? number) => number is { } n and >= 0 ? $"E{n:00}" : "";

    /// <summary>把非空的几段用单空格拼起来 —— 少一段（比如季号没给）就少一段，不留下多余的空格。</summary>
    private static string Join(params string[] parts) => string.Join(' ', parts.Where(part => part.Length > 0));
}

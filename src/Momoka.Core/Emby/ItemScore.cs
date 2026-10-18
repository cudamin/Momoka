namespace Momoka.Emby;

/// <summary>一格评分：屏上那个数，和它旁边那句「这是谁给的分」。</summary>
public sealed record ScoreBadge(string Text, string Label)
{
    /// <summary>服务器上一个分都没有。那一格整块收起来。</summary>
    public static ScoreBadge None { get; } = new("", "");

    public bool Any => Text.Length > 0;
}

/// <summary>
/// 「显示的评分」显示哪个数、旁边写谁的名字。<b>服务端给什么评分就用什么</b>（用户令 2026-10-16，设置里那一档
/// 「评分来源」整个删掉了）。
/// <para>
/// <b>先说这件事在服务器那一头有什么。</b>Emby 的条目上只有两个数字评分槽：<c>CommunityRating</c>（大众分，
/// 0–10）和 <c>CriticRating</c>（影评指数，0–100，装了 OMDb 那类插件时就是烂番茄的新鲜度）。豆瓣、TMDB、IMDb
/// 三家的大众分都写进 <c>CommunityRating</c> 这同一个数，哪家赢由服务器上装了哪些刮削插件决定，客户端换不出来 ——
/// 所以这里不去猜那个数是谁给的：有大众分就画大众分、署「公众评分」，没有就退到影评指数、署「烂番茄」，两个都没有
/// 那一格整块收起来。标签说的是<b>实际取到的是哪一个槽</b>，不是这个分来自哪家网站。
/// </para>
/// <para>
/// 判断放这儿而不是留在视图模型里，是因为它有唯一正确答案而且错得很安静：一个 0 分被当成「有分」画成「0.0」、
/// 烂番茄的 92 被当成十分制画成「92.0」、机器的小数点是逗号于是 8.4 变成 84 —— 三种都是屏上一个看着挺正常的数。
/// </para>
/// </summary>
public static class ItemScore
{
    /// <summary>
    /// 这个条目该显示什么。
    /// <para>
    /// 两级回落，一条到底：先试大众分，空了试影评指数，再空了才是空。<b>标签永远说实际取到的是哪一个槽</b>。
    /// </para>
    /// <para>
    /// 回落必须完整，缺一格就有条目莫名空掉：「这个条目没有 CommunityRating 但有 CriticRating=92」的时候服务器
    /// 明明有一个分，而只看一档的规矩会把那一格整块收起来。
    /// </para>
    /// </summary>
    public static ScoreBadge Resolve(EmbyItem item)
    {
        var community = Community(item);
        if (community.Any) return community;

        var critic = Critic(item);
        return critic.Any ? critic : ScoreBadge.None;
    }

    /// <summary>
    /// 大众分那个数怎么写：<c>8.4</c> / <c>8</c>，不是 <c>8.40</c>，而且不跟机器的文化走 —— 小数点是逗号的机器上
    /// 「8.4」会被格式化成「8,4」。0 和缺失都算没有分。
    /// </summary>
    private static ScoreBadge Community(EmbyItem item) =>
        item.CommunityRating is { } score and > 0
            ? new ScoreBadge(score.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), "公众评分")
            : ScoreBadge.None;

    /// <summary>
    /// 影评指数写成百分数：<c>92%</c>。它是 0–100 而大众分是 0–10，两者格式不一样，混了就是屏上一个「92.0 分」。
    /// <para>
    /// 取整用「远离零」而不是默认的「取偶」：92.5 分算 93 符合人的直觉，而 <c>Math.Round</c> 的默认答案是 92。
    /// </para>
    /// </summary>
    private static ScoreBadge Critic(EmbyItem item) =>
        item.CriticRating is { } score and > 0
            ? new ScoreBadge(
                ((int)Math.Round(score, MidpointRounding.AwayFromZero))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
                "烂番茄")
            : ScoreBadge.None;
}

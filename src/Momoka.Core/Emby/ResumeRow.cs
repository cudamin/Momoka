namespace Momoka.Emby;

/// <summary>
/// 「继续观看」里那一格在标记已看之后怎么收场：单集换成这一部剧的下一集，其余（电影、以及没有下一集的
/// 单集）整个撤掉。
/// <para>
/// 这件事服务器那条 Resume 接口办不到 —— 它的口径是「有没有播放进度」（<see cref="EmbyClient.QueryResumeAsync"/>），
/// 标记已看之后那一集从清单里消失是对的，可下一集没有播放进度、它不会给；而继续观看那一排要的正是
/// 「接着看什么」。所以「换还是撤」这个判据在这里，去取下一集那一步在调用方（服务器那条 <c>Shows/NextUp</c>，
/// 缩到一部剧上）。
/// </para>
/// <para>
/// 纯函数，单测盯着：续播这一类的语义不能以「只是一个判断」为由留在页面里（见 CLAUDE.md 首要目标）。
/// </para>
/// </summary>
public static class ResumeRow
{
    /// <summary>
    /// 标记已看之后还要不要问服务器「这一部剧的下一集是谁」。只有单集谈得上下一集；电影、以及没带剧 id 的
    /// 条目问都不用问 —— 问回来的东西也不该进继续观看。
    /// </summary>
    public static bool WantsNext(EmbyItem watched) =>
        watched.Type == EmbyItemType.Episode && watched.SeriesId is { Length: > 0 };

    /// <summary>
    /// 那一格留给谁；<see langword="null"/> 就是撤掉这一格。
    /// <para>
    /// <paramref name="next"/> 传空是「这一部剧没有下一集」（最后一集，或者压根没问出来）。服务器把刚标记的
    /// 那一集又发回来时也当没有 —— 同一集在那一排上出现两个分身，比少一格难看得多。
    /// </para>
    /// </summary>
    public static EmbyItem? SlotAfterWatched(EmbyItem watched, EmbyItem? next) =>
        WantsNext(watched) && next is not null && !string.Equals(next.Id, watched.Id, StringComparison.Ordinal)
            ? next
            : null;
}

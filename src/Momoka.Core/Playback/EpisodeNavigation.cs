using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>The episode to play after a step, together with the list its picker should show.</summary>
public sealed record EpisodeDestination(EmbyItem Episode, IReadOnlyList<EmbyItem> Siblings);

/// <summary>
/// Resolves 上一集 / 下一集 over the list the player is actually holding. The server owns the ordering;
/// this rule only walks that order and hands the <b>same</b> list back, so the picker never changes shape
/// because someone stepped off the end of a season.
/// </summary>
public static class EpisodeNavigation
{
    /// <summary>播放器的列表定位必须包含服务器账号；不同来源允许出现相同条目 ID。</summary>
    public static int IndexOf(IReadOnlyList<EmbyItem> episodes, string currentIdentityKey)
    {
        for (var index = 0; index < episodes.Count; index++)
            if (string.Equals(episodes[index].IdentityKey, currentIdentityKey, StringComparison.Ordinal)) return index;
        return -1;
    }

    public static bool HasMultipleSources(IReadOnlyList<EmbyItem> episodes) => episodes.Count > 1
        && episodes.Skip(1).Any(item => item.Source?.Key != episodes[0].Source?.Key);

    /// <summary>
    /// 上一集 / 下一集：在手上那份列表里按位置走一步，到两端返回 null（不回绕）。
    /// <para>
    /// **整份列表原样交回**（<c>Siblings</c> 就是 <paramref name="episodes"/>）。这既是聚合播放队列
    /// （主页「播放全部」那种跨媒体库队列，按用户给定顺序前进）的规矩，也是 2026-11-03 起的剧集规矩：
    /// 播放器的「剧集列表」从那天起装着<b>全剧</b>（按播出时间排，见 <see cref="ItemDetail.AirOrder"/>），
    /// 于是跨季一步不再把列表缩回某一季 —— 缩回去的话，从季页进来的人看得到全季，走一步之后又只剩一季。
    /// </para>
    /// <para>
    /// 2026-09-18 那条「自动连播只在本季内走、季末直接退出」同日作废：用户 2026-11-03 令「跟随全季列表，
    /// 跨季连播」。自动连播与上一集/下一集从此走同一条 <see cref="Step"/>，区别只在调用方是否放行
    /// （<c>NextEpisodeToAutoPlay</c> 还额外要求「真的放完了」与开关打开）。
    /// </para>
    /// <para>
    /// 那一天一起退役的还有「跨季落地时按落点那一季重新问服务器」那一段：它存在的理由是季列表里并着
    /// air-order 特典（《伪恋》S01 = 23 集），本地按 <c>SeasonId</c> 筛会少几条。列表改成全剧之后，
    /// 特典本就在它播出位置的那一行上，没有「重问一次才对齐」这回事了。
    /// </para>
    /// </summary>
    public static EpisodeDestination? Step(
        IReadOnlyList<EmbyItem> episodes,
        string currentIdentityKey,
        int offset)
    {
        if (offset is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(offset));
        var index = episodes is UserPlaybackQueue queue ? queue.IndexOfCurrent(currentIdentityKey) : IndexOf(episodes, currentIdentityKey);
        if (index < 0 || index + offset < 0 || index + offset >= episodes.Count) return null;
        return new(episodes[index + offset], episodes is UserPlaybackQueue currentQueue
            ? new UserPlaybackQueue(currentQueue) { CurrentIndex = index + offset } : episodes);
    }

    /// <summary>确认删除当前单集后，按原有顺序先找同来源同剧的上一集；首集才改找下一集。</summary>
    public static EmbyItem? AfterRemoval(EmbyItem removed, IReadOnlyList<EmbyItem> episodes)
    {
        if (removed.Type != EmbyItemType.Episode || removed.Id.Length == 0) return null;
        var index = IndexOf(episodes, removed.IdentityKey);
        if (index < 0) return null;
        for (var previous = index - 1; previous >= 0; previous--)
            if (SameWork(episodes[previous])) return episodes[previous];
        for (var next = index + 1; next < episodes.Count; next++)
            if (SameWork(episodes[next])) return episodes[next];
        return null;

        bool SameWork(EmbyItem candidate)
        {
            if (candidate.Type != EmbyItemType.Episode || candidate.IdentityKey == removed.IdentityKey
                || candidate.Source?.Key != removed.Source?.Key) return false;
            if (removed.SeriesId is { Length: > 0 } series && candidate.SeriesId is { Length: > 0 } other)
                return series == other;
            var season = removed.SeasonId is { Length: > 0 } id ? id : removed.ParentId;
            var candidateSeason = candidate.SeasonId is { Length: > 0 } value ? value : candidate.ParentId;
            return !string.IsNullOrEmpty(season) && season == candidateSeason;
        }
    }
}

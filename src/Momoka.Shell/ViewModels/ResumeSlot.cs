using System.Collections.ObjectModel;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 「继续观看」那一格标记已看之后的收尾：问出这一部剧的下一集，再照 Core 的判据
/// （<see cref="ResumeRow.SlotAfterWatched"/>）把那一格换掉或者撤掉。
/// <para>
/// 主页那一排和它点进去的那一页共用这一份 —— 两处同源（2026-09-22「一排 1 项、进去 9 项」那次的教训：这两个
/// 面子的口径必须一样）。判据在 Core，这一头只有「去问谁」和「怎么改那个集合」。
/// </para>
/// </summary>
internal static class ResumeSlot
{
    /// <summary>
    /// 这一部剧的下一集 —— 服务器那条 <c>Shows/NextUp</c> 缩到一部剧上（主页「接下来看」那一排走的是同一条
    /// 接口，只是不缩），问不出来就是没有下一集。
    /// <para>
    /// 读不到不当失败报：那一格本来就按「没有下一集」办（撤掉），弹一条红条反而是在说一件用户没让做的事失败了。
    /// <paramref name="category"/> 只是记账用的日志分类，两处各自的不一样。
    /// </para>
    /// </summary>
    internal static async Task<EmbyItem?> NextEpisodeAsync(EmbySessionScope scope, EmbyItem watched, string category)
    {
        if (watched.SeriesId is not { Length: > 0 } series || !scope.IsAvailable) return null;

        try
        {
            var found = await scope
                .ExecuteAsync((client, token) => client.GetNextUpAsync(1, token, series), CancellationToken.None)
                .ConfigureAwait(true);
            return found.FirstOrDefault();
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error)
        {
            Log.Warn(category, $"读「{watched.Name}」的下一集失败，那一格按没有下一集处理", error);
            return null;
        }
    }

    /// <summary>
    /// 把 <paramref name="watched"/> 那一格换掉或者撤掉，位置不动。
    /// <para>
    /// 卡片是原地撤、原地插，不是整排重填：那一排其余的卡照旧攥着自己解出来的画面（<c>Fill</c> 会把它们全放掉）。
    /// 服务器把同一集发回来（<paramref name="next"/> 就是刚标记的那一集）时那一格撤掉，不留两个分身。
    /// </para>
    /// </summary>
    /// <param name="build">造新卡。两处的卡片参数各在自己那一头（货架的尺寸／角标、网格当前的视图形状）。</param>
    /// <returns>那一格现在装着谁；<see langword="null"/> 是撤掉了。</returns>
    internal static EmbyItem? Apply(
        ObservableCollection<CardItem> cards,
        CardItem watched,
        EmbyItem? next,
        Func<EmbyItem, CardItem> build)
    {
        var index = cards.IndexOf(watched);
        if (index < 0) return null;

        var slot = ResumeRow.SlotAfterWatched(watched.Item, next);

        watched.AbandonPoster();
        cards.RemoveAt(index);

        if (slot is null) return null;

        // 同一集已经在那一排里（服务器两侧口径不一致时可能出现）：不再插一张，撤掉那一格就够。
        if (cards.Any(existing => string.Equals(existing.Item.IdentityKey, slot.IdentityKey, StringComparison.Ordinal))) return null;

        cards.Insert(Math.Min(index, cards.Count), build(slot));
        return slot;
    }
}

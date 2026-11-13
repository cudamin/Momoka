using Momoka.Emby;

namespace Momoka.MoviePilot;

/// <summary>
/// 搜索页「媒体库」来源同时展示的订阅命中：按作品名在本机的 MoviePilot 订阅清单里找。
/// <para>
/// 只比标题，不重新请求 MoviePilot 的媒体搜索 —— 用户已经订阅的作品在订阅清单里就有，再向
/// <c>media/search</c> 问一遍既是另一件事（那是「库里没有、可以订」），也会把每次键入变成一趟外网读取。
/// </para>
/// </summary>
public static class MoviePilotSubscriptionSearch
{
    /// <summary>命中上限：搜索页最多摆一条货架，再多的订阅也不是这一次搜索要回答的问题。</summary>
    public const int Limit = 24;

    public static IReadOnlyList<MoviePilotSubscription> Match(IReadOnlyList<MoviePilotSubscription> subscriptions,
        string term)
    {
        var keyword = term.Trim();
        if (keyword.Length == 0) return [];
        var result = new List<MoviePilotSubscription>(Math.Min(subscriptions.Count, Limit));
        foreach (var subscription in subscriptions)
        {
            if (!subscription.Media.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && !subscription.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(subscription);
            if (result.Count == Limit) break;
        }
        return result;
    }
}

/// <summary>
/// 媒体库卡片上的「MoviePilot 已订阅」标记。订阅与库条目按 <see cref="MoviePilotLibraryPlayback.Provider"/>
/// 认出的来源 + 编号 + 类型对齐，和入库播放的匹配是同一套身份规则。
/// <para>
/// 索引先建好再逐卡问：一页几百张卡、几十条订阅，逐对调用 <see cref="MoviePilotLibraryPlayback.MatchesMedia"/>
/// 也能算对，但把每张卡的提供者逐条比一遍更省；真正的判定（来源映射、编号格式、季/集类型）仍只有那一处。
/// </para>
/// </summary>
public sealed class MoviePilotSubscriptionIndex
{
    public static MoviePilotSubscriptionIndex Empty { get; } = new([]);

    private readonly HashSet<(bool Series, string Provider, string Id)> _entries;

    private MoviePilotSubscriptionIndex(HashSet<(bool Series, string Provider, string Id)> entries) => _entries = entries;

    public static MoviePilotSubscriptionIndex Build(IEnumerable<MoviePilotSubscription> subscriptions)
    {
        var entries = new HashSet<(bool Series, string Provider, string Id)>();
        foreach (var subscription in subscriptions)
        {
            if (MoviePilotLibraryPlayback.Provider(subscription.Media) is not { } provider
                || subscription.Media.MediaId is not { Length: > 0 } id) continue;
            entries.Add((subscription.IsSeries, provider.ToUpperInvariant(), id));
        }
        return entries.Count == 0 ? Empty : new(entries);
    }

    /// <summary>这张卡在不在已订阅清单里；剧与电影分开认，单集和其它类型不参与。</summary>
    public bool Contains(EmbyItem? item)
    {
        if (item is null || _entries.Count == 0) return false;
        var series = item.Type == EmbyItemType.Series;
        if (!series && item.Type != EmbyItemType.Movie) return false;
        foreach (var pair in item.ProviderIds)
            if (_entries.Contains((series, pair.Key.ToUpperInvariant(), pair.Value))) return true;
        return false;
    }
}

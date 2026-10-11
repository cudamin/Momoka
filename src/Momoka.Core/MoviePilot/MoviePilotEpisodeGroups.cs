using System.Text.Json;

namespace Momoka.MoviePilot;

/// <summary>
/// TMDB 的一个「剧集组」（<c>media/groups/{tmdbid}</c> 回话里的一条），外加屏上固定排在最前面那一项
/// <see cref="IsDefault">「默认」</see>。
/// <para>
/// 剧集组说的是「同一部剧按哪一套分季分集」：TMDB 默认排序之外还有别人编好的分组（如「Seasons」「Cours」），
/// 季数与集数都不一样。订阅带 <c>episode_group</c> 时 MoviePilot 就按那一套算集号 —— 它同时决定「屏上第几集」
/// 和「服务器去找哪些集」，所以取播出日期也要带上它（见 <see cref="MoviePilotService.SubscriptionAirDatesAsync"/>），
/// 否则集号是另一套、对不上屏上那几集。
/// </para>
/// </summary>
public sealed record MoviePilotEpisodeGroup(string Id, string Name, int SeasonCount, int EpisodeCount)
{
    /// <summary>「默认」那一项：<c>id</c> 是空串，代表不使用剧集组、按 TMDB 默认排序。订阅没设剧集组时它就是当前项。</summary>
    public bool IsDefault => Id.Length == 0;

    /// <summary>卡片上那行名字。服务器没给名字就用编号顶上，不留一行空白。</summary>
    public string Label => Name.Length > 0 ? Name : IsDefault ? "默认" : Id;

    /// <summary>「4 季 · 110 集」。两个计数都是 0 时是空串 —— 屏上就不显示那一行，而不是写「0 季」。</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>(2);
            if (SeasonCount > 0) parts.Add($"{SeasonCount} 季");
            if (EpisodeCount > 0) parts.Add($"{EpisodeCount} 集");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// 把 <c>media/groups/{tmdbid}</c>（剧集组列表）与 <c>media/seasons</c>（默认排序的季列表）两份回话拼成屏上
/// 那份选项：**第一项永远是「默认」**，后面按服务器给的次序排。纯函数，与排版无关，所以住 Core、测得动。
/// <para>
/// 「默认」那一项的季数/集数只能从季列表来 —— 剧集组接口自己不带它（MoviePilot 网页端也是拿媒体详情的
/// <c>season_info</c> 现算的）。季列表读不到不算错：那一项就少一行计数，选项本身照旧可用。
/// </para>
/// </summary>
public static class MoviePilotEpisodeGroups
{
    public static IReadOnlyList<MoviePilotEpisodeGroup> Build(JsonElement groups, JsonElement seasons)
    {
        var options = new List<MoviePilotEpisodeGroup> { Default(seasons) };
        if (groups.ValueKind == JsonValueKind.Array)
            foreach (var item in groups.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                // 没有 id 的条目选不了，也没有「当前」可言；名字缺了由 Label 用 id 顶上。
                var id = MoviePilotSubscription.Text(item, "id").Trim();
                if (id.Length == 0) continue;
                options.Add(new(id, MoviePilotSubscription.Text(item, "name").Trim(),
                    MoviePilotSubscription.Number(item, "group_count") ?? 0,
                    MoviePilotSubscription.Number(item, "episode_count") ?? 0));
            }
        // 同一组重复出现时留第一条：屏上是「选一组」，同一个 id 摆两格只会让人以为有两个选择。
        return options.DistinctBy(option => option.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 「当前：Seasons · 4 季 · 110 集」。没有选项时是空串（屏上不显示那一行）。
    /// 订阅用的组不在服务器这份列表里时照实说 —— 不硬指到「默认」上去，那会让人以为订阅没设过剧集组。
    /// </summary>
    public static string Current(IReadOnlyList<MoviePilotEpisodeGroup> options, string current)
    {
        if (options.Count == 0) return "";
        var option = Find(options, current);
        if (option is null) return current.Trim().Length == 0 ? "" : $"当前：{current.Trim()}（不在服务器的剧集组列表里）";
        return option.Summary.Length > 0 ? $"当前：{option.Label} · {option.Summary}" : $"当前：{option.Label}";
    }

    /// <summary>按 id 找那一项；空串（或全是空白）就是「默认」那一项。</summary>
    public static MoviePilotEpisodeGroup? Find(IReadOnlyList<MoviePilotEpisodeGroup> options, string id) =>
        options.FirstOrDefault(option => string.Equals(option.Id, id.Trim(), StringComparison.Ordinal));

    /// <summary>「默认」那一项的计数：季数＝条目数，集数＝各自 <c>episode_count</c> 之和。</summary>
    private static MoviePilotEpisodeGroup Default(JsonElement seasons)
    {
        var seasonCount = 0;
        var episodeCount = 0;
        if (seasons.ValueKind == JsonValueKind.Array)
            foreach (var season in seasons.EnumerateArray())
            {
                if (season.ValueKind != JsonValueKind.Object) continue;
                seasonCount++;
                episodeCount += MoviePilotSubscription.Number(season, "episode_count") ?? 0;
            }
        return new("", "默认", seasonCount, episodeCount);
    }
}

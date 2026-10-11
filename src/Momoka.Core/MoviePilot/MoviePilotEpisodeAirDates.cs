using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

/// <summary>
/// 从 MoviePilot 的 TMDB 季集接口（<c>tmdb/{tmdbid}/{season}</c>）回话里取出「集号 → 播出日期」。一个输入一个
/// 输出的纯函数，放 Core 配测试。
/// <para>
/// 为什么要另取：订阅的文件统计（<c>subscribe/files/{id}</c>）只给每集的标题、简介、剧照和文件，**没有播出日期**
/// —— 那个 schema（<c>SubscribeEpisodeInfo</c>）里就没有这一项。TMDB 季集接口的 <c>air_date</c> 是唯一能覆盖
/// 全部集（含未入库、未播出）的来源，但只对来源为 TMDB 的订阅有效。
/// </para>
/// </summary>
public static class MoviePilotEpisodeAirDates
{
    /// <summary>
    /// 回话是一串裸的 TMDB 集对象（<c>List[TmdbEpisode]</c>，没有信封）。空 <c>air_date</c>、缺集号、集号为负、
    /// 日期解析不出来的都跳过 —— 宁可那一集没有日期，也不拿一个猜出来的值去判「已播出」。
    /// </summary>
    public static IReadOnlyDictionary<int, DateTimeOffset> Parse(JsonElement data)
    {
        var result = new Dictionary<int, DateTimeOffset>();
        if (data.ValueKind != JsonValueKind.Array) return result;
        foreach (var episode in data.EnumerateArray())
        {
            if (episode.ValueKind != JsonValueKind.Object) continue;
            if (MoviePilotSubscription.Number(episode, "episode_number") is not { } number || number < 0) continue;
            if (DateTimeOffset.TryParse(MoviePilotSubscription.Text(episode, "air_date"),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) result[number] = date;
        }
        return result;
    }
}

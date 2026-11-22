using System.Globalization;
using System.Text.RegularExpressions;

namespace Momoka.MoviePilot;

/// <summary>
/// 网页端「季」筛选的选项与匹配口径：<c>meta_info.season_episode</c> 原样作为候选
/// （S01、S01E05、S01-S02、S01E01-E12 这些写法），整季排在单集前，同类按季号、集号降序。
/// </summary>
public static partial class MoviePilotSeasonEpisodeOptions
{
    /// <summary>按网页端顺序排列并去重；同一取值的大小写写法视为同一项。</summary>
    public static IReadOnlyList<string> Order(IEnumerable<string> values)
    {
        var parsed = new List<(string Value, Parsed Parsed, int Index)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed)) continue;
            parsed.Add((trimmed, Parse(trimmed), parsed.Count));
        }

        var wholeSeasons = parsed.Where(item => item.Parsed.WholeSeason)
            .OrderByDescending(item => item.Parsed.Season)
            .ThenBy(item => item.Index);
        var episodes = parsed.Where(item => !item.Parsed.WholeSeason)
            .OrderByDescending(item => item.Parsed.Season)
            .ThenByDescending(item => item.Parsed.MaxEpisode)
            .ThenByDescending(item => item.Parsed.Episode)
            .ThenBy(item => item.Index);
        return wholeSeasons.Concat(episodes).Select(item => item.Value).ToArray();
    }

    /// <summary>选项里写着的季号；整季、单集以及季范围都取第一段。</summary>
    public static int? SeasonNumber(string value)
    {
        var parsed = Parse(value);
        return parsed.Matched ? parsed.Season : null;
    }

    /// <summary>
    /// 与网页端一致按 <c>season_episode</c> 原值精确匹配；服务器没给这个字段时，
    /// 才退回 <c>season_list</c> 解析出的整季号，避免旧回包连整季都筛不了。
    /// </summary>
    public static bool Matches(MoviePilotResource resource, string value)
    {
        var selected = value.Trim();
        if (string.Equals(resource.SeasonEpisode?.Trim(), selected, StringComparison.OrdinalIgnoreCase)) return true;
        return string.IsNullOrWhiteSpace(resource.SeasonEpisode) &&
            SeasonNumber(selected) is { } season && resource.Seasons.Contains(season);
    }

    private static Parsed Parse(string value)
    {
        var match = Format().Match(value);
        if (!match.Success) return default;
        var season = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var episode = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        var maxEpisode = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : episode;
        return new Parsed(true, season, episode, maxEpisode, !match.Groups[3].Success);
    }

    /// <summary>与网页端 sortSeasonOptions 同一个正则；容忍季集之间的空格（回包里常见 S01 E05）。</summary>
    [GeneratedRegex(@"^S(\d+)(?:-S(\d+))?\s*(?:E(\d+)(?:-E(\d+))?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Format();

    private readonly record struct Parsed(bool Matched, int Season, int Episode, int MaxEpisode, bool WholeSeason);
}

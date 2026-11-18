using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Momoka.Emby;

namespace Momoka.Danmaku;

/// <summary>danmu_api 自动匹配的文件名与集号核对规则。</summary>
public static class DanmakuMatchRules
{
    private static readonly Regex ChineseEpisode = new(@"第\s*(\d{1,6})\s*[集话話]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LatinEpisode = new(@"(?:^|[\s\[【(（])(?:EP?|Episode)\s*(\d{1,6})(?=$|[\s\]】)）.,，。:：-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PlatformSuffix = new(@"\s*from\s+\S+\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BracketedTag = new(@"[\(（\[【][^\)）\]】]*[\)）\]】]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex YearTag = new(@"[\(（\[【]\s*((?:19|20)\d{2})\s*[\)）\]】]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool CanAutoMatch(EmbyItem item) => item.Type switch
    {
        EmbyItemType.Movie => true,
        // 续季也自动匹配；季号只用来核对候选，特别篇（第 0 季）仍留给手动选择。
        EmbyItemType.Episode => item.ParentIndexNumber is >= 1 && item.IndexNumber is > 0,
        _ => false
    };

    public static string MatchFileName(EmbyItem item, int? seriesYear = null)
    {
        var title = DandanPlayMatching.Title(item).Trim();
        // 单集的 ProductionYear 是这一集的播出年；连载多年的动画要拿整部剧的首播年去匹配，否则续集匹配不到。
        var productionYear = item.Type == EmbyItemType.Episode && seriesYear is > 0
            ? seriesYear
            : item.ProductionYear;
        var year = productionYear is > 0
            ? $" ({productionYear.Value.ToString(CultureInfo.InvariantCulture)})"
            : "";
        if (item.Type != EmbyItemType.Episode) return $"{title}{year}";
        var season = item.ParentIndexNumber ?? 0;
        var episode = item.IndexNumber ?? 0;
        return $"{title}{year} S{season:00}E{episode:00}";
    }

    public static bool Accepts(EmbyItem item, DandanPlayEpisode? match)
    {
        if (match is null) return false;
        if (item.Type != EmbyItemType.Episode) return item.Type == EmbyItemType.Movie;
        if (item.IndexNumber is not > 0) return false;
        var explicitNumber = ExplicitEpisodeNumber(match.EpisodeTitle);
        return explicitNumber is null || explicitNumber == item.IndexNumber;
    }

    /// <summary>只认标题里明确写出的集号；没有写就不拿它当反证。</summary>
    public static int? ExplicitEpisodeNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var match = ChineseEpisode.Match(title);
        if (!match.Success) match = LatinEpisode.Match(title);
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0
                ? number
                : null;
    }

    /// <summary>
    /// danmu_api 搜索结果的作品名形如「工作细胞(2018)【日番】from bilibili」。
    /// 去掉平台后缀、年份与方括号标签后，用于和当前剧名比较。
    /// </summary>
    public static string WorkTitle(string? animeTitle) =>
        BracketedTag.Replace(PlatformSuffix.Replace(animeTitle ?? "", ""), " ").Trim();

    private static int? WorkYear(string? animeTitle)
    {
        var match = YearTag.Match(animeTitle ?? "");
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year : null;
    }

    /// <summary>danmu_api 常把同一作品拆成多个平台行；只有同名同季才算可替换的候选。</summary>
    public static bool SameWork(EmbyItem item, DandanPlayEpisode candidate, int? seriesYear)
    {
        if (WorkYear(candidate.AnimeTitle) is { } year && seriesYear is > 0 && year != seriesYear.Value) return false;
        var work = WorkTitle(candidate.AnimeTitle);
        if (work.Length == 0) return false;
        var series = DandanPlayMatching.Title(item);
        if (Normalize(work) == Normalize(series)) return true;
        var season = item.ParentIndexNumber;
        if (season is not > 1) return false;
        return DandanPlayMatching.TrySplitSeason(work, out var baseTitle, out var candidateSeason)
            && candidateSeason == season
            && Normalize(baseTitle) == Normalize(series);
    }

    /// <summary>零弹幕兜底只收同作品、同集，且不是刚试过的那一条；按搜索结果原顺序保留。</summary>
    public static IReadOnlyList<DandanPlayEpisode> Alternatives(EmbyItem item,
        IReadOnlyList<DandanPlayEpisode> candidates, long attemptedId, int? seriesYear)
    {
        HashSet<long> seen = [];
        return [.. candidates.Where(candidate => candidate.EpisodeId != attemptedId
            && seen.Add(candidate.EpisodeId)
            && Accepts(item, candidate)
            && SameWork(item, candidate, seriesYear))];
    }

    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

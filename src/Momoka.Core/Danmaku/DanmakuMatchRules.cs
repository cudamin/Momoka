using System.Globalization;
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

    public static bool CanAutoMatch(EmbyItem item) => item.Type switch
    {
        EmbyItemType.Movie => true,
        EmbyItemType.Episode => item.ParentIndexNumber is 1 && item.IndexNumber is > 0,
        _ => false
    };

    public static string MatchFileName(EmbyItem item)
    {
        var title = DandanPlayMatching.Title(item).Trim();
        var year = item.ProductionYear is { } productionYear && productionYear > 0
            ? $" ({productionYear.ToString(CultureInfo.InvariantCulture)})"
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
}

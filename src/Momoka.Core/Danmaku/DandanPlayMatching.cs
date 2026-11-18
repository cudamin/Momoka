using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Danmaku;

/// <summary>
/// 标题搜索不等于文件识别。只自动接受唯一的精确标题；续季按标题里的季标记核对，特别篇留给手动选择。
/// </summary>
public static class DandanPlayMatching
{
    private static readonly Regex OrdinalSeason = new(@"([0-9]{1,2})(?:st|nd|rd|th)\s*Season",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ChineseSeason = new(@"第\s*([0-9]{1,2}|[一二三四五六七八九十]{1,3})\s*[季期部]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EnglishSeason = new(@"(?:^|[\s\-_:：·])(?:Season|Series|S)\s*([0-9]{1,2})(?=$|[\s\-_:：·])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingYear = new(@"\s*[\(（\[【]\s*(?:19|20)\d{2}\s*[\)）\]】]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex[] SeasonPatterns = [OrdinalSeason, ChineseSeason, EnglishSeason];

    public static string Title(EmbyItem item) => item.Type == EmbyItemType.Episode && !string.IsNullOrWhiteSpace(item.SeriesName)
        ? item.SeriesName : item.Name;

    public static string? EpisodeNumber(EmbyItem item) => item.Type == EmbyItemType.Episode
        && item.ParentIndexNumber is not 0 && item.IndexNumber is > 0
            ? item.IndexNumber.Value.ToString(CultureInfo.InvariantCulture) : null;

    public static DandanPlayEpisode? UniqueMatch(EmbyItem item, IReadOnlyList<DandanPlayEpisode> candidates, bool hasMore = false)
    {
        // 官方 hasMore 或本地截断表示还有未见候选，当前页的一项不能证明唯一。
        if (hasMore) return null;
        var title = Title(item);
        if (item.Type != EmbyItemType.Episode)
        {
            var normalizedTitle = Normalize(title);
            var movieMatches = candidates.Where(candidate => Normalize(candidate.AnimeTitle) == normalizedTitle).ToArray();
            return movieMatches.Length == 1 ? movieMatches[0] : null;
        }

        var season = item.ParentIndexNumber ?? 0;
        if (season < 1 || item.IndexNumber is not > 0) return null;
        // 一套 SeriesName 经常覆盖多季；相同标题的 S02E06 不能悄悄挂到第一季第六集。
        var seasonOfSeries = SeasonNumber(title);
        var seriesTitle = Normalize(title);
        var matches = candidates
            .Where(candidate => SameSeason(candidate.AnimeTitle, seriesTitle, season, seasonOfSeries))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>标题里明确写出的季号；没有季标记时返回 null，不把「没有」当成第一季。</summary>
    public static int? SeasonNumber(string? title) =>
        !string.IsNullOrWhiteSpace(title) && TrySplitSeason(title, out _, out var season) ? season : null;

    private static bool SameSeason(string candidateTitle, string seriesTitle, int season, int? seasonOfSeries)
    {
        var normalized = Normalize(candidateTitle);
        if (normalized == seriesTitle)
        {
            // 精确同名：系列名自己带季标记时按它核对，否则只认第一季，避免续季挂到裸标题上。
            return seasonOfSeries is { } pinned ? pinned == season : season == 1;
        }
        if (seasonOfSeries is not null) return false;
        return TrySplitSeason(candidateTitle, out var baseTitle, out var candidateSeason)
            && candidateSeason == season
            && Normalize(baseTitle) == seriesTitle;
    }

    internal static bool TrySplitSeason(string title, out string baseTitle, out int season)
    {
        baseTitle = "";
        season = 0;
        foreach (var pattern in SeasonPatterns)
        {
            var match = pattern.Match(title);
            if (!match.Success || !TrySeasonValue(match.Groups[1].Value, out season)) continue;
            baseTitle = Clean(title.Remove(match.Index, match.Length));
            return true;
        }
        return false;
    }

    private static bool TrySeasonValue(string value, out int season)
    {
        season = 0;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            season = number;
            return number is >= 1 and <= 99;
        }
        var ten = value.IndexOf('十');
        if (ten < 0)
        {
            if (value.Length != 1 || !TryDigit(value[0], out number)) return false;
            season = number;
            return number is >= 1 and <= 9;
        }
        if (value.Length > ten + 2) return false;
        var tens = ten == 0 ? 1 : TryDigit(value[0], out var digit) ? digit : -1;
        var ones = ten == value.Length - 1 ? 0 : TryDigit(value[ten + 1], out var tail) ? tail : -1;
        if (tens < 1 || ones < 0) return false;
        season = tens * 10 + ones;
        return season is >= 10 and <= 99;
    }

    private static bool TryDigit(char value, out int number)
    {
        number = value switch
        {
            '一' => 1,
            '二' => 2,
            '三' => 3,
            '四' => 4,
            '五' => 5,
            '六' => 6,
            '七' => 7,
            '八' => 8,
            '九' => 9,
            _ => -1,
        };
        return number > 0;
    }

    private static string Clean(string value)
    {
        value = TrailingYear.Replace(value, "");
        return value.Trim(' ', '\t', '-', '—', '–', '_', ':', '：', '·', '(', ')', '（', '）', '[', ']', '【', '】');
    }

    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool CanRender(MpvSettings settings) => settings.Backend == MpvBackendKind.BuiltInLibMpv;

    public static bool IsConfigured(AppSettings settings) => settings.Danmaku.Enabled && CanRender(settings.Mpv);
}

using System.Text;
using System.Text.RegularExpressions;

namespace Momoka.AniBt;

/// <summary>
/// 判断 AniBT 候选和库内剧名是不是同一部作品。AniBT 常按季拆条目（「青之箱 第二季」），而库里的主剧集不带
/// 季名；整理目标必须落回主剧集，不能因为这一层季名新建一部剧。比较前只去掉末尾的季标记，不动作品名本身。
/// </summary>
internal static partial class AniBtSeriesIdentity
{
    internal static bool SameSeries(IEnumerable<string?> candidateTitles, IEnumerable<string?> seriesTitles)
    {
        var series = seriesTitles.Select(BaseTitle).Where(title => title.Length > 0).ToHashSet(StringComparer.Ordinal);
        return series.Count > 0 && candidateTitles.Select(BaseTitle).Any(title => title.Length > 0 && series.Contains(title));
    }

    internal static string BaseTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var normalized = string.Concat(value.Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant));
        // 只削末尾的季标记；整串就是标记（极短标题）时保留原样，避免比空串。
        while (SeasonSuffix().Match(normalized) is { Success: true } match && match.Index > 0)
            normalized = normalized[..match.Index];
        return normalized;
    }

    /// <summary>保留空格和大小写的作品名，供 MoviePilot 整理历史按片名查询；比较仍用 <see cref="BaseTitle"/>。</summary>
    internal static string DisplayBaseTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        var stripped = DisplaySeasonSuffix().Replace(trimmed, "").Trim().TrimEnd('-', '–', '—', ':', '：', ' ');
        return stripped.Length > 0 ? stripped : trimmed;
    }

    // 「第二季」「第2期」「Season 2」「2nd Season」「Part 2」是发布标题里最常见的季标记。
    [GeneratedRegex(@"(?:第[0-9一二三四五六七八九十百]+[季期部]|season[0-9]*|[0-9]+(?:st|nd|rd|th)season|part[0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonSuffix();

    [GeneratedRegex(@"\s*(?:第[0-9一二三四五六七八九十百]+[季期部]|season\s*[0-9]*|[0-9]+(?:st|nd|rd|th)\s*season|part\s*[0-9]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplaySeasonSuffix();
}

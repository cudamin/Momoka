using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Momoka.Metadata;

public sealed record ParsedLocalMediaName
{
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public bool IsSeries { get; init; }
    public string Problem { get; init; } = "";
}

/// <summary>只分析文件名与父目录文本，不访问或上传媒体文件。</summary>
public static class LocalMediaName
{
    private static readonly Regex Seasons = Pattern(@"(?<![A-Za-z0-9])(?:S|Season[ ._-]*)(?<number>\d{1,3})(?!\d)");
    private static readonly Regex Episodes = Pattern(@"(?<![A-Za-z])(?:E|Episode[ ._-]*)(?<number>\d{1,4})(?!\d)");
    private static readonly Regex EpisodeRange = Pattern(@"(?:E\d{1,4})[ ._]*[-~至][ ._]*(?:E)?\d{1,4}(?!\d)");
    private static readonly Regex SeasonRange = Pattern(@"(?:S\d{1,3})[ ._]*[-~至][ ._]*(?:S)?\d{1,3}(?!\d)");
    private static readonly Regex Years = Pattern(@"(?<!\d)(?:19|20|21)\d{2}(?!\d)");
    // 中英双语片名常写成「爱·回家之三代同糖.Like.Mother.Like.Daughter」：整串拿去搜 TMDB 往往找不到，
    // 需要把中文段和拉丁字母段分别作为候选关键词。只分析文件名文本，不访问媒体。
    private static readonly Regex CjkRun = Pattern(@"[\p{IsCJKUnifiedIdeographs}\p{IsCJKCompatibilityIdeographs}\p{IsHiragana}\p{IsKatakana}·・]+");
    private static readonly Regex LatinRun = Pattern(@"[A-Za-z][A-Za-z0-9 '’:&!?,\-]*");
    private static readonly Regex TechnicalTail = Pattern(@"(?<![A-Za-z0-9])(?:\d{3,4}p|2160i|1080i|720i|WEB[ ._-]?DL|WEBRip|Blu[ ._-]?Ray|BDRip|BRRip|HDTV|DVDRip|REMUX|x26[45]|H[ ._-]?26[45]|HEVC|AVC|AV1|HDR10\+?|HDR|DV|Dolby[ ._-]?Vision)(?![A-Za-z0-9])");
    private static readonly Regex OnlyTechnical = Pattern(@"^(?:[ ._\[\]()]|\d{3,4}[pi]|WEB[ ._-]?DL|WEBRip|Blu[ ._-]?Ray|BDRip|BRRip|HDTV|DVDRip|REMUX|x26[45]|H[ ._-]?26[45]|HEVC|AVC|AV1|HDR10\+?|HDR|DV|Dolby[ ._-]?Vision|AAC(?:[ ._-]?\d(?:[ ._-]\d)?)?|DDP?(?:[ ._-]?\d(?:[ ._-]\d)?)?|DTS(?:[ ._-]?HD)?|TrueHD|Atmos|FLAC|10bit|8bit)*(?:-[A-Za-z0-9][A-Za-z0-9._-]*)?$");
    private static readonly Regex Whitespace = Pattern(@"\s+");
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".m2ts", ".mpg", ".mpeg", ".webm", ".flv", ".iso"
    };

    public static ParsedLocalMediaName Parse(string fileContext)
    {
        ArgumentNullException.ThrowIfNull(fileContext);
        if (fileContext.Length is 0 or > 8192)
            return new() { Problem = "文件名为空或过长，请手动输入标题。" };

        // 不调用Path.GetFullPath/File.Exists：父目录只是标题上下文，解析本身不能接触视频。
        var parts = fileContext.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return new() { Problem = "未找到文件名，请手动输入标题。" };
        var filename = parts[^1];
        var extension = filename.LastIndexOf('.');
        if (extension >= 0 && Extensions.Contains(filename[extension..])) filename = filename[..extension];

        var scopes = new List<string> { filename };
        for (var i = parts.Length - 2; i >= 0 && scopes.Count < 4; i--) scopes.Add(parts[i]);
        var seasonValues = scopes.SelectMany(scope => Seasons.Matches(scope).Select(Number)).Distinct().ToArray();
        var fileSeasons = Seasons.Matches(filename);
        var episodes = Episodes.Matches(filename);
        var problem = "";
        if (seasonValues.Length > 1 || fileSeasons.Count > 1 || scopes.Any(SeasonRange.IsMatch))
            problem = "文件名包含多季或与父目录季号冲突，请手动确认。";
        else if (episodes.Count > 1 || EpisodeRange.IsMatch(filename))
            problem = "文件名包含多集或集数范围，请手动确认。";

        int? season = seasonValues.Length > 0 ? seasonValues[0] : null;
        int? episode = episodes.Count > 0 ? Number(episodes[0]) : null;
        if (episode.HasValue && !season.HasValue && problem.Length == 0)
            problem = "已识别集号但没有季号，请手动确认季号。";

        var title = "";
        int? year = null;
        foreach (var scope in scopes)
        {
            var extracted = ExtractTitle(scope);
            if (year is null) year = extracted.Year;
            if (extracted.Title.Length == 0) continue;
            title = extracted.Title;
            break;
        }
        if (title.Length == 0 && problem.Length == 0) problem = "未识别到标题，请手动输入标题。";
        return new()
        {
            Title = title,
            Year = year,
            Season = season,
            Episode = episode,
            IsSeries = season.HasValue || episode.HasValue,
            Problem = problem
        };
    }

    public static TmdbMediaCandidate? UniqueExactMatch(ParsedLocalMediaName parsed, IEnumerable<TmdbMediaCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(candidates);
        if (parsed.Problem.Length > 0 || parsed.Title.Length == 0) return null;
        var titles = SearchQueries(parsed).Select(NormalizeTitle).ToHashSet(StringComparer.Ordinal);
        var matches = candidates.Where(candidate => candidate.IsSeries == parsed.IsSeries
            && (!parsed.Year.HasValue || candidate.Year == parsed.Year)
            && (titles.Contains(NormalizeTitle(candidate.Title)) || titles.Contains(NormalizeTitle(candidate.OriginalTitle))))
            .ToArray();
        if (matches.Any(candidate => !candidate.AutoMatchEligible)) return null;
        var unique = matches.DistinctBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();
        return unique.Length == 1 ? unique[0] : null;
    }

    /// <summary>
    /// 依次用整串片名、中文段、拉丁段去搜；第一个得到唯一精确匹配的关键词胜出。
    /// 这样「爱·回家之三代同糖.Like.Mother.Like.Daughter」不会因为整串搜不到而放弃。
    /// </summary>
    public static IReadOnlyList<string> SearchQueries(ParsedLocalMediaName parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var queries = new List<string>();
        void Add(string value)
        {
            value = Whitespace.Replace(value.Replace('.', ' ').Replace('_', ' '), " ")
                .Trim(' ', '-', '[', ']', '(', ')', '{', '}');
            if (value.Length < 2) return;
            var key = NormalizeTitle(value);
            if (key.Length < 2 || queries.Any(existing => NormalizeTitle(existing) == key)) return;
            queries.Add(value);
        }

        Add(parsed.Title);
        foreach (var chunk in parsed.Title.Split(['|', '/', '\\', ';', '；', '、'], StringSplitOptions.RemoveEmptyEntries))
        {
            Add(chunk);
            foreach (Match run in CjkRun.Matches(chunk)) Add(run.Value);
            foreach (Match run in LatinRun.Matches(chunk)) Add(run.Value);
        }
        return queries;
    }

    private static (string Title, int? Year) ExtractTitle(string scope)
    {
        var firstSeason = Seasons.Match(scope);
        var firstEpisode = Episodes.Match(scope);
        var boundary = Math.Min(firstSeason.Success ? firstSeason.Index : scope.Length,
            firstEpisode.Success ? firstEpisode.Index : scope.Length);
        var prefix = scope[..boundary];
        // 年份只取标题后的最后一个独立年份；片名开头的2001/1917/1984仍保留。
        var yearMatch = Years.Matches(prefix).LastOrDefault(match => match.Index > 0
            && !char.IsLetterOrDigit(prefix[match.Index - 1])
            && (match.Index + match.Length == prefix.Length || !char.IsLetterOrDigit(prefix[match.Index + match.Length])));
        int? year = yearMatch is null ? null : int.Parse(yearMatch.Value, CultureInfo.InvariantCulture);
        if (yearMatch is not null) prefix = prefix[..yearMatch.Index];
        else
        {
            // 技术词只有在后方全部是技术参数/发布组时才是尾巴；The 1080p Story仍是完整片名。
            var technical = TechnicalTail.Matches(prefix).FirstOrDefault(match => match.Index > 0 && OnlyTechnical.IsMatch(prefix[match.Index..]));
            if (technical is not null) prefix = prefix[..technical.Index];
        }
        var title = Whitespace.Replace(prefix.Replace('.', ' ').Replace('_', ' '), " ")
            .Trim(' ', '-', '[', ']', '(', ')', '{', '}');
        return (title, year);
    }

    private static string NormalizeTitle(string title)
    {
        var text = title.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character)) builder.Append(character);
            // 间隔号与撇号只是排版分隔，不制造词边界：爱·回家 ＝ 爱回家，Schindler's ＝ Schindlers。
            else if (character is '·' or '・' or '\'') continue;
            // 空格、逗号、冒号等保留成一个词边界：A.B 与 AB 仍然不同，Like Mother, Like Daughter 与
            // Like Mother Like Daughter 相同。
            else builder.Append(' ');
        }
        return Whitespace.Replace(builder.ToString(), " ").Trim();
    }

    private static int Number(Match match) => int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}

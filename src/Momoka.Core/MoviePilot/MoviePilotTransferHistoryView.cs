using System.Globalization;
using System.IO.Enumeration;

namespace Momoka.MoviePilot;

public enum MoviePilotTransferStatusFilter { All, Success, Failed }
public enum MoviePilotTransferSort { Default, Title, Path, Mode, Size, Time, Status }

/// <summary>只筛选已经收齐的原历史；不改变当前剧身份、源文件或整理授权。</summary>
public static class MoviePilotTransferHistoryView
{
    public static IReadOnlyList<MoviePilotTransferHistory> Apply(IEnumerable<MoviePilotTransferHistory> histories,
        string search, MoviePilotTransferStatusFilter status, MoviePilotTransferSort sort, bool descending)
    {
        var terms = search.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizePath).ToArray();
        var result = histories.Where(history => MatchesStatus(history, status) && Matches(history, terms));
        // 默认顺序就是接口顺序，不用 id 或时间重新猜测；相同字段保留原来的相对顺序。
        if (sort != MoviePilotTransferSort.Default)
        {
            var comparer = Comparer<MoviePilotTransferHistory>.Create((left, right) => descending
                ? Compare(right, left, sort) : Compare(left, right, sort));
            result = result.OrderBy(history => history, comparer);
        }
        return result.ToArray();
    }

    public static string ModeLabel(string mode) => mode switch
    {
        "copy" => "复制",
        "move" => "移动",
        "link" => "硬链接",
        "softlink" => "软链接",
        "" => "方式未返回",
        _ => mode
    };

    private static bool MatchesStatus(MoviePilotTransferHistory history, MoviePilotTransferStatusFilter status) => status switch
    {
        MoviePilotTransferStatusFilter.Success => history.Success,
        MoviePilotTransferStatusFilter.Failed => !history.Success,
        _ => true
    };

    private static bool Matches(MoviePilotTransferHistory history, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) return true;
        var fields = new[]
        {
            history.Title, history.Seasons + history.Episodes, history.Year,
            NormalizePath(history.SourcePath), NormalizePath(history.TargetPath),
            history.SourceStorage, history.TargetStorage, history.Mode, ModeLabel(history.Mode),
            history.Date, history.Success ? "成功" : "失败", "tmdbid=" + history.TmdbId,
            "media_id=" + history.MediaId, "#" + history.Id.ToString(CultureInfo.InvariantCulture)
        };
        // 多个词都要命中，但允许分别命中标题和路径。* / ? 只匹配文字，不作为正则执行。
        return terms.All(term => fields.Any(field => term.IndexOfAny(['*', '?']) >= 0
            ? FileSystemName.MatchesSimpleExpression("*" + term + "*", field, ignoreCase: true)
            : field.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizePath(string value) => value.Replace('\\', '/');

    private static int Compare(MoviePilotTransferHistory left, MoviePilotTransferHistory right, MoviePilotTransferSort sort) => sort switch
    {
        MoviePilotTransferSort.Title => CompareTitle(left, right),
        MoviePilotTransferSort.Path => StringComparer.CurrentCultureIgnoreCase.Compare(left.SourcePath, right.SourcePath),
        MoviePilotTransferSort.Mode => StringComparer.Ordinal.Compare(left.Mode, right.Mode),
        MoviePilotTransferSort.Size => left.Size.GetValueOrDefault().CompareTo(right.Size.GetValueOrDefault()),
        MoviePilotTransferSort.Time => Timestamp(left.Date).CompareTo(Timestamp(right.Date)),
        MoviePilotTransferSort.Status => left.Success.CompareTo(right.Success),
        _ => 0
    };

    private static int CompareTitle(MoviePilotTransferHistory left, MoviePilotTransferHistory right)
    {
        var type = StringComparer.CurrentCulture.Compare(left.Type, right.Type);
        if (type != 0) return type;
        var title = StringComparer.CurrentCultureIgnoreCase.Compare(left.Title, right.Title);
        if (title != 0 || left.Type != MoviePilotTransferRequest.TypeSeries) return title;
        var season = CompareEpisode(left.Seasons, right.Seasons);
        return season != 0 ? season : CompareEpisode(left.Episodes, right.Episodes);
    }

    private static int CompareEpisode(string left, string right)
    {
        // 与网页端去掉非数字后比较相同；用数字串比较避免超长季集标记溢出。
        var a = string.Concat(left.Where(char.IsAsciiDigit)).TrimStart('0');
        var b = string.Concat(right.Where(char.IsAsciiDigit)).TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : StringComparer.Ordinal.Compare(a, b);
    }

    private static long Timestamp(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var date) ? date.UtcTicks : 0;
}

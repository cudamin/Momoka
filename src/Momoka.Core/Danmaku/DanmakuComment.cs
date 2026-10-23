using System.Globalization;
using Momoka.Configuration;

namespace Momoka.Danmaku;

public sealed record DanmakuComment(double Time, int Mode, int Color, string Text);
public sealed record DandanPlayEpisode(long EpisodeId, string AnimeTitle, string EpisodeTitle)
{
    public string Label => $"{AnimeTitle} · {EpisodeTitle}";
}

/// <summary>解析与屏蔽规则独立于字幕轨道；非法模式、控制字符和超长文本都不进入渲染器。</summary>
public static class DanmakuComments
{
    public const int MaximumCount = 40000;
    public const int MaximumTextLength = 300;

    public static DanmakuComment? Parse(string? parameters, string? text)
    {
        var parts = parameters?.Split(',');
        if (parts is not { Length: >= 3 }
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time)
            || !double.IsFinite(time) || time is < 0 or > 86400
            || !int.TryParse(parts[1], out var mode) || mode is not (1 or 4 or 5)
            || !int.TryParse(parts[2], out var color) || color is < 0 or > 0xFFFFFF
            || string.IsNullOrWhiteSpace(text)) return null;

        var clean = new string(text.Where(character => !char.IsControl(character)).Take(MaximumTextLength).ToArray()).Trim();
        return clean.Length == 0 ? null : new(time, mode, color, clean);
    }

    public static IReadOnlyList<DanmakuComment> Filter(IEnumerable<DanmakuComment> comments, DanmakuSettings settings)
    {
        var words = settings.BlockedWords.Split(['\n', '\r', ',', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var previous = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DanmakuComment>();
        foreach (var comment in comments.OrderBy(comment => comment.Time))
        {
            if (comment.Mode == 1 && !settings.Scroll || comment.Mode == 4 && !settings.Bottom
                || comment.Mode == 5 && !settings.Top || !settings.Color && comment.Color != 0xFFFFFF
                || words.Any(word => comment.Text.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
            if (settings.Deduplicate && previous.TryGetValue(comment.Text, out var last) && comment.Time - last < 10) continue;
            previous[comment.Text] = comment.Time;
            result.Add(comment);
            if (result.Count == MaximumCount) break;
        }
        return result;
    }
}

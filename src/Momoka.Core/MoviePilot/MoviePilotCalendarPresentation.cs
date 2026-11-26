using System.Globalization;
using Momoka.AniBt;

namespace Momoka.MoviePilot;

public enum MoviePilotCalendarLibraryState { Unknown, NotInLibrary, PartiallyInLibrary, InLibrary }

/// <summary>月历卡片的纯展示规则；缺少入库事实时保留未知，不把下载数或集号顺序当入库证据。</summary>
public static class MoviePilotCalendarPresentation
{
    public static MoviePilotCalendarLibraryState LibraryState(MoviePilotCalendarEntry entry)
    {
        if (entry.Episodes.Count == 0 || entry.Episodes.Any(episode => episode.IsInLibrary is null))
            return MoviePilotCalendarLibraryState.Unknown;
        var count = entry.Episodes.Count(episode => episode.IsInLibrary == true);
        return count == entry.Episodes.Count ? MoviePilotCalendarLibraryState.InLibrary
            : count > 0 ? MoviePilotCalendarLibraryState.PartiallyInLibrary : MoviePilotCalendarLibraryState.NotInLibrary;
    }

    public static string LibraryStateText(MoviePilotCalendarEntry entry) => entry.Type != "电视剧" ? "" : LibraryState(entry) switch
    {
        MoviePilotCalendarLibraryState.InLibrary => "已入库",
        MoviePilotCalendarLibraryState.PartiallyInLibrary => "部分入库",
        MoviePilotCalendarLibraryState.NotInLibrary => "未入库",
        _ => "入库状态未知"
    };

    public static string EpisodeLabel(MoviePilotCalendarEntry entry, Func<string, string>? translate = null)
    {
        if (entry.Type == "电影") return translate?.Invoke("电影") ?? "电影";
        var numbers = entry.Episodes.Select(episode => episode.Number).Where(number => number > 0).Distinct().Order().ToArray();
        if (numbers.Length == 0) return "";
        var groups = new List<string>();
        for (var index = 0; index < numbers.Length; index++)
        {
            var start = numbers[index];
            var end = start;
            while (index + 1 < numbers.Length && (long)numbers[index + 1] == (long)end + 1) end = numbers[++index];
            groups.Add(start == end ? start.ToString(CultureInfo.InvariantCulture)
                : $"{start.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}");
        }
        return string.Format(CultureInfo.InvariantCulture, translate?.Invoke("第 {0} 集") ?? "第 {0} 集",
            string.Join(translate?.Invoke("、") ?? "、", groups));
    }

    public static string LibraryProgressText(MoviePilotCalendarEntry entry)
    {
        if (entry.Type != "电视剧") return "";
        if (entry.LibraryEpisodeCount is not >= 0) return "入库进度未知";
        return entry.LibraryTotalEpisodes is > 0 and var total
            ? $"已入库 {entry.LibraryEpisodeCount}/{total} 集" : $"已入库 {entry.LibraryEpisodeCount} 集";
    }

    public static string RuntimeText(MoviePilotCalendarEntry entry, Func<string, string>? translate = null) =>
        entry.RuntimeMinutes is > 0 and var runtime
            ? string.Format(CultureInfo.InvariantCulture, translate?.Invoke("{0} 分钟") ?? "{0} 分钟", runtime) : "";

    /// <summary>上游 date 只有日历日期，没有时区；电影为上映日期，剧集为播出日期。</summary>
    public static string ScheduleText(MoviePilotCalendarEntry entry) => entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        + (entry.Type == "电影" ? " 上映" : " 播出");

    public static string UpdatedText(MoviePilotCalendarEntry entry, DateTimeOffset now, Func<string, string>? translate = null)
    {
        if (entry.LibraryEpisodeCount is not > 0 || !TryUpdate(entry.LastUpdate, now.Offset, out var updated)) return "";
        return string.Format(CultureInfo.InvariantCulture, translate?.Invoke("{0}更新") ?? "{0}更新",
            AniBtReleaseLabels.Published(updated, now, translate));
    }

    private static bool TryUpdate(string value, TimeSpan offset, out DateTimeOffset updated)
    {
        updated = default;
        value = value.Trim();
        if (value.Length == 0) return false;
        // MoviePilot 常返回无时区的服务器本地时间。显式偏移照用；无偏移按调用方所在时区解释，
        // 不让运行测试的机器时区决定文案。无可信时间不把原始字符串放入卡片。
        var timeStart = value.IndexOf('T');
        if (timeStart < 0) timeStart = value.IndexOf(' ');
        var hasOffset = value.EndsWith('Z') || timeStart >= 0 && (value.IndexOf('+', timeStart) >= 0 || value.IndexOf('-', timeStart) >= 0);
        if (hasOffset)
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out updated);
        if (!DateTime.TryParseExact(value, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        try
        {
            updated = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
}

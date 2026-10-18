using System.Globalization;

namespace Momoka.AniBt;

/// <summary>
/// AniBT 发布行的短标签。集号写作 RSS 原值、不补季号；发布时间按传入的“现在”算相对值，
/// 否则同一份结果在测试与界面里会给出两个答案。
/// </summary>
public static class AniBtReleaseLabels
{
    public const string Batch = "合集";
    public const string UnknownEpisode = "未标集号";
    public const string UnknownGroup = "未标字幕组";
    public const string UnknownResolution = "未标清晰度";
    public const string UnknownPublished = "发布时间未提供";

    /// <summary>左侧那格大字：数字集号保留两位（1 → 01），其余在 RSS 里不是单集的东西一律读作合集。</summary>
    public static string EpisodeBadge(string? episodeKey)
    {
        var key = episodeKey?.Trim() ?? "";
        return key.Length is > 0 and <= 3 && key.All(char.IsAsciiDigit) ? key.PadLeft(2, '0') : Batch;
    }

    /// <summary>筛选下拉与行内副标题用的集号；空值是“未标集号”，不猜成第一集。</summary>
    public static string EpisodeChoice(string? episodeKey)
    {
        var key = episodeKey?.Trim() ?? "";
        return key.Length == 0 ? UnknownEpisode : key;
    }

    public static string Group(string? groupName)
    {
        var name = groupName?.Trim() ?? "";
        return name.Length == 0 ? UnknownGroup : name;
    }

    public static string Resolution(string? resolution)
    {
        var value = resolution?.Trim() ?? "";
        return value.Length == 0 ? UnknownResolution : value;
    }

    /// <summary>
    /// 一周以内说“多久以前”，更早的给日期。用 <paramref name="now"/> 的时区显示日期，
    /// 这样跨时区测试与界面拿到的是同一个字符串。
    /// </summary>
    public static string Published(DateTimeOffset? published, DateTimeOffset now)
    {
        if (published is not { } time) return UnknownPublished;

        var elapsed = now - time;
        if (elapsed >= TimeSpan.Zero)
        {
            if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
            if (elapsed < TimeSpan.FromHours(1))
                return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
            if (elapsed < TimeSpan.FromDays(1))
                return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
            if (elapsed < TimeSpan.FromDays(7))
                return $"{Math.Max(1, (int)elapsed.TotalDays)} 天前";
        }

        return time.ToOffset(now.Offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

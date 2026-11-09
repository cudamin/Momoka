using System.Globalization;

namespace Momoka.MoviePilot;

/// <summary>封面上的更新时间和内嵌头像；空数据不伪造下载事实。</summary>
public static class MoviePilotSubscriptionPresentation
{
    public static string Updated(string value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var time)) return "";
        var span = now - time;
        var seconds = Math.Round(Math.Abs(span.TotalSeconds), MidpointRounding.AwayFromZero);
        var minutes = Math.Round(Math.Abs(span.TotalMinutes), MidpointRounding.AwayFromZero);
        var hours = Math.Round(Math.Abs(span.TotalHours), MidpointRounding.AwayFromZero);
        var days = Math.Round(Math.Abs(span.TotalDays), MidpointRounding.AwayFromZero);
        // 与网页 dayjs 的秒、分、时、日阈值一致；长期订阅继续按天显示，便于比较下载间隔。
        var text = seconds <= 44 ? "几秒" : seconds <= 89 ? "1分钟" : minutes <= 44 ? $"{minutes}分钟"
            : minutes <= 89 ? "1小时" : hours <= 21 ? $"{hours}小时" : hours <= 35 ? "1天" : $"{days}天";
        return text + (span < TimeSpan.Zero ? "内" : "前");
    }

    public static byte[] AvatarBytes(string value)
    {
        // MoviePilot 上传接口保存 data:image/ico;base64；内容类型也可能是 png/jpeg。
        if (!value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || value.Length > 3_000_000) return [];
        var separator = value.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        if (separator < 0) return [];
        try { return Convert.FromBase64String(value[(separator + 8)..]); }
        catch (FormatException) { return []; }
    }
}

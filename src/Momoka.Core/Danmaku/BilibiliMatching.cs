using System.Globalization;
using Momoka.Emby;

namespace Momoka.Danmaku;

/// <summary>B站自动匹配只接受唯一精确作品；单集还必须有明确且一致的数字集号。</summary>
public static class BilibiliMatching
{
    public static DandanPlayEpisode? UniqueMatch(EmbyItem item, DandanPlaySearchResult result)
    {
        if (!DanmakuMatchRules.CanAutoMatch(item) || !DandanPlayMatching.Title(item).Any(char.IsLetterOrDigit)) return null;
        var match = DandanPlayMatching.UniqueMatch(item, result.Episodes, result.HasMore);
        if (match is null || match.EpisodeId <= 0) return null;
        if (item.Type == EmbyItemType.Movie) return match;

        // 客户端把官方数字 title 与 long_title 用空格连接；只读开头完整数字，不从副标题猜集号。
        var title = match.EpisodeTitle.Trim();
        var length = 0;
        while (length < title.Length && !char.IsWhiteSpace(title[length])) length++;
        var number = title[..length];
        return number.Length is > 0 and <= 6 && number.All(char.IsAsciiDigit)
            && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var episode)
            && episode > 0 && episode == item.IndexNumber
                ? match : null;
    }
}

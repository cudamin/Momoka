using System.Globalization;
using Momoka.Infrastructure;

namespace Momoka.Emby;

/// <summary>
/// 悬停预览面板上那几行字（2026-11-12「鼠标停在封面上一秒之后触发」的商店式预览）。
/// <para>
/// 面板只补卡片本身没有写出来的东西：卡片下面是片名和一行副标题，这里给年份、时长、分级、题材、
/// 评分、概要和续播位置。所有取值都来自列表接口已经带回来的字段，不为一张预览另发一次请求 ——
/// 悬停不该联网，也不该在用户只是路过时改变服务器上的任何东西。
/// </para>
/// <para>
/// 每条都返回空串而不是 null：调用方一律「空串就收起那一行」，和 <see cref="LibraryCardText"/> 同一种
/// 写法。数字与单位都走 <see cref="TimeFormat"/>，不在这里另写一套「几分钟」。
/// </para>
/// </summary>
public static class CardPreviewText
{
    /// <summary>
    /// 年份、时长、分级，用项目那套「  ·  」连起来。时长对剧集写「每集 24 分钟」—— 一部剧自己的
    /// <c>RunTimeTicks</c> 是单集时长，不写「每集」会被读成整部剧只有 24 分钟（与媒体库表格同一条规则）。
    /// </summary>
    public static string Meta(EmbyItem item)
    {
        var parts = new List<string>(3);
        if (item.CardSubtitle is { Length: > 0 } subtitle) parts.Add(subtitle);
        if (LibraryCardText.Runtime(item) is { Length: > 0 } runtime) parts.Add(runtime);
        if (item.OfficialRating is { Length: > 0 } official) parts.Add(official);
        return string.Join("  ·  ", parts);
    }

    /// <summary>题材，最多三个；服务器没给就是空串。</summary>
    public static string Genres(EmbyItem item) => string.Join("  ·  ",
        item.Genres.Where(genre => !string.IsNullOrWhiteSpace(genre)).Take(3));

    /// <summary>评分，一位小数。0 与缺失都当作「没有评分」，与卡片角上那颗读数的判据一致。</summary>
    public static string Rating(EmbyItem item) =>
        item.CommunityRating is > 0 and var score
            ? score.ToString("0.0", CultureInfo.InvariantCulture)
            : "";

    /// <summary>概要，去掉首尾空白；没有就是空串。</summary>
    public static string Overview(EmbyItem item) => item.Overview?.Trim() ?? "";

    /// <summary>
    /// 「12:34 / 29:10」—— 已看到哪里、全长多少。只在真的有续播位置时写；看完和没看过都不写，
    /// 判据与卡片底下那条进度线同一条（<see cref="EmbyItem.HasResumePosition"/>）。
    /// </summary>
    public static string Resume(EmbyItem item) => item.HasResumePosition
        ? $"{TimeFormat.Clock(item.ResumeTicks)} / {TimeFormat.Clock(item.RunTimeTicks)}"
        : "";

    /// <summary>已看百分比，0～100；没有续播位置时是 0。</summary>
    public static double Progress(EmbyItem item) =>
        item.HasResumePosition ? Math.Round(item.ProgressFraction * 100, 0) : 0;
}

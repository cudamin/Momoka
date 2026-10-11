using System.Globalization;
using System.Text.RegularExpressions;

namespace Momoka.MoviePilot;

/// <summary>
/// 在资源标题／描述里认「某一集」的写法。集的版本搜索（<see cref="MoviePilotVersionQuery"/>）把集号从搜索词里
/// 拿掉了 —— 「剧名 S01 E05」这种词太具体，站点常搜空，改发「剧名 年份 S01」把范围放到季 —— 结果范围变大，
/// 靠这里把目标集认回来，给 <see cref="MoviePilotResourceFilter"/> 按集筛选。认的写法：
/// <c>S01E05</c>／<c>E05</c>／<c>EP05</c>、<c>第5集</c>／<c>第05集</c>、汉字数字的<c>第五集</c>／<c>第十二集</c>。
/// <para>
/// 数字按整段读完再比数值 —— <c>E512</c> 不是第 5 集，<c>S01E51</c> 也不是。季包、全集合集标题里没有集号，
/// 会被按集的筛选滤掉；要看回它们，清掉筛选就行（版本窗口的「全部集」）。
/// </para>
/// </summary>
public static partial class MoviePilotEpisodePattern
{
    /// <summary>E05／EP05（含「S01E05」里那段）。E 前面要么不是字母数字（开头、点、空格、括号），要么是
    /// 「S01」这种季号，要么紧跟在上一段 E 号后面（「E05E06」这种连写打包）；挡掉「HEVC」「PART5」「4KE05」
    /// 这类把 E 和数字凑在一起的巧合。</summary>
    [GeneratedRegex(@"(?:(?<![0-9A-Za-z])|(?<=[sS]\d{1,4})|(?<=[eE]\d{1,4}))[eE][pP]?(?<number>\d{1,4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Latin();

    /// <summary>第5集／第05集，允许中间夹空格。</summary>
    [GeneratedRegex(@"第\s*(?<number>\d{1,4})\s*集", RegexOptions.CultureInvariant)]
    private static partial Regex ArabicCJK();

    /// <summary>第五集／第十二集／第一百零五集 —— 汉字数字写法。</summary>
    [GeneratedRegex(@"第\s*(?<chinese>[零〇一二三四五六七八九十百千]+)\s*集", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseCJK();

    public static bool Contains(string? text, int episode)
    {
        if (text is null || episode < 0) return false;
        foreach (Match match in Latin().Matches(text))
            if (int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) == episode) return true;
        foreach (Match match in ArabicCJK().Matches(text))
            if (int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) == episode) return true;
        foreach (Match match in ChineseCJK().Matches(text))
            if (ChineseNumber(match.Groups["chinese"].Value) == episode) return true;
        return false;
    }

    /// <summary>汉字数字换算成数值，只认集号会用到的 1..9999。带单位的按十进制位展开（十二=12、
    /// 一百零五=105）；没有单位的只放行单个字（第五集），「第二三集」这种罗列不猜，整体放弃。</summary>
    private static int? ChineseNumber(string text)
    {
        var section = 0;
        var pending = 0;
        var sawUnit = false;
        foreach (var character in text)
        {
            if (Digit(character) is { } digit) pending = digit;
            else
            {
                if (Unit(character) is not { } unit) return null;
                sawUnit = true;
                section += (pending == 0 ? 1 : pending) * unit;
                pending = 0;
            }
        }
        if (!sawUnit && text.Length != 1) return null;
        var value = section + pending;
        return value is > 0 and <= 9999 ? value : null;
    }

    private static int? Digit(char character) => character switch
    {
        '零' or '〇' => 0,
        '一' => 1,
        '二' => 2,
        '三' => 3,
        '四' => 4,
        '五' => 5,
        '六' => 6,
        '七' => 7,
        '八' => 8,
        '九' => 9,
        _ => null
    };

    private static int? Unit(char character) => character switch
    {
        '十' => 10,
        '百' => 100,
        '千' => 1000,
        _ => null
    };
}

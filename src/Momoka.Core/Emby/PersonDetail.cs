using System.Globalization;

namespace Momoka.Emby;

/// <summary>
/// One line of 影视作品: the item, its year, its title, and what this person did on it.
/// <para>
/// A value object rather than the raw <see cref="EmbyItem"/> because the credit line is not a property of
/// the item — it is a property of the pair (item, person). 同一个人在一部片子里有两种署名时，屏上要
/// 说的是这一种署名，而不是条目自己那一排 People 的第一个。
/// </para>
/// </summary>
public sealed record PersonCreditRow(EmbyItem Item, string Year, string Title, string Role);

/// <summary>
/// 人物页上那几个不由服务器排版、而由这一页自己决定的字与行：出生/逝世、出生地、数据库链接、影视作品
/// 分组标题和每一行的署名。
/// <para>
/// 和 <see cref="ItemDetail"/> 同一个理由放在 Core：这些字符串错起来屏上都看着正常 —— 差一岁的年龄、
/// 把导演写成「扮演」、IMDb 的 name 页拼成 title 页。测试够得到这里，页面只负责把结果摆上去。
/// </para>
/// <para>
/// 这里不碰网络、时钟和机器区域设置：年龄的「今天」由调用方给，日期用
/// <see cref="CultureInfo.InvariantCulture"/> 排，同一台服务器在任何机器上读出来都一样。
/// </para>
/// </summary>
public static class PersonDetail
{
    /// <summary>节目/电影那一排最多摆几张。和 <see cref="ItemDetail.CastLimit"/> 一样是版面数，不是接口上限。</summary>
    public const int RailLimit = 12;

    /// <summary>影视作品每一组先摆几行；更多的那部分交给人物作品网格（和演职人员原来那一格是同一个页面）。</summary>
    public const int FilmographyLimit = 20;

    /// <summary>
    /// 影视作品按什么职务分组，顺序照 Emby 网页端人物页的习惯：演员在前，技术署名在后。
    /// 查询也按这一份逐个问——服务器一次只答一种 <c>PersonTypes</c> 时，返回的每一行都属于这一种。
    /// </summary>
    public static IReadOnlyList<string> CreditTypes { get; } =
        ["Actor", "Director", "Writer", "Producer", "GuestStar", "Composer"];

    /// <summary>
    /// 影视作品里收哪几种条目。单集不进来：它们在「集」那一排里，而且一个人参演的剧集常常有几百条，
    /// 混进影视作品只会把「演过哪几部片子」淹掉。
    /// </summary>
    public static IReadOnlyList<string> FilmographyTypes { get; } =
        [EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Video];

    /// <summary>
    /// 「出生：1994年2月27日（32 岁）」。逝世的人不在这行写年龄——那一句在 <see cref="Death"/> 里，
    /// 两行都写岁数会读成两个人。没有出生日期就返回空，页面整行收起。
    /// </summary>
    public static string Birth(EmbyItem person, DateOnly today)
    {
        if (person.PremiereDate is not { } premiere) return "";

        // 出生日期是日历上的一天，不是某个时刻：按服务器给的日历分量读，不换成这台机器的时区。
        // ToLocalTime 会让 UTC 午夜的生日在美洲变成前一天，年龄也跟着差一岁。
        var born = DateOnly.FromDateTime(premiere.Date);
        if (born.Year < 1800) return "";
        var line = $"出生：{born.Year}年{born.Month}月{born.Day}日";
        if (person.EndDate is not null) return line;

        return Age(born, today) is { } age ? $"{line}（{age} 岁）" : line;
    }

    /// <summary>「逝世：2020年1月1日（享年 55 岁）」。只在这一天确实知道的时候出现。</summary>
    public static string Death(EmbyItem person)
    {
        if (person.EndDate is not { } end) return "";

        var died = DateOnly.FromDateTime(end.Date);
        if (died.Year < 1800) return "";
        var line = $"逝世：{died.Year}年{died.Month}月{died.Day}日";
        if (person.PremiereDate is not { } premiere) return line;

        var born = DateOnly.FromDateTime(premiere.Date);
        return Age(born, died) is { } age ? $"{line}（享年 {age} 岁）" : line;
    }

    /// <summary>
    /// The server's birthplace strings, deduplicated and joined. 空列表返回空串，页面据此收起这一行。
    /// </summary>
    public static string Birthplace(EmbyItem person)
    {
        var places = person.ProductionLocations
            .Select(place => place.Trim())
            .Where(place => place.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return string.Join("、", places);
    }

    /// <summary>
    /// 其他信息那一行：目前只有数据库链接。没有链接就返回空表，整块卡片收起 —— 一行「数据库链接：」
    /// 后面什么都没有，比不显示更让人以为服务器里没有这个人。
    /// </summary>
    public static IReadOnlyList<OtherFact> Facts(EmbyItem person) =>
        Links(person) is { Count: > 0 } links ? [new("数据库链接", links)] : [];

    /// <summary>
    /// 刮削源上这个人的主页。IMDb 的演员/职员页在 <c>/name/</c> 下，TMDB 在 <c>/person/</c> 下 ——
    /// 和影视条目的 <c>/title/</c>、<c>/tv/</c> 不是同一条路，错拼过去会得到一页「找不到」。
    /// </summary>
    public static IReadOnlyList<OtherFactValue> Links(EmbyItem person)
    {
        var links = new List<OtherFactValue>(2);

        if (ItemDetail.ProviderId(person, "Imdb") is { Length: > 0 } imdb)
            links.Add(new("IMDb", "https://www.imdb.com/name/" + Uri.EscapeDataString(imdb) + "/"));

        if (ItemDetail.ProviderId(person, "Tmdb") is { Length: > 0 } tmdb)
            links.Add(new("TMDB", "https://www.themoviedb.org/person/" + Uri.EscapeDataString(tmdb)));

        return links;
    }

    /// <summary>一种署名的中文说法；未知类型原样返回，不猜成「演员」。</summary>
    public static string CreditLabel(string? type) => EmbyPerson.CreditName(type);

    /// <summary>影视作品一组的标题：「演员 (156)」。</summary>
    public static string Heading(string? type, int total) =>
        $"{CreditLabel(type)} ({Math.Max(0, total).ToString(CultureInfo.InvariantCulture)})";

    /// <summary>
    /// 一行影视作品。署名优先用这一条目的 <c>People</c> 里这个人的那一条——列表响应只有点名
    /// <c>Fields=People</c> 才带它；没带时退回这一组问的职务。
    /// </summary>
    public static PersonCreditRow Credit(EmbyItem item, string personId, string? groupType)
    {
        var credit = item.People.FirstOrDefault(person =>
                personId.Length > 0 && string.Equals(person.Id, personId, StringComparison.Ordinal))
            ?? item.People.FirstOrDefault(person =>
                groupType is { Length: > 0 } && string.Equals(person.Type, groupType, StringComparison.OrdinalIgnoreCase));

        var type = credit?.Type is { Length: > 0 } creditType ? creditType : groupType;
        var role = credit?.Role?.Trim();
        var acting = type is "Actor" or "GuestStar";

        var text = acting && role is { Length: > 0 } ? $"扮演 {role}" : CreditLabel(type);
        if (!acting && role is { Length: > 0 }) text = text.Length == 0 ? role : $"{text} · {role}";

        return new PersonCreditRow(item, Year(item), item.Name.Trim(), text);
    }

    /// <summary>「2026」；没有年份就是空串，那一格在行里收起而不是写 0。</summary>
    public static string Year(EmbyItem item)
    {
        if (item.ProductionYear is { } year and > 0)
            return year.ToString(CultureInfo.InvariantCulture);

        return item.PremiereDate is { } premiere
            ? premiere.Year.ToString(CultureInfo.InvariantCulture)
            : "";
    }

    /// <summary>
    /// Whole years from <paramref name="born"/> to <paramref name="until"/>; null when the dates do not
    /// describe a life (a server sometimes sends a placeholder 0001-01-01 or a death before birth).
    /// <para>
    /// 按整年算，不是「今年减出生年」：没到生日的那一年要减一岁，否则一个 2 月 27 日出生的人在 1 月
    /// 就被写大一岁。2 月 29 日出生的人在平年按 3 月 1 日算（<see cref="DateOnly.AddYears"/> 的规则），
    /// 与常见的年龄算法一致。
    /// </para>
    /// </summary>
    public static int? Age(DateOnly born, DateOnly until)
    {
        if (born.Year < 1800 || until < born) return null;

        var age = until.Year - born.Year;
        if (until < born.AddYears(age)) age--;
        return age >= 0 ? age : null;
    }
}

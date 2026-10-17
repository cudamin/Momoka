using System.Globalization;
using Momoka.Infrastructure;

namespace Momoka.Emby;

/// <summary>
/// Emby 网页端「设置 › 图像大小」的八档。名字照抄网页端，`Default` 与 `Normal` 画出来一样宽 ——
/// 网页端那一档的意思是「跟随应用默认」，这个客户端没有第二套默认，所以两者同值。
/// </summary>
public enum LibraryCardSize
{
    ExtraSmall,
    Smaller,
    Small,
    Normal,
    Default,
    Large,
    Larger,
    ExtraLarge
}

/// <summary>
/// Emby 网页端「设置 › 显示字段」的十四项，顺序与网页端一致。
/// <para>
/// 每一项都对应服务器 <c>ItemFields</c> 里的一个名字（见 <see cref="LibraryDisplayCatalog"/>）；
/// 网页端没给、服务器也不返回的（比如「类型」之外的条目属性）不在这里 —— 官方接口没有的就不做。
/// </para>
/// </summary>
public enum LibraryField
{
    Title,
    CommunityRating,
    CriticRating,
    OfficialRating,
    ProductionYear,
    Runtime,
    Genres,
    Director,
    Tags,
    Studios,
    Tagline,
    Overview,
    PremiereDate,
    DateCreated
}

/// <summary>一项显示字段：界面上的名字，以及向服务器要它时要写进 <c>Fields</c> 的那个词。</summary>
/// <param name="Field">字段本身。</param>
/// <param name="Label">网页端那一行的中文名。</param>
/// <param name="RequestField">
/// 请求 <c>Fields=</c> 时要带的名字；<see langword="null"/> 表示这一项不需要单独要 ——
/// 标题的 <c>Name</c> 与播放时长的 <c>RunTimeTicks</c> 是 Emby 每条都给的。
/// </param>
public sealed record LibraryFieldInfo(LibraryField Field, string Label, string? RequestField);

/// <summary>
/// 网页端媒体库设置里的那三组东西：视图、图像大小、分组、显示字段。
/// <para>
/// **按库记住**，与排序、筛选同一个键（<see cref="LibraryBrowse"/> 的库根）。电视剧库和电影库是两份设置：
/// 一个库开在海报、显示年份与播出平台，另一个库开在列表、显示片长与合集，互不影响。
/// </para>
/// <para>
/// 这个类型进 <c>settings.json</c>，所以属性都是可读写的公开属性，枚举按整数存（与
/// <c>UiSettings.Views</c> 同一规矩）。<see cref="LibraryView"/> 的新形状只往后加，旧值不会改义。
/// </para>
/// </summary>
public sealed class LibraryDisplay
{
    public LibraryView View { get; set; } = LibraryView.Poster;

    public LibraryCardSize CardSize { get; set; } = LibraryCardSize.Default;

    /// <summary>
    /// 「将项目分组到合集中」—— 发 <c>GroupItemsIntoCollections</c>。网页端默认开着。
    /// </summary>
    public bool GroupIntoCollections { get; set; } = true;

    /// <summary>这一面墙上每张卡片底下要显示哪些字段。</summary>
    public List<LibraryField> Fields { get; set; } = [.. LibraryDisplayCatalog.DefaultFields];

    public LibraryDisplay Clone() => new()
    {
        View = View,
        CardSize = CardSize,
        GroupIntoCollections = GroupIntoCollections,
        Fields = [.. Fields]
    };

    /// <summary>
    /// 认不出的值丢掉、重复的去掉、顺序按目录走。settings.json 是手也能改的文件，一份认不出的字段
    /// 不该让整面墙退回默认，也不该在每张卡片上留一个空段。
    /// </summary>
    public LibraryDisplay Normalized()
    {
        var known = Fields.Where(LibraryDisplayCatalog.Knows).Distinct().ToHashSet();
        return new LibraryDisplay
        {
            View = Enum.IsDefined(View) ? View : LibraryView.Poster,
            CardSize = Enum.IsDefined(CardSize) ? CardSize : LibraryCardSize.Default,
            GroupIntoCollections = GroupIntoCollections,
            Fields = [.. LibraryDisplayCatalog.Fields.Select(info => info.Field).Where(known.Contains)]
        };
    }
}

/// <summary>网页端设置面板里的名字表，以及从这些设置推出来的查询参数。</summary>
public static class LibraryDisplayCatalog
{
    /// <summary>十四项显示字段，顺序照网页端「显示字段」那一列。</summary>
    public static IReadOnlyList<LibraryFieldInfo> Fields { get; } =
    [
        new(LibraryField.Title, "标题", null),
        new(LibraryField.CommunityRating, "IMDb 评分", "CommunityRating"),
        new(LibraryField.CriticRating, "影评人评分", "CriticRating"),
        new(LibraryField.OfficialRating, "家长评分", "OfficialRating"),
        new(LibraryField.ProductionYear, "年份", "ProductionYear"),
        new(LibraryField.Runtime, "播放时长", null),
        new(LibraryField.Genres, "类型", "Genres"),
        new(LibraryField.Director, "导演", "People"),
        new(LibraryField.Tags, "标签", "Tags"),
        new(LibraryField.Studios, "工作室", "Studios"),
        new(LibraryField.Tagline, "宣传语", "Taglines"),
        new(LibraryField.Overview, "概要", "Overview"),
        new(LibraryField.PremiereDate, "播放日期", "PremiereDate"),
        new(LibraryField.DateCreated, "创建日期", "DateCreated")
    ];

    /// <summary>
    /// 网页端的默认勾选：标题一项。这个客户端原来卡片底下就有一行年份，多留一项年份是为了不把
    /// 已有的版面悄悄改掉；两行字里「标题 + 年份」也是网页端最常见的组合。
    /// </summary>
    public static IReadOnlyList<LibraryField> DefaultFields { get; } =
        [LibraryField.Title, LibraryField.ProductionYear];

    public static bool Knows(LibraryField field) => Enum.IsDefined(field);

    public static LibraryFieldInfo Info(LibraryField field) =>
        Fields.First(info => info.Field == field);

    public static string LabelOf(LibraryField field) => Info(field).Label;

    /// <summary>
    /// 这组字段要写进 <c>Fields=</c> 的名字，逗号分隔，按目录顺序。
    /// <para>
    /// 只问勾上的：导演要 <c>People</c>（一份演职人员名单，一条能有几十号人），概要要 <c>Overview</c>
    /// （几百字），全都常备着会让每一页都白拖几十 KB。一条都不需要时给 <see cref="EmbyFields.Browse"/>
    /// 保底的那几样 —— 卡片总要有图、有年份、有类型才知道自己该画什么。
    /// </para>
    /// </summary>
    public static string RequestFields(IEnumerable<LibraryField> fields)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
            if (Info(field).RequestField is { } name)
                names.Add(name);

        // PrimaryImageAspectRatio 不是可勾的字段，但卡片的宽高比要靠它；它一直是列表请求的底座。
        names.Add("PrimaryImageAspectRatio");
        return string.Join(',', names);
    }

    /// <summary>八档图像大小各自的缩放，以默认档为 1。</summary>
    public static double Scale(LibraryCardSize size) => size switch
    {
        LibraryCardSize.ExtraSmall => 0.65,
        LibraryCardSize.Smaller => 0.76,
        LibraryCardSize.Small => 0.88,
        LibraryCardSize.Large => 1.18,
        LibraryCardSize.Larger => 1.35,
        LibraryCardSize.ExtraLarge => 1.6,
        _ => 1.0
    };

    public static string LabelOf(LibraryCardSize size) => size switch
    {
        LibraryCardSize.ExtraSmall => "超小",
        LibraryCardSize.Smaller => "特小",
        LibraryCardSize.Small => "小",
        LibraryCardSize.Normal => "中",
        LibraryCardSize.Default => "默认",
        LibraryCardSize.Large => "大",
        LibraryCardSize.Larger => "特大",
        LibraryCardSize.ExtraLarge => "超大",
        _ => "默认"
    };

    /// <summary>七种视图的中文名，与网页端 <c>viewsettings</c> 的七项一一对应。</summary>
    public static string LabelOf(LibraryView view) => view switch
    {
        LibraryView.Banner => "横幅图",
        LibraryView.Disc => "光盘封面",
        LibraryView.Logo => "徽标",
        LibraryView.Thumb => "缩略图",
        LibraryView.List => "列表",
        LibraryView.Table => "表格",
        _ => "海报"
    };

    /// <summary>网页端「图像大小」说明的那一行字。</summary>
    public const string ImageSizeHelp = "设置图像网格的首选尺寸。";

    /// <summary>网页端「将项目分组到合集中」下面那一行说明。</summary>
    public const string GroupIntoCollectionsHelp =
        "根据已添加到的合集对项目进行分组。启用某些筛选器或排序命令可能会自动禁用此功能。";
}

/// <summary>
/// 一张卡片画多宽、多高。形状与「图像大小」一起决定这两个数 —— 解码宽度就是画出来的宽度，
/// 所以这里算错一档，卡片要么糊要么白占内存。
/// </summary>
public static class LibraryCardMetrics
{
    /// <summary>
    /// 默认档的宽度：海报 170（<see cref="CardSize.PosterWidth"/>），宽卡 300
    /// （<see cref="CardSize.WideWidth"/>），其余档按 <see cref="LibraryDisplayCatalog.Scale"/> 缩放。
    /// </summary>
    public static int Width(LibraryView view, LibraryCardSize size)
    {
        var scale = LibraryDisplayCatalog.Scale(size);
        var baseWidth = view switch
        {
            LibraryView.Poster or LibraryView.Disc => CardSize.PosterWidth,
            _ => CardSize.WideWidth
        };
        return Math.Max(80, (int)Math.Round(baseWidth * scale));
    }

    /// <summary>
    /// 形状的高宽比：海报 2:3、光盘 1:1、横幅 1000:185（Emby 的横幅图就是那个比例）、
    /// 徽标与缩略图 16:9。列表与表格的图只是行首那一小张，同样按 16:9 算。
    /// </summary>
    public static double Height(LibraryView view, int width) => view switch
    {
        LibraryView.Poster => Math.Round(width * 1.5),
        LibraryView.Disc => width,
        LibraryView.Banner => Math.Round(width * 0.185),
        _ => Math.Round(width * 9.0 / 16.0)
    };
}

/// <summary>
/// 表格视图每一列多宽。列头与每一行读的是同一份数字 —— 两处各写一遍，第一列就会歪。
/// 宽度按内容的长短给：年份、评分这类是一小格，标题与概要最宽。
/// </summary>
public static class LibraryTableColumns
{
    public static double WidthOf(LibraryField field) => field switch
    {
        LibraryField.Title => 260,
        LibraryField.CommunityRating or LibraryField.CriticRating or LibraryField.OfficialRating => 88,
        LibraryField.ProductionYear => 80,
        LibraryField.Runtime => 110,
        LibraryField.Genres => 190,
        LibraryField.Director => 160,
        LibraryField.Tags => 160,
        LibraryField.Studios => 130,
        LibraryField.Tagline => 200,
        LibraryField.Overview => 320,
        LibraryField.PremiereDate or LibraryField.DateCreated => 100,
        _ => 120
    };
}

/// <summary>
/// 卡片底下那两行字：一行是勾上的字段，另一行是宣传语或概要。
/// <para>
/// **电视剧与电影不是同一套说法**：一部剧的「年份」是一个区间（2017 – 2026）、「播放时长」是每集多长，
/// 一部电影的年份是一个数、时长是整片。所以这里按 <see cref="EmbyItem.Type"/> 分支，而不是把两种库
/// 当成一种东西套同一段格式。
/// </para>
/// </summary>
public static class LibraryCardText
{
    /// <summary>勾上的字段拼成的一行，空字符串表示这一行不画。</summary>
    public static string Line(EmbyItem item, IReadOnlyList<LibraryField> fields)
    {
        var parts = new List<string>(fields.Count);
        foreach (var field in fields)
            if (Segment(item, field) is { Length: > 0 } text)
                parts.Add(text);
        return string.Join("  ·  ", parts);
    }

    /// <summary>宣传语优先，其次概要；两样都没勾就是 null（那一行整行收起）。</summary>
    public static string? Note(EmbyItem item, IReadOnlyList<LibraryField> fields)
    {
        if (fields.Contains(LibraryField.Tagline) && item.Taglines.FirstOrDefault() is { Length: > 0 } tagline)
            return tagline;
        if (fields.Contains(LibraryField.Overview) && item.Overview is { Length: > 0 } overview)
            return overview;
        return null;
    }

    /// <summary>表格视图的一格：这一项字段在这条条目上的文字。标题那一格就是条目名本身。</summary>
    public static string Cell(EmbyItem item, LibraryField field) =>
        field == LibraryField.Title ? item.Name : Segment(item, field);

    private static string Segment(EmbyItem item, LibraryField field) => field switch
    {
        // 「标题」在卡片上是上面那一行，不进这一行；只有单集例外 —— 一集的名字在「集」那一页上认不出是第几集，
        // 所以把「S01E02」这一段放在最前面，与详情页的单集列表同一个说法。
        LibraryField.Title => item.Type == EmbyItemType.Episode ? item.EpisodeCode : "",
        LibraryField.CommunityRating => item.CommunityRating is { } community and > 0
            ? community.ToString("0.0#", CultureInfo.InvariantCulture)
            : "",
        LibraryField.CriticRating => item.CriticRating is { } critic and > 0
            ? critic.ToString(CultureInfo.InvariantCulture) + "%"
            : "",
        LibraryField.OfficialRating => item.OfficialRating ?? "",
        LibraryField.ProductionYear => Year(item),
        LibraryField.Runtime => Runtime(item),
        LibraryField.Genres => Short(item.Genres, 2, " / "),
        LibraryField.Director => Short(Directors(item), 2, " / "),
        LibraryField.Tags => Short(item.Tags, 3, " / "),
        LibraryField.Studios => Short(item.Studios.Select(studio => studio.Name).ToList(), 1, " / "),
        LibraryField.PremiereDate => Date(item.PremiereDate),
        LibraryField.DateCreated => Date(item.DateCreated),
        _ => ""
    };

    /// <summary>
    /// 年份：剧集是区间（还在播就是「至今」），电影是那一个数。区间这一段直接借详情页那个函数，
    /// 两处说的是同一件事，不该各算一遍。
    /// </summary>
    private static string Year(EmbyItem item)
    {
        if (item.Type is EmbyItemType.Series)
            return string.Equals(item.Status, "Continuing", StringComparison.OrdinalIgnoreCase)
                && item.ProductionYear is { } start and > 0
                    ? $"{start.ToString(CultureInfo.InvariantCulture)} – 至今"
                    : ItemDetail.Years(item);

        if (item.ProductionYear is { } year and > 0) return year.ToString(CultureInfo.InvariantCulture);
        return item.PremiereDate is { } premiere
            ? premiere.ToLocalTime().Year.ToString(CultureInfo.InvariantCulture)
            : "";
    }

    /// <summary>
    /// 时长：电影是整片，剧集是「每集 45 分钟」—— 一部剧自己的 <c>RunTimeTicks</c> 是单集时长，
    /// 不写「每集」两个字，读起来就成了这部剧只有 45 分钟。
    /// </summary>
    private static string Runtime(EmbyItem item)
    {
        var duration = TimeFormat.Duration(item.RunTimeTicks);
        if (duration.Length == 0) return "";
        return item.Type is EmbyItemType.Series ? $"每集 {duration}" : duration;
    }

    private static string Date(DateTimeOffset? value) =>
        value is { } moment ? moment.ToLocalTime().ToString("yyyy/M/d", CultureInfo.InvariantCulture) : "";

    private static IReadOnlyList<string> Directors(EmbyItem item) =>
        [.. item.People
            .Where(person => string.Equals(person.Type, "Director", StringComparison.OrdinalIgnoreCase))
            .Select(person => person.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))];

    private static string Short(IReadOnlyList<string> values, int take, string separator) =>
        string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(take));
}

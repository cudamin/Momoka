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
    DateCreated,

    /// <summary>下面这八项在「文件夹」与「集」的卡片视图出现 —— 网页端那一页列的是文件本身。</summary>
    Filename,
    Container,
    Resolution,
    Bitrate,
    FileSize,
    VideoCodec,
    Framerate,
    AudioCodec,

    /// <summary>下面这两项只在「集」那一页的表格视图出现。</summary>
    SeriesName,
    EpisodeNumber
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
            Fields = [.. LibraryDisplayCatalog.CanonicalOrder.Where(known.Contains)]
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
    /// 文件本身的八项，只出现在「文件夹」那一页。名字取自网页端那一页的设置面板；<c>MediaSources</c>
    /// 一次带回容器、大小、码率与两条流的编解码器，八项里除文件名（从 <c>Path</c> 取）之外都从它读。
    /// </summary>
    public static IReadOnlyList<LibraryFieldInfo> FileFields { get; } =
    [
        new(LibraryField.Filename, "文件名", "Path"),
        new(LibraryField.Container, "媒体容器", "MediaSources"),
        new(LibraryField.Resolution, "分辨率", "MediaSources"),
        new(LibraryField.Bitrate, "比特率", "MediaSources"),
        new(LibraryField.FileSize, "文件尺寸", "MediaSources"),
        new(LibraryField.VideoCodec, "视频编解码器", "MediaSources"),
        new(LibraryField.Framerate, "帧率", "MediaSources"),
        new(LibraryField.AudioCodec, "音频编解码器", "MediaSources")
    ];

    /// <summary>
    /// 「文件夹」那一页的字段表：顺序照网页端，文件名紧跟标题，评分那一项在那里的名字是「社区评分」
    /// （同一份 <see cref="LibraryField.CommunityRating"/>，只是这一页叫法不同），文件那几项排在后面。
    /// </summary>
    private static readonly IReadOnlyList<LibraryFieldInfo> FolderFields =
    [
        new(LibraryField.Title, "标题", null),
        new(LibraryField.Filename, "文件名", "Path"),
        new(LibraryField.CommunityRating, "社区评分", "CommunityRating"),
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
        new(LibraryField.Container, "媒体容器", "MediaSources"),
        new(LibraryField.Resolution, "分辨率", "MediaSources"),
        new(LibraryField.Bitrate, "比特率", "MediaSources"),
        new(LibraryField.FileSize, "文件尺寸", "MediaSources"),
        new(LibraryField.PremiereDate, "播放日期", "PremiereDate"),
        new(LibraryField.DateCreated, "创建日期", "DateCreated"),
        new(LibraryField.VideoCodec, "视频编解码器", "MediaSources"),
        new(LibraryField.Framerate, "帧率", "MediaSources"),
        new(LibraryField.AudioCodec, "音频编解码器", "MediaSources")
    ];

    /// <summary>
    /// 「集」那一页多出的两项。网页端 <c>getAvailableFields</c> 的 Episode 那一支先 push 剧名再 push 集号，
    /// 位置在标题前面，这里照那个顺序；中文名取网页端那两列的译名（Series／Number）。
    /// </summary>
    private static readonly IReadOnlyList<LibraryFieldInfo> EpisodeOnly =
    [
        new(LibraryField.SeriesName, "所属剧集", "SeriesName"),
        // 集号不用单独要：IndexNumber 与 ParentIndexNumber 是每条都给的。
        new(LibraryField.EpisodeNumber, "集号", null)
    ];

    /// <summary>「集」的卡片字段与文件夹相同，但不包含宣传语；剧名和集号只在表格中单列。</summary>
    public static IReadOnlyList<LibraryFieldInfo> EpisodeFields { get; } =
        [.. FolderFields.Where(info => info.Field != LibraryField.Tagline)];

    /// <summary>
    /// 索引页（类型、播出平台）的字段表：**只有标题**。
    /// <para>
    /// 网页端这两种条目在 <c>baseitemcontroller.getAvailableFields</c> 里各有一支，但两支都只 <c>push</c>
    /// 一个 <c>SortName</c>，而那一项标着 <c>viewTypes:"datagrid"</c>（只在表格视图里出现）—— 于是卡片视图的
    /// 「显示字段」只剩「标题」一项。用户给的那张网页端设置截图也是这一行。这一边照那个结果做，而不是照那句
    /// 「没有专属分支」的解释（2026-10-18 复核过网页端源码，原先的注释写错了理由）。
    /// </para>
    /// <para>
    /// 卡片底下也只写这一个名字：一个类型没有年份、评分、导演可言，勾了也不会有值。
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<LibraryFieldInfo> IndexFields =
    [
        new(LibraryField.Title, "标题", null)
    ];

    public static LibraryDisplay DefaultFor(LibrarySection section, LibraryIndexKind index = LibraryIndexKind.None) => new()
    {
        // 索引页没有「合集」这回事：它列的是类型名与台名，不是条目。
        GroupIntoCollections = index == LibraryIndexKind.None
            && section is not (LibrarySection.Episodes or LibrarySection.Folders),
        Fields = index != LibraryIndexKind.None ? [LibraryField.Title]
            : section == LibrarySection.Episodes ? [LibraryField.Title]
            : [.. DefaultFields]
    };

    /// <summary>三张表合起来的全部字段；认名字、排规范顺序都走这一份。</summary>
    private static IReadOnlyList<LibraryFieldInfo> All { get; } = [.. Fields, .. FileFields, .. EpisodeOnly];

    /// <summary>这一页的字段表。文件夹那一页多出文件本身那几项，集那一页多出剧名与集号，两张索引页只有标题，其余各页是同一份十四项。</summary>
    public static IReadOnlyList<LibraryFieldInfo> FieldsFor(LibrarySection section, LibraryView view = LibraryView.Poster,
        LibraryIndexKind index = LibraryIndexKind.None)
    {
        if (index != LibraryIndexKind.None) return IndexFields;

        return section switch
        {
            LibrarySection.Folders => FolderFields,
            LibrarySection.Episodes when view == LibraryView.Table => [.. EpisodeOnly, .. EpisodeFields],
            LibrarySection.Episodes => EpisodeFields,
            _ => Fields
        };
    }

    /// <summary>
    /// 这一页的字段表里有没有这一项。文件夹页的「文件名」、集页的「集号」在别的页上没有开关可以关掉，
    /// 放它们过去就等于凭空多出一段关不掉的字。
    /// </summary>
    public static bool Applies(LibraryField field, LibrarySection section, LibraryView view = LibraryView.Poster,
        LibraryIndexKind index = LibraryIndexKind.None) =>
        FieldsFor(section, view, index).Any(info => info.Field == field);

    /// <summary>
    /// 存下来的那一份收成这一页真正要画的那几项，顺序照这一页的表 —— 表格的列、卡片那一行、请求里的
    /// <c>Fields</c> 都读它，三处因此不会各排一套。别的页独有的字段留在这份记录里但不生效：换回那一页
    /// 还在，开关也还在。
    /// </summary>
    public static IReadOnlyList<LibraryField> Applied(IEnumerable<LibraryField> fields, LibrarySection section,
        LibraryView view = LibraryView.Poster, LibraryIndexKind index = LibraryIndexKind.None)
    {
        var wanted = new HashSet<LibraryField>(fields);
        return [.. FieldsFor(section, view, index).Select(info => info.Field).Where(wanted.Contains)];
    }

    /// <summary>
    /// 网页端的默认勾选：标题一项。这个客户端原来卡片底下就有一行年份，多留一项年份是为了不把
    /// 已有的版面悄悄改掉；两行字里「标题 + 年份」也是网页端最常见的组合。
    /// </summary>
    public static IReadOnlyList<LibraryField> DefaultFields { get; } =
        [LibraryField.Title, LibraryField.ProductionYear];

    public static bool Knows(LibraryField field) => Enum.IsDefined(field);

    public static LibraryFieldInfo Info(LibraryField field) =>
        All.First(info => info.Field == field);

    /// <summary>所有字段的规范顺序（默认十四项在前，文件八项与单集两项在后），存下来的那一份按它排。</summary>
    public static IReadOnlyList<LibraryField> CanonicalOrder { get; } =
        [.. All.Select(info => info.Field)];

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
/// 索引页那两张卡片的形状。两份差别不是审美，是服务器给这两种条目存的图本来就不一样，而图不能裁错：
/// <list type="bullet">
/// <item>「类型」的图是服务器按这个类型下的条目拼出来的**四格封面**（1:1，本机实测 4.10.1.0 上 22 个类型里
/// 8 个有图、宽高比全是 1），所以卡片是方的 —— 按竖卡裁会切掉四格里的两格，按宽卡裁会切掉上下两半。
/// 网页端也是这么算的：<c>imagehelper.getShape</c> 取这一页那些图的高宽比的众数，方图落在「方」那一档。</item>
/// <item>「播出平台」的图是**台标**，服务器存在 <c>Thumb</c> 里（本机 134 个台里 23 个有，全是 16:9）。
/// 这一页**网页端没有先例**（4.10.1.0 的分类栏里没有播出平台这一格，只有类型）—— 16:9 的宽卡与「留边不裁」
/// 是这个客户端的产品决定：台标是一块字形，铺在方格里四边全是空白，铺在 16:9 的格子里才占得住。</item>
/// </list>
/// <para>
/// 没有图的那一张照样占同样的形状：网格是一行一行读的，缺图的格子忽大忽小比留一个占位更难看。
/// </para>
/// </summary>
public static class LibraryIndexCards
{
    /// <summary>卡片里那张图的高宽比（高 ÷ 宽）：类型 1，播出平台 9/16。</summary>
    public static double Aspect(LibraryIndexKind index) => index switch
    {
        LibraryIndexKind.Genre => 1,
        LibraryIndexKind.Studio => 9.0 / 16.0,
        _ => 1
    };

    /// <summary>
    /// 取图的先后。类型的图就是它自己的 <c>Primary</c>；播出平台的台标存在 <c>Thumb</c>，退一步再要是
    /// <c>Primary</c>（本机实测：23 个有图的台全在 Thumb 里，Primary 一个都没有）。
    /// </summary>
    public static IReadOnlyList<string> ImageTypes(LibraryIndexKind index) => index switch
    {
        LibraryIndexKind.Genre => [EmbyImageStore.Primary],
        LibraryIndexKind.Studio => [EmbyImageStore.Thumb, EmbyImageStore.Primary],
        _ => []
    };

    /// <summary>
    /// 这一页的图要不要「装进去」（留边、不裁）而不是「铺满」。只有台标要 —— 它是画在透明底上的一块字形，
    /// 铺满会把字切掉；四格封面本来就是一张画满的图，铺满才不留边。
    /// </summary>
    public static bool ContainArtwork(LibraryIndexKind index) => index == LibraryIndexKind.Studio;

    /// <summary>
    /// 卡片底下那一行字要留的高度。索引卡只有一个居中的名字（没有第二行、没有宣传语），所以比别处那
    /// 两行加一行的 <see cref="Momoka.Infrastructure.CardSize.Chrome"/>（59）矮一大截 —— 照 59 排，
    /// 每张卡底下会空出三十多像素，一行一行读下来是「每隔一张空一行」。
    /// </summary>
    public const int TextHeight = 28;
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
    /// <para>
    /// 索引页按 <see cref="LibraryIndexKind"/> 换基准：类型是方卡（基准同海报 170），播出平台是 16:9 宽卡
    /// （基准同剧照 300）。「图像大小」那一档照旧缩放，所以两页各有各的八档。
    /// </para>
    /// </summary>
    public static int Width(LibraryView view, LibraryCardSize size, LibrarySection section = LibrarySection.Items,
        LibraryIndexKind index = LibraryIndexKind.None)
    {
        var scale = LibraryDisplayCatalog.Scale(size);
        var baseWidth = index switch
        {
            LibraryIndexKind.Genre => CardSize.PosterWidth,
            LibraryIndexKind.Studio => CardSize.WideWidth,
            _ => section == LibrarySection.Episodes && view == LibraryView.Poster ? CardSize.WideWidth : view switch
            {
                LibraryView.Poster or LibraryView.Disc => CardSize.PosterWidth,
                _ => CardSize.WideWidth
            }
        };
        return Math.Max(80, (int)Math.Round(baseWidth * scale));
    }

    /// <summary>
    /// 一行里两张卡片之间的那条缝，和 <c>LibraryPage.xaml</c> 里 <c>UniformGridLayout.MinColumnSpacing</c>
    /// 是同一个数。
    /// <para>
    /// 两处各写一遍是必须的 —— 标记里那个是布局自己排格子用的，这里这个要参与算格宽 —— 但改一处就得改另一处：
    /// <see cref="Fit"/> 把「这一行剩下的宽度」全摊给这几列，列距对不上，一行就会差出十几像素，最后一张不是
    /// 挤出右边界就是右边空一条。
    /// </para>
    /// </summary>
    public const double ColumnSpacing = 16;

    /// <summary>
    /// 这一行排得下几列、每列多宽。用户令 2026-10-18：「封面随窗口大小变化，互相之间不要留太大的空隙」。
    /// <para>
    /// 从前这里只给一个固定宽度（海报 170、剧照 300），列数由版面按「塞得下几列」自己定，余下的宽度留在格子
    /// 里；卡片是居中的，那点余量于是摊成了两张封面之间的一条大缝 —— 电视节目-集 在 1280 宽的窗口上，300 宽的
    /// 封面住着 414 宽的格子，两张之间空出 114 像素，比封面自己还显眼。
    /// </para>
    /// <para>
    /// 现在反过来算：先按「最小宽度」（图像大小那一档）定列数，再把这一行剩下的宽度平摊给这几列，卡片于是填满
    /// 格子，列距永远是 <paramref name="spacing"/> 那一条缝。窗口变宽，封面跟着变大，直到塞得下多一列为止
    /// （那时每张会退回最小宽度附近再涨）。
    /// </para>
    /// <para>
    /// <paramref name="available"/> 比最小宽度还窄（窗口拖得很小）时给一列、宽度就是窗口那么宽：宁可卡片小
    /// 一点，也不要横向裁掉一截 —— 这一页的横向滚动是关着的。
    /// </para>
    /// <para>
    /// <paramref name="minimum"/> 必须是「图像大小」那一档，**不能拿这里算出来的格宽再喂回来**：格宽是摊出来的，
    /// n 列格宽加 n 条缝比可用宽还多一条缝，版面按自己的算术只肯排 n-1 列，格子于是比卡片宽出一大截。
    /// </para>
    /// </summary>
    public static (int Columns, int Cell) Fit(double available, int minimum, double spacing = ColumnSpacing)
    {
        var least = Math.Max(1, minimum);
        if (!double.IsFinite(available) || available <= 0) return (1, least);

        var gap = Math.Max(0, spacing);

        // 列数和版面那边必须用同一条算术：UniformGridLayout 数的是 floor(可用宽 / (最小宽 + 列距)) ——
        // **每一列都要连着自己的那条缝才算塞得下**，最后一列后面那条缝也算。少算这一条，这里就会比版面
        // 多认一列（2026-10-18 探针：1058 宽、格宽 342，这边算三列，版面只肯排两列，格子成了 521）。
        var columns = Math.Max(1, (int)Math.Floor(available / (least + gap)));

        // 向下取整：宁可每列少半个像素，也不要算出比版面多一列、把最后一张顶出右边界。
        var cell = (int)Math.Floor((available - (columns - 1) * gap) / columns);
        return (columns, Math.Max(1, cell));
    }

    /// <summary>
    /// 形状的高宽比：海报 2:3、光盘 1:1、横幅 1000:185（Emby 的横幅图就是那个比例）、
    /// 徽标与缩略图 16:9。列表与表格的图只是行首那一小张，同样按 16:9 算。
    /// <para>
    /// 索引页那一支不看「视图」：类型永远是方卡、播出平台永远是 16:9（见
    /// <see cref="LibraryIndexCards.Aspect"/>）。理由在那边 —— 图本身是什么形状，卡片就该是什么形状。
    /// </para>
    /// </summary>
    public static double Height(LibraryView view, int width, LibrarySection section = LibrarySection.Items,
        LibraryIndexKind index = LibraryIndexKind.None) =>
        index != LibraryIndexKind.None ? Math.Round(width * LibraryIndexCards.Aspect(index))
        : section == LibrarySection.Episodes && view == LibraryView.Poster ? Math.Round(width * 9.0 / 16.0) : view switch
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
        LibraryField.Filename => 260,
        LibraryField.Container => 90,
        LibraryField.Resolution => 90,
        LibraryField.Bitrate => 100,
        LibraryField.FileSize => 100,
        LibraryField.VideoCodec or LibraryField.AudioCodec => 120,
        LibraryField.Framerate => 90,
        LibraryField.SeriesName => 200,
        LibraryField.EpisodeNumber => 90,
        _ => 120
    };

    /// <summary>
    /// 一行表格要多宽：各列加起来，再加上行与列头左右各 12 像素的边距（两处模板都是 <c>Padding="12,0"</c>）。
    /// 勾满字段时这个数会超过页宽，表格就要能横向滚 —— 页面拿它决定滚动面的宽度。
    /// </summary>
    public static double TotalWidth(IEnumerable<LibraryField> fields) => 24 + fields.Sum(WidthOf);
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
        {
            // 「标题」这一项在单集上已经写成 SxxExx（见 Segment），再勾「集号」会把它写第二遍。
            if (field == LibraryField.EpisodeNumber && item.Type == EmbyItemType.Episode
                && fields.Contains(LibraryField.Title)) continue;

            if (Segment(item, field) is { Length: > 0 } text)
                parts.Add(text);
        }
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

    public static string Title(EmbyItem item, IReadOnlyList<LibraryField> fields, LibrarySection section) =>
        !fields.Contains(LibraryField.Title) ? "" : section == LibrarySection.Episodes
            ? item.SeriesName ?? item.Name : item.Name;

    public static string Subtitle(EmbyItem item, IReadOnlyList<LibraryField> fields, LibrarySection section)
    {
        if (section != LibrarySection.Episodes) return Line(item, fields);
        var episode = fields.Contains(LibraryField.Title) ? LibraryUpcoming.Subtitle(item) : "";
        var details = Line(item, [.. fields.Where(field => field != LibraryField.Title)]);
        return string.Join("  ·  ", new[] { episode, details }.Where(text => !string.IsNullOrEmpty(text)));
    }

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

        // 文件本身那几项只从媒体源读 —— 没问 MediaSources 的请求里它们是空的，正好整段不画。
        LibraryField.Filename => FileName(item),
        LibraryField.Container => item.DefaultMediaSource?.Container ?? "",
        LibraryField.Resolution => item.DefaultMediaSource?.PrimaryVideoStream?.Height is > 0 and var height
            ? height + "p" : "",
        LibraryField.Bitrate => item.DefaultMediaSource?.Bitrate is > 0 and var bitrate
            ? (bitrate / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + " Mbps" : "",
        LibraryField.FileSize => TimeFormat.FileSize(item.DefaultMediaSource?.Size),
        LibraryField.VideoCodec => item.DefaultMediaSource?.PrimaryVideoStream?.Codec ?? "",
        LibraryField.Framerate => item.DefaultMediaSource?.PrimaryVideoStream?.AverageFrameRate is > 0 and var fps
            ? fps.ToString("0.###", CultureInfo.InvariantCulture) + " fps" : "",
        LibraryField.AudioCodec => item.DefaultMediaSource?.AudioStreams.FirstOrDefault()?.Codec ?? "",

        // 单集多出来的两项。集号写 SxxExx 而不是光一个数字：这一页是一条条单集排下来的，脱离季号
        // 的「5」在表里读不出是第几季的第几集（网页端那一列只写数字，这里没有照抄）。
        LibraryField.SeriesName => item.Type == EmbyItemType.Episode ? item.SeriesName ?? "" : "",
        LibraryField.EpisodeNumber => item.Type == EmbyItemType.Episode ? item.EpisodeCode : "",
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

    private static string FileName(EmbyItem item) =>
        item.Path is { Length: > 0 } path ? System.IO.Path.GetFileName(path) : "";

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

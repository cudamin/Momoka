namespace Momoka.Emby;

/// <summary>
/// 轮播条目从哪来 —— 「要使用最近添加还是随机的」（用户的话，2026-09-13）。装机默认是最近添加，
/// 那是 2026-09-13 之前唯一的来路：缺键的旧设置文件读出来的整数 0 因此一个像素都不变。
/// <para>
/// JSON 没装 <c>JsonStringEnumConverter</c>，枚举存整数（同 <see cref="HomeLibraryPlacement"/> 的规矩），认不出的
/// 数字由 <c>SettingsMigration.Normalize</c> 拨回 Recent。
/// </para>
/// </summary>
public enum CarouselSource
{
    /// <summary>整个服务器最近添加的，新到旧。</summary>
    Recent = 0,

    /// <summary>服务器随手给的一批，每次回到主页都可能不一样。</summary>
    Random = 1
}

/// <summary>
/// 这条带站哪一类媒体 —— 「要使用什么媒体」（用户的话，2026-09-13）。装机默认是全部：电影和剧集都上，
/// 音乐从不出现在轮播里（<c>HomeViewModel</c> 那头滤，请求侧也不放进随机那一档）。
/// </summary>
public enum CarouselMediaType
{
    /// <summary>电影和剧集都上。</summary>
    All = 0,

    /// <summary>只看电影。</summary>
    Movies = 1,

    /// <summary>只看剧集。</summary>
    Series = 2
}

/// <summary>
/// 主页轮播的版式 —— 2026-11-16 用户令「参考上图在设置中再添加一版轮播图」，设置 → 主页 → 轮播图里二选一。
/// <para>
/// <see cref="Full"/> 是 2026-09-13 起一直有的整宽大图，也是装机默认：缺键的旧设置文件读出来的整数 0 就是它，
/// 从旧版本升上来的人一个像素都不变。枚举存整数（同 <see cref="CarouselSource"/> 的规矩），认不出来的数字由
/// <c>SettingsMigration.Normalize</c> 拨回。
/// </para>
/// <para>
/// <see cref="Store"/> 按参考的商店轮播：宽窗口把大图收到左侧、右边一列跟着翻页显示接下来的三张；窗口窄到
/// 放不下右栏时自动回到整宽大图，两种宽度下都用底部分页圆点代替右下角的读数与进度条。右栏的阈值、宽度和
/// 「接着哪三张」都在这一类的纯函数里（<see cref="ShowsRail"/> 等），屏上那部分交给 <c>HomeBanner</c>。
/// </para>
/// </summary>
public enum CarouselLayout
{
    /// <summary>整宽大图，右下角是张数读数与进度条（现有版式，缺键的旧设置文件读出来就是它）。</summary>
    Full = 0,

    /// <summary>
    /// 商店版式：宽窗口左侧大图、右侧三张推荐（上大下二小）；窄窗口回到整宽大图。两种宽度下都用大图
    /// <em>下面</em>那一排分页圆点代替右下角的读数与进度条（2026-11-16 重做，按用户给的商店参考图）。
    /// </summary>
    Store = 1
}

/// <summary>
/// 主页轮播 —— 「参考主页轮播大图版-misty-4.9.css 给主页轮播功能」: which of the home page's items become the
/// big picture at the top of it, how far one press moves, how tall that band is, and what one slide says.
/// <para>
/// Pure and in Core for the same reason as <see cref="ItemArtwork"/>: every rule here is a decision about
/// content rather than about pixels — one slide per show, no slide without wide artwork, wrap around at
/// either end — and each of them would otherwise live inside an event handler where nothing can ask it a
/// question. What is left on top of this (<c>HomeBanner</c>) is fades, a timer and two chevrons.
/// </para>
/// </summary>
public static class HomeCarousel
{
    /// <summary>
    /// <see cref="CarouselMediaType"/> 在「最近添加」那一档要向服务器要的类型。剧集按 Series 和 Episode
    /// 一起要：/Items/Latest 的 GroupItems 会把同一部剧并成一条，但并完那条可能以任意一头出现 —— 只要
    /// Series 的话服务器在有的版本上干脆什么都不给。空表是「不加参数」，就是从前的行为。
    /// </summary>
    public static IReadOnlyList<string> LatestTypes(CarouselMediaType media) => media switch
    {
        CarouselMediaType.Movies => [EmbyItemType.Movie],
        CarouselMediaType.Series => [EmbyItemType.Series, EmbyItemType.Episode],
        _ => []
    };

    /// <summary>
    /// <see cref="CarouselMediaType"/> 在「随机」那一档要向服务器要的类型。随机走通用 /Items（SortBy=Random，
    /// 见 <see cref="EmbyClient.GetRandomAsync"/>），没有 GroupItems 来并剧 —— 所以剧集只按 Series 要，
    /// 一部剧的五集在随机结果里站五张，读者只会以为这条带停住了。「全部」同样只 Movie 和 Series：宽图
    /// 和字幕块都是为这两类画的，其他类型上了这条带也站不住。
    /// </summary>
    public static IReadOnlyList<string> RandomTypes(CarouselMediaType media) => media switch
    {
        CarouselMediaType.Movies => [EmbyItemType.Movie],
        CarouselMediaType.Series => [EmbyItemType.Series],
        _ => [EmbyItemType.Movie, EmbyItemType.Series]
    };

    /// <summary>
    /// How many slides at most by default. Ten —— 「轮播图改用前十个最近添加」（用户的话，2026-09-13）把张数
    /// 和来源一起点了名，从前那个「八」跟着旧来源一起退了。2026-09-13 下午起这只是装机默认：设置里多了一行
    /// 张数（<see cref="ClampSlots"/> 夹住的那一档），这里供缺键的旧设置文件和「没动过设置的人」读。
    /// the dots along the bottom edge are how a reader tells where in the
    /// set they are, and a row of twenty dots says nothing — and because a slide near the front holds a
    /// decoded backdrop, the slot count is a memory budget as much as a design one.
    /// </summary>
    public const int Slots = 10;

    /// <summary>设置里那行张数的下限。少于三张的轮播读起来是一张停住的图，不设那一档。</summary>
    public const int MinSlots = 3;

    /// <summary>
    /// 设置里那行张数的上限。每一张靠近前台的都扣着一张解码完的宽图（见 <see cref="Slots"/> 那段），张数
    /// 说到底是内存预算；十五张是一排点数还读得清的极限，也是几十兆画面的极限。
    /// </summary>
    public const int MaxSlots = 15;

    /// <summary>设置里那行张数落到这一档：手改文件越界的、行范围没对齐的，都在这里归位。</summary>
    public static int ClampSlots(int slots) => Math.Clamp(slots, MinSlots, MaxSlots);

    /// <summary>
    /// 商店版式右栏摆几张。参考的三张是「上面一张大卡、下面并排两张小卡」；轮播本身不够三张额外条目时
    /// （总数不到四张）右栏没有意义 —— 翻过来翻过去都是台上那张和它的重复，所以整列收起。
    /// </summary>
    public const int RailSlots = 3;

    /// <summary>大图和右栏之间的空当（DIP）。右栏自己不带左边距，这个数由 <see cref="MainWidth"/> 从大图那一侧让出来。</summary>
    public const double RailGap = 8;

    /// <summary>
    /// 右栏的宽度占整条轮播宽度的比例。参考截图里右栏约占四成；上下限 <see cref="RailMinWidth"/>／
    /// <see cref="RailMaxWidth"/> 兜住窄窗和超宽屏 —— 太窄放不下两行标题，太宽会把大图挤成竖条。
    /// </summary>
    public const double RailWidthShare = 0.39;

    /// <summary>右栏最窄一档（DIP）。再窄，下面两张卡片的标题就没法读了。</summary>
    public const double RailMinWidth = 300;

    /// <summary>右栏最宽一档（DIP）。超宽屏上右栏不再跟着变宽，多出来的宽度留给大图。</summary>
    public const double RailMaxWidth = 460;

    /// <summary>
    /// 整条轮播至少多宽才摆右栏（DIP）。窗口窄到放不下「大图 + 右栏」时回到整宽大图 —— 参考截图里
    /// 两种宽度下看到的就是这两档（2026-11-16 用户说明「截图里是轮播图不同窗口大小下的状态」）。
    /// 「整条轮播的宽」是 <c>HomeBanner</c> 自己的宽（页面里那块卡片的内沿），不是窗口宽。
    /// </summary>
    public const double RailBasisWidth = 900;

    /// <summary>
    /// 商店版式这一档要不要摆右栏：版式是商店版、轮播条目多过右栏的三张（否则会重复台上的那张）、
    /// 而且宽度够放得下两栏。三个条件缺一个都回到整宽大图。
    /// </summary>
    public static bool ShowsRail(CarouselLayout layout, double width, int slots) =>
        layout == CarouselLayout.Store && slots > RailSlots && width >= RailBasisWidth;

    /// <summary>右栏那一列的宽（DIP）：整条轮播宽度的 <see cref="RailWidthShare"/>，夹在上下限之间，取整到像素。</summary>
    public static double RailWidth(double width) =>
        Math.Round(Math.Clamp(width * RailWidthShare, RailMinWidth, RailMaxWidth));

    /// <summary>
    /// 大图那一列的宽（DIP）。摆右栏时让出右栏和两栏之间的空当；不摆右栏时整条都归大图。
    /// 两个数都取整，右栏列的边界落在整像素上，和大图那边一样。
    /// </summary>
    public static double MainWidth(double width, bool rail) =>
        Math.Max(0, rail ? width - RailWidth(width) - RailGap : width);

    /// <summary>
    /// 右栏跟着台上那张走：从下一张起，依次取三张，到末尾绕回第一张。条目不够三张时只给现有的几张
    /// （调用方按 <see cref="ShowsRail"/> 判断整列摆不摆）；一张都没有时是空表。
    /// <para>
    /// 台上那张永远不在右栏里 —— 它不是「接下来」的那一张，而且同一张图在左右两处同时出现只会让人
    /// 以为轮播卡住了。
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> RailIndices(int current, int count)
    {
        if (count <= 0) return [];

        var take = Math.Min(RailSlots, count - 1);
        var indices = new List<int>(take);
        for (var step = 1; step <= take; step++) indices.Add(Step(current, step, count));
        return indices;
    }

    /// <summary>
    /// 右栏上下两段怎么分（2026-11-16 重做）：上面一张大卡占两份、下面并排两张小卡占一份，中间让出
    /// <see cref="RailGap"/>。参考图里大卡高约小卡的两倍；从前那一版是上下各一半，所以右栏看着是「三张同辈」，
    /// 不像参考图里「一张主推带两张陪衬」。标记里的行高写 2＊／1＊，两边算出来的数一致，自检据此对账。
    /// </summary>
    public static (double Big, double Small) RailRows(double height)
    {
        var usable = Math.Max(0, height - RailGap);
        var big = Math.Round(usable * 2 / 3);
        return (big, Math.Max(0, usable - big));
    }

    /// <summary>
    /// 最矮的封面仍留出标题和操作键的空间。窄窗口由 HomeBanner 收起简介，
    /// 保持封面中的必要操作可见。
    /// </summary>
    public const double MinHeight = 300;

    /// <summary>
    /// 第一帧尚未量到宽度时使用默认 1422 宽对应的高度，避免布局完成前后跳动。
    /// </summary>
    public const double UnmeasuredHeight = 608;

    /// <summary>
    /// 封面带高为整幅宽度按 16:9 换算高度的 76%，约 2.34:1。
    /// 2026-09-11 用户移除“我的片库 / 快速进入”整排，并要求封面覆盖该区域。
    /// 1064 宽时从 359 增至 455，图像按 UniformToFill 铺满，标题栏直接浮在图上。
    /// </summary>
    public const double BandHeightShare = 0.76;

    /// <summary>
    /// 16:9 —— 服务器发来的宽剧照自己的形状。
    /// <para>
    /// 现在它有两条差事：开窗那一档的默认宽度（<c>HostWindow</c>），和把带高从带宽换出来（<see cref="Height"/>：
    /// 带宽 × <see cref="BandHeightShare"/> ÷ 这个数）。**剧照自己画多大已经不用它了** —— 铺满那一版由
    /// <c>Stretch="UniformToFill"</c> 按图自己的比例放大到盖住整条带、多出来的一截对半裁掉
    /// （「去掉首页封面轮播图的黑边」，2026-09-11；在那之前是「缩到六成、整张画出来」，16:9 的图在 2.96:1 的
    /// 带子里只裁上下各 5%）。现在的带子约 2.34:1，16:9 图片保留中间约 76% 的高度。
    /// </para>
    /// <para>
    /// **带宽是整个页宽，一个数都不用扣掉，横竖都不用。** 大图 2026-09-08 从「左边一栏、右边一列媒体库」改回
    /// 铺满整宽（「移除轮播图右边的媒体库」），右栏那一段宽不再减；侧边栏 2026-09-06 就删了，页面本来也是整个
    /// 客户区。竖向也不扣：外壳画在页面<em>上面</em>而不是上方。2026-09-10 下半天到 09-11 之间「弄个框把轮播图
    /// 框起来」那一版曾经把带宽收成「页宽减四边各 24」，2026-09-11 用户要「占满窗口的上半部分（包括窗口标题）」
    /// 之后卡片版式整个作废，带宽回到整幅页宽。
    /// </para>
    /// </summary>
    public const double WindowAspect = 16.0 / 9.0;

    /// <summary>The ratio in the language a report says it in, rather than as 1.78:1.</summary>
    public const string WindowAspectLabel = "16:9";

    /// <summary>
    /// How long a slide stands before the carousel moves on, and how long it waits again after someone
    /// presses a chevron. Eight seconds is long enough to read the synopsis of the one slide you care about
    /// and short enough that the band does not look frozen.
    /// <para>
    /// 2026-09-13 起这只是装机默认：设置里多了一行「封面轮换秒数」（<see cref="DwellFor"/> 夹住的那一档），
    /// 屏上真正走的是 <c>HomeBanner</c> 手里那一份，随设置即时变。
    /// </para>
    /// </summary>
    public const int MinDwellSeconds = 3;

    /// <summary>设置里那行秒数的上限。六十秒之后的轮播读起来就是一张静画，不设那一档。</summary>
    public const int MaxDwellSeconds = 60;

    /// <summary>装机默认的停留秒数，见 <see cref="Dwell"/>。</summary>
    public const int DefaultDwellSeconds = 8;

    /// <summary>设置里那行秒数落到这一档：手改文件越界的、行范围没对齐的，都在这里归位。</summary>
    public static TimeSpan DwellFor(int seconds) =>
        TimeSpan.FromSeconds(Math.Clamp(seconds, MinDwellSeconds, MaxDwellSeconds));

    /// <summary>装机默认的停留时长：<see cref="DwellFor"/>(<see cref="DefaultDwellSeconds"/>)。</summary>
    public static readonly TimeSpan Dwell = DwellFor(DefaultDwellSeconds);

    /// <summary>
    /// 带高由 <see cref="BandHeightShare"/> 随整幅宽度换算，1422 宽的默认窗口对应 608。
    /// 标题栏与被移除的快捷入口区域都属于同一张封面，不另外堆叠有底色的导航行。
    /// <para>
    /// 剧照铺满这条带（<c>HomeBanner</c> 的 <c>Stretch="UniformToFill"</c>），带子比 16:9 的图宽，所以图按宽
    /// 放大、上下各裁掉约 12%。**裁多少是带子自己的形状说了算，不是另一个
    /// 常数**：带子多高，图就裁多少 —— 带宽一拉，带高跟着变，裁的量跟着变，而图一个像素都不变形。
    /// </para>
    /// <para>
    /// 一屏是上限而不是目标：超宽屏上按比例换算的高度可能超过一屏，那时带高被一屏封住。下限
    /// <see cref="MinHeight"/> 兜的是矮到不像话的窗口（那一档字块会伸出下沿，<c>HomeBanner.State</c> 报得出来）。
    /// </para>
    /// <para>
    /// 量不到带宽的那一下（第一帧、还有自检里那份没上树的控件）用 <see cref="UnmeasuredHeight"/>，那就是开窗那一
    /// 档的带高，所以第一帧已经是第二帧的样子。
    /// </para>
    /// </summary>
    /// <param name="viewport">The window's client height, or 0 for 「not measured yet」.</param>
    /// <param name="width">这条带自己有多宽（就是整个页宽），或者 0 表示还没量到。</param>
    public static double Height(double viewport, double width)
    {
        if (width <= 0) return UnmeasuredHeight;

        var band = Math.Round(width * BandHeightShare / WindowAspect);
        var ceiling = viewport > 0 ? Math.Max(MinHeight, Math.Round(viewport)) : double.MaxValue;

        return Math.Clamp(band, MinHeight, ceiling);
    }

    /// <summary>
    /// 带子按设计形状的高：随宽度换算、<see cref="MinHeight"/> 兜底，**不被一屏封住**的那一份。矮窗档判
    /// 「轮播上下裁切有没有超出默认形状」（<see cref="HomeFold.LibraryOnBanner"/>）用的就是它：一屏够不到
    /// 这一份，带子就被压矮，剧照上下多裁出一截。
    /// </summary>
    public static double NaturalHeight(double width) => Height(0, width);

    /// <summary>
    /// 商店版式有右栏那一档的带高占整条轮播宽度的比例（2026-11-16 重做）。参考图里宽窗的大图加右栏
    /// 合起来约 3:1，右栏的大卡与两张小卡都在这个高度里排；从前照整宽版式的 2.34:1 算，商店版式会比
    /// 参考图高出一大截。
    /// </summary>
    public const double StoreWideHeightShare = 0.335;

    /// <summary>
    /// 底部分页圆点那一行占的高度（DIP，2026-11-16 重做）。商店版式的圆点在两张卡片<em>下面</em>的页面
    /// 底上，不是压在剧照里（参考图两种宽度都是这样）；这一行连上下的呼吸一共 28。
    /// </summary>
    public const double DotsHeight = 28;

    /// <summary>
    /// 商店版式的带高（2026-11-16 重做）。两种宽度两副形状，都从参考图量出来的：
    /// <list type="bullet">
    /// <item>摆右栏：整条宽度 × <see cref="StoreWideHeightShare"/>，约 3:1，右栏的 2:1 上下分段才有意义。</item>
    /// <item>放不下右栏（整宽大图）：回到 16:9，就是参考图窄窗那一张的比例；此时大图是页面里唯一一张。</item>
    /// </list>
    /// 上限仍是一屏、下限仍是 <see cref="MinHeight"/>，与整宽版式同一道闸；<paramref name="width"/> 是整条
    /// 轮播的宽（不是大图那一列的宽），右栏和空当都算在里面。
    /// </summary>
    public static double StoreHeight(double viewport, double width, bool rail)
    {
        if (width <= 0) return UnmeasuredHeight;

        var band = Math.Round(rail ? width * StoreWideHeightShare : width / WindowAspect);
        var ceiling = viewport > 0 ? Math.Max(MinHeight, Math.Round(viewport)) : double.MaxValue;

        return Math.Clamp(band, MinHeight, ceiling);
    }

    /// <summary>商店版式按设计形状的带高，不被一屏封住；同 <see cref="NaturalHeight"/> 的差事。</summary>
    public static double NaturalStoreHeight(double width, bool rail) => StoreHeight(0, width, rail);

    /// <summary>按版式取带高：整宽版式走 <see cref="Height"/>，商店版式走 <see cref="StoreHeight"/>。</summary>
    public static double BandHeight(CarouselLayout layout, double viewport, double width, bool rail) =>
        layout == CarouselLayout.Store ? StoreHeight(viewport, width, rail) : Height(viewport, width);

    /// <summary>按版式取「不被一屏封住」的设计带高。</summary>
    public static double NaturalBandHeight(CarouselLayout layout, double width, bool rail) =>
        layout == CarouselLayout.Store ? NaturalStoreHeight(width, rail) : NaturalHeight(width);

    /// <summary>
    /// Which items get to be slides. 来源是设置里那两行的事（最近添加还是随机、什么媒体 —— 2026-09-13「新增
    /// 在设置中设置轮播图要使用什么媒体，和要使用最近添加还是随机的还有数量的选项」）：这一头拿到的已经是
    /// 服务器按那个来源发回来的一份，这里只管把它筛成幻灯片。
    /// <para>
    /// 这一条推翻了 2026-09-05 那一版「有继续观看就用继续观看，没有或者继续观看不够就用最近添加」：从这一天起
    /// 继续观看不再上轮播。
    /// </para>
    /// <para>
    /// 两条规则照旧。一个剧集只占一张：服务器那头的 <c>GroupItems</c> 已经把同一部剧的最新单集并成了一条
    /// （随机那一档没有 GroupItems，靠的就是这里），这里这条是保险 —— 一个剧集四张幻灯片站在同一张背景图上、
    /// 挂着同一个名字，读起来就是一条停住了的轮播。以及没有宽图的一概不上（<see cref="ItemArtwork.BannerOrder"/>）
    /// —— 图就是这条带的全部，一张没有图的幻灯片是一个深色矩形上的标题。「前若干张」因此是**筛完之后**的
    /// 前若干张：来源里有一十二条但一半没有宽图，那就只有六张。
    /// </para>
    /// </summary>
    /// <param name="latest">服务器发回来的候选，次序已经由来源定好（最近添加新到旧；随机是服务器随手给的）。</param>
    /// <param name="slots">最多几张，设置里那行张数（<see cref="ClampSlots"/> 夹过的），缺省 <see cref="Slots"/>。</param>
    public static IReadOnlyList<EmbyItem> Slides(
        IReadOnlyList<EmbyItem> latest,
        int slots = Slots)
    {
        var slides = new List<EmbyItem>(Math.Max(0, slots));
        var shows = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in latest)
        {
            if (slides.Count >= slots) return slides;
            if (ItemArtwork.Banner(item) is null || !shows.Add(Show(item))) continue;

            slides.Add(item);
        }

        return slides;
    }

    /// <summary>
    /// What counts as the same show: the series id when the item has one, else the item's own. An episode and
    /// its own series arriving in two different rows would otherwise be two slides of the same thing.
    /// </summary>
    private static string Show(EmbyItem item) =>
        (item.Source?.Key ?? "") + ":" + (item.SeriesId is { Length: > 0 } series ? series : item.Id);

    /// <summary>
    /// Where <paramref name="delta"/> presses from <paramref name="index"/> land, wrapping at both ends: the
    /// last slide's 下一张 is the first one again. A band that stopped at the end would have to dim a chevron
    /// to say so, and this one moves on a timer besides — a reader who looks up mid-cycle should not have to
    /// work out that it is over.
    /// </summary>
    public static int Step(int index, int delta, int count)
    {
        if (count <= 0) return 0;

        var next = (index + delta) % count;
        return next < 0 ? next + count : next;
    }

    /// <summary>
    /// 一张幻灯片因为取不到宽图被摘掉之后，台上该是谁。<paramref name="dropped"/> 是它在旧清单里的下标，
    /// <paramref name="current"/> 是台上那张的旧下标，<paramref name="count"/> 是摘之前的张数。
    /// <para>
    /// 摘的是台上那张时，这个下标处空出来的位置由它后面那张补上（原来最后一张被摘掉就回到第一张）；
    /// 摘在台下时，台上那张不下台，只是它前面的位置少了一格。摘掉一张之后还停在原来那张（哪怕它是坏图）
    /// 或者整条带回到第一张，都是错的 —— 「刚入库的资源如果没有背景图或可用于轮播图的图片就不要放到轮播
    /// 列表里」（2026-11-02），这条只答「摘完之后看谁」。
    /// </para>
    /// </summary>
    public static int AfterDrop(int dropped, int current, int count)
    {
        if (count <= 1) return 0;
        if (dropped < current) return current - 1;
        if (dropped > current) return current;

        // 摘的正是台上这张：留下的 count-1 张里，原下标处就是下一张；原最后一张被摘掉就回到第一张。
        return dropped % (count - 1);
    }

    /// <summary>
    /// The dim line under a slide's title: which episode it is, then the facts (<see cref="ItemDetail.Facts"/>
    /// — year, runtime, rating). An episode is billed under its show's name on the title line
    /// (<see cref="ItemDetail.Title"/>), so without this line the slide would not say which episode it is.
    /// </summary>
    public static string Caption(EmbyItem item)
    {
        var parts = new List<string>(2);

        if (item.Type == EmbyItemType.Episode) parts.Add(ItemDetail.EpisodeLabel(item));
        if (ItemDetail.Facts(item) is { Length: > 0 } facts) parts.Add(facts);

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// The synopsis, or empty when the server sent none. How many lines of it fit is the band's business, and
    /// a clamp here would cut the same sentence at a different word on every window width.
    /// <para>
    /// 走 <see cref="ItemDetail.ProseLine"/> 而不是光 <c>Trim()</c>：刮削来的简介里带着排版用的空白（中文那些
    /// 爱用两个全角空格当段首缩进），而这条带只给两行 —— 一个段落换行白占一行，句中那两格空白在屏上就是「句号
    /// 后面空出六个字」。
    /// </para>
    /// </summary>
    public static string Synopsis(EmbyItem item) => ItemDetail.ProseLine(item.Overview);

    /// <summary>
    /// What one dot along the bottom edge is called, for a reader who is not looking at the picture:
    /// 「第 3 张，共 10 张」. The dots are the only thing on this band that says how many slides there are.
    /// </summary>
    public static string Position(int index, int count) => $"第 {index + 1} 张，共 {count} 张";
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 电影、剧集、季和单集共用的那一页，as state rather than as assignments to named elements.
/// <para>
/// The page this replaces had one method, <c>LayoutPage()</c>, that assigned twenty-odd
/// <c>Visibility</c> properties from four private flags and from the text of other elements, and every
/// method that changed anything ended by calling it. That arrangement had a documented rule attached —
/// 「可见性只在 LayoutPage() 里赋值，任何地方都不读回 Visibility」 — because reading a visibility back
/// gives the wrong answer while the page is still off screen. The rule is unnecessary here: nothing is
/// assigned, every row's visibility is a property computed from the value it depends on, and the value
/// is what a test can read.
/// </para>
/// <para>
/// The two 「are we the ones assigning this?」 flags are gone too, and were not replaced by one flag with
/// a better name. The season picker is guarded on <see cref="_loadedSeason"/> — the season whose episodes
/// are on screen, which is a fact about the page rather than about the call stack, so it is right no
/// matter who asks and there is nothing left set if an exception unwinds a load. The source picker is
/// guarded by construction: see <see cref="ShowTarget"/>.
/// </para>
/// </summary>
public sealed partial class DetailViewModel : PageViewModel
{
    private const string Category = "详情";

    /// <summary>
    /// 头图的解码宽度。原来是 520 —— WinForms 那一版的数，配的是一格 376 高、四周留边的带子，图铺在里面还
    /// 被 <c>UniformToFill</c> 放大一截，糊一点正好当成免费的模糊。现在这张图铺满整个窗口（背景那一层在滚动
    /// 视图外面），一张 520 宽的图撑到 2560 宽要放大五倍，那就不是模糊而是坏了。
    /// 1280 是「一屏够清楚」和「一页只留一张位图」之间的那一档。
    /// </summary>
    private const int HeroDecodeWidth = 1280;

    /// <summary>
    /// 剧名上方那一枚徽标 —— 徽标 or 横幅图, see <see cref="ItemArtwork.Plate"/>. Decoded at the widest it can be
    /// drawn: the markup caps it at 280×56 and stretches it uniformly, so a wide 横幅图 fills that width exactly
    /// and a squarer 徽标 is shrunk by the height cap instead. 从前它站在角上、按 220 解；再往前它顶替文字标题，
    /// 按将近两倍宽解。
    /// </summary>
    private const int PlateDecodeWidth = 280;

    /// <summary>
    /// 右上角那张艺术图（<see cref="ItemArtwork.Corner"/>）的解码宽度，同它画出来的那个盒子
    /// （<see cref="DetailHero.CornerWidth"/>×<see cref="DetailHero.CornerHeight"/>）。它是一幅装饰画，不是读数，
    /// 按最大档的 540 宽解码，窗口拉大时不需要重新请求图片。
    /// </summary>
    private const int CornerDecodeWidth = (int)(DetailHero.CornerWidth * DetailHero.CornerScaleLimit);

    private double _cornerScale = 1;

    /// <summary>
    /// 页尾那张横幅（<see cref="ItemArtwork.Footer"/>）的解码宽度，同它画出来的那个上限（760）。横幅图上写着片名，
    /// 所以它是这一页上唯一一处「字」由位图承担、又画得很大的地方 —— 解窄了糊的是片名本身。
    /// </summary>
    private const int FooterDecodeWidth = 760;

    /// <summary>四种详情封面统一使用集页的 260×375 上限，按原图比例显示。</summary>
    private const int PosterStillWidth = 260;

    private const int PosterStillHeight = 375;

    /// <summary>
    /// 按封面画出来的宽取图 —— 取图宽度就是那一格的上限，不会出现「窄窗开出来一张小图、拉宽了再放大」。
    /// 取图不看当前显隐，窗口跨过紧凑断点时可直接显示已有图片。
    /// </summary>
    private static int StillDecodeWidth => PosterStillWidth;

    /// <summary>How many lines of 简介 are shown before 阅读更多 appears.</summary>
    private const int CollapsedLines = 4;

    private IShellActions? _actions;
    private ISettingsService? _settings;
    private EmbySession? _session;
    private EmbyImageStore? _images;
    private RemovedMedia? _removed;

    /// <summary>What the grid or shelf handed over. Used for its id; everything else is re-fetched.</summary>
    private EmbyItem? _seed;

    /// <summary>The item as the server describes it in full — the thing the whole page is about.</summary>
    private EmbyItem? _detail;
    private string? _announcedShapeType;

    /// <summary>
    /// 集页背后那部剧，随取图那一趟一起回来（见 <see cref="LoadShowAsync"/>）。其他信息那一行要的类型、工作室
    /// 和刮削 id 都挂在剧集那一层 —— 单集自己身上这三样是空的 —— 所以缓存起来让取图和这一行共用一趟往返。
    /// </summary>
    private EmbyItem? _show;

    /// <summary>
    /// The season whose episodes are on screen. The 季 picker's changed handler compares against this
    /// rather than consulting a 「syncing」 flag: replacing the picker's items makes it report a
    /// selection it chose itself, and that report has to be distinguishable from a person picking a
    /// season without the two paths having to know about each other. It is updated only after the
    /// episode request succeeds, so a failed switch cannot leave the picker naming one season while
    /// the shelf still contains another.
    /// </summary>
    private EmbyItem? _loadedSeason;
    private bool _active;
    private int _lifetime;
    private bool _syncingSeason;
    private EmbySessionScope? _scope;
    private IReadOnlyList<MediaVersion> _versions = [];
    private int _versionGeneration;
    private CancellationTokenSource? _versionLoad;

    /// <summary>
    /// The episodes on screen, handed to the player so 下一集 works and to the context menu so
    /// 标记本季已看 knows what 本季 is.
    /// </summary>
    private readonly List<EmbyItem> _episodes = [];
    private readonly HashSet<string> _deletedItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedSourceIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedSourcePaths = new(StringComparer.Ordinal);

    /// <summary>
    /// 剧页「其他信息」卡里的媒体信息描述的那一集（规则在 <see cref="ItemDetail.MediaInfoEpisode"/>：
    /// 有续播取最近播放的续播集，否则当季第一集）。null 表示这一页不是剧页、集还没回来，或者那一集
    /// 没有媒体源可讲。季页不摆媒体信息（2026-11-02 用户令「季页面不要媒体信息」）。
    /// </summary>
    private EmbyItem? _infoEpisode;

    /// <summary>同 <see cref="_infoEpisode"/> —— 那一集这一次用哪一版讲。</summary>
    private MediaSource? _infoSource;

    /// <summary>
    /// Artwork has its own cancellation, separate from the page's load. Switching seasons deliberately
    /// leaves it alone — the hero belongs to the show, not to the season — so cancelling it with the
    /// load would lose the picture on every flick of the drop-down.
    /// </summary>
    private CancellationTokenSource? _art;

    /// <summary>
    /// 自检用的三个数，都是**这一页从建出来到扔掉的总数**，不是某一次载入的：先画那一屏落下的片名
    /// （null = 这一页没先画，存根导航），先画过几次，和那四张图被起过几次。
    /// <para>
    /// 两个 1 是对的：先画只该发生一次（第一次载入时屏上是空的），图也只该取一次 —— 完整条目回来时图没变就不重取
    /// （见 <see cref="Apply"/>），而重新读取（标记已看之后、手动刷新）时屏上已经是完整那一份，不该再拿卡片盖一遍
    /// （见 <see cref="Preview"/>）。
    /// </para>
    /// <para>
    /// 屏上都看不出来：图起 2 次是点一张封面进来闪一下，一百毫秒的事，截图抓不住；先画 2 次是「标记为已观看」之后
    /// 那个勾先跳回未看、一趟往返之后才变回来 —— 也抓不住，而那两件正是这一条要修的东西。
    /// </para>
    /// </summary>
    private string? _previewed;

    private int _previews;

    private int _artStarts;

    public DetailViewModel()
    {
        StillWidth = PosterStillWidth;
        StillHeight = PosterStillHeight;

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Showcase))
            {
                RefitArtwork();
                OnPropertyChanged(nameof(HeroCardShown));
                OnPropertyChanged(nameof(HeroCardVisibility));
                OnPropertyChanged(nameof(HeroContentAlignment));
                OnPropertyChanged(nameof(CoverAlignment));
                OnPropertyChanged(nameof(CornerVisibility));
                OnPropertyChanged(nameof(HeroContent));
                OnPropertyChanged(nameof(HeroRoom));
                OnPropertyChanged(nameof(HeroInset));
                OnPropertyChanged(nameof(TailInset));
                OnPropertyChanged(nameof(OverviewPanelPadding));
                OnPropertyChanged(nameof(TailMargin));
                OnPropertyChanged(nameof(CardTailGapHeight));
                OnPropertyChanged(nameof(ColumnVideoVisibility));
                OnPropertyChanged(nameof(VideoLineVisibility));
                OnPropertyChanged(nameof(ColumnPickersVisibility));
                UpdateHeroBlur();
            }
        };

        // Four rows whose visibility is a count. [NotifyPropertyChangedFor] cannot watch a collection's
        // Count — it hangs off a property's setter, and these collections are never re-assigned — so the
        // notification is hung off the collection itself, where it cannot be forgotten at a call site.
        Seasons.CollectionChanged += (_, _) =>
        {
            PickerChanged(nameof(SeasonVisibility));
            OnPropertyChanged(nameof(EpisodeVisibility));
            OnPropertyChanged(nameof(CanPickEpisodeSeason));
        };

        // 媒体源那一行多喊一声「视频：…」那一行：头图上那一行在下拉出现时收起来（同一份读数不在一屏上说两遍，
        // 见 VideoVisibility），所以这个集合一变，两处的显隐都可能翻。
        Sources.CollectionChanged += (_, _) =>
        {
            PickerChanged(nameof(SourceVisibility));
            OnPropertyChanged(nameof(VideoVisibility));
        };

        AudioTracks.CollectionChanged += (_, _) =>
        {
            PickerChanged(nameof(AudioVisibility));
            OnPropertyChanged(nameof(AudioRowVisibility));
            OnPropertyChanged(nameof(SingleAudioVisibility));
            OnPropertyChanged(nameof(SingleAudioText));
        };
        SubtitleTracks.CollectionChanged += (_, _) => PickerChanged(nameof(SubtitleVisibility));

        // 媒体信息 is the same problem and not a picker: its own visibility only, without the pickers row's.
        // 分割线跟着这张表和下面那一行一起决定（见 OtherInfoRuleVisibility），所以这一份也归它。
        InfoRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(InfoVisibility));
            OnPropertyChanged(nameof(MediaInfoVisibility));
            OnPropertyChanged(nameof(OtherInfoRuleVisibility));
        };
    }

    // ---------------------------------------------------------------- 头部

    [ObservableProperty]
    public partial string? Title { get; set; }

    /// <summary>
    /// Where the headline leads, or null when the headline is only a headline. On an episode page the
    /// title is the show's name rather than the episode's (see <see cref="ItemDetail.Title"/>), so it is
    /// a promise the reader can follow; on every other page the title names the page you are already on
    /// and the button is disabled by <see cref="CanOpenTitle"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleTip))]
    [NotifyCanExecuteChangedFor(nameof(OpenTitleCommand))]
    public partial EmbyItem? TitleLink { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SublineVisibility))]
    [NotifyPropertyChangedFor(nameof(SublineFontSize))]
    [NotifyPropertyChangedFor(nameof(SublineFontWeight))]
    [NotifyPropertyChangedFor(nameof(SublineBrush))]
    public partial string? Subline { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreVisibility))]
    public partial string? Score { get; set; }

    /// <summary>
    /// 分数旁边那句「这是谁给的分」——「豆瓣」「TMDB」「烂番茄」或者「公众评分」。
    /// <para>
    /// 单独一格而不是拼进 <see cref="Score"/>：那四个字要走暗墨、跟着事实那一行的层次，而分数走强墨。而且它必须说
    /// <b>实际取到的是哪一档</b>，不是设置里选的哪一档 —— 选了豆瓣而服务器认不出豆瓣，这里写的就是「公众评分」。
    /// </para>
    /// </summary>
    [ObservableProperty]
    public partial string? ScoreLabel { get; set; }

    /// <summary>
    /// 自检用：规矩说这一格该画什么。屏上那两格由绑定填，而「该是什么」只有视图模型算得出来 —— 探针拿这一份和屏上
    /// 比，两边一致才算这一格真的接上了。
    /// </summary>
    internal Emby.ScoreBadge ScoreBadge { get; private set; } = Emby.ScoreBadge.None;

    /// <summary>
    /// 自检用：服务器在这个条目上到底给了什么评分字段。**只报不判** —— 有没有影评指数是服务器上装了哪些插件的
    /// 事，判它就是把一关的成败交给别人的配置。可它是这一关最值得读的一句：这台服务器有没有影评指数，从此每次
    /// 自检都自己答一遍。
    /// </summary>
    internal string ScoreFacts { get; private set; } = "还没读到条目";

    /// <summary>首播日期、时长、分级、季数, already joined. Empty is a row with nothing in it, not a gap.</summary>
    [ObservableProperty]
    public partial string? Facts { get; set; }

    /// <summary>
    /// 导演, the prose line under 简介. Empty for an item with no director credited, which collapses
    /// the line — same convention as every other string on this page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectorsVisibility))]
    [NotifyPropertyChangedFor(nameof(OverviewVisibility))]
    [NotifyPropertyChangedFor(nameof(TailMargin))]
    public partial string? Directors { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VideoVisibility))]
    [NotifyPropertyChangedFor(nameof(VideoLineVisibility))]
    [NotifyPropertyChangedFor(nameof(ColumnVideoVisibility))]
    [NotifyPropertyChangedFor(nameof(ColumnPickersVisibility))]
    public partial string? VideoLine { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroVisibility))]
    public partial ImageSource? HeroImage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StillVisibility))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    public partial ImageSource? StillImage { get; set; }

    /// <summary>
    /// 徽标 or 横幅图 —— 剧名上方那一枚（「统一改为在剧名上方显示徽标」）。四种页面同一个位置，不再按页面的种类
    /// 分左上角右上角。服务器两种都没有的条目上是 null，那一格就空着 —— 片名那一行照旧把名字说全，这一页没有
    /// 一样东西等这张图。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlateVisibility))]
    public partial ImageSource? PlateImage { get; set; }

    /// <summary>
    /// 右上角那张艺术图 —— 「给集页面右上角添加艺术图」（2026-09-12）。哪一张归这儿是
    /// <see cref="ItemArtwork.Corner"/> 的事（这一集自己的，没有就借剧集那一层的）；这里只存解出来的那张。
    /// 集页与简洁模式电影、剧页显示（见 <see cref="CornerVisibility"/>），没有艺术图时收起。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CornerVisibility))]
    public partial ImageSource? CornerImage { get; set; }

    /// <summary>
    /// 页面最底下那张横幅 —— 「在电影页面 剧页面 集页面的底部添加横幅」。哪一张归这儿是
    /// <see cref="ItemArtwork.Footer"/> 的事（这个条目自己的横幅图，而剧名上方那一枚已经用掉它时空着）；这里
    /// 只存解出来的那张。摆在哪儿用户点过名：整页最底下、演职人员那一排之后，滑到底才看得见。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterVisibility))]
    public partial ImageSource? FooterImage { get; set; }

    /// <summary>
    /// 页尾那张横幅这一次取的是哪一张 —— 自己的，还是借剧集那一层的（见 <see cref="ItemArtwork.Footer"/>）。
    /// null 就是「这一页没有」。自检读它：屏上那张图自己说不出它走了哪条路，而「借来的那一档到底走没走」正是这
    /// 一处唯一看不见的判断。
    /// </summary>
    public ArtworkRef? FooterPick { get; private set; }

    /// <summary>
    /// 这一页看得见的那一段有多宽，由视图在每次改尺寸时量给（<c>DetailPage.OnBodySizeChanged</c>）。它自己不上屏，
    /// 是两件事的自变量：紧凑版式换不换（<see cref="IsCompact"/>）、以及背景那一张画多高（<see cref="PictureHeight"/>）。
    /// <para>
    /// 量的是页面而不是窗口：将来哪一侧再长出占据宽度的东西，挤的也是页面这一头。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompact))]
    [NotifyPropertyChangedFor(nameof(Showcase))]
    [NotifyPropertyChangedFor(nameof(Narrow))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutWidth))]
    [NotifyPropertyChangedFor(nameof(BackdropShown))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(PaperMinHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(HeroArtVisibility))]
    [NotifyPropertyChangedFor(nameof(HeroPlainVisibility))]
    [NotifyPropertyChangedFor(nameof(HeroCardVisibility))]
    [NotifyPropertyChangedFor(nameof(HeroCardShown))]
    [NotifyPropertyChangedFor(nameof(ScrimVisibility))]
    [NotifyPropertyChangedFor(nameof(StillVisibility))]
    [NotifyPropertyChangedFor(nameof(CompactVisibility))]
    [NotifyPropertyChangedFor(nameof(CornerVisibility))]
    [NotifyPropertyChangedFor(nameof(HeroInset))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    [NotifyPropertyChangedFor(nameof(SheetPadding))]
    [NotifyPropertyChangedFor(nameof(SheetSpacing))]
    [NotifyPropertyChangedFor(nameof(BodyPanelPadding))]
    [NotifyPropertyChangedFor(nameof(ShowShelfLines))]
    [NotifyPropertyChangedFor(nameof(ColumnPickersVisibility))]
    [NotifyPropertyChangedFor(nameof(PickersVisibility))]
    [NotifyPropertyChangedFor(nameof(VideoLineVisibility))]
    [NotifyPropertyChangedFor(nameof(ColumnVideoVisibility))]
    [NotifyPropertyChangedFor(nameof(HeroContentAlignment))]
    [NotifyPropertyChangedFor(nameof(ColumnActionsVisibility))]
    [NotifyPropertyChangedFor(nameof(WideProgressVisibility))]
    [NotifyPropertyChangedFor(nameof(TailMargin))]
    [NotifyPropertyChangedFor(nameof(CardTailGapHeight))]
    public partial double PageWidth { get; set; }

    /// <summary>
    /// 这一页这次走哪套版式 —— 页面窄过 <see cref="DetailHero.CompactFloor"/> 就换成单列的紧凑版式：封面按
    /// <see cref="DetailHero.ShowsStill"/> 让位（紧凑版式下四种页面都不画，2026-10-13「窄窗口下不要封面」），
    /// 播放和那排操作挪到画面底下
    /// 的暗区里（<c>DetailPage</c> 标记里的紧凑块）。桌面版式不再缩小任何东西 ——
    /// 「收窄窗口后组件要自动换行」（2026-09-12）：从前 1024 到 1280 那一段整体等比缩小（<c>HeroScale</c>，
    /// 那个属性随之退役），缩到八成的组件又小又松；现在组件保持原大小，行装不下就换行，换行的落点跟着
    /// <see cref="HeroLayoutWidth"/>（就是页面本身的宽）走。
    /// </summary>
    // 简洁模式在窄窗也保留封面与双栏操作。沉浸模式和单集仍使用原紧凑断点。
    public bool IsCompact => !PlainDetail && DetailHero.IsCompact(PageWidth);

    /// <summary>沉浸开关开启时，电影、剧、季和单集宽窗让开正文纸面；窄窗仍使用紧凑排版。</summary>
    public bool Showcase => DetailHero.Immersive(ImmersiveEnabled, IsCompact, BackdropShown, ItemType);

    internal double ControlBackgroundOpacity => _settings?.Settings.Ui.ControlBackgroundOpacity ?? UiSettings.DefaultControlBackgroundOpacity;

    internal bool UseAcrylicControls => _settings?.Settings.Ui.UseAcrylicControls == true;

    /// <summary>详情按钮亚克力的 TintOpacity（0–1）。半透明叠层用不到它。</summary>
    internal double ControlTintOpacity => AppearanceOpacity.Tint(
        _settings?.Settings.Ui.ControlTintOpacity ?? UiSettings.DefaultAcrylicTintOpacity);

    /// <summary>详情按钮亚克力的 TintLuminosityOpacity（0–1）。</summary>
    internal double ControlTintLuminosityOpacity => AppearanceOpacity.Tint(
        _settings?.Settings.Ui.ControlTintLuminosityOpacity ?? UiSettings.DefaultAcrylicTintOpacity);

    /// <summary>背景亚克力的 TintOpacity（0–1）。背景固定走亚克力，两档浓度不再有别的入口。</summary>
    internal double BackdropTintOpacity => AppearanceOpacity.Tint(
        _settings?.Settings.Ui.BackdropTintOpacity ?? UiSettings.DefaultAcrylicTintOpacity);

    /// <summary>背景亚克力的 TintLuminosityOpacity（0–1）。</summary>
    internal double BackdropTintLuminosityOpacity => AppearanceOpacity.Tint(
        _settings?.Settings.Ui.BackdropTintLuminosityOpacity ?? UiSettings.DefaultAcrylicTintOpacity);

    /// <summary>
    /// 这层材料挡住了画面多少（0–1）：两项浓度都拖到 0 就是「什么都不盖」，图按原样清晰显示。
    /// 只用于「要不要为背景跑裁切模糊」与字色估算，不声称复刻平台的像素合成。
    /// </summary>
    internal double BackdropCoverage => AppearanceOpacity.Coverage(BackdropTintOpacity, BackdropTintLuminosityOpacity);

    internal void RefreshAppearance()
    {
        SyncBackdrop();
        RefitArtwork();
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(StillVisibility));
        OnPropertyChanged(nameof(CompactVisibility));
        OnPropertyChanged(nameof(CornerVisibility));
        OnPropertyChanged(nameof(ColumnActionsVisibility));
        OnPropertyChanged(nameof(WideProgressVisibility));
        OnPropertyChanged(nameof(PickersVisibility));
        // 设置切换不改变条目类型，不能走 AnnounceShape 的同类型早退。
        OnPropertyChanged(nameof(HeroCardVisibility));
        OnPropertyChanged(nameof(HeroCardShown));
        OnPropertyChanged(nameof(HeroPlainVisibility));
        OnPropertyChanged(nameof(ScrimVisibility));
        OnPropertyChanged(nameof(HeroContentAlignment));
        OnPropertyChanged(nameof(CoverAlignment));
        OnPropertyChanged(nameof(CornerVisibility));
        OnPropertyChanged(nameof(HeroContent));
        OnPropertyChanged(nameof(HeroRoom));
        OnPropertyChanged(nameof(HeroInset));
        OnPropertyChanged(nameof(TailInset));
        OnPropertyChanged(nameof(TailMargin));
        OnPropertyChanged(nameof(CardTailGapHeight));
        OnPropertyChanged(nameof(HeroLayoutHeight));
        OnPropertyChanged(nameof(HeroHeight));
        OnPropertyChanged(nameof(ScrimHeight));
        OnPropertyChanged(nameof(BodyMinHeight));
        OnPropertyChanged(nameof(Narrow));
        OnPropertyChanged(nameof(Squeeze));
        OnPropertyChanged(nameof(ColumnVideoVisibility));
        OnPropertyChanged(nameof(VideoLineVisibility));
        OnPropertyChanged(nameof(ColumnPickersVisibility));
        UpdateHeroBlur();
        OnPropertyChanged(nameof(Showcase));
        OnPropertyChanged(nameof(TailMinHeight));
        OnPropertyChanged(nameof(PaperMinHeight));
        OnPropertyChanged(nameof(PictureHeight));
        OnPropertyChanged(nameof(TailSpacing));
        OnPropertyChanged(nameof(TailPanelPadding));
        OnPropertyChanged(nameof(OverviewPanelPadding));
        OnPropertyChanged(nameof(SheetPadding));
        OnPropertyChanged(nameof(SheetSpacing));
        OnPropertyChanged(nameof(BodyPanelPadding));
        OnPropertyChanged(nameof(ShowShelfLines));
    }

    /// <summary>
    /// 货架标题前那条短强调线和底下那条通栏线画不画 —— 沉浸档都不画（2026-10-31 用户令「把线条去掉」）。
    /// 那一档纸面和面板都让开了，一条横贯整页的发丝线压在模糊背景上就是一条没来由的线；还原档照旧画。
    /// </summary>
    public bool ShowShelfLines => !Showcase;

    /// <summary>
    /// 集页整段上方合成的那一块面板在不在 —— <see cref="HeroCardVisibility"/>、<see cref="HeroPlainVisibility"/>、
    /// <see cref="ScrimVisibility"/>、<see cref="HeroInset"/> 和 <see cref="TailInset"/> 五处都问它，判据只写一份。
    /// </summary>
    private bool HeroCard => (IsEpisodePage && !Showcase || PlainDetail) && !IsCompact;

    /// <summary>沉浸总开关只改变外观，不改变单集、海报或播放目标语义。</summary>
    private bool ImmersiveEnabled => _settings?.Settings.Ui.EnableImmersive != false;

    private bool PlainDetail => !ImmersiveEnabled
        && ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season;

    /// <summary>
    /// 头图和尾部的内容按多宽排 —— 页面本身的宽。从前是「至少 1280、窄了由 Viewbox 整体缩下去」：那一版换行的
    /// 落点钉在 1280 上不动，窗口窄了只会整体变小（「组件又小又松」）。现在内容按真实页宽排，行装不下就地换行
    /// （按键行、文件选项那几块 chips）。版面里吃窗口宽的只剩一样，也在 Core：角图的让位
    /// （<see cref="DetailHero.CornerBoxWidth"/>）—— 封面那一格两个数都是定值，而且只画在宽版式上
    /// （<see cref="DetailHero.ShowsStill"/>）。0 是「还没量」（第一次布局之前）。
    /// </summary>
    public double HeroLayoutWidth => Math.Max(PageWidth, 0);

    /// <summary>
    /// 背景那张位图的高 ÷ 宽（16:9 是 0.5625），图解出来时跟着 <see cref="HeroImage"/> 一起换。它喂两个地方：
    /// 紧凑版式的带高（<see cref="DetailHero.CompactHeight"/>）和背景那一张的盒高
    /// （<see cref="DetailHero.PictureHeight"/>）—— 「窗口收窄时背景图要等比例缩放」要的就是图自己的形状。
    /// <para>
    /// 图还没解出来的时候按 16:9 兜底：背景那几张全是横图，第一帧按它布、真图到了再修正，跟别的版面数同一套
    /// 「先按估值布一遍」的道理。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(PaperMinHeight))]
    [NotifyPropertyChangedFor(nameof(Showcase))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    [NotifyPropertyChangedFor(nameof(SheetPadding))]
    [NotifyPropertyChangedFor(nameof(SheetSpacing))]
    [NotifyPropertyChangedFor(nameof(BodyPanelPadding))]
    [NotifyPropertyChangedFor(nameof(ShowShelfLines))]
    public partial double HeroHeightRatio { get; set; } = 9d / 16;

    // 裁切模糊的三个数：门槛（裁切不超过它不糊 —— 「提高拉长窗口后，背景被裁切触发背景图模糊的阈值」，
    // 拉长窗口头几档的轻微裁切不值得整张图陪着糊）、裁到最狠再加多少（BackdropBlur.CropRadius 的 extra）、
    // 按多大一档量化（step）。量化是给拖窗口的：占比一路变，档位不跟着一路变，重糊只发生在换档那一下。
    private const double CropBlurStart = 0.25;
    private const int CropBlurExtra = 32;
    private const int CropBlurStep = 8;

    /// <summary>解好的头图原图像素 —— 裁切模糊的原料。导航换条目、换图时整个作废。</summary>
    private HeroPictureLoader.HeroPixels? _heroPixels;
    internal HeroPictureLoader.HeroPixels? BackdropPixels => _heroPixels;
    internal UiSettings AppearanceSettings => _settings?.Settings.Ui ?? new UiSettings();

    private NavigationTimingRun? _heroTiming;
    private DetailHeroCache? _heroCache;
    private DetailHeroCache.Ticket? _heroCacheTicket;

    internal void UseHeroCache(DetailHeroCache cache) => _heroCache = cache;

    /// <summary>
    /// 解好的封面（集页那张剧海报、其余页面的海报）原图像素 —— 按图收窄那一格的原料
    /// （<see cref="FitStill"/>）。换条目、换图时作废。
    /// </summary>
    private (int Width, int Height)? _stillPixels;

    /// <summary>解好的角图原图像素，同 <see cref="_stillPixels"/>（<see cref="FitCorner"/> 的原料）。</summary>
    private (int Width, int Height)? _cornerPixels;

    /// <summary>上一次渲染用的模糊档位；-1 是「还没出过图」。</summary>
    private int _heroBlurRadius = -1;

    /// <summary>重糊的生代号：谁后到谁算数，路上换了档就作废先回来那一张。</summary>
    private int _heroBlurGeneration;

    /// <summary>这一张头图正在重糊 —— 换档之前先拦住「旧的那张盖上来」。</summary>
    private bool _heroRendering;

    /// <summary>
    /// 这一趟重糊的取消牌。拖窗口一次换十几档，旧的一趟要当场停下 —— 1280×720 上一趟 σ=24 的卷积是秒级的，
    /// 不取消就会攒成一队，屏上停在几秒前的读数。条目离开也走它（<see cref="Cancel"/>）。
    /// </summary>
    private CancellationTokenSource? _blurCancel;

    /// <summary>
    /// 当前的模糊档位 —— 按裁切占比（<see cref="DetailHero.PictureCrop"/>）换算半径。电影和剧没有基础
    /// 半径，裁多少加多少；集页只在紧凑版式里画这张图，宽版式上裁切占比算出来照样有值，只是没有一层画它。
    /// <para>
    /// 2026-11-07 起只有这一条路：设置里的高斯模糊选项随「去掉高斯模糊和颜色遮罩」一起退役，压在图上的那层
    /// 亚克力自己会糊，不再另跑一遍 CPU 卷积去调模糊程度。裁切模糊留着 —— 它管的是图自己被窗口裁掉多少，
    /// 和那层材料无关（省电回退成实色时这张图还在）。两项浓度都拖到 0 时那层材料什么都不盖，图按原样清晰
    /// 显示 —— 与 2026-11-06 那句「0 档图原样清晰」是同一件事，只是现在的 0 档由两项染色浓度一起表达。
    /// </para>
    /// </summary>
    private int HeroBlurTarget => BackdropCoverage <= 0 ? 0 : BackdropBlur.CropRadius(
        DetailHero.PictureCrop(PageWidth, HeroHeightRatio, Viewport), CropBlurStart, 0, CropBlurExtra, CropBlurStep);

    /// <summary>
    /// 窗口形状变了（<c>DetailPage.OnBodySizeChanged</c> 量完宽高顺手喊一声）：按新的裁切占比算模糊档位，
    /// 换档就用缓存的那批像素重糊一遍。生代号让「谁后到谁算数」—— 重糊还在路上又换了一档，先回来那一张
    /// 作废。不做可等待：拖窗口不等一次几十毫秒的像素活，屏上短暂停在上一档是这条路的正常形态。
    /// </summary>
    public void UpdateHeroBlur()
    {
        if (!Attached || _heroPixels is null) return;

        var target = HeroBlurTarget;
        if (target == _heroBlurRadius) return;

        var generation = ++_heroBlurGeneration;
        _heroBlurRadius = target;
        _heroRendering = true;

        _blurCancel?.Cancel();
        _blurCancel?.Dispose();
        _blurCancel = new CancellationTokenSource();
        var token = CancellationTokenSource.CreateLinkedTokenSource(
            _art?.Token ?? CancellationToken.None, _blurCancel.Token).Token;
        _ = RenderBlurAsync(generation, _heroPixels, target, token);
    }

    private async Task<BitmapImage?> RenderHeroAsync(HeroPictureLoader.HeroPixels pixels, int radius,
        CancellationToken token, DetailHeroCache.Ticket? ticket, NavigationTimingRun? timing)
    {
        token.ThrowIfCancellationRequested();
        ticket?.Scope.ThrowIfNotAvailable();
        var cache = _heroCache;
        // 详情页不再走高斯那一路：压在图上的亚克力自己会糊，CPU 这边只按裁切占比做盒式模糊。
        if (cache is not null && ticket is not null && cache.TryGetPicture(ticket, pixels, radius, out var kept, false))
        {
            timing?.Mark("hero-picture-hit", 1);
            return kept;
        }
        var picture = await HeroPictureLoader.RenderAsync(pixels, radius, token, timing, false).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        ticket?.Scope.ThrowIfNotAvailable();
        if (picture is not null && cache is not null && ticket is not null)
            cache.RememberPicture(ticket, pixels, radius, picture, false);
        return picture;
    }

    private async Task RenderBlurAsync(int generation, HeroPictureLoader.HeroPixels pixels, int radius,
        CancellationToken token)
    {
        try
        {
            var picture = await RenderHeroAsync(pixels, radius, token, _heroCacheTicket, _heroTiming)
                .ConfigureAwait(true);

            if (Attached && generation == _heroBlurGeneration && picture is not null)
            {
                _heroRendering = false;
                HeroImage = picture;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"背景图按裁切重糊失败：{error.Message}");
        }
        finally
        {
            if (generation == _heroBlurGeneration && _heroRendering)
            {
                _heroRendering = false;
                _heroBlurRadius = -1;
            }
        }
    }

    /// <summary>
    /// 海报那一栏（<c>PosterStill</c>）这一次画多宽 —— 图到手时按它自己的形状收窄
    /// （<see cref="DetailHero.StillBox"/>，见 <see cref="StillHeight"/>）。
    /// <para>
    /// 尾部那一段的内边距也跟着它走（<see cref="TailInset"/> → <see cref="StackLeft"/>）：集页的宽版式上剧情
    /// 说明要和片名那一栏同一条左沿，而那条沿就是这一栏的宽加一格间距决定的 —— 图到得晚一点，那一段也就晚
    /// 一点对齐，两个数读的是同一个来源。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    public partial double StillWidth { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroContent))]
    [NotifyPropertyChangedFor(nameof(HeroRoom))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMargin))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(HeroInset))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    public partial double StillHeight { get; set; }

    /// <summary>
    /// 右上角那张艺术图这一次画多高 —— 图到手时由 <see cref="ShowCorner"/> 按它的形状算进这个上限盒子里
    /// （<see cref="DetailHero.StillBox"/>，默认 360×202.5，宽窗逐步放大），带高就跟着它长。
    /// <para>
    /// 180 → 270 这一档是「放大集页面右侧的艺术图」（2026-09-12）的直接后果：从前的上限 180 比那一叠字键的
    /// 实测高（两百六七）矮，所以它从来不决定带子的高，也就没有这个数；抬到 270 之后它比那一叠还高，不把它算
    /// 进去就是这张画压在底下的音轨那一行上。
    /// </para>
    /// <para>
    /// 图还没到、或者这一条没有艺术图时是 0 —— 那一档带高仍由字键那一叠和剧照给。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroContent))]
    [NotifyPropertyChangedFor(nameof(HeroRoom))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMargin))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(HeroInset))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    public partial double CornerHeight { get; set; }

    /// <summary>
    /// 这一页要不要按「背后铺着一张图」那一档布 —— 服务器上有没有那张图（<see cref="ItemArtwork.Hero"/> 问标签
    /// 表的结果），加上集页这次的模式：沉浸宽窗铺图，简洁宽窗合成卡面，紧凑版式沿用背景图。
    /// 判据只写一份，在
    /// <see cref="SpreadsBackdrop"/>。
    /// <para>
    /// 分成两个值是为了「点击主页封面后窗口会闪一下，然后才会进入页面」。版面按有没有这张图分档
    /// （背景层、尾部、纸面下限，<see cref="DetailHero.Height"/>）；拿解好的位图当判据，那一下就是先按
    /// 没有图布一遍、图到了再翻一遍，屏上看着就是闪一下。列表接口回来的条目已经带着标签，所以这句话在
    /// <see cref="Attach"/> 那一刻就答得出：第一帧的版面就是最后的版面，之后到的只是画面本身。
    /// </para>
    /// <para>
    /// 「哪套版式」那一半是唯一会在同一页上翻的一次，翻的时机在 <see cref="OnPageWidthChanged"/> 里
    /// （<see cref="SyncBackdrop"/>）；跨过紧凑断点那一帧的版面跟着翻，和整套版式换掉是同一拍。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackdropShown))]
    [NotifyPropertyChangedFor(nameof(Showcase))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(HeroInset))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(PaperMinHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(HeroArtVisibility))]
    [NotifyPropertyChangedFor(nameof(HeroPlainVisibility))]
    [NotifyPropertyChangedFor(nameof(ScrimVisibility))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    [NotifyPropertyChangedFor(nameof(SheetPadding))]
    [NotifyPropertyChangedFor(nameof(SheetSpacing))]
    [NotifyPropertyChangedFor(nameof(BodyPanelPadding))]
    [NotifyPropertyChangedFor(nameof(ShowShelfLines))]
    public partial bool HeroArt { get; set; }

    /// <summary>
    /// 背景那一层这一次画不画 —— 只问一句：服务器上有这张图（<see cref="HeroArt"/>）。
    /// <para>
    /// 从前这里还有第二句「页面还宽到压得住它」（<c>DetailHero.BackdropFits</c>，窄过 720 整层收起来）。
    /// 「窗口收窄时背景图要等比例缩放」把那个分岔去掉了：图改画在跟宽走的盒子里（<see
    /// cref="DetailHero.PictureHeight"/>）之后，任何宽度都摆得下一张等比的画面，收起来那一档没了 —— 窄到
    /// <see cref="DetailHero.CompactFloor"/> 以下换的是整套紧凑版式，背景那张图照画，只是缩成一条。
    /// </para>
    /// <para>
    /// 所有按「有没有背景图」分档的版面问题照旧问它、不直接问 <see cref="HeroArt"/>：没有图的条目上头图退回
    /// 那一档底色版面，尾部不撑、纸面不整屏、标题栏墨色当没有图处理 —— 那些迁就都还欠着。
    /// </para>
    /// </summary>
    public bool BackdropShown => HeroArt;

    /// <summary>
    /// 这一页看得见的那一段有多高，由视图在每次改尺寸时量给（<c>DetailPage.OnBodySizeChanged</c>）。它自己
    /// 不上屏，是底下那三个下限（<see cref="BodyMinHeight"/>、<see cref="TailMinHeight"/>、
    /// <see cref="PaperMinHeight"/>）共同的那个自变量：视口高只有布好的版面知道，而拿它算什么归这里。
    /// <para>
    /// 头上那一格不看它 —— 三种页面的带高都由内容给（见 <see cref="HeroHeight"/>）。跟着视口走过的两版，一版
    /// 把片名和那排键压到窗口下沿（「图一页面怎么改的一大片空白」），一版在高窗口上把带子撑到 460 而那一叠只有
    /// 两百来高（「集拉大窗口后会导致左上角空空的」）—— 同一个错的两种长相。
    /// </para>
    /// <para>
    /// 反过来，<em>纸的上沿</em>必须跟着它走：不跟的那一版在不同窗口上盖掉的剧照完全不一样（「拉大或拉小窗口
    /// 会导致背景图被遮挡」），见 <see cref="DetailHero.TailHeight"/>。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(PaperMinHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(Showcase))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    [NotifyPropertyChangedFor(nameof(SheetPadding))]
    [NotifyPropertyChangedFor(nameof(SheetSpacing))]
    [NotifyPropertyChangedFor(nameof(BodyPanelPadding))]
    [NotifyPropertyChangedFor(nameof(ShowShelfLines))]
    public partial double Viewport { get; set; }

    /// <summary>
    /// 带子里那一叠字和键（片名、副标题、读数、那排键）实测有多高，由视图量给
    /// （<c>DetailPage.OnHeroStackSizeChanged</c>）。它自己不上屏，是 <see cref="HeroRoom"/> 的自变量。
    /// <para>
    /// 量出来而不是写死：片名折成两行的条目、窄窗口上那一叠会长高，写死一个数就会在那些页面上把字和键挤出
    /// 带子；而剧照比那一叠矮的条目上反过来 —— 写死的那个数在那些页面上就是一格空白。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroContent))]
    [NotifyPropertyChangedFor(nameof(HeroRoom))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(TailMargin))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(HeroInset))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    public partial double StackRoom { get; set; }

    /// <summary>
    /// 左侧封面（<c>PosterStill</c>）的下沿离带子上沿多远，由视图实测（<c>DetailPage.MeasureCoverBottom</c>）；
    /// 没有封面可画（没图、或者这一档不画）时是 0。它自己不上屏，是 <see cref="TailMargin"/> 那条
    /// 「剧情说明第一行不高于封面最下方」的上限（2026-11-02）。
    /// <para>
    /// 量出来而不是拿 <see cref="StillHeight"/> 加一道留白算：那一格在行里是垂直居中的（按钮撑满整行，里面那张
    /// 封面按自己的高居中），片名那一叠长过封面时封面的下沿会比「上留白 + 封面高」低几像素 —— 差的那一点正好是
    /// 用户要守的那条线。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TailMargin))]
    public partial double CoverBottom { get; set; }

    /// <summary>
    /// 尾部那一段（文件选项、操作那排键、剧情说明）的<em>内容</em>要高多少 —— 已经减掉它自己的内边距、块与块
    /// 之间的竖距和每块面板的上下内边距，由视图量给（<c>DetailPage.OnTailContentSizeChanged</c>）。
    /// 它自己不上屏，只喂 <see cref="Squeeze"/> 里那句「剧情说明快掉出画面了没有」—— <see cref="DetailHero.Fit"/>。
    /// <para>
    /// <see cref="DetailHero.Fit"/> 要拿「常态留白」和「最紧留白」两档的总高去解一个比例，两档用同一份
    /// 内容。视图直接累加各面板里的子元素 DesiredSize 与固定内部间距，不包含响应式父级留白；不能用已取整的
    /// 整段高度反扣未取整的 Padding，否则内容读数随留白变化，拖边框时会形成布局循环。
    /// </para>
    /// <para>
    /// <c>HeroTail</c> 有「补满第一屏」的下限（<see cref="TailMinHeight"/>），也不能用它的实际高测内容。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(HeroInset))]
    [NotifyPropertyChangedFor(nameof(HeroRoom))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    public partial double TailIntrinsic { get; set; }

    /// <summary>
    /// 带子里那一叠东西（剧照、片名、副标题、读数、那排键）和右上角那张艺术图里<em>最高</em>的那一个，
    /// 不算这一格自己的上下留白。带高（<see cref="HeroLayoutHeight"/>）和窄窗口那笔「还差多少」
    /// （<see cref="Squeeze"/>）都读它。
    /// <para>
    /// 艺术图和那一叠是**取大**而不是相加：它占的是右栏（只画在集页的宽版式上，<see cref="CornerVisibility"/>），
    /// 谁高听谁的；<see cref="CornerHeight"/> 在这里按 <see cref="HeroCard"/> 分一次档 —— 紧凑版式里那一角
    /// 收着，它的高不该算进带子。
    /// </para>
    /// </summary>
    public double HeroContent =>
        Math.Max(Math.Max(StackRoom, StillHeight), HeroCard ? CornerHeight : 0);

    /// <summary>
    /// 带子里那一叠（剧照、片名、副标题、读数、那排键）连同上下留白实测要占多高。集页那一格的高就是它
    /// （<see cref="DetailHero.EpisodeHeight"/>）；电影、剧、季三页也用它 —— 留白按窗口宽在两档之间取
    /// （<see cref="Squeeze"/>），那一叠长过海报的时候带子跟着长（<see cref="DetailHero.WideHeight"/>）。
    /// <para>
    /// 算在这儿而不在视图里：视图量得到的只有那一叠字键有多高（<see cref="StackRoom"/>，那排键 2026-09-13
    /// 起就是它的最后一个孩子，它的高自然在里面），而「剧照也算进去」「右上角那张艺术图也算进去」「加上
    /// 这一格自己的上下留白」三句是规矩。海报按图自己的形状收窄之后（<see cref="DetailHero.StillBox"/>）
    /// 剧照的高会变，写在视图那个尺寸回调里的那一版只在字键那一叠也跟着变的时候才会重算 —— 带子于是比
    /// 内容高出那么一截，正是「左上角空空的」那种坏法。
    /// </para>
    /// </summary>
    public double HeroRoom => HeroContent + HeroInset.Top + HeroInset.Bottom;

    /// <summary>
    /// 头上那一格的高。宽的一侧是老规矩：高由站在它里面那一叠东西加这一格自己的上下留白定，跟窗口无关
    /// （「图一页面怎么改的一大片空白，改回去」）—— 四个页面都量在运行时：电影、剧、季走
    /// <see cref="DetailHero.WideHeight"/>（常态 352，就是海报加两道边；窗口窄下来时那两道边按
    /// <see cref="Squeeze"/> 收，带子跟着矮、下方整体上移），集页走 <see cref="DetailHero.EpisodeHeight"/>
    /// （单集配的是一张 16:9 剧照，比 2:3 海报矮一大截，跟着用写死的档就是在那一叠头上留上百像素只有画面
    /// 的地方）。
    /// <para>
    /// 紧凑版式（<see cref="IsCompact"/>）分两档，判据是有没有画面可铺 —— 都在
    /// <see cref="DetailHero.CompactHeight"/> 里，连同「没有画面那一档按内容给」的理由。那一档不画封面
    /// （<see cref="DetailHero.ShowsStill"/>），<see cref="StillHeight"/> 随 <see cref="FitStill"/> 归零，
    /// <see cref="HeroRoom"/> 自然回落到文字那一叠。
    /// </para>
    /// </summary>
    public double HeroLayoutHeight => IsCompact
        ? DetailHero.CompactHeight(PageWidth, HeroHeightRatio, HeroRoom, HeroArt)
        : IsEpisodePage
            ? DetailHero.EpisodeHeight(HeroRoom)
            : DetailHero.WideHeight(HeroContent, HeroInset.Top, HeroInset.Bottom);

    /// <summary>
    /// 头部在视口中的高度。从前它等于排出来的高乘一个整体缩放（<c>HeroScale</c>，1024 到 1280 那一段最小缩到
    /// 八成）—— 桌面版式不再整体缩小之后（「收窄窗口后组件要自动换行」）没有这一手了，两者同值。
    /// </summary>
    public double HeroHeight => HeroLayoutHeight;

    /// <summary>
    /// 窗口宽变了（<c>DetailPage.OnBodySizeChanged</c> 量完喊）—— 三件事：封面按新宽重排（集页那一格在紧凑
    /// 版式里跟着收）、角图让位、以及重问一句「这一页这次铺不铺背景图」（<see cref="SyncBackdrop"/>）。
    /// 单集沉浸宽窗铺图，简洁宽窗卡面，紧凑版式保留背景。每一像素都喊，和带高、背景盒高那些绑定同一个
    /// 节奏；做的都是几次乘除加几次属性通知，拖窗口的负载不在这里。
    /// </summary>
    partial void OnPageWidthChanged(double value)
    {
        RefitArtwork();
        SyncBackdrop();
    }

    /// <summary>
    /// 页宽跨过紧凑断点之后重问一句「这一页要不要铺背景图」，答案翻了才写 ——
    /// <see cref="SpreadsBackdrop"/> 从 2026-10-13 起读 <see cref="IsCompact"/>，而 <see cref="HeroArt"/> 自己
    /// 那四个赋值点（<see cref="Attach"/>／<see cref="Preview"/>／<see cref="Apply"/>）都是「换条目」的时机，
    /// 没有人会因为窗口变窄再问一遍。写下去之后生成的 setter 把背景层、底色层、罩子、带高、画面盒高和尾部
    /// 下限一起通知出去，视图那一头 <c>DetailPage</c> 也挂了 <see cref="BackdropShown"/>，跟着重画罩子。
    /// <para>
    /// 只在真翻了的时候写：这个 setter 在拖窗口的每一像素上都会被喊到，而它一写就是半个页面的重排。
    /// </para>
    /// </summary>
    private void SyncBackdrop()
    {
        if (_detail is not { } item) return;

        var wanted = SpreadsBackdrop(item);
        if (wanted != HeroArt) HeroArt = wanted;
    }

    /// <summary>
    /// 封面和角图按现在的像素尺寸各排一遍 —— 封面装进自己那一格的上限盒（<see cref="FitStill"/>，集页那一格在
    /// 紧凑版式里按页宽收），角图按题栏保底让位（<see cref="DetailHero.CornerBoxWidth"/>）。两个时机喊：图到手
    /// （<see cref="ShowStill"/>／<see cref="ShowCorner"/> 先把像素尺寸记下来），和这一页讲的东西换了一种
    /// （<see cref="CoverShown"/> 可能跟着翻，<c>Preview</c> 与 <c>Apply</c> 各一次）。先封面后角图：角图让
    /// 多少位（<see cref="CornerMaxWidth"/>）要看封面这一次多宽。
    /// <para>
    /// 窗口宽变了也喊这一支（<see cref="OnPageWidthChanged"/>）：集页那一格在紧凑版式里跟着页宽收，其余两个数
    /// 不变，重排一遍只是把同样的值再算一次。
    /// </para>
    /// </summary>
    private void RefitArtwork()
    {
        FitStill();
        FitCorner();
    }

    /// <summary>
    /// 封面按图片原始比例装入统一的 260×375 上限盒（<see cref="PosterStillWidth"/>），
    /// 尺寸不随窗口宽走：简洁模式始终显示封面，其他模式只在宽版式显示。
    /// （<see cref="CoverShown"/> —— 紧凑版式下四种页面都不画，2026-10-13「窄窗口下不要封面」），而宽版式的组件
    /// 本来就不跟着窗口缩（「固定集页面封面的大小，拉窄窗口时不要缩小」，同一天的第一句）。不画的档先把宽高归零，
    /// 避免隐藏封面仍占据头部高度。
    /// <para>
    /// 集页封面不再用文字栏高度封顶，否则宽窗下仍会随文字行数变动。
    /// 按键已经放在文字栏内部，不再等待整行的底边；<see cref="HeroRoom"/> 按封面、文字和角图的实际高度
    /// 取大，让下方内容随之让位。
    /// </para>
    /// </summary>
    private void FitStill()
    {
        // 这一档不画封面（<see cref="CoverShown"/>，规则在 DetailHero.ShowsStill）：那一格整个收掉，宽高一并
        // 归零 —— 带高（HeroRoom 读 StillHeight）和片名那一栏的左沿（StackLeft 读 StillWidth）跟着回落到
        // 没有海报的那一档。摆一个看不见的宽高，带子就多垫一段「空海报」的高度。
        if (!CoverShown)
        {
            StillWidth = 0;
            StillHeight = 0;
            return;
        }

        var maxW = (double)PosterStillWidth;
        var maxH = (double)PosterStillHeight;

        if (_stillPixels is { } pixels
            && DetailHero.StillBox(pixels.Width, pixels.Height, maxW, maxH) is { } fit)
        {
            (StillWidth, StillHeight) = fit;
        }
        else
        {
            StillWidth = Math.Round(maxW);
            StillHeight = Math.Round(maxH);
        }
    }

    /// <summary>
    /// 角图那一格按现在的宽排 —— 让多少位由 <see cref="CornerMaxWidth"/> 给（封面的宽进去，题栏的保底出来），
    /// 高按位图自己的形状收进盒子里、喂带高（<see cref="CornerHeight"/> → <see cref="HeroRoom"/>）。
    /// 没有图的时候高回 0，那一栏收着 —— 同换条目时的那两句清空。
    /// </summary>
    private void FitCorner()
    {
        OnPropertyChanged(nameof(CornerMaxWidth));
        OnPropertyChanged(nameof(CornerMaxHeight));
        OnPropertyChanged(nameof(CornerVisibility));

        var maxW = CornerMaxWidth;

        if (_cornerPixels is { } pixels
            && DetailHero.StillBox(
                pixels.Width, pixels.Height,
                maxW, maxW * DetailHero.CornerHeight / DetailHero.CornerWidth) is { } fit)
        {
            CornerHeight = fit.Height;
        }
        else
        {
            CornerHeight = 0;
        }
    }

    /// <summary>
    /// 头图里那几样东西（封面、片名那一栏）靠上还是靠下站。判据就是 <see cref="HeroCard"/>：只有集页的宽版式靠上。
    /// <para>
    /// 集页靠上：那一格的高本来就是它里面那一叠量出来的（<see cref="HeroRoom"/>），靠上就是「内容从这一格的
    /// 上沿起」—— 「把集页面的封面向上移」（2026-09-12）说的就是它：从前靠下站，于是封面头顶上凭空留着百来
    /// 像素，而那正是他圈出来的那一块。右上角那张艺术图也能把这一格撑高（<see cref="CornerHeight"/> 比那一叠
    /// 还高的时候），靠上站的话多出来的一截落在底下，封面和片名照旧贴在上沿。
    /// </para>
    /// <para>
    /// 别的页面靠下：电影、剧、季那三页的带高常态就是内容的高（<see cref="DetailHero.WideHeight"/>，352
    /// 正好装下海报加两道边，富余只有在封面比那一叠矮的条目上才有一点），靠下站就是海报和那一栏字（连同
    /// 尾上的那排键）的下沿对齐 —— 他夸过的就是这一页的样子（「电影那边处理的就很好」）。紧凑版式同理：
    /// 那一档有画面可铺的时候带子就是那条等比的画面（<see cref="DetailHero.CompactHeight"/>，比内容高出来
    /// 的部分是画面），片名压在画面的下沿上，参考图上正是这个样子；没有画面可铺时带子等于内容的高，两个
    /// 对齐摆出来一模一样。
    /// </para>
    /// </summary>
    public VerticalAlignment HeroContentAlignment => HeroCard ? VerticalAlignment.Top : VerticalAlignment.Bottom;

    /// <summary>沉浸单集沿用剧页封面顶对齐；简洁单集保留居中卡面。</summary>
    public VerticalAlignment CoverAlignment => IsEpisodePage && !Showcase ? VerticalAlignment.Center : VerticalAlignment.Top;

    /// <summary>
    /// 右上角艺术图这一次的上限盒宽，默认 360，窗口超过 1280 后逐步放大至 540，
    /// （<see cref="DetailHero.CornerWidth"/>，这个数只在那里写一遍），以下让位给题栏
    /// （<see cref="DetailHero.CornerBoxWidth"/>：封面的宽进去，题栏的保底出来）。取图那一头
    /// （<see cref="ShowCorner"/> → <see cref="FitCorner"/>）读的也是它，两处不会分叉。
    /// 谁的手换了它的输入，谁负责喊它：<see cref="FitCorner"/>（页面宽和封面的宽都在它那儿汇齐）。
    /// </summary>
    public double CornerMaxWidth => DetailHero.CornerBoxWidth(PageWidth, StillWidth, _cornerScale);

    /// <inheritdoc cref="CornerMaxWidth"/>
    public double CornerMaxHeight => CornerMaxWidth * DetailHero.CornerHeight / DetailHero.CornerWidth;

    /// <summary>窗口和显示器的宽度都使用 DIP；侧栏只影响题栏让位，不影响放大进度。</summary>
    internal void SetCornerViewport(double windowWidth, double maximumWidth)
    {
        var scale = DetailHero.CornerScale(windowWidth, maximumWidth);
        if (_cornerScale == scale) return;

        _cornerScale = scale;
        FitCorner();
    }

    /// <summary>
    /// 背景那一张这次画多高 —— <see cref="DetailHero.PictureHeight"/>：宽窗铺满视口，窄窗整张等比缩，带子是
    /// 它的下限。画在 <c>DetailPage</c> 标记里那个跟宽走的盒子（<c>BackdropPicture</c>）上，层本身照旧铺满
    /// 整个视口 —— 盒子底下多出来的那段交给压暗的尾部。
    /// <para>
    /// <b>沉浸版式（<see cref="Showcase"/>）里它一律铺到窗口下沿。</b>那一档正文不再有纸面接着，背景要从片名一直
    /// 透到页尾；按页宽给高的话，比 16:9 更竖的窗口上画面只占第一屏上面那一条，下方组件就压在一片页底色上
    /// （2026-10-31 在竖屏上实拍到的黑带）。盒子仍然铺满整页、UniformToFill 照旧裁中间那一段 —— 反正这一层
    /// 是压暗过的模糊背景，裁掉的不是任何要读的东西。
    /// </para>
    /// <para>
    /// 加 <see cref="HeaderLift"/> 是因为背景那一层被顶上去越过标题栏了（<c>DetailPage.LiftBackdrop</c> 给它
    /// 一个负的上边距）：盒子从窗口上沿起画，视口高只到标题栏下面那一线，差的那一截就是窗口下沿的一条黑边
    /// （2026-10-31 实拍：底下 48 像素是页底色）。
    /// </para>
    /// </summary>
    public double PictureHeight => Showcase && Viewport > 0
        ? Math.Round(Viewport + Math.Max(0, HeaderLift))
        : DetailHero.PictureHeight(PageWidth, HeroHeightRatio, Viewport, HeroHeight);

    /// <summary>
    /// 这一档版面要不要封面 —— 规则在 Core（<see cref="DetailHero.ShowsStill"/>）：紧凑版式下一律不画
    /// （2026-10-13「窄窗口下不要封面」，四种页面同一个答案；在那之前是「剧、电影两页不画，季、集照旧」）。
    /// 图解出来没有不算在这里 —— 那是 <see cref="StillVisibility"/> 自己的另一半。
    /// </summary>
    private bool CoverShown => DetailHero.ShowsStill(IsCompact);

    /// <summary>
    /// 带子左边那张海报（集页上是剧照）显不显 —— 两句话合起来：「图解出来了没有」，和「这一档版面要不要它」
    /// （<see cref="CoverShown"/>）。
    /// <para>
    /// 第二句的规矩翻过好几回（2026-09-12 一天四句，来龙去脉记在 <see cref="DetailHero.ShowsStill"/> 那边），
    /// 最后落在 2026-10-13 的「窄窗口下不要封面」：紧凑版式下四种页面都不画，宽版式四页全画。规则本体在
    /// <see cref="DetailHero.ShowsStill"/>，这里只照着问。
    /// </para>
    /// <para>
    /// 不画的那一档 <see cref="FitStill"/> 把那一格的宽高一并归零：带高（<see cref="HeroRoom"/> 读
    /// <see cref="StillHeight"/>）和片名那一栏的左沿（<see cref="StackLeft"/> 读 <see cref="StillWidth"/>）
    /// 跟着回落到没有海报的那一档。
    /// </para>
    /// </summary>
    public Visibility StillVisibility => Show(StillImage is not null && CoverShown);

    /// <summary>
    /// 窗口窄到什么程度 —— 0 是常态，1 是最紧，曲线在 <see cref="DetailHero.Narrow"/>。
    /// <see cref="Squeeze"/> 拿它当底，再叠上「剧情说明快掉出画面」那一条；它自己也单独可读，好让自检分清
    /// 「窗口窄」和「装不下」两件事。
    /// </summary>
    public double Narrow => Crowdable ? DetailHero.Narrow(PageWidth) : 0;

    /// <summary>
    /// 这一页这一次把竖留白收多紧 —— 0 是常态，1 是最紧。两条来路取大：
    /// <list type="number">
    /// <item><see cref="Narrow"/>：窗口越窄越紧凑（1440 起收，窄到紧凑断点 1024 收到最紧）。</item>
    /// <item><see cref="DetailHero.Fit"/>：尾部最后那一块（剧情说明）快掉出背景图时，整页的竖留白一起再往上
    /// 收 —— 头部两道、尾部上沿、块间距、面板内边距，四组按同一个比例收；收完还不够就让它探出画面
    /// （用户 2026-11-02 的话：「无法再收缩时允许剧情说明超出背景图范围」）。</item>
    /// </list>
    /// 只对电影、剧、季三页的宽版式成立：集页整段合成在一块板上，紧凑版式有自己的那套数。
    /// <para>
    /// 四组用的是同一个比例，屏上才是「整体往上收」而不是某一道先塌。解这个比例的原料是<em>内容</em>高
    /// （<see cref="TailIntrinsic"/> 已经把当前的留白减掉）和两档留白的账 —— 跟这一次收到哪儿无关，所以不会
    /// 出现「收 → 尾变矮 → 算出不用收 → 放」那种来回抖。结果量化到 0.01：尾高是量出来的，会带零点几像素的
    /// 噪声；1% 一档换算成带高不到半个像素。
    /// </para>
    /// </summary>
    public double Squeeze => Crowdable
        ? Math.Round(Math.Max(Narrow, FitShare), 2)
        : 0;

    /// <summary>
    /// 这一页走不走宽窗可收缩留白 —— 沉浸单集与电影、剧、季共用；简洁卡面留白另有一档。
    /// 紧凑版式那一套在 <see cref="DetailHero.CompactHeight"/> 里。
    /// <para>
    /// 沉浸档不在这里单独排除：那一档尾部的 8／8 优先（见 <see cref="TailSpacing"/> 与
    /// <see cref="TailPanelPadding"/>），头部这两道留白仍按宽度收 —— 短窗口上本来就更该紧凑。代价是
    /// <see cref="FitShare"/> 在那一档拿纸面坐标算出来的比例可能偏大，最多把头部收到最紧；那一档画面铺满
    /// 整页，收一点头部不会让任何东西掉出画面。
    /// </para>
    /// </summary>
    private bool Crowdable => !IsCompact && !HeroCard
        && ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    /// <summary>
    /// 尾部这一段里有几块（文件选项、紧凑块、剧情说明）真的画着 —— <see cref="StackPanel.Spacing"/> 只算在
    /// 可见的孩子之间，面板内边距也只有画出来的那几块才占地方。
    /// </summary>
    private int TailPanels =>
        (PickersVisibility == Visibility.Visible ? 1 : 0)
        + (CompactVisibility == Visibility.Visible ? 1 : 0)
        + (OverviewVisibility == Visibility.Visible ? 1 : 0);

    /// <summary>
    /// 尾部这一段的竖留在某一档收口下共占多少 —— 上沿 <see cref="DetailHero.Between"/> 出来的那一道、底下固定
    /// 的 4、可见块之间的竖距、每块面板的上下内边距。只算竖的：左右那两道不参与窄窗那笔账。
    /// </summary>
    private double TailVertical(double squeeze)
    {
        var panels = TailPanels;
        var top = DetailHero.Between(DetailHero.TailTopRoom, DetailHero.TailTopTight, squeeze);
        var gap = DetailHero.Between(DetailHero.TailGapRoom, DetailHero.TailGapTight, squeeze);
        var pad = DetailHero.Between(DetailHero.TailPadRoom, DetailHero.TailPadTight, squeeze);

        return top + 4 + (Math.Max(panels, 1) - 1) * gap + (panels * 2 * pad);
    }

    /// <summary>背景图下沿在滚动内容里的纵坐标 —— 和 <see cref="TailMinHeight"/> 同一个折算。</summary>
    private double PictureBottom =>
        DetailHero.PictureHeight(PageWidth, HeroHeightRatio, Viewport, 0) - HeaderLift;

    /// <summary>
    /// 「光靠窗口窄还不够、整页得再往上收多少」—— <see cref="DetailHero.Fit"/> 的回答，0..1。
    /// <para>
    /// 两档总高都用 <see cref="TailIntrinsic"/>（已经减掉当前留白的内容高），所以这个数与当前收到哪儿无关；
    /// <see cref="DetailHero.PictureHeight"/> 的带高那一档传 0，量的是<em>图自己</em>能铺多高 —— 带子高的时候
    /// 它会被垫到视口，拿它当「有没有画面」用就成了自己答自己。
    /// </para>
    /// <para>
    /// 尾高还没量到（0）时不收：那一拍按宽度那条曲线布，量到了再补。背后没有那张图时也不收 —— 没有可躲的
    /// 画面，拿一个并不存在的下沿去挤正文只会白收一档（宽度那条曲线照旧）。
    /// </para>
    /// </summary>
    private double FitShare
    {
        get
        {
            if (!Crowdable || !BackdropShown || TailIntrinsic <= 0 || Viewport <= 0) return 0;

            var intrinsic = TailIntrinsic;
            var roomy = HeroContent + DetailHero.HeroTopRoom + DetailHero.HeroBottomRoom
                + intrinsic + TailVertical(0);
            var tight = HeroContent + DetailHero.HeroTopTight + DetailHero.HeroBottomTight
                + intrinsic + TailVertical(1);

            return DetailHero.Fit(roomy, tight, PictureBottom);
        }
    }

    /// <summary>
    /// 带子里那一叠字和键四周的留白。集页把底下那道收到 16 —— 「为什么中间要留空，导致下方的剧情说明
    /// 看不到？」：底下紧跟着音轨那一行和那一带集，多留就是纯粹的空气。
    /// <para>
    /// 其余页面那道底下的留白是同一笔旧账：它本来是给「这一格铺到窗口下沿」写的（贴着窗口边读着像被截了
    /// 一截）。背景改成等比画面之后带子底下紧跟着尾部那几块，「组件不够紧凑」（2026-09-12）说的就是这一截 ——
    /// 收到 24，和页边那 60、顶上那 28 一个量级。
    /// </para>
    /// <para>
    /// 默认宽详情封面与剧情左沿统一32；顶部20接面包屑模板留白，封面上距与左距一致。
    /// 左沿取剧情说明的实际文字左沿；右侧补回同样的距离，保持封面、片名与按钮区域原有宽度。
    /// </para>
    /// <para>
    /// 紧凑版式整档换成窄边：左右 20 是参考图上那种贴边一列的留法，60 的「压在剧照上的内容多让一点」是
    /// 宽页面的讲究 —— 单列的窄页面上它就是把字挤到中间去的空气。
    /// </para>
    /// <para>
    /// 集页那块板上（<see cref="HeroCard"/>）左右收到 48：板的外沿在 28，28 + 20 就是板上内容的左边 ——
    /// 和底下 <see cref="TailInset"/> 那 48 同一竖线，海报、片名、播放键和音轨那一行因此全落在板的同一条
    /// 边上。60 那一档的讲究是「压在剧照上的内容多让一点」，而板上没有剧照要躲。
    /// </para>
    /// <para>
    /// 集页宽版式连底下那道 16 也收成 0（2026-11-02「自动剪裁下方多余的空白区域」）：剧情说明提上去之后
    /// （见 <see cref="TailMargin"/>），带子底下那一道留白已经没有任何东西可隔 —— 它只剩下封面下沿到板底之间
    /// 的一截空气。板底要的那「少量边距」由 <see cref="CardTailGapHeight"/> 那 16 和说明自己的 8 出，所以这里
    /// 归零不会让字贴到板沿上。其余页面照旧 16／24。
    /// </para>
    /// </summary>
    public Thickness HeroInset => IsCompact
        ? new(20, 20, 20, 16)
        : HeroCard
            ? new(48, 28, 48, 0)
            : IsEpisodePage && !Showcase
                ? new(60, 28, 60, 16)
                : new(
                    TailInset.Left + OverviewPanelPadding.Left,
                    DetailHero.Between(DetailHero.HeroTopRoom, DetailHero.HeroTopTight, Squeeze),
                    88,
                    DetailHero.Between(DetailHero.HeroBottomRoom, DetailHero.HeroBottomTight, Squeeze));

    /// <summary>
    /// 带子底下那一段的内边距。同 <see cref="HeroInset"/>：集页把上面那道收到 12，那一叠键和「音频」
    /// 之间因此只隔一小截，和那一段里几块之间的间距同一个量级；其余页面 14，窗口窄下来时按
    /// <see cref="Squeeze"/> 收到 10。底下 4 —— 和正文纸面之间本来就没有可看的缝，纸面自己那 20 会接着垫。
    /// <para>
    /// 左右 20 是「组件不够紧凑」之后从 28 收下来的：24 的板内边距加在这里，字落在 44 的竖线上，和底下
    /// 纸面（20 + 24）正好同一条。
    /// </para>
    /// <para>
    /// 紧凑版式左右只给 2：这一段里每块板（<c>EgBodyPanelStyle</c>）自带 24 的内边距，2 + 24 正好是头图上
    /// 那一叠的 20（见 <see cref="HeroInset"/>）—— 片名、文件选项和播放键落在同一条竖线上，单列版式靠的
    /// 就是这条线站直。
    /// </para>
    /// <para>
    /// 集页那块板上（<see cref="HeroCard"/>）左右同样收到 48（28 的板沿加 20 的内边距，两处同一竖线），
    /// 底下给到 0 —— 那一块最后只剩剧情说明，而说明被提上去之后（见 <see cref="TailMargin"/>），板底到字之间
    /// 只剩说明自己的 8 加尾部最后那一格的 16（<see cref="OverviewPanelPadding"/>／<see
    /// cref="CardTailGapHeight"/>）：用户 2026-11-02 要的「剪裁下方空位、留点空位」。
    /// </para>
    /// <para>
    /// 集页的宽版式再往右挪一栏（<see cref="StackLeft"/>）：那一页这一段里只剩剧情说明（音频字幕搬进片名
    /// 那一栏了，见 <see cref="ColumnPickersVisibility"/>），它得和片名、那一排键站在同一条左沿上 ——
    /// 截图里那几行就是这么对齐的（「按键布局参考上方截图」，2026-09-12）。
    /// </para>
    /// </summary>
    public Thickness TailInset => IsCompact
        ? new(2, 12, 2, 8)
        : HeroCard
            ? new(StackLeft, 12, PlainDetail ? 48 : StackLeft, 0)
            : new(
                8,
                IsEpisodePage && !Showcase
                    ? 12
                    : DetailHero.Between(DetailHero.TailTopRoom, DetailHero.TailTopTight, Squeeze),
                32,
                4);

    /// <summary>
    /// 集页宽版式：剧情说明那一块的顶要落在「剩余 x 分钟」底下 —— 尾部那一段（<c>TailContent</c>）的负上边距；
    /// 别的页面、紧凑版式、没量到那一叠、没有剧情说明时都是 0。
    /// <para>
    /// 集页的带高按封面和片名那一叠取大（<see cref="HeroHeight"/>）；封面高过那一叠时，带子底下多出来的那一截全落在
    /// 片名那一栏以外的左侧空气里，而剧情说明原本接在带子下沿之后 —— 它离「剩余 x 分钟」隔着一整截空气
    /// （2026-11-02 用户圈出来的那块红框）。提的量见 <see cref="DetailHero.TailPull"/>：有封面时说明的第一行跟随
    /// <see cref="CoverBottom"/> 那条线（2026-11-02 同日「不要固定间距，以封面下沿为准」），封面下沿离那一行比
    /// <see cref="DetailHero.OverviewMinGap"/> 还近、或者没有封面时才退回那一行底下的最小间距。2026-11-05 两种定位都再多留 8。提上来的量同时从尾部
    /// 自己的高里扣掉，所以板底（<c>HeroTail</c> 的下沿）跟着内容收 ——「自动剪裁下方空位」。字底下那「一点空位」
    /// 是说明自己的 <see cref="OverviewPanelPadding"/> 那 8 加尾部最后那一格 <see cref="CardTailGapHeight"/> 的 16：
    /// 单靠说明那 8 撑不出板底那两个 16 的圆角，尾巴收成直角就不像这块板了。
    /// </para>
    /// <para>
    /// 封面的下沿由页面实测（<see cref="CoverBottom"/>）：那一格在行里垂直居中，不能用「上留白加封面高」算 ——
    /// 片名那一叠比封面高时封面会往下让半个差值，算出来的线会比屏上的高一截。
    /// </para>
    /// <para>
    /// 负边距把这一块画到带子下沿以上：那一段背后是集页那块大板的上半截（<c>HeroCardTop</c>，和尾部那张底同一个
    /// 画刷），接缝看不出来；封面在左、说明在片名那一栏的竖线上，两者不重叠。
    /// </para>
    /// </summary>
    public Thickness TailMargin
    {
        get
        {
            if (!HeroCard || OverviewVisibility != Visibility.Visible) return default;

            var pull = DetailHero.TailPull(
                HeroHeight, HeroInset.Top, StackRoom, TailInset.Top, OverviewPanelPadding.Top,
                DetailHero.OverviewMinGap, CoverBottom);

            return pull > 0 ? new Thickness(0, -pull, 0, 0) : default;
        }
    }

    /// <summary>
    /// 尾部最下面那一格的高（<c>CardTailGap</c>）—— 只在集页那块宽版式的板上出现，别的页面是 0，不占一像素。
    /// <para>
    /// 它不是又一段留白，是给板底那两个圆角撑出来的一截板面：<see cref="TailMargin"/> 把尾部整段往上提，提得多时
    /// 尾部自己的高所剩无几，<c>HeroCardBottom</c> 跟着矮，屏上就是一块下沿直角的板。留 16（= 那一份圆角半径），
    /// 圆角照旧画得出来，而字到板底仍然是 <see cref="OverviewPanelPadding"/> 那 8 加这一格的 16。
    /// </para>
    /// </summary>
    public double CardTailGapHeight => HeroCard ? DetailHero.CardTailGap : 0d;

    /// <summary>
    /// 尾部那几块（文件选项、播放那排键和进度、剧情说明）之间的竖距（<c>TailContent.Spacing</c>）——
    /// 紧凑版式收一档，电影、剧、季三页的宽版式跟着窗口窄下去再收一档。
    /// <para>
    /// 2026-10-13「窄窗口下这些组件上下之间弄紧凑一点，改动包括电影、电视剧、季、集页面」：单列版式里这一段
    /// 是一条从画面下沿一路排到纸面的窄带，12 落在宽版式那 60 的页边和 24 的板内边距之间看不出来，落在紧凑
    /// 版式这 20 的页边和 8 的板内边距之间就是一段接一段的空。四页共用这一支。
    /// </para>
    /// <para>
    /// 2026-10-31「不够紧凑」：沉浸档（启用时的电影/剧/季宽窗）也走 8 —— 那一档纸面和面板都让开了，宽版式那
    /// 24 的内边距不再衬出「板里的板」，留着只是把下方组件往下推。
    /// </para>
    /// <para>
    /// 2026-11-02：电影、剧、季的宽版式再走一档 <see cref="Squeeze"/>（12 收到 6）。收的是「窗口越窄布局
    /// 越紧凑」，最紧那档比沉浸档的 8 还小 —— 那一档有纸面接着，块与块之间不需要那 2 像素的余量。
    /// </para>
    /// </summary>
    public double TailSpacing => IsCompact || Showcase
        ? 8
        : DetailHero.Between(DetailHero.TailGapRoom, DetailHero.TailGapTight, Squeeze);

    /// <summary>
    /// 尾部那三块面板自己的上下内边距 —— <c>EgBodyPanelStyle</c> 那 24 在这里换成 <see cref="IsCompact"/> 的那一档
    /// （标记里三块各绑一次，样式表里的 24 留给别的页面的面板）。
    /// <para>
    /// 那三块外圈和底色都不画（「移除音频和剧情说明那两个栏的框」），内边距因此是它们唯一的可见效果：宽版式上
    /// 24 把一块的字和上下两块隔开，是「板里的板」那一套；紧凑版式里三块一条线叠着，24 上下相加就是一段 48 的
    /// 空，加上 <see cref="TailSpacing"/>，一屏里白白吃掉一百多像素。
    /// </para>
    /// <para>
    /// 左右仍是 24 不动：<see cref="TailInset"/> 那 2 是照着「2 + 24 落在头图那一叠的 20 上」定的
    /// （见那一支的注释），把横向那一份一起收掉，片名、文件选项和播放键那条竖线就散了。
    /// </para>
    /// <para>
    /// 沉浸档同紧凑档：那一档没有纸面，24 的上下留白在模糊背景上就是一块空。
    /// </para>
    /// <para>
    /// 2026-11-02：电影、剧、季的宽版式在窗口窄下来时按 <see cref="Squeeze"/> 从 24 收到 8（就是紧凑档那一档，
    /// 不是新编的数）—— 这三块横着没有底也没有框，上下那 24 是块与块之间唯一的一道缝，收它正是「越窄越紧凑」
    /// 里最值钱的一笔。
    /// </para>
    /// </summary>
    public Thickness TailPanelPadding => IsCompact || Showcase
        ? new(24, 8, 24, 8)
        : new(
            24,
            DetailHero.Between(DetailHero.TailPadRoom, DetailHero.TailPadTight, Squeeze),
            24,
            DetailHero.Between(DetailHero.TailPadRoom, DetailHero.TailPadTight, Squeeze));

    /// <summary>
    /// 剧情说明那一块自己的上下内边距 —— 集页宽版式底下收成 8。
    /// <para>
    /// 说明被提上去之后（<see cref="TailMargin"/>），板底由尾部最后那 16 的收尾格（<c>CardTailGap</c>，专门给
    /// 圆角留的高度）画圆角；8 + 16 = 24 就是「剪裁下方空位（留点空位）」那一句里的「一点空位」。其余页面
    /// 用 <see cref="TailPanelPadding"/> 那一份，一个数都不动。
    /// </para>
    /// </summary>
    public Thickness OverviewPanelPadding => HeroCard ? new(24, 24 + DetailHero.OverviewExtraGap, 24, 8) : TailPanelPadding;

    /// <summary>
    /// 正文纸面那一层的内边距（<c>BodySheet.Padding</c>）—— 默认宽详情左8加面板24，内容与封面左沿32对齐。
    /// <para>
    /// 纸面上沿紧跟着尾部最后一块（剧情说明），28 加上底下第一块自己的 24 就是「简介到 更多来自 之间那一大段
    /// 空」的大半（2026-10-13 的截图里最显眼的一处）。左右同为8，配面板内24形成对称32的内容留白；单集和紧凑档保留原左右28。
    /// </para>
    /// <para>
    /// 沉浸档把上下收到 10 / 14：那一档没有纸，28 只是把第一块货架从剧情说明底下推开。
    /// </para>
    /// </summary>
    public Thickness SheetPadding => IsCompact ? new(28, 18, 28, 18)
        : HeroCard ? new(28)
        : Showcase ? new(8, DetailHero.ShowcaseSheetTop, 8, DetailHero.ShowcaseSheetBottom)
        : new(8, 28, 8, 28);

    /// <summary>
    /// 纸面上那几块（单集、媒体信息、剧季、相似、演职人员）之间的竖距（<c>BodySheet.Spacing</c>）——
    /// 紧凑版式收一档，同 <see cref="TailSpacing"/>；沉浸档再紧一档（8），理由同 <see cref="TailSpacing"/>。
    /// </summary>
    public double SheetSpacing => Showcase ? DetailHero.ShowcaseSheetSpacing : IsCompact ? 14 : 20;

    /// <summary>
    /// 正文那几块货架面板自己的上下内边距（标记里每块各绑一次）—— 沉浸档从样式的 24 收到 10。
    /// <para>
    /// 那几块在沉浸档里没有底也没有框（<c>DetailPage.Showcase.cs</c> 把它们的画刷淡到 0），24 的上下留白于是
    /// 只剩「把两块货架隔开」一个作用，而隔着模糊背景读起来就是一截空。左右不动，货架标题和卡片的左沿不变。
    /// </para>
    /// </summary>
    public Thickness BodyPanelPadding => Showcase
        ? new(24, DetailHero.ShowcasePanelPadding, 24, DetailHero.ShowcasePanelPadding)
        : new(24);

    /// <summary>
    /// 片名那一栏里那两栏之间的一格间距（<c>HeroContent.ColumnSpacing</c>，标记里绑的是这一个数）。
    /// <see cref="StackLeft"/> 要它，所以它不能只写在标记里。
    /// </summary>
    public double ColumnSpacing => 22;

    /// <summary>
    /// 片名那一栏（<c>HeroStack</c>）的左沿离本页左边有多远 —— <see cref="HeroInset"/> 那一道，加上海报那一栏
    /// 自己的宽（<see cref="StillWidth"/>）和两栏之间那一格（<see cref="ColumnSpacing"/>）。
    /// <para>
    /// 海报没画出来的时候不加：那一栏是 Auto、收起来就是 0 宽（<see cref="StillVisibility"/>），片名那一栏
    /// 直接站到板的内沿上 —— 这里照着同一句话算，屏上那一条沿才不会差一格。
    /// </para>
    /// <para>
    /// 尾部那一段（<see cref="TailInset"/>）按它对齐；那排键 2026-09-13 起就站在片名那一栏的叠里，跟着
    /// 同一条左沿，不用再另算 —— 截图里片名、音频字幕、那排键和剧情说明是同一条左沿。
    /// </para>
    /// </summary>
    private double StackLeft => HeroInset.Left
        + (StillVisibility == Visibility.Visible ? StillWidth + ColumnSpacing : 0);

    /// <summary>
    /// 带子下沿那道渐深罩子这一次有多高 —— <see cref="DetailHero.ScrimSpan"/>：带子收窄了它就跟着收，
    /// 不然它会从带子的上沿溢出去，而标题条上那层洗（<see cref="DetailHero.TopWash"/>）算的是收完的那一块。
    /// </summary>
    public double ScrimHeight => DetailHero.ScrimSpan(HeroHeight);

    /// <summary>
    /// 正文那张纸的下限 —— 见 <see cref="DetailHero.BodyHeight"/>：内容短的页面上把看得见的那一段补满，
    /// 「滑到下面不用显示背景」靠的是这张纸真的盖住了背后那张图。有剧照的那一档富余高度改由
    /// <see cref="TailMinHeight"/> 吃掉，这一支管的是没有剧照的那一档。
    /// </summary>
    public double BodyMinHeight => DetailHero.BodyHeight(Viewport, HeroHeight);

    /// <summary>
    /// 纸面上沿的线，视口坐标 —— 阈值窗口的高（<see cref="DetailHero.PaperLineFor"/>，跟着显示器走：4K 屏
    /// 1080、2K 屏 900、1080p 屏 768）减掉标题栏加面包屑那截，由页面量好送进来（<c>DetailPage.SyncPaperLine</c>）。
    /// 0 是「还没送到」：那一档不撑尾部（<see cref="DetailHero.TailHeight"/> 的约定），纸面回到由内容定的位置。
    /// <para>
    /// 换显示器、换主题（面包屑那截的高跟着字号走）、窗口换尺寸都会让这个数变，所以由视图在那几处显式重写，
    /// 而不是算出来的属性。
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    public partial double PaperLine { get; set; }

    /// <summary>
    /// 标题栏加面包屑让出来的那一截有多高，由页面量好送进来（<c>DetailPage.LiftBackdrop</c>，和
    /// <see cref="PaperLine"/> 同一处量的同一个数）。0 是「还没量到」。它只喂一件事：背景画面盒站在窗口的
    /// 上沿，它的下沿折进滚动内容的坐标要减掉这一截 —— 见 <see cref="TailMinHeight"/>。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(Squeeze))]
    [NotifyPropertyChangedFor(nameof(HeroInset))]
    [NotifyPropertyChangedFor(nameof(HeroRoom))]
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailInset))]
    [NotifyPropertyChangedFor(nameof(TailSpacing))]
    [NotifyPropertyChangedFor(nameof(TailPanelPadding))]
    [NotifyPropertyChangedFor(nameof(OverviewPanelPadding))]
    public partial double HeaderLift { get; set; }

    /// <summary>
    /// 头图底下那段压暗的尾部的下限 —— 见 <see cref="DetailHero.TailHeight"/>：尾部先补满第一屏减掉带子那么多，
    /// 于是正文那张纸从第一屏的下沿起，「拉大或拉小窗口」都不会把那道不透明的边提到剧照上。撑到纸面那条线
    /// （<see cref="PaperLine"/>，窗口高过阈值）就不再撑，富余的高度归纸 —— 不然剧情说明底下那段空画面会跟着
    /// 窗口一起长（「下面越改空位越大」）。过了那条线之后纸就跟着窗口一像素一像素地露出来，中间没有台阶
    /// （「拉大窗口之后下面突然冒出一大截」）。
    /// <para>
    /// 还有第三道上限：背景那一张的下沿。尾部是「压暗的画面」，等比画面条底下已经没有画面，撑过去就是一段
    /// 空位 —— 纸面和货架被压到老下面去，中间空着一大块（他 2026-09-12 指着的那张截图）。撑到画面下沿为止，
    /// 底下的空当交给正文的内容自动补上。坐标系要折一次：画面盒顶在窗口的上沿（<c>DetailPage.LiftBackdrop</c>），
    /// 它的下沿折进滚动内容的坐标得减掉标题栏加面包屑那一截（<see cref="HeaderLift"/>）—— 从前直接拿
    /// <see cref="PictureHeight"/> 当下沿，尾部永远多撑出一截标题栏高的空黑，那截空黑加纸面上的留白正是
    /// 「黑色渐变下方不要留太大的空位」框住的那一块（2026-09-13）。
    /// </para>
    /// </summary>
    public double TailMinHeight => DetailHero.TailHeight(
        Viewport, HeroHeight, BackdropShown, PaperLine, PictureHeight - HeaderLift, Showcase);

    /// <summary>
    /// 正文那张纸自己的下限 —— 见 <see cref="DetailHero.PaperHeight"/>：滚到底的那一屏只能有纸。
    /// <para>
    /// 只跟着 <see cref="Viewport"/> 和 <see cref="HeroArt"/> 走，所以不进 <see cref="AnnounceShape"/>：那两个
    /// 都是 observable 的，值真变了就会自己喊一声。<see cref="TailMinHeight"/> 不同 —— 它还看带高，而带高跟着
    /// 页面的种类走。
    /// </para>
    /// </summary>
    public double PaperMinHeight => DetailHero.PaperHeight(Viewport, BackdropShown, Showcase);

    /// <summary>
    /// The file 播放 would start: the item itself for a film, the next unwatched episode for a show.
    /// Null for a series or season whose episodes have not arrived, which is what collapses the button.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText))]
    [NotifyPropertyChangedFor(nameof(ResumePlay))]
    [NotifyPropertyChangedFor(nameof(NotResumePlay))]
    [NotifyPropertyChangedFor(nameof(PlayVisibility))]
    [NotifyPropertyChangedFor(nameof(RestartVisibility))]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    [NotifyPropertyChangedFor(nameof(ResumeRemain))]
    [NotifyPropertyChangedFor(nameof(ResumeVisibility))]
    [NotifyPropertyChangedFor(nameof(InfoVisibility))]
    [NotifyPropertyChangedFor(nameof(PickersVisibility))]
    [NotifyPropertyChangedFor(nameof(ColumnPickersVisibility))]
    [NotifyPropertyChangedFor(nameof(WideProgressVisibility))]
    // 有断点没断点翻了宽版式进度那一行（WideProgressShown），其余三页的带高跟着给它留房 ——
    // 带高一变，站在带高上的这一串都得重喊一遍（同 PageWidth 的名单）。
    [NotifyPropertyChangedFor(nameof(HeroLayoutHeight))]
    [NotifyPropertyChangedFor(nameof(HeroHeight))]
    [NotifyPropertyChangedFor(nameof(PictureHeight))]
    [NotifyPropertyChangedFor(nameof(ScrimHeight))]
    [NotifyPropertyChangedFor(nameof(BodyMinHeight))]
    [NotifyPropertyChangedFor(nameof(TailMinHeight))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand))]
    public partial EmbyItem? PlayTarget { get; set; }

    /// <summary>紧凑版式进度条的位置 —— 断点占全片的比例乘满量，没有断点就是 0（那一行整个不画）。</summary>
    public double ProgressValue => (PlayTarget?.ProgressFraction ?? 0) * 100;

    /// <summary>进度条旁边「剩余 x 分钟」那一行 —— <see cref="ItemDetail.Remaining"/>，null 就不画。</summary>
    public string? ResumeRemain => ItemDetail.Remaining(PlayTarget);

    /// <summary>断点进度那一行画不画：有断点才有进度可言。</summary>
    public Visibility ResumeVisibility => Show(PlayTarget?.HasResumePosition == true);

    /// <summary>
    /// 宽版式的断点进度（截图里的「剩余 5 分钟」）画不画 —— 就一份，站在片名那一栏的叠里那排键的底下
    /// （<c>DetailPage</c> 标记里的 <c>WideProgress</c>）。其余三页 2026-09-12 起也有它（「宽窗口缺少播放
    /// 进度条」），2026-09-13 那排键搬进这一叠之后屏上只剩这一份。
    /// <para>
    /// 带高那头不再单独留房：这一行在那一叠里，<see cref="StackRoom"/> 量得到，带高跟着实测走
    /// （见 <see cref="HeroLayoutHeight"/>）—— 没有断点这一行收着，带高一个像素不动。紧凑版式里这一对
    /// （条＋剩余时间）在画面底下那一块里，绑的是不带版式判据的 <see cref="ResumeVisibility"/>。
    /// </para>
    /// </summary>
    public Visibility WideProgressVisibility => Show(WideProgressShown);

    /// <summary><see cref="WideProgressVisibility"/> 的 bool 面 —— 带高那头也读它，版面不吃 Visibility。</summary>
    private bool WideProgressShown => !IsCompact && PlayTarget?.HasResumePosition == true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedVisibility))]
    [NotifyPropertyChangedFor(nameof(NotWatchedVisibility))]
    public partial bool Watched { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteVisibility))]
    [NotifyPropertyChangedFor(nameof(NotFavoriteVisibility))]
    public partial bool Favorite { get; set; }

    /// <summary>
    /// 连播 —— 「集页面加一个连播按钮」（2026-09-12）。开的是设置里那一档「自动播放下一集」
    /// （<see cref="PlaybackSettings.AutoPlayNextEpisode"/>：一集放完自动接下一集，本季放完退出播放），
    /// 不是这一页自己的状态 —— 播放器 ⚙ 菜单和设置页读写的都是同一份，谁改了屏上到处都认；写回即存，
    /// 和播放器里那一颗的规矩一致（见 <c>PlayerViewModel.AutoPlayNextEpisode</c>）。
    /// <para>
    /// 没拿到设置（<see cref="Attached"/> 之前）读 false、写不进：页面还没接上服务，那一拍屏上那颗开关
    /// 是个占位。
    /// </para>
    /// </summary>
    public bool ContinuePlay
    {
        get => Attached && Settings.Playback.AutoPlayNextEpisode;
        set
        {
            if (!Attached || Settings.Playback.AutoPlayNextEpisode == value) return;

            Settings.Playback.AutoPlayNextEpisode = value;
            _settings!.Save();
            OnPropertyChanged(nameof(ContinuePlay));
        }
    }

    /// <summary>
    /// 连播那颗显不显 —— 电影页面上不画（2026-09-13「去掉电影页面的轮播按钮」；他嘴里的「轮播」就是
    /// 屏上这颗写着「连播」的开关）：一部电影没有「下一集」，自动接续对它没有意义。剧、季、单集三页照旧。
    /// 三颗同款（宽版式带子里那份、集页片名那一栏那份、紧凑版式那份）绑的是同一个判据。
    /// <para>
    /// 条目没到手（<see cref="_detail"/> 还是 null）按「不是电影」答显 —— 和播放键那一拍的占位一个道理，
    /// 屏上先有键、数据到了再各就各位。
    /// </para>
    /// </summary>
    public Visibility ContinueVisibility => Show(_detail?.Type != EmbyItemType.Movie);

    /// <summary>
    /// 副标题那一行画的是一行字还是一排点得动的类型：有类型就是后者（见 <see cref="SublineGenres"/>），
    /// 单集页和只剩剧名的季页还是前者。
    /// </summary>
    public Visibility SublineVisibility => Show(!string.IsNullOrWhiteSpace(Subline)
        && (SublineGenres.Count == 0 || IsSeasonPage));

    /// <inheritdoc cref="SublineVisibility"/>
    public Visibility GenreVisibility => Show(SublineGenres.Count > 0);

    /// <summary>
    /// 那一行里点得动的几个类型。空的时候那一行照旧是一行字 —— 规则在
    /// <see cref="ItemDetail.SublineGenres"/>，这里只是把它端上屏。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SublineVisibility))]
    [NotifyPropertyChangedFor(nameof(GenreVisibility))]
    public partial IReadOnlyList<string> SublineGenres { get; set; } = [];

    /// <summary>
    /// 跨库题材页 Plex 客户端未实现、飞牛的适配器对 Genre 条件直接抛不支持，两家都没有这条路；
    /// 只用详情所属 scope，不借默认管理来源。
    /// </summary>
    internal bool CanOpenGenre => Attached && _scope?.Connection.SupportsGenreBrowse == true;

    /// <summary>
    /// 点了一个类型：交给外壳开一格「这个类型下的全部影片和剧集」（<see cref="IShellActions.OpenGenre"/>）。
    /// 视图那边把这一行的每一个类型接到这儿来，同卡片上的点击接到 <see cref="OpenCard"/>。
    /// </summary>
    internal void OpenGenre(string genre)
    {
        if (!CanOpenGenre) return;
        _actions?.OpenGenre(genre, _scope);
    }

    /// <summary>
    /// 副标题的字号. On an episode page this line is 「S2:E7 - 集名」, which is the one thing the reader
    /// came for — the headline above it only says which show — so it is set at 标题号 there. On a film or
    /// a show the same line is a genre list, background information, and stays at 正文号. Both numbers
    /// are read out of the theme rather than written here so the page keeps a single type scale.
    /// </summary>
    public double SublineFontSize => FontSize(IsEpisodePage ? "EgTitleFontSize" : "EgBodyFontSize");

    /// <summary>The weight that goes with <see cref="SublineFontSize"/>: emphasised on an episode page.</summary>
    public Windows.UI.Text.FontWeight SublineFontWeight =>
        IsEpisodePage ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>
    /// 头图副标题与视频、音频共用白色文字。遮罩与高斯模式统一，不改变主题色链接和图标。
    /// </summary>
    public Brush SublineBrush => Painted("EgOnScrimBrush");

    /// <summary>
    /// The 标题按钮's tooltip, or null when there is nowhere to go. Null rather than empty on purpose:
    /// <c>ToolTipService</c> shows nothing for a null, while an empty string pops an empty box over a
    /// headline that cannot be pressed.
    /// </summary>
    public string? TitleTip =>
        TitleLink is { } link ? $"转到{EmbyItemType.ToChinese(link.Type)}：{link.Name}" : null;

    /// <summary>Whether the page is about one episode — the case that moves the emphasis to the subline.</summary>
    private bool IsEpisodePage => _detail?.Type == EmbyItemType.Episode;

    /// <summary>
    /// 这一页是不是一季。季页的头行自 2026-11-02 起写剧名、第二行写季名（用户令「把季页面的剧名和季度的
    /// 字体大小还有位置互换」），而类型那一行仍在（图三那一条），所以副标题和类型行要同时画出来。
    /// </summary>
    private bool IsSeasonPage => _detail?.Type == EmbyItemType.Season;

    /// <summary>
    /// 这一页要不要按「背后铺着一张图」那一档布（<see cref="HeroArt"/> 的判据）—— 两句话：服务器上有没有那张
    /// 图（<see cref="ItemArtwork.Hero"/> 问标签表的结果），以及集页那一页这次走哪套版式。
    /// <para>
    /// 集页的宽版式不铺背景图（2026-09-12「去掉集页面的背景图」）：那一页整段合成在一块板上，铺一张图反而把
    /// 板糊掉；紧凑版式照铺 —— 「再给集页面的窄窗口加背景图」（2026-10-13），单列版式本来就没有那块板，而
    /// 参考图上的窄版式头图上正是一张画面。季页和电影、剧两页一直是照 <see cref="ItemArtwork.Hero"/> 问的。
    /// </para>
    /// <para>
    /// 因此这一句读 <see cref="PageWidth"/>（经 <see cref="IsCompact"/>），跨过紧凑断点那一下必须重问一遍 ——
    /// 那是 <see cref="OnPageWidthChanged"/> 里的 <see cref="SyncBackdrop"/>。四处赋值（<see cref="Attach"/>、
    /// <see cref="Preview"/>、<see cref="Apply"/> 和这一处）共用这一句，答案分叉的后果是同一页里带高按两档
    /// 各算各的。
    /// </para>
    /// </summary>
    private bool SpreadsBackdrop(EmbyItem item) =>
        (ImmersiveEnabled || item.Type is not (EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season))
        && (item.Type != EmbyItemType.Episode || ImmersiveEnabled || IsCompact) && ItemArtwork.Hero(item).Count > 0;

    /// <summary>
    /// Whether 单集 belongs down the page as rows rather than across it as cards. The rule itself is
    /// <see cref="ItemDetail.EpisodesAsList"/>; what is here is only 「asked of this page's own item」.
    /// </summary>
    private bool EpisodesAsList => ItemDetail.EpisodesAsList(_detail?.Type);

    private static double FontSize(string key) => (double)Application.Current.Resources[key];

    private static Brush Painted(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>自检用：一个可空的评分字段读出来怎么写。缺失和 0 要分得清 —— 两者屏上都是「没有分」。</summary>
    private static string Describe(float? value) =>
        value is { } number ? number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "没给";

    public Visibility ScoreVisibility => Show(!string.IsNullOrWhiteSpace(Score));

    /// <summary>
    /// 「视频：417.7 MB · 1080p · H264 · AAC · 制作组」这一行该不该有。两处用它：集页的宽版式里它是一枚 chips
    /// （和音频、字幕那几格一起换行，「按键布局参考上方截图」），其余版式里它是片名那一栏的独立一行
    /// （<see cref="VideoLineVisibility"/>）。内容是 <see cref="ItemDetail.VideoLine"/> 那条共用读数
    /// （文件大小 · 分辨率 · 视频编码 · 音频格式 · 组名，与播放页第二行同源，用户令 2026-10-09）。
    /// <para>
    /// 有话说是第一句，第二句是**底下那个媒体源下拉没在屏上**。一个文件的条目上没有下拉，这一行是那份读数
    /// 唯一的出处，收掉它就等于把分辨率和大小从页面上抹了。
    /// </para>
    /// <para>
    /// 下拉只在有两个以上媒体源时才出现（<see cref="SourceVisibility"/>）。它的行文曾经以同一份读数结尾
    /// （<see cref="ItemDetail.SourceLabel"/> 问的也是 <c>ToQualityLabel()</c>），两样同屏就是同一句话说两遍
    /// （2026-09-05 界面复查）；2026-09-13 起下拉不再拖画质，2026-10-26 起在文件名前补体积，互斥保下来是因为多源
    /// 条目上画质那些事实媒体信息表说得比这一行全 —— 挑中的是哪个文件，表就描述哪个文件。
    /// </para>
    /// </summary>
    private bool VideoShown => !string.IsNullOrWhiteSpace(VideoLine) && Sources.Count <= 1;

    public Visibility VideoVisibility => Show(VideoShown);

    /// <summary>
    /// 片名那一栏里那行「视频：…」显不显 —— 集页的宽版式不画（那一页它下到文件选项那排 chips 里了，见
    /// <see cref="VideoVisibility"/>），其余版式照旧。
    /// </summary>
    public Visibility ColumnVideoVisibility => Show(VideoShown && HeroCard);

    public Visibility VideoLineVisibility => Show(VideoShown && !HeroCard);

    /// <summary>
    /// Whether there is a picture yet, rather than a flag set beside the assignment. The two used to be
    /// a <c>_hasHeroImage</c> and an <c>Image.Source</c> that a later edit could put out of step; here
    /// there is one value and the question is asked of it.
    /// </summary>
    public Visibility HeroVisibility => Show(HeroImage is not null);

    /// <summary>
    /// 固定在背景里那一层画不画 —— 「当前任务的背景要用亚克力遮罩，固定在背景中……占满标题栏」里那一层
    /// （<c>DetailPage.xaml</c> 的 <c>Backdrop</c>）。
    /// <para>
    /// 判据是 <see cref="BackdropShown"/>（服务器上有这张图），不是 <see cref="HeroVisibility"/>「位图解出来
    /// 了没有」：层先站好，压住标题栏那行字的罩子就先在了，图到了只是这一层里多一张画面。反过来就是「点击
    /// 主页封面后窗口会闪一下」—— 先按没有图布一遍、图到了再布第二遍。
    /// </para>
    /// </summary>
    public Visibility HeroArtVisibility => Show(BackdropShown);

    /// <summary>
    /// 上面那一层的反面：背景不画的时候（这一条目没有图，或者集页正走宽版式），头图那一格自己当底的那一层
    /// （一层底色加顶上一道罩子）。有图的时候这两样都在那张固定的图上，这一层就得让开，否则一块不透明的底色
    /// 会把图盖掉。
    /// <para>
    /// 2026-09-12 起集页的宽版式不在这一档里：那一页那一段合成了一块面板（见 <see cref="HeroCard"/>），底色、
    /// 圆角和外圈由面板自己画。两层都画的话面板的圆角先被糊掉，左右那两条 28 的留白也会被填成底色。集页的
    /// 紧凑版式仍然在（没有板，也没有图的那一档就是它自己当底）。
    /// </para>
    /// </summary>
    public Visibility HeroPlainVisibility => Show(!BackdropShown && !HeroCard);

    /// <summary>
    /// 集页整段上方那一块面板显不显 —— 「把集页面上方框的组件框起来」（2026-09-12）。
    /// <para>
    /// 板是一整块、分两截画（见 <c>DetailPage.xaml</c> 的 <c>HeroCardTop</c> 和 <c>HeroCardBottom</c>）：上截贴在
    /// 头图那一格里（海报、片名、六颗键、右上角那张艺术图都站在这块板上），下截是尾部那一格（音轨字幕和剧情
    /// 说明）。两截的左右留白和下沿完全对上，四个角只有外侧那两个圆 —— 屏上是一条通栏的板。
    /// </para>
    /// <para>
    /// 只有宽版式有它：紧凑版式是单列，整页就没有「上方那一段」可框，而那一档每一块本来就自带面板 —— 多套
    /// 一层只是又一条边框。<see cref="HeroInset"/> 和 <see cref="TailInset"/> 都跟着它走（板边上要留出 20），
    /// 所以这三个属性是一组：宽窄一变、页面种类一变，三个一起重算。
    /// </para>
    /// <para>
    /// 判据不是「有没有剧照」：有剧照的那几页整段压在照片上，一块不透明的板会把照片盖掉；集页的宽版式是唯一
    /// 「没有剧照、又整段挤在第一屏里」的那一档。（集页的紧凑版式 2026-10-13 起铺背景图，也不在这一档里 ——
    /// 单列版式本来就没有「上方那一段」可框。）
    /// </para>
    /// <para>
    /// 同日他还要去掉过一次（「去掉集页面最上面的框」），去完又要回来（「框加回去」）—— 同一个下午来回两趟，
    /// 落地的是有板的这一版。要撤的话，去的不是这一句，是 <c>DetailPage.xaml</c> 里那两块 Border、这两句
    /// 显隐和 <c>DetailPage.PaintScrim</c> 的分档，四样一起。
    /// </para>
    /// </summary>
    public Visibility HeroCardVisibility => Show(HeroCard);

    /// <summary>
    /// <see cref="HeroCardVisibility"/> 的布尔那一份 —— 视图在代码里要问它两句：尾部那一块的底色（有板时透明、
    /// 由板自己画，没板时接住头图末色），以及那四块内层小面板各自的框。判据只写在这儿一处。
    /// </summary>
    public bool HeroCardShown => HeroCard;

    /// <summary>
    /// 无背景图时保留带子下沿的渐深罩子；有图时整块头部改由同一支半透明黑色画刷承接标题栏与尾部。
    /// <para>
    /// 集页那块板上不画（<see cref="HeroCard"/>，只可能是宽版式）：那一页的宽版式不铺背景图，没有照片可压；
    /// 留着它只会把板的下半截压暗，板底和自己那几块小面板的底边对不上。板上的底由板自己给。集页的紧凑版式
    /// 铺图，走的是有图那一档（<c>DetailPage.PaintScrim</c> 把带子的底接成尾部那一支）。
    /// </para>
    /// </summary>
    public Visibility ScrimVisibility => Show(!HeroCard && !BackdropShown);

    /// <summary>紧凑版式里才画的那一块（全宽播放键、断点进度、带字的一排操作）显不显 —— <see cref="IsCompact"/>。</summary>
    public Visibility CompactVisibility => Show(IsCompact);

    /// <summary>
    /// 片名那一叠里那排键（<c>DetailPage</c> 标记里的 <c>ColumnActions</c>）显不显 —— 宽版式上它是唯一
    /// 的一份：2026-09-12 集页先把键搬进这一叠（独立一行要等封面行结束，文字和按键之间空出海报高出来的
    /// 那一截），2026-09-13 电影、剧、季也搬了进来（「排列在封面右边」），带子底下那一行退役。紧凑版式里
    /// 它收着，活交给画面底下那一块（<see cref="CompactVisibility"/>）。
    /// </summary>
    public Visibility ColumnActionsVisibility => Show(!IsCompact);

    /// <summary>
    /// 剧名上方那一枚徽标画不画 —— 只问一句：图解出来了没有。
    /// <para>
    /// 从前这里还有第二句「这一页是不是集页」：那一版里名牌在集页上摆右上角、别的页面上是左上角那个位置的第二档。
    /// 「统一改为在剧名上方显示徽标」把那个分岔去掉了 —— 四种页面同一个位置，也就没有「哪一页摆哪个角」可问。
    /// </para>
    /// <para>
    /// 算出来的而不是谁解完图顺手把元素显出来：这样「那一枚在不在」是页面上一个可读的值，自检能从树上读到它，
    /// 而不是去猜一张图有没有到。
    /// </para>
    /// </summary>
    public Visibility PlateVisibility => Show(PlateImage is not null);

    /// <summary>集页与简洁模式电影、剧页显示艺术图；没有图或标题栏空间不足时收起。</summary>
    public Visibility CornerVisibility => Show(CornerImage is not null && !IsCompact && CornerMaxWidth > 0
        && (IsEpisodePage && !Showcase || PlainDetail && ItemType is EmbyItemType.Movie or EmbyItemType.Series));

    /// <summary>
    /// 页面最底下那张横幅画不画。两句话：图解出来了没有，以及这一页是不是那三种页面之一 —— 用户点的名是「电影
    /// 页面 剧页面 集页面」，季页不在里头。
    /// <para>
    /// 季页为什么不在：那一页整个是「这一季有哪些集」的一张清单，横幅上写的是剧名，摆在清单末尾说的是上一层的
    /// 事。这是用户给的范围，不是算出来的 —— 所以判据就照着他点的那三种写。
    /// </para>
    /// </summary>
    public Visibility FooterVisibility => Show(FooterImage is not null && FooterPage);

    /// <summary>
    /// 这一页在不在「页尾摆横幅」那三种里（电影、剧、集）。跟着页面的种类走，所以要在
    /// <see cref="AnnounceShape"/> 里喊一声。
    /// </summary>
    private bool FooterPage => ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Episode;


    /// <summary>「播放」, 「播放 S01E02」 or 「继续播放 20:34」 — the resume clock is on the button itself.</summary>
    public string PlayText => ItemDetail.PlayText(PlayTarget);

    /// <summary>
    /// 播放键上的回转图标亮不亮 —— 有断点就亮。和 <see cref="RestartVisibility"/> 同一个判据（<see
    /// cref="ItemDetail.PlayText"/> 里那条 <c>HasResumePosition</c> 分支），所以图标、字、旁边的「从头开始」
    /// 三样说的是同一件事：断点。
    /// <para>
    /// 亮的是那一颗图标自己 —— 有断点时它取 accent（<c>EgAccentBrush</c>），没有断点时和字一样是压在图上
    /// 那支浅墨。按键的底是半透明的药丸（<c>EgPlayButtonStyle</c>，和连播同一块底，2026-10-11），强调色
    /// 于是只能落在图标上：从前那颗键是整块实心 accent、字是深墨，一颗图标反而不显眼。这一下和连播
    /// 那颗「激活只换图标」是同一个手法，只是这颗常态就有一颗图标，亮的就是它。
    /// </para>
    /// <para>
    /// 这里不问「点了会不会真的从断点接上」（那是 <see cref="PlaybackSettings.ResumeFromSavedPosition"/>，
    /// 关掉后 从头开始 也是片头）—— 亮的是「这颗条目有断点」，和 <see cref="PlayText"/> 的
    /// 「继续播放 20:34」、旁边那颗 从头开始 同进同退。承接位置归播放器那一头算。
    /// </para>
    /// </summary>
    public Visibility ResumePlay => Show(PlayTarget?.HasResumePosition == true);

    /// <summary>没有断点时那一支图标 —— 两颗 FontIcon 一个占位、一个让位，同「未看／已看」那一对的写法。</summary>
    public Visibility NotResumePlay => Show(PlayTarget?.HasResumePosition != true);

    public Visibility PlayVisibility => Show(PlayTarget is not null);

    public Visibility RestartVisibility => Show(PlayTarget?.HasResumePosition == true);

    public Visibility WatchedVisibility => Show(Watched);

    public Visibility NotWatchedVisibility => Show(!Watched);

    public Visibility FavoriteVisibility => Show(Favorite);

    public Visibility NotFavoriteVisibility => Show(!Favorite);

    private bool FavoritesApplicable => Attached && _scope!.Connection.SupportsFavorites
        && (_seed ?? _detail)?.TracksUserState == true;

    public Visibility FavoriteActionVisibility => Show(FavoritesApplicable);

    // ---------------------------------------------------------------- 下拉

    public ObservableCollection<EmbyItem> Seasons { get; } = [];

    public ObservableCollection<SourceRow> Sources { get; } = [];

    public ObservableCollection<TrackRow> AudioTracks { get; } = [];

    public ObservableCollection<TrackRow> SubtitleTracks { get; } = [];

    [ObservableProperty]
    public partial EmbyItem? SelectedSeason { get; set; }

    [ObservableProperty]
    public partial SourceRow? SelectedSource { get; set; }

    [ObservableProperty]
    public partial TrackRow? SelectedAudio { get; set; }

    [ObservableProperty]
    public partial TrackRow? SelectedSubtitle { get; set; }

    partial void OnSelectedAudioChanged(TrackRow? value)
    {
        if (!Attached || SelectedSource?.Source is not { } source || SubtitleTracks.Count == 0) return;
        var wasAutomatic = ReferenceEquals(SelectedSubtitle, SubtitleTracks[0]);
        var automatic = ItemDetail.SubtitleRows(Settings.Playback, source, value?.Stream)[0];
        SubtitleTracks[0] = automatic;
        if (wasAutomatic) SelectedSubtitle = automatic;
    }

    /// <summary>
    /// Only a show with more than one season gets a picker. No conjunct on the item's type, unlike the
    /// page this replaces: <see cref="LoadSeriesAsync"/> and the episode page's season list fill
    /// <see cref="Seasons"/>, so a non-empty list already says so.
    /// <para>
    /// 屏上那颗季选择栏 2026-11-02 按用户令删掉；2026-11-03 集列表改为全季一条之后，季列表改由牌子本身
    /// 点开（<see cref="CanPickEpisodeSeason"/>）。<see cref="Seasons"/>／<see cref="SelectedSeason"/> 仍是
    /// 那一页的季锚点：换它只换标题和集带起点，不再按季重读。
    /// </para>
    /// </summary>
    public Visibility SeasonVisibility => Show(Seasons.Count > 1);

    public Visibility SourceVisibility => Show(Sources.Count > 1);

    /// <summary>自动不算实际音轨；至少两条实际音轨才提供选择。</summary>
    public Visibility AudioVisibility => Show(AudioTracks.Count > AutoRows + 1);

    public Visibility AudioRowVisibility => Show(AudioTracks.Count > AutoRows);
    public Visibility SingleAudioVisibility => Show(AudioTracks.Count == AutoRows + 1);
    public string SingleAudioText => AudioTracks.Count == AutoRows + 1 ? AudioTracks[AutoRows].Text : "";

    /// <summary>Two rows are 自动 and 不使用字幕, which is not a choice either.</summary>
    public Visibility SubtitleVisibility => Show(SubtitleTracks.Count > AutoRows + 1);

    /// <summary>紧凑版式的文件选项，位于尾部播放操作之前。</summary>
    public Visibility PickersVisibility => Show(IsCompact && Pickable);

    /// <summary>宽版式四种详情页共用片名栏内的选择框，紧邻播放操作上方。</summary>
    public Visibility ColumnPickersVisibility => Show(!IsCompact && (Pickable || HeroCard && VideoShown));

    /// <summary>有播放目标及版本、音频信息或字幕选项时显示文件栏。</summary>
    private bool Pickable =>
        PlayTarget is not null
        && (Sources.Count > 1 || AudioTracks.Count > AutoRows || SubtitleTracks.Count > AutoRows + 1);

    /// <summary>The 自动 row every track picker carries whether the file has tracks or not.</summary>
    private const int AutoRows = 1;

    // ---------------------------------------------------------------- 正文

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverviewVisibility))]
    [NotifyPropertyChangedFor(nameof(TailMargin))]
    public partial string? Overview { get; set; }

    /// <summary>
    /// Whether 简介 is showing in full. Drives four bound properties instead of the four assignments
    /// <c>LayoutPage()</c> made, which is the same arithmetic with the ordering problem removed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverviewMaxLines))]
    [NotifyPropertyChangedFor(nameof(OverviewTrimming))]
    [NotifyPropertyChangedFor(nameof(ReadMoreText))]
    [NotifyPropertyChangedFor(nameof(ReadMoreVisibility))]
    public partial bool Expanded { get; set; }

    /// <summary>
    /// Set by the page from <c>TextBlock.IsTextTrimmed</c>, which only the laid-out element knows. The
    /// one fact on this view model that has to come back from the view: whether four lines were enough
    /// depends on the width the text was given, and nothing here knows that.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadMoreVisibility))]
    public partial bool OverviewTrimmed { get; set; }

    public int OverviewMaxLines => Expanded ? 0 : CollapsedLines;

    public TextTrimming OverviewTrimming => Expanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;

    public string ReadMoreText => Expanded ? "收起" : "阅读更多";

    public Visibility ReadMoreVisibility => Show(OverviewTrimmed || Expanded);

    /// <summary>
    /// 简介那一块整块的显隐 —— 简介和导演两行都空的时候收起来。
    /// <para>
    /// 这一条是「正文那几块坐上亚克力」带出来的：这一块以前是版面里一段没有底的文字，简介为空就是零高，
    /// 屏上看不出来；现在它自己是一块玻璃板，空的一块玻璃是一条 37 高的空板，得自己收起来。判据里带上
    /// 导演那一行，因为它也长在这一块里 —— 有导演没简介的条目照样得有块板。
    /// </para>
    /// </summary>
    public Visibility OverviewVisibility => Show(
        !string.IsNullOrWhiteSpace(Overview) || !string.IsNullOrWhiteSpace(Directors));

    // ---------------------------------------------------------------- 单集列表、三条卡片带与媒体信息

    /// <summary>
    /// 单集, drawn either as a vertical list of rows or as a horizontal paging strip of cards — one shelf
    /// for both, filled through <see cref="CardShelf.FillDeferred"/> without creating offscreen cards. Which
    /// of the two is <see cref="ItemDetail.EpisodesAsList"/>, asked of this page's own item. Nullable
    /// because the shelf cannot exist before <see cref="Attach"/> — it needs the image store and the card
    /// width from 设置 — and the page binds to it from the moment it is constructed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EpisodeVisibility))]
    [NotifyPropertyChangedFor(nameof(EpisodeListVisibility))]
    [NotifyPropertyChangedFor(nameof(EpisodeStripVisibility))]
    public partial CardShelf? EpisodeShelf { get; set; }

    /// <summary>
    /// Which card the strip opens on, bound to <c>ShelfStrip.FocusIndex</c>; 0 means 「leave it at the
    /// start」. Only ever non-zero on an episode's own page, where the strip lists that episode's siblings
    /// and the episode itself is somewhere in the middle of the season. See
    /// <see cref="ItemDetail.EpisodeFocus"/>.
    /// </summary>
    [ObservableProperty]
    public partial int EpisodeFocus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CastVisibility))]
    public partial CardShelf? CastShelf { get; set; }

    /// <summary>
    /// 全部剧季, the horizontal strip of season cards a series page carries — Emby's own arrangement,
    /// where a series is browsed season by season and each card opens that season's page.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeasonShelfVisibility))]
    [NotifyPropertyChangedFor(nameof(CanPickEpisodeSeason))]
    public partial CardShelf? SeasonShelf { get; set; }

    /// <summary>更多类似, the recommendations row Emby's own detail page carries under the cast.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SimilarVisibility))]
    public partial CardShelf? SimilarShelf { get; set; }

    /// <summary>
    /// The 媒体信息 table, one <see cref="InfoRow"/> per line. A collection rather than the one long
    /// string this used to be so the page can right-align the labels into a column: a single
    /// <c>TextBlock</c> cannot align anything, and a table is what the reader is scanning.
    /// </summary>
    public ObservableCollection<InfoRow> InfoRows { get; } = [];

    /// <summary>
    /// 其他信息那一行：类型、工作室、数据库链接，集页再加一条上次播放日期（判据在
    /// <see cref="ItemDetail.OtherInfo"/>）。空表示这一页没有这一段 —— 季页，或者服务器这些项一条也没给。
    /// 剧页自 2026-11-02 起也有这一段（说的就是这部剧自己），所以它不再是「只有文件页才有」的那一行。
    /// 它不是集合而是整份换掉：这一行要么重排，要么空着，没有「逐项增删」这回事。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OtherInfoVisibility))]
    [NotifyPropertyChangedFor(nameof(OtherInfoRuleVisibility))]
    [NotifyPropertyChangedFor(nameof(InfoVisibility))]
    public partial IReadOnlyList<OtherFact> OtherFacts { get; set; } = [];

    public Visibility OtherInfoVisibility => Show(OtherFacts.Count > 0);

    /// <summary>
    /// 媒体信息表与下面那一行之间的分割线。两样都在才画：剧页的集还没回来或那一集没有媒体源时只剩一行字，
    /// 那时候还画一条线就是牌子下面凭空一道横杠。电影、剧、集三页数据齐时和从前一样；季页没有这一段。
    /// </summary>
    public Visibility OtherInfoRuleVisibility => Show(InfoRows.Count > 0 && OtherFacts.Count > 0);

    public Visibility EpisodeVisibility => Show(CardVisible("episodes") && (EpisodeShelf is { Cards.Count: > 0 } || Seasons.Count > 1));

    /// <summary>
    /// Which of the two shapes 单集 takes on this page. Both are inside the block
    /// <see cref="EpisodeVisibility"/> governs — they share its heading — so each one
    /// only has to answer 「is this page's shape mine」 on top of 「is there anything to draw」.
    /// <para>
    /// A pair of computed visibilities rather than one flag the markup inverts: a page that hid neither, or
    /// hid both, would be a page with the episodes drawn twice or not at all, and a <c>Visibility</c> the
    /// view model states outright is the thing the self-check can read back off the tree.
    /// </para>
    /// </summary>
    public Visibility EpisodeListVisibility => Show(EpisodeShelf is { Cards.Count: > 0 } && EpisodesAsList);

    public Visibility EpisodeStripVisibility => Show(EpisodeShelf is { Cards.Count: > 0 } && !EpisodesAsList);

    public Visibility CastVisibility => Show(CardVisible("cast") && CastShelf is { Cards.Count: > 0 });

    /// <summary>
    /// 全部剧季那一格画不画。两张卡以上才画：只有一季时那一格是一张通往本页已经在列的那一季的卡，摆在那里只是
    /// 让人多点一次（用户令 2026-11-02「当剧集只有一季时，在剧页面不显示『全部剧集』卡片」）。季下拉框原来在同一个
    /// 条件上收起（<see cref="SeasonVisibility"/>），那颗 2026-11-02 已删；判据仍然只写这一份。
    /// <para>
    /// 这一条对剧页和集页同时成立：集页那张卡由 <see cref="LoadSeasonShelfAsync"/> 填（同日用户令「集页面也加上
    /// 全部剧季卡片」），单季的集页因此也不会画出这一块。电影、季两页的季卡永远是空的，整块收着。
    /// </para>
    /// <para>
    /// 卡带里从 2026-11-03 晚起也列第 0 季（用户令要 S00 的封面），所以「一季正片 + 特别篇」的剧页、集页
    /// 会画出这一块 —— 那两张卡指向两个不同的季页面，不是同一张卡摆两遍。
    /// </para>
    /// </summary>
    public Visibility SeasonShelfVisibility => Show(CardVisible("seasons") && SeasonShelf is { Cards.Count: > 1 });

    /// <summary>
    /// 集列表牌子能不能点开季列表。剧页和集页的集列表现在是全剧一条（按播出时间排），所以两页都要能按季
    /// 把带子对到某一季的第一张；只有一季时没有可选的，牌子保持不可点。
    /// </summary>
    public bool CanPickEpisodeSeason =>
        _detail?.Type is EmbyItemType.Series or EmbyItemType.Episode
        && _episodes.Count > 0
        && Seasons.Count > 1;

    /// <summary>季列表里要列出哪些季 —— 服务器给的那一份，包含第 0 季。</summary>
    internal IReadOnlyList<EmbyItem> EpisodeSeasonChoices => [.. Seasons];

    public Visibility SimilarVisibility => Show(CardVisible("similar") && SimilarShelf is { Cards.Count: > 0 });

    public Visibility DirectorsVisibility => Show(!string.IsNullOrWhiteSpace(Directors));

    /// <summary>
    /// 其他信息那一块板画不画。表和那一行只要有一样有内容就画：电影、剧、集三页都是表加那一行（剧页的表
    /// 自 2026-11-02 起也有，描述有续播的最近一集或当季第一集）。季页两样都没有（同日用户令「季页面不要
    /// 媒体信息」），整块收起。集还没回来、那一集没有媒体源、又没有其他信息时也收起。
    /// </summary>
    public Visibility InfoVisibility => Show(CardVisible("info") && (InfoRows.Count > 0 || OtherFacts.Count > 0));

    public Visibility MediaInfoVisibility => Show(InfoRows.Count > 0);

    /// <summary>The episodes on screen, for the player's 下一集 and for the context menu's 本季 commands.</summary>
    internal IReadOnlyList<EmbyItem> Episodes => _episodes;

    /// <summary>
    /// The fully loaded item behind this page. The page reads it when it needs to build a view-only
    /// context menu; exposing the domain item keeps that menu's anchor and flyout construction out of
    /// the view model without duplicating the server-loaded state in the page.
    /// </summary>
    internal EmbyItem? CurrentItem => _detail;

    /// <summary>
    /// What kind of thing this page is showing, for the self-check's report. The item the page fetched
    /// rather than the one it was handed, since those two disagree in exactly the case worth reading about:
    /// a series whose play target is one of its episodes.
    /// </summary>
    internal string ItemType => _detail?.Type ?? _seed?.Type ?? "未定";

    /// <summary>
    /// 自检：「点封面进详情页要空等一趟服务器往返」那一条修好了没有，而且没修出新毛病来。三句话 —— 第一屏是拿点
    /// 进来那张卡片画的（落下的片名就是证据）、**这一整页只先画过一次**、那四张图**这一整页只取过一次**。
    /// <para>
    /// 三件屏上都看不出来。第一屏画得早不早，截图只能拍到已经载完的那一页。另两件是次数：图起 2 次是点一张封面进
    /// 来闪一下；先画 2 次说明重新读取时又拿卡片那一份盖了一遍，屏上是「标记为已观看」之后那个勾先跳回未看、一趟
    /// 往返之后才变回来 —— 两件都是一百毫秒的事，截图抓不住。所以这里报的是数，而自检会先重新读取一遍再问。
    /// </para>
    /// <para>
    /// 存根导航（集页那行剧名、演职人员那一排）故意不先画，那一档报「没先画」并且不判红 —— 见 <c>Preview</c>。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail) PreviewRead()
    {
        if (_seed is { IsStub: true })
            return (true, $"存根导航（{_seed.Type}），故意不先画；图起了 {_artStarts} 次");

        var painted = _previewed is { Length: > 0 };
        var onceEach = _previews == 1 && _artStarts == 1;

        return (painted && onceEach,
            (painted ? $"第一屏用卡片画的，片名「{_previewed}」" : "第一屏没先画（片名是空的）")
                + $"；这一页先画了 {_previews} 次、图起了 {_artStarts} 次"
                + (onceEach
                    ? "（各一次 —— 完整条目回来没重取，重新读取也没拿卡片盖一遍）"
                    : _previews > 1
                        ? "，先画多于 1 次 —— 重新读取时又拿卡片那一份盖了一遍，那个勾会先跳回旧值"
                        : "，图多于 1 次 —— 完整条目回来把图白重取了一遍"));
    }

    /// <summary>
    /// 按钮跟的是「主体已经确认」而不是整页 Busy：剧集／相似推荐这些附属链还在读时，
    /// 已经成立的播放目标、已观看和收藏不该跟着一起等。首次读取、换条目和读取失败仍从 false 开始。
    /// </summary>
    private bool _actionsReady;
    private CardItem? _subjectStateCard;

    private void ObserveSubjectState(EmbyItem item)
    {
        if (_subjectStateCard is { } previous) previous.PropertyChanged -= OnSubjectStateChanged;
        _subjectStateCard = new CardItem(item, _images!, CardSize.PosterWidth);
        _subjectStateCard.PropertyChanged += OnSubjectStateChanged;
    }

    private void OnSubjectStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Attached || !ReferenceEquals(sender, _subjectStateCard) || _detail is not { } item) return;
        if (args.PropertyName == nameof(CardItem.Watched)) Watched = item.IsWatched;
        if (args.PropertyName == nameof(CardItem.IsFavorite)) Favorite = item.UserData?.IsFavorite == true;
    }

    private bool CanAct => Attached && _actionsReady;

    private bool CanFavorite => CanAct && FavoritesApplicable && _detail is { } item
        && item.Id == _seed?.Id && (item.Source is null || item.Source.Key == _scope!.Key);

    private bool CanPlay => CanAct && PlayTarget is not null;

    /// <summary>
    /// Not gated on <see cref="PageViewModel.Busy"/>, unlike the buttons: following the headline is
    /// navigation, same as pressing a card, and leaving a page that is still loading is a reasonable
    /// thing to want.
    /// </summary>
    private bool CanOpenTitle => TitleLink is not null;

    /// <inheritdoc />
    protected override void BusyChanged() => NotifyActionAvailability();

    private void NotifyActionAvailability()
    {
        PlayCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        ToggleWatchedCommand.NotifyCanExecuteChanged();
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- 载入

    /// <summary>
    /// Whether <see cref="Attach"/> has run. The five services arrive in that one call as non-nullable
    /// parameters, so this single question answers for all of them; past this gate they are dereferenced
    /// with <c>!</c>. <see cref="_seed"/> is a separate question — 「which item」 — and the loads ask both.
    /// </summary>
    private bool Attached => _active && _settings is not null && _scope?.IsAvailable == true;

    /// <summary>
    /// The live settings object, read through the service rather than copied: it is one instance for the
    /// process and the settings page edits it in place, so a snapshot taken here would go stale the moment
    /// 恢复上次播放位置 was switched off.
    /// </summary>
    private AppSettings Settings => _settings!.Settings;

    /// <summary>
    /// Handed the item to show, the shell it opens things through, the three services this page reads —
    /// the settings for the score source and the two playback preferences, the session for every round
    /// trip, and the image store for the shelves.
    /// </summary>
    internal void Attach(
        DetailRequest request,
        IShellActions actions,
        ISettingsService settings,
        EmbySession session,
        EmbyImageStore images)
    {
        Cancel();
        _deletedItems.Clear();
        _deletedSourceIds.Clear();
        _deletedSourcePaths.Clear();
        _actionsReady = false;
        _announcedShapeType = null;
        _active = true;
        _heroCacheTicket = null;
        _heroTiming = null;
        _scope = images.CaptureScope(request.Item);
        _actions = actions;
        _seed = request.Item;
        _settings = settings;
        RefreshCardLayout();

        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(StillVisibility));
        OnPropertyChanged(nameof(CompactVisibility));
        _session = session;
        _images = images;
        _removed = request.Services?.GetService(typeof(RemovedMedia)) as RemovedMedia;

        // 版面高在这一句就定了下来，早于任何一次网络往返 —— 「点击主页封面后窗口会闪一下」说的就是这一句以前
        // 得等图。列表接口回来的条目已经带着 ImageTags，而这一页正是从那张列表上点进来的。
        HeroArt = SpreadsBackdrop(request.Item);

        var episodes = new CardShelf("更多单集", images, CardSize.WideWidth, wide: true);
        var cast = new CardShelf("演职人员", images, CardSize.CastWidth,
            wide: false, indicators: false);
        var seasons = new CardShelf("全部剧季", images, CardSize.PosterWidth, wide: false);

        var similar = new CardShelf("更多类似", images, CardSize.PosterWidth, wide: false);

        if (EpisodeShelf is { } previous) previous.Cards.CollectionChanged -= OnEpisodeCardsChanged;
        if (CastShelf is { } before) before.Cards.CollectionChanged -= OnCastCardsChanged;
        if (SeasonShelf is { } oldSeasons) oldSeasons.Cards.CollectionChanged -= OnSeasonShelfChanged;
        if (SimilarShelf is { } oldSimilar) oldSimilar.Cards.CollectionChanged -= OnSimilarCardsChanged;

        episodes.Cards.CollectionChanged += OnEpisodeCardsChanged;
        cast.Cards.CollectionChanged += OnCastCardsChanged;
        seasons.Cards.CollectionChanged += OnSeasonShelfChanged;
        similar.Cards.CollectionChanged += OnSimilarCardsChanged;

        EpisodeShelf = episodes;
        CastShelf = cast;
        SeasonShelf = seasons;
        SimilarShelf = similar;
        AnnounceAffordances();
    }

    /// <inheritdoc />
    private new bool IsCurrent(CancellationToken token) => Attached && base.IsCurrent(token);

    public override Task ReloadAsync() => LoadAsync();

    /// <summary>Re-reads the item and everything hanging off it. The one entry point; every path lands here.</summary>
    private async Task LoadAsync(string? committedNotice = null)
    {
        if (!Attached || _seed is not { } seed) return;
        if (_removed?.Hides(seed) == true)
        {
            ClearDeletedSubject();
            return;
        }

        var token = BeginLoad();
        NavigationTiming.Mark("load-begin");

        try
        {
            // 先用点进来那张卡片画一屏，**在这一趟往返之前**。片名、副标题那一行类型、简介、已看和收藏、还有那四张
            // 图，卡片手上全都有（列表接口那份字段集就带着 Overview / ProductionYear / Genres / ImageTags），从前
            // 它们一律在等详情接口 —— 于是点一张封面进来先看一屏「正在读取」，而这是整个客户端里点得最多的动作。
            NavigationTiming.Mark("preview-begin");
            Preview(seed);
            NavigationTiming.Mark("preview-end");

            NavigationTiming.Mark("get-begin");
            var detail = await _scope!
                .ExecuteAsync((client, ct) => client.GetItemAsync(
                    seed.Id, ct, order: Settings.Playback.MediaSourceOrder), token).ConfigureAwait(true);
            NavigationTiming.Mark("get-end");
            if (!IsCurrent(token)) return;

            NavigationTiming.Mark("apply-begin");
            Apply(detail);
            NavigationTiming.Mark("apply-end");

            // 更多类似 only needs the item this page is about, so it has no business waiting behind the
            // 剧季 → 单集 → 播放目标 chain — which is three round trips on a series page. Started here and
            // awaited at the end: one fewer trip on the way to a finished page, and it still has to be
            // finished before the page calls itself loaded. Started after Apply, which clears the shelf
            // it fills.
            var similar = LoadSimilarAsync(detail, token);

            NavigationTiming.Mark("branch-begin");
            switch (detail.Type)
            {
                case EmbyItemType.Series:
                    await LoadSeriesAsync(detail, token).ConfigureAwait(true);
                    break;

                case EmbyItemType.Season:
                    // 季页背后那部剧与这一季的集同时起：大标题、点得回的剧名、类型和事实那一行都取剧的
                    // （见 ApplySeasonSeries），和集那一趟互不依赖。两趟都得在 EndLoad 之前落地 —— 页面说
                    // 自己加载完的时候，屏上那几行字就该是最终的；失败各自吞掉（剧取不到就少一行事实）。
                    var parent = LoadSeasonSeriesAsync(detail, token);
                    await LoadEpisodesAsync(detail, retarget: true, token).ConfigureAwait(true);
                    await parent.ConfigureAwait(true);
                    break;

                // An episode page lists its own siblings, which is what anyone who just finished one is
                // looking for. It keeps itself as the play target, so retarget is false.
                case EmbyItemType.Episode:
                    // 集页那张「全部剧季」卡（2026-11-02 用户令「集页面也加上全部剧季卡片」）与兄弟集那一趟同时起：
                    // 两者互不依赖，而季清单慢一点不该把集列表拖在后面。也在 EndLoad 之前落地 —— 页面说自己加载完的
                    // 时候，屏上该有的都在；失败各自吞掉（季清单取不到就少一张卡，不影响这一页其余内容）。
                    var seasonShelf = LoadSeasonShelfAsync(detail, token);
                    await LoadEpisodesAsync(null, retarget: false, token).ConfigureAwait(true);
                    await seasonShelf.ConfigureAwait(true);
                    break;
            }
            NavigationTiming.Mark("branch-end");

            // Cannot throw: it swallows its own failures, on purpose — see its own note.
            NavigationTiming.Mark("similar-wait-begin");
            await similar.ConfigureAwait(true);
            NavigationTiming.Mark("similar-wait-end");

            NavigationTiming.Mark("end-load-begin");
            EndLoad(token);
            NavigationTiming.Mark("end-load-end");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            NavigationTiming.Mark("load-error");
            Log.Warn(Category, "加载详情失败", error);
            if (!IsCurrent(token)) return;

            if (committedNotice is null) Report("加载详情失败", error);
            else
            {
                var transient = error is EmbyUnreachableException || error is EmbyApiException { StatusCode: { } status }
                    && (int)status is >= 500 and <= 599;
                var severity = transient ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
                Notify(committedNotice, Failure.Describe(error), severity);
                _actions?.Notify($"{committedNotice}：{Failure.Describe(error)}", severity);
            }

            // Ready, not stuck: IsReady means the first load finished, whether it found anything or not,
            // and the startup self-check waits on it before it reads the page.
            NavigationTiming.Mark("end-load-begin");
            EndLoad(token);
            NavigationTiming.Mark("end-load-end");
        }
    }

    /// <summary>
    /// 用点进来那张卡片先画一屏，早于详情那一趟往返。只写卡片真的知道的那几样 —— 列表接口要的字段集
    /// （<c>EmbyFields.Browse</c>）带着 Overview、ProductionYear、Genres、Tags 和全部图片标签，所以片名、副标题
    /// 那一行类型、简介、已看与收藏、以及四张图在这一拍就都齐了。
    /// <para>
    /// **不写详情接口才有的那几样**：评分（CommunityRating）、事实那一行（分级、完结年份、工作室）、导演和演职
    /// 人员（People）、还有播放目标和它那三个文件选项（MediaSources）。那些字段在卡片上不是「空的」而是「没问过」
    /// —— 拿空值先画一遍，屏上就是一行事实凭空长出来、一个播放按钮先指着一个没有片源的目标。
    /// </para>
    /// <para>
    /// <see cref="Apply"/> 紧接着会用完整条目把整页重写一遍，包括这里写过的每一样；那时候相同的值不会再通知一次
    /// （生成的 setter 只在真变了的时候通知），而那四张图在图没变的时候整个留着（见 <see cref="Apply"/>）。
    /// </para>
    /// </summary>
    private void Preview(EmbyItem seed)
    {
        // **只在第一次载入时画。** 屏上已经有东西了就一个字都不许改：重新读取（「标记为已观看」之后那一次、手动
        // 刷新）走的是同一个 LoadAsync，而手上那张卡片是当初点进来时的那一份 —— 拿它盖一遍就是把整页退回旧值。
        // 看得见的那一处是那个勾：点「标记为已观看」之后它会先跳回未看，一趟往返之后才变回来；而那一趟要是失败
        // （服务器打个嗝），屏上就一直停在「未看」，可服务器上已经是看过了。
        if (_detail is not null) return;

        // 存根不够画一屏：集页那行剧名点过去、演职人员那一排点过去，交出来的只有 id、名字和类型（见
        // EmbyItem.IsStub）。拿它先画就是先写一遍「暂无简介」、头图的位置先空一下再长出来 —— 那是闪，不是快。
        if (seed.IsStub) return;

        _previews++;
        _detail = seed;

        // 这一页讲的是哪一类东西，从这一拍起就定了 —— 带子的高、名牌落哪个角、那一行文件选项开不开。少喊这一句的
        // 下场是第一屏用「没有条目」那一档的答案画，详情回来再跳一次。
        NavigationTiming.Mark("announce-begin");
        AnnounceShape();
        NavigationTiming.Mark("announce-end");

        // 这一份和 Apply 里那一段同源，所以两处的答案不会分叉 —— 这里少的正是「卡片答不出的那几样」。
        ApplyHeading(seed);
        Overview = ItemDetail.Prose(seed.Overview) is { Length: > 0 } prose ? prose : "暂无简介。";

        Watched = seed.IsWatched;
        Favorite = seed.UserData?.IsFavorite == true;

        // 这一格先按上限盒摆（集页那一格 260×375，见 FitStill）；图到手再按它自己的形状收窄。
        _stillPixels = null;
        _cornerPixels = null;
        RefitArtwork();

        HeroArt = SpreadsBackdrop(seed);

        // 四张图现在就开始取 —— 它们是最慢的一样，而卡片手上的标签和详情接口给的是同一批。
        _art?.Cancel();
        _art?.Dispose();
        _art = new CancellationTokenSource();
        _artStarts++;
        NavigationTiming.Mark("art-start");
        _ = LoadArtworkAsync(seed, _art);

        _previewed = Title;
    }

    private void AnnounceAffordances()
    {
        OnPropertyChanged(nameof(FavoriteActionVisibility));
        OnPropertyChanged(nameof(CanOpenGenre));
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 「这一页讲的是哪一类东西」变了之后要重新问一遍的那一批算得属性。<see cref="Preview"/> 和
    /// <see cref="Apply"/> 都会核对 —— 前者是第一次（在这之前 <see cref="_detail"/> 是 null，那几个属性答的是
    /// 「没有条目」那一档），后者只在完整条目更正了类型时再通知。同类型刷新由字段和集合自己的通知更新内容。
    /// <para>
    /// 一处集中而不是散在两边：这几个属性的共同点是它们跟着**条目的种类**走，不跟着任何一个字段走，所以生成的
    /// setter 一个都不会替它们通知。少喊一个的下场是屏上某一格停在上一档 —— 编译看不见，测试也进不来。
    /// </para>
    /// <para>
    /// 剧名上方那枚徽标<em>不在</em>这一批里，从「统一改为在剧名上方显示徽标」那次起就不在了：它的显隐只问一句
    /// 「图解出来了没有」（见 <see cref="PlateVisibility"/>），而那一句跟着 <see cref="PlateImage"/> 自己的
    /// setter 走。从前名牌摆哪个角是按页面的种类分的，那时候它确实要在这儿喊一声。（右上角那张艺术图也是这一句
    /// 的老主顾 —— 2026-09-12 整个退场，连着那一问一起走了。）
    /// </para>
    /// </summary>
    private void AnnounceShape()
    {
        AnnounceAffordances();
        // 能力跟来源和活动状态走，每次都重问；版式只在本访问首次确定或类型改变时整批通知。
        // 不能读 ItemType：它会回退到 seed，让存根首次 Apply 被误认为已经通知过。
        // 单独记字符串也能发现同一个可变 DTO 的 Type 被原地更正。
        var type = _detail?.Type;
        if (type is not null && type == _announcedShapeType) return;
        _announcedShapeType = null;
        var lifetime = _lifetime;
        RefreshCardLayout();
        RefitArtwork();
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(StillVisibility));
        OnPropertyChanged(nameof(CompactVisibility));

        // 单集 changes shape with the kind of page this is, and that is a fact about the item rather than
        // about the shelf — no collection has changed here, so nothing else would say it. Raised right after
        // the assignment, before anything can read a visibility off the item that is no longer showing.
        OnPropertyChanged(nameof(EpisodeListVisibility));
        OnPropertyChanged(nameof(EpisodeStripVisibility));
        OnPropertyChanged(nameof(CanPickEpisodeSeason));

        // 带子的高也跟着页面的种类走（集页按里面那一叠实测给，见 HeroHeight），那一带集摆在图上还是纸上同理。
        // 同样是「条目的事」而不是「谁的值变了」：从一部电影翻到一集时 HeroArt 和视口都可能一个字没改，
        // 那两处的通知一次都不会来。
        OnPropertyChanged(nameof(HeroLayoutHeight));
        OnPropertyChanged(nameof(HeroHeight));
        OnPropertyChanged(nameof(ScrimHeight));
        OnPropertyChanged(nameof(BodyMinHeight));
        OnPropertyChanged(nameof(TailMinHeight));
        OnPropertyChanged(nameof(PaperMinHeight));
        // 「这一页是不是电影/剧/季」也是沉浸版式（Showcase）和窄窗紧凑（Squeeze）的一句话；从一集翻到一部剧时
        // HeroArt 和视口可能一个字没改，那两处的通知一次都不会来。TailMinHeight / PaperMinHeight 读它，一并重喊。
        OnPropertyChanged(nameof(Showcase));
        OnPropertyChanged(nameof(Narrow));
        OnPropertyChanged(nameof(Squeeze));
        OnPropertyChanged(nameof(HeroContent));
        OnPropertyChanged(nameof(TailSpacing));
        OnPropertyChanged(nameof(TailPanelPadding));
        OnPropertyChanged(nameof(SheetPadding));
        OnPropertyChanged(nameof(SheetSpacing));
        OnPropertyChanged(nameof(BodyPanelPadding));
        OnPropertyChanged(nameof(ShowShelfLines));
        OnPropertyChanged(nameof(PictureHeight));
        OnPropertyChanged(nameof(HeroInset));
        OnPropertyChanged(nameof(TailInset));
        OnPropertyChanged(nameof(TailMargin));
        OnPropertyChanged(nameof(OverviewPanelPadding));
        OnPropertyChanged(nameof(CardTailGapHeight));
        OnPropertyChanged(nameof(HeroRoom));

        // 集页那块面板、以及它替掉的两层（头图的底色和带子下沿的罩子），三句判据里都有一句是「这一页是不是
        // 集页」，所以和上面那五个同宗：从一集翻到一部电影时 HeroArt 和视口可能一个字没改。
        OnPropertyChanged(nameof(HeroCardVisibility));
        OnPropertyChanged(nameof(HeroCardShown));
        OnPropertyChanged(nameof(HeroPlainVisibility));
        OnPropertyChanged(nameof(ScrimVisibility));

        // 片名那一叠靠上还是靠下（HeroContentAlignment）也是这句「是不是集页」的一句话，同一宗。
        OnPropertyChanged(nameof(HeroContentAlignment));
        OnPropertyChanged(nameof(CoverAlignment));

        // 片名那一栏里的那排键和那一条断点进度（ColumnActions/WideProgress）如今只按宽窄收放
        // （PageWidth 那一头有通知），跟「是不是集页」不再相干；还相干的是「视频：…」那一行让不让位给
        // 片名那一栏的 chips（VideoLineVisibility），以及文件选项那一行是搬在片名那一栏里还是在尾部
        // （两份只有一份在屏上）。
        OnPropertyChanged(nameof(ColumnActionsVisibility));
        OnPropertyChanged(nameof(WideProgressVisibility));
        OnPropertyChanged(nameof(ColumnPickersVisibility));
        OnPropertyChanged(nameof(VideoLineVisibility));
        OnPropertyChanged(nameof(ColumnVideoVisibility));

        // 那一行文件选项跟着播放键的落点开合（见 PickersVisibility）：落点是空的（人物页、集还没回来）就收起来。
        // 轨道那几个集合是上一个条目留下的，从一部剧翻到一集时它们可能一个都没变，那边的通知一次不会来。
        OnPropertyChanged(nameof(PickersVisibility));

        // 连播那颗跟着页面的种类收放（电影不画，见 ContinueVisibility）：从一部电影翻到一部剧时别的通知
        // 一次都不会来，这里不喊它就停在上一颗的状态。
        OnPropertyChanged(nameof(ContinueVisibility));

        // 页尾那张横幅只摆在三种页面上（电影、剧、集，见 FooterVisibility），也就是跟着页面的种类走。图本身可能
        // 还是上一个条目那张，那边的通知一次不会来。
        OnPropertyChanged(nameof(FooterVisibility));

        // 右上角那张艺术图只摆在集页上（见 CornerVisibility），同一句话对它也成立。
        OnPropertyChanged(nameof(CornerVisibility));

        // 头两行的那两格也读「这一页是不是季页」（见 SublineVisibility：季页的季名和类型行同时画出来）。
        // 从别的页面翻到季、或者从季翻走时，Subline 和 SublineGenres 可能一个字都没改 —— 那两处的通知
        // 一次不会来，屏上就停在上一档：季页只剩一行孤零零的季名，类型那一行不见了。
        OnPropertyChanged(nameof(SublineVisibility));
        OnPropertyChanged(nameof(GenreVisibility));

        // 中途通知失败不能记成完成；处理器若重新 Attach，也不能把旧访问的类型写回新访问。
        if (lifetime == _lifetime) _announcedShapeType = type;
    }

    /// <summary>
    /// 头两行：这一页最大的那行字，以及它底下那一行。
    /// <para>
    /// 季页自 2026-11-02 起和单集页反过来（用户令「把季页面的剧名和季度的字体大小还有位置互换」）：
    /// 大标题写剧名、点得回剧页面，第二行写季名；类型仍走第三行（图三那一条）。其余页面照旧 ——
    /// 单集页标题写剧名、第二行写集号与集名，电影和剧的标题写自己、第二行是类型。
    /// </para>
    /// </summary>
    private void ApplyHeading(EmbyItem item)
    {
        Title = item.Type == EmbyItemType.Season && item.SeriesName is { Length: > 0 } series
            ? series
            : ItemDetail.Title(item);
        TitleLink = ItemDetail.TitleTarget(item);
        Subline = item.Type == EmbyItemType.Season ? item.Name : ItemDetail.Subline(item);
        SublineGenres = ItemDetail.SublineGenres(item);
    }

    /// <summary>
    /// 评分那三件（分数、署名、自检读数）都在这一处写 —— 条目回来时写自己，季页的剧集那一层回来后再写一遍。
    /// </summary>
    private void ApplyScore(EmbyItem item)
    {
        var badge = ItemScore.Resolve(item);
        Score = badge.Text;
        ScoreLabel = badge.Label;
        ScoreBadge = badge;
        ScoreFacts = $"CommunityRating={Describe(item.CommunityRating)}"
            + $"，CriticRating={Describe(item.CriticRating)}"
            + $"，ProviderIds={(item.ProviderIds.Count == 0 ? "空" : string.Join('/', item.ProviderIds.Keys))}";
    }

    /// <summary>
    /// 季页的剧集那一层到了：大标题、可点的剧名、类型和事实行改用剧的数据。季自己的类型、评分、年份
    /// 往往都是空的，而用户要的图三那一行说的正是这部剧（2026-11-02「给季页面补上图三哪些」）。
    /// <para>
    /// 只改这几样，季自己的简介、演职人员、单集和季号都不动 —— 这一页讲的仍是这一季。
    /// </para>
    /// </summary>
    private void ApplySeasonSeries(EmbyItem series)
    {
        if (!IsSeasonPage) return;

        Title = series.Name;
        TitleLink = new EmbyItem
        {
            Id = series.Id,
            Source = series.Source,
            Name = series.Name,
            Type = EmbyItemType.Series,
            IsStub = true
        };
        SublineGenres = ItemDetail.SublineGenres(series);
        ApplyScore(series);
        Facts = ItemDetail.Facts(series);
    }

    /// <summary>
    /// 季页背后那部剧，为图三那一行和可点的剧名取一趟。剧集那一层可能已经在缓存里（从集页退回来、
    /// 或同一页重读）就直接用；取失败当没有，季页少一行事实，不影响这一页的其余内容。
    /// </summary>
    private async Task LoadSeasonSeriesAsync(EmbyItem season, CancellationToken token)
    {
        if (!Attached || season.Type != EmbyItemType.Season || season.SeriesId is not { Length: > 0 } seriesId)
            return;

        if (_show is { } cached && cached.Id == seriesId)
        {
            ApplySeasonSeries(cached);
            return;
        }

        try
        {
            var series = await _scope!
                .ExecuteAsync((client, ct) => client.GetItemAsync(seriesId, ct), token).ConfigureAwait(true);
            if (!IsCurrent(token) || !IsSeasonPage) return;

            _show = series;
            ApplySeasonSeries(series);
            RefreshOtherInfo();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"季页取剧集那一层失败（{season.Name}）：{error.Message}");
        }
    }

    /// <summary>
    /// Everything the item itself says, before anything else is fetched. Also the reset: every list and
    /// every string that belongs to the previous item is cleared here rather than left to whichever
    /// later step happens to overwrite it.
    /// </summary>
    private void Apply(EmbyItem item)
    {
        ObserveSubjectState(item);
        if (item.Id == _seed?.Id && item.Source?.Key == _scope?.Key)
            item.MediaSources.RemoveAll(SourceWasDeleted);
        // 这一趟要不要重取那四张图：<see cref="Preview"/> 刚用卡片那一份起过一次，而完整条目回来时那几个图片标签
        // 通常一个字都没变 —— 那时候整批留着。重取的代价不是一次下载（缓存都在），是屏上的图先被清空再回来，
        // 也就是点一张封面进来看见的那一次闪。
        var keepArtwork = ItemArtwork.SamePictures(_detail, item);

        // 上一页那部剧只在还是同一部剧时留着（同一页的重新读取省一趟）：单集和季都认同一个 SeriesId ——
        // 季页的头几行同样要这部剧（见 LoadSeasonSeriesAsync）。换集、换剧、回电影页都作废。
        if (_show is { } show
            && (item.Type is not (EmbyItemType.Episode or EmbyItemType.Season) || show.Id != item.SeriesId))
            _show = null;

        _detail = item;
        _loadedSeason = null;
        _episodes.Clear();
        _infoEpisode = null;
        _infoSource = null;

        NavigationTiming.Mark("announce-begin");
        AnnounceShape();
        NavigationTiming.Mark("announce-end");

        EpisodeFocus = 0;

        EpisodeShelf?.Clear();
        CastShelf?.Clear();
        SeasonShelf?.Clear();
        SimilarShelf?.Clear();
        Seasons.Clear();

        Expanded = false;
        OverviewTrimmed = false;

        ApplyHeading(item);
        // 「显示的评分」直接画服务器给的那个数（2026-10-16 起不再有「评分来源」可选）。规则在 Core（ItemScore）：
        // 取哪个槽、写谁的名字、这一槽没有就回落到哪儿，都是有唯一正确答案的判断，而错起来屏上只是一个看着挺正常的数。
        ApplyScore(item);
        Facts = ItemDetail.Facts(item);
        Directors = ItemDetail.Directors(item);
        Overview = ItemDetail.Prose(item.Overview) is { Length: > 0 } prose ? prose : "暂无简介。";

        Watched = item.IsWatched;
        Favorite = item.UserData?.IsFavorite == true;

        // 图留着的那一档连这些一起留着：海报到手时 FitStill 已经把它们按图收窄过，写回上限就是把那一格重新
        // 撑开一次，屏上是海报周围凭空多出一条边再收回去。（上限盒本身是定值，所以这里不写死两个数，交给
        // FitStill 算 —— 集页那一页的盒子将来要改，改的也只是它自己那对常量。）
        if (!keepArtwork)
        {
            _stillPixels = null;
            _cornerPixels = null;
            RefitArtwork();
        }

        // 再问一遍，因为完整条目才是权威的那一份 —— 列表上的条目偶尔比它少几个标签。多数时候两次的答案一样，
        // 于是这一句什么也不改：生成的 setter 只在值真变了的时候才通知。
        HeroArt = SpreadsBackdrop(item);

        // 图一样就整批留着，连正在飞的那一趟一起（Fresh 比的是「要画的还是这批图」而不是「还是同一个对象」，
        // 所以卡片那一份起的解码回来照样贴得上）。不一样才清空重取 —— 换季、翻页、或者服务器上换过图。
        if (!keepArtwork)
        {
            HeroImage = null;
            StillImage = null;
            PlateImage = null;
            CornerImage = null;
            CornerHeight = 0;
            FooterImage = null;
            FooterPick = null;

            // 裁切模糊的原料和档位跟着这一批图一起作废：在路上的重糊回来时生代号对不上，自己会扔。
            _heroPixels = null;
            _heroBlurRadius = -1;
            _heroBlurGeneration++;
            _heroRendering = false;

            _art?.Cancel();
            _art?.Dispose();
            _art = new CancellationTokenSource();
            _artStarts++;
            _ = LoadArtworkAsync(item, _art);
        }

        CastShelf?.Fill(ItemDetail.Cast(item).Select(credit => (credit.Card, (string?)credit.Credit)));

        var knownSourceRemoved = (_deletedSourceIds.Count > 0 || _deletedSourcePaths.Count > 0) && item.Id == _seed?.Id
            && item.Source?.Key == _scope?.Key && item.MediaSources.Count == 0;
        PlayTarget = item.IsPlayable && !knownSourceRemoved ? item : null;
        ShowTarget(PlayTarget);

        // 放在最后：它读的是刚写好的 _detail，而 _show 可能早在取图那一趟就已经回来了。
        RefreshOtherInfo();

        _actionsReady = true;
        NotifyActionAvailability();
        NavigationTiming.Mark("actions-ready");
    }

    /// <summary>
    /// Fills in everything that describes the file 播放 would start: the 媒体源 picker, the two track
    /// pickers, the video line and the 媒体信息 panel. Null clears all of it — a show is not a file.
    /// <para>
    /// The last two lines are what replaces the 「are we the ones assigning this?」 flag the page used to
    /// keep. <see cref="ShowSource"/> has to run exactly once, and the generated setter raises its
    /// changed handler only when the value actually differs; so assigning a row the picker already holds
    /// would silently skip the work, and assigning a different one would do it twice if this method did
    /// it as well. Asking which case this is settles both, and the comparison is the same
    /// <c>EqualityComparer&lt;T&gt;.Default</c> the generated setter itself uses.
    /// </para>
    /// </summary>
    private void ShowTarget(EmbyItem? target)
    {
        _versionLoad?.Cancel();
        var generation = ++_versionGeneration;
        _versions = MediaVersionDiscovery.Local(target);
        Sources.Clear();

        if (target is null)
        {
            SelectedSource = null;
            ShowSource(null);
            return;
        }

        foreach (var version in _versions) Sources.Add(new SourceRow(version.Source, version.LabelFor(MediaVersionDiscovery.HasMultipleServers(_session!.Sources))));

        // 默认预选哪一版：多版本时按视频文件名筛选规则挑（单版本／没规则时就是服务器第一版，同从前）。
        var rules = Attached ? Settings.Playback.VideoFileRules : null;
        var wanted = MediaVersionSwitch.Preferred(target, rules) ?? target.DefaultMediaSource ?? target.MediaSources.FirstOrDefault();
        var picked = ItemDetail.PickSource(Sources, wanted);

        if (Equals(SelectedSource, picked)) ShowSource(picked?.Source);
        else SelectedSource = picked;
        if (Attached) _ = LoadVersionsAsync(target, generation, _lifetime);
    }

    private async Task LoadVersionsAsync(EmbyItem target, int generation, int lifetime)
    {
        using var cancellation = new CancellationTokenSource();
        _versionLoad = cancellation;
        try
        {
            var result = await MediaVersionDiscovery.FindAsync(_session!, target,
                Settings.Playback.MediaSourceOrder, cancellation.Token).ConfigureAwait(true);
            if (!Attached || generation != _versionGeneration || lifetime != _lifetime) return;
            _versions = result.Versions.Where(version => version.Item.Id != _seed?.Id || version.Item.Source?.Key != _scope?.Key
                || !SourceWasDeleted(version.Source)).ToArray();
            foreach (var version in _versions.Skip(Sources.Count))
                Sources.Add(new SourceRow(version.Source, version.LabelFor(MediaVersionDiscovery.HasMultipleServers(_session!.Sources))));
            if (result.FailedSources.Count > 0)
                _actions?.Notify($"部分来源的版本读取失败：{string.Join("、", result.FailedSources)}", InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Attached && generation == _versionGeneration && lifetime == _lifetime)
                _actions?.Notify($"读取其他来源版本失败：{Failure.Describe(error)}", InfoBarSeverity.Warning);
        }
        finally
        {
            if (ReferenceEquals(_versionLoad, cancellation)) _versionLoad = null;
        }
    }

    /// <summary>
    /// Applies one media source: its tracks, its video line and its 媒体信息 panel. Runs for the source
    /// the page lands on by itself as well as for one the user picks, so the panel and the picker above
    /// it can never describe two different files.
    /// </summary>
    private void ShowSource(MediaSource? source)
    {
        VideoLine = ItemDetail.VideoLine(source);

        AudioTracks.Clear();
        SubtitleTracks.Clear();

        if (source is not null && Attached)
        {
            var playback = Settings.Playback;
            foreach (var row in ItemDetail.AudioRows(playback, source)) AudioTracks.Add(row);
            foreach (var row in ItemDetail.SubtitleRows(playback, source)) SubtitleTracks.Add(row);
        }

        // Assigned rather than left to the picker's own reset. A picker that has never been realised —
        // the page was navigated away from before it drew — writes nothing back, and the stale track it
        // would leave behind is one 播放 would then hand to mpv.
        SelectedAudio = AudioTracks.FirstOrDefault();
        SelectedSubtitle = SubtitleTracks.FirstOrDefault();

        // 媒体信息 describes a file: 电影与单集说自己这一版（当前媒体源），剧页说
        // _infoEpisode 那一集（2026-11-02 用户令「剧页面的更多信息卡片也加上媒体信息」）。季页没有这张表
        // （同日用户令「季页面不要媒体信息」）。三个答案都从 RefreshInfoRows 一处出，换媒体源和集列表回来
        // 不会各写一份。
        RefreshInfoRows(source);
    }

    /// <summary>
    /// 媒体信息表该说哪个文件 —— 文件页（电影、单集）说当前媒体源那一版；剧页说
    /// <see cref="_infoEpisode"/> 那一集，有续播取最近播放的续播集，没有就取当季第一集
    /// （<see cref="ItemDetail.MediaInfoEpisode"/>）。季页和其他页面什么都不填。
    /// <para>
    /// 剧页和播放键看的是两件事：播放键从第一集未看的开始，媒体信息表按用户的话跟着「播放记录」走。
    /// 两者指向同一集时（常态），表跟着媒体源下拉换版；指向不同集时，表始终说它自己那一集，不被另一个
    /// 文件的下拉带着跑。
    /// </para>
    /// </summary>
    private void RefreshInfoRows(MediaSource? source)
    {
        InfoRows.Clear();
        if (!Attached || _detail is null) return;

        if (_detail.IsPlayable)
        {
            var owner = _versions.FirstOrDefault(version => ReferenceEquals(version.Source, source))?.Item ?? _detail;
            foreach (var row in ItemDetail.MediaInfo(owner, source)) InfoRows.Add(row);
            return;
        }

        if (_detail.Type != EmbyItemType.Series || _infoEpisode is not { } episode) return;

        var described = PlayTarget is { } target && target.Id == episode.Id
            ? source ?? _infoSource
            : _infoSource;
        if (described is null) return;

        foreach (var row in ItemDetail.MediaInfo(episode, described)) InfoRows.Add(row);
    }

    /// <summary>
    /// 剧页那一段媒体信息要描述的那一集，从全剧集列表里挑
    /// （<see cref="ItemDetail.MediaInfoEpisode"/>；剧页的集列表现在是全季一条，所以这里传 null 表示整份），
    /// 再把它的媒体源取齐 —— 列表接口下发的媒体源常常不带轨道表（见 <see cref="ItemDetail.CarriesTracks"/>），
    /// 多数时候要按 id 补一趟。
    /// <para>
    /// 播放落点（<see cref="PlayTarget"/>）已经在同一趟里按需取过时直接复用，不再问一遍；补取失败退回列表
    /// 那一份（表里只剩说得清的几行），页面其余内容照旧（同取图那一头）。成功取到但服务器没给媒体源时也收起
    /// 这张表 —— 剧页上「服务器没有返回可播放的媒体源」那一行说的是另一个问题，摆在这里只会误导。
    /// </para>
    /// </summary>
    private async Task LoadInfoEpisodeAsync(
        IReadOnlyList<EmbyItem> episodes, EmbyItem? season, EmbyItem? playTarget, CancellationToken token)
    {
        if (!Attached || _detail?.Type != EmbyItemType.Series) return;

        _infoEpisode = null;
        _infoSource = null;

        if (ItemDetail.MediaInfoEpisode(episodes, season) is not { } picked) return;

        var detail = picked;
        if (playTarget is { } target && target.Id == picked.Id && target.MediaSources.Count > 0)
        {
            detail = target;
        }
        else if (!ItemDetail.CarriesTracks(detail))
        {
            detail = await FetchTracksAsync(detail, token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;
        }

        var rules = Settings.Playback.VideoFileRules;
        _infoEpisode = detail;
        _infoSource = MediaVersionSwitch.Preferred(detail, rules) ?? detail.DefaultMediaSource;
        RefreshInfoRows(SelectedSource?.Source);
    }

    /// <summary>
    /// 列表那一份只有媒体源、没有轨道表时，按 id 补取整集详情（<see cref="ItemDetail.CarriesTracks"/>）。
    /// 补取是锦上添花，失败不能把页面弄坏：退回列表那一份，音轨、字幕下拉退回「自动（由 mpv 决定）」，
    /// 只在 Debug 里说明（用户 2026-11-02 那条报的就是这一行字，补不上时照实说，不编一条假轨道）。
    /// 取消照旧冒上去 —— 那是换页，不是失败。
    /// </summary>
    private async Task<EmbyItem> FetchTracksAsync(EmbyItem episode, CancellationToken token)
    {
        try
        {
            return await _scope!.ExecuteAsync((client, ct) => client.GetItemAsync(
                episode.Id, ct, order: Settings.Playback.MediaSourceOrder), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"详情页补取轨道失败（{episode.Name}）：{error.Message}");
            return episode;
        }
    }

    /// <summary>
    /// Rebuilds the 其他信息 line. Called when a page is applied and again when the show behind an episode
    /// arrives — the series is fetched for artwork, and this line reads its genres, studio and scraper ids.
    /// <para>
    /// 哪些页有这一行、这一行说谁，全在 <see cref="ItemDetail.OtherInfo"/> 里（电影与剧说自己，集说剧集那一层，
    /// 季什么也不说）；这里不另判 —— 判据分两处写就会分叉，而分叉的那一版在屏上只是「剧页少一行」。
    /// </para>
    /// </summary>
    private void RefreshOtherInfo() =>
        OtherFacts = _detail is { } item
            ? ItemDetail.OtherInfo(item, item.Type == EmbyItemType.Episode ? _show : null)
            : [];

    private async Task LoadSeriesAsync(EmbyItem series, CancellationToken token)
    {
        if (!Attached) return;

        var seasons = await _scope!
            .ExecuteAsync((client, ct) => client.GetSeasonsAsync(series.Id, ct), token).ConfigureAwait(true);
        if (!IsCurrent(token)) return;

        seasons.RemoveAll(WasDeleted);
        var picked = ItemDetail.PickSeason(seasons);
        _syncingSeason = true;
        try
        {
            Seasons.Clear();
            foreach (var season in seasons) Seasons.Add(season);
            SelectedSeason = picked;
        }
        finally { _syncingSeason = false; }

        // 「全部剧季」列服务器给的全部季，第 0 季（特别篇）也在里面 —— 2026-11-03 晚用户令「全部剧集卡片要
        // 显示特别篇（S00季）的封面」，原先把第 0 季滤掉的那一刀因此撤掉。顺序照服务器给的那一份（第 0 季
        // 在最前），和集列表牌子的季菜单同源。2026-11-04 删除重复的特别篇单集排后，季封面入口仍保留。
        SeasonShelf?.Fill(seasons, ItemDetail.SeasonSubtitle);

        await LoadEpisodesAsync(picked, retarget: true, token).ConfigureAwait(true);
    }

    /// <summary>
    /// 集页那张「全部剧季」卡 —— 2026-11-02 用户令「集页面也加上全部剧季卡片」。剧页那一份由
    /// <see cref="LoadSeriesAsync"/> 顺手填好（它本来就要读这部剧的季清单去挑一季来列集）；集页没有那一趟 ——
    /// 这一页不读这部剧自己的详情 —— 所以这里单独问一遍季清单。
    /// <para>
    /// **只填 <see cref="SeasonShelf"/>，不碰 <see cref="Seasons"/>**：那个集合是季选择器和自检在用的，而集页上的
    /// 季选择器同一天已被删掉。两边都填会让自检读到的「季几行」在这一页翻倍，也会让 <see cref="SeasonVisibility"/>
    /// 在一个没有选择器的页面上说「有」。
    /// </para>
    /// <para>
    /// 卡片几张才画归 <see cref="SeasonShelfVisibility"/>（两张以上）；列的是服务器给的全部季，第 0 季也在
    /// 里面（2026-11-03 晚用户令要它的封面），所以「一季正片 + 特别篇」的集页也会画出这张卡。
    /// </para>
    /// <para>
    /// 失败当「这部剧没有季清单」：少一张卡，不是这一页加载失败（同 <see cref="LoadSimilarAsync"/>）。
    /// </para>
    /// </summary>
    private async Task LoadSeasonShelfAsync(EmbyItem item, CancellationToken token)
    {
        if (!Attached || SeasonShelf is not { } shelf) return;

        // 只有集页要这一趟：剧页的季清单在 LoadSeriesAsync 里已经回来并填好了，季页、电影页不要这张卡
        // （用户点名的只有集页）。
        if (item.Type != EmbyItemType.Episode || item.SeriesId is not { Length: > 0 } series) return;

        try
        {
            var seasons = await _scope!
                .ExecuteAsync((client, ct) => client.GetSeasonsAsync(series, ct), token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;

            // 同剧页：已经删掉的那一季不再摆一张通往空页面的卡（判据在 WasDeleted）；第 0 季照旧列进来 ——
            // 第 0 季的封面入口保留，和主集列表中的特别篇共用原有导航。
            seasons.RemoveAll(WasDeleted);
            shelf.Fill(seasons, ItemDetail.SeasonSubtitle);

            // 集页的季列表和剧页共用同一支下拉；Seasons 填上之后，SelectedSeason 才能指向清单里的真季，
            // 而不是 LoadEpisodesAsync 用这一集自己的字段拼出来的临时锚点。
            _syncingSeason = true;
            try
            {
                Seasons.Clear();
                foreach (var season in seasons) Seasons.Add(season);
                AdoptEpisodeAnchor(ItemDetail.SeasonOf(seasons, item) ?? SeasonAnchor(item));
            }
            finally { _syncingSeason = false; }

            UpdateEpisodeHeading(SelectedSeason);
            OnPropertyChanged(nameof(CanPickEpisodeSeason));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"集页取剧季失败（{item.Name}）：{error.Message}");
        }
    }

    /// <param name="retarget">
    /// True when 播放 should follow the list — the first unwatched episode of the season just loaded.
    /// False on an episode's own page, where the target is the episode the reader opened.
    /// </param>
    private async Task LoadEpisodesAsync(EmbyItem? season, bool retarget, CancellationToken token)
    {
        if (!IsCurrent(token) || _detail is not { } page || EpisodeShelf is not { } shelf) return;
        var source = _scope!;
        var lifetime = _lifetime;
        var timing = NavigationTiming.Capture();
        bool Current() => IsCurrent(token) && lifetime == _lifetime && ReferenceEquals(source, _scope)
            && ReferenceEquals(page, _detail) && ReferenceEquals(shelf, EpisodeShelf);

        var scope = ItemDetail.EpisodeScope(page, season);

        // 剧页和集页要的是「所有季连起来」的那一条（网页端的样子），所以不带 SeasonId 问全剧；
        // 季页读的是这一季自己的清单，照旧按季问。三种入口的差别只写在这里。
        var allSeasons = page.Type is EmbyItemType.Series or EmbyItemType.Episode;
        var episodes = await source
            .ExecuteAsync((client, ct) => client.GetEpisodesAsync(
                scope.SeriesId, allSeasons ? null : scope.SeasonId, ct), token)
            .ConfigureAwait(true);
        if (!Current()) return;
        episodes.RemoveAll(WasDeleted);

        // 剧页、集页按播出时间排：特典落在它实际播出的位置，而不是因为记在第 0 季就永远排在最前。
        // 季页保留服务器按季给的顺序 —— 那一页的清单本身就是「这一季有哪些集」。
        timing?.Mark("episodes-sort-begin", episodes.Count);
        if (allSeasons)
        {
            // The response list is private until publication. AirOrder only reads immutable scalar
            // ordering fields; no CardItem, shared UserData or page-owned collection is touched here.
            var response = episodes;
            episodes = await Task.Run(() => ItemDetail.AirOrder(response).ToList(), token).ConfigureAwait(true);
            if (!Current()) return;
            // A confirmed deletion may have happened while sorting. Read the UI-owned set only here.
            episodes.RemoveAll(WasDeleted);
        }
        timing?.Mark("episodes-sort-end", episodes.Count);

        // 播放目标也要读齐才提交，不能把新列表与旧目标组合成可播放状态。剧页从全剧里挑播出时间最早的
        // 未看集；季页仍只从这一季里挑。两种都走 ItemDetail.PickEpisode，规则只写一份。
        EmbyItem? target = null;
        var picked = page.Type == EmbyItemType.Series
            ? ItemDetail.PickEpisode(episodes, null)
            : ItemDetail.PickEpisode(episodes, season);
        if (retarget && picked is { } choice)
        {
            // 列表接口下发的集常常有媒体源、没有轨道表（见 ItemDetail.CarriesTracks）：那一份建不出音轨和
            // 字幕下拉，自动那一行只剩「自动（由 mpv 决定）」。缺轨道表就按 id 补取整集详情；带完整轨道的
            // 列表条目照旧直接用，不多跑一趟。
            target = choice;
            if (!ItemDetail.CarriesTracks(choice))
            {
                // 一条媒体源都没有仍是老规矩：取不到就是这一页加载失败（那一档本来就没有东西可播）。
                // 只是缺轨道表时才走可失败的补取，取不到退回列表那一份，页面照常加载。
                target = choice.MediaSources.Count == 0
                    ? await source.ExecuteAsync((client, ct) => client.GetItemAsync(
                        choice.Id, ct, order: Settings.Playback.MediaSourceOrder), token).ConfigureAwait(true)
                    : await FetchTracksAsync(choice, token).ConfigureAwait(true);
            }
            if (!Current()) return;
        }

        // 剧页的锚点是挑中的那一季；集页先从这一集自己的字段拼一个临时锚点，季清单回来后换成真季。
        // 季页不参与季下拉，_loadedSeason 只是这一页加载的是哪一季这个事实。
        _loadedSeason = season;
        if (page.Type == EmbyItemType.Series) AdoptEpisodeAnchor(season);
        else if (page.Type == EmbyItemType.Episode) AdoptEpisodeAnchor(SeasonAnchor(page));
        if (!Current()) return;

        _episodes.Clear();
        _episodes.AddRange(episodes);

        // One shelf, two shapes. Rows on a season page — and the page itself goes in so the row that is this
        // page can mark itself, which on any other page matches nothing and marks nothing. Cards everywhere
        // else, where the second line is 「第 7 集 · 46 分钟」 rather than the card default's 「S02E09 ·
        // 剧名」: both halves of that are already on this page.
        timing?.Mark("episodes-publish-begin", episodes.Count);
        shelf.FillDeferred(episodes, EpisodesAsList ? null : ItemDetail.EpisodeCardSubtitle, page);
        if (!Current()) return;
        timing?.Mark("episodes-publish-end", episodes.Count);

        // 剧页、集页的标题用锚点那一季的名字；季页仍是这一季自己。数量与已看数不再写进标题
        // （2026-11-03 用户令「去掉后面的xx集，已观看到0集」）。
        var heading = page.Type is EmbyItemType.Series or EmbyItemType.Episode
            ? SelectedSeason?.Name ?? scope.SeasonName
            : scope.SeasonName;
        shelf.Title = ItemDetail.EpisodeHeading(heading);
        if (!Current()) return;

        // Assigned after the cards, so the strip has something to measure itself against; it keeps the
        // request until the first card is laid out either way. 剧页开在锚点那一季的第一张，集页开在
        // 正在看的那一集；季页是竖列表，这个数不参与排版。
        EpisodeFocus = page.Type == EmbyItemType.Series
            ? Math.Max(0, ItemDetail.FirstEpisodeIndex(episodes, season))
            : ItemDetail.EpisodeFocus(episodes, page);
        if (!Current()) return;

        if (retarget)
        {
            PlayTarget = target;
            if (!Current()) return;
            ShowTarget(target);
            if (!Current()) return;
        }

        // 放在播放落点之后：剧/季页那张表可能复用同一趟取回来的目标集，而 ShowTarget 会先清一次表。
        await LoadInfoEpisodeAsync(episodes, allSeasons ? null : season, target, token).ConfigureAwait(true);
    }

    /// <summary>
    /// 把这一集自己的季字段拼成一个季对象，给集页在没有季清单时当临时锚点用。名字、季号、季 id 都照抄，
    /// 只把类型改成 Season —— <see cref="ItemDetail.EpisodeScope"/> 和标题都靠这三位认人。
    /// </summary>
    private static EmbyItem SeasonAnchor(EmbyItem episode) => new()
    {
        Id = episode.SeasonId ?? "",
        Name = episode.SeasonName ?? "",
        Type = EmbyItemType.Season,
        SeriesId = episode.SeriesId,
        SeriesName = episode.SeriesName,
        IndexNumber = episode.ParentIndexNumber
    };

    /// <summary>
    /// 把季锚点落到 <see cref="SelectedSeason"/> 上。清单里已经有这一季时用清单那一份（自检要求选择器
    /// 的值就在它自己的清单里），没有才用临时拼的那一份；清单晚到时会再叫一次，把它换掉。
    /// </summary>
    private void AdoptEpisodeAnchor(EmbyItem? candidate)
    {
        if (candidate is null) return;

        var anchor = ItemDetail.SeasonOf(Seasons, candidate) ?? candidate;
        _loadedSeason = anchor;

        _syncingSeason = true;
        try { SelectedSeason = anchor; }
        finally { _syncingSeason = false; }
    }

    /// <summary>
    /// 集列表牌子的标题跟着锚点走，只写锚点那一季的名字。
    /// </summary>
    private void UpdateEpisodeHeading(EmbyItem? anchor)
    {
        if (EpisodeShelf is not { } shelf) return;

        var name = anchor?.Name
            ?? (_detail is null ? null : ItemDetail.EpisodeScope(_detail, null).SeasonName);
        shelf.Title = ItemDetail.EpisodeHeading(name);
    }

    /// <summary>
    /// 集带翻页时，让标题跟着带子左沿那一集走 —— 2026-11-03 用户令「集列表翻页翻到第x季第1集或者最后一集时
    /// 上方要自动切换为对应季」。只改季锚点，不重读、不动播放落点；锚点没变时标题也不重写。
    /// </summary>
    internal void FollowEpisode(EmbyItem? episode)
    {
        if (episode is null || !Attached) return;
        if (_detail?.Type is not (EmbyItemType.Series or EmbyItemType.Episode)) return;

        if (ItemDetail.SeasonOf(Seasons, episode) is { } season)
        {
            if (SelectedSeason?.Id == season.Id) return;
            SelectedSeason = season;
            return;
        }

        // 季清单里没有这一季（例如服务器没把特别篇列进季清单）：标题照这一集自己的字段走，
        // 但不把锚点写成清单外的一季 —— 季列表里没有它，选中标记也就无处可落。
        UpdateEpisodeHeading(SeasonAnchor(episode));
    }

    /// <summary>
    /// 更多类似. Not awaited-failed: a server with no recommendations, or one that refuses the Similar
    /// call, leaves an empty row hidden rather than an error on a page whose whole job was done. A
    /// person is not offered 「items like this person」 either — Emby skips it there too.
    /// </summary>
    private async Task LoadSimilarAsync(EmbyItem item, CancellationToken token)
    {
        if (!Attached || SimilarShelf is not { } shelf) return;
        if (item.Type == EmbyItemType.Person) return;

        try
        {
            var similar = await _scope!
                .ExecuteAsync((client, ct) => client.GetSimilarAsync(item.Id, ItemDetail.SimilarLimit, ct), token)
                .ConfigureAwait(true);
            if (!IsCurrent(token)) return;

            shelf.Fill(similar.Where(item => !WasDeleted(item)));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"加载更多类似失败（{item.Name}）：{error.Message}");
        }
    }

    /// <summary>
    /// Makes one episode the thing 播放 would start. Re-fetched when the list's copy carries no tracks —
    /// most of the time, since the fields a list asks for bring media sources without their stream table
    /// (<see cref="ItemDetail.CarriesTracks"/>) — and the pickers, the video line and 媒体信息 are all
    /// about the tracks. 补取失败时退回列表那一份（见 <see cref="FetchTracksAsync"/>）：这条路上一次
    /// 读失败不该变成一个报错，更不该让播放键没有落点。
    /// </summary>
    private async Task ApplyPlayTargetAsync(EmbyItem target, CancellationToken token)
    {
        if (!Attached) return;

        var detail = target;
        if (!ItemDetail.CarriesTracks(target))
        {
            // 同 LoadEpisodesAsync：无媒体源那一档照旧让失败冒上去（调用方还有自己的提示），只是缺轨道表
            // 的补取可以失败。
            detail = target.MediaSources.Count == 0
                ? await _scope!.ExecuteAsync((client, ct) => client.GetItemAsync(
                    target.Id, ct, order: Settings.Playback.MediaSourceOrder), token).ConfigureAwait(true)
                : await FetchTracksAsync(target, token).ConfigureAwait(true);
        }
        if (!IsCurrent(token)) return;

        PlayTarget = detail;
        ShowTarget(detail);
    }

    /// <summary>
    /// 集页背后那部剧。取图那一趟顺路取回来（右上角艺术图、页尾横幅和左上角封面都要借它的图），其他信息
    /// 那一行要的类型、工作室和刮削 id 也在这份条目上 —— 单集自己身上这三样都是空的，所以两处共用这一趟，
    /// 取回来以后记在 <see cref="_show"/>。同一页里只问一次；换集时 <see cref="Apply"/> 会把不属于这一集的清掉。
    /// <para>
    /// 失败当「没有剧」处理，和原来取图那一头一样：少借几张图、少一行信息，不是这一页加载失败。
    /// </para>
    /// </summary>
    private async Task<EmbyItem?> LoadShowAsync(EmbyItem item, CancellationTokenSource art)
    {
        if (!Attached || item.Type != EmbyItemType.Episode || item.SeriesId is not { Length: > 0 } series)
            return null;

        if (_show is { } cached && cached.Id == series) return cached;

        try
        {
            var show = await _scope!
                .ExecuteAsync((client, ct) => client.GetItemAsync(series, ct), art.Token).ConfigureAwait(true);
            if (!Fresh(art, item)) return show;

            _show = show;
            RefreshOtherInfo();
            return show;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error)
        {
            Log.Debug(Category, $"取剧集那一层失败（{item.Name}）：{error.Message}");
            return null;
        }
    }

    private async Task LoadArtworkAsync(EmbyItem item, CancellationTokenSource art)
    {
        if (!Attached) return;
        NavigationTiming.Mark("art-load-begin");
        _heroTiming = NavigationTiming.Capture();

        // 左上角那一格封面（「集页面的封面改用剧页面的封面」，2026-09-12）：集页取剧集那一层的 Primary ——
        // 就是剧页面上那张 2:3 海报，借的也是同一条路（show 在 WhenAll 外头就已经在路上了）；借不到（没有
        // SeriesId、服务器没应答、或者剧自己连一张封面都没有）才退回本集自己的那几张 —— 有一张剧照总比
        // 空着一格强。其余页面照旧取自己的 Primary / Thumb。
        // 集页简洁宽窗不铺背景图，沉浸宽窗与紧凑版式铺图；取图不看这一页这次铺不铺：
        // 窗口从宽拉窄跨过紧凑断点的那一下答案就从不要翻成要，图已经在手，画面就地出来，不用再等一趟往返
        // （2026-10-13「再给集页面的窄窗口加背景图」）。集页多问的倒是另一趟：剧集那一层的条目。右上角那张
        // 艺术图（<see cref="ItemArtwork.Corner"/>）、页尾那张横幅（<see cref="ItemArtwork.Footer"/>）和左上角
        // 这张封面都可能要借它的 —— 服务器不下发 ParentArt / ParentBanner，剧集那个条目只有自己取到才借得成。
        // 一趟往返三处共用；电影和剧自己就是答案，不用问。取回来的那一份也缓存进 _show —— 其他信息那一行要的
        // 类型、工作室和刮削 id 也在这份条目上，两处共用这一趟。
        var show = LoadShowAsync(item, art);

        var plate = ItemArtwork.Plate(item);

        try
        {
            // 四张图分头去取。以前是一张接一张：名牌回来了才开始要头图，头图回来了才开始要剧照 —— 三次往返
            // 排成一队，慢的那一张拖住后面两张。它们之间没有任何依赖，谁先回来谁先显示，版面不看先后。
            await Task.WhenAll(
                    Paint(DecodeFirstAsync(Refs(plate), PlateDecodeWidth, art.Token),
                        picture => PlateImage = picture),
                    HeroAsync(item, art.Token),
                    Paint(CornerAsync(item, show, art.Token), ShowCorner),
                    Paint(FooterAsync(item, show, art.Token), picture => FooterImage = picture),
                    Paint(StillAsync(item, show, art.Token), ShowStill))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            // A missing picture is not worth a notice bar; the page reads perfectly well without one.
            Log.Debug(Category, $"加载详情图片失败：{error.Message}");
        }

        NavigationTiming.Mark("art-load-end");

        // Every one of the four ends the same way: show it if it arrived and if this page is still the
        // page that asked. Both halves of that check matter — see Fresh.
        async Task Paint(Task<BitmapImage?> decode, Action<BitmapImage> assign)
        {
            var picture = await decode.ConfigureAwait(true);
            if (Fresh(art, item) && picture is not null)
            {
                assign(picture);
                NavigationTiming.Mark("image-assign");
            }
        }

        // 电影和剧的头图：解到像素存起来（_heroPixels —— 裁切模糊的原料），按当前的模糊档位渲染上屏，自己
        // 完成上屏（不经 Paint：它要多带一个生代号，重糊那一头比它新就不许盖上去）。「当窗口拉宽导致背景图
        // 下方被裁切的时候，触发背景图模糊，裁切越多越模糊」：半径由 DetailHero.PictureCrop ×
        // BackdropBlur.CropRadius 算（电影和剧没有基础半径，裁多少加多少；量化成档，拖窗口不为一个像素重糊），
        // 档位换了由 UpdateHeroBlur 用同一批像素重糊。集页的宽版式不画这一张（紧凑版式画），所以那一页上这一
        // 路解出来的东西多数时候躺在 _heroPixels 里等一次窗口变窄。
        // 头图多带一个数上屏：这张图自己的高÷宽（ShowHero）——紧凑版式的带高和背景盒高都按它算
        // （DetailHero.CompactHeight / PictureHeight）；量不成话（0 或负）就不动兜底的那 0.5625。
        async Task HeroAsync(EmbyItem page, CancellationToken token)
        {
            var cache = _heroCache;
            var cacheGeneration = cache?.Generation;
            var scope = _scope!;
            DetailHeroCache.Ticket? selectedTicket = null;
            var source = await ArtworkCandidates.FirstAsync<ArtworkRef, HeroPictureLoader.HeroPixels>(
                ItemArtwork.Hero(page), async (picture, candidateToken) =>
                {
                    candidateToken.ThrowIfCancellationRequested();
                    scope.ThrowIfNotAvailable();
                    var ticket = cache?.Begin(scope, picture, HeroDecodeWidth, cacheGeneration);
                    HeroPictureLoader.HeroPixels? decoded;
                    if (ticket is not null && cache!.TryGetPixels(ticket, out var kept))
                    {
                        decoded = kept;
                        NavigationTiming.Mark("hero-pixels-hit", 1);
                    }
                    else
                    {
                        decoded = await _images!.GetDecodedAsync(scope, picture.ItemId, picture.ImageType, picture.Tag,
                            EmbyImageStore.RequestWidth(HeroDecodeWidth),
                            bytes => HeroPictureLoader.DecodePixelsAsync(bytes, HeroDecodeWidth, candidateToken), candidateToken)
                            .ConfigureAwait(true);
                    }
                    candidateToken.ThrowIfCancellationRequested();
                    scope.ThrowIfNotAvailable();
                    if (decoded is not null)
                    {
                        selectedTicket = ticket;
                        if (ticket is not null) cache!.RememberPixels(ticket, decoded);
                    }
                    return decoded;
                }, token).ConfigureAwait(true);
            if (source is null || !Attached || !Fresh(art, page)) return;

            // 先用解码后的真实比例排图，再算可见下沿；初次加载不能沿用16:9兜底。
            HeroHeightRatio = source.Height / (double)source.Width;
            _heroPixels = source;
            _heroCacheTicket = selectedTicket;
            var radius = HeroBlurTarget;
            var generation = ++_heroBlurGeneration;
            _heroBlurRadius = radius;
            _heroRendering = true;

            var rendered = await RenderHeroAsync(source, radius, token, selectedTicket, _heroTiming).ConfigureAwait(true);
            if (rendered is null || !Attached || !Fresh(art, page) || generation != _heroBlurGeneration) return;
            _heroRendering = false;
            ShowHero(rendered);
            NavigationTiming.Mark("hero-image-assign", 1);
        }

        // 头图多带一个数上屏：这张图自己的高÷宽。紧凑版式的带高和背景那张的盒高都按它算
        // （DetailHero.CompactHeight / PictureHeight —— 「窗口收窄时背景图要等比例缩放」），解出来的是
        // 什么形状就照什么形状缩；量不成话（0 或负）就不动兜底的那 0.5625。集页的宽版式不画这张图，所以这一手
        // 在那一页上多数时候白跑一趟 —— 而它算的正是窗口变窄之后要用的那个数，那一趟不能省。
        void ShowHero(BitmapImage picture)
        {
            if (picture.PixelWidth > 0 && picture.PixelHeight > 0)
                HeroHeightRatio = picture.PixelHeight / (double)picture.PixelWidth;
            HeroImage = picture;
        }

        // 左上角那格封面：集页取剧集那一层的 Primary（「集页面的封面改用剧页面的封面」—— 剧页面上那张 2:3
        // 海报），其余页面取自己的。借不到才退回本集自己的那几张 —— 有一张剧照总比空着一格强。
        async Task<BitmapImage?> StillAsync(EmbyItem page, Task<EmbyItem?> show, CancellationToken token)
        {
            if (page.Type != EmbyItemType.Episode)
            {
                return await DecodeFirstAsync(
                    page, [EmbyImageStore.Primary, EmbyImageStore.Thumb], StillDecodeWidth, token)
                    .ConfigureAwait(true);
            }

            var series = await show.ConfigureAwait(true);

            if (series is not null)
            {
                var poster = await DecodeFirstAsync(
                    series, [EmbyImageStore.Primary, EmbyImageStore.Thumb], StillDecodeWidth, token)
                    .ConfigureAwait(true);

                if (poster is not null) return poster;
            }

            return await DecodeFirstAsync(
                page,
                [EmbyImageStore.Primary, EmbyImageStore.Thumb, EmbyImageStore.Backdrop],
                StillDecodeWidth, token).ConfigureAwait(true);
        }

        // 右上角那张艺术图（「给集页面右上角添加艺术图」）：哪一张归这儿由 <see cref="ItemArtwork.Corner"/> 定 ——
        // 这一集自己的，没有就借剧集那一层的（show 在 WhenAll 外头就已经在路上了，这里等它回来再问一次标签）。
        // 电影和剧预取自身艺术图，使同页切到简洁模式时立即显示；集页保留借父剧的路径。
        async Task<BitmapImage?> CornerAsync(EmbyItem page, Task<EmbyItem?> show, CancellationToken token)
        {
            if (page.Type is not (EmbyItemType.Episode or EmbyItemType.Movie or EmbyItemType.Series)) return null;

            var pick = ItemArtwork.Corner(page,
                page.Type == EmbyItemType.Episode ? await show.ConfigureAwait(true) : null);

            return await DecodeFirstAsync(Refs(pick), CornerDecodeWidth, token).ConfigureAwait(true);
        }

        // 页尾那张横幅：一集自己几乎不会有横幅图（服务器把它挂在剧集那一层，同徽标），借哪一张等 show 回来
        // 一并问 <see cref="ItemArtwork.Footer"/>。它误不了任何一件正事 —— 那张图在整页最底下、滑到底才看得见，
        // 问失败就是那儿空着（ShowAsync 自己把错吞了）。
        async Task<BitmapImage?> FooterAsync(EmbyItem page, Task<EmbyItem?> show, CancellationToken token)
        {
            var pick = ItemArtwork.Footer(page, await show.ConfigureAwait(true));

            // 记下这一次取的是哪一张，给自检读（见 FooterPick）。图解不出来也照记：那时候「规矩说该有一张、屏上
            // 没画」是一句准确的读数，而记成 null 会把它说成「本来就没有」。
            if (Fresh(art, page)) FooterPick = pick;

            return await DecodeFirstAsync(Refs(pick), FooterDecodeWidth, token).ConfigureAwait(true);
        }

        // 一张图或者一张都没有，都写成那个重载要的那一串。null 就是「服务器上没有这一张」，走的是同一条空路 ——
        // 空串取不到东西，那个位置就一直空着。从前还有一档「这一页不摆这一张」，那是名牌按页面的种类分角的年代。
        static IReadOnlyList<ArtworkRef> Refs(ArtworkRef? one) => one is { } single ? [single] : [];
    }

    /// <summary>
    /// 封面到了：连它该画多大一起换上 —— 「海报下方会被裁切，要能看到完整的海报」。
    /// <para>
    /// 那一格原来是写死的 210×300、图按 <c>UniformToFill</c> 铺满它，而服务器上的海报是 2:3，于是上下各裁掉七八
    /// 个像素。现在那一格按这张图自己的形状收窄（<see cref="DetailHero.StillBox"/>），一个像素都不裁。
    /// 集页上这一张是剧的那张海报（「集页面的封面改用剧页面的封面」），取图那头已经换过源头了（见
    /// <c>StillAsync</c>），这里四种页面同一套：按图装进自己那一格定值的上限盒（<see cref="FitStill"/>）。
    /// </para>
    /// <para>
    /// 换尺寸和换图是同一拍里的两件事，顺序也就要紧：先尺寸再图。反过来的那一版会先按上一档尺寸画一帧、再收窄，
    /// 屏上是海报出现之后抖一下。而在这之前这一格整个是收着的（<see cref="StillVisibility"/> 问的就是有没有图），
    /// 所以这一拍之前那个尺寸谁也没看见。
    /// </para>
    /// </summary>
    private void ShowStill(BitmapImage picture)
    {
        _stillPixels = (picture.PixelWidth, picture.PixelHeight);
        RefitArtwork();
        StillImage = picture;
    }

    /// <summary>
    /// 右上角那张艺术图到了：连它该画多大一起换上 —— 「放大集页面右侧的艺术图」（2026-09-12）。
    /// <para>
    /// 和剧照那一张同一套写法（<see cref="ShowStill"/>），只是量出来的高喂的是带子
    /// （<see cref="CornerHeight"/> → <see cref="HeroRoom"/>）：这一格顶对齐摆在这一格里，带子矮过它就是这张画
    /// 压在底下的音轨那一行上。180 那一档不用管这件事 —— 它比那一叠字键矮，压不着。
    /// </para>
    /// <para>
    /// 位图的尺寸不成话（没解出来、或者报的是 0）时那个高留在 0：这一格照旧按 <c>Uniform</c> 缩在上限盒子里，
    /// 带子不因此长高，而不是拿一个量错的数去撑开版面。
    /// </para>
    /// </summary>
    private void ShowCorner(BitmapImage picture)
    {
        _cornerPixels = (picture.PixelWidth, picture.PixelHeight);
        FitCorner();
        CornerImage = picture;
    }

    /// <summary>
    /// Whether this artwork request is still the newest one and still describes the item on screen. Two
    /// questions rather than one: a season switch leaves the artwork alone on purpose, so the token
    /// alone would let a stale picture through, and a reload replaces the item object, so the item alone
    /// would let a cancelled one through.
    /// <para>
    /// 第二问比的是「要画的还不还是这批图」而不是「还不还是同一个对象」（<c>ItemArtwork.SamePictures</c>）：
    /// 详情页现在先用点进来那张卡片起这一趟，完整条目回来时 <see cref="_detail"/> 换成了另一个对象，而要取的是同
    /// 一批文件 —— 按对象比就等于把卡片那一份起的解码全部丢掉，也就白起了。换条目、换季、服务器上换过图，这几种
    /// id 或者标签会变，照旧判 false。
    /// </para>
    /// </summary>
    private bool Fresh(CancellationTokenSource art, EmbyItem item) =>
        ReferenceEquals(_art, art) && ItemArtwork.SamePictures(_detail, item);

    private async Task<BitmapImage?> DecodeFirstAsync(
        EmbyItem item, IReadOnlyList<string> types, int width, CancellationToken token)
    {
        if (!Attached) return null;

        return await ArtworkCandidates.FirstAsync<string, BitmapImage>(types, async (type, candidateToken) =>
        {
            var tag = EmbyImageStore.TagFor(item, type);
            if (tag is null || _images!.CaptureScope(item) is not { } scope) return null;
            return await _images.GetDecodedAsync(scope, EmbyImageStore.ImageItemId(item, type), type, tag,
                EmbyImageStore.RequestWidth(width), bytes => PosterLoader.DecodeAsync(bytes, width), candidateToken)
                .ConfigureAwait(true);
        }, token).ConfigureAwait(true);
    }

    /// <summary>
    /// 同上，只是这一串图各有各的主人：谁的哪一种、按哪个标签取，都由 <see cref="ItemArtwork"/> 事先定好
    /// （<see cref="ItemArtwork.Hero"/>）。集页和季页头上那张图是剧集那一层的，标签属于剧集那一头的 id，按这一页
    /// 自己的 id 去取只会取回一个空答案 —— 那不是错误，是一页安静地不显示背景图。
    /// <para>
    /// 挨着往下退这件事两个重载是一样的：某一张取不回来或者解不出来（服务器上那一版是坏的、格式不认），换下一张，
    /// 而不是让整页退回没有图的那一档版面。
    /// </para>
    /// </summary>
    private async Task<BitmapImage?> DecodeFirstAsync(
        IReadOnlyList<ArtworkRef> pictures, int width, CancellationToken token)
    {
        if (!Attached) return null;

        return await ArtworkCandidates.FirstAsync<ArtworkRef, BitmapImage>(pictures, async (picture, candidateToken) =>
        {
            return await _images!.GetDecodedAsync(_scope!, picture.ItemId, picture.ImageType, picture.Tag,
                EmbyImageStore.RequestWidth(width), bytes => PosterLoader.DecodeAsync(bytes, width), candidateToken)
                .ConfigureAwait(true);
        }, token).ConfigureAwait(true);
    }

    // ---------------------------------------------------------------- 选择

    /// <summary>
    /// 季锚点换了：只换标题和集带要对的季，不重新问服务器 —— 剧页、集页手上的集本来就是全季那一条，
    /// 换季不该把别的季从列表里拿走。真正的滚动由页面在 <see cref="SelectEpisodeSeason"/> 返回索引后做
    /// （索引 0 也要滚到开头，而 <c>ShelfStrip.FocusIndex</c> 的 0 表示「别动」）。
    /// </summary>
    partial void OnSelectedSeasonChanged(EmbyItem? value)
    {
        if (_syncingSeason || value is null) return;
        if (_detail?.Type is not (EmbyItemType.Series or EmbyItemType.Episode)) return;
        if (_episodes.Count == 0) return;
        if (ReferenceEquals(value, _loadedSeason)) return;

        _loadedSeason = value;
        UpdateEpisodeHeading(value);
        OnPropertyChanged(nameof(CanPickEpisodeSeason));
    }

    /// <summary>
    /// 季列表里点了某一季：把锚点换过去，返回那一季第一张卡在全季列表里的位置（找不到时 -1）。
    /// 页面拿这个数把集带对到左边沿；返回 -1 就保持原位。
    /// </summary>
    internal int SelectEpisodeSeason(EmbyItem season)
    {
        if (!CanPickEpisodeSeason) return -1;

        SelectedSeason = season;
        return ItemDetail.FirstEpisodeIndex(_episodes, season);
    }

    partial void OnSelectedSourceChanged(SourceRow? value)
    {
        if (_versions.FirstOrDefault(version => ReferenceEquals(version.Source, value?.Source)) is { } version)
            PlayTarget = version.Item;
        ShowSource(value?.Source);
    }

    private void OnEpisodeCardsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(EpisodeVisibility));

        // Both shapes, because both are 「is there anything」 as well as 「is this shape mine」: the block
        // going from empty to filled has to reveal one of the two, and it is the count that just changed.
        OnPropertyChanged(nameof(EpisodeListVisibility));
        OnPropertyChanged(nameof(EpisodeStripVisibility));
        OnPropertyChanged(nameof(CanPickEpisodeSeason));
    }

    private void OnCastCardsChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        OnPropertyChanged(nameof(CastVisibility));

    private void OnSeasonShelfChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(SeasonShelfVisibility));
        OnPropertyChanged(nameof(CanPickEpisodeSeason));
    }

    private void OnSimilarCardsChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        OnPropertyChanged(nameof(SimilarVisibility));

    /// <summary>
    /// 三个下拉里有一个换了选择 —— 那一个属性的通知，加上「这一行画不画」。两份都要喊：尾部那一份和片名那一栏
    /// 那一份是同一句判据的两半（见 <see cref="PickersVisibility"/>／<see cref="ColumnPickersVisibility"/>）。
    /// </summary>
    private void PickerChanged(string which)
    {
        OnPropertyChanged(which);
        OnPropertyChanged(nameof(PickersVisibility));
        OnPropertyChanged(nameof(ColumnPickersVisibility));

        // 「视频：…」那一句跟媒体源的下拉互斥（见 VideoShown）：源多起来它让位给下拉，两边都得重问一遍。
        OnPropertyChanged(nameof(VideoVisibility));
        OnPropertyChanged(nameof(VideoLineVisibility));
        OnPropertyChanged(nameof(ColumnVideoVisibility));
    }

    // ---------------------------------------------------------------- 命令

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task PlayAsync() => StartAsync(fromStart: false);

    /// <summary>从头开始. A second button rather than a switch, because 播放 already states the time.</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task RestartAsync() => StartAsync(fromStart: true);

    private async Task StartAsync(bool fromStart)
    {
        if (!CanPlay || _actions is null || PlayTarget is not { } target) return;
        if (SelectedSource is { } selected && !Sources.Contains(selected)) return;

        var version = _versions.FirstOrDefault(version => ReferenceEquals(version.Source, SelectedSource?.Source));
        target = version?.Item ?? target;
        var sameSource = target.Source?.Key == _scope?.Key;

        var parent = version?.Parent ?? (sameSource && target.Type == EmbyItemType.Episode
            && _detail is { Type: EmbyItemType.Series or EmbyItemType.Season }
            ? _detail
            : null);
        var episodes = sameSource ? _episodes : null;

        // No source at all is still playable: the player asks the server for one itself, and refusing
        // here would turn a server that reports its files late into a button that does nothing.
        if ((SelectedSource?.Source ?? target.DefaultMediaSource) is not { } source)
        {
            var intent = new PlaybackChoice(null, null, null, SelectedSubtitle?.Disable == true,
                fromStart || !Settings.Playback.ResumeFromSavedPosition ? 0 : target.ResumeTicks);
            await _actions.PlayAsync(target, parent, intent, episodes).ConfigureAwait(true);
            return;
        }

        var start = fromStart || !Settings.Playback.ResumeFromSavedPosition ? 0 : target.ResumeTicks;
        var choice = new PlaybackChoice(source, SelectedAudio?.Stream?.Index, SelectedSubtitle?.Stream?.Index,
            SelectedSubtitle?.Disable == true, start);

        await _actions.PlayAsync(target, parent, choice, episodes).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ToggleWatchedAsync()
    {
        if (!CanAct || _detail is not { } item) return;

        var played = item.IsWatched;
        var lifetime = _lifetime;
        var scope = _scope!;
        var card = _subjectStateCard!;
        var order = card.UserStateOrder;
        var change = order.BeginWatched(!played);

        try
        {
            var response = await scope.ExecuteAsync((client, token) => played
                    ? client.MarkUnplayedAsync(item.Id, token)
                    : client.MarkPlayedAsync(item.Id, token), CancellationToken.None)
                .ConfigureAwait(true);

            if (!scope.IsAvailable) return;
            if (order.Apply(change, card.Item.UserData, response) is not { } confirmed) return;
            card.ApplyUserState(confirmed, watched: true);

            // 标记为已看（played 为 false 的那一次）还要替服务器补报一趟播放停止，位置按用户令报 100%，好让
            // 服务器上那条通知立刻把 playback.stop 转发出去（用户令 2026-10-09，见 IShellActions.ReportMarkedWatched）。
            // 反过来「标记未观看」不是「看完了」，不报。
            if (!played) _actions?.ReportMarkedWatched(item);

            // Re-read rather than flipped locally: marking a season watched moves every episode's tick
            // and the show's unplayed count with it, and only the server knows what it did.
            if (Attached && lifetime == _lifetime && ReferenceEquals(item, _detail) && scope.IsAvailable)
                await LoadAsync(scope.Connection.IsFnNas || scope.Connection.IsPlex
                    ? "观看状态已保存，但刷新详情失败" : null).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Attached && lifetime == _lifetime)
                _actions?.Notify($"更新观看状态失败：{Failure.Describe(error)}", InfoBarSeverity.Error);
        }
        finally { order.Complete(change); }
    }

    [RelayCommand(CanExecute = nameof(CanFavorite))]
    private async Task ToggleFavoriteAsync()
    {
        // 旧命令可绕过 CanExecute；详情尚未换完时，也不能拿旧条目 ID 借新来源写入。
        if (!CanFavorite || _detail is not { } item) return;

        var favorite = item.UserData?.IsFavorite == true;
        var lifetime = _lifetime;
        var scope = _scope!;
        var card = _subjectStateCard!;
        var order = card.UserStateOrder;
        var change = order.BeginFavorite(!favorite);

        try
        {
            var response = await scope.ExecuteAsync(
                    (client, token) => client.SetFavoriteAsync(item.Id, !favorite, token), CancellationToken.None)
                .ConfigureAwait(true);

            // One flag, flipped where it is kept. 观看状态 above re-reads the item because the server moves
            // other things with it; 收藏 moves nothing — and a reload here cost the page its artwork, its
            // 演职人员 row and its 更多类似 row, all re-fetched to answer a question the click already
            // answered. The item's own copy is updated too, so the next click reads the current value.
            if (!scope.IsAvailable) return;
            if (order.Apply(change, card.Item.UserData, response) is not { } confirmed) return;
            card.ApplyUserState(confirmed, watched: false);
            _actions?.RefreshFavorites(item);
            if (!Attached || lifetime != _lifetime || !ReferenceEquals(item, _detail)) return;
            Favorite = confirmed.IsFavorite;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Attached && lifetime == _lifetime)
                _actions?.Notify($"更新收藏失败：{Failure.Describe(error)}", InfoBarSeverity.Error);
        }
        finally { order.Complete(change); }
    }

    [RelayCommand]
    private void ToggleOverview() => Expanded = !Expanded;

    /// <summary>
    /// The headline on an episode page — 「99.9 刑事专业律师」 above 「S2:E7 - …」 — opens 那部剧自己的页面.
    /// Where that goes is <see cref="ItemDetail.TitleTarget"/>'s decision; this only hands it to the
    /// shell, the same way a card click does. 季页面不从这儿进（见那边的注），它的入口是剧页面上
    /// 「全部剧季」那一格。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenTitle))]
    private void OpenTitle()
    {
        if (TitleLink is { } link) _actions?.OpenItem(link);
    }

    // ---------------------------------------------------------------- 导航

    /// <summary>
    /// An episode card opens that episode's own page; a face opens what that person is in. Both go
    /// through the shell, which is the one place the 「detail page, person, or another grid」 choice is
    /// made — this page used to make the person half of that choice again for itself, and now that the
    /// shell knows a person is not a folder there is no reason for two copies of the answer.
    /// </summary>
    internal void OpenCard(CardItem card) => _actions?.OpenItem(card.Item);

    /// <summary>
    /// Opens the first card in 单集, the same way a click on it would. For the startup self-check, whose
    /// walk arrives here off a library row — a show, on any TV library — while 媒体信息 only exists one page
    /// further in, on the episode's own. Null when this page has no episode strip to click.
    /// </summary>
    internal EmbyItem? OpenFirstEpisode()
    {
        if (!Attached || EpisodeShelf is not { } shelf) return null;
        var index = _episodes.FindIndex(item => item.Type == EmbyItemType.Episode);
        if (index < 0 || index >= shelf.Cards.Count) return null;
        var card = shelf.Cards[index];

        OpenCard(card);
        return card.Item;
    }

    /// <summary>
    /// A full-detail More menu owns a temporary card. Keep only that episode's shelf card alive so its
    /// confirmed state group still updates the deferred DTO after the menu and its card are released.
    /// </summary>
    internal void EnsureCurrentEpisodeCard()
    {
        if (!Attached || _detail?.Type != EmbyItemType.Episode || EpisodeShelf is not { } shelf) return;
        var identity = _detail.IdentityKey;
        var index = _episodes.FindIndex(item => item.IdentityKey == identity);
        if (index >= 0 && index < shelf.Cards.Count) _ = shelf.Cards[index];
    }

    /// <summary>What a context menu calls after it has changed something on the server.</summary>
    internal void Reload()
    {
        // The version command removes only a confirmed deleted file from its captured DTO. Refresh
        // local pickers immediately; a failed GET must not leave that vanished version executable.
        if (Attached && _scope!.Connection.IsFnNas && PlayTarget is { } target
            && _versions.Any(version => ReferenceEquals(version.Item, target) && !target.MediaSources.Contains(version.Source)))
            ShowTarget(target);
        _ = LoadAsync();
    }

    /// <summary>Capture a successful delete callback for this visit, not whichever detail is open later.</summary>
    internal Action<CardItem> CaptureRemoval()
    {
        var lifetime = _lifetime;
        var source = _scope?.Key;
        var itemId = _seed?.Id;
        return removed =>
        {
            if (!Attached || lifetime != _lifetime || source != _scope?.Key
                || removed.Scope?.Key != source || !removed.IsCurrent) return;
            if (removed.Item.Id != itemId) { Remove(removed); return; }

            // A confirmed subject deletion is not an ordinary failed refresh. End all target/version
            // owners before clearing their data, so a late read cannot restore executable old media.
            _deletedItems.Add(removed.Item.Id);
            ClearDeletedSubject();
        };
    }

    private void ClearDeletedSubject()
    {
        Cancel();
        _detail = _seed = null;
        PlayTarget = null;
        ShowTarget(null);
        TitleLink = null;
        _episodes.Clear();
        Seasons.Clear();
        SelectedSeason = _loadedSeason = null;
        EpisodeShelf?.Clear();
        SeasonShelf?.Clear();
        CastShelf?.Clear();
        SimilarShelf?.Clear();
        AnnounceShape();
        IsReady = true;
        Notify("条目已删除", "此条目已从服务器移除，请返回媒体库查看其它内容。", InfoBarSeverity.Informational);
    }

    internal Action CaptureReload()
        => CaptureReloadFor(_detail);

    internal Action CaptureCardReload(EmbyItem item) => CaptureReloadFor(item);

    private Action CaptureReloadFor(EmbyItem? item)
    {
        var lifetime = _lifetime;
        var source = _scope?.Key;
        var beforeSources = item?.MediaSources.Select(media => media.Id).ToArray() ?? [];
        return () =>
        {
            if (!Attached || lifetime != _lifetime || source != _scope?.Key || item is null
                || item.Source?.Key != source) return;
            if (_detail?.Id != item.Id) { Reload(); return; }
            // A normal same-ID read may have replaced the DTO while the delete dialog was open.
            // Compare the captured source list to the confirmed command's edited DTO and apply only
            // removed file IDs to this still-owned subject, including late version/detail reads.
            if (_scope!.Connection.IsFnNas)
            {
                foreach (var id in beforeSources.Except(item.MediaSources.Select(media => media.Id), StringComparer.Ordinal))
                    if (id.Length > 0) _deletedSourceIds.Add(id);
                _detail.MediaSources.RemoveAll(media => _deletedSourceIds.Contains(media.Id));
                // Menu entries belong to all discovered sources, not just the selected play target.
                // Remove only this subject's confirmed file IDs and preserve a selected valid copy.
                _versionLoad?.Cancel();
                _versionGeneration++;
                var discarded = _versions.Where(version => version.Item.Id == item.Id && version.Item.Source?.Key == source
                    && _deletedSourceIds.Contains(version.Source.Id)).ToArray();
                _versions = _versions.Except(discarded).ToArray();
                foreach (var row in Sources.Where(row => discarded.Any(version => ReferenceEquals(version.Source, row.Source))).ToArray())
                    Sources.Remove(row);
                if (SelectedSource is { } selected && !Sources.Contains(selected))
                    SelectedSource = Sources.FirstOrDefault();
                if (PlayTarget is { } target && target.Id == item.Id && target.Source?.Key == source)
                {
                    target.MediaSources.RemoveAll(media => _deletedSourceIds.Contains(media.Id));
                    if (_deletedSourceIds.Count > 0 && target.MediaSources.Count == 0) PlayTarget = null;
                }
                ShowSource(SelectedSource?.Source);
            }
            Reload();
        };
    }

    /// <summary>封面删除只移除受影响的卡片；播放目标若被删除，再读取剩余单集的目标。</summary>
    internal void Remove(CardItem removed) => _ = RemoveAsync(removed);

    private bool WasDeleted(EmbyItem item) => _removed?.Hides(item) == true || _deletedItems.Contains(item.Id)
        || item.ParentId is { } parent && _deletedItems.Contains(parent)
        || item.SeriesId is { } series && _deletedItems.Contains(series)
        || item.SeasonId is { } season && _deletedItems.Contains(season);

    private bool SourceWasDeleted(MediaSource source) => _deletedSourceIds.Contains(source.Id)
        || source.Path is { Length: > 0 } path && _deletedSourcePaths.Any(deleted => MoviePilotTransfer.SamePath(deleted, path));

    /// <summary>删除一个库文件版本时保留主体，只撤已确认路径及其旧下拉入口，晚到响应也不能恢复它。</summary>
    internal void RemoveLibraryFiles(EmbyItem item, IReadOnlyCollection<string> removedPaths)
    {
        if (!Attached || removedPaths.Count == 0 || item.Source?.Key != _scope?.Key
            || _detail is not { } detail || detail.Id != item.Id || detail.Source?.Key != _scope?.Key) return;
        foreach (var path in removedPaths.Where(path => !string.IsNullOrWhiteSpace(path))) _deletedSourcePaths.Add(path);
        if (_deletedSourcePaths.Count == 0) return;
        _versionLoad?.Cancel();
        _versionGeneration++;
        detail.MediaSources.RemoveAll(SourceWasDeleted);
        var discarded = _versions.Where(version => version.Item.Id == detail.Id && version.Item.Source?.Key == _scope?.Key
            && SourceWasDeleted(version.Source)).ToArray();
        _versions = _versions.Except(discarded).ToArray();
        foreach (var row in Sources.Where(row => discarded.Any(version => ReferenceEquals(version.Source, row.Source))).ToArray())
            Sources.Remove(row);
        if (PlayTarget is { } target && target.Id == detail.Id && target.Source?.Key == _scope?.Key)
        {
            target.MediaSources.RemoveAll(SourceWasDeleted);
            if (target.MediaSources.Count == 0) PlayTarget = null;
        }
        if (SelectedSource is { } selected && !Sources.Contains(selected)) SelectedSource = Sources.FirstOrDefault();
        ShowSource(SelectedSource?.Source);
        NotifyActionAvailability();
    }

    internal async Task RemoveAsync(CardItem removed)
    {
        if (!Attached || !removed.IsCurrent) return;
        if (_detail is { } subject && removed.Scope?.Key == _scope?.Key && RemovedMedia.Covers(removed.Item, subject))
        {
            _deletedItems.Add(removed.Item.Id);
            ClearDeletedSubject();
            return;
        }
        CardShelf?[] shelves = [EpisodeShelf, SeasonShelf, SimilarShelf];
        if (!shelves.Any(shelf => shelf?.Cards.Contains(removed) == true)) return;
        _deletedItems.Add(removed.Item.Id);
        // 推荐卡不参与单集或播放目标时，只移除推荐；不能撤销仍在读取的剧季或补选目标。
        if (!_episodes.Any(WasDeleted) && !Seasons.Any(WasDeleted) && (PlayTarget is null || !WasDeleted(PlayTarget)))
        {
            if (SimilarShelf is { } similar)
                similar.Cards.RemoveWhere(WasDeleted);
            return;
        }
        bool Deleted(EmbyItem item) => WasDeleted(item);
        var token = BeginLoad();
        try
        {
            if (!IsCurrent(token)) return;
            // Collection notifications can move the strip's leading index immediately. Publish the
            // matching business index first so that callback cannot read the deleted season's DTO.
            _episodes.RemoveAll(Deleted);
            foreach (var shelf in shelves.OfType<CardShelf>())
            {
                shelf.Cards.RemoveWhere(Deleted);
                if (!IsCurrent(token)) return;
            }
            _syncingSeason = true;
            try
            {
                foreach (var season in Seasons.Where(Deleted).ToArray()) Seasons.Remove(season);
                if (SelectedSeason is { } selected && Deleted(selected)) SelectedSeason = ItemDetail.PickSeason(Seasons);
            }
            finally { _syncingSeason = false; }

            if (_loadedSeason is { } loaded && Deleted(loaded) || SelectedSeason is { } picked && !ReferenceEquals(picked, _loadedSeason))
            {
                _loadedSeason = null;
                PlayTarget = null;
                ShowTarget(null);
                if (SelectedSeason is { } next) await LoadEpisodesAsync(next, retarget: true, token).ConfigureAwait(true);
            }
            else
            {
                if (PlayTarget is { } target && Deleted(target))
                {
                    PlayTarget = null;
                    ShowTarget(null);
                }
                if (_detail?.Type is EmbyItemType.Series or EmbyItemType.Season
                    && PlayTarget is null
                    && ItemDetail.PickEpisode(
                        _episodes, _detail.Type == EmbyItemType.Series ? null : _loadedSeason) is { } next)
                    await ApplyPlayTargetAsync(next, token).ConfigureAwait(true);
            }
            if (IsCurrent(token) && _detail is { } detail && EpisodeShelf is { } episodes)
                episodes.Title = ItemDetail.EpisodeHeading(ItemDetail.EpisodeScope(detail, _loadedSeason).SeasonName);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsCurrent(token)) _actions?.Notify($"更新删除后的单集失败：{Failure.Describe(error)}", InfoBarSeverity.Error);
        }
        finally { EndLoad(token); }
    }

    /// <inheritdoc />
    public override void Cancel()
    {
        if (_subjectStateCard is { } card) card.PropertyChanged -= OnSubjectStateChanged;
        _subjectStateCard = null;
        _active = false;
        _actionsReady = false;
        _versionLoad?.Cancel();
        _lifetime++;
        base.Cancel();
        _art?.Cancel();
        // 还在路上的那趟重糊也别让它回来把图换掉 —— 页面都走了。
        _blurCancel?.Cancel();
        // Frame can still measure the departing page. Clearing this deferred source prevents a late
        // GetAt from creating a new current-account card and restarting its image request after leave.
        EpisodeShelf?.Clear();
        BusyChanged();
        AnnounceAffordances();
        _heroTiming = null;
        _heroCacheTicket = null;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Cancel();
        base.Dispose();

        _art?.Cancel();
        _art?.Dispose();
        _art = null;
        _blurCancel?.Cancel();
        _blurCancel?.Dispose();
        _blurCancel = null;
        _heroTiming = null;
        _heroCacheTicket = null;
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// The home page: 继续观看, 媒体库, 接下来看, 加上每个媒体库自己那一排最近添加 —— each from its own
/// source. 排哪几排、什么次序、哪几排显示，由设置里那份版面说（<see cref="HomeLayout"/>，「新增页里拖拽决定这些
/// 列表的顺序，勾选显示或者不勾选取消显示」）。整个服务器的「最近添加」那一排 2026-09-12 去掉了：
/// 「去掉最近添加，保留最近添加 电视节目、最近添加 电影」。
/// <para>
/// The server rows go out together and are reported on separately. They fail independently on a
/// real server — 接下来看 is empty for an account that only watches films, and some endpoints are
/// administrator-only — so one of them returning nothing must not cost the page the others.
/// </para>
/// <para>
/// 媒体库 costs no request at all: the shell already read the account's view list once, and this row is
/// that same list drawn as cards. It is the one row that is always there, even for an account that has
/// never played anything — and since the top tab strip was removed (2026-09-08), clicking one of these
/// cards is how a library is opened, so it earns its place on the page.
/// </para>
/// <para>
/// 勾掉的那一排连请求一起省掉：媒体库那几排各是一次「这个库的最近添加」，不看的库不该每次开主页都问一遍。
/// </para>
/// <para>
/// 设置 → 界面 → 「显示主页轮播大图」关掉之后这一页就是一叠普通的货架（<see cref="_banner"/>）：顶上那张大图
/// 没有了，所有内容都横着排、按各自在版面表上的位置站着。
/// </para>
/// </summary>
public sealed partial class HomeViewModel : PageViewModel
{
    private const string Category = "主页";

    /// <summary>How many items each of the two 「what you were doing」 rows asks for.</summary>
    private const int ShelfSize = 12;

    /// <summary>最近添加 gets more, because posters are narrower than stills and the row looks thin.</summary>
    private const int LatestSize = 24;

    /// <summary>
    /// <see cref="LoadedCount"/> before the first load. The self-check already speaks this language —
    /// it reports a negative count as 未读取 — and it keeps 「nothing came back」 distinct from
    /// 「nothing has been asked yet」, which is the difference between an empty server and a slow one.
    /// </summary>
    private const int NotLoaded = -1;

    private EmbySession? _session;
    private IShellActions? _actions;
    private EmbyImageStore? _images;
    private ISettingsService? _settings;
    private IReadOnlyList<EmbyItem> _libraryViews = [];
    private readonly HomeRefresh _refresh = new();

    /// <summary>
    /// 「服务器上有没有新入库的东西」的账（<see cref="LibraryWatch"/>）：整页重读时记下每个来源此刻最新的一批，
    /// 定时那一趟（<see cref="AutoRefreshAsync"/>）拿新的一批和它比。变了才重读 —— 没变也重读，等于每隔半分钟
    /// 把一页海报连轮播全拆了重装。
    /// </summary>
    private readonly LibraryWatch _watch = new();

    /// <summary>定时那一趟还在路上。防止 30 秒的钟比一趟往返还快时叠上第二趟。</summary>
    private bool _watchBusy;

    /// <summary>
    /// 服务器推了「有库变更」但这一趟还没去核对。<see cref="EmbyRealtimeWatch"/> 收到 <c>LibraryChanged</c> 时立起来，
    /// 由 <see cref="PumpRealtimeAsync"/> 消费。
    /// <para>
    /// 为什么要中间这一格而不是收到就直接重读：一条库变更常常连着推好几条（Emby 扫一遍会为一集、一季、一部各发一条），
    /// 直接重读就是几秒钟里把整页海报拆装好几遍。立脏位、再在一拍之后统一核对一次，是让那几条并成一次重读。
    /// </para>
    /// </summary>
    private bool _realtimeDirty;

    /// <summary>
    /// 实时通道报上来的来源钥匙（<see cref="EmbySessionScope.Key"/>）。只是日志里要说清是哪个来源变了；
    /// 重读走的还是整页那一趟（<see cref="LoadAsync"/>），不按来源分开。
    /// </summary>
    private readonly HashSet<string> _realtimeSources = new(StringComparer.Ordinal);

    /// <summary>这一拍的通知已经排上了。连着收到几条不该排好几个一拍。</summary>
    private bool _realtimeScheduled;

    /// <summary>
    /// 实时通道。<c>HomePage</c> 导航进来时开、离开时收（<see cref="Attach"/> / <see cref="Cancel"/>）。
    /// <para>
    /// 这是**快路径**：收到 <c>LibraryChanged</c> 就提前走一遍核对，把 30 秒那一趟的空窗压到约一秒。
    /// 30 秒那一趟留着不动，所以这条通道连不上、或者服务器不推的时候，行为等于没有它。
    /// </para>
    /// </summary>
    private readonly EmbyRealtimeWatch _realtime = new();

    /// <summary>这一场读取故障已经记过日志了。定时检查每半分钟一趟，不设这一格就是每半分钟一条同样的警告。</summary>
    private bool _watchReported;

    private EmbySessionScope? _scope;
    private IReadOnlyList<EmbySessionScope> _sources = [];
    private IUiDispatcher? _dispatcher;
    private bool _active;
    private bool Live => _active && _sources.Any(source => source.IsAvailable);
    private bool Visible(string key) => _all.Any(entry => entry.Row.Key == key && entry.Row.Visible);

    /// <summary>
    /// 两个及以上在线账号时，主页那三处要写服务器名：继续观看卡季集下面的第三行、每个库「最近添加」的货架
    /// 标题、媒体库那一排的卡片名称。只有一个账号时一律不写 —— 多出来的服务器名不区分任何东西。
    /// </summary>
    private bool ShowServerNames => _sources.Count > 1;

    /// <summary>
    /// 这一页上有没有东西会因为「服务器新入库」而换掉（<see cref="HomeLayout.CanRefresh"/>）：轮播开着，或者
    /// 至少有一排读服务器条目清单的货架勾着。都不是的时候定时检查和实时通道都不该再问服务器 —— 隐藏的行不联网。
    /// </summary>
    private bool ShowsAnything => HomeLayout.CanRefresh(_all.Select(entry => entry.Row), _banner);

    /// <summary>
    /// 设置 → 界面 → 「显示主页轮播大图」（<c>UiSettings.ShowHomeBanner</c>）。关掉的时候顶上那张大图没有：
    /// 一张幻灯片都不造（<see cref="Slides"/> 空着，带子自己就收起来了），页面就是一叠横着排的货架。
    /// <para>
    /// 每次 <see cref="BuildShelves"/> 重读，而不是 <see cref="Attach"/> 时取一次快照 —— 拖拽次序和勾选是
    /// 改完当场生效的（<c>ShellPrefs</c>），这一项和它们同一张表上的东西，「改完要重开这一页才算」读起来就是坏的。
    /// 卡片尺寸那三个数不同：它们决定卡片按多宽解码，见 <see cref="_all"/>。
    /// </para>
    /// </summary>
    private bool _banner = true;

    /// <summary>
    /// 这一次要排哪几排、什么次序、哪几排显示（<see cref="HomeLayout.Plan"/>），和它们各自那一排。
    /// 每次 <see cref="Attach"/> 和每次版面改过（<see cref="ApplyLayoutAsync"/>）都重建：拖出来的新次序要在这里落地。
    /// </summary>
    private (HomeRowPlan Row, CardShelf Shelf)[] _all = [];

    /// <summary>
    /// 矮窗档开没开：开了就是把 <see cref="LibraryShelf"/> 从 <see cref="Shelves"/> 里摘掉、让它压到轮播左下角，
    /// 摘掉之后横排里它那个位置由下面那一排补上。判定（量窗口、量货架）在 <c>HomePage</c>，这一头只管执行和记状态
    /// —— 重载重建 <see cref="Shelves"/> 之后由 <see cref="ApplyLibraryOverlay"/> 原样恢复，重载前后不跳档。
    /// </summary>
    private bool _libraryOnBanner;

    /// <summary>矮窗档现在开没开。读数用（自检的版面比对、货号摘要要说清媒体库去了哪儿）。</summary>
    internal bool LibraryOnBanner => _libraryOnBanner;

    /// <summary>
    /// 刚刚那次 <see cref="Shelves"/> 的增删是矮窗档自己搬的（<see cref="ApplyLibraryOverlay"/> 里的搬进搬出），
    /// 不是重新装货。页面据此**不排重查**。
    /// <para>
    /// 少了这一格就是自己喂自己：翻档 → 集合变化 → 页面排一次重查 → 重查又翻档。2026-09-18 两场、09-21 一场
    /// 「矮窗档来回翻」都是这个环在滚，最后那场刷了三万行日志、把界面线程吃干二十七秒（见 <c>HomePage</c> 的
    /// <c>Shelves.CollectionChanged</c>）。
    /// </para>
    /// <para>
    /// 重建货架（<see cref="BuildShelves"/> 里那次 <c>Clear</c> 与逐个 <c>Add</c>）不落在这个记号里 —— 那才是
    /// 页面该重查的「货架集合真变了」。
    /// </para>
    /// </summary>
    internal bool ShelvesChangeIsOverlay { get; private set; }

    /// <summary>
    /// 媒体库那一排（版面钥匙 <see cref="HomeLayout.Libraries"/>），装到了东西才记 —— 勾掉或者空的账号没有这一排，
    /// 矮窗档整个不参与。它和 <see cref="LibraryFlowIndex"/> 都是每次 <see cref="LoadAsync"/> 重建横排时落定的。
    /// </summary>
    internal CardShelf? LibraryShelf { get; private set; }

    /// <summary>
    /// <see cref="LibraryShelf"/> 在横排里的位置（默认摆法，不是压上档的）。压上档要回默认时按它插回去，插错了
    /// 次序就是「拖拽版面被窗口高度改写」。
    /// </summary>
    internal int LibraryFlowIndex { get; private set; } = -1;

    /// <summary>
    /// 设置 → 主页 → 「主页媒体库位置」（<c>UiSettings.HomeLibraryPlacement</c>）：媒体库那一排该压在轮播里、
    /// 还是待在轮播下面。
    /// <para>
    /// **每次问设置要，不存快照**：它和轮播那几项同一张表上的东西，改完当场生效（<c>ShellPrefs</c>），
    /// 存成快照就得等下次开这一页 —— 而那正是用户会当成「设置没生效」的样子。
    /// </para>
    /// <para>
    /// 这一头只交出用户选的那一档；「这一档窗口该压不该压」由 <see cref="HomeFold.LibraryOnBanner"/> 结合量出来的
    /// 几何定，屏上量那几样在 <c>HomePage.UpdateLibraryOverlay</c>。还没 <see cref="Attach"/> 时没有设置可问，
    /// 交回装机默认那一档。
    /// </para>
    /// </summary>
    internal Emby.HomeLibraryPlacement LibraryPlacement =>
        _settings?.Settings.Ui.HomeLibraryPlacement ?? Emby.HomeLibraryPlacement.Auto;

    /// <summary>
    /// 视图（量完几何之后）对矮窗档下的判断。开、关都走 <see cref="ApplyLibraryOverlay"/> 搬那一排；没有媒体库
    /// 那一排时开不了。
    /// </summary>
    internal void SetLibraryOverlay(bool on)
    {
        if (_libraryOnBanner == on) return;
        if (on && LibraryShelf is null) return;

        _libraryOnBanner = on;
        ApplyLibraryOverlay();
    }

    /// <summary>
    /// 按当前开关把媒体库那一排搬进搬出横排。重载（<see cref="LoadAsync"/> 重建 <see cref="Shelves"/>）之后也走
    /// 这一句恢复原状 —— 开着的时候重建出来的那一排立刻再摘掉，屏上不出现「先回横排再压回去」的一跳。
    /// </summary>
    private void ApplyLibraryOverlay()
    {
        if (LibraryShelf is not { } shelf)
        {
            _libraryOnBanner = false;
            return;
        }

        // 搬的时候挂记号：这一下集合变化是翻档自己造成的，页面据此不排重查（见 ShelvesChangeIsOverlay）。
        ShelvesChangeIsOverlay = true;
        try
        {
            if (_libraryOnBanner)
            {
                if (Shelves.Contains(shelf)) Shelves.Remove(shelf);
            }
            else if (!Shelves.Contains(shelf) && LibraryFlowIndex >= 0)
            {
                Shelves.Insert(Math.Min(LibraryFlowIndex, Shelves.Count), shelf);
            }
        }
        finally
        {
            ShelvesChangeIsOverlay = false;
        }
    }

    public HomeViewModel()
    {
        LoadedCount = NotLoaded;
        Slides = [];
    }

    /// <summary>
    /// 横着排的那几排，只留装到了东西的。Only the non-empty ones, rather than all four with the
    /// empty ones collapsed: an <c>ItemsRepeater</c> still spends its <c>StackLayout</c> spacing on a
    /// collapsed item, so an account with no 接下来看 would get a blank gap where the row would be.
    /// </summary>
    public ObservableCollection<CardShelf> Shelves { get; } = [];

    /// <summary>
    /// 需求 5 的那条大图轮播（<see cref="HomeCarousel"/>）：设置里那个来源（最近添加或随机）、那一类媒体
    /// （全部、电影或剧集）的头几张，each drawn as one full-width backdrop instead of a card.
    /// 2026-09-13「轮播图改用前十个最近添加」之后继续观看不再参加，同日下午来源、媒体、张数三样进了设置。
    /// 设置里关掉轮播（<see cref="_banner"/>）时这里是空的，带子自己就收起来。
    /// <para>
    /// A whole new list per load rather than a collection edited in place. The band swaps its slides as
    /// one thing — old artwork released, index back to the first, dots rebuilt — and an
    /// <c>ObservableCollection</c> cleared and refilled would put it through that eight times in a row,
    /// once per <c>Add</c>, restarting the fade each time.
    /// </para>
    /// <para>
    /// 从前这里还挂着一个 <c>HeroFilled</c>（「顶上那块是不是一张图」，页眉和标题栏墨色的联动靠它）—— 2026-09-10
    /// 「框成卡片」之后页眉和标题栏都不再压在图上，联动删了，它跟着删。
    /// </para>
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<BannerSlide> Slides { get; set; }

    /// <summary>Which server, and how much of it is on the page.</summary>
    [ObservableProperty]
    public partial string? Subheading { get; set; }

    /// <summary>
    /// How many cards the server's own rows produced — 媒体库 excluded, because it is this account's
    /// shelf list rather than anything on it. What the self-check counts to prove real data arrived.
    /// </summary>
    [ObservableProperty]
    public partial int LoadedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmptyNotice { get; set; }

    public Visibility EmptyVisibility => Show(ShowEmptyNotice);

    /// <summary>
    /// Handed the shell and the three capabilities this page draws from, once per navigation to it.
    /// <para>
    /// <paramref name="settings"/> is read here and not kept: the card sizes are a snapshot per
    /// navigation, on purpose (see <see cref="_all"/>). The session and the image store are kept — every
    /// row's request goes through the one, and the carousel builds a slide per load out of the other.
    /// </para>
    /// </summary>
    internal void Attach(
        IShellActions actions,
        IReadOnlyList<EmbyItem> libraryViews,
        ISettingsService settings,
        EmbySession session,
        EmbyImageStore images,
        IUiDispatcher dispatcher)
    {
        Cancel();
        _active = true;
        _scope = images.CaptureScope();
        _sources = session.Sources.ToArray();
        // 换了一趟会话，上一次那份「服务器长什么样」不再作数（连账号都可能是另一个了）。
        _watch.Clear();
        _watchReported = false;
        _realtimeDirty = false;
        _realtimeSources.Clear();
        if (_scope is not null) BrowsingPreferences.SelectIdentity(settings.Settings.Ui, _scope.Connection);
        _actions = actions;
        _libraryViews = libraryViews;
        _session = session;
        _images = images;
        _settings = settings;
        _dispatcher = dispatcher;

        // 实时通道跟着这一页走：开着的时候服务器一推 LibraryChanged 就提前核对一次新入库，
        // 不必等 30 秒那一趟。它只是快路径 —— 连不上或者服务器不推，30 秒那一趟照旧。
        _realtime.Attach(_sources, OnRealtimeLibraryChanged);

        BuildShelves();
    }

    /// <summary>
    /// 按存档里那份版面（<see cref="HomeLayout.Plan"/>）造出这一次的每一排。归一化之后的版面顺手写回设置文件 ——
    /// 服务器上新加的媒体库因此自己排到末尾、删掉的自己消失、改了名的把新名字带回去，而拖拽那张表读的就是这一份。
    /// </summary>
    private void BuildShelves()
    {
        if (_images is not { } images || !Live) return;

        _refresh.Invalidate();
        var ui = _settings?.Settings.Ui;
        var plan = HomeLayout.Plan(ui?.HomeRows, _libraryViews);

        _banner = ui?.ShowHomeBanner ?? true;

        if (ui is not null && !HomeLayout.Same(ui.HomeRows, plan))
        {
            ui.HomeRows = HomeLayout.Save(plan);
            _settings?.Save();
        }

        // 继续观看、媒体库、接下来看走 16:9 的宽卡（那三排讲的是「你在看的那一格画面」）；每个媒体库自己那一排
        // 走海报 —— 一整排新片的封面比一整排剧照读得快。媒体库那一排不上角标：一个库没有「已看」。
        // 媒体库现在也是横着排的普通一排（2026-09-08「移除轮播图右边的媒体库」之后，右边那一列没了），站在页面
        // 自己的纸上、走主题的墨（OnScrim 默认关）。点它的卡进库 —— 顶部标签栏删掉之后，这一排就是进各媒体库的入口。
        // 媒体库那一排比别的宽卡窄一档（2026-09-13「参考上图缩小媒体库图标的大小」，比例记在
        // CardSize.LibraryWidth 上）：一排「进哪座库」的入口格子，不需要剧照那么大。形状和宽度从一个 switch
        // 里出，两边分头写就会有一天各说各话。
        // 继续观看 / 接下来看 那一档 2026-09-14 从 CardSize.WideWidth(300) 收到 CardSize.HomeWideWidth(256)
        // （「缩小首页中的媒体库卡片和继续观看卡片，调整其尺寸使其更紧凑，同时保持布局对齐、间距协调以及各屏幕
        // 尺寸下的响应式显示效果」）—— 主页单独一档，不动 WideWidth，详情页的 更多单集 和媒体库网格那两处照旧。
        _all = [.. plan.Select(row =>
        {
            var (wide, width) = row.Key switch
            {
                HomeLayout.Libraries => (true, CardSize.LibraryWidth),
                HomeLayout.Resume or HomeLayout.NextUp => (true, CardSize.HomeWideWidth),
                _ => (false, CardSize.PosterWidth)
            };
            var badges = row.Key != HomeLayout.Libraries;

            // 多账号时这一排属于哪台服务器要写在标题前面；媒体库那几排各自带着自己的来源。固定排没有来源，
            // Prefix 原样交回标题。
            var source = _libraryViews.FirstOrDefault(library => HomeLayout.LibraryKey(library) == row.Key)?.Source;
            var title = SourceCaption.Prefix(row.Title, source, ShowServerNames);

            // 这一排点进哪儿由版面说了算（HomeLayout.TargetOf）：库那几排认库，继续观看 / 接下来看各有
            // 自己的一页，详情页造出来的那些排是 None —— 牌子右端跟不跟大于号就看它。2026-09-14 之前这里
            // 只递一个库 id，于是那两排没有 id、牌子一直是死的。
            return (row, new CardShelf(
                title, images, width, wide, badges,
                HomeLayout.LibraryId(row.Key), HomeLayout.TargetOf(row.Key),
                sourceLine: row.Key == HomeLayout.Resume && ShowServerNames)
            {
                HomeKey = row.Key,
                Source = source
            });
        })];
    }

    /// <summary>
    /// 版面改过了（拖拽排序、或者勾掉了一排）：重建每一排再读一遍。设置窗口那一头改完喊一声
    /// （<c>ShellPrefs</c>），主页这一头照这句话重排 ——「改完要重启才算」的设置读起来就是坏的。
    /// </summary>
    internal Task ApplyLayoutAsync()
    {
        BuildShelves();
        return LoadAsync();
    }

    private new bool IsCurrent(CancellationToken token) => Live && base.IsCurrent(token);

    public override Task ReloadAsync() => LoadAsync();

    internal void Remove(CardItem removed)
    {
        if (!Live || !removed.IsCurrent || !_all.Any(entry => entry.Shelf.Cards.Contains(removed))) return;
        base.Cancel();
        bool Deleted(EmbyItem item) => item.Source?.Key == removed.Item.Source?.Key && (item.Id == removed.Item.Id || item.ParentId == removed.Item.Id
            || removed.Item.Type == EmbyItemType.Series && item.SeriesId == removed.Item.Id
            || removed.Item.Type == EmbyItemType.Season && item.SeasonId == removed.Item.Id);
        foreach (var (_, shelf) in _all)
        {
            foreach (var card in shelf.Cards.Where(card => Deleted(card.Item)).ToArray())
            {
                card.AbandonPoster();
                shelf.Cards.Remove(card);
            }
        }
        if (Slides.Any(slide => Deleted(slide.Item)))
            Slides = Slides.Where(slide => !Deleted(slide.Item)).ToArray();
        ReconcileRows();
        IsReady = true;
    }

    /// <summary>
    /// 主页「继续观看」那一排的一张卡被标成已看之后，把那一格收拾干净 —— 单集换成本剧的下一集，电影、以及
    /// 没有下一集的单集一律撤掉（用户令 2026-10-15「主页的继续观看标记已看之后要自动切换为下一集，如果没有下一集
    /// 或者是电影标记已看后要移出继续观看」）。
    /// </summary>
    /// <remarks>
    /// 只认「继续观看」那一排：别的排上的卡标记已看只是它自己那个对勾的事，条目还在那一排里（服务器那条 Resume
    /// 接口才管得着「这一项该不该留在继续观看」）。
    /// <para>
    /// 不重读整排、也不走整页重读：服务器那条 Resume 接口只认「有没有播放进度」，标记已看之后那一集从清单里消失
    /// 是对的，可下一集没有播放进度、它不会给 —— 「切换为下一集」得自己问（<see cref="ResumeSlot"/>，服务器那条
    /// <c>Shows/NextUp</c> 缩到这一部剧上）。重读整页换不来下一集，却要重建一页海报和轮播。
    /// </para>
    /// </remarks>
    internal void MarkedWatched(CardItem card) => _ = AfterWatchedAsync(card);

    private async Task AfterWatchedAsync(CardItem card)
    {
        if (!Live || card.Scope is not { IsAvailable: true } scope) return;
        if (ResumeShelf() is not { } shelf) return;
        if (!card.IsCurrent || !shelf.Cards.Contains(card)) return;

        var watched = card.Item;

        // 下一集先问回来再动屏：问的这段时间那张卡还在原处（它已经带上已看的对勾了），回来才一次换掉 ——
        // 先撤后插会让那一排空着闪一下。问不出来（最后一集、断线、离页）都按「没有下一集」办。
        var next = ResumeRow.WantsNext(watched)
            ? await ResumeSlot.NextEpisodeAsync(scope, watched, Category).ConfigureAwait(true)
            : null;

        // 这一趟往返里页面可能已经走了、或者换了账号：那就一个字都别动。
        if (!Live || !card.IsCurrent || !shelf.Cards.Contains(card)) return;

        var slot = ResumeSlot.Apply(shelf.Cards, card, next, shelf.NewCard);
        Log.Info(Category, slot is null
            ? $"「{watched.Name}」标记已看，从继续观看撤下"
            : $"「{watched.Name}」标记已看，那一格换成「{slot.Name}」");

        ReconcileRows();
    }

    /// <summary>「继续观看」那一排；勾掉了、或者这个账号一条都没有时是 null。</summary>
    private CardShelf? ResumeShelf() =>
        _all.FirstOrDefault(entry => entry.Row.Key == HomeLayout.Resume).Shelf;

    /// <summary>
    /// 某一排的卡片增减过之后的账：装载数、空态，以及空掉的那几排从横排里收起来（装到了东西才排，见
    /// <see cref="LoadAsync"/>）。删除和「标记已看之后那一格撤掉」共用这一份。
    /// </summary>
    private void ReconcileRows()
    {
        foreach (var (_, shelf) in _all)
        {
            if (shelf.Cards.Count == 0) Shelves.Remove(shelf);
        }

        if (LibraryFlowIndex >= 0)
            LibraryFlowIndex = _all.TakeWhile(entry => entry.Row.Key != HomeLayout.Libraries)
                .Count(entry => entry.Row.Visible && entry.Shelf.Cards.Count > 0);
        LoadedCount = _all.Where(entry => entry.Row.Key != HomeLayout.Libraries).Sum(entry => entry.Shelf.Cards.Count);
        ShowEmptyNotice = Shelves.Count == 0;
    }

    /// <summary>播放只改变观看状态，不重建轮播，以免返回时丢掉已解码背景并重置轮播位置。</summary>
    internal Task RefreshPlaybackAsync() => LoadAsync(refreshSlides: false);

    /// <summary>
    /// 定时看看服务器上有没有新入库的东西（见 <see cref="LibraryWatch"/>）：有新内容才整页重读一遍，货架和
    /// 轮播一起换；没变化就什么都不动。
    /// <para>
    /// 用户令（2026-10-16）「有新媒体入库时主页不会自动刷新」：从别的页面返回时这一页本来就会重读一次
    /// （<c>ShellPage.RefreshActive</c>），缺的正是「就停在这一页上」那一档 —— 主页只在导航进来时读一次，
    /// 之后服务器新入库的内容不会自己出现。
    /// </para>
    /// <para>
    /// 这一趟只问一句「最新的一批变没变」（每个来源一次），**不重读整页**：没变也重读，等于每隔半分钟把一页
    /// 海报连轮播全拆了重装。真变了才走 <see cref="LoadAsync"/> —— 那和用户自己按刷新、从别的页面返回时走的
    /// 是同一条路（含轮播，用户 2026-10-16 选定「货架+轮播一起更新」）。
    /// </para>
    /// <para>
    /// 钟在页面上（<c>HomePage</c>），这一头只有这一件事。还没 <c>Attach</c>、已经离页、正在读、或者上一趟
    /// 还没回来，都直接放过这一拍。
    /// </para>
    /// </summary>
    internal async Task AutoRefreshAsync()
    {
        if (!Live || _session is null || _actions is null || _all.Length == 0) return;
        if (Busy || _watchBusy) return;

        // 空页面（全不勾、轮播也关着）没有东西可换：这一拍连那一句「变了没有」都不问。
        if (!ShowsAnything) return;

        _watchBusy = true;
        try
        {
            if (await ProbeStampsAsync(CancellationToken.None).ConfigureAwait(true) is not { } stamps) return;
            if (!Live) return;

            var changed = _watch.Changed(stamps);
            if (changed.Count == 0) return;

            // 先记下新的一批再重读：重读那一趟自己也会记一遍，但万一它读不成（比如最新那一条接口刚好抽风），
            // 没有这一句就会每半分钟拿同一个变化重读一次，一直刷下去。
            _watch.Reset(stamps);
            Log.Info(Category, $"定时检查：{string.Join('、', changed.Select(SourceName))} 有新入库，重读主页");
            await LoadAsync().ConfigureAwait(true);
        }
        finally
        {
            _watchBusy = false;
        }
    }

    /// <summary>
    /// 实时通道报上来了：服务器说某个来源上有库变更（<c>LibraryChanged</c>）。
    /// <para>
    /// **不直接重读**，只立脏位、排一拍，由 <see cref="PumpRealtimeAsync"/> 统一消费。理由见
    /// <see cref="_realtimeDirty"/>：一次库变更常连着推好几条，直接重读会把整页海报拆装好几遍。
    /// </para>
    /// <para>
    /// 这一句可能在任意线程上被叫到（通道的循环跑在线程池上），所以脏位和集合只做最轻的写、不碰 UI：
    /// 真正读 UI 的那一步在 <see cref="PumpRealtimeAsync"/> 里，它已经回到了这一页的同步上下文。
    /// </para>
    /// </summary>
    private void OnRealtimeLibraryChanged(string sourceKey)
    {
        lock (_realtimeSources)
        {
            _realtimeDirty = true;
            if (sourceKey.Length > 0) _realtimeSources.Add(sourceKey);
            if (_realtimeScheduled) return;
            _realtimeScheduled = true;
        }

        // 排到这一页自己的同步上下文上跑。拿不到队列（还没 Attach / 已经离页）就放过这一拍 ——
        // 30 秒那一趟还在，不会漏掉什么。
        if (_dispatcher?.Post(() => _ = PumpRealtimeAsync()) != true) ClearRealtimeScheduled();
    }

    /// <summary>
    /// 把攒下的实时通知折成一趟核对。<see cref="AutoRefreshAsync"/> 本来就是「问一句变了没有、真变了才重读」，
    /// 这一趟直接复用它 —— 快路径和 30 秒那一趟走的是同一条路，两边的判据、防重刷、日志都是同一套。
    /// </summary>
    private async Task PumpRealtimeAsync()
    {
        string[] sources;
        lock (_realtimeSources)
        {
            if (!_realtimeDirty) { _realtimeScheduled = false; return; }
            _realtimeDirty = false;
            sources = [.. _realtimeSources];
            _realtimeSources.Clear();
            _realtimeScheduled = false;
        }

        if (sources.Length > 0)
            Log.Info(Category, $"实时通道：{string.Join('、', sources.Select(SourceName))} 有新入库，提前核对一次");

        await AutoRefreshAsync().ConfigureAwait(true);
    }

    /// <summary>把这一拍交出去失败之后把「已排」放回去，否则下一批通知会被那一格挡住、再也排不上。</summary>
    private void ClearRealtimeScheduled()
    {
        lock (_realtimeSources) _realtimeScheduled = false;
    }

    /// <summary>
    /// 每个来源此刻最新的一批入库，折成一行字（<see cref="LibraryWatch.Stamp"/>）。
    /// <para>
    /// 读不出来的来源**不放进结果里**：调用方拿它和上一次的读数比，缺了一个来源会被读成「变了」，那正是多账号
    /// 聚合时每半分钟白刷一次的来源（见 <see cref="LibraryWatch"/>）。这一趟失败也不弹提示条 —— 用户没让做什么
    /// 事，一个后台的例行问询不该在屏上留一条红 —— 只记日志，而且同一场故障只记一条（<see cref="_watchReported"/>）。
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, string>?> ProbeStampsAsync(CancellationToken token)
    {
        var sources = _sources.Where(scope => scope.IsAvailable).ToArray();
        if (sources.Length == 0) return null;

        async Task<(string Key, string? Stamp, Exception? Error)> ReadAsync(EmbySessionScope scope)
        {
            try
            {
                var items = await scope
                    .ExecuteAsync((client, ct) => client.GetLatestAsync(null, LatestSize, ct), token)
                    .ConfigureAwait(true);
                return (scope.Key, LibraryWatch.Stamp(items), null);
            }
            catch (Exception error)
            {
                return (scope.Key, null, error);
            }
        }

        var results = await Task.WhenAll(sources.Select(ReadAsync)).ConfigureAwait(true);

        var stamps = new Dictionary<string, string>(StringComparer.Ordinal);
        Exception? failure = null;

        foreach (var result in results)
        {
            if (result.Stamp is { } stamp) stamps[result.Key] = stamp;
            else if (result.Error is not OperationCanceledException) failure ??= result.Error;
        }

        if (failure is null)
        {
            _watchReported = false;
        }
        else if (!_watchReported)
        {
            _watchReported = true;
            Log.Warn(Category, "定时检查服务器最新入库失败，这一趟按没有变化处理（同一场故障只记这一条）", failure);
        }

        return stamps.Count > 0 ? stamps : null;
    }

    /// <summary>一个来源的显示名（日志用）；认不出来就照钥匙报。</summary>
    private string SourceName(string key) =>
        _sources.FirstOrDefault(scope => scope.Key == key)?.DisplayName ?? key;

    private async Task LoadAsync(bool refreshSlides = true)
    {
        if (!Live || _session is null || _actions is null || _all.Length == 0) return;

        var token = BeginLoad();
        var refreshVersion = _refresh.Begin();
        refreshSlides = _refresh.NeedsSlides(refreshSlides);
        Subheading = "正在读取…";

        // Started together rather than awaited one after another: a round trip per row in sequence is
        // a page that takes a second per shelf, and none of them depends on another's answer.
        var resume = Visible(HomeLayout.Resume)
            ? FetchAsync("继续观看", (client, ct) => client.GetResumeAsync(ShelfSize, ct), token)
            : Task.FromResult(new List<EmbyItem>());
        var nextUp = Visible(HomeLayout.NextUp)
            ? FetchAsync("接下来看", (client, ct) => client.GetNextUpAsync(ShelfSize, ct), token)
            : Task.FromResult(new List<EmbyItem>());

        // 轮播自己的那一次请求（2026-09-13「轮播图改用前十个最近添加」；同日补上设置里的四行 ——
        // 「新增在设置中设置轮播图要使用什么媒体，和要使用最近添加还是随机的还有数量的选项。还有封面的
        // 轮换的秒数」）。来源和媒体类型照设置走：最近添加走 /Items/Latest（不带 parentId，和主页那几排
        // 的勾选、次序互不相干），随机走 /Items SortBy=Random。请求条数跟着设置里的张数放大 —— 筛掉没有
        // 宽图、并掉同一部剧之后还得凑得出那个数。
        var carouselUi = _settings?.Settings.Ui;
        var carouselCount = Emby.HomeCarousel.ClampSlots(
            carouselUi?.CarouselCount ?? Emby.HomeCarousel.Slots);
        var carouselMedia = carouselUi?.CarouselMedia ?? Emby.CarouselMediaType.All;
        var latestSize = Math.Max(LatestSize, carouselCount * 3);

        var latest = refreshSlides && _banner ? FetchAsync("轮播", (client, ct) =>
            carouselUi is not null && carouselUi.CarouselSource == Emby.CarouselSource.Random
                ? client.GetRandomAsync(latestSize, Emby.HomeCarousel.RandomTypes(carouselMedia), ct)
                : client.GetLatestAsync(
                    null,
                    latestSize,
                    ct,
                    Emby.HomeCarousel.LatestTypes(carouselMedia) is { Count: > 0 } types
                        ? string.Join(',', types)
                        : null),
            token) : Task.FromResult(new List<EmbyItem>());

        // 媒体库那几排各问一次自己那个库的最近添加。只问勾着的那几排 —— 勾掉一排就是连这次请求一起省掉。
        // 整个服务器的「最近添加」那一排 2026-09-12 从版面上退役了，但它没有断请求 —— 轮播 2026-09-13 起
        // 单独问一次（见上面 latest）。
        var perLibrary = new Dictionary<string, Task<List<EmbyItem>>>(StringComparer.Ordinal);
        foreach (var (row, shelf) in _all)
        {
            if (!row.Visible || HomeLayout.LibraryId(row.Key) is not { Length: > 0 } id) continue;
            if (_libraryViews.FirstOrDefault(library => HomeLayout.LibraryKey(library) == row.Key) is not { } library) continue;

            perLibrary[row.Key] = FetchAsync(
                HomeLayout.LibraryTitle(library.Name), (client, ct) => client.GetLatestAsync(id, LatestSize, ct), token, shelf.Source ?? _scope);
        }

        // 定时检查（见 AutoRefreshAsync）的基线也是这一趟记下的：整页重读之后，屏上这一份就是「服务器此刻的
        // 样子」。和别的请求一起发出去，不另起一趟往返。空页面上没有可换的东西，这一句省掉 —— 否则「隐藏的行
        // 不联网」就在这一句上破功（见 ShowsAnything）。
        var watch = ShowsAnything
            ? ProbeStampsAsync(token)
            : Task.FromResult<Dictionary<string, string>?>(null);

        await Task.WhenAll(perLibrary.Values.Cast<Task>().Append(resume).Append(nextUp).Append(latest).Append(watch)).ConfigureAwait(true);

        if (!IsCurrent(token)) return;

        if (watch.Result is { } stamps) _watch.Reset(stamps);

        // 继续观看 and 接下来看 span every item the account has touched — music included. This app has no
        // music face, so a track card would open onto nothing; the rows are filtered here rather than by
        // asking the server, because Emby's own endpoints for these two take no type filter. Each library
        // row's own latest list is filtered the same way, at Items below.
        var resumed = WithoutMusic(resume.Result);
        var next = WithoutMusic(nextUp.Result);

        // 轮播那份（见上面 latest）同样滤掉音乐类：这条带只站电影和剧集。「全部媒体」那一档音乐从来
        // 进不了最近添加的前排，这层滤网在；「随机」那一档请求侧就没有音乐，它白跑一遍。
        var newest = WithoutMusic(latest.Result);

        // 一排装什么由它那把钥匙说，不再是「数组第几个」—— 次序现在是用户拖出来的，位置说明不了任何事。
        // 轮播不在这张表里：它认的是 latest 那一次请求，跟这几排谁勾谁不勾、拖成什么次序都没有关系。
        IReadOnlyList<EmbyItem> Items(string key) => key switch
        {
            HomeLayout.Resume => resumed,
            HomeLayout.Libraries => _libraryViews,
            HomeLayout.NextUp => next,
            _ => perLibrary.TryGetValue(key, out var library) ? WithoutMusic(library.Result) : []
        };

        // 各媒体库自己那排按版面次序连成的一串。从前轮播的补位从这儿来，2026-09-13 轮播改用整服最近添加
        // （见上面 latest）之后，这一串只剩一个读者：LoadedCount 的那个数。
        List<EmbyItem> added = [.. _all
            .Where(entry => HomeLayout.LibraryId(entry.Row.Key) is not null)
            .SelectMany(entry => Items(entry.Row.Key))];

        foreach (var (row, shelf) in _all)
        {
            if (!row.Visible)
            {
                shelf.Clear();
                continue;
            }

            // 媒体库那一排的第二行空着：一个库自己的 CardSubtitle 就是「媒体库」三个字，而那正是这一排牌子上写的
            // 词（同 CardItem 那个 subtitle 参数上写的演职人员那条 —— 一整排卡片底下重复一遍排名，一个字的信息
            // 都不多）。空串而不是 null：null 是「按条目自己算」。多账号时卡片标题前面写服务器名，和货架标题
            // 同一份来源。
            shelf.Fill(Items(row.Key), row.Key == HomeLayout.Libraries ? _ => "" : null,
                row.Key == HomeLayout.Libraries
                    ? item => SourceCaption.Prefix(item.Name, item.Source, ShowServerNames)
                    : null);
        }

        // 装到了东西的排一律横着排。媒体库也在其中 —— 2026-09-08「移除轮播图右边的媒体库」之后不再单挑出来当
        // 右栏，就跟别的几排一样站在它自己在版面表上的位置上。
        Shelves.Clear();
        LibraryShelf = null;
        LibraryFlowIndex = -1;

        foreach (var (row, shelf) in _all)
        {
            if (!row.Visible || shelf.Cards.Count == 0) continue;

            // 媒体库那一排记下自己和位置：矮窗档（<see cref="SetLibraryOverlay"/>）搬的就是它，回默认档时按
            // 这个位置插回去。勾掉或者空的账号没有这一排，两个都空着，那一档整个不参与。
            if (row.Key == HomeLayout.Libraries)
            {
                LibraryShelf = shelf;
                LibraryFlowIndex = Shelves.Count;
            }

            Shelves.Add(shelf);
        }

        // 矮窗档跨重载不跳：上一趟压在轮播上的，这一趟重建完立刻再摘掉（<see cref="ApplyLibraryOverlay"/>）。
        ApplyLibraryOverlay();

        // 需求 5 → 2026-09-13「轮播图改用前十个最近添加」，同日下午张数进了设置：轮播不再站在继续观看上，
        // 只站设置里那个来源发回来的头若干张（筛掉没有宽图、并掉同一部剧，是 HomeCarousel 的事）。关掉轮播
        // 就一张都不造，带子自己收起来。
        if (refreshSlides) Slides = _banner ? BuildSlides(newest, carouselCount) : [];
        _refresh.Complete(refreshVersion, refreshSlides);

        LoadedCount = resumed.Count + next.Count + added.Count;

        // Only when there is nothing at all. An account whose libraries are listed but which has never
        // played anything is not an empty account, and telling it so under a row of its own libraries
        // would be plainly untrue.
        ShowEmptyNotice = Shelves.Count == 0;

        Subheading = LoadedCount == 0 ? null : $"{LoadedCount} 项";

        EndLoad(token);
    }

    /// <summary>
    /// 自检：这一次的版面 ——「轮播开；继续观看✓›、媒体库✓、最近添加 · 电影✗」。屏上那几排
    /// （<see cref="Shelves"/>）只有装到了东西的才在，所以这一句是唯一能看出「勾掉的那一排真的没排」和「拖出来的
    /// 次序真的生效了」的地方。轮播那个开关也报在这儿：它只管顶上那张大图在不在，不再动媒体库的位置。
    /// <para>
    /// 标题后面那个 › 是「这一排的牌子点得动」—— 2026-09-14「新增点击图中红框的标题可以进入对应的页面」之后
    /// 「继续观看」也该带一个，所以它必须能从这一行读出来：从前这一句只报标题，那一排到底有没有去处，报告里
    /// 一个字也看不出来。
    /// </para>
    /// </summary>
    internal string LayoutSummary => _all.Length == 0
        ? "未读取"
        : $"轮播{(_banner ? "开" : "关")}；" + string.Join("、", _all.Select(entry =>
            $"{entry.Shelf.Title}{(entry.Row.Visible ? "✓" : "✗")}"
                + $"{(entry.Shelf.CanOpen ? "›" : "")}"
                + $"{(entry.Shelf.Cards.Count > 0 ? "" : "（空）")}"));

    /// <summary>
    /// 自检：屏上那几排真按版面来的 —— 次序一样、勾掉的那几排真没排。两边各算一次再比：版面那一份是设置文件说的
    /// （<see cref="HomeLayout.Plan"/>），屏上那一份是 <see cref="Shelves"/>，中间隔着「装到了东西才排」这一条。
    /// 媒体库 2026-09-08 从右栏改回横排之后，它和别的排一样参加比对，没有单独一档。
    /// <para>
    /// 矮窗档（<see cref="LibraryOnBanner"/>）是唯一的例外：媒体库那一排真在屏上，只是压在轮播左下角、不在横排
    /// 里 —— 这是这一页对矮窗口的安排，不是版面丢了哪一排。比对时按**钥匙**把它从期望里摘掉（按标题摘，撞上一个
    /// 恰好叫「媒体库」的媒体库名就比对不出真丢了排的那天），读数里单独交代它去了哪儿。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail) LayoutRead()
    {
        var wanted = _all
            .Where(entry => entry.Row.Visible && entry.Shelf.Cards.Count > 0)
            .Select(entry => (entry.Row.Key, entry.Shelf.Title))
            .Where(entry => !_libraryOnBanner || LibraryShelf is null || entry.Key != HomeLayout.Libraries)
            .Select(entry => entry.Title)
            .ToList();

        var onScreen = Shelves.Select(shelf => shelf.Title).ToList();

        // 牌子上那个大于号该有的排：库那几排各有各的库，加上 2026-09-14 起接通的「继续观看」/「接下来看」。
        // 详情页那几排也会走到这儿（同一个 CardShelf），它们的目标是 None，约定上不在这一页上，所以按标题
        // 对不上也无所谓 —— 这一句问的是「这一页该有牌子的排，牌子是不是真有」。
        var opensWanted = _all
            .Where(entry => HomeLayout.TargetOf(entry.Row.Key) != HomeLayout.HomeRowTarget.None)
            .Select(entry => entry.Shelf.Title)
            .ToList();
        var opensMissing = opensWanted.Where(title => !CardOpen(title)).ToList();

        var ok = _all.Length > 0
            && wanted.SequenceEqual(onScreen, StringComparer.Ordinal)
            && opensMissing.Count == 0;

        return (ok, $"版面 {LayoutSummary}；横着排的 {(onScreen.Count == 0 ? "无" : string.Join('、', onScreen))}"
            + $"{(_all.Length == 0 ? "" : $"，该带大于号的 {opensWanted.Count} 排，没带的 {(opensMissing.Count == 0 ? "0" : string.Join('、', opensMissing))}")}"
            + (_libraryOnBanner ? "；媒体库那一排压在轮播左下角（矮窗档）" : ""));
    }

    /// <summary>
    /// 自检：这一页上标题叫 <paramref name="title"/> 的那一排，牌子报不报得可进。按标题找而不是按钥匙找，
    /// 因为屏上那份（<see cref="Shelves"/>）只有标题；标题在这一页是唯一的 —— 版面就是这么造的。
    /// </summary>
    private bool CardOpen(string title) =>
        _all.Any(entry => entry.Shelf.CanOpen
            && string.Equals(entry.Shelf.Title, title, StringComparison.Ordinal));

    /// <summary>
    /// The same rows without the music ones; see <see cref="EmbyItemType.IsMusic"/> for why they are
    /// hidden rather than shown and left to disappoint.
    /// </summary>
    private static List<EmbyItem> WithoutMusic(List<EmbyItem> items) =>
        items.Where(item => !EmbyItemType.IsMusic(item.Type)).ToList();

    /// <summary>
    /// The carousel's slides, out of the batch the band asked for itself
    /// (<see cref="HomeCarousel.Slides"/>). Empty until the page has been attached: a slide holds
    /// artwork, and there is nothing to fetch it with before then.
    /// </summary>
    private IReadOnlyList<BannerSlide> BuildSlides(IReadOnlyList<EmbyItem> latest, int slots) =>
        _images is not { } images
            ? []
            : [.. HomeCarousel.Slides(latest, slots).Select(item => new BannerSlide(item, images))];

    /// <summary>
    /// 播放 on the carousel. Straight to the shell with no parent and no sibling list, same as a card on
    /// this page: a slide is one item out of three unrelated rows, so there is no 选集 it belongs to.
    /// </summary>
    internal Task PlayAsync(BannerSlide slide) =>
        _actions is null || !Live ? Task.CompletedTask : _actions.PlayAsync(slide.Item);

    /// <summary>详情 on the carousel, and the same routing decision as <see cref="Open(CardItem)"/>.</summary>
    internal void Open(BannerSlide slide)
    {
        if (Live) _actions?.OpenItem(slide.Item);
    }

    /// <summary>
    /// 自检: how many slides the band got, and what artwork the items behind them actually have
    /// (<see cref="ItemArtwork.Census"/>). The census is the empirical half — 主页轮播 stands on 背景图,
    /// an episode almost never has one of its own, and whether this server hands down its series'
    /// backdrop is a question only a real account can answer.
    /// <para>
    /// 轮播被设置关掉那一档单说一句：那时张数为零是对的，而「0 张」和「服务器上一个宽图都没有」在报告里长得一样。
    /// </para>
    /// </summary>
    internal string BannerSummary => LoadedCount < 0
        ? "未读取"
        : !_banner
            ? "设置里关掉了（继续观看回到横着排的那一叠里）"
            : Slides.Count == 0
                ? "0 张（最近添加里没有带宽图的条目）"
                : $"{Slides.Count} 张，{ItemArtwork.Census([.. Slides.Select(slide => slide.Item)])}";

    /// <summary>
    /// Where a card goes when it is clicked.
    /// <para>
    /// Everything that is not a library defers to <see cref="ShellPage.OpenItem"/>, which is the one
    /// place the 「detail page or another grid」 choice is made. This page used to make that choice
    /// again for itself and get it wrong: it sent a series or a season straight to a child grid, so
    /// clicking a series on the home page skipped the series page — the synopsis, the cast and the
    /// episode list — and dropped the user into a bare grid of season folders.
    /// </para>
    /// </summary>
    internal void Open(CardItem card)
    {
        if (_actions is null || !Live || !card.IsCurrent) return;

        // A library's own card routes through the pane entry that opens it, so the pane highlight and
        // the breadcrumb end up exactly where clicking it in the pane would have put them.
        if (card.Item.Type is EmbyItemType.CollectionFolder && _actions.TryOpenLibrary(card.Item.Id, card.Scope)) return;

        _actions.OpenItem(card.Item);
    }

    /// <summary>
    /// 点一排的牌子（标题或者它右边那个大于号）时去哪儿。
    /// </summary>
    /// <remarks>
    /// 2026-09-13 用户原话「在最近添加右边添加一个大于号，点击标题后可以进入对应媒体库」。走的是和点这一排
    /// 卡片完全同一个入口（<see cref="IShellActions.TryOpenLibrary"/>），所以磁盘栏的高亮和面包屑落点一致 ——
    /// 从牌子进库和从卡片进库是同一件事，只是不用先挑一张卡。
    /// <para>
    /// 库那几排之外，2026-09-14「新增点击图中红框的标题可以进入对应的页面」把「继续观看」也接上了：它不属于
    /// 任何库，进的是自己那一张列着「没看完」的网格（<see cref="IShellActions.OpenRow"/>）。那一头照旧构造一个
    /// <c>LibraryRequest</c> 交给同一个 <c>LibraryPage</c>，所以排序、筛选、翻页这些是现成的。
    /// </para>
    /// <para>
    /// 没有去处的排（<see cref="HomeLayout.HomeRowTarget.None"/>，也就是详情页那几排）这里什么也不做，
    /// UI 那一头也不会给它们画出大于号。
    /// </para>
    /// </remarks>
    internal void OpenShelf(CardShelf shelf)
    {
        if (!Live) return;
        if (_actions is null)
        {
            Log.Warn(Category, $"{shelf.Title} 的牌子点了，但外壳动作还没接上");
            return;
        }

        switch (shelf.Target)
        {
            case HomeLayout.HomeRowTarget.Library:
                if (shelf.LibraryId is { Length: > 0 } id)
                {
                    Log.Info(Category, $"进库（牌子）：{id}");
                    _actions.TryOpenLibrary(id, shelf.Source);
                }
                else
                {
                    Log.Warn(Category, $"{shelf.Title} 的目标是库却没有库 id，牌子点了个寂寞");
                }
                break;

            case HomeLayout.HomeRowTarget.Resume:
            case HomeLayout.HomeRowTarget.NextUp:
                Log.Info(Category, $"开一排的页面：{shelf.Target}");
                _actions.OpenRow(shelf.Target, shelf.Source);
                break;

            default:
                Log.Warn(Category, $"{shelf.Title} 的牌子收到了点击，但目标是无处可去的 {shelf.Target}");
                break;
        }
    }

    public override void Cancel()
    {
        _active = false;
        // 这一页走了，实时通道一起收：它听的是这一页上的数据，页都不在了没人接这个结果。
        _realtime.Release();
        _realtimeDirty = false;
        lock (_realtimeSources) _realtimeSources.Clear();
        base.Cancel();
    }

    /// <summary>
    /// One row's worth of items, with its own failure kept to itself. Returns an empty list rather than
    /// throwing, so <c>Task.WhenAll</c> above never has to decide which failure won.
    /// </summary>
    private async Task<List<EmbyItem>> FetchAsync(
        string what,
        Func<EmbyClient, CancellationToken, Task<List<EmbyItem>>> operation,
        CancellationToken token,
        EmbySessionScope? source = null)
    {
        var sources = source is not null ? [source] : _sources;
        var results = await Task.WhenAll(sources.Select(scope => FetchSourceAsync(scope, what, operation, token))).ConfigureAwait(true);
        var items = new List<EmbyItem>();
        // 轮流取各来源的一项，避免默认账号填满轮播或首屏，其他来源永远看不到。
        for (var index = 0; results.Any(result => result.Count > index); index++)
            foreach (var result in results)
                if (index < result.Count) items.Add(result[index]);
        return items;
    }

    private async Task<List<EmbyItem>> FetchSourceAsync(
        EmbySessionScope scope,
        string what,
        Func<EmbyClient, CancellationToken, Task<List<EmbyItem>>> operation,
        CancellationToken token)
    {
        try
        {
            if (!Live || !scope.IsAvailable) return [];
            return await scope.ExecuteAsync(operation, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception error)
        {
            Log.Warn(Category, $"读取{scope.DisplayName}的{what}失败", error);

            // First failure wins the bar: three stacked messages saying the server is unreachable is
            // two more than anyone needs, and the other two are in the log either way.
            if (IsCurrent(token) && !NoticeOpen) Report(what, error);

            return [];
        }
    }
}

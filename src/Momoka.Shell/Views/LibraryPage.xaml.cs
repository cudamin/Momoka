using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell.Localization;
using Momoka.Shell.ViewModels;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>「布局」浮层的一行：两项互斥，勾跟着当前视图走；Jellyfin 的菜单也复用它。</summary>
public sealed record LayoutEntry(string Label, bool IsChecked, bool Wide = false, LibraryView View = LibraryView.Poster)
{
    public Visibility CheckVisibility => IsChecked ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// The poster grid's view. What is left here is the four things XAML and a view model cannot do for
/// themselves: a navigation parameter turned into an <c>Attach</c>, the scroll offset that drives the
/// paging, the construction of a <c>MenuFlyout</c>'s items — it has an <c>Items</c> list and no
/// <c>ItemsSource</c>, so there is nothing for a template to bind — and the context menu, which needs the
/// element it was raised over.
/// <para>
/// Everything else is in <see cref="LibraryViewModel"/>: the query, the paging arithmetic, the sort and
/// filter state, and what the two buttons say. None of it needs a window on screen any more, and the
/// page no longer names an element it might one day rename.
/// </para>
/// </summary>
public sealed partial class LibraryPage : Page, IShellContent
{
    private const string Category = "媒体库";

    public static Visibility BrowseHintVisibility(Visibility searchVisibility) =>
        searchVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private LibraryRequest? _request;

    /// <summary>
    /// Resolved on navigation, for the three things this page cannot ask the view model for: the card
    /// menus need a session, the sizing probe needs the raw 设置 value that the view model has already
    /// clamped, and the filter panel needs the server version to know which switches to offer.
    /// </summary>
    private ISettingsService? _settings;

    private EmbySession? _session;
    private IServerCapabilities? _capabilities;
    private IShellActions? _actions;

    private ScrollView? _scroll;
    private bool _released = true;
    private int _navigation;
    private CancellationTokenSource? _typing;

    /// <summary>暖缓存恢复后要落回的滚动位置；滚动器或布局还没就绪时先存着，由首次 Loaded 消费。</summary>
    private double? _pendingScroll;

    /// <summary>
    /// 导航参数正在接进视图模型的这一段：属性通知只记待办，接完统一执行一次视图、分区和头部布局。
    /// 视图模型一次 Attach 会连着发十几条通知，逐条建分区等于把同一面墙拆装十几遍。
    /// </summary>
    private bool _attaching;
    private NavigationTimingRun? _timing;

    /// <summary>导航时程序化设来源标签，别让 SelectionChanged 和后面的显式 ApplySource 各跑一遍。</summary>
    private bool _suppressSourceApply;

    /// <summary>
    /// 这次导航请求要显示的来源。来源标签在页面加载后才真正建出选中项，控件会把导航时写进去的
    /// <c>SelectedIndex</c> 改回第一项（2026-11-23 用户报「跳到搜索页却停在媒体库，搜索也没触发」）——
    /// 在页面落定之前以这里为准把它钉回去。
    /// </summary>
    private int _requestedSource;

    /// <summary>
    /// 还在「来源标签刚落定」这段窗口里：此间被控件改回去的选择要钉回 <see cref="_requestedSource"/>。
    /// 第一次排版时来源已经对上了就结束这段窗口，之后程序或用户改来源一律照办。
    /// </summary>
    private bool _pinningSource;

    /// <summary>用户已经自己选过来源（指针按下或键盘方向键）：此后的选择归用户，不再钉回请求的那一项。</summary>
    private bool _sourceChosen;

    /// <summary>这一页真正执行视图/分区/头部布局的次数，给性能探针核对一次 Attach 只做一次。</summary>
    internal int ViewApplies { get; private set; }
    internal int SectionBuilds { get; private set; }
    internal int HeaderApplies { get; private set; }

    /// <summary>
    /// 用户刚在布局/视图入口换了形状：下一个 <see cref="ApplyView"/> 落定后给整面墙补一段短淡入。
    /// 首次导航、恢复已保存的视图和探针直接改视图也走 ApplyView，但没有这个标记；标记只属于一次
    /// 真实的视图切换，ApplyView 读到就消费，不靠时序猜测。
    /// </summary>
    private bool _revealViewSwitch;

    // 五个面板都按首次使用创建：普通媒体库不该替 MoviePilot、AniBT、筛选和设置付构造与隐藏控件的价钱。
    private MoviePilotSearchPanel? _moviePilotPanel;
    private MoviePilotVersionsView? _moviePilotResources;
    private MoviePilotSubtitlePanel? _moviePilotSubtitles;
    private AniBtSearchPanel? _aniBtPanel;
    private FilterPanel? _filterPane;
    private LibrarySettingsPanel? _settingsPane;

    // 浮层的进出只动不透明度与位移：遮罩快一点，面板慢一点并轻推一段。
    private SurfaceMotion? _filterScrimMotion;
    private SurfaceMotion? _filterMotion;
    private SurfaceMotion? _settingsScrimMotion;
    private SurfaceMotion? _settingsMotion;

    /// <summary>返回搜索页时先记下 MoviePilot 那段的结果，等面板真正建出来再恢复。</summary>
    private MoviePilotSearchSnapshot? _pendingMoviePilotSearch;

    /// <summary>从订阅详情「搜索资源」跳来：面板建出来就用搜索框里的词真搜一次。</summary>
    private bool _pendingMoviePilotResourceSearch;

    /// <summary>从订阅详情「搜索字幕」跳来：字幕面板建出来就直接读这部剧的字幕。</summary>
    private MoviePilotDetailSearch? _pendingMoviePilotDetailSearch;

    internal MoviePilotSearchPanel? MoviePilotPanel => _moviePilotPanel;
    internal MoviePilotVersionsView? MoviePilotResources => _moviePilotResources;
    internal MoviePilotSubtitlePanel? MoviePilotSubtitles => _moviePilotSubtitles;
    internal AniBtSearchPanel? AniBtPanel => _aniBtPanel;
    internal bool MoviePilotPanelCreated => _moviePilotPanel is not null;
    internal bool MoviePilotResourcesCreated => _moviePilotResources is not null;
    internal bool MoviePilotSubtitlesCreated => _moviePilotSubtitles is not null;
    internal bool AniBtPanelCreated => _aniBtPanel is not null;
    internal bool FilterPanelCreated => _filterPane is not null;
    internal bool SettingsPanelCreated => _settingsPane is not null;

    /// <summary>探针与自检用；真正创建仍由打开面板的那条路走。</summary>
    internal FilterPanel FilterPane => _filterPane ?? EnsureFilterPanel();
    internal LibrarySettingsPanel SettingsPane => _settingsPane ?? EnsureSettingsPanel();

    /// <summary>筛选、排序或分区切换已经把重查发出去：第一批结果落定时给整面墙补一段淡入。</summary>
    private bool _wallResetPending;
    private readonly SolidColorBrush _tabSelected = new();
    private readonly SolidColorBrush _tabForeground = new();

    /// <summary>
    /// 尺寸即时更新，只把图片重新取样留到停拖后，避免每帧为新宽度重复取图。
    /// </summary>
    private readonly DispatcherQueueTimer _gridResize;

    public LibraryPage()
    {
        NavigationTiming.Mark("page-ctor-begin");
        InitializeComponent();
        InitializeSelection();
        NavigationTiming.Mark("init-end");
        NavigationCacheMode = NavigationCacheMode.Enabled;

        // 几何在 SizeChanged 当场同步；停拖之后仅更新图片清晰度。
        // 计时器在这里建，是因为「停下来」这件事只有页面知道 —— 尺寸变化本身不带「这是最后一帧」。
        _gridResize = DispatcherQueue.CreateTimer();
        _gridResize.Interval = TimeSpan.FromMilliseconds(120);
        _gridResize.IsRepeating = false;
        _gridResize.Tick += (_, _) => RebuildWall();

        // Before the first load can raise it. The view model asks for one more page when it has just
        // added one; whether the window is full is the one thing only the view can answer.
        ViewModel.PageArrived += PumpIfShort;
        // 筛选、排序、分区切换是「整面墙换了一批」；分页追加不发这个信号，也就不该重播淡入。
        ViewModel.ResultsReset += () => _wallResetPending = true;
        ViewModel.WindowChanging += BeforeWindowChange;
        ViewModel.WindowChanged += AfterWindowChange;
        ViewModel.WindowAnchorRequested += () => RememberScrollAnchor();

        // The view shape is the view model's, applying it to the live controls is the view's — the
        // template and the menu ticks are both named elements, which is exactly what a view model
        // must not reach for.
        ViewModel.PropertyChanged += OnViewModelChanged;

        // Wired here rather than in the markup on purpose: the Segmented raises SelectionChanged while its
        // items are being built inside InitializeComponent — before EmbyContent / MoviePilotPanel (further
        // down the tree) have been assigned — so a markup handler runs ApplySource against null fields and
        // crashes the page. Subscribing after InitializeComponent skips that init-time firing; every later
        // change lands with the whole tree in place.
        SourceTabs.SelectionChanged += OnSourceChanged;

        // 换来源之后光标要能落在搜索框里：这一页唯一的输入口是那个框，而点标签会把焦点留在标签上，
        // 用户换完来源就打不了字、也按不出回车（2026-10-15 用户报的正是「按回车不触发搜索」）。只认
        // 指针点的那一下，键盘方向键在标签之间移动时不抢焦点，见 OnSourceTabsKeyDown。
        SourceTabs.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSourceTabsPointerPressed), handledEventsToo: true);
        SourceTabs.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSourceTabsPointerReleased), handledEventsToo: true);
        SourceTabs.PreviewKeyDown += OnSourceTabsKeyDown;

        SearchInput.GotFocus += OnSearchInputGotFocus;
        SearchInput.Loaded += (_, _) => PlaceSearchSites();
        // 搜索页的站点表（用户令 2026-11-22）：「完成」左边多一颗「立即搜索」，按它保存这次的选择并当场重搜。
        SearchSitePicker.SearchNow = true;
        SearchSitePicker.SearchRequested += (_, _) =>
        {
            if (SearchSource == 3 && _moviePilotSubtitles is { ViewModel.HasSelectedMedia: true } subtitles)
                _ = subtitles.ViewModel.ReloadAsync();
            else
                _ = SubmitSearchAsync(SearchInput.Text ?? string.Empty);
        };
        ViewModel.SubmitRecentSearch = SubmitSearchAsync;
        SearchCategories.SelectionChanged += OnSearchCategoryChanged;
        HeaderLayout.SizeChanged += OnHeaderSizeChanged;
        SearchSurface.SizeChanged += OnHeaderSizeChanged;
        Toolbar.SizeChanged += OnHeaderSizeChanged;
        LibraryTitle.SizeChanged += OnHeaderSizeChanged;
        PageRoot.SizeChanged += (_, _) => ApplyPanelSize();
        FilterOverlay.SizeChanged += (_, _) => ApplyPanelSize();
        SettingsOverlay.SizeChanged += (_, _) => ApplyPanelSize();
        // 窗口宽窄变了，表格画布要多宽就跟着变（见 ApplyTableWidth）。
        TableScroll.SizeChanged += (_, _) => ApplyTableWidth();
        // 量的是外面这层滚动器而不是卡片墙自己：墙的宽度是算出来钉上去的（见 ApplyWallMetrics），
        // 量它自己等于拿上一帧的答案算这一帧。
        TableScroll.SizeChanged += (_, _) => OnWallWidthChanged();
        UpcomingScroll.SizeChanged += (_, _) => ViewModel.ResizeUpcoming(UpcomingScroll.ActualWidth - UpcomingScroll.Padding.Right);
        UpcomingScroll.ViewChanged += OnUpcomingViewChanged;
        FavoritesScroll.SizeChanged += (_, _) => ViewModel.ResizeFavorites(FavoritesScroll.ActualWidth - FavoritesScroll.Padding.Right);
        SearchGroupsScroll.SizeChanged += (_, _) => OnSearchGroupsWidthChanged();
        SearchGroupsScroll.ViewChanged += OnSearchGroupsViewChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void BuildBrowseSections()
    {
        SectionBuilds++;
        UpdateTabColors(ThemeHost.Current);
        LibrarySections.Children.Clear();
        foreach (var entry in ViewModel.BrowseSections)
        {
            var tab = new ToggleButton
            {
                Content = entry.Label,
                Tag = entry.Section,
                Style = (Style)Resources["LibraryTabStyle"],
                IsChecked = ViewModel.Section == entry.Section
            };
            // Scoped resource overrides keep the selected pill neutral, like the web reference,
            // while theme roles still switch when the app's theme changes.
            foreach (var key in new[] { "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver",
                "ToggleButtonBackgroundCheckedPressed" }) tab.Resources[key] = _tabSelected;
            foreach (var key in new[] { "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver",
                "ToggleButtonForegroundCheckedPressed" }) tab.Resources[key] = _tabForeground;
            AutomationProperties.SetAutomationId(tab, "LibrarySection" + entry.Section);
            AutomationProperties.SetName(tab, entry.Label);
            tab.Checked += OnBrowseSectionClick;
            tab.Click += OnBrowseSectionClick;
            LibrarySections.Children.Add(tab);
        }
        ApplyHeaderLayout();
    }

    private void UpdateTabColors(UiTheme theme)
    {
        _tabSelected.Color = ThemeHost.ToColor(theme.Colors.Text.Mix(theme.Colors.Window, 0.6));
        _tabForeground.Color = ThemeHost.ToColor(theme.Colors.Text);
    }

    private async void OnBrowseSectionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: LibrarySection section }) return;
        await ViewModel.ChooseSectionAsync(section);
        if (_released || ViewModel.Section != section) return;
        // Clicking the already-selected ToggleButton would otherwise uncheck the only selection.
        foreach (var tab in LibrarySections.Children.OfType<ToggleButton>())
            tab.IsChecked = tab.Tag is LibrarySection value && value == ViewModel.Section;
        if (ViewModel.IsUpcomingPage)
            UpcomingScroll.ChangeView(null, 0, null, disableAnimation: true);
        else if (ViewModel.IsFavoritesPage)
            FavoritesScroll.ChangeView(null, 0, null, disableAnimation: true);
        else _scroll?.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }

    private async void OnBrowseGroupBack(object sender, RoutedEventArgs e) =>
        await ViewModel.LeaveBrowseGroupAsync();

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.ToolbarVisibility))
            ToolbarSurface.Visibility = _request?.IsSearch == true ? Visibility.Collapsed : ViewModel.ToolbarVisibility;
        // Attach 那一段的通知不逐条执行：挂完之后统一跑一次最终视图、分区和头部布局。
        if (_attaching) return;
        RefreshSelectionAvailability();
        if (e.PropertyName == nameof(LibraryViewModel.View)) ApplyView();
        if (e.PropertyName == nameof(LibraryViewModel.Fields)) ApplyTableWidth();
        if (e.PropertyName == nameof(LibraryViewModel.SectionVisibility))
        {
            BuildBrowseSections();
            ApplyView();
        }
        if (e.PropertyName is nameof(LibraryViewModel.UpcomingVisibility) or nameof(LibraryViewModel.FavoritesVisibility)) ApplyHeaderLayout();
        if (e.PropertyName == nameof(LibraryViewModel.MoviePilotTabsVisibility)) ApplyHeaderLayout();
        if (e.PropertyName == nameof(LibraryViewModel.SortLabel)
            || e.PropertyName == nameof(LibraryViewModel.SortDescending))
            Alpha.SetCurrent(null);
    }

    /// <summary>
    /// Created with the page rather than on navigation, so <c>x:Bind</c> never has a null root; see
    /// <see cref="HomePage.ViewModel"/>.
    /// </summary>
    internal LibraryViewModel ViewModel { get; } = new();

    /// <summary>For the self-check and the two tooling paths, which have to see the page from outside.</summary>
    internal int LoadedCount => ViewModel.LoadedCount;

    // Kept for the shell self-check and breadcrumb code, both of which need the page's title from
    // outside. Reads the view model rather than the slate: the slate is what draws it, and a probe
    // that walks the tree would be testing the drawing instead of the answer.
    internal string HeadingText => ViewModel.Heading;

    internal string SortKey => ViewModel.SortKey;

    internal bool SortDescending => ViewModel.SortDescending;

    internal string? SortPreset => ViewModel.SortPreset;

    internal bool IsReady => ViewModel.IsReady;

    internal EmbyItem? FirstItem => ViewModel.FirstItem;

    /// <summary>
    /// 需求 4: what artwork the server actually holds for the items on this page — 「60 个条目 —— 海报 60、
    /// 徽标 3、缩略图 58、横幅图 0、艺术图 0、背景图 41」. Counted over everything loaded rather than off the
    /// realised cards, because the question is the catalogue's, not the viewport's: whether an artwork this
    /// build now draws will ever be seen on this server.
    /// </summary>
    internal string ArtworkCensus => ItemArtwork.Census([.. ViewModel.Cards.Select(card => card.Item)]);

    /// <summary>
    /// Tooling: clicks the first row that drills down and says which one it was, for the self-check's
    /// detail stage. Goes through the grid's own invoke handler, so what it proves includes 「a click on a
    /// series reaches the series page」.
    /// </summary>
    internal EmbyItem? OpenFirstDetail() => ViewModel.OpenFirstDetail();

    /// <summary>What the filter panel is currently set to; read by the self-check.</summary>
    internal ItemFilters Filters => ViewModel.Filters;

    /// <summary>
    /// 搜索页's state, for the self-check: whether this grid is the search one, whether its box is on
    /// screen, what is in the box, what the middle of the page says and how many rows arrived.
    /// </summary>
    internal (bool IsSearch, bool BoxShown, string Text, string Notice, int Rows) SearchState => (
        _request?.IsSearch == true,
        SearchInput.Visibility == Visibility.Visible,
        SearchInput.Text ?? string.Empty,
        ViewModel.EmptyNotice,
        ViewModel.LoadedCount);

    /// <summary>
    /// What size this grid draws at: the width the view model builds its cards at, and the cell the live
    /// layout is actually using.
    /// <para>
    /// The latter is read off the layout the grid is holding rather than recomputed, which is the point
    /// of the probe — this grid's cell, card and decode width used to be three literals in the markup, so
    /// a binding that silently never fired would show up here as <c>NaN</c>, not as a number this method
    /// worked out for itself. Zero means a live layout with no cell to speak of, which is 列表: see
    /// <see cref="Shape"/>.
    /// </para>
    /// <para>
    /// The probe used to report the setting it came from alongside the card (设置 → 海报宽度); that row
    /// was deleted 2026-09-05 and the width is <c>CardSize.PosterWidth</c> now, so the card number left is
    /// the view model's, and the cell is what is still worth reading off the live page.
    /// </para>
    /// </summary>
    internal (int Card, double CellWidth, double CellHeight) Sizing
    {
        get
        {
            var (_, _, cell, row) = Shape();
            return (ViewModel.CardWidth, cell, row);
        }
    }

    /// <summary>
    /// Tooling: puts each of the three view shapes on the live grid in turn, says what the layout made of
    /// each one, and puts the original back.
    /// <para>
    /// Assigns the view model's <c>View</c> rather than calling <see cref="LibraryViewModel.ChooseView"/>:
    /// that also saves the choice and re-queries the server, and a probe may do neither — the walk would
    /// leave the library on 列表 and hand the next stage of the self-check an empty grid to click into.
    /// What is under test is the half that a click cannot get right on its own: a view change reaching the
    /// live layout. Both cell measurements are computed from the shape and were not announced when it
    /// changed, so 缩略图 drew 16:9 stills into 170-wide poster cells, and 列表 — a row is a picture with
    /// its text beside it — had the title, the info line and the synopsis cut off past the cell's edge.
    /// </para>
    /// <para>
    /// A launch cannot see that on its own, which is why this walk exists rather than one more reading of
    /// whatever view the app happened to start in: the cell bindings are evaluated with the saved view
    /// already in place, so every view is correct until something changes it.
    /// </para>
    /// </summary>
    internal (IReadOnlyList<(string Label, string Layout, double Cell, double Row)> Views, bool Restored) WalkViews()
    {
        var original = ViewModel.Display;
        var seen = new List<(string, string, double, double)>(7);

        foreach (var view in LibraryDisplayCatalog.EmbyViews)
        {
            // remember: false —— 走查看过就走，不把库留在别的形状上，也不写设置文件。
            ViewModel.ApplyDisplay(
                new LibraryDisplay { View = view, CardSize = original.CardSize, Fields = [.. original.Fields] },
                remember: false, requery: false);

            // The layout is swapped by ApplyView off the property change, and the cell numbers are pushed
            // into it by two bindings; a measured pass is what turns both into something to read.
            CardWall.UpdateLayout();
            seen.Add(Shape());
        }

        ViewModel.ApplyDisplay(original, remember: false, requery: false);
        CardWall.UpdateLayout();

        // Whether the page was handed back the way it was found, answered here because this is the only
        // scope that remembers what that was: everything the report reads off the page afterwards is
        // already the restored value, so a check comparing those to each other would agree with itself.
        // Enum.GetValues comes back in numeric order, so each shape's reading sits at its own value.
        var restored = ViewModel.View == original.View && Shape() == seen[(int)original.View];

        return (seen, restored);
    }

    /// <summary>
    /// What the grid's live layout currently is, read off the layout the grid is holding rather than off
    /// the one in the markup: the two are the same object until something swaps them, and 「the swap put
    /// the right object back」 is precisely what the walk above is asking. A <c>StackLayout</c> has no cell
    /// to report — the row it lays out is as wide as the page.
    /// </summary>
    private (string Label, string Layout, double Cell, double Row) Shape() => CardWall.Layout switch
    {
        // MinItemWidth is only the minimum image-size choice; the fitted cell width still comes from
        // the responsive grid model. Read row height from the actual live layout, not back from the
        // model that supplied its binding: a stale MinItemHeight must remain observable to self-check.
        UniformGridLayout grid => (ViewModel.ViewLabel, nameof(UniformGridLayout), ViewModel.CellWidth, grid.MinItemHeight),
        { } other => (ViewModel.ViewLabel, other.GetType().Name, 0, 0),
        _ => (ViewModel.ViewLabel, "没有布局", 0, 0)
    };

    /// <summary>
    /// What one格子 paints under the pointer, measured off a realised container rather than read back out
    /// of the markup. 三个读数：静止、指针在上面、按下去。
    /// <para>
    /// 这一份存在的理由是它问的那句话在截图里看不见，而且它靠的机制是会悄悄失效的那一种：
    /// <c>ItemContainer</c> 的三个状态把 <c>PART_CommonVisual</c> 的 <c>Fill</c> 换成
    /// <c>{ThemeResource ItemContainerPointerOverBackground}</c>，而那个键是从模板里往外查的 ——
    /// 本页把它改成了透明（见 LibraryPage.xaml 顶上那段），一旦 WinUI 改了键名，或者这份覆盖没被查到，
    /// 灰底就又回来了，压在卡片那圈胶片格外面，而闸门全绿。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 第一个名字不叫 <c>Rest</c>：那是 <c>ValueTuple</c> 给第八个字段留的名字，编译器不让用 —— 同
    /// <see cref="PosterCard"/> 里那一份。
    /// </remarks>
    internal (bool Found, string Still, string Over, string Pressed) ContainerHalo()
    {
        if (Find<ItemContainer>(CardWall) is not { } box) return (false, "无格子", "无格子", "无格子");

        var (still, over, pressed) = (Fill("UnselectedNormal"), Fill("UnselectedPointerOver"), Fill("UnselectedPressed"));

        // 量完把它交回静止：这个格子还在页面上，接着往下走的自检看到的得是没人碰过的那一张。
        VisualStateManager.GoToState(box, "UnselectedNormal", false);

        return (true, still, over, pressed);

        string Fill(string state)
        {
            VisualStateManager.GoToState(box, state, false);

            // 模板里那块矩形就叫这个名字；按名字找而不是「第一个矩形」——同一层还有选中框和复选框。
            return Find<Microsoft.UI.Xaml.Shapes.Rectangle>(box, "PART_CommonVisual") switch
            {
                { Fill: SolidColorBrush solid } => $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}",
                { Fill: null } => "不画",
                null => "找不到那块矩形",
                _ => "不是纯色",
            };
        }
    }

    /// <summary>
    /// 探针：这一面墙上一个真实格子多宽、格子里的卡片画多宽、这一行排了几张、墙本身多宽。
    /// <para>
    /// 用户令 2026-10-18「封面随窗口大小变化，互相之间不要留太大的空隙」说的是像素，所以这里量的是活的容器：
    /// 算出来的数只能证明算式自洽，证明不了卡片真的填满了格子。格子比卡片宽出来的那一截，就是两张封面之间多
    /// 出来的那条缝。
    /// </para>
    /// </summary>
    internal (double Cell, double Card, double Wall, double Viewport) GridPaint()
    {
        CardWall.UpdateLayout();

        var origin = new Windows.Foundation.Point(0, 0);
        var cards = DescendantsOf(CardWall).OfType<PosterCard>()
            .Select(card => (Card: card, Top: card.TransformToVisual(CardWall).TransformPoint(origin).Y))
            .ToArray();
        if (cards.Length == 0) return (0, 0, CardWall.ActualWidth, TableScroll.ActualWidth);

        var first = cards.Min(entry => entry.Top);
        var row = cards.Where(entry => Math.Abs(entry.Top - first) < 2).ToArray();
        var sample = row[0].Card;

        // 卡片外面那层就是格子（模板里 PosterCard 直接住在 ItemContainer 里）；量格子而不是量卡片，才看得见
        // 「卡片没填满格子」这件事本身。
        var cell = (FrameworkElement)sample;
        for (var node = VisualTreeHelper.GetParent(sample); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ItemContainer box)
            {
                cell = box;
                break;
            }
        }

        return (cell.ActualWidth, sample.ActualWidth, CardWall.ActualWidth, TableScroll.ActualWidth);
    }

    /// <summary>视觉树里所有这种东西，按从外到里的顺序。</summary>
    private static IEnumerable<DependencyObject> DescendantsOf(DependencyObject node)
    {
        yield return node;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            foreach (var child in DescendantsOf(VisualTreeHelper.GetChild(node, index))) yield return child;
    }

    /// <summary>视觉树里第一个这种东西，可以再挑名字。找不到就是 null —— 没实现出来的容器不是错。</summary>
    private static T? Find<T>(DependencyObject node, string? name = null) where T : FrameworkElement
    {
        if (node is T hit && (name is null || hit.Name == name)) return hit;

        var children = VisualTreeHelper.GetChildrenCount(node);

        for (var index = 0; index < children; index++)
            if (Find<T>(VisualTreeHelper.GetChild(node, index), name) is { } found) return found;

        return null;
    }

    /// <inheritdoc />
    public object? NavigationRequest => _request;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not LibraryRequest request)
        {
            Log.Warn(Category, "导航参数缺失，无法确定要显示哪个媒体库");
            return;
        }

        _attaching = true;
        var resumed = ViewModel.TryResumeNavigation(request, _settings!);
        if (!resumed) ViewModel.ClearNavigationContent();
        _request = request;
        _released = false;
        _navigation++;
        _attaching = true;
        NavigationTiming.Mark("attach-begin");
        _timing = NavigationTiming.Capture();

        // The shell reads this back to restore its pane highlight after a Frame.GoBack.
        Tag = request.Tag;

        // The one place this page resolves anything. Deliberately a single block: it is the line that goes
        // when the shell stops handing a container around, and spreading it would turn one deletion into a
        // hunt.
        var services = request.Services;
        _settings = services.GetRequiredService<ISettingsService>();
        _session = services.GetRequiredService<EmbySession>();
        _session.PermissionsChanged += OnBatchPermissionsChanged;
        _session.SourcesChanged += OnBatchSourcesChanged;
        _capabilities = services.GetRequiredService<IServerCapabilities>();
        _actions = services.GetRequiredService<IShellActions>();

        _moviePilot = services.GetRequiredService<MoviePilotService>();
        _moviePilot.AvailabilityChanged += OnMoviePilotAvailabilityChanged;
        _settings.Settings.MoviePilot.AniBtChanged += OnAniBtSettingChanged;
        ViewModel.UseSnapshots(services.GetRequiredService<BrowseSnapshotStore>());
        if (!resumed) ViewModel.Attach(
            request,
            _actions,
            _settings,
            _session,
            services.GetRequiredService<EmbyImageStore>(),
            _capabilities, _moviePilot);

        // 搜索面板只在真正切到那个来源时才建；这里只记下要恢复的 MoviePilot 结果与跨页跳来的搜索请求。
        // 返回栈复用同一页时（resumed）面板已在，跨页请求不能留到下一次手切来源才执行。
        _pendingMoviePilotSearch = request.IsSearch ? request.MoviePilotSearch : null;
        _pendingMoviePilotResourceSearch = !resumed && request.IsSearch && request.MoviePilotResourceSearchNow;
        _pendingMoviePilotDetailSearch = !resumed && request.IsSearch ? request.MoviePilotDetailSearch : null;
        SearchCategories.SelectedItem = SearchCategories.Items.FirstOrDefault(item =>
            item.Tag is string value && value == request.SearchCategory.ToString()) ?? TopResultsCategory;

        // 返回搜索页时恢复来源；新请求默认仍从媒体库开始。
        ApplyAniBtTab();
        _requestedSource = request.IsSearch ? request.SearchSource : 0;
        _sourceChosen = false;
        _pinningSource = true;
        WatchSourcePin();
        _suppressSourceApply = true;
        try
        {
            SourceTabs.SelectedIndex = _requestedSource;
        }
        finally
        {
            _suppressSourceApply = false;
        }
        if (!resumed) ApplySource();

        // Attach 期间的通知只跳过不执行；依赖接完、来源也定下之后跑一次最终版面。
        _attaching = false;
        RefreshSelectionAvailability();
        if (!resumed)
        {
            ApplyView();
            BuildBrowseSections();
        }
        NavigationTiming.Mark("attach-end");

        if (request.IsSearch && SearchSource != 0) ViewModel.Cancel();
        else
        {
            // 同一身份与查询下有快照就先摆出来；没有才走首次读取。两条都由 LoadNavigationAsync 收尾，
            // 区别只在它是否等待网络、是否串行补读之前的页。
            var restored = resumed || ViewModel.RestoreSnapshot();
            NavigationTiming.Mark(resumed ? "page-reuse-hit" : "page-reuse-miss");
            NavigationTiming.Mark(restored ? "snapshot-hit" : "snapshot-miss");
            NavigationTiming.Mark("reload-start");
            _ = LoadNavigationAsync(request, _navigation);
        }
    }

    private async Task LoadNavigationAsync(LibraryRequest request, int navigation)
    {
        if (ViewModel.RestoredFromCache)
        {
            // 卡片已经在集合里：先落回滚动位置，再让后台核对更新内容，不重读之前翻过的每一页。
            if (ViewModel.HasSavedAnchor && (request.ScrollOffset > 1 || ViewModel.WindowStart > 0)) _pendingAnchor = SavedScrollAnchor();
            else _pendingScroll = request.ScrollOffset;
            ApplyPendingScroll();
            _ = ViewModel.RevalidateAsync();
            return;
        }

        var offset = request.ScrollOffset;
        var count = request.LoadedCount;
        var anchored = ViewModel.HasSavedAnchor;
        var loading = ViewModel.LoadForNavigationAsync();
        var queryToken = ViewModel.QueryToken;
        await loading.ConfigureAwait(true);
        if (_released || navigation != _navigation || !ViewModel.QueryCurrent(queryToken)) return;
        if (!anchored && count > 0 && !ViewModel.IsFavoritesPage) await ViewModel.LoadUntilAsync(count - 1).ConfigureAwait(true);
        if (_released || navigation != _navigation || !ViewModel.QueryCurrent(queryToken)) return;
        if (anchored && ViewModel.HasSavedAnchor)
        {
            _pendingAnchor = SavedScrollAnchor();
            ApplyPendingScroll();
        }
        else if (ViewModel.IsUpcomingPage)
        {
            UpcomingScroll.UpdateLayout();
            UpcomingScroll.ChangeView(null, offset, null, disableAnimation: true);
        }
        else if (ViewModel.IsGroupedSearch)
        {
            SearchGroupsScroll.UpdateLayout();
            SearchGroupsScroll.ChangeView(null, offset, null, disableAnimation: true);
        }
        else if (ViewModel.IsFavoritesPage)
        {
            if (offset > 0)
                foreach (var row in ViewModel.FavoriteRows)
                    while (ViewModel.QueryCurrent(queryToken) && row.HasMore && row.Error.Length == 0
                        && row.ReadCount < count)
                    {
                        var before = row.ReadCount;
                        await ViewModel.LoadFavoriteRowAsync(row).ConfigureAwait(true);
                        if (row.ReadCount <= before) break;
                    }
            if (_released || navigation != _navigation || !ViewModel.QueryCurrent(queryToken)) return;
            FavoritesScroll.UpdateLayout();
            FavoritesScroll.ChangeView(null, offset, null, disableAnimation: true);
        }
        else
        {
            CardWall.UpdateLayout();
            CardWall.ScrollView?.ScrollTo(0, offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
        }
    }

    public void Release()
    {
        if (_released) return;
        EndSelectionImmediately();
        if (_session is { } batchSession)
        {
            batchSession.PermissionsChanged -= OnBatchPermissionsChanged;
            batchSession.SourcesChanged -= OnBatchSourcesChanged;
        }
        // Frame caches one instance per page type. Non-root, incomplete and oversized pages must not
        // turn search/results or unbounded card histories into an application-lifetime cache.
        if (!ViewModel.NavigationReuseEligible || ViewModel.WindowStart > 0 || ContentScrollOffset > 1)
            NavigationCacheMode = NavigationCacheMode.Disabled;
        _timing = null;
        _released = true;
        ResetWallReveal();
        if (_moviePilot is { } moviePilot) moviePilot.AvailabilityChanged -= OnMoviePilotAvailabilityChanged;
        _moviePilot = null;
        if (_settings is { } settings) settings.Settings.MoviePilot.AniBtChanged -= OnAniBtSettingChanged;
        CancelTyping();
        DetachSearchTextBox();
        _navigation++;
        SearchSitePicker.ClosePopup();
        if (_request is { } request)
        {
            RememberScrollAnchor(allowReleased: true);
            request.ScrollOffset = ContentScrollOffset;
            request.LoadedCount = ViewModel.LoadedCount;
            if (request.IsSearch)
            {
                request.SearchTerm = ViewModel.SearchText;
                request.SearchSource = SearchSource;
                if (_moviePilotPanel is { } panel)
                {
                    var snapshot = panel.ViewModel.Capture();
                    request.MoviePilotSearch = snapshot is not null
                        && string.Equals(snapshot.Term, request.SearchTerm.Trim(), StringComparison.Ordinal) ? snapshot : null;
                    request.MoviePilotScrollOffset = request.MoviePilotSearch is not null ? panel.ScrollOffset : 0;
                }
            }
        }
        ViewModel.Cancel();
        _pendingAnchor = null;
        _pendingScroll = null;
        _restoringAnchor = false;
        ViewModel.ReleaseSearchPosters();
        _filterPane?.Dismiss();
        // 离开这一页不播淡出：把两层的值复位，回来时看到的仍是原来那一档。
        _filterScrimMotion?.Collapse();
        _filterMotion?.Collapse();
        CloseSettings(animated: false);
        StopSourcePin();
        _moviePilotPanel?.Release();
        _moviePilotResources?.Release();
        _moviePilotSubtitles?.Release();
        _aniBtPanel?.Release();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Release();
        base.OnNavigatedFrom(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeHost.Changed -= UpdateTabColors;
        ThemeHost.Changed += UpdateTabColors;
        UpdateTabColors(ThemeHost.Current);
        if (ViewModel.IsFnNas && !ViewModel.IsFolderDrillDown && _request?.IsSearch != true) ReorderFnNasToolbar();
        ApplyHeaderLayout();
        // 来源标签加载完才建出选中项：导航时写进去的选择可能已被控件改回第一项。用户还没自己选过，
        // 就按这次请求再钉一次（静默改回不会发 SelectionChanged，所以这里也得补一手）。
        if (_pinningSource && !_sourceChosen && SourceTabs.SelectedIndex != _requestedSource)
        {
            _suppressSourceApply = true;
            try { SourceTabs.SelectedIndex = _requestedSource; }
            finally { _suppressSourceApply = false; }
            ApplySource();
        }
        // 侧边栏模式点标题框进来时，先让关联动画把那只框接到这里（它同时从标题栏收起）；
        // 接不上才退回原来的淡入。
        var connected = _request?.IsSearch == true
            && TitleSearchTransition.Attach(Frame, SearchInput,
                started => { if (!started) HomeMotion.Reveal(SearchSurface, 0, 220, 8); });
        if (_request?.IsSearch == true && !connected)
            HomeMotion.Reveal(SearchSurface, 0, 220, 8);

        // 框刚落下来（或本来就空着）时光标就该在框里；带着结果回到这一页则不抢焦点。
        // Here rather than in OnNavigatedTo: nothing can take focus before it is in the tree.
        if (_request is { IsSearch: true } && (connected || ViewModel.SearchText.Length == 0))
            SearchInput.Focus(FocusState.Programmatic);
        if (_request is { IsSearch: true, SearchSource: 1 } search && _moviePilotPanel is { } panel)
            panel.RestoreScroll(search.MoviePilotScrollOffset);

        if (_scroll is not null)
        {
            ApplyPendingScroll();
            return;
        }

        // ItemsView exposes the scroller it owns; there is no other way to know how far down the list
        // the user is, and no other place to hang the paging trigger.
        _scroll = CardWall.ScrollView;

        if (_scroll is null)
        {
            Log.Warn(Category, "取不到 ItemsView.ScrollView，滚动分页将不可用");
            return;
        }

        _scroll.ViewChanged += OnViewChanged;
        ApplyPendingScroll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeHost.Changed -= UpdateTabColors;
        HomeMotion.Stop(SearchSurface);
        _gridResize.Stop();
        if (_scroll is null) return;

        _scroll.ViewChanged -= OnViewChanged;
        _scroll = null;
    }

    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e) => ApplyHeaderLayout();

    /// <summary>
    /// 卡片与网格当场同步尺寸，图片刷新单独防抖。跨列数时继续保留原有滚动锚点。
    /// </summary>
    private void OnWallWidthChanged()
    {
        var anchor = ViewModel.WindowStart > 0 && !_restoringAnchor ? ReadScrollAnchor() : null;
        var columns = ViewModel.GridColumns;
        ApplyWallMetrics();
        if (anchor is not null && columns != ViewModel.GridColumns)
        {
            _pendingAnchor = anchor;
            ApplyPendingScroll();
        }

        _gridResize.Stop();
        _gridResize.Start();
    }

    /// <summary>
    /// 窗口停下来后刷新当前尺寸的图片，等待期间继续显示旧图。
    /// </summary>
    private void RebuildWall()
    {
        _gridResize.Stop();
        ViewModel.RebuildGrid();
    }

    /// <summary>
    /// 按滚动条车道之外的可用宽度统一计算列数、卡片宽度和行高。
    /// </summary>
    private void ApplyWallMetrics()
    {
        // 卡片墙在右边让出一条滚动条车道（见 CardWall 的 Padding）；版面按让完以后的宽度铺格子，
        // 量外面那层滚动器之后要减掉这条带子，否则格宽是按多出 20 像素算的、卡片对不上格子。
        var width = TableScroll.ActualWidth - CardWall.Padding.Right;
        if (width <= 0 || !double.IsFinite(width)) return;
        ViewModel.ResizeGrid(width);
    }

    /// <summary>库名独占一行，分类与工具栏在下面分别居中；搜索保留自己的版式。</summary>
    private void ApplyHeaderLayout()
    {
        if (_attaching) return;

        var width = HeaderLayout.ActualWidth;
        if (width <= 0) return;
        HeaderApplies++;

        var search = _request?.IsSearch == true;
        var sectionLibrary = ViewModel.IsSectionLibrary;
        var folderDrillDown = ViewModel.IsFolderDrillDown;
        // 飞牛库根：只留筛选、排序、布局，摆到左上角（2026-11-01 用户令，照官网页端）。
        var fnNasRoot = ViewModel.IsFnNas && !folderDrillDown && !search;
        // Jellyfin 的混合内容库根沿用网页端 list.html：没有分区签，工具条居中。文件夹下钻页标题仍由面包屑承担。
        var jellyfinList = ViewModel.IsJellyfinListPage;
        var contentPage = ViewModel.IsUpcomingPage || ViewModel.IsFavoritesPage;
        // 截图整理仅保留 Emby/Jellyfin 继续观看的小标题与紧凑留白，其他页面沿用原版。
        var compactResume = !search && _request?.Row == HomeLayout.HomeRowTarget.Resume
            && ViewModel.SourceServerKind is MediaServerKind.Emby or MediaServerKind.Jellyfin;
        // 分区库（Emby／Jellyfin／飞牛）的头部有一批只在特定状态下出现的行：浏览分组、筛选条、状态条、
        // 搜索分类栏、飞牛的筛选面板。Grid 的 RowSpacing 连收起的行也照算，工具栏到封面因此空出 100 像素。
        // 这一档把这些间距改由元素自己的外边距表达：行收起就一个像素不占，显示时仍是原来的 12／16。
        // 文件夹下钻页同理：页内大标题收起，工具条是头部唯一的一行。Jellyfin list 页没有分区签，
        // 也不该被 Grid 里那两行空位撑出百来像素的空白，所以同样改由外边距控制。
        var headerByMargin = fnNasRoot || (sectionLibrary && !contentPage) || folderDrillDown || jellyfinList || compactResume;
        var contentGap = new Thickness(0, compactResume ? 28 : 20, 0, 0);
        var pageMargin = (Thickness)Application.Current.Resources["EgPageMargin"];
        // 顶部 32 DIP 供原生窗口按钮使用，再留 8 DIP；去掉搜索标题后内容仍整体上移。
        // 库根、Jellyfin 的 list 根与所有文件夹下钻页都用库页这 20 边距：卡片宽度是「可用宽 ÷ 列数」，
        // 同样是 1368 宽的窗口，32 边距会让下钻页从 7 列掉成 6 列、封面比 Emby 库大一档
        // （2026-11-01 用户令「参考 emby 库封面的大小统一其他库的封面大小」）。
        PageRoot.Margin = search ? new Thickness(pageMargin.Left, 40, pageMargin.Right, pageMargin.Bottom)
            : sectionLibrary || fnNasRoot || folderDrillDown || jellyfinList ? new Thickness(20) : pageMargin;
        PageRoot.RowSpacing = search ? 0 : contentPage ? 4 : headerByMargin ? 0 : 20;
        HeaderGrid.RowSpacing = search || contentPage || headerByMargin ? 0 : 16;
        HeaderLayout.RowSpacing = search || headerByMargin ? 0 : sectionLibrary ? 12 : 20;
        ((Grid)UpcomingScroll.Parent).RowSpacing = search || contentPage || headerByMargin ? 0 : 16;
        SearchCategoriesScroll.Margin = search ? new Thickness(0, 0, 0, 12) : new Thickness(0);
        LibrarySectionsSurface.Margin = headerByMargin ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        ToolbarSurface.Margin = headerByMargin ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        LibraryBrowseGroup.Margin = headerByMargin ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        LibraryFilterChips.Margin = headerByMargin ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        // 飞牛的筛选面板也在头部那一列里，但它有自己的 4 像素下边距；展开时补 16 上边距，
        // 收起时整行不占位置。
        FnNasFilterPanel.Margin = headerByMargin ? new Thickness(0, 16, 0, 4) : new Thickness(0, 0, 0, 4);
        // 搜索页没有标题和外卡；间距放在可见内容上，收起状态行不会留下空槽。
        var searchGap = search ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        EmbyContent.Margin = headerByMargin ? contentGap : searchGap;
        ApplySearchPanelMargins(searchGap);
        LibraryBusyBar.Margin = LibraryNoticeBar.Margin = compactResume ? new Thickness(0, 12, 0, 0)
            : headerByMargin ? contentGap : searchGap;
        // 文件夹下钻页的面包屑已经写着「媒体库 > 文件夹名」，网页端 list.html 也把这一行放在顶栏里，
        // 页内不再重复一道大标题；标题收起后工具条独占头部，居中摆放。
        LibraryTitle.Visibility = search || folderDrillDown ? Visibility.Collapsed : Visibility.Visible;
        // 库页左上角标题一律是 Emby 媒体库那一档（2026-11-12 用户令：参考 emby 媒体库左上角标题的大小，
        // 统一其他库左上角标题的大小）。从前分类库、飞牛库根、Jellyfin list 与继续观看会落到分区库那一档
        // （36 → 20，2026-11-01 用户令），同一个库名在不同页面大小不一。档位只由 EgPageTitleStyle 决定，
        // 页面差异仍由下面的间距与对齐表达。
        LibraryTitle.Style = (Style)Application.Current.Resources["EgPageTitleStyle"];
        LibrarySectionsSurface.MaxWidth = width;
        LibrarySectionsSurface.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(LibrarySectionsSurface, 1);
        Grid.SetColumn(LibrarySectionsSurface, 0);
        Grid.SetColumnSpan(LibrarySectionsSurface, 2);
        ToolbarSurface.HorizontalAlignment = search ? HorizontalAlignment.Right
            : fnNasRoot ? HorizontalAlignment.Left
            : sectionLibrary || folderDrillDown || jellyfinList ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Toolbar.CenterRows = !fnNasRoot && (sectionLibrary || folderDrillDown || jellyfinList);
        Grid.SetRow(SearchSurface, search ? 0 : 1);
        Grid.SetRow(LibraryTitle, 0);
        Grid.SetColumnSpan(LibraryTitle, search ? 1 : 2);
        Grid.SetRow(ToolbarSurface, search || folderDrillDown ? 0 : fnNasRoot ? 1 : sectionLibrary ? 2 : 1);
        LibraryTitle.MaxWidth = width;
        ToolbarSurface.MaxWidth = width;
        if (!search) return;

        var formWidth = Math.Min(900, width);
        SourceTabs.Visibility = ViewModel.MoviePilotTabsVisibility;
        // 资源与字幕两格都要站点入口：字幕接口同样按站点范围搜（2026-11-23 用户令「搜索字幕也要能选站点」）。
        SearchSitePicker.Visibility = SearchSource is 2 or 3 && SearchSourceAvailable ? Visibility.Visible : Visibility.Collapsed;
        SearchSitePicker.MaxWidth = formWidth < 520 ? 108 : 180;
        SearchSitePicker.SetCompact(SearchSitePicker.MaxWidth);
        SearchSurface.Margin = new Thickness(0, 8, 0, 0);
        ApplySearchPanelMargins(searchGap);
        SearchSurface.Width = formWidth;
        SearchSurface.MaxWidth = width;
        SearchEntry.Width = formWidth;
        // 间距放在可见来源栏上；第3行站点入口收起后不能留下 Grid 的额外行距。
        SearchForm.RowSpacing = 0;
        SourceTabs.Margin = SourceTabs.Visibility == Visibility.Visible
            ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        // 600：五个来源平分之后每格仍和原来的四格差不多宽，长标签不至于一上来就省略。
        SourceTabs.Width = Math.Min(600, formWidth);
        var visibleTabs = SourceTabs.Items.OfType<CommunityToolkit.WinUI.Controls.SegmentedItem>()
            .Where(item => item.Visibility == Visibility.Visible).ToArray();
        foreach (var item in visibleTabs)
        {
            // 五个来源始终保留；窄窗只缩短文案，不改查询来源或移走入口。
            item.Width = item.MaxWidth = Math.Max(0, (SourceTabs.Width - 14) / Math.Max(1, visibleTabs.Length));
            // 360 宽时五个来源平分后每格只有 69 DIP，4 DIP 的左右内边距会把「MP 订阅」挤出省略号；
            // 窄档收到 2 DIP，标签仍用短文案，不换行也不截断。
            var horizontalPadding = formWidth < 520 ? 2 : 8;
            item.Padding = new Thickness(horizontalPadding, 2, horizontalPadding, 2);
            if (item.Content is TextBlock label)
            {
                label.MaxWidth = Math.Max(0, item.MaxWidth - horizontalPadding * 2);
                label.Text = SourceTabs.Items.IndexOf(item) switch
                {
                    1 => formWidth < 520 ? UiText.Translate("MP 订阅") : UiText.Translate("MoviePilot 订阅"),
                    2 => formWidth < 520 ? UiText.Translate("MP 资源") : UiText.Translate("MoviePilot 资源"),
                    3 => formWidth < 520 ? UiText.Translate("MP 字幕") : UiText.Translate("MoviePilot 字幕"),
                    4 => "AniBT",
                    _ => UiText.Translate("媒体库")
                };
            }
        }
    }

    /// <summary>搜索页各段共用的内容边距；只拨已经建出来的面板。</summary>
    private void ApplySearchPanelMargins(Thickness margin)
    {
        if (_moviePilotPanel is { } moviePilot) moviePilot.Margin = margin;
        if (_moviePilotResources is { } resources) resources.Margin = margin;
        if (_moviePilotSubtitles is { } subtitles) subtitles.Margin = margin;
        if (_aniBtPanel is { } aniBt) aniBt.Margin = margin;
    }

    /// <summary>官网页端的三件顺序：筛选、排序、布局。只对飞牛库根重排，其它服务器保持原顺序。</summary>
    private void ReorderFnNasToolbar()
    {
        UIElement[] order = [FilterButton, SortButton, FnNasLayoutButton];
        foreach (var control in order) Toolbar.Children.Remove(control);
        for (var index = 0; index < order.Length; index++) Toolbar.Children.Insert(index, order[index]);
    }

    private void ApplyPanelSize()
    {
        if (PageRoot.ActualWidth <= 0 || PageRoot.ActualHeight <= 0) return;
        var (width, height) = Momoka.Infrastructure.LibraryPanelSize.For(PageRoot.ActualWidth, PageRoot.ActualHeight);
        if (_filterPane is { } filter)
        {
            filter.Width = width;
            filter.Height = height;
        }
        if (_settingsPane is { } settings)
        {
            settings.Width = width;
            settings.Height = height;
        }
    }

    /// <summary>
    /// The paging trigger. One viewport of slack, which is what keeps a fast scroll from ever reaching
    /// the bottom of what has been loaded.
    /// </summary>
    private void OnViewChanged(ScrollView sender, object args)
    {
        if (_removingCard || _restoringAnchor || _pendingAnchor is not null || _released) return;
        if (ViewModel.HasPrevious && sender.VerticalOffset < sender.ViewportHeight)
        {
            _ = ViewModel.LoadPreviousAsync();
            return;
        }
        var remaining = sender.ExtentHeight - (sender.VerticalOffset + sender.ViewportHeight);
        if (remaining < sender.ViewportHeight) ViewModel.LoadMore();

        SyncAlphaFromScroll();
    }

    private void OnUpcomingViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_released || _removingCard || !ViewModel.IsUpcomingPage) return;
        var remaining = UpcomingScroll.ScrollableHeight - UpcomingScroll.VerticalOffset;
        if (remaining < UpcomingScroll.ViewportHeight) ViewModel.LoadMore();
    }

    private void OnSearchGroupsViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_released || _removingCard || !ViewModel.IsGroupedSearch) return;
        var remaining = SearchGroupsScroll.ScrollableHeight - SearchGroupsScroll.VerticalOffset;
        if (remaining < SearchGroupsScroll.ViewportHeight) ViewModel.LoadMore();
    }

    /// <summary>
    /// 分组视图的可用宽度变了：格宽按它重算，窗口停下来之后再按新宽度重画卡片（同卡片墙那一路）。
    /// 卡片墙这时是收起的，量它只会得到零，所以分组视图得自己量。
    /// </summary>
    private void OnSearchGroupsWidthChanged()
    {
        if (!ViewModel.IsGroupedSearch) return;
        var width = SearchGroupsScroll.ActualWidth - SearchGroupsScroll.Padding.Right;
        if (width <= 0 || !double.IsFinite(width)) return;
        ViewModel.ResizeGrid(width);
        _gridResize.Stop();
        _gridResize.Start();
    }

    private void OnFavoriteMoreClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FavoriteRow row }) _ = ViewModel.LoadFavoriteRowAsync(row);
    }

    private void OnUpcomingRetryClicked(object sender, RoutedEventArgs e) => Reload();

    private void OnLibraryRetryClicked(object sender, RoutedEventArgs e) => Reload();

    /// <summary>
    /// Asks for one more page when the last did not fill the window, and keeps the two play buttons
    /// and the letter bar's rule in step with the rows that just landed.
    /// </summary>
    private void PumpIfShort()
    {
        ViewModel.RowsArrived();
        SyncAlphaFromScroll();
        RevealWallIfReset();

        if (!ViewModel.HasMore) return;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_released || _removingCard || _restoringAnchor || _pendingAnchor is not null || !ViewModel.HasMore) return;
            if (ViewModel.IsUpcomingPage)
            {
                if (UpcomingScroll.ScrollableHeight < UpcomingScroll.ViewportHeight) ViewModel.LoadMore();
            }
            else if (ViewModel.IsGroupedSearch)
            {
                if (SearchGroupsScroll.ScrollableHeight < SearchGroupsScroll.ViewportHeight) ViewModel.LoadMore();
            }
            else if (_scroll is { } scroll && scroll.ExtentHeight <= scroll.ViewportHeight + 1) ViewModel.LoadMore();
        });
    }

    /// <summary>
    /// 筛选、排序或分区的重新查询落定：整面墙淡入一段。标记只由 <see cref="LibraryViewModel.ResultsReset"/>
    /// 来，分页追加从不设置它；空结果、墙本身收着（推荐／收藏／分组搜索）以及离页之后都不播，值直接落定 ——
    /// 可见性与空态由各自原来的绑定决定，这一手只碰不透明度与位移。
    /// </summary>
    private void RevealWallIfReset()
    {
        if (!_wallResetPending) return;
        _wallResetPending = false;
        if (_released || ViewModel.Cards.Count == 0 || CardWall.Visibility != Visibility.Visible) return;
        HomeMotion.Reveal(CardWall, 0, 150, 6);
    }

    /// <summary>离页或重新进入时把整墙淡入的标记与残留不透明度一起复位。</summary>
    private void ResetWallReveal()
    {
        _wallResetPending = false;
        HomeMotion.Stop(CardWall);
    }

    private void OnItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (_selecting) return;
        if (args.InvokedItem is not CardItem card) return;
        if (ConsumeActionInvoke(card)) return;
        var prepared = CardDetailTransition.Prepare(Frame, DescendantsOf(sender).OfType<PosterCard>().FirstOrDefault(poster => ReferenceEquals(poster.Card, card)));
        NavigationTiming.Click("card");
        try { ViewModel.Open(card); }
        finally
        {
            if (prepared && ReferenceEquals(Frame?.Content, this)) CardDetailTransition.Cancel(Frame);
        }
    }

    /// <summary>
    /// 卡片上那颗更多／播放／已看／收藏按下去之后，<c>ItemContainer</c> 紧跟着报上来的那一次「调用」
    /// 要丢掉。它会和按钮自己的动作同时发生，用户看到的就是「点了更多，菜单一闪，人到了详情页」。
    /// 只认同一张卡、离按下的那一下很近；过了这一小段，点卡片还是正常的打开。
    /// </summary>
    private bool ConsumeActionInvoke(CardItem card)
    {
        if (!ReferenceEquals(card, _lastActionCard)) return false;
        _lastActionCard = null;
        return Environment.TickCount64 - _lastActionAt < 400;
    }

    private CardItem? _lastActionCard;
    private long _lastActionAt;

    /// <summary>探针：动作按钮之后紧跟的同一张卡调用要被吞掉，之后正常点卡片仍然放行。</summary>
    internal bool ProbeActionInvokeGuard()
    {
        if (ViewModel.Cards.FirstOrDefault() is not { } card) return false;

        _lastActionCard = card;
        _lastActionAt = Environment.TickCount64;
        var suppressed = ConsumeActionInvoke(card);
        var later = ConsumeActionInvoke(card);
        _lastActionCard = null;
        return suppressed && !later;
    }

    /// <summary>
    /// 推荐页那一排里的一张卡：点开这一项。和主页同一条路（<c>PosterCard.Card</c> 这条编译期通道），
    /// **不走 DataContext** —— ItemsRepeater + x:Bind 的模板实例上没人设过它，模式匹配必败、点击无声
    /// （2026-09-14 主页「继续观看点了没反应」就是漏在这一格）。
    /// </summary>
    private void OnShelfCardClicked(object sender, RoutedEventArgs e)
    {
        if (_selecting) return;
        if (sender is Button { Content: PosterCard { Card: { } card } poster } && !ConsumeActionInvoke(card))
        {
            var prepared = CardDetailTransition.Prepare(Frame, poster);
            NavigationTiming.Click("card");
            try { ViewModel.Open(card); }
            finally
            {
                if (prepared && ReferenceEquals(Frame?.Content, this)) CardDetailTransition.Cancel(Frame);
            }
        }
    }

    /// <summary>
    /// 需求 6：右键一张卡片。The list this grid is showing is handed along so 选集 works off it, exactly
    /// as it does for an invoke, and a metadata save reloads the page rather than patching the card —
    /// an edit can move the item's title, its artwork and its position in the sort all at once.
    /// </summary>
    private void OnCardContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_selecting) { args.Handled = true; return; }
        ViewModel.PrepareCollectionMembers();
        ItemCommands.Handle(_session, _actions, sender, args, ViewModel.Episodes(), Reload, RemovalCallback(), ViewModel.MarkedWatched,
            select: SelectionCallback());
    }

    /// <summary>需求 6：the same commands from the strip that appears on a card under the pointer.</summary>
    private void OnCardActionRequested(object? sender, CardActionEventArgs args)
    {
        if (_selecting) return;
        if (sender is PosterCard { Card: { } card })
        {
            _lastActionCard = card;
            _lastActionAt = Environment.TickCount64;
        }

        ViewModel.PrepareCollectionMembers();
        var poster = args.Action == CardAction.Details ? sender as PosterCard : null;
        PreviewHeadingTransition.Prepare(Frame, poster);
        CardDetailTransition.Navigate(this, poster, () =>
            ItemCommands.Handle(_session, _actions, sender, args, ViewModel.Episodes(), Reload, RemovalCallback(), ViewModel.MarkedWatched,
                select: SelectionCallback()));
    }

    private double ContentScrollOffset => ViewModel.IsUpcomingPage ? UpcomingScroll.VerticalOffset
        : ViewModel.IsGroupedSearch ? SearchGroupsScroll.VerticalOffset
        : ViewModel.IsFavoritesPage ? FavoritesScroll.VerticalOffset : _scroll?.VerticalOffset ?? 0;

    private bool _removingCard;
    private int _removalVersion;

    internal Action<CardItem> RemovalCallback()
    {
        var token = ViewModel.QueryToken;
        return card => _ = RemoveCardAsync(card, token);
    }

    private async Task RemoveCardAsync(CardItem card, CancellationToken token, bool deleted = true)
    {
        if (_released || !ViewModel.QueryCurrent(token)) return;
        var version = ++_removalVersion;
        var offset = ContentScrollOffset;
        var anchor = ReadScrollAnchor();
        _removingCard = true;
        var removed = await ViewModel.RemoveAsync(card, token, deleted).ConfigureAwait(true);
        if (version != _removalVersion) return;
        if (!removed || _released || !ViewModel.QueryCurrent(token))
        {
            _removingCard = false;
            return;
        }
        if (ViewModel.IsUpcomingPage)
        {
            UpcomingScroll.UpdateLayout();
            UpcomingScroll.ChangeView(null, offset, null, disableAnimation: true);
        }
        else if (ViewModel.IsGroupedSearch)
        {
            SearchGroupsScroll.UpdateLayout();
            SearchGroupsScroll.ChangeView(null, offset, null, disableAnimation: true);
        }
        else if (ViewModel.IsFavoritesPage)
        {
            FavoritesScroll.UpdateLayout();
            FavoritesScroll.ChangeView(null, offset, null, disableAnimation: true);
        }
        else
        {
            if (anchor is not null)
            {
                _pendingAnchor = anchor;
                ApplyPendingScroll();
            }
            else
            {
                CardWall.UpdateLayout();
                _scroll?.ScrollTo(0, offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            }
        }
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (version != _removalVersion) return;
            _removingCard = false;
            if (_released || !ViewModel.QueryCurrent(token)) return;
            SyncAlphaFromScroll();
            PumpIfShort();
        });
    }

    internal void RemoveUnfavorite(EmbyItem changed)
    {
        if ((_request?.IsFavorites == true || ViewModel.Section == LibrarySection.Favorites) && ViewModel.Cards.FirstOrDefault(card => card.Item.IdentityKey == changed.IdentityKey) is { } card)
            _ = RemoveCardAsync(card, ViewModel.QueryToken, deleted: false);
    }

    private void Reload() => _ = ViewModel.ReloadAsync();

    /// <summary>
    /// 需求 4：fills the sort flyout. Rebuilt on every open rather than once — the view model decides
    /// what belongs in it and why; see <see cref="LibraryViewModel.SortEntries"/>.
    /// </summary>
    private void OnSortMenuOpening(object sender, object e) => BuildSortMenu();

    /// <summary>
    /// Fills the flyout and hands back the labels it put there, so the self-check reports on the real
    /// menu rather than on a copy of this loop.
    /// <para>
    /// 一列扁平的条目，按标签排序（<see cref="LibraryViewModel.SortEntries"/> 决定），选中的那一条左边一个
    /// 勾、右边一个箭头 —— 网页端就是这个样子。方向不再是菜单顶上那两颗「升序／降序」：它挂在选中的那一条
    /// 上，点箭头翻方向，点字换字段。两件事分在两处，是因为它们本来就是两件事。
    /// </para>
    /// </summary>
    internal IReadOnlyList<string> BuildSortMenu()
    {
        var entries = ViewModel.SortEntries();
        SortList.ItemsSource = entries;
        return [.. entries.Select(entry => entry.Label)];
    }

    private void OnSortItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SortEntry entry) return;

        ViewModel.ChooseSort(entry.Option);
        SortMenu.Hide();
    }

    /// <summary>箭头：同一字段翻方向。菜单留在原地，网格立刻重查。</summary>
    private void OnSortDirectionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: EmbySortOption option }) return;

        ViewModel.FlipSort(option);
        BuildSortMenu();
    }

    private void OnFavoriteAccountMenuOpening(object sender, object e) => BuildFavoriteAccountMenu();

    /// <summary>收藏页账号筛选的菜单行；探针读它，浮层也用它。</summary>
    internal IReadOnlyList<FavoriteAccountEntry> BuildFavoriteAccountMenu()
    {
        var entries = ViewModel.FavoriteAccountEntries();
        FavoriteAccountList.ItemsSource = entries;
        return entries;
    }

    private async void OnFavoriteAccountItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FavoriteAccountEntry entry) return;

        FavoriteAccountMenu.Hide();
        if (!await ViewModel.ChooseFavoriteAccountAsync(entry.Key) || _released) return;
        _scroll?.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }

    /// <summary>Tooling: opens the account filter where a screenshot can see it.</summary>
    internal void ShowFavoriteAccountMenu() => FavoriteAccountButton.Flyout?.ShowAt(FavoriteAccountButton);

    /// <summary>Tooling: drops the sort menu open where a screenshot can see it.</summary>
    internal void ShowSortMenu() => SortButton.Flyout?.ShowAt(SortButton);

    /// <summary>Tooling: rebuilds Jellyfin's 布局 flyout and answers the labels it put there.</summary>
    internal IReadOnlyList<string> BuildJellyfinLayoutMenu()
    {
        var entries = ViewModel.JellyfinViews()
            .Select(view => new LayoutEntry(LibraryDisplayCatalog.LabelOf(view), view == ViewModel.View, View: view))
            .ToArray();
        JellyfinLayoutList.ItemsSource = entries;
        return [.. entries.Select(entry => entry.Label)];
    }

    /// <summary>Tooling: drops Jellyfin's 布局 flyout open where a screenshot can see it.</summary>
    internal void ShowJellyfinLayoutMenu()
    {
        BuildJellyfinLayoutMenu();
        JellyfinLayoutButton.Flyout?.ShowAt(JellyfinLayoutButton);
    }

    internal void HideJellyfinLayoutMenu() => JellyfinLayoutMenu.Hide();

    /// <summary>Tooling: rebuilds the Jellyfin list page's 更多 flyout and answers its image-type labels.</summary>
    internal IReadOnlyList<string> BuildJellyfinMoreMenu()
    {
        var entries = ViewModel.JellyfinViews()
            .Select(view => new LayoutEntry(ViewModel.JellyfinViewLabel(view), view == ViewModel.View, View: view))
            .ToArray();
        JellyfinImageTypeList.ItemsSource = entries;
        JellyfinShowTitleCheck.IsChecked = ViewModel.JellyfinListShowsTitles;
        return [.. entries.Select(entry => entry.Label)];
    }

    /// <summary>Tooling: drops the list page's 更多 flyout open where a screenshot can see it.</summary>
    internal void ShowJellyfinMoreMenu()
    {
        BuildJellyfinMoreMenu();
        JellyfinMoreButton.Flyout?.ShowAt(JellyfinMoreButton);
    }

    internal void HideJellyfinMoreMenu() => JellyfinMoreMenu.Hide();

    private void OnJellyfinMoreMenuOpening(object sender, object e) => BuildJellyfinMoreMenu();

    private void OnJellyfinImageTypeItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LayoutEntry entry) return;
        if (entry.View != ViewModel.View) _revealViewSwitch = true;
        ViewModel.ChooseJellyfinView(entry.View);
        // The web dialog stays open while the user changes image type and title; keep the ticks and the
        // title checkbox in step with the display that just landed.
        BuildJellyfinMoreMenu();
    }

    private void OnJellyfinShowTitleClicked(object sender, RoutedEventArgs e) =>
        ViewModel.SetJellyfinListShowTitles(JellyfinShowTitleCheck.IsChecked == true);

    /// <summary>Tooling: the same for the filter panel.</summary>
    internal void ShowFilterPanel() => OpenFilter();

    /// <summary>工具栏那颗筛选键：打开盖在整页上的面板。</summary>
    private void OnFilterClicked(object sender, RoutedEventArgs e)
    {
        // 飞牛的筛选是工具栏下面展开的一条，不是 Emby 那块盖住整页的浮层。
        if (ViewModel.IsFnNas)
        {
            _ = ViewModel.ToggleFnNasFilterAsync();
            return;
        }
        if (ViewModel.IsPlex)
        {
            _ = ViewModel.TogglePlexFilterAsync();
            return;
        }

        OpenFilter();
    }

    private void OnFnNasFilterCloseClicked(object sender, RoutedEventArgs e) => ViewModel.CloseFnNasFilter();

    private void OnPlexFilterCloseClicked(object sender, RoutedEventArgs e) => ViewModel.ClosePlexFilter();
    private void OnPlexFilterClearClicked(object sender, RoutedEventArgs e) => _ = ViewModel.ClearPlexFiltersAsync();
    private void OnPlexFilterRetryClicked(object sender, RoutedEventArgs e) => _ = ViewModel.RetryPlexFiltersAsync();

    private void OnFnNasLayoutMenuOpening(object sender, object e) => BuildFnNasLayoutMenu();

    internal IReadOnlyList<string> BuildFnNasLayoutMenu()
    {
        // 每次打开重建，和排序菜单同一套：勾要跟着刚刚落下的那一次选择走。
        var entries = ViewModel.FnNasViews()
            .Select(view => new LayoutEntry(ViewModel.FnNasViewLabel(view), view == ViewModel.View, View: view)).ToArray();
        FnNasLayoutList.ItemsSource = entries;
        return [.. entries.Select(entry => entry.Label)];
    }

    internal void ShowFnNasLayoutMenu() => FnNasLayoutButton.Flyout?.ShowAt(FnNasLayoutButton);

    internal void HideFnNasLayoutMenu() => FnNasLayoutMenu.Hide();

    private void OnFnNasLayoutItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LayoutEntry entry) return;
        if (entry.View != ViewModel.View) _revealViewSwitch = true;
        ViewModel.ChooseFnNasView(entry.View);
        FnNasLayoutMenu.Hide();
    }

    /// <summary>Jellyfin 的「布局」：菜单每次打开重摆，勾跟着当前视图走。</summary>
    private void OnJellyfinLayoutMenuOpening(object sender, object e)
    {
        JellyfinLayoutList.ItemsSource = ViewModel.JellyfinViews()
            .Select(view => new LayoutEntry(LibraryDisplayCatalog.LabelOf(view), view == ViewModel.View, View: view))
            .ToArray();
    }

    private void OnJellyfinLayoutItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LayoutEntry entry) return;
        if (entry.View != ViewModel.View) _revealViewSwitch = true;
        ViewModel.ChooseJellyfinView(entry.View);
        JellyfinLayoutMenu.Hide();
    }

    /// <summary>
    /// 需求 5：fills the panel for this grid, then puts it on the page. Rebuilt on every open rather than
    /// once, for the reasons the sort menu is — and the server version in particular may still have been
    /// in flight when the page opened.
    /// </summary>
    internal void OpenFilter()
    {
        if (ViewModel.IsPlex) { _ = ViewModel.TogglePlexFilterAsync(); return; }
        PrepareFilterPanel();
        if (_capabilities is null) return;

        FilterOverlay.Visibility = Visibility.Visible;
        ApplyPanelSize();
        (_filterScrimMotion ??= new SurfaceMotion(FilterScrim)).Enter(150, 0);
        if (_filterPane is { } filter) (_filterMotion ??= new SurfaceMotion(filter)).Enter(200, 12);
    }

    /// <summary>
    /// Rebuilds the panel without putting it on screen — what the self-check and the filter-changed path
    /// need. Showing it is <see cref="OpenFilter"/>'s half, and the two are apart so that reading the
    /// panel never leaves a modal layer over a page a screenshot is about to take.
    /// </summary>
    internal void PrepareFilterPanel()
    {
        if (_capabilities is null) return;

        EnsureFilterPanel().Open(ViewModel.Filters, ViewModel.FilterPreset, _capabilities.ServerVersion,
            ViewModel.FetchFilterValuesAsync, ViewModel.SourceServerKind, ViewModel.FilterFieldsForPage());
    }

    /// <summary>
    /// 两个浮层此刻都没有在播进出动画。截图与几何检查前等这一条 —— 尺寸不受动画影响（只动不透明度
    /// 与 RenderTransform），但画面还在淡入淡出里，拍下来的证据会是一片半透明。
    /// </summary>
    internal bool OverlayMotionSettled =>
        _filterScrimMotion?.IsAnimating != true && _filterMotion?.IsAnimating != true
        && _settingsScrimMotion?.IsAnimating != true && _settingsMotion?.IsAnimating != true;

    /// <summary>收起筛选层，回到原来的网格。用户点外面或按 ✕ 时先播完淡出再收。</summary>
    internal void CloseFilter() => CloseFilter(animated: true);

    /// <summary>
    /// 探针与页面释放用：不播淡出，立即收起。用户路径仍是 <see cref="CloseFilter()"/>，
    /// 那一段 140ms 的淡出不该让读可见性的检查去猜时序。
    /// </summary>
    internal void CloseFilterImmediately() => CloseFilter(animated: false);

    private void CloseFilter(bool animated)
    {
        ViewModel.ClosePlexFilter();
        _filterPane?.Dismiss();

        if (!animated)
        {
            _filterScrimMotion?.Collapse();
            _filterMotion?.Collapse();
            FilterOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        // 遮罩比面板先落定；只由面板的落定收起整层，中途重新打开会打断这两段、不会误收。
        (_filterScrimMotion ??= new SurfaceMotion(FilterScrim)).Exit(120, 0);
        if (_filterPane is { } filter)
            (_filterMotion ??= new SurfaceMotion(filter)).Exit(140, 8, () => FilterOverlay.Visibility = Visibility.Collapsed);
        else FilterOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnFilterScrimClicked(object sender, RoutedEventArgs e) => CloseFilter();

    private void OnFilterCloseRequested(object? sender, EventArgs e) => CloseFilter();

    /// <summary>
    /// Builds the filter panel through the page's own handler and hands back what it offered, so the
    /// self-check reports on the real panel rather than on a copy of this loop. Same reason
    /// <see cref="BuildSortMenu"/> is internal.
    /// </summary>
    internal IReadOnlyList<string> BuildFilterPanel()
    {
        PrepareFilterPanel();
        return FilterPane.Labels;
    }

    /// <inheritdoc cref="FilterPanel.EveryTemplateResolves"/>
    /// <remarks>Read after <see cref="BuildFilterPanel"/>: there are no blocks to ask about before it.</remarks>
    internal bool FilterTemplatesResolve => _filterPane?.EveryTemplateResolves ?? false;

    /// <summary>自检：the view shape in force, by name, and whether the letter bar is showing.</summary>
    internal string ViewName => ViewModel.View.ToString();

    internal bool AlphaShown => Alpha.Visibility == Visibility.Visible;

    private void OnFilterChanged(object? sender, EventArgs e) => ViewModel.ApplyFilters();

    /// <summary>
    /// 在系统 AutoSuggestBox 的清除键与查询键之间加一格，保留原生输入、清除和回车语义。
    /// 同时扩展模板的跨列表面，站点入口也在输入框边框内；模板重建时重新挂载。搜索页与「在 MoviePilot 搜索版本」
    /// 窗口共用 <see cref="SearchSiteSlot"/> 这一份做法。
    /// </summary>
    private void PlaceSearchSites() => SearchSiteSlot.Place(SearchInput, SearchSitePicker);

    /// <summary>实际焦点在不在搜索框的模板 TextBox 里（AutoSuggestBox 自己的 FocusState 不反映这一点）。</summary>
    internal bool SearchInputFocused
    {
        get
        {
            if (SearchInput.XamlRoot is null) return false;
            for (var focused = FocusManager.GetFocusedElement(SearchInput.XamlRoot) as DependencyObject;
                focused is not null; focused = VisualTreeHelper.GetParent(focused))
                if (ReferenceEquals(focused, SearchInput)) return true;
            return false;
        }
    }

    internal Task SubmitSearchAsync(string text)
    {
        CancelTyping();
        if (_released) return Task.CompletedTask;
        // 从来源标签按回车后把焦点交回输入框，接着修改关键词不必再点一次。
        SearchInput.Focus(FocusState.Programmatic);
        return RunSearchAsync(text, remember: true);
    }

    /// <summary>
    /// 键入（<see cref="OnSearchTextChanged"/>）与离线探针共用这条入口。
    /// <para>
    /// <b>随输入更新只属于「媒体库」这一个来源</b>：另外三个（MoviePilot 订阅、MoviePilot 资源、AniBT）都要用户
    /// 按回车或点搜索图标才发请求（2026-10-15 用户令）。那三个来源上键入仍做「收起旧查询」这一步 —— 它不发请求，
    /// 但能让上一个关键词的结果不再挂在新词下面，也让清空输入框回到空态。
    /// </para>
    /// </summary>
    internal async Task SearchTypingAsync(string text)
    {
        if (_released) return;
        CancelTyping();
        if (SearchSource != 0)
        {
            ViewModel.SearchText = text;
            if (!SearchSourceAvailable) return;
            if (SearchSource == 1) await EnsureMoviePilotPanel().PrepareSearchAsync().ConfigureAwait(true);
            else if (SearchSource == 3) EnsureMoviePilotSubtitles().Clear();
            else if (SearchSource == 4) await EnsureAniBtPanel().SearchAsync("").ConfigureAwait(true);
            else await EnsureMoviePilotResources().SearchAsync("").ConfigureAwait(true);
            return;
        }
        ViewModel.PrepareSearch(text);
        var typing = new CancellationTokenSource();
        _typing = typing;
        try
        {
            if (text.Trim().Length > 0) await Task.Delay(400, typing.Token).ConfigureAwait(true);
            if (!typing.IsCancellationRequested && !_released)
                await RunSearchAsync(text, remember: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_typing, typing)) _typing = null;
            typing.Dispose();
        }
    }

    private async Task RunSearchAsync(string text, bool remember)
    {
        if (_released) return;
        ViewModel.SearchText = text;
        if (!SearchSourceAvailable)
        {
            ApplySource();
            return;
        }
        await (SearchSource switch
        {
            1 => EnsureMoviePilotPanel().SearchAsync(text),
            2 => EnsureMoviePilotResources().SearchAsync(text),
            3 => EnsureMoviePilotSubtitles().SearchAsync(text),
            4 => EnsureAniBtPanel().SearchAsync(text),
            _ => ViewModel.SearchAsync(text, remember)
        }).ConfigureAwait(true);
    }

    // 来源是用户明确选的分段，连接变化不能偷偷把 MP / AniBT 查询改成媒体库查询。
    private int SearchSource
    {
        get => Math.Clamp(SourceTabs.SelectedIndex, 0, 4);
    }

    private bool SearchSourceAvailable => SearchSource == 0 || (_moviePilot?.Available == true
        && (SearchSource != 4 || _settings?.Settings.MoviePilot.AniBtEnabled != false));

    private void CancelTyping()
    {
        _typing?.Cancel();
        _typing = null;
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSourceApply) return;
        if (_pinningSource && !_sourceChosen && SourceTabs.SelectedIndex != _requestedSource)
        {
            // 控件在落定过程中把导航时写进去的选择改回了第一项：钉回这次请求要显示的来源。
            // 这里不能再走 ApplySource —— 那会把刚由请求触发的自动搜索／字幕读取当成「用户切走」收掉。
            _suppressSourceApply = true;
            try { SourceTabs.SelectedIndex = _requestedSource; }
            finally { _suppressSourceApply = false; }
            return;
        }
        _sourceChosen = true;
        _pinningSource = false;
        ApplySource();
    }

    /// <summary>来源标签第一次真正落定（已加载且选中项就是这次请求的来源）就结束钉定窗口。</summary>
    private void TrackSourcePin(object? sender, object e)
    {
        if (!_pinningSource || !SourceTabs.IsLoaded || SourceTabs.SelectedIndex != _requestedSource) return;
        SourceTabs.LayoutUpdated -= TrackSourcePin;
        _pinningSource = false;
    }

    /// <summary>接上「来源标签落定」这一耳朵；每次导航重接一次，不叠监听。</summary>
    private void WatchSourcePin()
    {
        SourceTabs.LayoutUpdated -= TrackSourcePin;
        SourceTabs.LayoutUpdated += TrackSourcePin;
    }

    /// <summary>离页／换请求时收手：钉定窗口就此结束。</summary>
    private void StopSourcePin()
    {
        SourceTabs.LayoutUpdated -= TrackSourcePin;
        _pinningSource = false;
    }

    /// <summary>
    /// 搜索页「媒体库」来源同时命中的订阅（2026-11-13）：点封面进这条订阅的媒体页，
    /// 用的是搜索页已经接好的 <see cref="IShellActions.OpenMoviePilotMedia"/>。
    /// </summary>
    private void OnSubscriptionMatchOpen(object? sender, EventArgs args)
    {
        if (_released || sender is not MoviePilotSubscriptionPoster { Card: { } card }) return;
        ViewModel.OpenMatchingSubscription(card);
    }

    private void OnSubscriptionMatchMenu(object? sender, EventArgs args)
    {
        if (_released || SearchSource != 0 || _moviePilot is not { Available: true } service
            || sender is not MoviePilotSubscriptionPoster { Card: { } card } poster) return;
        var manager = ViewModel.GetMatchingSubscriptionManager(card,
            ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
        if (manager is null) return;
        MoviePilotSubscriptionMenu.Create(this, manager, card, () => ViewModel.OpenMatchingSubscription(card), service)
            .ShowAt(poster.MenuAnchor);
    }

    private void OnSubscriptionMatchPosterRequested(object? sender, EventArgs args)
    {
        if (_released || sender is not MoviePilotSubscriptionPoster { Card: { } card }) return;
        ViewModel.RequestMatchingSubscriptionPoster(card);
    }

    /// <summary>搜索匹配到的订阅封面也弹同一块悬停预览；横版背景读不到就留海报。</summary>
    private void OnSubscriptionMatchPreviewRequested(object? sender, EventArgs args)
    {
        if (_released || _moviePilot is not { Available: true } service
            || sender is not MoviePilotSubscriptionPoster { Card: { } card }) return;
        _ = card.LoadBackdropAsync(service);
    }

    private void OnSubscriptionMatchPreviewDetails(object? sender, EventArgs args)
    {
        if (_released || sender is not MoviePilotSubscriptionPoster { Card: { } card }) return;
        ViewModel.OpenMatchingSubscription(card);
    }

    /// <summary>搜索匹配的订阅预览右上角「编辑订阅」：与更多菜单里的「编辑」同一个入口。</summary>
    private void OnSubscriptionMatchPreviewEdit(object? sender, EventArgs args)
    {
        if (_released || SearchSource != 0 || _moviePilot is not { Available: true } service
            || sender is not MoviePilotSubscriptionPoster { Card: { } card }) return;
        var manager = ViewModel.GetMatchingSubscriptionManager(card,
            ConfirmDialog.For(this, dialog => MoviePilotDialog.ShowAsync(dialog, service)));
        if (manager is null) return;
        _ = manager.EditAsync(card,
            (subscription, save, remove) => MoviePilotSubscriptionEditor.ShowAsync(this, subscription, service, save, remove));
    }

    /// <summary>
    /// 指针点了来源标签：把光标交回搜索框。点击把焦点留在标签上，而用户接下来要么打新词、要么按回车
    /// 用现有的词搜 —— 两件事都要求焦点在框里。挂 <c>PointerReleased</c> 而不是无条件抢：键盘方向键
    /// 在标签之间移动时焦点本来就该留在标签上（见 <see cref="OnSourceTabsKeyDown"/>）。
    /// </summary>
    /// <summary>
    /// 指针按下就算用户自己选了来源（早于选中项变化），后续选择归用户，不再钉回导航请求的那一项。
    /// </summary>
    private void OnSourceTabsPointerPressed(object sender, PointerRoutedEventArgs e) => _sourceChosen = true;

    private void OnSourceTabsPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_released && _request?.IsSearch == true) SearchInput.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 焦点在来源标签上时的回车＝照框里现有的词搜一次。键盘换来源不走上面那条（焦点留在标签上），所以
    /// 这一颗回车得由标签自己补上；标签本身没有回车语义 —— <c>Segmented</c> 只用方向键移焦点并跟着改
    /// 选中项，回车在它上面本来就是空的。
    /// </summary>
    private void OnSourceTabsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_released) return;
        // 方向键／Home／End／空格会让标签自己换选中项：先记成用户的选择，别被「钉回请求来源」拽回去。
        if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right
            or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down
            or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End
            or Windows.System.VirtualKey.Space)
        {
            _sourceChosen = true;
            return;
        }
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        _ = SubmitSearchAsync(SearchInput.Text ?? string.Empty);
        // 提交会把焦点交回输入框。这个回车随后在输入框抬起时不应补搜。
        _searchInputGuard?.MarkEnterConsumed();
    }

    private MoviePilotService? _moviePilot;

    private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_released || !ReferenceEquals(sender, _moviePilot)) return;
        ViewModel.RefreshMoviePilotAvailability();
        if (SearchSource != 0) ApplySource();
    });

    private void OnAniBtSettingChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_released) return;
        ApplyAniBtTab();
        ApplySource();
        ApplyHeaderLayout();
    });

    /// <summary>用户指定搜索页固定五个来源；AniBT 开关只决定该页能否搜索，不移走标签。</summary>
    private void ApplyAniBtTab()
    {
        AniBtSourceTab.Visibility = Visibility.Visible;
    }

    /// <summary>MoviePilot 订阅面板：首次切到这个来源时创建并接线。</summary>
    private MoviePilotSearchPanel EnsureMoviePilotPanel()
    {
        if (_moviePilotPanel is { } existing) return existing;

        var request = _request!;
        var panel = new MoviePilotSearchPanel { Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(panel, "MoviePilotPanel");
        panel.Attach(request.Services.GetRequiredService<MoviePilotService>(),
            request.Services.GetRequiredService<Momoka.Shell.Platform.ISystemLauncher>(), _actions!.OpenMoviePilotMedia);
        SearchSourceHost.Children.Add(panel);
        _moviePilotPanel = panel;

        if (_pendingMoviePilotSearch is { } snapshot)
        {
            _pendingMoviePilotSearch = null;
            if (panel.ViewModel.Restore(snapshot)) _ = panel.ViewModel.RefreshSubscriptionStatesAsync();
        }
        else if (panel.ViewModel.RestoreCached(ViewModel.SearchText) is { } restoredTerm)
        {
            ViewModel.SearchText = restoredTerm;
            _ = panel.ViewModel.RefreshSubscriptionStatesAsync();
        }

        return panel;
    }

    /// <summary>MoviePilot 资源面板：首次切到「MP 资源」时创建，并把站点选择器接到它的来源表。</summary>
    private MoviePilotVersionsView EnsureMoviePilotResources()
    {
        if (_moviePilotResources is not { } panel)
        {
            var request = _request!;
            panel = new MoviePilotVersionsView { Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(panel, "MoviePilotResources");
            panel.Attach(request.Services.GetRequiredService<MoviePilotService>(),
                request.Services.GetRequiredService<Momoka.Shell.Platform.ISystemLauncher>(), sharedInput: true);
            // 本次运行内保留上一次结果（用户令 2026-11-22）；换页面回来先摆出来，不重搜。
            panel.ViewModel.UseCache(request.Services.GetRequiredService<MoviePilotResourceSearchCache>());
            SearchSourceHost.Children.Add(panel);
            _moviePilotResources = panel;
            if (!_pendingMoviePilotResourceSearch && _pendingMoviePilotDetailSearch is not { Options.Subtitles: false })
                panel.ViewModel.RestoreCached(ViewModel.SearchText);
        }
        // 每次切到这一格都接一遍：字幕那一格也会占用页面上这唯一一颗站点入口，切回来要换回资源这份选择。
        SearchSitePicker.Attach(panel.ViewModel.SearchSites);
        // 面板是复用的：回到这一格时重读一次站点偏好（不发请求、不打断在飞的目录读取）。用户可能刚在
        // 订阅详情选过站点再跳回来，只靠建面板那一刻读的那一份会把这次选择漏掉。
        panel.ViewModel.SearchSites.ReloadPreference();
        if (_pendingMoviePilotDetailSearch is { Options.Subtitles: false } detailSearch)
        {
            _pendingMoviePilotDetailSearch = null;
            _request!.MoviePilotDetailSearch = null;
            _ = panel.ViewModel.SearchDetailAsync(detailSearch);
        }
        else if (_pendingMoviePilotResourceSearch)
        {
            // 从订阅详情跳来：那个词是用户刚确认过的，真搜一次，也顺手把旧缓存换掉。面板上次已经建过
            // （搜索页 → 订阅详情 → 再从详情跳回来）也要搜，不能因为控件还在就停在旧结果上。
            _pendingMoviePilotResourceSearch = false;
            _request!.MoviePilotResourceSearchNow = false;
            _ = panel.SearchAsync(ViewModel.SearchText);
        }
        return panel;
    }

    /// <summary>MoviePilot 字幕面板：首次切到「MP 字幕」时创建；从订阅详情「搜索字幕」跳来时直接读那部片。</summary>
    private MoviePilotSubtitlePanel EnsureMoviePilotSubtitles()
    {
        if (_moviePilotSubtitles is not { } panel)
        {
            var request = _request!;
            panel = new MoviePilotSubtitlePanel { Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(panel, "MoviePilotSubtitles");
            panel.Attach(request.Services.GetRequiredService<MoviePilotService>());
            SearchSourceHost.Children.Add(panel);
            _moviePilotSubtitles = panel;
        }
        // 字幕接口同样按站点范围搜；这一格也用页面上那一颗站点入口（切来切去要跟着换）。
        SearchSitePicker.Attach(panel.ViewModel.SearchSites);
        panel.ViewModel.SearchSites.ReloadPreference();
        if (_pendingMoviePilotDetailSearch is { Options.Subtitles: true } detailSearch)
        {
            _pendingMoviePilotDetailSearch = null;
            _request!.MoviePilotDetailSearch = null;
            _ = panel.ViewModel.LoadSubtitlesAsync(detailSearch.Media, detailSearch.Options.Episode, detailSearch);
        }
        return panel;
    }

    /// <summary>AniBT 面板：首次切到 AniBT 来源时创建。</summary>
    private AniBtSearchPanel EnsureAniBtPanel()
    {
        if (_aniBtPanel is { } existing) return existing;

        var request = _request!;
        var panel = new AniBtSearchPanel { Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(panel, "AniBtPanel");
        panel.Attach(request.Services.GetRequiredService<Momoka.AniBt.AniBtService>(), _moviePilot!,
            request.Services.GetRequiredService<Momoka.Shell.Platform.ISystemLauncher>());
        SearchSourceHost.Children.Add(panel);
        return _aniBtPanel = panel;
    }

    /// <summary>筛选面板：第一次打开筛选时才创建并放进浮层。</summary>
    private FilterPanel EnsureFilterPanel()
    {
        if (_filterPane is { } existing) return existing;

        var panel = new FilterPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(panel, "FilterPane");
        panel.Changed += OnFilterChanged;
        panel.CloseRequested += OnFilterCloseRequested;
        FilterOverlay.Children.Add(panel);
        _filterPane = panel;
        ApplyPanelSize();
        return panel;
    }

    /// <summary>设置面板：第一次点齿轮时才创建并放进浮层。</summary>
    private LibrarySettingsPanel EnsureSettingsPanel()
    {
        if (_settingsPane is { } existing) return existing;

        var panel = new LibrarySettingsPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetAutomationId(panel, "SettingsPane");
        panel.Changed += OnDisplayChanged;
        panel.CloseRequested += CloseSettings;
        SettingsOverlay.Children.Add(panel);
        _settingsPane = panel;
        ApplyPanelSize();
        return panel;
    }

    /// <summary>
    /// 换来源只换面板，不替用户搜一次。旧行为是拿框里现有的词直接发一趟搜索，用户 2026-10-15 报的就是
    /// 它（「切换时不要自动触发搜索」）—— 词换不换、搜不搜都归用户按回车决定。所以这里只剩三件事：切
    /// 显隐、收起旧请求、按新来源改占位文案；把光标交给搜索框那一手在
    /// <see cref="OnSourceTabsPointerReleased"/>。
    /// </summary>
    private void ApplySource()
    {
        CancelTyping();
        SearchSitePicker.ClosePopup();
        var source = SearchSource;
        var available = SearchSourceAvailable;

        // 先把要显示的那一格接上、再让别的收手。跨页跳来的自动搜索／字幕读取就在 Ensure 里消费 ——
        // 提前 Release 会把刚发出去的那一趟取消掉（来源标签加载后补发的 SelectionChanged 会再走一遍这里）。
        FrameworkElement? active = null;
        if (available)
        {
            active = source switch
            {
                1 => EnsureMoviePilotPanel(),
                2 => EnsureMoviePilotResources(),
                3 => EnsureMoviePilotSubtitles(),
                4 => EnsureAniBtPanel(),
                _ => null
            };
        }
        if (!ReferenceEquals(_moviePilotPanel, active)) _moviePilotPanel?.CancelSearch();
        if (!ReferenceEquals(_moviePilotResources, active)) _moviePilotResources?.Release();
        if (!ReferenceEquals(_moviePilotSubtitles, active)) _moviePilotSubtitles?.Release();
        if (!ReferenceEquals(_aniBtPanel, active)) _aniBtPanel?.Release();

        if (_moviePilotPanel is { } moviePilot)
            moviePilot.Visibility = ReferenceEquals(moviePilot, active) ? Visibility.Visible : Visibility.Collapsed;
        if (_moviePilotResources is { } resources)
            resources.Visibility = ReferenceEquals(resources, active) ? Visibility.Visible : Visibility.Collapsed;
        if (_moviePilotSubtitles is { } subtitles)
            subtitles.Visibility = ReferenceEquals(subtitles, active) ? Visibility.Visible : Visibility.Collapsed;
        if (_aniBtPanel is { } aniBt)
            aniBt.Visibility = ReferenceEquals(aniBt, active) ? Visibility.Visible : Visibility.Collapsed;
        EmbyContent.Visibility = source == 0 ? Visibility.Visible : Visibility.Collapsed;
        // MoviePilot 网页端上次搜索的结果就是网页「资源搜索结果」页打开时看到的那一份。这一来源刚露面、
        // 页上还没有结果、框里也没有待搜的词时把缓存铺出来；读取失败留在空态，不挡用户自己搜。
        if (available && source == 2 && _moviePilotResources is { } cached && ViewModel.SearchText.Trim().Length == 0)
            _ = cached.LoadCachedResultsAsync();
        SearchUnavailableSurface.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        SearchUnavailableTitle.Text = source == 4 && _settings?.Settings.MoviePilot.AniBtEnabled == false
            ? UiText.Translate("AniBT 尚未启用") : UiText.Translate("MoviePilot 尚未连接");
        SearchUnavailableMessage.Text = source == 4 && _settings?.Settings.MoviePilot.AniBtEnabled == false
            ? UiText.Translate("请在设置 → MoviePilot 中启用 AniBT；此入口会一直保留。")
            : UiText.Translate("请在设置 → MoviePilot 中启用并连接服务，然后在这里继续搜索。");
        ToolbarSurface.Visibility = _request?.IsSearch == true ? Visibility.Collapsed : ViewModel.ToolbarVisibility;
        SearchInput.PlaceholderText = source switch
        {
            1 => UiText.Translate("输入关键词，按回车搜索可订阅的影视"),
            2 => UiText.Translate("输入资源关键词，按回车搜索"),
            3 => UiText.Translate("输入片名，按回车查找影片，再点一部查看字幕"),
            4 => UiText.Translate("输入番剧名称，按回车搜索 AniBT"),
            _ => ViewModel.ExtendedSearchCategoriesVisibility == Visibility.Visible
                ? UiText.Translate("搜索影片、节目、人物、合集和播放列表") : UiText.Translate("搜索媒体库中的影片、节目和单集")
        };
        ApplyHeaderLayout();
        ViewModel.Cancel();
        ViewModel.NoticeOpen = false;
        if (_released) return;
        if (source == 0)
        {
            ViewModel.Resume(reload: false);
            ViewModel.SetEmbySourceActive(true);
        }
        else
        {
            ViewModel.SetEmbySourceActive(false);
        }
    }

    private void OnSearchCategoryChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_released && sender.SelectedItem?.Tag is string name && Enum.TryParse<LibrarySearchCategory>(name, out var category))
        {
            // 分类条首次显示也会补发选中事件；同一分类不能取消正在等待的输入查询。
            if (_request?.SearchCategory == category) return;
            CancelTyping();
            _ = ViewModel.ChooseSearchCategoryAsync(category);
        }
    }

    /// <summary>A view shape was picked out of the view menu. The Tag carries the enum name — the
    /// menu is markup, and an enum-typed Tag would need a converter either way.</summary>
    /// <summary>
    /// 齿轮打开了：把这一面墙现在的那一套摆上去，再把面板层显出来。每次打开都重摆一次 —— 面板是同一个
    /// 控件，换了库（电视剧 ↔ 电影）之后它还留着上一个库的勾选。
    /// </summary>
    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settings = EnsureSettingsPanel();
        settings.Show(ViewModel.Display, ViewModel.Section, ViewModel.SourceServerKind,
            ViewModel.IndexKind, ViewModel.CollectionType);
        SettingsOverlay.Visibility = Visibility.Visible;
        ApplyPanelSize();
        (_settingsScrimMotion ??= new SurfaceMotion(SettingsScrim)).Enter(150, 0);
        (_settingsMotion ??= new SurfaceMotion(settings)).Enter(200, 12);
    }

    /// <summary>面板上那颗 ✕，或者面板外面那片空位。收起面板层，回到原来的网格。</summary>
    internal void CloseSettings() => CloseSettings(animated: true);

    /// <summary>探针与页面释放用：不播淡出，立即收起。理由同 <see cref="CloseFilterImmediately"/>。</summary>
    internal void CloseSettingsImmediately() => CloseSettings(animated: false);

    private void CloseSettings(bool animated)
    {
        if (!animated)
        {
            _settingsScrimMotion?.Collapse();
            _settingsMotion?.Collapse();
            SettingsOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        (_settingsScrimMotion ??= new SurfaceMotion(SettingsScrim)).Exit(120, 0);
        if (_settingsPane is { } settings)
            (_settingsMotion ??= new SurfaceMotion(settings)).Exit(140, 8, () => SettingsOverlay.Visibility = Visibility.Collapsed);
        else SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>点面板外面。遮罩铺满整层，点它就是把面板收起来。</summary>
    private void OnSettingsScrimClicked(object sender, RoutedEventArgs e) => CloseSettings();

    /// <summary>
    /// 面板上改了一处。立刻生效，与网页端一致：视图模型重查一次，网格按新形状/新字段重建。
    /// </summary>
    private void OnDisplayChanged(LibraryDisplay display)
    {
        // 改图像大小、字段和分组也走这条通知；只有真的换了形状才值得补那一段整墙淡入。
        if (display.View != ViewModel.View) _revealViewSwitch = true;
        ViewModel.ApplyDisplay(display);
    }

    /// <summary>
    /// The letter bar was clicked. The view model answers with the row to land on — a count the server
    /// did, not a walk of rows this grid happens to hold — and this page does the scrolling, which is
    /// the one half the view model cannot do: how far down the list the user is belongs to the view.
    /// </summary>
    private async void OnAlphaJump(object? sender, string letter)
    {
        if (await ViewModel.JumpToAsync(letter).ConfigureAwait(true) is not { } jump) return;

        if (!_released && XamlRoot is not null)
        {
            Alpha.SetCurrent(jump.Letter);
            BringItemIntoView(jump.Index);
        }
    }

    /// <summary>
    /// Brings one item to the top of the viewport through <see cref="ItemsView"/>'s own layout. The
    /// uniform grid is free to change its column count and stretch its cells as the window resizes; asking
    /// the control to locate the item keeps column and row spacing in that calculation instead of trying
    /// to reproduce the layout's arithmetic here.
    /// </summary>
    private void BringItemIntoView(int index)
    {
        if (index < 0 || index >= ViewModel.Cards.Count) return;

        CardWall.StartBringItemIntoView(index, new BringIntoViewOptions
        {
            AnimationDesired = false,
            HorizontalAlignmentRatio = 0,
            VerticalAlignmentRatio = 0
        });
    }

    /// <summary>
    /// Lights the letter bar up for wherever the user has scrolled to. Called from the scroll event,
    /// cheaply: <see cref="ItemsView.TryGetItemIndex"/> asks the live layout which item is nearest the
    /// viewport's top-left corner, so responsive column counts, stretched cells and layout spacing are
    /// already reflected in the answer.
    /// </summary>
    private void SyncAlphaFromScroll()
    {
        if (_scroll is null || Alpha.Visibility != Visibility.Visible) return;
        if (!CardWall.TryGetItemIndex(0, 0, out var index)) return;

        Alpha.SetCurrent(ViewModel.CurrentLetterOf(index));
    }

    /// <summary>Applies the current view shape to the live controls: which template the grid draws
    /// with, which layout arranges those templates, and whether the letter bar can be seen. Called on
    /// navigation and on every change.</summary>
    /// <remarks>
    /// The layout has to move with the template. 海报 and 缩略图 are grids of cells and share one
    /// <c>UniformGridLayout</c> sized from the view model; 列表 is one row per line, which no cell width
    /// can express — see the two layouts in the page's resources.
    /// </remarks>
    private void ApplyView()
    {
        ViewApplies++;
        // 索引页（类型、播出平台）走的就是下面这套，没有专属分支：从 2026-10-18 起这两页的卡片就是普通
        // 海报卡，差别只在卡片自己身上 —— 方卡/宽卡的高宽比、居中的单行名字、留边不裁的台标，都由
        // CardItem 按 IndexKind 带着走（见 LibraryIndexCards 与 LibraryViewModel.NewCard）。
        //
        // 从前这两页有一个专属模板与专属版面（写死 170×72 的文字格）。把它们去掉不只是删旧代码：换页时
        // 换模板换版面会让 ItemsView 把整面墙重新实现一遍，而「临时换一份版面」正是 2026-10-18 那次
        // 格子宽度错乱（模型 342、屏上 422）的来源。现在四种视图之外的页共用同一份模板与版面，
        // 换页不再动它们，那条路径就不存在了。
        CardWall.ItemTemplate = ViewModel.View switch
        {
            LibraryView.Thumb or LibraryView.ThumbCard or LibraryView.Banner or LibraryView.Disc or LibraryView.Logo =>
                (DataTemplate)Resources["ThumbTemplate"],
            LibraryView.List => (DataTemplate)Resources["ListTemplate"],
            LibraryView.Table => (DataTemplate)Resources["TableRowTemplate"],
            _ => (DataTemplate)Resources["PosterTemplate"]
        };

        CardWall.Layout = ViewModel.View switch
        {
            LibraryView.List => (Layout)Resources["ListLayout"],
            LibraryView.Table => (Layout)Resources["TableLayout"],
            _ => CardLayout
        };

        // 卡片与格子共用 VM 给出的实际宽度；文件夹页保留整行居中，其余页从左排。
        var centerCards = ViewModel.Section == LibrarySection.Folders;
        CardLayout.ItemsStretch = UniformGridLayoutItemsStretch.None;
        CardLayout.ItemsJustification = centerCards
            ? UniformGridLayoutItemsJustification.Center : UniformGridLayoutItemsJustification.Start;

        // 表格的列头与每一行同一套宽度；换成别的视图就整行收起（一个像素都不占）。
        TableHeader.Visibility = ViewModel.View == LibraryView.Table ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.View == LibraryView.Table) BuildTableHeader();
        ApplyTableWidth();

        // 换模板/换版面会让 ItemsView 把整面墙重新实现一遍，屏幕上是瞬间跳变。只补一段很短的
        // 整墙淡入掩盖它：标记只由用户的视图入口设置，首次导航、恢复保存的视图与探针都不设，
        // 免得和浏览页淡入或几何量测叠在一起。关闭动画、离页、还没就绪都直接落定。
        var revealSwitch = _revealViewSwitch;
        _revealViewSwitch = false;
        if (revealSwitch && !_attaching && !_released && IsLoaded && ViewModel.IsReady
            && ViewModel.Cards.Count > 0 && CardWall.Visibility == Visibility.Visible)
            HomeMotion.Reveal(CardWall, 0, 120, 6);
    }





    /// <summary>
    /// 表格视图的横向滚动面。勾满字段时一行比页面宽，从前是从右边裁掉；现在画布跟着列宽走，多出来的
    /// 部分靠外面那层滚动器左右滚，列头在同一个画布里所以跟着走。
    /// <para>
    /// 其余视图把画布交回页面宽度（<c>NaN</c> ＝ 由布局拉伸），横向滚动关掉 —— 卡片墙的格子是按页面宽
    /// 算的，画布要是留着上一轮表格的宽度，格子就会按那个宽度铺。
    /// </para>
    /// </summary>
    private void ApplyTableWidth()
    {
        var table = ViewModel.View == LibraryView.Table && !ViewModel.IsBrowseIndex;
        TableScroll.HorizontalScrollMode = table ? ScrollMode.Enabled : ScrollMode.Disabled;
        TableScroll.HorizontalScrollBarVisibility = table ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        if (!table)
        {
            if (!double.IsNaN(TableCanvas.Width)) TableCanvas.Width = double.NaN;
            return;
        }

        var viewport = TableScroll.ActualWidth;
        if (viewport <= 0) return;

        var width = Math.Max(viewport, LibraryTableColumns.TotalWidth(ViewModel.Fields));
        if (double.IsNaN(TableCanvas.Width) || Math.Abs(TableCanvas.Width - width) > 0.5) TableCanvas.Width = width;
    }

    /// <summary>
    /// 表格视图的列头。列 = 勾上的字段（含标题），宽度取自 <see cref="LibraryTableColumns"/> ——
    /// 行里每一格用的也是同一份宽度，两处各写一遍的话第一列就会歪。
    /// </summary>
    private void BuildTableHeader()
    {
        var fields = ViewModel.Fields;
        TableHeader.ColumnDefinitions.Clear();
        TableHeader.Children.Clear();

        foreach (var field in fields)
        {
            TableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LibraryTableColumns.WidthOf(field)) });
            var label = new TextBlock
            {
                Text = LibraryDisplayCatalog.LabelOf(field),
                Style = (Style)Resources["TableHeaderTextStyle"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, TableHeader.ColumnDefinitions.Count - 1);
            TableHeader.Children.Add(label);
        }
    }
}

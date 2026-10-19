using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Momoka.Theming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell.Views;

/// <summary>飞牛「布局」浮层的一行：两项互斥，勾跟着当前视图走。</summary>
public sealed record LayoutEntry(string Label, bool IsChecked, bool Wide)
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
    private readonly SolidColorBrush _tabSelected = new();
    private readonly SolidColorBrush _tabForeground = new();

    /// <summary>
    /// 窗口停下来之后再去算这一面墙的格宽（见 <see cref="OnCardWallSizeChanged"/>）。拖动窗口时尺寸每帧都变，
    /// 每帧重造一遍卡片等于每帧给几十张封面各发一趟请求。
    /// </summary>
    private readonly DispatcherQueueTimer _gridResize;

    public static string HeaderTitle(Visibility search, string heading) => search == Visibility.Visible ? "搜索" : heading;

    public LibraryPage()
    {
        InitializeComponent();

        // 这一面墙一格多宽是按窗口宽度算的（见 LibraryViewModel.ResizeGrid）：窗口一停就把新宽度推过去。
        // 计时器在这里建，是因为「停下来」这件事只有页面知道 —— 尺寸变化本身不带「这是最后一帧」。
        _gridResize = DispatcherQueue.CreateTimer();
        _gridResize.Interval = TimeSpan.FromMilliseconds(120);
        _gridResize.IsRepeating = false;
        _gridResize.Tick += (_, _) => RebuildWall();

        // Before the first load can raise it. The view model asks for one more page when it has just
        // added one; whether the window is full is the one thing only the view can answer.
        ViewModel.PageArrived += PumpIfShort;

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

        // 面板上那颗 ✕：面板自己不认识宿主（它也能放进别处），收起由页面做。
        SettingsPane.CloseRequested += CloseSettings;

        // 换来源之后光标要能落在搜索框里：这一页唯一的输入口是那个框，而点标签会把焦点留在标签上，
        // 用户换完来源就打不了字、也按不出回车（2026-10-15 用户报的正是「按回车不触发搜索」）。只认
        // 指针点的那一下，键盘方向键在标签之间移动时不抢焦点，见 OnSourceTabsKeyDown。
        SourceTabs.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSourceTabsPointerReleased), handledEventsToo: true);
        SourceTabs.PreviewKeyDown += OnSourceTabsKeyDown;
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
        UpcomingScroll.SizeChanged += (_, _) => ViewModel.ResizeUpcoming(UpcomingScroll.ActualWidth);
        UpcomingScroll.ViewChanged += OnUpcomingViewChanged;
        FavoritesScroll.SizeChanged += (_, _) => ViewModel.ResizeFavorites(FavoritesScroll.ActualWidth);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void BuildBrowseSections()
    {
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
        if (e.PropertyName == nameof(LibraryViewModel.View)) ApplyView();
        if (e.PropertyName == nameof(LibraryViewModel.Fields)) ApplyTableWidth();
        if (e.PropertyName == nameof(LibraryViewModel.SectionVisibility))
        {
            BuildBrowseSections();
            ApplyView();
        }
        if (e.PropertyName is nameof(LibraryViewModel.UpcomingVisibility) or nameof(LibraryViewModel.FavoritesVisibility)) ApplyHeaderLayout();
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

        foreach (var view in Enum.GetValues<LibraryView>())
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
        // 格宽取视图模型算出来的那一份，不取版面的 MinItemWidth：2026-10-18 起版面的最小宽是「图像大小」那一档，
        // 摊出来的格宽比它宽一点（列数由版面定，剩下的宽度平摊给这几列）。行高同源。
        UniformGridLayout => (ViewModel.ViewLabel, nameof(UniformGridLayout), ViewModel.CellWidth, ViewModel.RowHeight),
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

        _request = request;
        _released = false;
        _navigation++;

        // The shell reads this back to restore its pane highlight after a Frame.GoBack.
        Tag = request.Tag;

        // The one place this page resolves anything. Deliberately a single block: it is the line that goes
        // when the shell stops handing a container around, and spreading it would turn one deletion into a
        // hunt.
        var services = request.Services;
        _settings = services.GetRequiredService<ISettingsService>();
        _session = services.GetRequiredService<EmbySession>();
        _capabilities = services.GetRequiredService<IServerCapabilities>();
        _actions = services.GetRequiredService<IShellActions>();

        _moviePilot = services.GetRequiredService<MoviePilotService>();
        _moviePilot.AvailabilityChanged += OnMoviePilotAvailabilityChanged;
        ViewModel.Attach(
            request,
            _actions,
            _settings,
            _session,
            services.GetRequiredService<EmbyImageStore>(),
            _capabilities, _moviePilot);

        // MoviePilot 那半的连接服务，交给面板（它自己接确认对话框）。只有搜索页且接了 MoviePilot 时那个分段
        // 才出现，但服务无条件拿——构造它不花什么，也省得这里再判一次设置。
        MoviePilotPanel.Attach(services.GetRequiredService<MoviePilotService>(),
            services.GetRequiredService<Momoka.Shell.Platform.ISystemLauncher>());
        MoviePilotResources.Attach(services.GetRequiredService<MoviePilotService>(),
            services.GetRequiredService<Momoka.Shell.Platform.ISystemLauncher>(), sharedInput: true);
        AniBtPanel.Attach(services.GetRequiredService<Momoka.AniBt.AniBtService>(), _moviePilot,
            services.GetRequiredService<Momoka.Shell.Platform.ISystemLauncher>());
        SearchCategories.SelectedItem = SearchCategories.Items.FirstOrDefault(item =>
            item.Tag is string value && value == request.SearchCategory.ToString()) ?? TopResultsCategory;

        // 每次进来都从「我的媒体库」开始：Emby 那半可见、MoviePilot 那半藏着。SelectedIndex 已是 0 时不触发
        // SelectionChanged，所以显式对一次账。
        SourceTabs.SelectedIndex = 0;
        ApplySource();

        ApplyView();
        BuildBrowseSections();

        _ = LoadNavigationAsync(request, _navigation);
    }

    private async Task LoadNavigationAsync(LibraryRequest request, int navigation)
    {
        var offset = request.ScrollOffset;
        var count = request.LoadedCount;
        var loading = ViewModel.ReloadAsync();
        var queryToken = ViewModel.QueryToken;
        await loading.ConfigureAwait(true);
        if (_released || navigation != _navigation || !ViewModel.QueryCurrent(queryToken)) return;
        if (count > 0 && !ViewModel.IsFavoritesPage) await ViewModel.LoadUntilAsync(count - 1).ConfigureAwait(true);
        if (_released || navigation != _navigation || !ViewModel.QueryCurrent(queryToken)) return;
        if (ViewModel.IsUpcomingPage)
        {
            UpcomingScroll.UpdateLayout();
            UpcomingScroll.ChangeView(null, offset, null, disableAnimation: true);
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
        _released = true;
        if (_moviePilot is { } moviePilot) moviePilot.AvailabilityChanged -= OnMoviePilotAvailabilityChanged;
        _moviePilot = null;
        CancelTyping();
        _navigation++;
        if (_request is { } request)
        {
            request.ScrollOffset = ContentScrollOffset;
            request.LoadedCount = ViewModel.Cards.Count;
        }
        ViewModel.Cancel();
        ViewModel.ReleaseSearchPosters();
        FilterPane.Dismiss();
        MoviePilotPanel.Release();
        MoviePilotResources.Release();
        AniBtPanel.Release();
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
        ApplyHeaderLayout();
        if (_request?.IsSearch == true) HomeMotion.Reveal(SearchSurface, 0, 220, 8);

        // The search page opens with an empty box and nothing to look at, so the caret belongs in the box.
        // Here rather than in OnNavigatedTo: nothing can take focus before it is in the tree.
        if (_request is { IsSearch: true } && ViewModel.SearchText.Length == 0)
            SearchInput.Focus(FocusState.Programmatic);

        if (_scroll is not null) return;

        // ItemsView exposes the scroller it owns; there is no other way to know how far down the list
        // the user is, and no other place to hang the paging trigger.
        _scroll = CardWall.ScrollView;

        if (_scroll is null)
        {
            Log.Warn(Category, "取不到 ItemsView.ScrollView，滚动分页将不可用");
            return;
        }

        _scroll.ViewChanged += OnViewChanged;
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
    /// 这一面墙的可用宽度变了：先把格宽、行高算出来（拖窗口时每帧都要算，这一步只算账），再等窗口停下来
    /// 重画卡片 —— 拖动时每帧重画一遍，等于每帧给几十张封面各发一趟请求，而解码宽度必须跟着画出来的宽度走，
    /// 不能不重画（见 <see cref="LibraryViewModel.RebuildGrid"/>）。
    /// </summary>
    private void OnWallWidthChanged()
    {
        ApplyWallMetrics();

        _gridResize.Stop();
        _gridResize.Start();
    }

    /// <summary>
    /// 窗口停下来了：按当前格宽重画卡片。卡片是原地换宽度的（不换对象、不动列表），所以滚动位置不用管。
    /// </summary>
    private void RebuildWall()
    {
        _gridResize.Stop();
        ViewModel.RebuildGrid();
    }

    /// <summary>
    /// 按可用宽度算这一面墙的格子（格宽、行高）。版面的 <c>MinItemWidth</c> 是档位宽、不随窗口变，它自己会在
    /// 可用尺寸变过时重算列数；格宽与行高按同一条算术跟着走（见 <see cref="LibraryCardMetrics.Fit"/>）。
    /// </summary>
    private void ApplyWallMetrics()
    {
        var width = TableScroll.ActualWidth;
        if (width <= 0 || !double.IsFinite(width)) return;
        ViewModel.ResizeGrid(width);
    }

    /// <summary>库名独占一行，分类与工具栏在下面分别居中；搜索保留自己的版式。</summary>
    private void ApplyHeaderLayout()
    {
        var width = HeaderLayout.ActualWidth;
        if (width <= 0) return;

        var search = _request?.IsSearch == true;
        var sectionLibrary = ViewModel.IsSectionLibrary;
        var contentPage = ViewModel.IsUpcomingPage || ViewModel.IsFavoritesPage;
        // 分区库（Emby／Jellyfin）的头部有一批只在特定状态下出现的行：浏览分组、筛选条、状态条、
        // 搜索分类栏。Grid 的 RowSpacing 连收起的行也照算，工具栏到封面因此空出 100 像素。
        // 这一档把这些间距改由元素自己的外边距表达：行收起就一个像素不占，显示时仍是原来的 12／16。
        // 飞牛的分区页不跟着改，按服务器分开处理。
        var headerByMargin = sectionLibrary && !contentPage && !ViewModel.IsFnNas;
        var contentGap = new Thickness(0, 20, 0, 0);
        PageRoot.Margin = sectionLibrary ? new Thickness(20) : (Thickness)Application.Current.Resources["EgPageMargin"];
        PageRoot.RowSpacing = search ? 8 : contentPage ? 4 : headerByMargin ? 0 : 20;
        HeaderGrid.RowSpacing = search || contentPage || headerByMargin ? 0 : 16;
        HeaderLayout.RowSpacing = search ? 16 : headerByMargin ? 0 : sectionLibrary ? 12 : 20;
        ((Grid)UpcomingScroll.Parent).RowSpacing = contentPage || headerByMargin ? 0 : 16;
        LibrarySectionsSurface.Margin = headerByMargin ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        ToolbarSurface.Margin = headerByMargin ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        LibraryBrowseGroup.Margin = headerByMargin ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        LibraryFilterChips.Margin = headerByMargin ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        EmbyContent.Margin = headerByMargin ? contentGap : new Thickness(0);
        LibraryBusyBar.Margin = headerByMargin ? contentGap : new Thickness(0);
        LibraryNoticeBar.Margin = headerByMargin ? contentGap : new Thickness(0);
        LibraryTitle.Visibility = Visibility.Visible;
        LibraryTitle.Style = (Style)Application.Current.Resources[sectionLibrary ? "EgSectionTitleStyle" : "EgPageTitleStyle"];
        LibrarySectionsSurface.MaxWidth = width;
        LibrarySectionsSurface.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(LibrarySectionsSurface, 1);
        Grid.SetColumn(LibrarySectionsSurface, 0);
        Grid.SetColumnSpan(LibrarySectionsSurface, 2);
        ToolbarSurface.HorizontalAlignment = search ? HorizontalAlignment.Right : sectionLibrary ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Toolbar.CenterRows = sectionLibrary;
        Grid.SetRow(SearchSurface, 1);
        Grid.SetRow(LibraryTitle, 0);
        Grid.SetColumnSpan(LibraryTitle, search ? 1 : 2);
        Grid.SetRow(ToolbarSurface, search ? 0 : sectionLibrary ? 2 : 1);
        LibraryTitle.MaxWidth = width;
        ToolbarSurface.MaxWidth = width;
        if (!search) return;

        var formWidth = Math.Min(900, width);
        SearchSurface.Width = formWidth;
        SearchSurface.MaxWidth = width;
        SearchInput.Width = Math.Max(0, formWidth - 22);
        SearchForm.ColumnSpacing = 0;
        SearchForm.RowSpacing = SourceTabs.Visibility == Visibility.Visible ? 8 : 0;
        foreach (var item in SourceTabs.Items.OfType<CommunityToolkit.WinUI.Controls.SegmentedItem>())
        {
            item.MaxWidth = Math.Max(0, (formWidth - 22) / Math.Max(1, SourceTabs.Items.Count));
            item.Padding = new Thickness(8, 4, 8, 4);
            if (item.Content is TextBlock label)
            {
                label.MaxWidth = Math.Max(0, item.MaxWidth - 18);
                label.Text = SourceTabs.Items.IndexOf(item) switch
                {
                    1 => formWidth < 520 ? "MP 订阅" : "MoviePilot 订阅",
                    2 => formWidth < 520 ? "MP 资源" : "MoviePilot 资源",
                    3 => "AniBT",
                    _ => "媒体库"
                };
            }
        }
    }

    private void ApplyPanelSize()
    {
        if (PageRoot.ActualWidth <= 0 || PageRoot.ActualHeight <= 0) return;
        var (width, height) = Momoka.Infrastructure.LibraryPanelSize.For(PageRoot.ActualWidth, PageRoot.ActualHeight);
        foreach (var pane in new FrameworkElement[] { FilterPane, SettingsPane })
        {
            pane.Width = width;
            pane.Height = height;
        }
    }

    /// <summary>
    /// The paging trigger. One viewport of slack, which is what keeps a fast scroll from ever reaching
    /// the bottom of what has been loaded.
    /// </summary>
    private void OnViewChanged(ScrollView sender, object args)
    {
        if (_removingCard) return;
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

    private void OnFavoriteMoreClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FavoriteRow row }) _ = ViewModel.LoadFavoriteRowAsync(row);
    }

    private void OnUpcomingRetryClicked(object sender, RoutedEventArgs e) => Reload();

    /// <summary>
    /// Asks for one more page when the last did not fill the window, and keeps the two play buttons
    /// and the letter bar's rule in step with the rows that just landed.
    /// </summary>
    private void PumpIfShort()
    {
        ViewModel.RowsArrived();
        SyncAlphaFromScroll();

        if (!ViewModel.HasMore) return;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_released || _removingCard || !ViewModel.HasMore) return;
            if (ViewModel.IsUpcomingPage)
            {
                if (UpcomingScroll.ScrollableHeight < UpcomingScroll.ViewportHeight) ViewModel.LoadMore();
            }
            else if (_scroll is { } scroll && scroll.ExtentHeight <= scroll.ViewportHeight + 1) ViewModel.LoadMore();
        });
    }

    private void OnItemInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is CardItem card) ViewModel.Open(card);
    }

    /// <summary>
    /// 推荐页那一排里的一张卡：点开这一项。和主页同一条路（<c>PosterCard.Card</c> 这条编译期通道），
    /// **不走 DataContext** —— ItemsRepeater + x:Bind 的模板实例上没人设过它，模式匹配必败、点击无声
    /// （2026-09-14 主页「继续观看点了没反应」就是漏在这一格）。
    /// </summary>
    private void OnShelfCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: PosterCard { Card: { } card } }) ViewModel.Open(card);
    }

    /// <summary>
    /// 需求 6：右键一张卡片。The list this grid is showing is handed along so 选集 works off it, exactly
    /// as it does for an invoke, and a metadata save reloads the page rather than patching the card —
    /// an edit can move the item's title, its artwork and its position in the sort all at once.
    /// </summary>
    private void OnCardContextRequested(UIElement sender, ContextRequestedEventArgs args) =>
        ItemCommands.Handle(_session, _actions, sender, args, ViewModel.Episodes(), Reload, RemovalCallback(), ViewModel.MarkedWatched);

    /// <summary>需求 6：the same commands from the strip that appears on a card under the pointer.</summary>
    private void OnCardActionRequested(object? sender, CardActionEventArgs args) =>
        ItemCommands.Handle(_session, _actions, sender, args, ViewModel.Episodes(), Reload, RemovalCallback(), ViewModel.MarkedWatched);

    private double ContentScrollOffset => ViewModel.IsUpcomingPage ? UpcomingScroll.VerticalOffset
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
        else if (ViewModel.IsFavoritesPage)
        {
            FavoritesScroll.UpdateLayout();
            FavoritesScroll.ChangeView(null, offset, null, disableAnimation: true);
        }
        else
        {
            CardWall.UpdateLayout();
            _scroll?.ScrollTo(0, offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
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

    /// <summary>Tooling: drops the sort menu open where a screenshot can see it.</summary>
    internal void ShowSortMenu() => SortButton.Flyout?.ShowAt(SortButton);

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

        OpenFilter();
    }

    private void OnFnNasFilterCloseClicked(object sender, RoutedEventArgs e) => ViewModel.CloseFnNasFilter();

    private void OnFnNasLayoutMenuOpening(object sender, object e)
    {
        // 每次打开重建，和排序菜单同一套：勾要跟着刚刚落下的那一次选择走。
        FnNasLayoutList.ItemsSource = new[]
        {
            new LayoutEntry("竖幅海报", !ViewModel.FnNasLayoutWide, false),
            new LayoutEntry("横幅海报", ViewModel.FnNasLayoutWide, true)
        };
    }

    private void OnFnNasLayoutItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LayoutEntry entry) return;
        ViewModel.SetFnNasLayout(entry.Wide);
        FnNasLayoutMenu.Hide();
    }

    /// <summary>
    /// 需求 5：fills the panel for this grid, then puts it on the page. Rebuilt on every open rather than
    /// once, for the reasons the sort menu is — and the server version in particular may still have been
    /// in flight when the page opened.
    /// </summary>
    internal void OpenFilter()
    {
        PrepareFilterPanel();
        if (_capabilities is null) return;

        FilterOverlay.Visibility = Visibility.Visible;
        ApplyPanelSize();
    }

    /// <summary>
    /// Rebuilds the panel without putting it on screen — what the self-check and the filter-changed path
    /// need. Showing it is <see cref="OpenFilter"/>'s half, and the two are apart so that reading the
    /// panel never leaves a modal layer over a page a screenshot is about to take.
    /// </summary>
    internal void PrepareFilterPanel()
    {
        if (_capabilities is null) return;

        FilterPane.Open(ViewModel.Filters, ViewModel.FilterPreset, _capabilities.ServerVersion, ViewModel.FetchFilterValuesAsync, ViewModel.SourceServerKind);
    }

    /// <summary>收起筛选层，回到原来的网格。</summary>
    internal void CloseFilter()
    {
        FilterOverlay.Visibility = Visibility.Collapsed;
        FilterPane.Dismiss();
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
    internal bool FilterTemplatesResolve => FilterPane.EveryTemplateResolves;

    /// <summary>自检：the view shape in force, by name, and whether the letter bar is showing.</summary>
    internal string ViewName => ViewModel.View.ToString();

    internal bool AlphaShown => Alpha.Visibility == Visibility.Visible;

    private void OnFilterChanged(object? sender, EventArgs e) => ViewModel.ApplyFilters();

    /// <summary>
    /// 搜索页's own box. Handed to the view model rather than turned into a navigation: this is already
    /// the right page, and one back-stack entry per word typed is not a history anyone wants to walk.
    /// <para>
    /// 按当前来源分流：在「MoviePilot」那一段时把词交给那半的面板，否则照旧搜 Emby 库。
    /// </para>
    /// </summary>
    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e) =>
        _ = SubmitSearchAsync(e.QueryText ?? sender.Text ?? string.Empty);

    internal Task SubmitSearchAsync(string text)
    {
        CancelTyping();
        return RunSearchAsync(text, remember: true);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && args.CheckCurrent()
            && !string.Equals(sender.Text, ViewModel.SearchText, StringComparison.Ordinal))
            _ = SearchTypingAsync(sender.Text ?? string.Empty);
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
            if (SearchSource == 1) await MoviePilotPanel.PrepareSearchAsync().ConfigureAwait(true);
            else if (SearchSource == 3) await AniBtPanel.SearchAsync("").ConfigureAwait(true);
            else await MoviePilotResources.SearchAsync("").ConfigureAwait(true);
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

    private Task RunSearchAsync(string text, bool remember)
    {
        if (_released) return Task.CompletedTask;
        ViewModel.SearchText = text;
        return SearchSource switch
        {
            1 => MoviePilotPanel.SearchAsync(text),
            2 => MoviePilotResources.SearchAsync(text),
            3 => AniBtPanel.SearchAsync(text),
            _ => ViewModel.SearchAsync(text, remember)
        };
    }

    private int SearchSource => SourceTabs.Visibility == Visibility.Visible ? Math.Max(0, SourceTabs.SelectedIndex) : 0;

    private void CancelTyping()
    {
        _typing?.Cancel();
        _typing = null;
    }

    private void OnSourceChanged(object sender, SelectionChangedEventArgs e) => ApplySource();

    /// <summary>
    /// 指针点了来源标签：把光标交回搜索框。点击把焦点留在标签上，而用户接下来要么打新词、要么按回车
    /// 用现有的词搜 —— 两件事都要求焦点在框里。挂 <c>PointerReleased</c> 而不是无条件抢：键盘方向键
    /// 在标签之间移动时焦点本来就该留在标签上（见 <see cref="OnSourceTabsKeyDown"/>）。
    /// </summary>
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
        if (_released || e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        _ = SubmitSearchAsync(SearchInput.Text ?? string.Empty);
    }

    private MoviePilotService? _moviePilot;

    private void OnMoviePilotAvailabilityChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_released || !ReferenceEquals(sender, _moviePilot)) return;
        ViewModel.RefreshMoviePilotAvailability();
        if (_moviePilot?.Available != true && SourceTabs.SelectedIndex != 0)
        {
            SourceTabs.SelectedIndex = 0;
        }
    });

    /// <summary>
    /// 换来源只换面板，不替用户搜一次。旧行为是拿框里现有的词直接发一趟搜索，用户 2026-10-15 报的就是
    /// 它（「切换时不要自动触发搜索」）—— 词换不换、搜不搜都归用户按回车决定。所以这里只剩三件事：切
    /// 显隐、收起旧请求、按新来源改占位文案；把光标交给搜索框那一手在
    /// <see cref="OnSourceTabsPointerReleased"/>。
    /// </summary>
    private void ApplySource()
    {
        CancelTyping();
        var source = SearchSource;
        MoviePilotPanel.Release();
        MoviePilotResources.Release();
        AniBtPanel.Release();
        MoviePilotPanel.Visibility = source == 1 ? Visibility.Visible : Visibility.Collapsed;
        MoviePilotResources.Visibility = source == 2 ? Visibility.Visible : Visibility.Collapsed;
        AniBtPanel.Visibility = source == 3 ? Visibility.Visible : Visibility.Collapsed;
        EmbyContent.Visibility = source == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolbarSurface.Visibility = _request?.IsSearch == true ? Visibility.Collapsed : ViewModel.ToolbarVisibility;
        SearchInput.PlaceholderText = source switch
        {
            1 => "输入关键词，按回车搜索可订阅的影视",
            2 => "输入资源关键词，按回车搜索",
            3 => "输入番剧名称，按回车搜索 AniBT",
            _ => "搜索媒体库中的影片、节目和单集"
        };
        ApplyHeaderLayout();
        ViewModel.Cancel();
        ViewModel.NoticeOpen = false;
        if (_released) return;
        if (source == 0) ViewModel.Resume(reload: false);
        if (source == 1) MoviePilotPanel.LoadSources();
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
        SettingsPane.Show(ViewModel.Display, ViewModel.Section, ViewModel.IndexKind, ViewModel.CollectionType);
        SettingsOverlay.Visibility = Visibility.Visible;
        ApplyPanelSize();
    }

    /// <summary>面板上那颗 ✕，或者面板外面那片空位。收起面板层，回到原来的网格。</summary>
    internal void CloseSettings() => SettingsOverlay.Visibility = Visibility.Collapsed;

    /// <summary>点面板外面。遮罩铺满整层，点它就是把面板收起来。</summary>
    private void OnSettingsScrimClicked(object sender, RoutedEventArgs e) => CloseSettings();

    /// <summary>
    /// 面板上改了一处。立刻生效，与网页端一致：视图模型重查一次，网格按新形状/新字段重建。
    /// </summary>
    private void OnDisplayChanged(LibraryDisplay display) => ViewModel.ApplyDisplay(display);

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
            LibraryView.Thumb or LibraryView.Banner or LibraryView.Disc or LibraryView.Logo =>
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

        // 表格的列头与每一行同一套宽度；换成别的视图就整行收起（一个像素都不占）。
        TableHeader.Visibility = ViewModel.View == LibraryView.Table ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.View == LibraryView.Table) BuildTableHeader();
        ApplyTableWidth();
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

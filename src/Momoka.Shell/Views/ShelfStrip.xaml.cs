using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Infrastructure;
using Windows.System;

namespace Momoka.Shell.Views;

/// <summary>
/// 一条横向卡片带，翻页而不是拖着滚动条自由滑。主页的每个分区、详情页的 单集、全部剧季、演职人员、更多类似
/// 都是这个控件。
/// <para>
/// 三处以前各写一遍同样的 <c>ScrollView</c> + <c>ItemsRepeater</c>，底下各挂一条水平滚动条。要看后面的
/// 卡片就得把指针放到那条几像素高的条上拖 —— 「下方拖动的这种不是很好用」。现在滚动条藏掉，指针进到带上
/// 时两边浮出箭头，一次翻整整一屏卡片。主页另外可以按设置开启「拖动封面翻页」：按住封面拖动时内容跟手，
/// 松手落到整页位置，见 <see cref="DragPagingProperty"/> 与 Core 的 <see cref="CardStrip.DragTargetFor"/>。
/// </para>
/// <para>
/// 翻多远是这里唯一算得上规则的东西，所以它不在这个文件里：四条规则（一屏放得下几张卡就翻几张、翻过去
/// 落在哪、哪个箭头该在屏上、要让第几张卡露出来得滚到哪儿）都在 Core 的
/// <see cref="Momoka.Infrastructure.CardStrip"/> 上，由单元测试钉着。这里剩下的是量：视口多宽、卡片
/// 间距多大、还有多少可滚 —— 只有屏上问得出来，所以 <see cref="Probe"/> 只问这些。
/// </para>
/// </summary>
public sealed partial class ShelfStrip : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(object),
        typeof(ShelfStrip),
        new PropertyMetadata(null, OnItemsChanged));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(ShelfStrip),
        new PropertyMetadata(null, OnTemplateChanged));

    public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
        nameof(ItemSpacing),
        typeof(double),
        typeof(ShelfStrip),
        new PropertyMetadata(16d, OnSpacingChanged));

    public static readonly DependencyProperty PictureHeightProperty = DependencyProperty.Register(
        nameof(PictureHeight), typeof(double), typeof(ShelfStrip),
        new PropertyMetadata(0d, (owner, _) => ((ShelfStrip)owner).SyncBand()));

    /// <summary>显式图片高度供可换行标题的货架定位翻页圆钮；普通货架保留原文字高度估算。</summary>
    public double PictureHeight
    {
        get => (double)GetValue(PictureHeightProperty);
        set => SetValue(PictureHeightProperty, value);
    }

    public static readonly DependencyProperty FocusIndexProperty = DependencyProperty.Register(
        nameof(FocusIndex),
        typeof(int),
        typeof(ShelfStrip),
        new PropertyMetadata(0, OnFocusIndexChanged));

    /// <summary>
    /// 这一带压在一张剧照上，而不是站在页面那张纸上 —— 集页把同季那一带摆在头图底下的画面里
    /// （见 <c>DetailPage.PlaceEpisodes</c>）。这里只负责把它传给每张卡，见
    /// <see cref="PosterCard.OnScrim"/>。
    /// </summary>
    public static readonly DependencyProperty OnScrimProperty = DependencyProperty.Register(
        nameof(OnScrim),
        typeof(bool),
        typeof(ShelfStrip),
        new PropertyMetadata(false, OnScrimSwitched));

    /// <summary>
    /// 这一排能不能用鼠标拖动翻页。主页按设置 → 主页 → 主页版面的开关交给每一排；详情页的几条带不设，
    /// 保持原样（只翻页箭头、键盘和触摸屏平移）。规则在 Core 的 <see cref="CardStrip.DragTargetFor"/>。
    /// </summary>
    public static readonly DependencyProperty DragPagingProperty = DependencyProperty.Register(
        nameof(DragPaging),
        typeof(bool),
        typeof(ShelfStrip),
        new PropertyMetadata(false, OnDragPagingChanged));

    /// <summary>
    /// 箭头只盖卡片的图片，不盖图片下面那两行字 —— 带的高度里有 <see cref="CardSize.Chrome"/> 是那两行
    /// 的。这个下限管的是还没量过的那一瞬间：高度是 0 时按 0 减出来是负数，按钮会直接消失。
    /// </summary>
    private const double MinBandHeight = 48;

    /// <summary>指针在带上。箭头是悬停才出现的，同卡片上那排按钮。</summary>
    private bool _hover;
    private bool _live;
    private Storyboard? _prevAnimation;
    private Storyboard? _nextAnimation;

    /// <summary>
    /// 「鼠标移出窗口后不会自动恢复」：指针到底还在不在带上，按 OS 说的算，不光信 <c>PointerExited</c>。
    /// 为什么事件不够用，见 <see cref="HoverWatch"/>。
    /// </summary>
    private readonly HoverWatch _watch;

    /// <summary>拖动翻页按住的指针；不在拖动候选里时为 null。只有鼠标左键会进这条路。</summary>
    private Pointer? _dragPointer;

    /// <summary>已经按下、还在等方向明确：横向拖过 <see cref="DragStartDistance"/> 才开始跟手。</summary>
    private bool _dragCandidate;

    /// <summary>已经越过起始阈值、由这条带接管指针，内容正跟着指针走。</summary>
    private bool _dragActive;

    /// <summary>按下那一点在 <see cref="Root"/> 里的位置，拖动距离都相对它量。</summary>
    private Windows.Foundation.Point _dragOrigin;

    /// <summary>按下那一刻的滚动位置，松手不够远就回到这里。</summary>
    private double _dragStartOffset;

    /// <summary>横向拖过这么多才开始翻页。比它小的抖动留给卡片自己的点击。</summary>
    private const double DragStartDistance = 8;

    /// <summary>
    /// 上一次量到的卡片间距（卡片宽 + 间隔）。记下来而不是每次现问：翻到后面时第一张卡已经被虚拟化掉，
    /// <c>TryGetElement(0)</c> 会返回空，而那时正是最需要知道一页多宽的时候。
    /// </summary>
    private double _pitch;

    /// <summary>自检用：最近一次翻页请求的目标位置，-1 表示还没翻过。</summary>
    private double _requested = -1;

    /// <summary>
    /// 还没落实的定位请求（第几张卡），-1 表示没有。
    /// <para>
    /// 记着而不是当场滚过去：<see cref="FocusOn"/> 是数据刚填进来的那一刻叫的，那时带上一张卡都还没长出
    /// 来，量不到卡片宽度也就算不出位置。带或卡片行的尺寸一变（正是卡片长出来的那一刻）再试一次。
    /// </para>
    /// </summary>
    private int _focus = -1;

    /// <summary>
    /// 这一条定位请求是不是「把第几张对到左边沿」（<see cref="JumpTo"/>）。<c>FocusIndex</c> 的 0 是
    /// 「别动」，而换季点第一季要的正是「回到开头」—— 两件事都落在同一个索引上，只能另记一位。
    /// </summary>
    private bool _align;

    /// <summary>
    /// 最近一次落在这条带里的焦点项。横向 <see cref="ItemsRepeater"/> 是虚拟化的：当方向键把焦点推到
    /// 视口边缘时，下一张卡可能还没有容器，不能只依赖 WinUI 的候选查找。记下索引后，
    /// <see cref="OnNoFocusCandidateFound"/> 可以创建目标容器、让它进视口，再把焦点交给它。
    /// </summary>
    private int _focusedIndex = -1;

    /// <summary>
    /// 视口左沿现在是第几张卡。详情页的集带拿它把标题切到正在看的那一季（2026-11-03 用户令：
    /// 「集列表翻页翻到第x季第1集或者最后一集时上方要自动切换为对应季」）。-1 表示还没量过。
    /// </summary>
    private int _leading = -1;

    /// <summary>视口左沿换到了另一张卡。只在索引真的变了时发一次，滚动一帧来十几次也不重复问页面。</summary>
    internal event Action<int>? LeadingChanged;
    internal event Action? ReadingChanged;

    /// <summary>正在等待布局完成后重试的焦点索引，-1 表示没有。</summary>
    private int _pendingFocusedIndex = -1;

    /// <summary>防止同一轮布局里重复排队焦点重试。</summary>
    private bool _focusRetryQueued;
    private bool _applyingFocusRetry;
    private int _focusGeneration;
    private int _focusScrollCorrelation = -1;
    private FrameworkElement? _focusReadyElement;
    private UIElement? _focusBringElement;

    public ShelfStrip()
    {
        InitializeComponent();

        _watch = new HoverWatch(Root, up =>
        {
            _hover = up;
            SyncArrows();
        });

        SizeChanged += OnContentResized;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Unloaded += OnDragUnloaded;
        Scroller.ScrollCompleted += OnFocusScrollCompleted;
        Scroller.BringingIntoView += OnFocusBringingIntoView;
        Root.BringIntoViewRequested += OnFocusBringRequested;

        // 拖动翻页（设置 → 主页）：卡片自己的 Button 会先把按下标掉并捕获指针，所以三个事件都按
        // handledEventsToo 挂；真正开始拖动时才把捕获从卡片手里接过来，没拖够的按下仍是一次普通点击。
        Root.AddHandler(PointerPressedEvent, new PointerEventHandler(OnDragPressed), true);
        Root.AddHandler(PointerMovedEvent, new PointerEventHandler(OnDragMoved), true);
        Root.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnDragReleased), true);
        Root.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnDragCanceled), true);
        Root.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnDragCaptureLost), true);
    }

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>卡片之间的间隔。翻页的步长按它算，所以它不只是 StackLayout 的一个数。</summary>
    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>
    /// 开场先看第几张卡。详情页的 单集 带绑它：在某一集自己的页面上，这条带列的是这一集的邻居，那就该
    /// 从这一集开始，而不是让人从第一集翻过来。0 是「别动」—— 见 <see cref="ApplyFocus"/>。
    /// <para>
    /// 做成依赖属性而不是让页面去订阅 ViewModel 的 PropertyChanged：绑定写在 XAML 里，「这条带看哪张卡」
    /// 就和「这条带装哪些卡」并排摆着，读一眼就完；而卡片还没长出来时定位落实不了，
    /// <see cref="_focus"/> 那套延迟重试本来就在这个控件里。
    /// </para>
    /// </summary>
    public int FocusIndex
    {
        get => (int)GetValue(FocusIndexProperty);
        set => SetValue(FocusIndexProperty, value);
    }

    private StackLayout Rows => (StackLayout)Repeater.Layout;

    /// <inheritdoc cref="OnScrimProperty"/>
    public bool OnScrim
    {
        get => (bool)GetValue(OnScrimProperty);
        set => SetValue(OnScrimProperty, value);
    }

    /// <inheritdoc cref="DragPagingProperty"/>
    public bool DragPaging
    {
        get => (bool)GetValue(DragPagingProperty);
        set => SetValue(DragPagingProperty, value);
    }

    /// <summary>
    /// 这一档交给每张卡（<see cref="PosterCard.OnScrim"/>）。卡片是回收着用的，所以两处都要：这里过一遍
    /// 已经建出来的，<see cref="OnElementPrepared"/> 管之后建出来的和回收回来的那些。
    /// </summary>
    private static void OnScrimSwitched(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var strip = (ShelfStrip)sender;

        for (var index = 0; index < strip.ItemCount; index++)
            if (strip.Repeater.TryGetElement(index) is { } element) strip.PaintInk(element);
    }

    /// <summary>开关关掉那一刻如果正按着拖，先把内容送回起点再放手，不留半页。</summary>
    private static void OnDragPagingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is false) ((ShelfStrip)sender).CancelDrag();
    }

    /// <summary>
    /// 一张卡在这条带里该用哪套墨。卡在模板里，所以从容器往下找 —— 先看逻辑内容那一路
    /// （<c>ContentControl.Content</c>），因为刚建出来的容器还没套上自己的模板，那一刻可视树里一个孩子都没有：
    /// 只走可视树的那一版会漏掉除第一张之外的每一张卡，屏上是浅色主题下几张卡的片名整行消失。
    /// </summary>
    private void PaintInk(DependencyObject container)
    {
        foreach (var card in CardsIn(container)) card.OnScrim = OnScrim;
    }

    internal static IEnumerable<PosterCard> CardsIn(DependencyObject node)
    {
        if (node is PosterCard card) { yield return card; yield break; }
        if (node is ContentControl { Content: DependencyObject content })
            foreach (var inside in CardsIn(content)) yield return inside;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var child in CardsIn(VisualTreeHelper.GetChild(node, index))) yield return child;
    }

    /// <summary>
    /// 过一遍这条带里已经建出来的卡。详情页高对比度换墨用这一支：整页视觉树递归每天会被滚动里每建出来
    /// 一张卡触发一次，走的却是页面上所有控件；带子自己知道哪些容器真的在。
    /// </summary>
    internal void ForEachRealizedCard(Action<PosterCard> apply)
    {
        var count = ItemCount;
        for (var index = 0; index < count; index++)
            if (Repeater.TryGetElement(index) is { } container && CardIn(container) is { } card)
                apply(card);
    }

    /// <summary>
    /// 某张卡此刻在这条带里的元素；被虚拟化掉的那些没有容器，返回 null —— 那里本来也没有可动的像素。
    /// 页面拿它做删除时的离场动画（<c>HomePage.RemoveAsync</c>）。
    /// </summary>
    internal PosterCard? CardElement(CardItem card)
    {
        var count = ItemCount;
        for (var index = 0; index < count; index++)
        {
            if (Repeater.TryGetElement(index) is not { } container) continue;
            if (CardIn(container) is { } poster && ReferenceEquals(poster.Card, card)) return poster;
        }

        return null;
    }

    /// <summary>
    /// 往下找这张卡：先看逻辑内容那一路（<c>ContentControl.Content</c>），因为刚建出来的容器还没套上自己的
    /// 模板，那一刻可视树里一个孩子都没有：只走可视树的那一版会漏掉除第一张之外的每一张卡。
    /// </summary>
    private static PosterCard? CardIn(DependencyObject? node)
    {
        switch (node)
        {
            case null: return null;
            case PosterCard card: return card;
            case ContentControl { Content: DependencyObject content } when CardIn(content) is { } inside:
                return inside;
        }

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            if (CardIn(VisualTreeHelper.GetChild(node, index)) is { } inside) return inside;

        return null;
    }

    // ---- 规则 -------------------------------------------------------------------
    //
    // 翻多远、落在哪、哪个箭头该在屏上、要让第几张卡露出来得滚到哪儿 —— 四条规则都在 Core 的
    // Momoka.Infrastructure.CardStrip 上，由单元测试钉着（CardStripTests）。留在这里的是量：视口多宽、
    // 卡片间距多大、还有多少可滚，只有屏上问得出来。

    /// <summary>
    /// 翻一页并返回请求的位置。<paramref name="direction"/> 是 -1 或 1。
    /// <para>
    /// 请求的位置记在 <see cref="_requested"/> 上：<c>ScrollTo</c> 是带动画的，问完之后立刻去读
    /// <c>HorizontalOffset</c> 读到的还是动画起点，而自检要问的正是「点下去有没有要到那个位置」。
    /// </para>
    /// </summary>
    private double Page(int direction)
    {
        var scrollable = Scroller.ScrollableWidth;
        var target = CardStrip.TargetFor(Scroller.HorizontalOffset, direction, Step, scrollable);

        _requested = target;

        // 没有余量就不去请求。翻不动的时候箭头本来就是收起的，所以这条只在一种情况下管事：自检里这份控件
        // 还没进过树，视口和内容都是 0，而 ScrollTo 要的是模板里那个 ScrollPresenter。
        if (scrollable > 0)
            Scroller.ScrollTo(target, Scroller.VerticalOffset,
                new ScrollingScrollOptions(HomeMotion.AnimationsEnabled ? ScrollingAnimationMode.Auto : ScrollingAnimationMode.Disabled));

        return target;
    }

    // ---- 拖动翻页 ---------------------------------------------------------------
    //
    // 主页那几排按设置里的开关可以按住封面拖动：内容跟手走，松手按 CardStrip.DragTargetFor 落到整页位置。
    // 触摸和触控笔仍由 ScrollView 自己的平移接管，这里只认鼠标左键 —— 两种输入各走各的，不会互相抢指针。
    //
    // Gallery 对照（microsoft/WinUI-Gallery @ 6de91feccadfbb0a796b71caedead5a2284f730e，
    // WinUIGallery/Samples/ScrollView/ScrollViewPage.xaml.cs）：官方样例只演示 ScrollTo 与缩放，没有鼠标左键
    // 拖动平移；Learn「Scroll viewer controls」把 panning 定义为触摸／笔，鼠标走滚轮或滚动条（2026-11-23 读）。
    // 没有可照抄的样例，因此沿用项目自己的指针捕获写法（ShellPage.Sidebar 的拖动条、PlayerPage.Input 的拖动），
    // 只把翻页落点放进 Core 的 CardStrip 由单测钉住。

    /// <summary>
    /// 按下：只登记候选，不捕获指针，也不把事件标掉。卡片自己的 Button 因此照旧收到这次按下，没拖动的
    /// 点击仍然是一次点击；箭头上的按下不算，否则点翻页会先被当成一次拖动。
    /// </summary>
    private void OnDragPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!DragPaging || e.Pointer.PointerDeviceType is not Microsoft.UI.Input.PointerDeviceType.Mouse) return;

        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed || Scroller.ScrollableWidth <= CardStrip.Edge) return;
        if (FromArrow(e.OriginalSource as DependencyObject)) return;

        ResetDrag();
        _dragPointer = e.Pointer;
        _dragCandidate = true;
        _dragOrigin = point.Position;
        _dragStartOffset = Scroller.HorizontalOffset;
    }

    /// <summary>
    /// 移动：横向拖过 <see cref="DragStartDistance"/> 才接管指针并让内容跟手。竖向的拖动不是翻页，
    /// 一旦方向明确就放弃这一次候选，把这一下手势还给页面和卡片。
    /// </summary>
    private void OnDragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragCandidate || !ReferenceEquals(e.Pointer, _dragPointer)) return;

        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) { CancelDrag(); return; }

        var dx = point.Position.X - _dragOrigin.X;
        if (!_dragActive)
        {
            if (Math.Abs(dx) < DragStartDistance) return;
            if (Math.Abs(dx) <= Math.Abs(point.Position.Y - _dragOrigin.Y)) { ResetDrag(); return; }
            _dragActive = TryCaptureDragPointer(e.Pointer, e.OriginalSource as DependencyObject);
            if (!_dragActive) { ResetDrag(); return; }
        }

        var target = Math.Clamp(_dragStartOffset - dx, 0, Scroller.ScrollableWidth);
        e.Handled = true;
        if (Math.Abs(target - Scroller.HorizontalOffset) < 0.5) return;
        Scroller.ScrollTo(target, Scroller.VerticalOffset,
            new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }

    /// <summary>
    /// 松手：拖够了就按方向和距离翻页，没拖够就回原位。落地动画与箭头一致（跟随「减少动画」偏好）。
    /// </summary>
    private void OnDragReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragCandidate || !ReferenceEquals(e.Pointer, _dragPointer)) return;
        if (!_dragActive) { ResetDrag(); return; }

        var target = CardStrip.DragTargetFor(
            _dragStartOffset, e.GetCurrentPoint(Root).Position.X - _dragOrigin.X, Step, Scroller.ScrollableWidth);
        _requested = target;
        Scroller.ScrollTo(target, Scroller.VerticalOffset,
            new ScrollingScrollOptions(HomeMotion.AnimationsEnabled ? ScrollingAnimationMode.Auto : ScrollingAnimationMode.Disabled));

        var pointer = _dragPointer;
        ResetDrag();
        if (pointer is not null) Root.ReleasePointerCapture(pointer);
        e.Handled = true;
    }

    private void OnDragCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCandidate && ReferenceEquals(e.Pointer, _dragPointer)) CancelDrag();
    }

    /// <summary>
    /// 这一排自己丢了指针捕获才算中断。卡片自己的捕获被这里接手时也会来一发，认它就会把刚开始的拖动
    /// 当场取消 —— 所以只认 <see cref="Root"/> 作为来源的那一条。
    /// </summary>
    private void OnDragCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragCandidate || !ReferenceEquals(e.Pointer, _dragPointer)) return;
        if (!ReferenceEquals(e.OriginalSource, Root)) return;
        CancelDrag();
    }

    /// <summary>单独挂一条离树收尾：焦点那一段的 OnUnloaded 会被 UiMotion 原样抽走，拖动状态不属于那一摊。</summary>
    private void OnDragUnloaded(object sender, RoutedEventArgs e)
    {
        ResetDrag();
        Root.ReleasePointerCaptures();
    }

    /// <summary>拖动被打断（开关关掉、捕获丢失、换了一批卡）：回到按下时的位置，别把这一排留在半页上。</summary>
    private void CancelDrag()
    {
        if (!_dragCandidate && !_dragActive) return;

        var restore = _dragActive ? _dragStartOffset : double.NaN;
        var pointer = _dragPointer;
        ResetDrag();
        if (pointer is not null) Root.ReleasePointerCapture(pointer);
        if (!double.IsFinite(restore)) return;

        _requested = Math.Clamp(restore, 0, Scroller.ScrollableWidth);
        Scroller.ScrollTo(_requested, Scroller.VerticalOffset,
            new ScrollingScrollOptions(HomeMotion.AnimationsEnabled ? ScrollingAnimationMode.Auto : ScrollingAnimationMode.Disabled));
    }

    private void ResetDrag()
    {
        _dragPointer = null;
        _dragCandidate = false;
        _dragActive = false;
    }

    /// <summary>
    /// 把指针从按下路径上的卡片按钮手里接过来。按下那一刻 Button 已经捕获了指针，而
    /// <c>CapturePointer</c> 在指针已被别人捕获时会直接返回 false（Learn，2026-11-23 读），所以先沿
    /// 原始来源往上逐个松手，再由这一排捕获。松不开就放弃这一次拖动，交给原来的按钮。
    /// </summary>
    private bool TryCaptureDragPointer(Pointer pointer, DependencyObject? source)
    {
        if (Root.CapturePointer(pointer)) return true;

        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not UIElement element) continue;
            element.ReleasePointerCapture(pointer);
            if (Root.CapturePointer(pointer)) return true;
        }

        return false;
    }

    /// <summary>这一次按下是不是落在两边那两颗翻页圆钮上：那两颗自己管翻页，不该再被拖一次。</summary>
    private bool FromArrow(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is Button button && (ReferenceEquals(button, Prev) || ReferenceEquals(button, Next))) return true;

        return false;
    }

    /// <summary>一页多远，按当前视口和量到的卡片间距。</summary>
    private double Step =>
        CardStrip.StepFor(Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : ActualWidth, Measure(), ItemSpacing);

    /// <summary>
    /// 把第 <paramref name="index"/> 张卡定位到屏上。<see cref="FocusIndex"/> 一变就叫，也就是数据刚填进来
    /// 的那一刻，落实不了就记下来等布局 —— 见 <see cref="_focus"/>。
    /// </summary>
    internal void FocusOn(int index)
    {
        _focus = index;
        _align = false;
        ApplyFocus();
    }

    /// <summary>
    /// 把第 <paramref name="index"/> 张卡对到视口左边沿。与 <see cref="FocusOn"/> 分开：后者的 0 是
    /// 「别动」，而换季点到第一季时要的正是回到开头；落点在第一屏里也照滚，因为用户要的是「第一张就是它」。
    /// </summary>
    internal void JumpTo(int index)
    {
        if (index < 0) return;

        _focus = index;
        _align = true;
        ApplyFocus();
    }

    /// <summary>
    /// 落实一次定位请求。卡片还没长出来（量不到间距，或还没有可滚动的余量）就留着请求不动，下一次尺寸
    /// 变化再来 —— 那时卡片正好长出来了。落实过就把请求清掉，否则人手动翻页之后会被拽回去。
    /// </summary>
    private void ApplyFocus()
    {
        if (_focus < 0 || (_focus == 0 && !_align))
        {
            _focus = -1;
            _align = false;
            return;
        }

        var pitch = Measure();
        if (pitch <= 0 || Scroller.ScrollableWidth <= 0) return;

        ScrollToIndex(_focus, _align);
        _focus = -1;
        _align = false;
    }

    /// <summary>
    /// 直接滚到第 <paramref name="index"/> 张卡并返回请求的位置。不带动画：这是「页面打开时就该在这里」，
    /// 不是一次人做出来的翻页。整张露着就不动的规则见 <see cref="CardStrip.OffsetFor"/>；换季要的左边沿
    /// 落点见 <see cref="CardStrip.StartFor"/>。
    /// </summary>
    private double ScrollToIndex(int index, bool align)
    {
        var viewport = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : ActualWidth;
        var target = align
            ? CardStrip.StartFor(index, Measure(), Scroller.ScrollableWidth)
            : CardStrip.OffsetFor(index, Measure(), viewport, Scroller.ScrollableWidth);

        _requested = target;

        // 回到第 0 张也是真滚：换季换到第一季时，带子多半已经不在开头，跳过 0 就只记了个请求。
        if (target > 0 || Scroller.ScrollableWidth > 0)
            Scroller.ScrollTo(target, Scroller.VerticalOffset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));

        return target;
    }

    /// <summary>
    /// 卡片间距：已实现卡片的宽度加一个间隔。首项被虚拟化后仍可量到缩窗后的新宽度。
    /// </summary>
    private double Measure()
    {
        var count = VisualTreeHelper.GetChildrenCount(Repeater);
        for (var index = 0; index < count; index++)
        {
            if (VisualTreeHelper.GetChild(Repeater, index) is not FrameworkElement item
                || Repeater.GetElementIndex(item) < 0 || item.ActualWidth <= 0) continue;
            _pitch = item.ActualWidth + ItemSpacing;
            break;
        }

        return _pitch;
    }

    private void SyncArrows()
    {
        var arrows = CardStrip.ArrowsFor(Scroller.HorizontalOffset, Scroller.ScrollableWidth, _hover);

        AnimateArrow(Prev, PrevMotion, arrows.Prev);
        AnimateArrow(Next, NextMotion, arrows.Next);
    }

    /// <summary>48px 圆形按钮始终放在封面中线，不覆盖标题和续播信息。</summary>
    private void SyncBand()
    {
        var band = PictureHeight > 0 ? PictureHeight
            : Math.Max(MinBandHeight, ActualHeight - CardSize.Chrome - Scroller.Padding.Top);
        var top = Scroller.Padding.Top + (band - MinBandHeight) / 2;

        Prev.Height = Next.Height = MinBandHeight;
        Prev.Margin = new Thickness(8, top, 0, 0);
        Next.Margin = new Thickness(0, top, 8, 0);
    }

    private void AnimateArrow(Button button, TranslateTransform shift, bool show)
    {
        // ViewChanged 一帧会来多次，相同目标不重复启动动画。
        if (button.IsHitTestVisible == show) return;

        var fromOpacity = button.Opacity;
        var fromX = shift.X;
        var hiddenX = ReferenceEquals(button, Prev) ? -8 : 8;
        var wasHidden = button.Visibility == Visibility.Collapsed;
        SetArrowAnimation(button, null);
        button.IsHitTestVisible = show;

        void Settle()
        {
            button.Opacity = show ? 1 : 0;
            shift.X = show ? 0 : hiddenX;
            button.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!_live || XamlRoot is null || !HomeMotion.AnimationsEnabled)
        {
            Settle();
            return;
        }

        button.Visibility = Visibility.Visible;
        var board = new Storyboard();
        Add(button, "Opacity", wasHidden ? 0 : fromOpacity, show ? 1 : 0);
        Add(shift, "X", wasHidden ? hiddenX : fromX, show ? 0 : hiddenX);
        SetArrowAnimation(button, board);
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(ArrowAnimation(button), board)) return;
            Settle();
            SetArrowAnimation(button, null);
        };
        board.Begin();

        void Add(DependencyObject target, string path, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, path);
            board.Children.Add(animation);
        }
    }

    private Storyboard? ArrowAnimation(Button button) => ReferenceEquals(button, Prev) ? _prevAnimation : _nextAnimation;

    private void SetArrowAnimation(Button button, Storyboard? animation)
    {
        ArrowAnimation(button)?.Stop();
        if (ReferenceEquals(button, Prev)) _prevAnimation = animation;
        else _nextAnimation = animation;
    }

    // ---- 事件 -------------------------------------------------------------------

    private void OnPrevClicked(object sender, RoutedEventArgs e) => Page(-1);

    private void OnNextClicked(object sender, RoutedEventArgs e) => Page(1);

    /// <summary>
    /// 焦点落到横带里的卡片（包括海报内部的播放/已看/收藏按钮）时，把那张卡横着露出来。仅在焦点确实属于
    /// Repeater 项时记录索引；翻页箭头是鼠标可点但不应加入焦点历史。
    /// <para>
    /// 露出这件事在这条带自己的滚动视图里做完，一句 <c>BringIntoView</c> 都不往外发 —— 「点击主页继续观看、
    /// 媒体库、最近添加的封面之后会先跳转到页面下方，然后才会进入页面」正是往外发的那一版，理由和这一版的规则
    /// 都在 <see cref="CardStrip.RevealFor"/> 上。
    /// </para>
    /// </summary>
    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (FindRepeaterElement(e.OriginalSource as DependencyObject) is not { } element) return;

        var index = Repeater.GetElementIndex(element);
        if (index < 0) return;

        _focusedIndex = index;
        _pendingFocusedIndex = -1;
        _focusRetryQueued = false;
        _focusScrollCorrelation = -1;
        _focusGeneration++;
        ClearFocusReadyElement();
        _focusBringElement = null;

        BringFocusedElementIntoView(element);
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not FrameworkElement element) return;

        // Item containers are supplied by the DataTemplate (normally a Button). Keep the accessibility
        // tree useful even when the control is virtualized: Narrator can announce both the item's place
        // in this shelf and the total count without forcing every card to be realized.
        AutomationProperties.SetPositionInSet(element, args.Index + 1);
        AutomationProperties.SetSizeOfSet(element, ItemCount);

        // 这一带压在剧照上时（集页那一带）卡片底下那两行字要换墨。放在这里而不是只在开关翻转时过一遍：
        // 容器是回收着用的，翻页翻出来的那些是「之后才建的」，漏掉就会有几张卡的字读不出来。
        PaintInk(element);
        ReadingChanged?.Invoke();
    }

    /// <summary>
    /// ItemsRepeater 不提供默认的键盘/遥控器交互策略。通常由 XYFocus 找到下一张卡；当下一张卡被
    /// 虚拟化掉时会走到这里。只接管「带内仍有目标」的左右移动，真正到达带头/带尾时保持未处理，
    /// 让外层页面有机会把焦点移到下一块内容。
    /// </summary>
    private void OnNoFocusCandidateFound(UIElement sender, NoFocusCandidateFoundEventArgs e)
    {
        if (_focusedIndex < 0 || e.Direction is not (FocusNavigationDirection.Left or FocusNavigationDirection.Right))
            return;

        var target = e.Direction == FocusNavigationDirection.Left ? _focusedIndex - 1 : _focusedIndex + 1;
        if (target < 0 || target >= ItemCount) return;

        e.Handled = true;
        FocusItem(target);
    }

    /// <summary>
    /// Home/End 是电视遥控器和键盘用户寻找横带两端的快捷路径；PageUp/PageDown 则按一整页移动，
    /// 并把焦点放在新视口里的第一/最后一张卡。普通左右键仍由 XYFocus 负责，避免每次移动都打断
    /// WinUI 自己的焦点动画。
    /// </summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_focusedIndex < 0 || ItemCount == 0) return;

        switch (e.Key)
        {
            case VirtualKey.Home:
                e.Handled = true;
                FocusItem(0);
                break;
            case VirtualKey.End:
                e.Handled = true;
                FocusItem(ItemCount - 1);
                break;
            case VirtualKey.PageUp:
                e.Handled = true;
                FocusByPage(-1);
                break;
            case VirtualKey.PageDown:
                e.Handled = true;
                FocusByPage(1);
                break;
        }
    }

    /// <summary>页面被移除时清掉虚拟化容器引用，避免下一次挂回页面时把焦点送回旧数据。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 和 PosterCard/CardArtwork 一样：同一详情面板移位会迟到投递 Unloaded，Loaded未必再来。
        // 当前仍有XamlRoot就是重挂后的活动实例，不清掉真实焦点请求或监听；真离树才执行收尾。
        _live = XamlRoot is not null;
        if (_live) return;
        _focusedIndex = -1;
        _pendingFocusedIndex = -1;
        _focusRetryQueued = false;
        _focusScrollCorrelation = -1;
        ClearFocusReadyElement();
        _focusBringElement = null;
        _focusGeneration++;

        // 带自己离开了树，指针在不在它上面已经无所谓了 —— 留着的话十赫兹那一拍还会继续问一个量不到的矩形。
        _watch.Leave();
        SetArrowAnimation(Prev, null);
        SetArrowAnimation(Next, null);
        Prev.Opacity = Next.Opacity = 0;
        Prev.Visibility = Next.Visibility = Visibility.Collapsed;
        Prev.IsHitTestVisible = Next.IsHitTestVisible = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _live = true;
        // Loaded 可能发生在数据和首轮布局之后；若有排队请求，马上再试一次。
        ApplyPendingFocusedItem();
    }

    /// <summary>
    /// 当前焦点落到带内的一张卡上时，把它横着露出来 —— 在这条带自己的滚动视图里做完，见
    /// <see cref="CardStrip.RevealFor"/>。
    /// <para>
    /// 位置按索引算，不按元素量：这里最要紧的一次调用来自 <see cref="FocusItem"/>，那时目标容器可能是
    /// <c>GetOrCreateElement</c> 刚建出来的，还没量过，问它自己在哪儿问到的是 0。一条带里的卡是同一个模板，
    /// 所以第 n 张的左边沿就是 n 个间距 —— 那个数现在就算得出来。
    /// </para>
    /// </summary>
    private int BringFocusedElementIntoView(UIElement element)
    {
        if (!IsLoaded || element.XamlRoot is null)
        {
            QueueFocusRetry(_focusedIndex);
            return -1;
        }

        var index = Repeater.GetElementIndex(element);
        var viewport = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : ActualWidth;
        var target = CardStrip.RevealFor(
            index, Measure(), ItemSpacing, Scroller.HorizontalOffset, viewport, Scroller.ScrollableWidth);

        if (target >= 0)
            return Scroller.ScrollTo(target, Scroller.VerticalOffset,
                new ScrollingScrollOptions(HomeMotion.AnimationsEnabled ? ScrollingAnimationMode.Auto : ScrollingAnimationMode.Disabled));
        return -1;
    }

    /// <summary>
    /// 将焦点交给指定索引。GetOrCreateElement 是 ItemsRepeater 为虚拟化项提供的官方入口，创建后
    /// 先 BringIntoView 再 Focus，确保手柄/键盘焦点不会落在视口外的不可见容器上。
    /// </summary>
    private void FocusItem(int index)
    {
        if (!_live || index < 0 || index >= ItemCount) return;

        _focusGeneration++;
        _pendingFocusedIndex = -1;
        _focusRetryQueued = false;
        _focusScrollCorrelation = -1;
        ClearFocusReadyElement();
        _focusBringElement = null;
        TryFocusItem(index);
    }

    private void TryFocusItem(int index)
    {
        if (!_live || index < 0 || index >= ItemCount) return;
        var generation = _focusGeneration;
        _focusedIndex = index;
        var unrealized = Repeater.TryGetElement(index) is null;

        UIElement? element;
        try
        {
            element = Repeater.TryGetElement(index) ?? Repeater.GetOrCreateElement(index);
        }
        catch (Exception)
        {
            QueueFocusRetry(index);
            return;
        }

        if (element is null)
        {
            QueueFocusRetry(index);
            return;
        }

        // Gallery ItemsRepeaterPage.xaml.cs v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7:
        // GetOrCreateElement 后交焦点；Learn 的恢复位置示例先 UpdateLayout，让新容器取得实际位置。
        // 仍只滚本条横带，不把 StartBringIntoView 冒泡给页面的纵向滚动。
        if (element is FrameworkElement prepared) prepared.UpdateLayout();
        if (!_live || generation != _focusGeneration) return;
        _pendingFocusedIndex = -1;
        if (unrealized)
        {
            // GetOrCreateElement 的临时锚点要经过正式 bring 协议才能保持实现范围；单独ScrollTo后立刻
            // Focus(true)可能只给未Loaded的临时Button，下一布局回收它并把焦点挪到旧视口。
            _pendingFocusedIndex = index;
            WatchFocusReadyElement(element as FrameworkElement);
            _focusBringElement = element;
            element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            QueueFocusRetry(index);
            return;
        }
        _focusScrollCorrelation = BringFocusedElementIntoView(element);
        if (element is FrameworkElement { IsLoaded: false } || _focusScrollCorrelation >= 0)
        {
            WatchFocusReadyElement(element as FrameworkElement);
            QueueFocusRetry(index);
            return;
        }

        // Realised 元素通常可以立即接收焦点；若仍在布局中，下一次布局再试。Focus 返回 false 而不是抛异常，
        // 所以这里必须检查返回值。
        var focused = element.Focus(FocusState.Keyboard);
        if (!_live || generation != _focusGeneration) return;
        if (!focused)
        {
            WatchFocusReadyElement(element as FrameworkElement);
            QueueFocusRetry(index);
        }
        else
        {
            _focusScrollCorrelation = -1;
            ClearFocusReadyElement();
            _focusBringElement = null;
        }
    }

    private void FocusByPage(int direction)
    {
        var step = Step;
        var targetOffset = CardStrip.TargetFor(Scroller.HorizontalOffset, direction, step, Scroller.ScrollableWidth);

        if (step <= 0 || Scroller.ScrollableWidth <= 0)
        {
            FocusItem(direction < 0 ? 0 : ItemCount - 1);
            return;
        }

        // 由当前滚动位置反推一个近似索引，再用实际容器的索引修正。即使卡片宽度尚未量到，
        // FocusItem 也会在下一轮布局重试，因此不会把焦点丢到带外。
        var pitch = Measure();
        var estimate = pitch > 0
            ? (int)Math.Round(targetOffset / pitch, MidpointRounding.ToZero)
            : (direction < 0 ? _focusedIndex - 1 : _focusedIndex + 1);

        estimate = Math.Clamp(estimate, 0, ItemCount - 1);
        FocusItem(estimate);
    }

    private int ItemCount => Repeater.ItemsSourceView?.Count ?? 0;

    /// <summary>从 GotFocus 的原始元素向上找出 ItemsRepeater 的实际项容器。</summary>
    private UIElement? FindRepeaterElement(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is UIElement element && Repeater.GetElementIndex(element) >= 0) return element;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void QueueFocusRetry(int index)
    {
        if (!_live || index < 0 || index >= ItemCount) return;

        _pendingFocusedIndex = index;
        if (_focusRetryQueued || _applyingFocusRetry) return;

        _focusRetryQueued = true;
        var generation = _focusGeneration;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (generation != _focusGeneration || !_live) return;
            _focusRetryQueued = false;
            ApplyPendingFocusedItem();
        })) _focusRetryQueued = false;
    }

    private void ApplyPendingFocusedItem()
    {
        if (!_live || _applyingFocusRetry) return;
        var index = _pendingFocusedIndex;
        if (index < 0 || index >= ItemCount)
        {
            _pendingFocusedIndex = -1;
            return;
        }

        _pendingFocusedIndex = -1;
        // 若仍不可聚焦，等本次滚动完成或目标布局就绪；不在同一轮低优先级回调里自排队。
        _applyingFocusRetry = true;
        try { TryFocusItem(index); }
        finally { _applyingFocusRetry = false; }
    }

    private void OnFocusScrollCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
    {
        if (!_live || args.CorrelationId != _focusScrollCorrelation || _pendingFocusedIndex < 0) return;
        _focusScrollCorrelation = -1;
        QueueFocusRetry(_pendingFocusedIndex);
    }

    private void OnFocusBringingIntoView(ScrollView sender, ScrollingBringingIntoViewEventArgs args)
    {
        if (_focusBringElement is { } target && ReferenceEquals(args.RequestEventArgs.TargetElement, target))
            _focusScrollCorrelation = args.CorrelationId;
    }

    private void OnFocusBringRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        // Repeater/ScrollPresenter 先完成本条横带的bring；Root截住继续冒泡，主页和详情正文不跟着纵滚。
        if (_focusBringElement is null) return;
        args.Handled = true;
        _focusBringElement = null;
    }

    private void WatchFocusReadyElement(FrameworkElement? element)
    {
        if (element is null || (element.IsLoaded && element.XamlRoot is not null && element.ActualWidth > 0 && element.ActualHeight > 0)) return;
        if (ReferenceEquals(element, _focusReadyElement)) return;
        ClearFocusReadyElement();
        _focusReadyElement = element;
        element.Loaded += OnFocusElementLoaded;
        element.LayoutUpdated += OnFocusElementLayoutUpdated;
    }

    private void OnFocusElementLoaded(object sender, RoutedEventArgs args) => RetryReadyFocusedElement();

    private void OnFocusElementLayoutUpdated(object? sender, object args) => RetryReadyFocusedElement();

    private void RetryReadyFocusedElement()
    {
        if (_focusReadyElement is not { IsLoaded: true, XamlRoot: not null, ActualWidth: > 0, ActualHeight: > 0 }) return;
        ClearFocusReadyElement();
        if (_pendingFocusedIndex >= 0) QueueFocusRetry(_pendingFocusedIndex);
    }

    private void ClearFocusReadyElement()
    {
        if (_focusReadyElement is not { } element) return;
        _focusReadyElement = null;
        element.Loaded -= OnFocusElementLoaded;
        element.LayoutUpdated -= OnFocusElementLayoutUpdated;
    }

    /// <summary>
    /// 指针进到带上。<c>PointerMoved</c> 也接到这里：带是数据到了之后才长出卡片的，指针一直停在原处不动
    /// 的话 <c>PointerEntered</c> 早就过去了，而 <c>PointerMoved</c> 是下一次挪动的第一手消息。
    /// </summary>
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _watch.Enter();

    /// <summary>
    /// 指针离开。位置要现查一遍，同 <see cref="PosterCard.OnPointerExited"/>：这个事件会从子元素冒上来，
    /// 而指针踩到刚浮出来的箭头上算「离开了带上的那张卡」，直接信它就会把手底下的按钮收掉。
    /// <para>
    /// 也只是快路径：指针从带上直接移出窗口时，这个事件报的位置还在带里，收不掉的那一次交给
    /// <see cref="HoverWatch"/>。
    /// </para>
    /// </summary>
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root).Position;

        if (point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight)
            return;

        _watch.Leave();
    }

    private void OnViewChanged(ScrollView sender, object e)
    {
        SyncArrows();
        RaiseLeading();
        ReadingChanged?.Invoke();
    }

    /// <summary>
    /// 视口左沿落在第几张卡：卡片等宽，位置就是「偏移 ÷ 间距」。取整向下 —— 半张露在左边沿时，
    /// 那半张仍是这一屏的第一张（标题该说它所属的那一季），不是被它盖住的那一张。
    /// </summary>
    private void RaiseLeading()
    {
        var pitch = Measure();
        if (pitch <= 0 || ItemCount == 0) return;

        var index = Math.Clamp((int)Math.Floor(Scroller.HorizontalOffset / pitch + 0.01), 0, ItemCount - 1);
        if (index == _leading) return;

        _leading = index;
        LeadingChanged?.Invoke(index);
    }

    /// <summary>
    /// 带、视口或卡片行的尺寸变了。三个来源都接同一个处理器，因为要重算的东西是一样的：箭头多高（带的
    /// 高度变了）、还能不能翻（窗口宽了之后可能已经一屏放得下）。
    /// </summary>
    private void OnContentResized(object sender, SizeChangedEventArgs e)
    {
        SyncBand();
        SyncArrows();

        // 卡片刚长出来的那一刻也是这里：ApplyFocus 之前算不出位置，现在算得出。
        ApplyFocus();
        ApplyPendingFocusedItem();
        RaiseLeading();
    }

    private static void OnItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var strip = (ShelfStrip)sender;

        strip.Repeater.ItemsSource = args.NewValue;

        // 换了一批卡片，上一批量出来的间距不作数了 —— 主页每条带的卡片形状可以不一样（海报 2:3、剧照
        // 16:9），而这个控件是同一个。
        strip.ResetDrag();
        strip.Root.ReleasePointerCaptures();
        strip._pitch = 0;
        strip._focusedIndex = -1;
        strip._pendingFocusedIndex = -1;
        strip._focusRetryQueued = false;
        strip._focusScrollCorrelation = -1;
        strip.ClearFocusReadyElement();
        strip._focusBringElement = null;
        strip._focusGeneration++;
        // 换了一批卡，上一批的定位请求（尤其是换季的强制左对齐）不该落到新列表上。
        strip._focus = -1;
        strip._align = false;
        strip._leading = -1;
        strip.SyncArrows();
    }

    private static void OnTemplateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ShelfStrip)sender).Repeater.ItemTemplate = args.NewValue;

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var strip = (ShelfStrip)sender;

        strip.Rows.Spacing = (double)args.NewValue;
        strip._pitch = 0;
    }

    private static void OnFocusIndexChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ShelfStrip)sender).FocusOn((int)args.NewValue);

    // ---- 自检 -------------------------------------------------------------------

    /// <summary>
    /// 这条带只在屏上才问得出来的那几件：这份标记解析得了、间隔真进了布局、还没量过时两个箭头是收着的、点下
    /// 去和「让第几张露出来」这两条路真的把要到的位置记下来了。
    /// <para>
    /// 翻多远、落在哪、哪个箭头该在屏上、露出第几张卡要滚到哪儿 —— 这四条规则从前也在这里，拿几个写死的数走
    /// 一遍。现在它们在 Core 的 <see cref="CardStrip"/> 上，由 <c>CardStripTests</c> 钉着：同样的数字，钉在一
    /// 个每次构建都跑、不用开窗口的地方。留在这儿的是那几个数拿不到的东西。
    /// </para>
    /// <para>
    /// 顺带证明的那件事同样值钱：<c>new ShelfStrip()</c> 会把这份标记解析一遍，里面每个资源键（
    /// <c>DefaultButtonStyle</c>、<c>EgOnScrimBrush</c>、那六个改掉的按钮状态键）解析不了就在这里抛，而
    /// 不是等到用户第一次划过某条带。
    /// </para>
    /// </summary>
    internal static (bool Ok, string Detail) Probe()
    {
        var strip = new ShelfStrip { ItemSpacing = 14 };
        var spacing = Math.Abs(strip.Rows.Spacing - 14) < 0.01;

        // 还没量过、指针也不在上面：两个箭头都该是收起的，高度退到下限而不是负数。
        strip.SyncBand();
        strip.SyncArrows();
        var quiet = !strip.PrevShown && !strip.NextShown && strip.Prev.Height >= MinBandHeight;

        // 点下去这条路本身：Page 记下要到的位置，而这一份控件没有余量可翻，所以要到的位置是 0。
        var clicked = strip.Page(1) == 0 && strip.Requested == 0;

        // 定位请求同理：这一份控件量不到卡片，所以请求留着等布局，而不是当场滚到 0 把它吞掉。
        strip.FocusOn(5);
        var pending = strip.PendingFocus == 5;

        var ok = spacing && quiet && clicked && pending;

        return (ok,
            $"标记解析通过；间隔 {strip.Rows.Spacing:0} 已进布局；还没量过时两个箭头收起、高 "
                + $"{strip.Prev.Height:0}；翻页要到 {strip.Requested:0}；定位第 6 张的请求留着（"
                + $"{strip.PendingFocus}）等布局。四条算术规则见 CardStripTests");
    }

    /// <summary>自检用：<c>Visibility</c> 是这条规则唯一看得见的结果。</summary>
    internal bool PrevShown => Prev.Visibility == Visibility.Visible;

    internal bool NextShown => Next.Visibility == Visibility.Visible;

    /// <summary>自检用：最近一次翻页要到的位置。</summary>
    internal double Requested => _requested;

    /// <summary>自检用：带子此刻真实停在哪里（换季回到第一季时，光看请求位置分不出它有没有真滚）。</summary>
    internal double Offset => Scroller.HorizontalOffset;

    /// <summary>自检用：还没落实的定位请求，-1 表示没有。<see cref="FocusIndex"/> 是要到第几张，这是还没到的那一张。</summary>
    internal int PendingFocus => _focus;
}

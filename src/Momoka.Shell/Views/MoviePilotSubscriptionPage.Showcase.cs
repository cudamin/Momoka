using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Windows.UI;

namespace Momoka.Shell.Views;

/// <summary>
/// 订阅详情页的大窗模式（2026-11-03 用户令「给 mp 订阅页面也增强大窗模式」）。
/// <para>
/// 判据与电影／剧／季详情页的沉浸档同一套（<see cref="DetailHero.SubscriptionImmersive"/>）：窗口铺开
/// （最大化，或客户区面积占所在显示器工作区五分之四以上）、资料卡能并排的宽版式、背后有那张图。
/// 成立时正文那张不透明的纸换成尾部那道半透明洗色，面板的底与框淡到 0，头部不再撑第一屏，下方组件
/// 整体上移、直接压在固定的剧照上；背景图从页顶铺到窗口下沿并略微更不透明，图下沿的黑色渐变收起。
/// </para>
/// <para>
/// 换向那一下走 260ms 的过渡：头部下限是布局属性（只能一帧一帧重排），纸面颜色与面板透明度是画刷上的
/// 依赖动画（不写 <c>EnableDependentAnimation</c> 会被静默丢掉），三样同一段时间、同一条缓动；每块正文
/// 面板再各带一支 16px 的浮起位移，按 <see cref="DetailHero.FloatOrder"/> 错峰 —— 与详情页那一支是同一
/// 种手法，理由写在那里（<c>DetailPage.Showcase.cs</c>）。
/// </para>
/// </summary>
public sealed partial class MoviePilotSubscriptionPage
{
    /// <summary>这一趟走多久 —— 与详情页同一档（<c>DetailPage.ShowcaseSpan</c>）。</summary>
    private static readonly TimeSpan ShowcaseSpan = TimeSpan.FromMilliseconds(260);

    /// <summary>单块正文面板浮起那一段 —— 比整体短，只管块与块之间那点差额。</summary>
    private static readonly TimeSpan FloatSpan = TimeSpan.FromMilliseconds(180);

    /// <summary>相邻两块相隔多久 —— 四块排满时整趟收在 300ms。</summary>
    private static readonly TimeSpan FloatStagger = TimeSpan.FromMilliseconds(40);

    /// <summary>起步时每块面板偏开多少 —— 只改渲染变换，不动布局。</summary>
    private const double FloatOffset = 16;

    /// <summary>正文那张纸的底色，构造函数里换成这一支（沉浸档要把它淡成半透明洗色，不能改全局画刷）。</summary>
    private readonly SolidColorBrush _sheetBrush = new();

    /// <summary>正文四块面板的底与框 —— 沉浸档里淡到 0。</summary>
    private readonly SolidColorBrush _panelFill = new();
    private readonly SolidColorBrush _panelLine = new();

    /// <summary>这一趟的 Storyboard；换向或收尾时整份停掉。</summary>
    private Storyboard? _showcaseBoard;

    /// <summary>窗口上一次被读到的「铺开了没有」—— 已经落到版面上的那一个。</summary>
    private bool _spacious;

    /// <summary>这一刻量到的铺开与否；拖动中它先变、<see cref="_spacious"/> 等手停下来再跟上。</summary>
    private bool _pendingSpacious;

    /// <summary>上一次读到的最大化那一位 —— 它翻的时候是用户按了窗口按钮，当场落定。</summary>
    private bool _maximized;

    /// <summary>拖边／贴靠时等手停下来的那一拍（同详情页，180ms）。</summary>
    private DispatcherQueueTimer? _spaciousWatch;

    /// <summary>已经排了一次「布局落定之后再应用」的活。</summary>
    private bool _showcaseQueued;

    /// <summary>正在把窗口形态写进视图模型 —— 这一写引发的通知不能当成「别的来路」当场落定。</summary>
    private bool _applyingShowcase;

    /// <summary>这一页已经排过第一次版；在那之前翻面是版面落位，不插动画。</summary>
    private bool _showcaseSettled;

    /// <summary>版面上当前这一档是谁 —— 重复通知靠它挡住重复的落定与动画。</summary>
    private bool _shownShowcase;

    /// <summary>上一次落定时是不是宽版式 —— 宽窄之间的翻面不插过渡，那是换了整套版面。</summary>
    private bool _settledWide;

    /// <summary>这一页实际走了几次大窗过渡 —— 探针读它证明动画真的走过，而不只是把终点写上去。</summary>
    internal int ShowcaseAnimations { get; private set; }

    /// <summary>过渡还在走 —— 探针等它落定之后再读纸面、面板与头部那几个数。</summary>
    internal bool ShowcaseAnimating => _showcaseBoard is not null;

    /// <summary>页面挂上的那扇窗口，由外壳递进来。</summary>
    private Windowing.HostWindow? _window;

    /// <summary>
    /// 四块正文面板，从上往下：季、演职员、相关推荐、相似作品。它们在标记里都挂着
    /// <c>EgBodyPanelStyle</c>，这里把底色和描边换成自己持有的画刷，好让透明度能动画。
    /// </summary>
    private IEnumerable<StackPanel> BodyPanels => [SeasonPanel, PeoplePanel, RecommendationsPanel, SimilarPanel];

    /// <summary>
    /// 构造时准备三支画刷与四支浮起变换，并给背景图定下常态那一档不透明度。都在
    /// <c>InitializeComponent</c> 之后、上树之前做完 —— 第一帧就是对的。
    /// </summary>
    private void PrepareShowcase()
    {
        _sheetBrush.Color = PaperColor;
        BodySheet.Background = _sheetBrush;
        DetailBackdropBand.Opacity = DetailHero.BackdropOpacity;

        RefreshPanelBrushes();
        foreach (var panel in BodyPanels)
        {
            panel.Background = _panelFill;
            panel.BorderBrush = _panelLine;
            panel.RenderTransform = new TranslateTransform();
        }
    }

    /// <summary>
    /// 这一页上树／离树时挂上与摘下主题订阅。与详情页同一支写法：页面实例可能被复用，挂一次、走时解掉；
    /// 先减再加，重复上树也只挂一支。
    /// </summary>
    private void WatchShowcaseTheme()
    {
        Loaded += (_, _) =>
        {
            ThemeHost.Changed -= OnShowcaseThemeChanged;
            ThemeHost.Changed += OnShowcaseThemeChanged;
        };
        Unloaded += (_, _) => ThemeHost.Changed -= OnShowcaseThemeChanged;
    }

    /// <summary>换了主题：面板那两档颜色跟着换；纸面在还原档也要换（沉浸档它一直是尾部那道半透明黑，与主题无关）。</summary>
    private void OnShowcaseThemeChanged(Momoka.Theming.UiTheme theme)
    {
        RefreshPanelBrushes();
        if (!MediaViewModel.Showcase) _sheetBrush.Color = PaperColor;
    }

    private void RefreshPanelBrushes()
    {
        _panelFill.Color = BrushColor("EgSurfaceAltBrush");
        _panelLine.Color = BrushColor("EgBorderBrush");
    }

    /// <summary>主题里那支窗口色 —— 正文那张纸原本的底。</summary>
    private static Color PaperColor => BrushColor("EgWindowBrush");

    private static Color BrushColor(string key) => Application.Current is { } app
        && app.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush
        ? brush.Color
        : key switch
        {
            "EgWindowBrush" => Color.FromArgb(255, 0x0C, 0x11, 0x16),
            "EgSurfaceAltBrush" => Color.FromArgb(255, 0x1C, 0x25, 0x2E),
            _ => Color.FromArgb(255, 0x30, 0x39, 0x41)
        };

    /// <summary>
    /// 沉浸档的纸面用它 —— 与图下沿那道罩子最浓的一档同色（<see cref="DetailHero.ScrimCeiling"/>），
    /// 正文和上方那一段因此接得上，接缝看不出来。
    /// </summary>
    private static Color WashColor => Color.FromArgb(
        DetailHero.ScrimCeiling, DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B);

    /// <summary>
    /// 外壳把窗口递进来（<c>ShellPage</c> 挂在 <c>ContentFrame.Navigated</c> 上，后退／前进重建的页面
    /// 也一样能领到）。领到后按当前窗口形态先落定一档，不插过渡 —— 版面还没排过。
    /// </summary>
    internal void AttachWindow(Windowing.HostWindow window)
    {
        if (ReferenceEquals(_window, window)) return;

        if (_window is not null)
        {
            _window.GeometryChanged -= OnWindowGeometryChanged;
            _window.InteractiveResizeChanged -= OnInteractiveResizeChanged;
        }

        _window = window;
        window.GeometryChanged += OnWindowGeometryChanged;
        window.InteractiveResizeChanged += OnInteractiveResizeChanged;

        _spacious = IsSpacious();
        _pendingSpacious = _spacious;
        _maximized = window.IsMaximized;
        ApplyShowcase(animate: false);
    }

    /// <summary>离页时把窗口订阅与还在等的那一拍一起收掉。</summary>
    private void DetachShowcase()
    {
        ThemeHost.Changed -= OnShowcaseThemeChanged;
        StopShowcase();
        StopSpaciousWatch();
        if (_window is null) return;
        _window.GeometryChanged -= OnWindowGeometryChanged;
        _window.InteractiveResizeChanged -= OnInteractiveResizeChanged;
        _window = null;
    }

    /// <summary>
    /// 窗口几何变了（换显示器、改大小、全屏往返、最大化与还原）：窗口形态决定纸面让不让开。
    /// 最大化／还原当场落定；面积跨线多半是拖着窗口边过去的，等手停下来；最小化那一拍客户区是空的，
    /// 不重新量。
    /// </summary>
    private void OnWindowGeometryChanged()
    {
        if (_window is { Minimized: false } window)
        {
            var maximized = window.IsMaximized;
            var toggle = maximized != _maximized;
            _maximized = maximized;

            _pendingSpacious = IsSpacious();
            if (_pendingSpacious == _spacious) _spaciousWatch?.Stop();
            else if (toggle) CommitSpacious();
            else ScheduleSpacious();
        }
    }

    /// <summary>拖边／贴靠结束那一拍把还没落定的形态补上；<c>WM_EXITSIZEMOVE</c> 比计时器准。</summary>
    private void OnInteractiveResizeChanged(bool active)
    {
        if (!active) CommitSpacious();
    }

    /// <summary>这一刻窗口铺开了没有：最大化，或客户区面积占了工作区五分之四以上。</summary>
    private bool IsSpacious()
    {
        if (_window is not { } window) return false;

        var work = window.WorkArea();
        var (width, height) = window.ClientSize;
        return DetailHero.Spacious(
            window.IsMaximized, DetailHero.Share(width, height, work.Width, work.Height));
    }

    /// <summary>把「面积跨过了那条线」推到手停下来之后（计时器兜底，松手事件提前叫醒）。</summary>
    private void ScheduleSpacious()
    {
        if (_spaciousWatch is not { } watch)
        {
            watch = DispatcherQueue.CreateTimer();
            watch.Interval = TimeSpan.FromMilliseconds(180);
            watch.IsRepeating = false;
            watch.Tick += (_, _) => CommitSpacious();
            _spaciousWatch = watch;
        }

        watch.Stop();
        watch.Start();
    }

    private void CommitSpacious()
    {
        _spaciousWatch?.Stop();
        if (_pendingSpacious == _spacious)
        {
            // 拖过线又拖回来：窗口形态没变，但宽窄版式可能已经翻过面 —— 按当前视图模型落定一次。
            if (_shownShowcase != MediaViewModel.Showcase) SettleShowcase();
            return;
        }

        _spacious = _pendingSpacious;
        ScheduleShowcase();
    }

    private void StopSpaciousWatch() => _spaciousWatch?.Stop();

    /// <summary>等这一轮布局落定之后再应用 —— 那一刻视口还是旧的，终点会跟着变。</summary>
    private void ScheduleShowcase()
    {
        if (_showcaseQueued) return;

        _showcaseQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _showcaseQueued = false;
            ApplyShowcase(animate: true);
        });
    }

    /// <summary>
    /// 把窗口形态写进视图模型，并按需要走一趟过渡。只有窗口形态翻面这一条路带过渡；换条目、宽窄版式
    /// 跨断点、背景图到手这些让 <c>Showcase</c> 翻面的路都是版面自己的事，当场落定。
    /// </summary>
    private void ApplyShowcase(bool animate)
    {
        var was = _shownShowcase;
        var fromHero = HeroSection.ActualHeight;

        _applyingShowcase = true;
        try
        {
            MediaViewModel.Spacious = _spacious;
        }
        finally
        {
            _applyingShowcase = false;
        }

        var now = MediaViewModel.Showcase;

        if (!animate || was == now || !IsLoaded || XamlRoot is null || !HomeMotion.AnimationsEnabled)
        {
            if (animate && was == now && _showcaseBoard is not null) return;

            SettleShowcase();
            return;
        }

        AnimateShowcase(now, fromHero);
    }

    /// <summary>
    /// 视图模型里的 <c>Showcase</c> 从别的来路翻面（宽窄版式跨断点、背景图到手）：当场落定，不插过渡。
    /// </summary>
    private void OnShowcaseChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MoviePilotMediaViewModel.Showcase) || _applyingShowcase) return;

        // 窗口形态正在翻面的那一趟交给 ApplyShowcase 统一结算：恢复窗口时宽窄版式可能先于窗口状态翻面，
        // 在这里当场落定会把过渡吃掉（探针实测：还原窗口 ShowcaseAnimations 不再增加）。
        if (_showcaseQueued || IsSpacious() != _spacious) return;

        var now = MediaViewModel.Showcase;
        if (!_showcaseSettled)
        {
            SettleShowcase();
            return;
        }

        if (now == _shownShowcase) return;
        SettleShowcase();
    }

    /// <summary>
    /// 不停留地落定：纸面色、面板透明度、头部下限、每块面板的浮起位移直接写终值。第一次上树、换条目、
    /// 宽窄翻面和动画收尾都走它 —— 收尾那一下必须走，因为 <see cref="Storyboard.Stop"/> 会把属性还回
    /// 动画开始前的底值。
    /// </summary>
    private void SettleShowcase()
    {
        StopShowcase();

        var showcase = MediaViewModel.Showcase;
        _shownShowcase = showcase;
        _showcaseSettled = true;
        _settledWide = MediaViewModel.WideLayout;
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        _panelFill.Opacity = showcase ? 0 : 1;
        _panelLine.Opacity = showcase ? 0 : 1;
        DetailBackdropBand.Opacity = showcase
            ? DetailHero.ShowcaseBackdropOpacity
            : DetailHero.BackdropOpacity;

        foreach (var panel in BodyPanels)
            if (Drift(panel) is { } drift) drift.Y = 0;

        // 头部下限绑在视图模型上；动画期间它被本地值压住过，收尾要交回同一个数。
        HeroSection.MinHeight = MediaViewModel.HeroMinHeight;
    }

    /// <summary>这一块面板的浮起变换 —— 构造时每块装了一支，这里只取。</summary>
    private static TranslateTransform? Drift(StackPanel panel) => panel.RenderTransform as TranslateTransform;

    /// <summary>这一刻真正看得见的那几块正文面板，从上往下 —— 错峰的格子按它们数。</summary>
    private List<StackPanel> ShownPanels()
    {
        var shown = new List<StackPanel>(4);
        foreach (var panel in BodyPanels)
            if (panel.Visibility == Visibility.Visible) shown.Add(panel);

        return shown;
    }

    /// <summary>
    /// 走一趟：头部高度、纸面颜色、面板底与框、每块正文的浮起位移。前四样同一段时间、同一条缓动；
    /// 浮起那一段更短，并按 <see cref="DetailHero.FloatOrder"/> 一块错后一块。每一样都从「现在是」走到
    /// 「该是」，中途再翻回来只是换一个终点，不会从半路跳回起点。
    /// </summary>
    private void AnimateShowcase(bool showcase, double fromHero)
    {
        ShowcaseAnimations++;
        _shownShowcase = showcase;

        var sheetFrom = _sheetBrush.Color;
        var fillFrom = _panelFill.Opacity;
        var lineFrom = _panelLine.Opacity;
        var backdropFrom = DetailBackdropBand.Opacity;
        var panels = ShownPanels();
        var driftFrom = new double[panels.Count];
        for (var slot = 0; slot < panels.Count; slot++)
            driftFrom[slot] = Drift(panels[slot])?.Y ?? 0;

        StopShowcase();

        var board = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 1) 头部：布局属性，必须留在 UI 线程。收掉它，下方组件就整体上移。
        Track(board, HeroSection, "MinHeight", fromHero, MediaViewModel.HeroMinHeight, ease, dependent: true);

        // 2) 纸面：窗口色 ↔ 尾部那道半透明黑。RGB 本来就一样（ScrimInk），变的只有 alpha。
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        TrackColor(board, _sheetBrush, sheetFrom, _sheetBrush.Color, ease);

        // 3) 四块正文面板的底和框：沉浸档里淡到 0，还原时淡回来。
        Track(board, _panelFill, "Opacity", fillFrom, showcase ? 0 : 1, ease, dependent: true);
        Track(board, _panelLine, "Opacity", lineFrom, showcase ? 0 : 1, ease, dependent: true);

        // 4) 背景那张图：0.9 ↔ 0.96。Opacity 是合成属性，不用 EnableDependentAnimation。
        Track(board, DetailBackdropBand, "Opacity", backdropFrom,
            showcase ? DetailHero.ShowcaseBackdropOpacity : DetailHero.BackdropOpacity, ease);

        // 5) 每块正文各自浮起那 16px 归位，一块错后一块。
        for (var slot = 0; slot < panels.Count; slot++)
        {
            if (Drift(panels[slot]) is not { } drift) continue;

            var from = Math.Abs(driftFrom[slot]) > 0.5
                ? driftFrom[slot]
                : showcase ? FloatOffset : -FloatOffset;

            drift.Y = from;
            Track(
                board, drift, "Y", from, 0, ease,
                delay: FloatStagger * DetailHero.FloatOrder(slot, panels.Count, showcase),
                duration: FloatSpan);
        }

        _showcaseBoard = board;
        board.Completed += (_, _) => SettleShowcase();
        board.Begin();
    }

    private void StopShowcase()
    {
        if (_showcaseBoard is not { } board) return;

        _showcaseBoard = null;
        board.Stop();
    }

    private static void Track(
        Storyboard board, DependencyObject target, string property, double from, double to,
        EasingFunctionBase ease, bool dependent = false, TimeSpan? duration = null, TimeSpan? delay = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(duration ?? ShowcaseSpan),
            BeginTime = delay,
            EasingFunction = ease,
            EnableDependentAnimation = dependent
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }

    private static void TrackColor(
        Storyboard board, DependencyObject target, Color from, Color to, EasingFunctionBase ease)
    {
        var animation = new ColorAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(ShowcaseSpan),
            EasingFunction = ease,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Color");
        board.Children.Add(animation);
    }
}

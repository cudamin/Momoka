using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Theming;
using Windows.System;
using Windows.UI.ViewManagement;
// 上面那两个命名空间各有一个 DispatcherQueueTimer。Windows.System 在这里只为 VirtualKey 而来，计时器要的是
// Microsoft.UI 那个（WinUI 的 DispatcherQueue 发的就是它），所以指名一次，省下每处的全名。
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Momoka.Shell.Views;

/// <summary>
/// 主页最上面那条大图轮播 —— 「参考"主页轮播大图版-misty-4.9.css"给主页轮播功能」。
/// <para>
/// 一张幻灯片是一条剧照（铺满整条带）、上面压一道给字垫底的渐变幕、左边站着一块字（<see cref="BannerSlide"/>），
/// 这个控件是那条带：谁在台上、什么时候换、换的时候怎么过去。哪些条目上得了台、一共几张、带该多高，都是
/// <see cref="HomeCarousel"/> 里的纯函数，由 Core 的测试盯着；这里剩下的是屏上那部分。
/// </para>
/// <para>
/// 两层 <c>Image</c> 轮着上而不是一层换 <c>Source</c>：换一张时新的淡入、旧的淡出，中间不会闪一下底色。带上
/// 同时解码的只有台上这张和它左右各一张（<see cref="Prefetch"/>），其余的 <see cref="BannerSlide.Release"/>
/// 掉 —— 一张解码出来的剧照是兆字节量级的画面，八张一起留着就是几十兆。
/// </para>
/// <para>
/// 动画全在代码里搭：<c>Storyboard.TargetName</c> 只在运行时解析，写在标记里编译期一声不响、跑起来才抛，而
/// 这条带是主页第一眼看见的东西。<see cref="Probe"/> 是它在 <c>--self-check</c> 里的那一份读数。
/// </para>
/// </summary>
public sealed partial class HomeBanner : UserControl
{
    public static readonly DependencyProperty SlidesProperty = DependencyProperty.Register(
        nameof(Slides),
        typeof(IReadOnlyList<BannerSlide>),
        typeof(HomeBanner),
        new PropertyMetadata(null, OnSlidesChanged));

    /// <summary>
    /// 字从自己的位置底下多远抬起来。misty 是 30px 配 2.5 秒，桌面上那个太慢也太远。
    /// <para>不叫 <c>Drop</c>：<see cref="UIElement"/> 上已经有一个 <c>Drop</c> 事件，同名会把它藏起来。</para>
    /// </summary>
    private const double TextDrop = 22;

    /// <summary>
    /// 换图的那一下：两层交叉着淡入淡出。两下同时走，所以这也是「上一张还看得见」的时长。不叫 <c>Fade</c> 是
    /// 2026-09-09 之后的事：左边那道渐变幕在标记里占了 <c>Fade</c> 这个名（<c>HomeBanner.xaml</c>），一个类里
    /// 摆不下两个。
    /// </summary>
    private static readonly TimeSpan CrossFade = TimeSpan.FromMilliseconds(650);
    private bool _everLoaded;
    private bool _restoringEntry;
    private bool _paintingEntry;

    /// <summary>一行字抬起来用多久。</summary>
    private static readonly TimeSpan Lift = TimeSpan.FromMilliseconds(600);

    /// <summary>下一行比上一行晚多久起。misty 那份是 .4/.6/.8 秒，四行错完接近一秒，这里压到四行 210 毫秒。</summary>
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(70);

    /// <summary>「鼠标移出窗口后不会自动恢复」：翻页箭头浮出来了还得收回去，理由见 <see cref="HoverWatch"/>。</summary>
    private readonly HoverWatch _watch;
    private readonly BannerPointerGlow _pointerGlow;

    /// <summary>
    /// 自动翻页和右下那条进度条共用的钟，见 <see cref="SyncTimer"/>。它每 <see cref="TickInterval"/> 响一次，
    /// 按「这一张站了多久」算进度，跑满一个 <see cref="_dwell"/> 就换下一张。
    /// </summary>
    private readonly DispatcherQueueTimer? _timer;

    /// <summary>钟响一次隔多久。50 毫秒是条子走起来看不出台阶的最慢一档（160 宽、8 秒停留时一格一像素）。</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 台上这张是从哪一刻开始站的（<see cref="Environment.TickCount64"/>，单调、不受改系统时间影响）。
    /// 进度条和换片都从这一个数推出来，所以「条跑满了还没换」和「没跑满就被换」在结构上不会发生。
    /// </summary>
    private long _cycleStarted;

    // 每一种动画只有一个所有者：换片先收尾，卸载全部停止，避免上一张的 Completed 改到下一张。
    private readonly Dictionary<string, (Storyboard Board, Action Settle)> _motions = [];
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _colorAccessibility = new();
    private bool _lastColorContrast;
    private bool _lastColorEffects;
    private DispatcherQueueTimer? _colorSystemTimer;
    private bool _settingsObserved;
    private bool _focusWithin;
    private bool _active;

    private readonly ControlBackgroundBrushes _openBackground;
    private UiSettings? _appearanceSettings;
    private int _appearanceLifetime;

    private List<BannerSlide> _slides = [];

    /// <summary>设置 → 主页 → 「轮播图版式」（2026-11-16）。缺省是现有的整宽大图。</summary>
    private CarouselLayout _layout = CarouselLayout.Full;

    /// <summary>这一刻摆没摆右栏，由 <see cref="Resize"/> 按 <see cref="HomeCarousel.ShowsRail"/> 拨。</summary>
    private bool _railShown;

    /// <summary>主卡那一列这一刻的宽（摆右栏时是整条宽减右栏和空当）；页面那头用它限制轮播上的媒体库视口。</summary>
    private double _mainWidth;

    /// <summary>
    /// 上面那两列卡片（Stage）这一刻的高。下沿的圆点行不归它管，Root 的总高是它加上圆点那一行
    /// （见 <see cref="UpdateRootHeight"/>）。
    /// </summary>
    private double _bandHeight;

    /// <summary>右栏三格各自订着的幻灯片，次序和参考图一致：0 是大卡，1、2 是下面并排两张。</summary>
    private readonly BannerSlide?[] _railSlides = new BannerSlide?[HomeCarousel.RailSlots];

    /// <summary>底部分页圆点的按钮与点本身（商店版式；点数跟着幻灯片张数走）。</summary>
    private readonly List<Button> _dots = [];
    private readonly List<Border> _dotMarks = [];

    /// <summary>台上那张的下标。<see cref="_slides"/> 空的时候是 0，也就是「没有」。</summary>
    private int _index;

    /// <summary>台上那层是 <c>LayerB</c>。两层轮着上，见 <see cref="Paint"/>。</summary>
    private bool _second;

    /// <summary>
    /// 矮窗档：媒体库那一排压在带的左下角（2026-09-13「窗口拉矮到媒体库下方那一排被藏起来之后，把媒体库压到
    /// 轮播图封面的左下角」）。排子占的是带的下半截，所以字块要让出那一截（下边距＝排高，在自己剩下的地界里
    /// 照旧竖向居中）。右下的张数读数照常显示（2026-09-13「媒体库上移时图二轮播图右下角的那个不用隐藏」：
    /// 它在右下角，压上来的那一排在左下角，两边不打架）—— 从前它跟着底边那排分页横条一起收，横条那天整个删了。
    /// </summary>
    private bool _slim;

    /// <summary>压上来的那一排总高（牌子 ＋ 空当 ＋ 一排卡）。页面量了递进来，字块的下边距就是它。</summary>
    private double _slimShelfHeight;

    /// <summary>指针在带上：箭头浮出来，自动翻页停下。</summary>
    private bool _hover;

    /// <summary>台上那张，订着它的 <c>PropertyChanged</c> —— 图是解码完才到的。</summary>
    private BannerSlide? _current;

    /// <summary>
    /// 矮窗档开、关（页面量完几何后拨，排高一起递进来）。三个开关各回各位：字块让位（<see cref="PlaceInfo"/>）、
    /// 右下读数（<see cref="SyncSlideStatus"/>，矮窗档照常显示）、简介的高度线
    /// （<see cref="SynopsisVisibility"/> 量的是字块实分到的高）。
    /// </summary>
    internal void SetLibraryOverlay(bool on, double shelfHeight)
    {
        var height = on ? Math.Max(0, shelfHeight) : 0;

        if (_slim == on && Math.Abs(_slimShelfHeight - height) < 0.5) return;

        _slim = on;
        _slimShelfHeight = height;

        PlaceInfo();
        SyncSlideStatus();
        SynopsisRow.Visibility = SynopsisVisibility(Current?.SynopsisVisibility ?? Visibility.Collapsed);
    }

    /// <summary>
    /// 大图自己那层圆角剪裁（2026-11-16 用户指出「怎么有些地方不是圆角」）。商店版式是 16，沉浸通栏是 0；
    /// 半径由页面按显示模式拨，不能只靠外面 BannerCard 那一圈 —— 圆点移到卡片下面之后，大图的右两角和
    /// 下两角落在整条轮播内部，没有这一层就会露出直角。
    /// <para>
    /// 2026-11-17 再加一层「比卡片小 1 的圆角 + 1 像素底色」（<c>PictureClip</c>，见标记里那一段）：
    /// 剧照被圆角裁掉时最外一圈像素是原图边缘色乘部分覆盖，图偏亮时弧线上就留一条 1 像素亮边。
    /// 让剧照自己退 1 像素，弧线上只剩带子底色，那条亮边就没有了。沉浸通栏（圆角 0）两样都归零，
    /// 图仍旧铺到窗口的三条边上。
    /// </para>
    /// </summary>
    internal void SetCardCornerRadius(CornerRadius radius)
    {
        if (HeroClip.CornerRadius != radius) HeroClip.CornerRadius = radius;

        var rounded = radius.TopLeft > 0;
        var inner = rounded
            ? new CornerRadius(
                Math.Max(0, radius.TopLeft - 1), Math.Max(0, radius.TopRight - 1),
                Math.Max(0, radius.BottomRight - 1), Math.Max(0, radius.BottomLeft - 1))
            : default;
        if (PictureClip.CornerRadius != inner) PictureClip.CornerRadius = inner;
        var inset = rounded ? new Thickness(1) : default;
        if (PictureClip.Margin != inset) PictureClip.Margin = inset;
    }

    /// <summary>
    /// 带子里那块字（左边站着的标题、读数、简介、按钮那一整块）。矮窗档翻档时页面要在它身上补一趟位移
    /// （见 <see cref="HomeFoldMotion"/>）：它让位那一下是把底边距整段换掉（<see cref="PlaceInfo"/>），
    /// 一像素不差地硬切；而压上来的那一排是滑上来的，两头差着两百来像素，不同时走就是「字先跳、排后到」。
    /// </summary>
    internal FrameworkElement InfoBlock => Info;

    /// <summary>
    /// 矮窗档开、关（页面量完几何后拨，排高一起递进来）。三个开关各回各位：字块让位（<see cref="PlaceInfo"/>）、
    /// 右下读数（<see cref="SyncSlideStatus"/>）、简介的高度线（<see cref="SynopsisVisibility"/>）。
    /// </summary>
    private void PlaceInfo()
    {
        Info.Margin = new Thickness(InfoInset, 0, 0, _slim ? _slimShelfHeight : 0);
    }

    /// <summary>
    /// 右下的张数读数：多张、带够宽。**矮窗档照常显示**（2026-09-13「媒体库压在轮播图上的时候不用隐藏图二
    /// 轮播图右下角的那个」）。页面按 <see cref="LibraryOverlayRightInset"/> 限制左下媒体库的视口；以前它跟着底边那排分页横条
    /// 一起收起，那天他点了名要它留下来。（倒计时条跟着它一起，见标记里 SlideStatus 那一段。）
    /// </summary>
    private void SyncSlideStatus()
    {
        // 整宽版式保留右下角的张数读数与进度条；商店版式改用底部分页圆点（参考截图里两种宽度下都是圆点）。
        SlideStatus.Visibility = _layout == CarouselLayout.Full && _slides.Count > 1 && ActualWidth >= 800
            ? Visibility.Visible
            : Visibility.Collapsed;
        SyncPageDots();
    }

    /// <summary>
    /// 底部分页圆点（商店版式）。张数变了就重建成一排；没变只把当前那颗放大、换墨，不重排。
    /// 圆点只做指针直达 —— 键盘仍由整条带的方向键负责，所以它们不占 Tab 停靠位。
    /// <para>
    /// 2026-11-16 重做：圆点在两张卡片<em>下面</em>的页面底上（参考图两种宽度都这样），行高由
    /// <see cref="UpdateRootHeight"/> 折进 Root，收起时整行不占位置；当前那颗比其余大一号而不是拉成长条。
    /// </para>
    /// </summary>
    private void SyncPageDots()
    {
        var shown = _layout == CarouselLayout.Store && _slides.Count > 1;
        PageDots.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        DotsHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown)
        {
            UpdateRootHeight();
            return;
        }

        if (_dots.Count != _slides.Count) RebuildDots();

        for (var index = 0; index < _dots.Count; index++)
        {
            var active = index == _index;
            var mark = _dotMarks[index];
            var size = active ? 6d : 5d;
            if (Math.Abs(mark.Width - size) > 0.01)
            {
                mark.Width = size;
                mark.Height = size;
                mark.CornerRadius = new CornerRadius(size / 2);
            }
            mark.Background = PageDotBrush(active);
            AutomationProperties.SetName(_dots[index], HomeCarousel.Position(index, _slides.Count));
        }

        UpdateRootHeight();
    }

    /// <summary>
    /// 圆点那一行占不占位置：占就按 <see cref="HomeCarousel.DotsHeight"/> 加进 Root 的高，收起时归零；
    /// 同时把这一行的宽拨成主卡那一列，圆点因此在主卡下方居中。
    /// </summary>
    private void UpdateRootHeight()
    {
        var dots = PageDots.Visibility == Visibility.Visible ? HomeCarousel.DotsHeight : 0;
        var total = _bandHeight + dots;
        if (double.IsNaN(Root.Height) || Math.Abs(Root.Height - total) > 0.5) Root.Height = total;

        var width = _mainWidth > 0 ? _mainWidth : ActualWidth;
        if (double.IsNaN(DotsHost.Width) || Math.Abs(DotsHost.Width - width) > 0.5) DotsHost.Width = width;
    }

    /// <summary>
    /// 分页圆点的墨：落在页面底上，跟主题文字色走 —— **不是**带子上那两支固定的浅墨。
    /// 从前这里从 <c>Application.Current.Resources</c> 找 <c>EgBannerDotBrush</c>，而那一支只写在
    /// <c>HomeBanner.xaml</c> 自己的字典里，窗口一拖动（Resize → SyncPageDots）就抛
    /// 「Cannot find a resource with the given key」把应用打掉 —— 2026-11-16 的崩溃卡死就是这一句。
    /// 现在两支都从应用主题色里取，本控件不再自带圆点画刷。
    /// </summary>
    private Brush PageDotBrush(bool active)
    {
        if (_colorAccessibility.HighContrast)
        {
            var color = _uiSettings.GetColorValue(UIColorType.Foreground);
            if (!active) color.A = 0x99;
            return new SolidColorBrush(color);
        }

        return (Brush)Application.Current.Resources[active ? "EgTextBrush" : "EgTextFaintBrush"];
    }

    private void RebuildDots()
    {
        foreach (var dot in _dots) dot.Click -= OnDotClicked;
        _dots.Clear();
        _dotMarks.Clear();
        PageDots.Children.Clear();

        var style = (Style)Resources["EgBannerDotButtonStyle"];
        for (var index = 0; index < _slides.Count; index++)
        {
            var mark = new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                VerticalAlignment = VerticalAlignment.Center
            };
            var dot = new Button
            {
                Style = style,
                Content = mark,
                IsTabStop = false
            };
            AutomationProperties.SetAutomationId(dot, $"BannerPageDot{index + 1}");
            dot.Click += OnDotClicked;
            PageDots.Children.Add(dot);
            _dots.Add(dot);
            _dotMarks.Add(mark);
        }
    }

    private void OnDotClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button dot) return;
        var at = _dots.IndexOf(dot);
        if (at < 0 || at == _index) return;

        Show(at);
        SyncTimer();
    }

    /// <summary>
    /// 商店版式的右栏：跟着台上那张换成接下来的三张（<see cref="HomeCarousel.RailIndices"/>）。
    /// 摆右栏时主卡让出的宽度由 <see cref="Resize"/> 拨列宽，这里只管那三格画谁。
    /// </summary>
    private void SyncRail()
    {
        if (!_railShown)
        {
            HideRail();
            return;
        }

        Rail.Visibility = Visibility.Visible;
        var indices = HomeCarousel.RailIndices(_index, _slides.Count);
        for (var slot = 0; slot < _railSlides.Length; slot++)
        {
            var slide = slot < indices.Count ? _slides[indices[slot]] : null;
            if (ReferenceEquals(_railSlides[slot], slide)) continue;

            if (_railSlides[slot] is { } previous) previous.PropertyChanged -= OnRailSlideChanged;
            _railSlides[slot] = slide;
            if (slide is not null) slide.PropertyChanged += OnRailSlideChanged;
            PaintRailCard(slot, slide);
        }
    }

    /// <summary>收右栏时把它订着的幻灯片退掉、图也放掉 —— 收起来的右栏不该继续扣着三张解码好的剧照。</summary>
    private void HideRail()
    {
        Rail.Visibility = Visibility.Collapsed;
        if (_railSlides.All(slide => slide is null)) return;

        for (var slot = 0; slot < _railSlides.Length; slot++)
        {
            if (_railSlides[slot] is { } slide) slide.PropertyChanged -= OnRailSlideChanged;
            _railSlides[slot] = null;
            PaintRailCard(slot, null);
        }
    }

    /// <summary>三格卡片里的一格：图片铺满、标题压在左下角；没有图时退回带子底色，不留上一张的图。</summary>
    private void PaintRailCard(int slot, BannerSlide? slide)
    {
        var (surface, title, button) = slot switch
        {
            0 => (RailBigSurface, RailBigTitle, (Button)RailBigCard),
            1 => (RailSmallSurfaceA, RailSmallTitleA, (Button)RailSmallCardA),
            _ => (RailSmallSurfaceB, RailSmallTitleB, (Button)RailSmallCardB)
        };

        title.Text = slide?.Title ?? "";
        AutomationProperties.SetName(button, slide?.OpenTip ?? "");

        if (slide?.Picture is not { } picture)
        {
            if (surface.Background is not SolidColorBrush)
                surface.Background = (Brush)Application.Current.Resources["EgBannerBaseBrush"];
            return;
        }

        if (surface.Background is ImageBrush { } existing && ReferenceEquals(existing.ImageSource, picture)) return;
        surface.Background = new ImageBrush { ImageSource = picture, Stretch = Stretch.UniformToFill };
    }

    /// <summary>
    /// 右栏那张的图解码好了。和台上那张一样认一下是不是这一格此刻要的那张 —— 换页快的时候上一张的解码
    /// 会晚到，那张图不该盖在现在这张的标题上面。
    /// </summary>
    private void OnRailSlideChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BannerSlide.Picture) || sender is not BannerSlide slide) return;

        for (var slot = 0; slot < _railSlides.Length; slot++)
        {
            if (ReferenceEquals(_railSlides[slot], slide)) PaintRailCard(slot, slide);
        }
    }

    private void OnRailCardClicked(object sender, RoutedEventArgs e)
    {
        var slot = ReferenceEquals(sender, RailBigCard) ? 0
            : ReferenceEquals(sender, RailSmallCardA) ? 1
            : ReferenceEquals(sender, RailSmallCardB) ? 2 : -1;
        if (slot >= 0 && _railSlides[slot] is { } slide) OpenRequested?.Invoke(this, slide);
    }

    /// <summary>
    /// 左下媒体库需要留出的右边距：读数和进度条的宽、现有右边距，再加一格间隔。
    /// 首次排版时读数还没有 ActualWidth，进度底条的声明宽度保证那一拍也不会被多库盖住。
    /// </summary>
    internal double LibraryOverlayRightInset => SlideStatus.Visibility == Visibility.Visible
        ? SlideStatus.Margin.Right
            + Math.Max(SlideStatus.ActualWidth, SlideStatus.Children.OfType<Border>().Single().Width)
            + (double)Application.Current.Resources["EgSpaceXL"]
        : 36;

    /// <summary>
    /// 简介收不收的那条高度线。矮窗档里字块实分到的高是带高减去压上来的那一排 —— 还按带高整根量，
    /// 带子矮的那几档简介会伸进排子里。**量的是卡片那一截（<see cref="_bandHeight"/>），不是 Root**：
    /// 2026-11-16 起 Root 底下还挂着圆点那一行，把它算进「字块有多少地方站」会让矮带多显示一行不该出现的简介。
    /// </summary>
    private Visibility SynopsisVisibility(Visibility wanted)
    {
        var textHeight = _bandHeight - (_slim ? _slimShelfHeight : 0);

        return textHeight < 340 ? Visibility.Collapsed : wanted;
    }

    /// <summary>
    /// 订着的那个 <c>XamlRoot</c>，也就是窗口的客户区。带高照带宽算、上限一屏（<see cref="HomeCarousel.Height"/>），
    /// 而只拖下边沿的那一下这条带自己的宽度一点没变 —— <c>SizeChanged</c> 因此不响，一屏有多高却已经换了一个数。
    /// 存下来是为了退订：进树时的 XamlRoot 和离树后能不能问到不是一回事。
    /// </summary>
    private XamlRoot? _viewport;

    /// <summary>
    /// 一张站多久（设置 → 主页 → 「封面轮换秒数」，2026-09-13）。构造时先落在装机默认（<see cref="HomeCarousel.Dwell"/>），
    /// 页面接上设置和设置页改动的那句话（<see cref="ApplyDwell"/>）都会把它更新成真值 —— 钟的间隔和底下那道
    /// 进度条的时长都从这一份读，两处永远同一个数。
    /// </summary>
    private TimeSpan _dwell = HomeCarousel.Dwell;

    /// <summary>
    /// 设置里那行秒数改了（或者页面刚接上设置）：按新值重摆自动翻页的钟。没在走的那一档（一张、不在树上）
    /// <see cref="SyncTimer"/> 自己会继续停着，下一次走的时候就是新时长。
    /// <para>
    /// 值没变就什么都不做：这一句在整页重读、拖版面这些「设置里别的行改了」的场合也会被喊到，而每喊一次就把
    /// 进度条清零，正是 2026-11-02 那句「跑到一半回退」。
    /// </para>
    /// </summary>
    internal void ApplyDwell(int seconds)
    {
        var dwell = HomeCarousel.DwellFor(seconds);
        if (_dwell == dwell) return;

        _dwell = dwell;
        SyncTimer();
    }

    /// <summary>
    /// 设置里那行「轮播图版式」改了（或者页面刚接上设置）：整宽大图和商店版式之间换一档。
    /// 宽度已经量到就照新档重摆（列宽、右栏、底部分页一起换）；还没量到的那一帧只换可见性，
    /// 下一次 <c>SizeChanged</c> 会补上几何。
    /// </summary>
    internal void ApplyLayout(CarouselLayout layout)
    {
        if (_layout == layout) return;

        _layout = layout;

        // 商店版式的背景是原图本身（2026-11-16 用户实屏：「没必要加高斯模糊」）；整宽版式保留左侧采色扩边。
        foreach (var slide in _slides) slide.SetLeftExtension(layout != CarouselLayout.Store);

        if (ActualWidth > 0) Resize(ActualWidth);
        else SyncSlideStatus();

        if (_slides.Count > 0)
        {
            // 扩边模式变了，台上那张的图可能刚被放掉：先按空图/已缓存图重画一次，预取再按新模式补上。
            Show(_index, animate: false);
            Prefetch();
        }

        PaintFadeInk();
    }

    /// <summary>这一刻摆没摆右栏。页面那头据此决定轮播上的媒体库视口要不要收窄。</summary>
    internal bool RailShown => _railShown;

    /// <summary>主卡那一列此刻的宽（DIP）。还没量到宽时退回控件自己的宽。</summary>
    internal double MainColumnWidth => _mainWidth > 0 ? _mainWidth : ActualWidth;

    /// <summary>当前轮播版式；页面按它算翻档界线与自检的预期带高。</summary>
    internal CarouselLayout Layout => _layout;

    /// <summary>
    /// 这条带这一刻应有的总高：卡片那一截按版式从 Core 现算，商店版式再多留圆点那一行。页面自检拿它
    /// 对账 —— 这里重新走一遍 <see cref="HomeCarousel.BandHeight"/>，不是把 Resize 写下的数读回来，
    /// 所以「Resize 算错了」这件事仍然量得出来。
    /// </summary>
    internal double ExpectedHeight
    {
        get
        {
            var width = ActualWidth;
            var rail = HomeCarousel.ShowsRail(_layout, width, _slides.Count);
            var band = HomeCarousel.BandHeight(_layout, XamlRoot?.Size.Height ?? 0, width, rail);
            var dots = _layout == CarouselLayout.Store && _slides.Count > 1 ? HomeCarousel.DotsHeight : 0;
            return band + dots;
        }
    }

    /// <summary>按设计形状、不被一屏封住的带高（矮窗档界线读它）。</summary>
    internal double NaturalBandHeight => HomeCarousel.NaturalBandHeight(_layout, ActualWidth, _railShown);

    public HomeBanner()
    {
        InitializeComponent();
        _openBackground = ControlBackgroundBrushes.ForBanner(OpenButton, Resources);
        _pointerGlow = new BannerPointerGlow(PointerGlow, PointerGlowBrush, Root);
        PaintPointerGlowInk();

        // 字底下那层跟着字形走的影子（见 TextInk）。四行各一层，宿主是同一格里排在字前面那个空 Border。
        // 装在构造里而不是 Loaded 里：这四层是这块字的一部分，不是「进树之后才有的装饰」，而自检里那份
        // 控件（Probe 里 new 出来的那一个）也该把这条路走一遍 —— 走不通就该在闸门上红，不是在屏上少一层。
        // （2026-09-16 十六报二分实验证明这四层影子与主页启动崩溃无关，恢复无条件挂上。）
        TextInk.Attach(LogoInk, LogoImage);
        TextInk.Attach(TitleInk, TitleText, TextInk.TitleBlur);
        TextInk.Attach(CaptionInk, CaptionText);
        TextInk.Attach(SynopsisInk, SynopsisText);

        // 悬停只管箭头浮不浮出来。它从前还会停掉自动翻页的钟，而指针停在窗口上半截（也就是这条带）上
        // 是常态 —— 那条路让进度条看着像坏了（2026-11-02「经常会卡住不动」），现在钟不认指针。
        _watch = new HoverWatch(Root, up =>
        {
            _hover = up;
            if (!up) _pointerGlow.Leave();
            SyncArrows();
        });

        // 全名一次：UserControl 自己有个同名的 DispatcherQueue 属性，简名先落在它身上，静态方法就取不到了。
        _timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.CreateTimer();

        if (_timer is not null)
        {
            _timer.Interval = TickInterval;
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;
        }

        SizeChanged += OnResized;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>外观只重画底色和两支图片画刷，翻页动画继续独占图片层的 Opacity。</summary>
    internal void ApplyAppearance(UiSettings ui)
    {
        _appearanceSettings = ui;
        var contrast = _colorAccessibility.HighContrast;
        _lastColorContrast = contrast;
        _lastColorEffects = _uiSettings.AdvancedEffectsEnabled;
        PaintPointerGlowInk();
        _pointerGlow.SetEnabled(_active && !contrast && _lastColorEffects);
        _openBackground.Apply(contrast ? 100 : ui.ControlBackgroundOpacity, !contrast && ui.UseAcrylicControls,
            contrast ? _uiSettings.GetColorValue(UIColorType.Background) : ThemeHost.ToColor(ShellPrefs.AppearanceColor(false, ui)));
        PaintOpenButtonInk();
        SyncPageDots();
        // 轮播那两张图本来就按满不透明铺着（详情页那道浓度滑块不归这里管），2026-11-07 起更彻底：
        // 设置里只剩详情页的亚克力参数，首页这两层不再跟着任何浓度走。
        Brush(LayerA).Opacity = 1;
        Brush(LayerB).Opacity = 1;
        // 全图高斯仅供详情沉浸布局；首页保留原图及左侧扩边过渡。
        foreach (var slide in _slides) slide.SetGaussianBackdrop(false);
        PaintBackdropShade();
        // 高对比度开关会改变左侧渐变与光晕取色（那一档一律用固定深墨/透明），换主题时当场重画。
        PaintFadeInk();
    }

    internal bool GaussianBackdropApplied => Current?.GaussianBackdropApplied == true;
    internal bool GaussianBackdropReady => Current?.GaussianBackdropReady == true;

    private void PaintOpenButtonInk()
    {
        var sample = BackdropInk.Sample(OpenButton, FrontLayer, Current?.BackdropPixels, false, ThemeHost.Current.Colors.Window);
        var along = 0d;
        try
        {
            var position = OpenButton.TransformToVisual(Fade).TransformPoint(new Windows.Foundation.Point(OpenButton.ActualWidth / 2, OpenButton.ActualHeight / 2));
            along = Fade.ActualWidth > 0 ? Math.Clamp(position.X / Fade.ActualWidth, 0, 1) : 0;
        }
        catch (ArgumentException) { }
        var stops = ((LinearGradientBrush)Fade.Background).GradientStops;
        var left = stops.Last(stop => stop.Offset <= along);
        var right = stops.First(stop => stop.Offset >= along);
        var alpha = left.Offset == right.Offset ? left.Color.A / 255d
            : (left.Color.A + (right.Color.A - left.Color.A) * (along - left.Offset) / (right.Offset - left.Offset)) / 255d;
        var background = sample.Mix(ThemeColor.Rgb(left.Color.R, left.Color.G, left.Color.B), alpha);
        _openBackground.ApplyForeground(background, !_uiSettings.AdvancedEffectsEnabled,
            _colorAccessibility.HighContrast ? _uiSettings.GetColorValue(UIColorType.Foreground) : null);
    }

    private void PaintBackdropShade()
    {
        var shading = GaussianBackdropApplied ? Visibility.Collapsed : Visibility.Visible;
        Top.Visibility = shading;
        Fade.Visibility = shading;
        RightShade.Visibility = shading;
    }

    private void OnAppearanceChanged(UiSettings ui)
    {
        if (!_active) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyAppearance(ui);
            return;
        }
        var lifetime = _appearanceLifetime;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_active && lifetime == _appearanceLifetime) ApplyAppearance(ui);
        });
    }

    private void OnAppearanceThemeChanged(Momoka.Theming.UiTheme theme)
    {
        if (_active && _appearanceSettings is { } ui) OnAppearanceChanged(ui);
    }

    /// <summary>按下播放键的那张幻灯片。</summary>
    public event EventHandler<BannerSlide>? PlayRequested;

    /// <summary>按下详情键（或按回车）的那张幻灯片。</summary>
    public event EventHandler<BannerSlide>? OpenRequested;

    /// <summary>
    /// 台上换人了。<see langword="null"/> 是「台上没人」（整批换成空的）。
    /// <para>
    /// 事件而不是让页面去轮询：换一张有四个来处（钟、两颗箭头、左右方向键、换一整批），而它们
    /// 都汇到 <see cref="Show"/> 这一处。
    /// </para>
    /// <para>
    /// 右边那一列继续观看 2026-09-08 删掉之后没人再订它，但留着：整批换掉、收起整条带，这类「这一刻台上是谁」
    /// 的读数对下一个要看这条带的听者（比如一张选择条）是现成的，价钱是一个没人订的事件。
    /// </para>
    /// </summary>
    public event EventHandler<BannerSlide?>? SlideChanged;

    /// <summary>
    /// 一张幻灯片取不到宽图、被从这一批里摘掉了（见 <see cref="BannerSlide.PictureUnavailable"/>）。页面据此把
    /// 同一件从视图模型那份清单里也去掉 —— 只摘带子自己这一份的话，下一次整页重读又会把它带回来。
    /// </summary>
    public event EventHandler<BannerSlide>? SlideDropped;

    /// <summary>
    /// 这条带放哪几张。整份换掉而不是往里增删：一次读取重建一批 <see cref="BannerSlide"/>，上一批的图连着解码
    /// 出来的画面一起丢掉（<see cref="Apply"/>），这样「换了一批」就只有一个入口。
    /// </summary>
    public IReadOnlyList<BannerSlide>? Slides
    {
        get => (IReadOnlyList<BannerSlide>?)GetValue(SlidesProperty);
        set => SetValue(SlidesProperty, value);
    }

    // ---- 换一批幻灯片 -------------------------------------------------------------

    /// <summary>
    /// 换一批幻灯片。同一个条目、同一张图、同样几行字的沿用原来那个幻灯片对象，台上那张因此不用下台；
    /// 一张都没有就把整条带收起来 —— 服务器上没有一个带宽图的条目时，主页顶上留一条空白的黑带比没有轮播
    /// 更难解释。
    /// <para>
    /// **整页重读不等于这一批换人了**：新入库的定时检查、设置里别的行改了、从别的页面回来，都会走到这里，
    /// 而从前每次都从头造一批、从第一张开始，就是 2026-11-02 那句「进度条跑到一半回退」。现在只有台上那张
    /// 真的不在新清单里（被删了、或者内容变了）才回到第一张并重算钟。
    /// </para>
    /// <para>
    /// 自己改自己的 <c>Visibility</c>，所以页面那边不用为这条带再绑一个可见性；这也是主页的标记里它只有一行的
    /// 原因。<c>StackPanel</c> 的 <c>Spacing</c> 不给收起来的孩子留位置，所以收起来就是真的不占地方。
    /// </para>
    /// </summary>
    internal void Apply(IReadOnlyList<BannerSlide> slides)
    {
        var previous = _slides;
        var retained = new List<BannerSlide>(previous);
        var next = new List<BannerSlide>(slides.Count);

        // 同一个条目、同一张图、同样几行字 = 还是这一张：沿用原来那个幻灯片对象。整页重读（新入库的定时检查、
        // 设置里别的行改了、从别的页面回来）不等于这一批换人了，而每次都从头造一批、Show(0)，就是 2026-11-02
        // 那句「进度条跑到一半回退」。
        foreach (var slide in slides)
        {
            var match = retained.FindIndex(old => Same(old, slide));
            if (match < 0)
            {
                next.Add(slide);
                continue;
            }

            next.Add(retained[match]);
            retained.RemoveAt(match);
        }

        if (next.Count == previous.Count && next.SequenceEqual(previous) && next.Count > 0) return;

        // 被换下去的那几张：撤掉失败订阅、放掉解码好的画面。新造的那些还没加载过，不用管。
        foreach (var stale in retained)
        {
            stale.PictureUnavailable -= OnPictureUnavailable;
            stale.Release();
        }
        foreach (var slide in next)
        {
            if (!previous.Contains(slide)) slide.PictureUnavailable += OnPictureUnavailable;
        }

        // 台上是谁要在换清单之前问：换完 _index 说的已经是新清单里的位置了。
        var current = Current;

        StopMotions();
        Unwatch();

        _slides = next;
        foreach (var slide in _slides) slide.SetGaussianBackdrop(false);
        // 新建的幻灯片默认按整宽版式带左侧扩边；商店版式要原图，先在这里把模式定下来再发起解码。
        foreach (var slide in _slides) slide.SetLeftExtension(_layout != CarouselLayout.Store);
        Visibility = _slides.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 张数变了可能改变右栏的取舍（少于四张就收起来）。先照新清单重算几何，Show 随后再落字；
        // 宽度还没量到的那一帧只同步可见性，SizeChanged 会补列宽。
        if (ActualWidth > 0) Resize(ActualWidth);
        else SyncSlideStatus();

        if (_slides.Count == 0)
        {
            _index = 0;
            _second = false;
            LayerA.Opacity = 0;
            LayerB.Opacity = 0;
            Brush(LayerA).ImageSource = null;
            Brush(LayerB).ImageSource = null;
            PictureShimmer.IsActive = false;
            LogoImage.Source = null;
            LogoRow.Visibility = Visibility.Collapsed;
            SlideChanged?.Invoke(this, null);
            PaintFadeInk();
            SyncArrows();
            SyncTimer();
            return;
        }

        var keep = current is null ? -1 : next.FindIndex(slide => ReferenceEquals(slide, current));
        if (keep < 0 && current is not null)
        {
            keep = next.FindIndex(slide =>
                string.Equals(slide.Item.IdentityKey, current.Item.IdentityKey, StringComparison.Ordinal));
        }

        if (keep >= 0)
        {
            // 台上那张还在：位置跟着新清单走，钟不重来（字和图也没变，不重播抬升）。
            Show(keep, animate: !ReferenceEquals(next[keep], current));
            if (previous.Count <= 1 || next.Count <= 1) SyncTimer();
        }
        else
        {
            Show(0);
            SyncTimer();
        }

        SyncArrows();
    }

    /// <summary>两条幻灯片画出来是不是同一张。见 <see cref="BannerSlide.Signature"/>。</summary>
    private static bool Same(BannerSlide left, BannerSlide right) =>
        string.Equals(left.Signature, right.Signature, StringComparison.Ordinal);

    /// <summary>
    /// 把第 <paramref name="index"/> 张请上台：字、徽标、剧照，右下角的张数读数一起换数。
    /// <para>
    /// 字是当场就换的，剧照要等解码 —— 所以订上这张幻灯片的 <c>PropertyChanged</c>，图到了再淡进来
    /// （<see cref="OnSlideChanged"/>）。台上换人时前一张的订阅要撤掉，否则上一张的图迟到时会盖掉现在这张。
    /// </para>
    /// <para>
    /// <paramref name="animate"/> 是假的这一档只出现在「还是台上这张，只是整批换了个对象」的时候
    /// （<see cref="Apply"/>）：字和图都没变，再抬一次、再淡一次就是一次白闪。
    /// </para>
    /// </summary>
    private void Show(int index, bool animate = true)
    {
        if (_slides.Count == 0) return;

        _index = Math.Clamp(index, 0, _slides.Count - 1);
        var slide = _slides[_index];

        TitleText.Text = slide.Title;
        CaptionText.Text = slide.Caption;
        CaptionRow.Visibility = slide.CaptionVisibility;
        SynopsisText.Text = slide.Synopsis;
        SynopsisRow.Visibility = _bandHeight < 340 ? Visibility.Collapsed : slide.SynopsisVisibility;
        PlayText.Text = slide.PlayText;
        SlideCounter.Text = $"{_index + 1:00}  /  {_slides.Count:00}";
        SyncSlideStatus();

        // 念给读屏的人听：这条带上的字全压在剧照上，光念标题听不出这是第几张。
        AutomationProperties.SetName(Root, $"主页轮播，{HomeCarousel.Position(_index, _slides.Count)}，{slide.Title}");
        AutomationProperties.SetName(PlayButton, $"{slide.PlayText} {slide.Title}");
        AutomationProperties.SetName(OpenButton, slide.OpenTip);

        Unwatch();
        _current = slide;
        slide.PropertyChanged += OnSlideChanged;

        if (animate)
        {
            _restoringEntry = false;
            Rise();
        }
        Paint(slide.Picture);
        PaintLogo(slide);
        SyncRail();
        Prefetch();

        // 最后一句：上面那几件都是这条带自己的事，这一声是给外面的听者的。
        SlideChanged?.Invoke(this, slide);
        PaintFadeInk();
    }

    /// <summary>
    /// 左侧渐变幕的颜色（2026-11-16 用户令「左边的渐变能不能根据不同的背景图颜色自动切换」）：商店版式跟着
    /// 台上那张的代表色走，颜色压到暗部（峰值 52）再铺回原来的四档 alpha —— 既要看得出来这张图是什么颜色，
    /// 又不能让白字压在亮色上读不出来。整宽版式和高对比度仍用固定的深墨。
    /// </summary>
    private void PaintFadeInk()
    {
        var ink = FadeDefaultInk;
        var ambient = Current?.Ambient ?? CarouselInk.None;
        if (_layout == CarouselLayout.Store && !_colorAccessibility.HighContrast && !ambient.IsNone)
        {
            var peak = Math.Max(ambient.R, Math.Max(ambient.G, ambient.B));
            var scale = peak <= 0 ? 0 : 52d / peak;
            ink = Windows.UI.Color.FromArgb(0xFF,
                (byte)Math.Clamp(Math.Round(ambient.R * scale), 0, 255),
                (byte)Math.Clamp(Math.Round(ambient.G * scale), 0, 255),
                (byte)Math.Clamp(Math.Round(ambient.B * scale), 0, 255));
        }

        var stops = ((LinearGradientBrush)Fade.Background).GradientStops;
        for (var i = 0; i < stops.Count && i < FadeAlphas.Length; i++)
            stops[i].Color = Windows.UI.Color.FromArgb(FadeAlphas[i], ink.R, ink.G, ink.B);
    }

    /// <summary>整宽版式左侧渐变幕的固定深墨，也是高对比度/没有取色时的退路。</summary>
    private static readonly Windows.UI.Color FadeDefaultInk = Windows.UI.Color.FromArgb(0xFF, 0x10, 0x12, 0x17);

    /// <summary>左侧渐变幕四档 alpha（左沿近实心、往图里散尽），取自标记里的原值。</summary>
    private static readonly byte[] FadeAlphas = [0xF0, 0xC2, 0x66, 0x00];

    /// <summary>台上那张的订阅撤掉。换人和离开树都走这里。</summary>
    private void Unwatch()
    {
        if (_current is null) return;

        _current.PropertyChanged -= OnSlideChanged;
        _current = null;
    }

    // ---- 屏上的样子 --------------------------------------------------------------

    /// <summary>
    /// 换剧照：新的那张放到台下那层上，两层对着淡入淡出，台上台下换个位置。
    /// <para>
    /// 已经在台上的那张（包括两边都还没有图的开场）就不动，否则解码晚到时同一张图会再淡一次。图还没解出来时
    /// 传进来的是空的，那就淡到底色上而不是把上一张的剧照留着 —— 上一张的画面配这一张的标题是句假话。
    /// </para>
    /// </summary>
    private void Paint(BitmapImage? picture)
    {
        PaintBackdropShade();
        PaintOpenButtonInk();

        // 台上没有图 = 「这一张还在取」：整条带底下扫一道光。这一句要摆在下面那个「同一张就不动」之前 ——
        // 开场第一趟传进来的正是 null，而那一刻台上的也是 null，提前返回的话这道光永远等不到人点亮。
        PictureShimmer.IsActive = picture is null;

        if (ReferenceEquals(FrontPicture, picture)) return;

        StopMotion("image");

        var rising = _second ? LayerA : LayerB;
        var falling = FrontLayer;

        Brush(rising).ImageSource = picture;
        _second = !_second;

        if (_restoringEntry && _paintingEntry)
        {
            rising.Opacity = 1;
            falling.Opacity = 0;
            return;
        }

        var board = new Storyboard();
        board.Children.Add(FadeTo(rising, 1, CrossFade, TimeSpan.Zero));
        board.Children.Add(FadeTo(falling, 0, CrossFade, TimeSpan.Zero));

        Play(board, () =>
        {
            rising.Opacity = 1;
            falling.Opacity = 0;
        }, "image");

    }

    /// <summary>
    /// 台上那层。两层轮着上，见 <see cref="Paint"/>。空 Grid 一层，图在它的背景画刷里（标记里那一段写着为什么
    /// 不是 <c>Image</c>）。
    /// </summary>
    internal Grid FrontLayer => _second ? LayerB : LayerA;

    internal bool ImageArrivalRunning => _motions.ContainsKey("image");

    /// <summary>台上那张图。读的地方有几处（换图前先比一比、自检报「还在取还是已上图」），所以收成一句。</summary>
    internal ImageSource? FrontPicture => Brush(FrontLayer).ImageSource;

    /// <summary>一层的背景画刷。标记里那两支 <c>ImageBrush</c> 是写死的，所以这里取不到就是标记被人改了。</summary>
    private static ImageBrush Brush(Panel layer) => (ImageBrush)layer.Background;

    /// <summary>
    /// 徽标：有就跟着字一起抬起来，没有就收起来。收起来而不是留一块空白 —— 「这个剧集没有徽标」和「徽标没画
    /// 出来」在屏上长得一样，而只有后一种是这份代码的错。
    /// <para>
    /// 抬的方向和延迟都和字块一样（<see cref="TextDrop"/>，延迟 0）：徽标 2026-09-05 从压在角上的一张图变成了
    /// 字块的第一行（「把徽标移到剧名上面」），而这一叠是从上往下错着抬起来的（<see cref="Rise"/>），所以它就是
    /// 第 0 拍。从前它反过来、从上面落下来一点（−12）、延迟两拍 —— 那一版它站在另一个角上，和这一叠没关系。
    /// </para>
    /// <para>
    /// 这一段没有并进 <see cref="Rise"/>：图是解码完才到的，晚到的那一张要单独再抬一次
    /// （<see cref="OnSlideChanged"/>），而那时字块早落定了。
    /// </para>
    /// <para>
    /// 淡入和位移都写在徽标外面那一格（<c>LogoRow</c>）上，不写在图自己身上：影子挂在那一格里，图单独淡入的话
    /// 影子会先一步整黑地站在那儿（见 <see cref="TextInk"/>）。
    /// </para>
    /// </summary>
    private void PaintLogo(BannerSlide slide)
    {
        StopMotion("logo");

        // 同一枚徽标又画一遍（整批重读沿用旧对象、Show(animate:false)）：不重播淡入。
        if (ReferenceEquals(LogoImage.Source, slide.Logo) && LogoRow.Visibility == slide.LogoVisibility)
        {
            if (slide.Logo is not null) LogoRow.Opacity = 1;
            return;
        }

        LogoImage.Source = slide.Logo;
        LogoRow.Visibility = slide.LogoVisibility;

        if (slide.Logo is null)
        {
            LogoRow.Opacity = 0;
            return;
        }

        if (_restoringEntry && _paintingEntry)
        {
            LogoRow.Opacity = 1;
            LogoShift.Y = 0;
            return;
        }

        LogoRow.Opacity = 0;
        LogoShift.Y = TextDrop;

        var board = new Storyboard();
        board.Children.Add(FadeTo(LogoRow, 1, Lift, TimeSpan.Zero));
        board.Children.Add(SlideTo(LogoShift, 0, Lift, TimeSpan.Zero));

        Play(board, () =>
        {
            LogoRow.Opacity = 1;
            LogoShift.Y = 0;
        }, "logo");
    }

    /// <summary>
    /// 精选标识、标题、元信息、简介与操作五行错拍上浮。系统关闭动画时同一条路径立即落定。
    /// </summary>
    internal void Rise()
    {
        StopMotion("text");
        var steps = Steps();
        var board = new Storyboard();

        for (var i = 0; i < steps.Length; i++)
        {
            var (element, shift) = steps[i];
            var delay = Stagger * i;

            element.Opacity = 0;
            shift.Y = TextDrop;

            board.Children.Add(FadeTo(element, 1, Lift, delay));
            board.Children.Add(SlideTo(shift, 0, Lift, delay));
        }

        Play(board, () =>
        {
            foreach (var (element, shift) in steps)
            {
                element.Opacity = 1;
                shift.Y = 0;
            }
        }, "text");
    }

    /// <summary>
    /// 抬起来的那四行，从上往下。**每一行给的是它外面那一格（<c>…Row</c>）而不是字自己** —— 影子挂在那一格里，
    /// 位移和淡入写在字上就会把影子留在原处（见 <see cref="TextInk"/>）。
    /// </summary>
    private (UIElement Element, TranslateTransform Shift)[] Steps() =>
    [
        (TitleRow, TitleShift),
        (CaptionRow, CaptionShift),
        (SynopsisRow, SynopsisShift),
        (Actions, ActionsShift)
    ];

    /// <summary>
    /// 跑一段有明确所有者的装饰动画。没进树、应用关闭动画、或系统关闭动画时直接落定；完成后撤掉故事板，
    /// 再写回终值。
    /// <para>
    /// 钟和进度条不走这里（见 <see cref="SyncTimer"/>）：它们是读数，不跟着这个开关停。
    /// </para>
    /// <para>
    /// 自检里这份控件没有 <c>XamlRoot</c>，那时 <c>Begin()</c> 既没人看也没有意义；而故事板照样是搭出来的，
    /// 所以 <c>SetTarget</c> 拿到一个空的变换（标记里漏了一个 <c>TranslateTransform</c>）在那里就抛。
    /// </para>
    /// <para>
    /// **<c>Completed</c> 上的那一遍 settle 不是装饰**：换一张的那一拍里，同一个元素可能先被淡出动画
    /// （上一张）再被淡入动画（这一张）碰过，两个故事板都活着时后到的 <c>Begin</c> 不保证把前一个的
    /// HoldEnd 掰掉 —— 09-10 那版左翼正是这么黑着的：图设上了、Opacity 停在 0（实拍＋日志量到）。落一遍
    /// 终值把这事兜死。
    /// </para>
    /// </summary>
    private void Play(Storyboard board, Action settle, string channel)
    {
        if (!_active || XamlRoot is null || !HomeMotion.AnimationsEnabled)
        {
            settle();
            return;
        }

        _motions[channel] = (board, settle);
        board.Completed += (_, _) =>
        {
            if (!_motions.TryGetValue(channel, out var current) || !ReferenceEquals(current.Board, board)) return;
            _motions.Remove(channel);
            board.Stop();
            settle();
        };
        board.Begin();
    }

    private void StopMotion(string channel)
    {
        if (!_motions.Remove(channel, out var motion)) return;
        motion.Board.Stop();
        motion.Settle();
    }

    private void StopMotions()
    {
        foreach (var channel in _motions.Keys.ToArray()) StopMotion(channel);
    }

    /// <summary>透明度动画。透明度和位移都由合成器自己跑，所以不需要 <c>EnableDependentAnimation</c>。</summary>
    private static DoubleAnimation FadeTo(DependencyObject target, double to, TimeSpan span, TimeSpan delay) =>
        Track(target, "Opacity", to, span, delay);

    private static DoubleAnimation SlideTo(TranslateTransform target, double to, TimeSpan span, TimeSpan delay) =>
        Track(target, "Y", to, span, delay);

    private static DoubleAnimation Track(DependencyObject target, string property, double to, TimeSpan span, TimeSpan delay)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(span),
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // 直接把对象交给故事板，而不是写 TargetName：名字只在运行时解析，而这条带上的目标有一半是变换而不是元素。
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);

        return animation;
    }

    // ---- 走一张 -----------------------------------------------------------------

    /// <summary>
    /// 往前或往后一张，到头再回来（<see cref="HomeCarousel.Step"/>）。翻过之后自动翻页的钟重新起算：刚自己
    /// 翻过去就被钟推走，是这类带子最惹人的一处。
    /// </summary>
    internal void Advance(int delta)
    {
        if (_slides.Count <= 1) return;

        Show(HomeCarousel.Step(_index, delta, _slides.Count));
        SyncTimer();
    }

    /// <summary>
    /// 台上这张和它左右各一张解码着，其余的丢掉。带上八张整宽剧照全留着是几十兆的画面，而
    /// <see cref="BannerSlide.Release"/> 之后再回来是一次磁盘读取 —— 字节还在图片仓库的缓存里。
    /// </summary>
    private void Prefetch()
    {
        // 初始绑定早于可视树时还不知道所属 Frame；Loaded 中的 Show 会重新发起。
        if (XamlRoot is null) return;
        var presentation = BrowsePresentation.WaitForImagesAsync(this);
        var back = HomeCarousel.Step(_index, -1, _slides.Count);
        var next = HomeCarousel.Step(_index, 1, _slides.Count);

        // 右栏那三张也要解码：它们是「接下来」的推荐，图没到就是三块深色矩形。
        var wanted = new HashSet<int> { _index, back, next };
        if (_railShown)
            foreach (var index in HomeCarousel.RailIndices(_index, _slides.Count)) wanted.Add(index);

        for (var i = 0; i < _slides.Count; i++)
        {
            if (wanted.Contains(i)) _ = _slides[i].EnsureAsync(presentation);
            else _slides[i].Release();
        }
    }

    /// <summary>
    /// 箭头在不在屏上。指针在带上、并且有第二张可翻才浮出来 —— 只有一张时两个箭头都点不动，而一个点了没反应的
    /// 按钮比没有按钮更难解释（同 <see cref="Momoka.Infrastructure.CardStrip.ArrowsFor"/> 那条注释）。
    /// </summary>
    private void SyncArrows()
    {
        var shown = (_hover || _focusWithin) && _slides.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        PrevButton.Visibility = shown;
        NextButton.Visibility = shown;
    }

    /// <summary>
    /// 自动翻页和进度条共用的那只钟重新起算：台上这一张从现在开始站满 <see cref="_dwell"/>，右下的条从 0 再走。
    /// 只有一张、还没进树、或者一张都没有时停着。
    /// <para>
    /// **只有这一只钟，条和翻页是同一份经过时间的两个读法**（<see cref="OnTick"/>）。从前条是一段
    /// <c>Duration = _dwell</c> 的故事板、翻页是另一只间隔 <c>_dwell</c> 的定时器，两只钟谁先到都不保证
    /// 另一边在同一帧 —— 2026-11-02「进度条经常会卡住不动，或者跑到一半回退」。
    /// </para>
    /// <para>
    /// **指针不再停表**：同一批反馈里「卡住不动」的另一半来处是悬停暂停 —— 指针停在窗口上半截（也就是这条带上）
    /// 是常态，那条路让钟和条一起停，移开又从零起算，看着就是「跑到一半回退」。悬停现在只管箭头浮不浮出来。
    /// 只有一张、还没进树、一张都没有，才是真的停。
    /// </para>
    /// <para>
    /// **钟和条不是装饰，不吃界面的动画开关**（2026-09-13「首页的轮播图貌似不会自己滚动了」：这台机器 Windows
    /// 的「动画效果」是关的，而 v0.0.6 起这道闸里还站着一条 <c>AnimationsEnabled</c>，关动画的机器上钟永远起不来，
    /// 八张剧照站成一张静画；同日「把下方那条线弄成进度条，跑满进度条就翻页」）。条报的是「还剩几秒翻页」这个
    /// 内容事实 —— 现在它是 <see cref="OnTick"/> 直接写的一格宽度，根本不过故事板，所以界面动画关着也照走。
    /// 真正的装饰 —— 换片的交叉淡入、字块抬升 —— 照旧跟着开关走（<see cref="Play"/>）。
    /// </para>
    /// </summary>
    private void SyncTimer()
    {
        CountdownScale.ScaleX = 0;
        _cycleStarted = Environment.TickCount64;

        _timer?.Stop();
        if (_slides.Count <= 1 || !_active) return;

        _timer?.Start();
    }

    /// <summary>
    /// 带多高、渐变幕站在哪儿、字块多宽。带高按版式走 <see cref="HomeCarousel.BandHeight"/>：整宽版式照旧是
    /// 带宽按 16:9 算出来的高的 76%（2026-09-09「把轮播图弄扁一些」）；商店版式有右栏时约 3:1、窄档回 16:9
    /// （2026-11-16 重做，按参考图）。**剧照的几何这里一句都不写** ——
    /// 两层是 <c>Stretch="UniformToFill"</c>、对齐没有写（默认 Stretch），它们自己撑满 Band 的内沿、图按自己
    /// 的比例放大到盖住整格。
    /// <para>
    /// **这里是 2026-09-11「去掉首页封面轮播图的黑边」砍得最狠的一处**：从前 <c>Resize</c> 要算图宽（带高÷0.9×
    /// 16÷9、再和带宽取 min）、两层的显式高（带高÷<c>KeptShare</c>）、左右各让出多宽（<c>gutter</c>），还有渐变
    /// 幕公式里的第一项。图铺满之后那三样全没了 —— 只剩「两层就是 Band 的内沿那么大」这一条，裁多少、裁在哪
    /// 交给 <c>UniformToFill</c> 自己算，而它算得对不对由 <see cref="PictureRead"/> 报出来。**别把图宽加回来**：
    /// 图一旦比带子窄（哪怕只窄一条边），屏上就是他要去掉的那条黑边。
    /// </para>
    /// <para>
    /// 渐变幕（左头近实心、走进图里散尽）从带子自己的左沿起、宽取「字块左沿 ＋ 字块最宽 ＋
    /// <see cref="ScrimBreath"/>」—— 字整块压在剧照上，盖不住就是亮底上读简介，所以 <c>Info.MaxWidth</c> 得先
    /// 算出来。从前还要和「左边纯色那一片的宽 ＋ 图宽的三成半」取大的那个（那一项是揉开纯色底和剧照之间那条
    /// 硬边用的），纯色底没了它也就没了。
    /// </para>
    /// <para>
    /// 字块 2026-09-05 走过两趟：先按「把红框框出来的移到右下角」挪去了右下角，同一天又按「移到左下角，然后把徽标
    /// 移到剧名上面」挪回左边、贴着下沿，徽标从压在角上的一张独立的图变成了这一叠的第一行。两趟一起丢掉的是「往下
    /// 沉一点」那条规则（从前的 <c>InfoShift</c> 加 <c>HomeCarousel.InfoDrop</c>）和 <c>PlaceLogo</c> 那一段留白
    /// 计算：站在字块里的徽标由布局给位置。2026-09-10 又两趟：先按「把红框里的东西移动到左上角」挪到顶上，再按
    /// 「字块放到封面居中的位置」改成竖向居中 —— 还是布局给位置（对齐说了话，连顶距的常数都没了），别把那条
    /// 下沉规则请回来。
    /// </para>
    /// <para>
    /// 整条带都是自己的：继续观看从前「压在图的下半截上」，后来当过第一屏右边那一栏（那一栏 2026-09-08 随着
    /// 「移除轮播图右边的媒体库」删掉了），所以字块不用让开谁 —— 底边那排分页横条从前也站在这条带上，
    /// 2026-09-13「删除封面中心下方的那个组件」整个删了。带宽就是卡片自己的宽（2026-11-12 晚起这一页是
    /// 四边留白的圆角大图，带子跟着那块圆角剪裁走，不再通栏到窗口的边）。
    /// </para>
    /// <para>
    /// 一屏是带高的上限（超宽屏上「带宽 × 六成 ÷ 16 × 9」算出来会比一屏还高），问的是 <c>XamlRoot.Size</c>，
    /// 也就是整个客户区 —— 和自检里 <c>BannerCardRead</c> 问的同一个数；量不到（自检里这份控件没有 XamlRoot）就是 0，
    /// 那时不封顶。
    /// </para>
    /// </summary>
    internal void Resize(double width)
    {
        var viewport = XamlRoot?.Size ?? default;
        var store = _layout == CarouselLayout.Store;

        // 商店版式：整条够宽才把大图收到左列、右边摆三张推荐；窄窗回整宽（右栏两列宽 0）。
        // 列宽和主卡的宽都从 Core 那几个纯函数来，标记里不写数（见 HomeCarousel.ShowsRail）。
        var rail = HomeCarousel.ShowsRail(_layout, width, _slides.Count);
        var main = HomeCarousel.MainWidth(width, rail);
        var railChanged = _railShown != rail;
        RailGapColumn.Width = new GridLength(rail ? HomeCarousel.RailGap : 0);
        RailColumn.Width = new GridLength(rail ? HomeCarousel.RailWidth(width) : 0);
        _railShown = rail;
        _mainWidth = main;
        SyncRail();

        // 摆进／收起右栏那一刻要重挑「谁该解码」：收起那几档刚被 Prefetch 放掉，摆回来得重新要。
        // 换幻灯片引起的张数变化在 Apply 里另走一遍 Show，两条路都汇到同一个 Prefetch。
        if (railChanged && ActualWidth > 0) Prefetch();

        // 带高按版式分两副形状（HomeCarousel.BandHeight）：整宽版式照旧；商店版式有右栏时约 3:1、
        // 放不下右栏的窄档回 16:9。Stage 只拿卡片那一截，Root 底下还给圆点那一行留位置
        // （见 UpdateRootHeight）。
        var height = HomeCarousel.BandHeight(_layout, viewport.Height, width, rail);
        _bandHeight = height;
        if (double.IsNaN(Stage.Height) || Math.Abs(Stage.Height - height) > 0.5) Stage.Height = height;
        Frame.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, main), height) };

        // 两层剧照不在这里给尺寸：空 Grid 由对齐撑满 Band 的内沿，图在背景那支 ImageBrush 里按 UniformToFill
        // 铺满它。**别把尺寸或 Image 控件加回来** —— 2026-09-11 第一趟试过 <c>Image Stretch="UniformToFill"</c>
        // （宽高都按内沿给死）：自检量到元素 1014×570 而带子 1014×341，实拍里图从带子顶上开始铺、下沿那截花田
        // 整片被裁掉，也就是裁在了顶上而不是居中。Image 按内容取尺寸这条脾气，标记里那一段写着。
        //
        // 几何这一层 2026-09-11 简化过一道：框成卡片那一版这里要按 Frame 那圈发丝线的内沿算（差一圈就是图左沿
        // 那道两像素的亮缝），现在 Frame 又是个没有线、没有留白的 Grid，内沿就是控件自己的宽，那一整段连同
        // 「按内沿算」的说法一起删了。
        Info.MaxWidth = Math.Min(Math.Max(0, main - InfoInset - 28), Math.Clamp(main * 0.48, 280, 620));
        // 固定简介这行的宽度，避免按文字内容测量时只改 MaxWidth 却没有实际收缩；其他行仍按原规则排版。
        SynopsisRow.Width = Info.MaxWidth * 0.75;
        Info.Spacing = height < 360 ? 8 : 12;
        LogoImage.MaxHeight = height < 360 ? 32 : 44;

        // 商店版式的大图窄一截、片名改走详情页头图那一档（36，可折两行）；整宽版式仍按原来的高度线走。
        // 翻页箭头同理：商店版式换成参考图那两颗小圆键，整宽版式保留 44DIP 的方键。
        TitleText.Style = (Style)Resources[store ? "EgBannerStoreTitleStyle" : "EgBannerDisplayTitleStyle"];
        TitleText.MaxLines = store || height >= 440 ? 2 : 1;
        var pageStyle = (Style)Resources[store ? "EgBannerPageRoundStyle" : "EgBannerPageButtonStyle"];
        if (!ReferenceEquals(PrevButton.Style, pageStyle))
        {
            PrevButton.Style = pageStyle;
            NextButton.Style = pageStyle;
        }

        SynopsisRow.Visibility = SynopsisVisibility(Current?.SynopsisVisibility ?? Visibility.Collapsed);
        SyncSlideStatus();

        Fade.Margin = new Thickness(0, 0, 0, 0);
        Fade.Width = Math.Min(main, InfoInset + Info.MaxWidth + ScrimBreath);

        PlaceInfo();
    }

    /// <summary>
    /// 渐变幕盖过字块尾巴多少。幕是那块字的底衬（见 <see cref="Resize"/> 那一句），右边留一口气再散尽，
    /// 免得最后一行字的右端正好停在幕的尽头、看着像被切了一刀。字块整个压在剧照上，幕必须自己兑现「盖住字」
    /// 这件事 —— 09-11 起尤其如此：带里没有纯色底了，字底下就是图。
    /// </summary>
    private const double ScrimBreath = 64;

    /// <summary>字块离带子左沿多远。见标记里 Info 那一段：让开的是翻页箭头那条窄栏。</summary>
    private const double InfoInset = 60;

    // ---- 事件 -------------------------------------------------------------------

    private void OnPlayClicked(object sender, RoutedEventArgs e)
    {
        if (Current is { } slide) PlayRequested?.Invoke(this, slide);
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (Current is { } slide) OpenRequested?.Invoke(this, slide);
    }

    private void OnPrevClicked(object sender, RoutedEventArgs e) => Advance(-1);

    private void OnNextClicked(object sender, RoutedEventArgs e) => Advance(1);

    /// <summary>
    /// 钟响一拍：条按「这一张站了多久」走到该走的位置，跑满一个停留时长就是换下一张的那一拍。
    /// 经过时间用 <see cref="Environment.TickCount64"/> 现算，界面线程忙了一拍、晚到的那一次也不会把进度
    /// 留在半路 —— 晚到就多走一点，到点就换。
    /// </summary>
    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_active || _slides.Count <= 1)
        {
            sender.Stop();
            return;
        }

        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - _cycleStarted);

        if (elapsed >= _dwell)
        {
            // Advance 里的 SyncTimer 重新起算，条也从 0 再来 —— 这一帧之后台上已经换人了。
            Advance(1);
            return;
        }

        CountdownScale.ScaleX = elapsed / _dwell;
    }

    /// <summary>
    /// 键盘翻页。箭头是悬停才浮出来的，收起来的按键接不了焦点，所以用键盘的人翻页靠这里 —— 带自己是
    /// <c>IsTabStop</c>，Tab 停在带上，左右方向键走片。
    /// </summary>
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_slides.Count == 0) return;

        switch (e.Key)
        {
            case VirtualKey.Left:
                e.Handled = true;
                Advance(-1);
                break;
            case VirtualKey.Right:
                e.Handled = true;
                Advance(1);
                break;
            case VirtualKey.Home:
                e.Handled = true;
                Show(0);
                SyncTimer();
                break;
            case VirtualKey.End:
                e.Handled = true;
                Show(_slides.Count - 1);
                SyncTimer();
                break;
        }
    }

    /// <summary>
    /// 台上那张的图解码好了。认一下是不是台上这张：走得快的时候上一张的解码会晚到，那张图不该盖在现在这张的
    /// 标题上面。
    /// </summary>
    private void OnSlideChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _current) || _current is not { } slide) return;

        if (e.PropertyName == nameof(BannerSlide.Picture))
        {
            Paint(slide.Picture);
            // 图到了，像素也就在了：左侧渐变的颜色和卡片四周的光晕此刻才算得出来。
            PaintFadeInk();
        }
        else if (e.PropertyName == nameof(BannerSlide.Logo)) PaintLogo(slide);
    }

    /// <summary>
    /// 一张幻灯片报上来「我取不到宽图」。排到下一拍再摘 —— 这句话可能是在 <see cref="Prefetch"/> 发起的那一趟
    /// 里同步回来的，当场改清单会把正在跑的 <see cref="Show"/> 掀掉。
    /// </summary>
    private void OnPictureUnavailable(object? sender, EventArgs e)
    {
        if (sender is not BannerSlide slide) return;

        if (!DispatcherQueue.TryEnqueue(() => DropSlide(slide))) DropSlide(slide);
    }

    /// <summary>
    /// 把取不到图的这张从这一批里摘掉，台上那张尽量不动（见 <see cref="HomeCarousel.AfterDrop"/>）。
    /// <para>
    /// 「刚入库的资源如果没有背景图或可用于轮播图的图片就不要放到轮播列表里」（2026-11-02 用户令）：服务器发下来的
    /// 标签说它有背景图、真去取却回 404 或解不出来，这种情况下留在带上的是一块深色矩形加几行字。摘掉之后还要
    /// 通知页面（<see cref="SlideDropped"/>），让清单那一头也少这一件，下一次重读才不会再把它带回来。
    /// </para>
    /// </summary>
    private void DropSlide(BannerSlide failed)
    {
        if (!_active) return;

        var at = _slides.IndexOf(failed);
        if (at < 0) return;

        var before = _slides.Count;
        var wasCurrent = at == _index;

        failed.PictureUnavailable -= OnPictureUnavailable;
        failed.Release();
        _slides.RemoveAt(at);

        if (_slides.Count == 0)
        {
            _index = 0;
            _second = false;
            LayerA.Opacity = 0;
            LayerB.Opacity = 0;
            Brush(LayerA).ImageSource = null;
            Brush(LayerB).ImageSource = null;
            PictureShimmer.IsActive = false;
            LogoImage.Source = null;
            LogoRow.Visibility = Visibility.Collapsed;
            Visibility = Visibility.Collapsed;
            _railShown = false;
            if (ActualWidth > 0) Resize(ActualWidth);
            else
            {
                HideRail();
                SyncSlideStatus();
            }
            SlideChanged?.Invoke(this, null);
            PaintFadeInk();
            SyncArrows();
            SyncTimer();
            SlideDropped?.Invoke(this, failed);
            return;
        }

        if (wasCurrent)
        {
            _index = HomeCarousel.AfterDrop(at, at, before);
            Show(_index);
            SyncTimer();
        }
        else
        {
            _index = HomeCarousel.AfterDrop(at, _index, before);
            SlideCounter.Text = $"{_index + 1:00}  /  {_slides.Count:00}";
            AutomationProperties.SetName(Root,
                $"主页轮播，{HomeCarousel.Position(_index, _slides.Count)}，{Current?.Title}");

            // 只剩一张时读数会收起来，钟也该停；多于一张时这一下不动它 —— 摘的是台下那张，台上这张的
            // 倒计时没有理由回退。
            SyncSlideStatus();
            if (_slides.Count <= 1) SyncTimer();
        }

        // 摘掉一张可能让右栏不再成立（掉到三张及以下就收起来），圆点也要少一颗；几何按新清单重算一遍。
        if (ActualWidth > 0) Resize(ActualWidth);
        else
        {
            HideRail();
            SyncSlideStatus();
        }

        SyncArrows();
        SlideDropped?.Invoke(this, failed);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _watch.Enter();
        _pointerGlow.Move(e.GetCurrentPoint(Root).Position,
            e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse);
    }

    private void PaintPointerGlowInk()
    {
        var color = ((SolidColorBrush)Application.Current.Resources["EgOnScrimBrush"]).Color;
        PointerGlowBrush.GradientStops[0].Color = color;
        color.A = (byte)(color.A * 0.45);
        PointerGlowBrush.GradientStops[1].Color = color;
        color.A = 0;
        PointerGlowBrush.GradientStops[2].Color = color;
    }

    private void OnBannerGotFocus(object sender, RoutedEventArgs e)
    {
        // 焦点只决定箭头在不在（键盘用户看不到悬停）。钟不停 —— 停在带上等一张图看完是常态，
        // 焦点在带里等它自己走完，比「走进来时进度条突然冻住」更像这条带该有的样子（2026-11-02）。
        _focusWithin = true;
        SyncArrows();
    }

    private void OnBannerLostFocus(object sender, RoutedEventArgs e)
    {
        // 焦点从一颗按钮走到另一颗时仍留在轮播里，等新的焦点落稳后再决定是否恢复。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active) return;
            var focused = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as DependencyObject : null;
            _focusWithin = false;
            for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (!ReferenceEquals(node, Root)) continue;
                _focusWithin = true;
                break;
            }

            SyncArrows();
        });
    }

    /// <summary>
    /// 指针离开。位置现查一遍，同 <see cref="ShelfStrip.OnPointerExited"/>：这个事件会从子元素冒上来，而指针踩
    /// 到刚浮出来的箭头上算「离开了带」，直接信它就会把手底下的按钮收掉。
    /// </summary>
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root).Position;

        if (point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight)
            return;

        _watch.Leave();
    }

    private void OnResized(object sender, SizeChangedEventArgs e)
    {
        Resize(e.NewSize.Width);
        PaintOpenButtonInk();
    }

    /// <summary>
    /// 窗口的客户区变了。只拖下边沿那一下带子自己没变尺寸，可它的上限变了（<see cref="_viewport"/>），所以照
    /// 现在的宽度重算一次。宽度是 0 的时候不算 —— 那是还没排过的那一帧，按 0 算会把带子压到下限，而那一下之后
    /// <c>SizeChanged</c> 自己会带着真宽度来。
    /// </summary>
    private void OnViewportChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (ActualWidth > 0) Resize(ActualWidth);
    }

    /// <summary>数据可能比 <c>Loaded</c> 先到，所以进树时再问一遍钟该不该走。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _active = true;
        _pointerGlow.SetEnabled(!_colorAccessibility.HighContrast && _uiSettings.AdvancedEffectsEnabled);
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
        ShellPrefs.AppearanceChanged += OnAppearanceChanged;
        ThemeHost.Changed -= OnAppearanceThemeChanged;
        ThemeHost.Changed += OnAppearanceThemeChanged;
        if (_appearanceSettings is { } appearance) ApplyAppearance(appearance);
        _colorSystemTimer ??= DispatcherQueue.CreateTimer();
        _colorSystemTimer.Interval = TimeSpan.FromSeconds(1);
        _colorSystemTimer.Tick -= OnColorSystemTick;
        _colorSystemTimer.Tick += OnColorSystemTick;
        _colorSystemTimer.Start();
        if (!_settingsObserved)
        {
            _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
            HomeMotion.PreferenceChanged += OnAnimationPreferenceChanged;
            _settingsObserved = true;
        }

        // 控件返回可视树时重新订阅当前图片；Unloaded 释放过的图允许从磁盘缓存重读。
        _restoringEntry = _everLoaded;
        _everLoaded = true;
        if (_slides.Count > 0)
        {
            // 同步恢复的缓存图保持可见；随后才到的图片和徽标仍走各自的淡入。
            _paintingEntry = true;
            try { Show(_index, animate: !_restoringEntry); }
            finally { _paintingEntry = false; }
        }
        SyncTimer();

        if (XamlRoot is { } root && !ReferenceEquals(_viewport, root))
        {
            UnwatchViewport(_viewport);
            _viewport = root;
            root.Changed += OnViewportChanged;

            // 进树的这一下窗口可能已经不是默认大小了（自检就会把窗口拉成几种尺寸），而带高是构造时算的。
            if (ActualWidth > 0) Resize(ActualWidth);
        }
    }

    /// <summary>
    /// 离开树：钟停掉、指针的监视撤掉、图全丢掉。主页换到别的页面时这条带整个不在屏上，而八张剧照还占着内存，
    /// 钟还每八秒解一次码。
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _colorSystemTimer?.Stop();
        _active = false;
        _appearanceLifetime++;
        ShellPrefs.AppearanceChanged -= OnAppearanceChanged;
        _focusWithin = false;
        ThemeHost.Changed -= OnAppearanceThemeChanged;
        _timer?.Stop();
        _watch.Leave();
        _pointerGlow.Leave(immediate: true);
        Unwatch();
        StopMotions();
        if (_settingsObserved)
        {
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            HomeMotion.PreferenceChanged -= OnAnimationPreferenceChanged;
            _settingsObserved = false;
        }

        UnwatchViewport(_viewport);
        _viewport = null;

        // 右栏三格退订并把图放掉：主页不在屏上时它们不该继续扣着三张解码好的剧照。
        HideRail();

        foreach (var slide in _slides) slide.Release();
        Brush(LayerA).ImageSource = null;
        Brush(LayerB).ImageSource = null;
        PictureShimmer.IsActive = false;
        LogoImage.Source = null;
    }

    private void OnColorSystemTick(DispatcherQueueTimer sender, object args)
    {
        _pointerGlow.SetEnabled(_active && !_colorAccessibility.HighContrast && _uiSettings.AdvancedEffectsEnabled);
        if (_active && _appearanceSettings is { } ui && (_lastColorContrast != _colorAccessibility.HighContrast
            || _lastColorEffects != _uiSettings.AdvancedEffectsEnabled)) ApplyAppearance(ui);
    }

    private void OnAnimationsEnabledChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(() =>
        OnAnimationPreferenceChanged());

    private void OnAnimationPreferenceChanged()
    {
        if (!_active) return;

        // 只收装饰那一批动画。钟和条是读数，不因这个开关重来 —— 重来一次就是进度条回一次零。
        StopMotions();
    }

    /// <summary>把窗口那一头的订退掉。<see langword="null"/> 进来就是「本来没订」。</summary>
    private void UnwatchViewport(XamlRoot? root)
    {
        if (root is not null) root.Changed -= OnViewportChanged;
    }

    private static void OnSlidesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((HomeBanner)sender).Apply(args.NewValue as IReadOnlyList<BannerSlide> ?? []);

    /// <summary>台上那张，一张都没有时是空的。</summary>
    internal BannerSlide? Current => _index >= 0 && _index < _slides.Count ? _slides[_index] : null;

    // ---- 自检 -------------------------------------------------------------------

    /// <summary>
    /// 这条带在 <c>--self-check</c> 里的那一份读数，同 <see cref="ShelfStrip.Probe"/>：自检时没有服务器，主页
    /// 一张幻灯片也没有，所以问的是不依赖数据的那几件事，加上这份标记自己能不能解析。
    /// <para>
    /// <c>new HomeBanner()</c> 就把标记走了一遍：里面每个资源键（<c>DefaultButtonStyle</c>、
    /// <c>EgOnScrimBrush</c>、四组改掉的按钮状态键、三层渐变）解析不了就在这里抛。渐变里写颜色键会在高对比度
    /// 下抛，也是这一句挡着的 —— 那本词典里只有画刷。
    /// </para>
    /// <para>
    /// 剩下那几件是屏上的行为里 Core 摸不到的部分：一张都没有时整条带收起来、带高按页宽落到布局上、
    /// **外面一圈圆角发丝框**（「弄个框把轮播图框起来（圆角）」，2026-09-10）、
    /// **两层剧照 <c>UniformToFill</c> 撑满整格**（「去掉首页封面轮播图的黑边」，2026-09-11 —— 判的是拉伸方式、
    /// 对齐和「尺寸正好是 Band 的内沿」三样，因为这三样少一样，屏上就回到「图比带子窄、一边露底色」那一版）、
    /// **字块竖向居左而徽标是它的第一行**（2026-09-05「移到左下角，然后把徽标移到剧名上面」、2026-09-10 居中）、
    /// **带上三层黑渐变各是各的形状**：左边那道渐变幕（2026-09-10，判的是从带沿起、左头九成浓、往图里走到全
    /// 透明，见 <see cref="Melts"/>），加下、右两条只压边缘的（「给轮播页面边缘加上黑色的渐变」，2026-09-05，判的
    /// 是每一层的形状而不是层数，见 <see cref="Rims"/>）、翻页箭头和字块不在同一列、两层剧照真的轮着上。字块那段
    /// 错拍动画顺带跑一遍，故事板里哪个目标是空的就在这里抛，而不是等到主页第一次换幻灯片。
    /// 底边那排分页横条 2026-09-13 整个删了（「删除封面中心下方的那个组件」），它那几判随元件一起退役。
    /// </para>
    /// </summary>
    internal static (bool Ok, string Detail) Probe()
    {
        var banner = new HomeBanner();

        // 一张都没有：整条带收起来（主页顶上不留一条空黑带），钟不走。
        banner.Apply([]);
        var quiet = banner.Visibility == Visibility.Collapsed && !banner.Ticking;

        // 带高落到布局上，而不只是算出来：扩展到原快捷区域后，1100 宽的封面为 470 高。
        // 这份控件没有 XamlRoot，量不到一屏有多高，所以那道「不超过一屏」的封顶这一趟不参与。
        banner.Resize(1100);
        var height = HomeCarousel.Height(0, 1100);
        var width = 1100d;
        var wide = banner.Info.MaxWidth;
        var tall = Math.Abs(banner.Root.Height - height) < 0.01 && wide <= 620
            && Math.Abs(banner.SynopsisRow.Width - wide * 0.75) < 0.01
            && banner.SynopsisRow.HorizontalAlignment == HorizontalAlignment.Left;

        // 框没有了（「占满窗口的上半部分（包括窗口标题）」，2026-09-11）：带子铺到窗口的三条边上，贴着窗口的
        // 边画圆角会在窗口角上剪出三角形的缺口，整圈发丝线也没有边可收，卡片底色更是要遮住剧照 —— 2026-09-10
        // 到 09-11 之间那套（Frame 是个 Border，圆角走词表、整圈走 EgHairline）整个作废。现在 Frame 是个没有
        // 圆角、没有线、没有留白的 Grid，只剩带子自己那支底色（图没解码到的那一瞬兜底）。
        var framed = banner.Frame.CornerRadius == default(CornerRadius)
            && banner.Frame.BorderThickness == default(Thickness);

        // 两层剧照铺满整条带（「去掉首页封面轮播图的黑边」，2026-09-11）：背后是 <c>ImageBrush Stretch="UniformToFill"</c>
        // —— 画刷按图自己的比例放大到盖住整格、多出来的一截对半裁掉（AlignmentX/Y 默认 Center），空 Grid 由对齐
        // 撑满整格，一个像素都不拉、两边也不再露底色。**判的是这三样**：网格里没有别的东西（有孩子就不再是「空壳
        // 撑满」）、背景是那支画刷、画刷的拉伸方式是 UniformToFill。裁多少、裁在哪由 PictureRead 在真实页面上量。
        var filled = BrushStretch(banner.LayerA) == Stretch.UniformToFill
            && BrushStretch(banner.LayerB) == Stretch.UniformToFill
            && banner.LayerA.Children.Count == 0 && banner.LayerB.Children.Count == 0
            && banner.LayerA.HorizontalAlignment == HorizontalAlignment.Stretch
            && banner.LayerA.VerticalAlignment == VerticalAlignment.Stretch
            && banner.LayerB.HorizontalAlignment == HorizontalAlignment.Stretch
            && banner.LayerB.VerticalAlignment == VerticalAlignment.Stretch
            && double.IsNaN(banner.LayerA.Width) && double.IsNaN(banner.LayerA.Height)
            && double.IsNaN(banner.LayerB.Width) && double.IsNaN(banner.LayerB.Height);

        // 字块竖向居中站在图上（「字块放到封面居中的位置」，2026-09-10），徽标是它的第一行（2026-09-05「移到
        // 左下角，然后把徽标移到剧名上面」）。徽标那一条判的是**它在字块里、而且排在片名前面**，不是它的坐标 ——
        // 它从前是压在角上的一张独立的图，谁把它挪回去，这一条当场红。三行字的 TextAlignment 一起判：整块靠左，
        // 字也得靠左。全从标记里设死的对齐上读，量的不是坐标。每一行外面套着一层 Grid（那层是影子的落脚处，见下
        // 面 inked），所以字块的头两个孩子是那两格、不是图和字本身 —— 图和字在各自那一格里另判一次。
        var corners = banner.Info.HorizontalAlignment == HorizontalAlignment.Left
            && banner.Info.VerticalAlignment == VerticalAlignment.Center
            && banner.Info.Children.Count > 2
            && ReferenceEquals(banner.Info.Children[0], banner.LogoRow)
            && ReferenceEquals(banner.Info.Children[1], banner.TitleRow)
            && banner.LogoRow.Children.Contains(banner.LogoImage)
            && banner.TitleRow.Children.Contains(banner.TitleText)
            && banner.TitleText.TextAlignment == TextAlignment.Left
            && banner.CaptionText.TextAlignment == TextAlignment.Left
            && banner.SynopsisText.TextAlignment == TextAlignment.Left;

        // 四行字底下那层跟着字形走的影子（<see cref="TextInk"/>）。判的是**宿主排在字前面**这件事：挂上去的那层
        // 视觉画在宿主自己的内容之上，所以宿主排到字后面就是影子盖在字上 —— 而那在截图里是「字脏了」，不是
        // 「少了一层」，最难看出来。影子本身画得对不对只有亮剧照上看得出，那是截图的活。
        var inked = Inked(banner.LogoRow, banner.LogoInk, banner.LogoImage)
            && Inked(banner.TitleRow, banner.TitleInk, banner.TitleText)
            && Inked(banner.CaptionRow, banner.CaptionInk, banner.CaptionText)
            && Inked(banner.SynopsisRow, banner.SynopsisInk, banner.SynopsisText);

        // 字块 2026-09-10 竖向居中：顶距这一份没有了（对齐说了话），左右两份还在 —— 左边距让开箭头那条窄栏，
        // 字块自己的上下 Margin 该是 0。
        var placed = Math.Abs(banner.Info.Margin.Left - InfoInset) < 0.01
            && Math.Abs(banner.Info.Margin.Top) < 0.01
            && Math.Abs(banner.Info.Margin.Bottom) < 0.01;

        // 三层阅读遮罩：左侧按文字范围散尽，顶侧保护标题栏，右侧只压边缘。底部不再添加渐变。
        var scrims = banner.Band.Children.OfType<Border>().ToList();
        var sized = scrims.Where(one => double.IsNaN(one.Height) && !double.IsNaN(one.Width)).ToList();
        var edges = scrims.Where(one => double.IsNaN(one.Height) && double.IsNaN(one.Width)).ToList();
        var topped = scrims.Where(one => one.VerticalAlignment == VerticalAlignment.Top && !double.IsNaN(one.Height)).ToList();
        var bare = scrims.Count == 3
            && sized is [{ } fade]
            && fade.HorizontalAlignment == HorizontalAlignment.Left
            && Math.Abs(fade.Margin.Left) < 0.01
            && Math.Abs(fade.Width - Math.Min(width, InfoInset + wide + ScrimBreath)) < 0.01
            && Melts(fade)
            && topped is [{ } top]
            && top.VerticalAlignment == VerticalAlignment.Top
            && Caps(top)
            && edges.Count == 1
            && edges.TrueForAll(Rims)
            && scrims.All(one => one.VerticalAlignment != VerticalAlignment.Bottom);

        // 翻页箭头和字块不同列（「翻页的按钮会挡住字体」）。箭头贴着带的左右边沿、在竖向正中；字块贴着左上角，
        // 左箭头贴边占的那条窄栏在另一列 —— 所以这一条判的仍然是列，不是高度。
        // 两边都从设死的边距和宽度上读，箭头默认是收起的、量不到位置，而这两个数收起来照样在。
        var strip = banner.PrevButton.Margin.Left + banner.PrevButton.Width;
        var apart = banner.Info.Margin.Left >= strip + 8;

        // 两层轮着上：换两次图，两层各站过一次台；同一张再来一次不再换层，也不白跑一次淡入淡出。
        var one = new BitmapImage();
        var two = new BitmapImage();

        banner.Paint(one);
        var onB = ReferenceEquals(banner.FrontLayer, banner.LayerB) && ReferenceEquals(banner.FrontPicture, one);

        banner.Paint(two);
        var onA = ReferenceEquals(banner.FrontLayer, banner.LayerA) && ReferenceEquals(banner.FrontPicture, two);

        banner.Paint(two);
        var layered = onB && onA
            && ReferenceEquals(banner.FrontLayer, banner.LayerA)
            && banner.LayerA.Opacity == 1
            && banner.LayerB.Opacity == 0;

        // 字块那段错拍动画。没进树，所以故事板搭出来之后直接落到终值。
        banner.Rise();
        var risen = banner.TitleRow.Opacity == 1 && banner.TitleShift.Y == 0 && banner.ActionsShift.Y == 0;

        // 商店版式换档（2026-11-16 重做）：没有幻灯片时摆不了右栏，走的是窄档 16:9；带高落在 Stage 上，
        // Root 不带圆点那一行；片名换成详情头图那一档。圆点的墨从应用主题色取得到 —— 2026-11-16 的崩溃
        // 正是从控件自己的字典里找不到那支固定浅墨（EgBannerDotBrush 已经删掉）。
        banner.ApplyLayout(CarouselLayout.Store);
        banner.Resize(1200);
        var storeBand = HomeCarousel.StoreHeight(0, 1200, false);
        var store = banner.Layout == CarouselLayout.Store
            && Math.Abs(banner.Stage.Height - storeBand) < 0.01
            && Math.Abs(banner.Root.Height - storeBand) < 0.01
            && banner.PageDots.Visibility == Visibility.Collapsed
            && ReferenceEquals(banner.TitleText.Style, banner.Resources["EgBannerStoreTitleStyle"]);
        var dotInk = DotInk(banner, false) is not null && DotInk(banner, true) is not null;

        var ok = quiet && tall && framed && filled && corners && inked
            && placed && bare && layered && risen && apart && store && dotInk;

        return (ok,
            $"没有幻灯片时{(quiet ? "整条带收起、钟不走" : "带还在屏上或钟在走")}；"
                + $"带高＝带宽×{HomeCarousel.BandHeightShare:0%}÷16×9（上限一屏、下限 {HomeCarousel.MinHeight:0}）："
                + $"带宽 1100→{height:0}；"
                + (framed
                    ? "带子没有框：圆角、发丝线、卡片边一概没有，铺到窗口的三条边上；"
                    : "带子上还留着一圈框（圆角或边线没删干净）；")
                + (filled
                    ? "剧照两层是空的格子加一支 UniformToFill 的画刷（按图自己的比例放大到盖住整格、多出来的一截对半裁掉；两边不留底色）；"
                    : "剧照两层没有铺满整格：网格里有别的东西、背景不是画刷、或者拉伸方式不是 UniformToFill；")
                + $"字块宽 页宽1100→{wide:0}；"
                + (corners
                    ? "字块竖向居中、三行字靠左，精选标识、徽标与片名依次排列；"
                    : "字块没有竖向居中，或者精选标识、徽标、片名次序不对，或者有一行字没靠左；")
                + (inked
                    ? $"四行字底下各一层跟着字形走的影子（模糊 {TextInk.SmallBlur:0}，片名那行 {TextInk.TitleBlur:0}）；"
                    : "字底下那层影子的宿主不在字前面（影子会盖在字上）；")
                + (placed
                    ? $"字块离左沿 {InfoInset:0}、上下零边距（竖向居中由对齐管）"
                    : "字块的左右边距或上下边距不对")
                + "；"
                + $"画面上的暗罩 {scrims.Count} 层"
                + (bare
                    ? $"（一道渐变幕，从 {sized[0].Margin.Left:0} 起、"
                        + $"宽 {sized[0].Width:0} ＝ min(页宽, 字块右沿 {InfoInset:0}+{wide:0}＋{ScrimBreath:0})，"
                        + $"左头九成浓往图里散尽；顶上给标题栏垫底的一条 {topped[0].Height:0} 高；右侧只压边缘，底部没有渐变："
                        + $"{string.Join('、', edges.Select(RimRead))}）"
                    : "（该是三层：顶上一条给标题栏垫底、一道左侧渐变幕和右侧边缘，底部不加渐变）")
                + "；"
                + $"箭头占到 {strip:0}、字块从 {banner.Info.Margin.Left:0} 起"
                + $"{(apart ? "，两边不同列" : "，压到字了")}；"
                + $"两层剧照{(layered ? "轮着上，同一张不重来" : "没换过位置")}；"
                + $"五行错 {Stagger.TotalMilliseconds:0} 毫秒、{Lift.TotalMilliseconds:0} 毫秒抬起 {TextDrop:0} 像素"
                + $"{(risen ? "后落定" : "但没落定")}，换图 {CrossFade.TotalMilliseconds:0} 毫秒；"
                + $"没人碰时每 {banner._dwell.TotalSeconds:0} 秒走一张（设置 → 主页 → 封面轮换秒数），最多 {HomeCarousel.MaxSlots} 张"
                + "；"
                + (store
                    ? $"商店版式换档后窄档带高 {storeBand:0}（16:9）、片名走 36 号档、没有多张时圆点不占位"
                    : "商店版式换档没落对（带高、片名样式或圆点可见性不对）")
                + $"；圆点的墨{(dotInk ? "从应用主题色取得到（不再找控件里那支已删的固定浅墨）" : "取不到，会当场抛")}");
    }

    /// <summary>
    /// 自检用：圆点的墨画得出来。这一句专门盯 2026-11-16 那次「Cannot find a resource with the given key:
    /// EgBannerDotBrush」—— 那一版从 <c>Application.Current.Resources</c> 找一支只写在控件字典里的画刷，
    /// 窗口一拖就抛。现在两支都来自应用主题色，取不到就会在这里红，而不是等用户去拉窗口。
    /// </summary>
    private static Brush? DotInk(HomeBanner banner, bool active)
    {
        try
        {
            return banner.PageDotBrush(active);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 一行字底下那层影子挂对了没有：宿主在这一格里、而且排在字**前面**。挂上去的那层视觉画在宿主自己的内容之上，
    /// 所以次序反了就是影子盖在字上（见 <see cref="TextInk"/>）。
    /// </summary>
    private static bool Inked(Grid row, UIElement ink, UIElement text) =>
        row.Children.Count >= 2 && ReferenceEquals(row.Children[0], ink) && ReferenceEquals(row.Children[1], text);

    /// <summary>
    /// 左边那道渐变幕的形状：**横向**（浓的那一头在左）、贴沿那头近实心（#E6、九成 —— 字块站的那一头压得够黑，
    /// 图还透得出来），往图里走到全透明。它左头在带子的边上、右头过图中线也没到半张带，所以不跟下、右两条走
    /// <see cref="Rims"/> 那句「半张之前就散尽」—— 这一条管的正是「给那块字垫底」。
    /// </summary>
    private static bool Melts(Border layer) =>
        layer.Background is LinearGradientBrush { StartPoint.X: 0, EndPoint.X: 1 }
            && Ramp(layer) is [{ } first, .., { } last]
            && first.Color.A >= 0xD0
            && last.Color.A == 0;

    /// <summary>
    /// 顶边那条暗罩（<c>Top</c>，「让首页轮播图占满窗口的上半部分（包括窗口标题）」，2026-09-11）的形状：
    /// **竖向**（浓的那一头在窗口顶边）、贴沿那头够浓、往下一路散尽。它压的是标题栏那一整段，所以要伸到 120
    /// 像素那么深 —— 比 <see cref="Rims"/> 那两句「半张之前散尽」深得多，判法因此单独一条：只要求浓头在
    /// StartPoint 那一侧、有一处到 0，**不限制 0 出现在哪儿**。
    ///
    /// 不把它塞进 Rims：那一条管的是下、右两条「半张之前散尽、之后一点不压」的收边层，顶上这条是另一种东西
    /// （给浅墨垫底用的压顶），硬套只会让两种形状互相迁就。
    /// </summary>
    private static bool Caps(Border layer) =>
        layer.Background is LinearGradientBrush { StartPoint.Y: 0, EndPoint.Y: 1 }
            && Ramp(layer) is [{ } first, ..]
            && first.Color.A > 0x40
            && Ramp(layer).Any(stop => stop.Color.A == 0);

    /// <summary>
    /// 一条边缘渐变的形状，三条一个样子：<c>Offset</c> 0 那一头（也就是它自己贴的那条边）够浓，**到半张处已经全
    /// 透明、半张之后一点不压**。后面这一句才是「只压边缘」和「压住整张画面」的分界线 —— 被删掉的那三层里横着
    /// 那一层正是从左沿一路淡到右沿的，它过不了这一条。见 HomeBanner.xaml 里那一段。
    /// </summary>
    private static bool Rims(Border layer)
    {
        var ramp = Ramp(layer);

        return ramp is [{ } first, ..]
            && first.Color.A > 0x40
            && ramp.Any(stop => stop.Color.A == 0 && stop.Offset <= 0.5)
            && ramp.All(stop => stop.Offset <= 0.5 || stop.Color.A == 0);
    }

    /// <summary>
    /// 一条边缘渐变在报告里的读数：贴的是哪条边、贴边那一头多浓、走到哪儿散尽（比如「左 55%→0.33」）。
    /// <para>
    /// 这两个数是他在屏幕前挑的（「渐变弄淡一些，面积弄少一些」，2026-09-05 —— 见 HomeBanner.xaml 里那张表），
    /// 而 <see cref="Rims"/> 故意只守「贴边那头够浓、半张之前散尽」这条更松的底线：**它钉的是「只压边缘」这件事
    /// 本身，不是他挑的浓度**。所以浓度和深度得由报告说出来，否则屏上淡了一档、报告一个字不动，等于没人看着它。
    /// 贴哪条边不从标记的次序猜，问渐变自己从哪个角起 —— 谁把三层的次序换了，这一读照旧说得对。
    /// </para>
    /// </summary>
    private static string RimRead(Border layer)
    {
        var ramp = Ramp(layer);

        if (layer.Background is not LinearGradientBrush brush || ramp.Count == 0)
        {
            return "读不出来（不是线性渐变）";
        }

        var side = brush.StartPoint.X == 0 ? "左"
            : brush.StartPoint.X == 1 ? "右"
            : brush.StartPoint.Y == 1 ? "下" : "上";
        var gone = ramp.FirstOrDefault(stop => stop.Color.A == 0);
        var where = gone is null ? "没散尽" : $"{gone.Offset:0.00}";

        return $"{side} {ramp[0].Color.A / 255d * 100:0}%→{where}";
    }

    /// <summary>一层暗罩的渐变，按 <c>Offset</c> 排好。不是线性渐变就交回空的，那时上面两条都判假。</summary>
    private static IReadOnlyList<GradientStop> Ramp(Border layer) =>
        layer.Background is LinearGradientBrush brush
            ? [.. brush.GradientStops.OrderBy(stop => stop.Offset)]
            : [];

    /// <summary>自检用：自动翻页的钟在不在走。</summary>
    private bool Ticking => _timer?.IsRunning ?? false;

    /// <summary>
    /// 自检用：这条带在真页面上的读数。<see cref="Probe"/> 拿假图证规则，这一句证的是规则之外的事 ——
    /// 带在 <c>ScrollView</c> 的竖排里真量到了宽度、那个宽度真变成了高度、台上那张的剧照真解码进了前面那层。
    /// 少一样，屏上就是一条空带，而 <see cref="HomeViewModel.BannerSummary"/> 报的张数照旧好看。
    /// <para>
    /// 字块竖向居中之后「它有没有超出带子」只有屏上量得出来：报字块的高和「带高 − 字块高」上下各摊的一半。
    /// 这一位变成负数，就是简介和按键那一头伸出带的下沿（下限 240 那一档正是这样，所以它继续报）。
    /// </para>
    /// <para>
    /// 带自己的形状也报作诊断：剧照铺满整条带，图按自己的比例放大到盖住整格、多出来的一截对半裁掉 —— 这一句把
    /// 「原图的比例进这条带要裁掉多少」算出来（16:9 进 2.96:1 是上下各两成，屏上那一版就是他说的没有黑边了）。
    /// 渐变幕（<c>Fade</c>）的 Width 是代码摆的，连着它到没到字块的尾巴一起说。屏上是不是真这样，由
    /// <see cref="PictureRead"/> 在真实页面上量。
    /// </para>
    /// </summary>
    internal string State
    {
        get
        {
            if (_slides.Count == 0) return "收起";

            var height = _bandHeight;
            var viewport = _viewport?.Size.Height ?? 0;
            var fade = double.IsNaN(Fade.Width) ? 0 : Fade.Width;
            var spare = (height - Info.ActualHeight) / 2;
            var bandShape = Band.ActualHeight > 0 ? Band.ActualWidth / Band.ActualHeight : 0;

            return $"带高 {height:0}、剧照{(_layout == CarouselLayout.Store ? "原图铺满卡片（商店版式不扩边）" : $"左侧采色延长 {CarouselBackdrop.LeftExtensionShare:0%} 后铺满整条带")}{Describe(FrontPicture as BitmapImage, bandShape)}"
                + $"（渐变幕 {fade:0} 宽）、字块宽 {Info.MaxWidth:0}、"
                + $"字块高 {Info.ActualHeight:0} 竖向居中（上下各 {spare:0}）、"
                + $"带 {bandShape:0.00}:1"
                + $"（窗口高 {viewport:0}）、"
                + $"{HomeCarousel.Position(_index, _slides.Count)}、"
                + $"剧照{(FrontPicture is null ? "还在取" : "已上图")}、"
                + $"徽标{(LogoImage.Source is null ? "无" : "有")}";
        }
    }

    /// <summary>
    /// 「这张图铺进这条带要裁掉多少」的一句话读数，给 <see cref="State"/> 用。图还没到、或者它自己的尺寸问不到
    /// （不是 <c>BitmapImage</c>）就不说 —— 那是网络和类型的事，不是版面的事。
    /// </summary>
    private static string Describe(BitmapImage? source, double bandShape)
    {
        if (source is not { PixelWidth: > 0, PixelHeight: > 0 } || bandShape <= 0) return "";

        var shape = (double)source.PixelWidth / source.PixelHeight;
        var (cropX, cropY) = Crop(shape, bandShape);

        return $"（背景位图 {source.PixelWidth}×{source.PixelHeight} = {shape:0.00}:1，"
            + (cropX > 0 ? $"左右各裁 {cropX:P0}" : $"上下各裁 {cropY:P0}")
            + "，比例不变）";
    }

    /// <summary>
    /// 图铺满整格时被裁掉多少，按两条轴各报一个比例（0 就是这一轴一个像素都没裁）。
    /// <para>
    /// <c>UniformToFill</c> 不给外面看它裁在哪儿，而能算的原因是两边的比例都问得到：把它放大到刚好盖住整格，
    /// 富余的那一轴就是对半裁掉的那一轴。带子 2.96:1 比 16:9 的剧照宽，所以裁的是上下；哪天真来一张比带子还宽
    /// 的图（5:1 的横幅），被裁的就该换成左右。
    /// </para>
    /// </summary>
    private static (double X, double Y) Crop(double sourceShape, double bandShape) =>
        sourceShape <= 0 || bandShape <= 0 ? (0, 0)
            : sourceShape > bandShape ? (1 - bandShape / sourceShape, 0)
            : (0, 1 - sourceShape / bandShape);

    /// <summary>
    /// 自检：剧照真铺满了整条带 —— 「去掉首页封面轮播图的黑边」（2026-09-11）。
    /// <para>
    /// 量的是台上那一层**空 Grid** 的尺寸和它在带子里的位置：那两层由对齐撑满整格，图在背景那支
    /// <c>ImageBrush Stretch="UniformToFill"</c> 里按自己的比例放大到盖住整格、多出来的一截对半裁掉。格子比带子
    /// 小、或者站偏了，屏上就是一侧露出底色 —— 那正是他要拿掉的那两条黑边，而在截图里它长得像「照片没占满」，
    /// 不像一个错。**这一读之所以量得到，是因为图在画刷里而不是在 <c>Image</c> 控件里**：后者的
    /// <c>ActualWidth/Height</c> 报的是画出来那张图的大小（2026-09-11 第一趟就是它报的 1014×570，而带子
    /// 1014×341），照它判会把「图铺到带子外面去了」当成「铺满了」。
    /// </para>
    /// <para>
    /// 裁掉多少是**算出来的，不是量出来的**：整格铺满之后尺寸就是整格尺寸，图裁在哪儿从元素上读不到，但这件
    /// 事本来就只由两个比例决定（带子的、原图的），所以这一读把它们算出来报给人看 —— 「16:9 的剧照进 2.96:1 的
    /// 带，上下各裁两成」。判的是**只有一条轴被裁**：两条都裁说明这个拉伸方式被换掉了（<c>Fill</c> 会同时裁两轴
    /// 还把图拉变形），一条都不裁只可能是带子和图正好同形。
    /// </para>
    /// <para>
    /// 图还没解码回来时没有得量，那一档只报不判 —— 那是网络的事，不是版面的事。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail) PictureRead()
    {
        var layer = FrontLayer;
        var band = (Width: Band.ActualWidth, Height: Band.ActualHeight);
        var drawn = (Width: layer.ActualWidth, Height: layer.ActualHeight);
        var picture = FrontPicture;

        if (picture is null || drawn.Width <= 1 || drawn.Height <= 1)
            return (true, $"剧照还没解出来，带 {band.Width:0}×{band.Height:0}");

        var at = layer.TransformToVisual(Band).TransformPoint(new Windows.Foundation.Point(0, 0));
        var left = at.X;
        var scale = layer.RenderTransform as ScaleTransform;
        var paintedWidth = drawn.Width * (scale?.ScaleX ?? 1);
        var paintedHeight = drawn.Height * (scale?.ScaleY ?? 1);
        var right = band.Width - (at.X + paintedWidth);

        // 图片保持单位缩放；布局格子与画框一致，只让开 PictureClip 那一圈（商店版式 1 像素、沉浸 0），
        // 不留别的底色边缘 —— 那 1 像素是拿来盖掉圆角弧线上的亮边的，见标记里 PictureClip 那一段。
        var inset = PictureClip.Margin.Left;
        var covers = Math.Abs(drawn.Width - (band.Width - 2 * inset)) <= 1.5
            && Math.Abs(drawn.Height - (band.Height - 2 * inset)) <= 1.5
            && Math.Abs(left - inset) <= 1.5 && Math.Abs(right - inset) <= 1.5
            && Math.Abs(at.Y - inset) <= 1.5
            && Math.Abs(at.Y + paintedHeight - (band.Height - inset)) <= 1.5;

        var source = picture as BitmapImage;
        var sourceShape = source is { PixelWidth: > 0, PixelHeight: > 0 }
            ? (double)source.PixelWidth / source.PixelHeight
            : 0;

        var bandShape = band.Height > 0 ? band.Width / band.Height : 0;
        var (cropX, cropY) = Crop(sourceShape, bandShape);

        var oneAxis = sourceShape > 0 && (cropX <= 0.001 || cropY <= 0.001);

        return (covers && oneAxis,
            $"剧照铺满整条带 {drawn.Width:0}×{drawn.Height:0}（画刷那层就是整格，左 {left:0.#}、上 {at.Y:0.#}）"
                + (sourceShape > 0
                    ? $"；背景位图 {source!.PixelWidth}×{source!.PixelHeight} = {sourceShape:0.00}:1（"
                        + (_layout == CarouselLayout.Store
                            ? "商店版式用原图，不扩边、不做过渡高斯"
                            : $"左侧 {CarouselBackdrop.LeftExtensionShare:0%} 采色扩边，全扩边高斯经原图左8%平滑恢复清晰，图片静止")
                        + "）"
                        + (cropX > 0
                            ? $" 比带子宽，左右各裁 {cropX:P0}"
                            : cropY > 0 ? $" 比带子窄，上下各裁 {cropY:P0}" : " 和带子同形，一个像素都不裁")
                        + "（比例不变，多出来的一截对半裁）"
                    : "；问不到原图尺寸")
                + $"；带 {band.Width:0}×{band.Height:0} = {bandShape:0.00}:1（左右余量 {left:0.#}/{right:0.#}，不大于 0 表示铺满）"
                + (covers ? "" : " —— 有一边没铺满，屏上就是一条底色边")
                + (oneAxis ? "" : "；裁的不止一轴或没裁，拉伸方式可能被换掉了")
                + $"；字块最宽 {Info.MaxWidth:0}");
    }

    /// <summary>
    /// 自检：带上那三行字真解析到的字体和字号，以及三颗键真拿到的高度。
    /// <para>
    /// 和详情页头图上那条（<c>DetailPage.HeroType</c>）是同一件事，值得两处各查一遍：这两块是整个界面上最
    /// 大的两个画面，而它们的版式来自两份互不相干的标记。这一版之前带上的片名写死了 40 和 Bold 却没写字
    /// 体，于是主页最大的一块字用的是继承来的正文字，屏上看得见、截图里看不出。
    /// </para>
    /// <para>
    /// 比的是元素真解析到的族名和它该有的那个键，所以 Bahnschrift 装没装都不影响判断 —— 那件事由 字体已解析
    /// 那条管。键高一起量，是因为「整套界面只有一个按键高度」这句话在这条带上要兑现三次：播放、详情、翻页
    /// 箭头，以前它们是 40、40、44。
    /// </para>
    /// </summary>
    internal (bool Ok, string Detail) TypeRead()
    {
        var display = Face("EgDisplayFontFamily");
        var data = Face("EgDataFontFamily");
        var ui = Face("EgUiFontFamily");
        var size = Number("EgDisplayFontSize");
        var height = Number("EgActionHeight");

        var ok = TitleText.FontFamily.Source == display
            && Same(TitleText.FontSize, size)
            && CaptionText.FontFamily.Source == data
            && SynopsisText.FontFamily.Source == ui
            && Same(PlayButton.Height, height)
            && Same(OpenButton.Height, height)
            && Same(NextButton.Height, height);

        return (ok,
            $"片名 {TitleText.FontFamily.Source} {TitleText.FontSize:0}、"
                + $"读数 {CaptionText.FontFamily.Source} {CaptionText.FontSize:0}、"
                + $"简介 {SynopsisText.FontFamily.Source} {SynopsisText.FontSize:0}、"
                + $"键高 {PlayButton.Height:0}/{OpenButton.Height:0}/{NextButton.Height:0}"
                + (ok
                    ? ""
                    : $" —— 要的是片名「{display}」{size:0}、读数「{data}」、简介「{ui}」、键高 {height:0}"));

        static string Face(string key) => ((FontFamily)Application.Current.Resources[key]).Source;

        static double Number(string key) => (double)Application.Current.Resources[key];

        static bool Same(double left, double right) => Math.Abs(left - right) < 0.01;
    }

    /// <summary>自检用：一层剧照那支背景画刷的拉伸方式。标记里写死的，所以取不到就抛。</summary>
    private static Stretch BrushStretch(Panel layer) => Brush(layer).Stretch;
}

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
/// 「剧集页面、电影页面和季页面进入最大化状态时，去掉下方组件的背景遮罩，并将下方组件整体上移，并添加动画过渡
/// 效果」（2026-10-31 用户令，附参考图）。
/// <para>
/// 版面的判据在 Core（<see cref="DetailHero.Immersive"/>）和视图模型（<c>DetailViewModel.Showcase</c>）：
/// 最大化、宽版式、背后有图、电影/剧/季四句同时成立。这一支只管屏上那三样东西 —— 正文那张纸的底色、五块正文
/// 面板的底与框、尾部补第一屏的那段留白 —— 以及最大化/还原那一下怎么走。
/// </para>
/// <para>
/// <b>三样一起走，才是「组件整体上移」。</b>尾部高度是布局属性（<see cref="FrameworkElement.MinHeightProperty"/>），
/// 只能由 UI 线程一帧一帧重排；纸面颜色和面板底色的透明度是画刷自己的属性，由合成器跑。同一段时间、同一条缓动，
/// 屏上读起来才是「纸面化开、正文顺着尾部收起来的方向升上去」而不是三件互不相干的事。位移为什么不另做动画：
/// 正文和尾部在同一个网格里首尾相接，尾部收多少、正文就升多少，这是布局给的；再叠一层位移只会把两者拉开一道缝。
/// </para>
/// <para>
/// 只动 <c>MinHeight</c>、<c>SolidColorBrush.Color</c> 和 <c>SolidColorBrush.Opacity</c>，只走 XAML Storyboard，
/// 不用 composition —— 理由同 <see cref="HomeFoldMotion"/> 上那一段（<c>GetElementVisual</c> 与框架自己的
/// <c>Scale</c> 抢同一份 visual，界面线程当场带走了进程）。
/// </para>
/// </summary>
public sealed partial class DetailPage
{
    /// <summary>这一趟走多久。同 <see cref="HomeFoldMotion"/>：两百六十毫秒，比播放页那 220 长一点，因为挪的是
    /// 整段正文而不是两层界面的溶解。</summary>
    private static readonly TimeSpan ShowcaseSpan = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// 正文那张纸的底色。这一份是这一页自己的，不是全局那支 <c>EgWindowBrush</c>：沉浸档要把它淡成尾部那道
    /// 半透明黑（<see cref="DetailHero.ScrimCeiling"/> 那一档），改全局画刷会把别的页面一起改掉。
    /// </summary>
    private readonly SolidColorBrush _sheetBrush = new();

    /// <summary>五块正文面板的底 —— 沉浸档里淡到 0，纸面让开之后不再有「板里的板」那一层。</summary>
    private readonly SolidColorBrush _panelFill = new();

    /// <summary>五块正文面板的描边，同 <see cref="_panelFill"/>。</summary>
    private readonly SolidColorBrush _panelLine = new();

    /// <summary>这一趟的 Storyboard；换向或收尾时整份停掉，理由见 <see cref="HomeFoldMotion"/>。</summary>
    private Storyboard? _showcaseBoard;

    /// <summary>窗口上一次被读到的最大化状态 —— 只有它翻了才走动画，拖窗口的每一帧不重来一遍。</summary>
    private bool _maximized;

    /// <summary>已经排了一次「布局落定之后再应用」的活；最大化那一下的几道通知并成一次。</summary>
    private bool _showcaseQueued;

    /// <summary>
    /// 正在把最大化状态写进视图模型。那一写会让 <c>Showcase</c> 发一次通知，而通知那一头（<c>OnViewModelChanged</c>）
    /// 走的是「当场落定」—— 它会把纸面和面板的起始值先写成终点，紧接着的过渡就没有可走的了。写入期间挡掉。
    /// </summary>
    private bool _applyingShowcase;

    /// <summary>
    /// 五块正文面板 —— 它们在标记里都挂着 <c>EgBodyPanelStyle</c>，底色和描边由样式给；这一支在构造时把两样换成
    /// 自己持有的画刷，好让颜色和透明度能动画。收 <see cref="StackPanel"/> 而不是 <see cref="FrameworkElement"/>：
    /// 换的是 <c>Background</c>／<c>BorderBrush</c> 两个专属于这个类型的属性。
    /// </summary>
    private IEnumerable<StackPanel> BodyPanels => [EpisodePanel, InfoPanel, SeasonPanel, SimilarPanel, CastPanel];

    /// <summary>
    /// 构造时准备三支画刷：纸面取主题的窗口色，面板取样式原本那两档。都在 <c>InitializeComponent</c> 之后、上树
    /// 之前做完 —— 第一帧就是对的，不会先按样式的底画一下再换成这一支。
    /// </summary>
    private void PrepareShowcase()
    {
        _sheetBrush.Color = PaperColor;
        BodySheet.Background = _sheetBrush;

        RefreshPanelBrushes();
        foreach (var panel in BodyPanels)
        {
            panel.Background = _panelFill;
            panel.BorderBrush = _panelLine;
        }
    }

    /// <summary>
    /// 换了主题：面板那两档颜色跟着换（纸面那一支在还原档也要换；沉浸档它一直是尾部那道半透明黑，与主题无关）。
    /// 主题现在全是深色，这一句在屏上多数时候看不出来 —— 但把颜色抄进画刷的那一刻就欠下了这笔账。
    /// </summary>
    private void RefreshShowcaseBrushes()
    {
        RefreshPanelBrushes();
        if (!ViewModel.Showcase) _sheetBrush.Color = PaperColor;
    }

    private void RefreshPanelBrushes()
    {
        _panelFill.Color = BrushColor("EgSurfaceAltBrush");
        _panelLine.Color = BrushColor("EgBorderBrush");
    }

    /// <summary>主题里那支窗口色 —— 正文那张纸原本的底。</summary>
    private static Color PaperColor => BrushColor("EgWindowBrush");

    private static Color BrushColor(string key) => Application.Current.Resources[key] is SolidColorBrush brush
        ? brush.Color
        : key switch
        {
            "EgWindowBrush" => Color.FromArgb(255, 0x0C, 0x11, 0x16),
            "EgSurfaceAltBrush" => Color.FromArgb(255, 0x1C, 0x25, 0x2E),
            _ => Color.FromArgb(255, 0x30, 0x39, 0x41)
        };

    /// <summary>
    /// 尾部最浓那一档的颜色 —— 沉浸档的纸面用它，正文和上方那一段因此同色，接缝看不出来。取的是屏上那支
    /// 画刷真正的颜色，不是另抄一遍 <see cref="DetailHero.ScrimCeiling"/>：两处各写一个数就是两处会分叉。
    /// </summary>
    private Color WashColor => HeroTail.Background is SolidColorBrush { Color: var color }
        ? color
        : Color.FromArgb(DetailHero.ScrimCeiling, DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B);

    /// <summary>
    /// 沉浸版式（最大化时正文让开）跟着条目种类、页宽跨断点和背景图翻档三处翻面。这三处都不是「用户按了窗口上的
    /// 最大化」，所以当场落定、不插过渡 —— 那一下只是换条目或换版式，不是窗口在变形态。
    /// <para>
    /// 单独一支而不是并进 <c>OnViewModelChanged</c>：那个方法是 Plex 入口夹具原样提取的契约正文
    /// （<c>tests/Momoka.PlexAffordanceTests</c>，提取范围从它到「自检：详情页的第一屏」），引用这一页新加的
    /// 字段会让那份提取出来的类编译不过。
    /// </para>
    /// </summary>
    private void OnShowcaseChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DetailViewModel.Showcase) && !_applyingShowcase) SettleShowcase();
    }

    /// <summary>
    /// 窗口的最大化状态变了 —— 排到这一轮布局落定之后再应用。
    /// <para>
    /// 为什么要等：<c>GeometryChanged</c> 是从 <c>WM_SIZE</c> 里同步发出来的，那一刻视口还是旧的，尾部那个下限
    /// 按旧视口算出来会是一个中间值，随后 <c>SizeChanged</c> 再改一次 —— 动画的终点跟着变，屏上就是「滑到一半
    /// 再跳一下」。延后一拍，<c>DetailPage.OnBodySizeChanged</c> 已经把新的页宽和视口写进视图模型了。
    /// </para>
    /// </summary>
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
    /// 把「是不是最大化」写进视图模型，并按需要走一趟过渡。
    /// <para>
    /// 只有最大化/还原这一条路带 <paramref name="animate"/>；换条目、页宽跨过紧凑断点、背景图翻档这些让
    /// <c>Showcase</c> 翻面的路都是布局自己的事，当场落定 —— 那几处不是用户按了窗口按钮，插一段两百多毫秒的
    /// 滑动只会像是卡了一下。
    /// </para>
    /// </summary>
    /// <param name="animate">这一次要不要走过渡（只有最大化/还原那一下是 true）。</param>
    private void ApplyShowcase(bool animate)
    {
        var was = ViewModel.Showcase;
        var from = HeroTail.ActualHeight;

        _applyingShowcase = true;
        try
        {
            ViewModel.Maximized = _window?.IsMaximized == true;
        }
        finally
        {
            _applyingShowcase = false;
        }

        var now = ViewModel.Showcase;
        if (!animate || was == now || !IsLoaded || XamlRoot is null)
        {
            SettleShowcase();
            return;
        }

        AnimateShowcase(now, from);
    }

    /// <summary>
    /// 不停留地落定：纸面色、面板透明度、尾部下限三样直接写终值。第一次上树、换条目和动画收尾都走它 ——
    /// 收尾那一下也必须走，因为 <see cref="Storyboard.Stop"/> 会把属性还回动画开始前的底值。
    /// </summary>
    private void SettleShowcase()
    {
        StopShowcase();

        var showcase = ViewModel.Showcase;
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        _panelFill.Opacity = showcase ? 0 : 1;
        _panelLine.Opacity = showcase ? 0 : 1;

        // 尾部那个下限绑在视图模型上；这里再写一次是因为动画期间它被本地值压住过，收尾要交回同一个数。
        HeroTail.MinHeight = ViewModel.TailMinHeight;

        PaintWash(force: true);
    }

    /// <summary>
    /// 走一趟：尾部高度、纸面颜色、面板底与框同一段时间、同一条缓动。三样都从「现在是」走到「该是」，
    /// 所以中途再翻回来也只是换一个终点，不会从半路跳回起点。
    /// </summary>
    private void AnimateShowcase(bool showcase, double fromTail)
    {
        StopShowcase();

        var board = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 1) 尾部：布局属性，必须留在 UI 线程（EnableDependentAnimation）。正文就在它下面，尾部收多少正文升多少。
        Track(board, HeroTail, "MinHeight", fromTail, ViewModel.TailMinHeight, ease, dependent: true);

        // 2) 纸面：窗口色 ↔ 尾部那道半透明黑。RGB 本来就一样（ScrimInk），变的只有 alpha。
        var sheetFrom = _sheetBrush.Color;
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        TrackColor(board, _sheetBrush, sheetFrom, _sheetBrush.Color, ease);

        // 3) 五块正文面板的底和框：沉浸档里淡到 0，还原时淡回来。
        Track(board, _panelFill, "Opacity", _panelFill.Opacity, showcase ? 0 : 1, ease);
        Track(board, _panelLine, "Opacity", _panelLine.Opacity, showcase ? 0 : 1, ease);

        _showcaseBoard = board;
        board.Completed += (_, _) => SettleShowcase();
        board.Begin();

        // 标题栏那一条也跟着换：沉浸档里没有纸面往上盖，整条一直是尾部那道半透明黑上的浅墨。
        PaintWash(force: true);
    }

    private void StopShowcase()
    {
        if (_showcaseBoard is not { } board) return;

        _showcaseBoard = null;
        board.Stop();
    }

    private static void Track(
        Storyboard board, DependencyObject target, string property, double from, double to,
        EasingFunctionBase ease, bool dependent = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(ShowcaseSpan),
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

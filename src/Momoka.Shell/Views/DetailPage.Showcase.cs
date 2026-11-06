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
/// 窗口铺开（最大化，或面积过了工作区五分之四）、或者背景图已经/即将被裁掉（<see cref="DetailHero.NearCrop"/>，
/// 2026-11-02）、宽版式、背后有图、电影/剧/季 —— 五句同时成立。这一支只管屏上
/// 那三样东西 —— 正文那张纸的底色、五块正文面板的底与框、尾部补第一屏的那段留白 —— 以及窗口形态翻面那一下怎么走。
/// </para>
/// <para>
/// <b>三样一起走，才是「组件整体上移」。</b>尾部高度是布局属性（<see cref="FrameworkElement.MinHeightProperty"/>），
/// 只能由 UI 线程一帧一帧重排；纸面颜色和面板底色的透明度也是画刷上的依赖动画（不写
/// <c>EnableDependentAnimation</c> 就会被静默丢掉，屏上只剩尾部自己在动、底色到收尾才啪地一跳）。同一段时间、
/// 同一条缓动，屏上读起来才是「纸面化开、正文顺着尾部收起来的方向升上去」而不是三件互不相干的事。整体位移不另做
/// 动画：正文和尾部在同一个网格里首尾相接，尾部收多少、正文就升多少，这是布局给的；再叠一层整体位移只会把两者
/// 拉开一道缝。
/// </para>
/// <para>
/// <b>块与块之间是另一回事（2026-10-31 晚间「设计一个过渡动画」，用户选定「分块错峰浮起」）。</b>每块正文面板
/// 各带一支 <see cref="TranslateTransform"/>，起步时先偏开 <see cref="FloatOffset"/>，再各自在 180ms 里归位；
/// 相邻两块错开 <see cref="FloatStagger"/>，顺序由 <see cref="DetailHero.FloatOrder"/> 给 —— 切进沉浸从上往下，
/// 切回纸面倒序。这层位移只改渲染变换、不动布局，所以它和尾部那一段可以同时跑而不打架：尾部负责「整块升到哪儿」，
/// 它负责「这一块比上一块晚多少」，合起来才是「一层层让开」而不是一块铁板平移。
/// </para>
/// <para>
/// 只动 <c>MinHeight</c>、<c>SolidColorBrush.Color</c>、两支 <c>Opacity</c>（面板底／描边）和背景图那一层的
/// <c>Opacity</c>，只走 XAML Storyboard，不用 composition —— 理由同 <see cref="HomeFoldMotion"/> 上那一段
/// （<c>GetElementVisual</c> 与框架自己的 <c>Scale</c> 抢同一份 visual，界面线程当场带走了进程）。
/// </para>
/// </summary>
public sealed partial class DetailPage
{
    /// <summary>这一趟走多久。同 <see cref="HomeFoldMotion"/>：两百六十毫秒，比播放页那 220 长一点，因为挪的是
    /// 整段正文而不是两层界面的溶解。</summary>
    private static readonly TimeSpan ShowcaseSpan = TimeSpan.FromMilliseconds(260);

    /// <summary>单块正文面板浮起那一段有多长 —— 比 <see cref="ShowcaseSpan"/> 短：它只管块与块之间那点差额，
    /// 跟整体一起在尾部停下来之后再各自归位，拖得一样长就成了「慢慢挪」。</summary>
    private static readonly TimeSpan FloatSpan = TimeSpan.FromMilliseconds(180);

    /// <summary>相邻两块相隔多久。四块排满（剧页常态）时最后一块比第一块晚三格（120ms），整趟收在 300ms；
    /// 再大就是一段慢动作，再小就看不出是一块接一块。</summary>
    private static readonly TimeSpan FloatStagger = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// 起步时每块面板偏开多少：切进沉浸先落在该到的位置<em>下面</em> 16px，切回纸面先在上面 16px，然后归位。
    /// 只改渲染变换，不动布局，所以这 16px 不会挤动下面任何东西。
    /// </summary>
    private const double FloatOffset = 16;

    /// <summary>
    /// 正文那张纸的底色。这一份是这一页自己的，不是全局那支 <c>EgWindowBrush</c>：沉浸档要把它淡成尾部那道
    /// 半透明黑（<see cref="DetailHero.ScrimCeiling"/> 那一档），改全局画刷会把别的页面一起改掉。
    /// </summary>
    private readonly SolidColorBrush _sheetBrush = new();

    /// <summary>五块正文面板的底 —— 沉浸档里淡到 0，纸面让开之后不再有「板里的板」那一层。</summary>
    private readonly AcrylicBrush _panelFill = new();

    /// <summary>五块正文面板的描边，同 <see cref="_panelFill"/>。</summary>
    private readonly SolidColorBrush _panelLine = new();

    /// <summary>这一趟的 Storyboard；换向或收尾时整份停掉，理由见 <see cref="HomeFoldMotion"/>。</summary>
    private Storyboard? _showcaseBoard;

    /// <summary>
    /// 窗口上一次被读到的「铺开了没有」—— 已经落到版面上的那一个。铺开指最大化，或者面积占了工作区五分之四
    /// 以上（<see cref="DetailHero.Spacious"/>）。
    /// </summary>
    private bool _spacious;

    /// <summary>这一刻量到的铺开与否；拖动中它先变、<see cref="_spacious"/> 等手停下来再跟上。</summary>
    private bool _pendingSpacious;

    /// <summary>上一次读到的最大化那一位 —— 它翻的时候是用户按了窗口按钮，当场落定，不等拖边的静默期。</summary>
    private bool _maximized;

    /// <summary>
    /// 拖边／贴靠时等手停下来的那一拍。拖动里 <c>WM_SIZE</c> 每几毫秒来一次，五分之四那条线在途中被跨过时
    /// 先不翻面 —— 跟着每一帧翻就是一路闪；尺寸停下来约两百毫秒后落定，松手时由
    /// <c>InteractiveResizeChanged</c> 提前叫醒。计时器只是那声「松手」的兜底：消息漏了，版面也不会一直停
    /// 在旧的那一档。
    /// </summary>
    private DispatcherQueueTimer? _spaciousWatch;

    /// <summary>已经排了一次「布局落定之后再应用」的活；窗口形态翻面那一下的几道通知并成一次。</summary>
    private bool _showcaseQueued;

    /// <summary>
    /// 正在把窗口形态写进视图模型。那一写会让 <c>Showcase</c> 发一次通知，而通知那一头（<c>OnViewModelChanged</c>）
    /// 走的是「当场落定」—— 它会把纸面和面板的起始值先写成终点，紧接着的过渡就没有可走的了。写入期间挡掉。
    /// </summary>
    private bool _applyingShowcase;

    /// <summary>
    /// 这一页已经排过第一次版。在那之前 <c>Showcase</c> 会从「没量到」那一档翻成真实答案，那是版面自己
    /// 落位、不是窗口在变形态，不能插动画（同 <see cref="ApplyShowcase"/> 的 <c>animate:false</c>）。
    /// 第一次 <see cref="OnBodySizeChanged"/> 写完之后置位。
    /// </summary>
    private bool _showcaseSettled;

    /// <summary>
    /// 版面上当前这一档（<c>Showcase</c>）是谁 —— 落定或动画起步时更新。同一个状态重复发通知时靠它挡住
    /// 重复的落定与动画；也让「哪一次是真的翻面」只算一次。
    /// </summary>
    private bool _shownShowcase;

    /// <summary>上一次落定时这一页是不是紧凑版式 —— 大窗与紧凑之间的翻面不插过渡，那是换了整套版面。</summary>
    private bool _settledCompact;

    /// <summary>
    /// 正在把 <see cref="OnBodySizeChanged"/> 量到的宽高写进视图模型。只有这一段里发生的 <c>Showcase</c>
    /// 翻面才算「窗口在变形态」，才走过渡；换条目、图到手、用户点窗口按钮各有自己的路。
    /// </summary>
    private bool _sizing;

    /// <summary>
    /// 这一页实际走了几次大窗过渡 —— 探针读它证明「拉宽触发裁切」那一路也真的走了动画，而不只是把终点
    /// 写上去。屏上看不见一个已经结束的动画，只有次数分得清「走过」和「直接落定」。
    /// </summary>
    internal int ShowcaseAnimations { get; private set; }

    /// <summary>
    /// 这一页这一刻还有没有翻面过渡在走 —— 和上面那个计数配成一对：计数回答「走没走过」，这一句回答「走完没有」。
    /// 探针等的是后者（同 <c>MoviePilotSubscriptionPage.ShowcaseAnimating</c>）。
    /// </summary>
    internal bool ShowcaseAnimating => _showcaseBoard is not null;

    /// <summary>
    /// 五块正文面板 —— 它们在标记里都挂着 <c>EgBodyPanelStyle</c>，底色和描边由样式给；这一支在构造时把两样换成
    /// 自己持有的画刷，好让颜色和透明度能动画。收 <see cref="StackPanel"/> 而不是 <see cref="FrameworkElement"/>：
    /// 换的是 <c>Background</c>／<c>BorderBrush</c> 两个专属于这个类型的属性。
    /// <para>
    /// 次序就是屏上从上往下的次序（<see cref="ShownPanels"/> 按它数错峰的格子）：2026-11-04 信息卡与季卡互换，
    /// 这里同样重排，保证浮起动画按实际版面从上往下进行。
    /// </para>
    /// </summary>
    private IEnumerable<StackPanel> BodyPanels =>
        BodySheet.Children.OfType<StackPanel>();

    /// <summary>
    /// 构造时准备三支画刷和五支浮起变换：纸面取主题的窗口色，面板取样式原本那两档，每块面板各装一支
    /// <see cref="TranslateTransform"/>（错峰那一段动的就是它）。都在 <c>InitializeComponent</c> 之后、上树
    /// 之前做完 —— 第一帧就是对的，不会先按样式的底画一下再换成这一支。
    /// </summary>
    private void PrepareShowcase()
    {
        _sheetBrush.Color = PaperColor;
        BodyPaper.Background = _sheetBrush;
        BodySheet.Background = null;

        // 背景整层不调不透明度：设置里那一档落在图上那道遮罩（PaintScrim／PaintBackdropFade），
        // 2026-11-06 起普通与沉浸共用同一个数，翻面不再改图本身。

        // 监听本页的 ThemeResource 替换，覆盖系统高对比度；不订阅需要 CoreWindow 的系统事件。
        CardMaterialSource.RegisterPropertyChangedCallback(Border.BackgroundProperty, (_, _) =>
        {
            RefreshPanelBrushes();
            PaintBackdropReading();
        });
        RefreshPanelBrushes();
        foreach (var panel in BodyPanels)
        {
            panel.Background = _panelFill;
            panel.BorderBrush = _panelLine;
            panel.RenderTransform = new TranslateTransform();
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
        if (CardMaterialSource.Background is AcrylicBrush template)
        {
            _panelFill.AlwaysUseFallback = template.AlwaysUseFallback;
            _panelFill.TintColor = template.TintColor;
            _panelFill.FallbackColor = template.FallbackColor;
            _panelFill.TintOpacity = template.TintOpacity;
            _panelFill.TintLuminosityOpacity = template.TintLuminosityOpacity;
        }
        else if (CardMaterialSource.Background is SolidColorBrush contrast)
        {
            _panelFill.AlwaysUseFallback = true;
            _panelFill.FallbackColor = contrast.Color;
        }
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
    /// 沉浸档的纸面与阅读遮罩同色；普通模式的透明尾部不能成为进入沉浸时的取色来源。
    /// </summary>
    private Color WashColor => UsesBackdropFade
        ? Color.FromArgb(0, DetailHero.ScrimInk.R, DetailHero.ScrimInk.G, DetailHero.ScrimInk.B)
        : ReadingShadeColor;

    /// <summary>
    /// 沉浸版式（窗口铺开时正文让开）跟着条目种类、页宽跨断点和背景图翻档三处翻面。这三处都不是「用户按了
    /// 最大化或松开了拖边的手」，所以当场落定、不插过渡 —— 那一下只是换条目或换版式，不是窗口在变形态。
    /// <para>
    /// 单独一支而不是并进 <c>OnViewModelChanged</c>：那个方法是 Plex 入口夹具原样提取的契约正文
    /// （<c>tests/Momoka.PlexAffordanceTests</c>，提取范围从它到「自检：详情页的第一屏」），引用这一页新加的
    /// 字段会让那份提取出来的类编译不过。
    /// </para>
    /// </summary>
    private void OnShowcaseChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(DetailViewModel.Showcase)) return;
        if (!_sizing) ViewModel.UpdateHeroBlur();
        if (_applyingShowcase) return;

        // 改尺寸那一趟由 OnBodySized 在宽高都写完之后一次结算：写到一半时量到的带高、纸面位置都是旧的，
        // 而且一次布局可能先翻过去又翻回来。这里只处理别的来路 —— 换条目、图到手、版本切换，一律当场落定。
        if (_sizing) return;

        var now = ViewModel.Showcase;

        // 第一次排版之前与重复通知：版面还没落定，或者答案根本没变 —— 不插过渡，也不重写已落定的状态。
        if (!_showcaseSettled)
        {
            SettleShowcase();
            return;
        }

        if (now == _shownShowcase) return;

        SettleShowcase();
    }

    /// <summary>
    /// 一次尺寸变化写完宽高之后的唯一结算点（<see cref="DetailPage.OnBodySizeChanged"/> 调用）。
    /// <para>
    /// 正常↔大窗走过渡；紧凑版式那一头当场落定 —— 换的是整套单列版面，不是纸面让开，插一段尾部滑动只会
    /// 像是卡了一下。第一次排版的翻面也当场落定：那是版面落位，不是用户在拖窗口。
    /// </para>
    /// </summary>
    /// <param name="wasCompact">上一次排版落定时是不是紧凑版式。</param>
    private void ResolveShowcaseAfterResize(bool wasCompact)
    {
        var now = ViewModel.Showcase;

        if (!_showcaseSettled || now == _shownShowcase)
        {
            // 第一次排版、或者这一趟尺寸没把状态翻过去：前者要落定成真实答案，后者什么都不用做 ——
            // 尺寸相关的属性自己会通知，动画只在状态真的翻了才需要。
            if (!_showcaseSettled) SettleShowcase();
            return;
        }

        if (wasCompact || ViewModel.IsCompact
            || !IsLoaded || XamlRoot is null || !HomeMotion.AnimationsEnabled)
        {
            SettleShowcase();
            return;
        }

        AnimateShowcase(now, HeroTail.ActualHeight);
    }

    /// <summary>
    /// 窗口形态变了（最大化／还原，或者拖到一半松手时面积跨过了工作区五分之四那条线）—— 排到这一轮布局落定
    /// 之后再应用。
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
    /// 把「窗口铺开了没有」写进视图模型，并按需要走一趟过渡。
    /// <para>
    /// 只有窗口形态翻面这一条路带 <paramref name="animate"/>；换条目、页宽跨过紧凑断点、背景图翻档这些让
    /// <c>Showcase</c> 翻面的路都是布局自己的事，当场落定 —— 那几处不是用户按了窗口按钮或松开了拖边的手，
    /// 插一段两百多毫秒的滑动只会像是卡了一下。
    /// </para>
    /// </summary>
    /// <param name="animate">这一次要不要走过渡（窗口形态翻面那一下是 true）。</param>
    private void ApplyShowcase(bool animate)
    {
        var was = ViewModel.Showcase;
        var from = HeroTail.ActualHeight;

        _applyingShowcase = true;
        try
        {
            ViewModel.Spacious = _spacious;
        }
        finally
        {
            _applyingShowcase = false;
        }

        var now = ViewModel.Showcase;

        // 系统关掉界面动画（或设置里关了这一档）就直接落到终点：错峰那一段本来就是为了好看，开关关着的时候
        // 摆在屏上的只是三百多毫秒的挪动。规矩同 <see cref="HomeMotion"/>。
        // 「was == now」但已经有一趟在走（常见于拉宽触发裁切先起步、松手后铺开状态才追上）时交给它走完，
        // 不能在这里落定 —— 那会把动画停在半路。
        if (!animate || was == now || !IsLoaded || XamlRoot is null || !HomeMotion.AnimationsEnabled)
        {
            if (animate && was == now && _showcaseBoard is not null) return;

            SettleShowcase();
            return;
        }

        AnimateShowcase(now, from);
    }

    /// <summary>
    /// 这一刻窗口铺开了没有：最大化，或者客户区面积占了所在显示器工作区的五分之四以上
    /// （<see cref="DetailHero.Spacious"/>，2026-10-31 用户令「窗口占屏幕五分之四面积时也要自动切换」）。
    /// <para>
    /// 两个数都在窗口那一侧：<c>IsMaximized</c> 与客户区，分母是 <c>WorkArea()</c> —— 窗口此刻所在那台显示器
    /// 的可用区域（任务栏之外）。两张都取物理像素，不折 DIP：这里比的是同一台显示器上的两个面积，比例与单位无关，
    /// 折算一次反而要在两块不同 DPI 的屏之间挑一个比例。
    /// </para>
    /// </summary>
    private bool IsSpacious()
    {
        if (_window is not { } window) return false;

        var work = window.WorkArea();
        var (width, height) = window.ClientSize;
        return DetailHero.Spacious(
            window.IsMaximized, DetailHero.Share(width, height, work.Width, work.Height));
    }

    /// <summary>
    /// 把「面积跨过了那条线」推到手停下来之后：每来一次新的尺寸就把计时器重起一遍，见 <see cref="_spaciousWatch"/>。
    /// </summary>
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

    /// <summary>
    /// 把量到的那一档落到版面上。没有变就什么都不做 —— 拖过线又拖回原处时，等待中的那一趟到这里收工。
    /// </summary>
    private void CommitSpacious()
    {
        _spaciousWatch?.Stop();
        if (_pendingSpacious == _spacious) return;

        _spacious = _pendingSpacious;
        ScheduleShowcase();
    }

    /// <summary>这一页离开可视树：还在等的那一趟作废。</summary>
    private void StopSpaciousWatch() => _spaciousWatch?.Stop();

    /// <summary>
    /// 不停留地落定：纸面色、面板透明度、尾部下限、每块面板的浮起位移直接写终值。第一次上树、换条目和动画
    /// 收尾都走它 —— 收尾那一下也必须走，因为 <see cref="Storyboard.Stop"/> 会把属性还回动画开始前的底值。
    /// </summary>
    private void SettleShowcase()
    {
        StopShowcase();

        PaintScrim();
        var showcase = ViewModel.Showcase;
        _shownShowcase = showcase;
        _settledCompact = ViewModel.IsCompact;
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        _panelFill.Opacity = showcase ? 0 : 1;
        _panelLine.Opacity = showcase ? 0 : 1;
        PaintBackdropReading();

        // 浮起位移的底值就是起步时种下的那 16px；停掉动画只是把它交回来，还得再写一次 0 才算落定。
        foreach (var panel in BodyPanels)
            if (Drift(panel) is { } drift) drift.Y = 0;

        // 尾部那个下限绑在视图模型上；这里再写一次是因为动画期间它被本地值压住过，收尾要交回同一个数。
        HeroTail.MinHeight = ViewModel.TailMinHeight;

        PaintWash(force: true);
    }

    /// <summary>这一刻真正看得见的那几块正文面板，从上往下 —— 错峰的格子按它们数，不按标记里写了几块。</summary>
    private List<StackPanel> ShownPanels()
    {
        var shown = new List<StackPanel>(5);
        foreach (var panel in BodyPanels)
            if (panel.Visibility == Visibility.Visible) shown.Add(panel);

        return shown;
    }

    /// <summary>这一块面板的浮起变换 —— 构造时每块装了一支（<see cref="PrepareShowcase"/>），这里只取。</summary>
    private static TranslateTransform? Drift(StackPanel panel) => panel.RenderTransform as TranslateTransform;

    /// <summary>
    /// 走一趟：尾部高度、纸面颜色、面板底与框、每块正文的浮起位移。前四样同一段时间、同一条缓动；浮起那一段
    /// 更短，并且按 <see cref="DetailHero.FloatOrder"/> 一块错后一块。
    /// <para>
    /// 每一样都从「现在是」走到「该是」，所以中途再翻回来也只是换一个终点，不会从半路跳回起点。为了这个
    /// 「现在是」，量的时候要赶在 <see cref="StopShowcase"/> 之前：停下上一趟会把被它压住的属性交回底值，
    /// 停完再量，量到的其实是上一趟的终点。
    /// </para>
    /// </summary>
    private void AnimateShowcase(bool showcase, double fromTail)
    {
        ShowcaseAnimations++;
        _shownShowcase = showcase;

        var sheetFrom = _sheetBrush.Color;
        var fillFrom = _panelFill.Opacity;
        var lineFrom = _panelLine.Opacity;
        var panels = ShownPanels();
        var driftFrom = new double[panels.Count];
        for (var slot = 0; slot < panels.Count; slot++)
            driftFrom[slot] = Drift(panels[slot])?.Y ?? 0;

        StopShowcase();

        PaintScrim();
        var board = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 1) 尾部：布局属性，必须留在 UI 线程（EnableDependentAnimation）。正文就在它下面，尾部收多少正文升多少。
        Track(board, HeroTail, "MinHeight", fromTail, ViewModel.TailMinHeight, ease, dependent: true);

        // 2) 纸面：窗口色 ↔ 尾部那道半透明黑。RGB 本来就一样（ScrimInk），变的只有 alpha。
        _sheetBrush.Color = showcase ? WashColor : PaperColor;
        TrackColor(board, _sheetBrush, sheetFrom, _sheetBrush.Color, ease);

        // 3) 五块正文面板的底和框：沉浸档里淡到 0，还原时淡回来。
        Track(board, _panelFill, "Opacity", fillFrom, showcase ? 0 : 1, ease, dependent: true);
        Track(board, _panelLine, "Opacity", lineFrom, showcase ? 0 : 1, ease, dependent: true);

        // 3.5) 背景整层不跟版式走 —— 2026-11-06 起它就是设置里那一档，翻面不改它，所以这里没有过渡。

        // 4) 每块正文各自浮起那 16px 归位，一块错后一块。停在半路的接着走，停在终点的才种上方向。
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

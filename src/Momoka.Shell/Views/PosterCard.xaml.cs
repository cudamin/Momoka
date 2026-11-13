using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Diagnostics;
using Momoka.Infrastructure;

namespace Momoka.Shell.Views;

/// <summary>
/// 自检：what one card's hover buttons are and where they sit, with the pointer's part of it faked.
/// <paramref name="OffsetX"/> and <paramref name="OffsetY"/> are the play button's centre measured from
/// the artwork's centre, so 「in the middle of the cover」 is 0,0 and nothing else;
/// <paramref name="InsetRight"/> and <paramref name="InsetBottom"/> are the three icons' bottom-right
/// corner measured from the artwork's, so 「贴在封面的右下角」 is a small number and centred is half a
/// card. <paramref name="Chrome"/> is whether any of the three still paints a background in any of its
/// three states — the grey circles and the black pill that 「仅保留白色的图标按钮」 took away.
/// <paramref name="Watch"/> is whether the card can tell where the cursor really is, which is what takes
/// the buttons back down when the pointer leaves the window without saying so.
/// <para>
/// 封面框线、中间播放键与无描边读数分开记录。<paramref name="FrameBorderless"/> 核三态都不画线。<paramref name="FrameRest"/> 是静止时的框线色，
/// <paramref name="FrameActive"/> 是指针上来之后的色 —— 2026-10-27 起两者必须相同：封面不再画那圈绿框。
/// <paramref name="PlayRest"/> 是播放键停着的底色（灰），<paramref name="PlayHover"/> 是指针压到键上的底色
/// （强调色）—— 48 像素的圆在截图里认得出颜色，但「是不是调色板里那一支」还得靠读数。
/// <paramref name="FrameFocus"/> 是键盘那一半接上了没有：卡片自己接不到焦点，得往上找到外层容器，找不到的
/// 话焦点那一半整个不存在，而那是完全没有症状的。
/// </para>
/// </summary>
internal sealed record CardHoverProbe(
    string Title,
    bool Play,
    bool Watched,
    bool Favorite,
    bool More,
    double Size,
    double OffsetX,
    double OffsetY,
    double ArtWidth,
    double ArtHeight,
    double InsetRight,
    double InsetBottom,
    bool Chrome,
    bool HostSurfaceTransparent,
    bool Watch,
    Windows.UI.Color FrameRest,
    Windows.UI.Color FrameActive,
    Windows.UI.Color PlayRest,
    Windows.UI.Color PlayHover,
    bool FrameFocus,
    bool FrameBorderless);

/// <summary>需求 6：which of the card's hover buttons was pressed.</summary>
public enum CardAction
{
    /// <summary>
    /// Starts the item straight away. In the middle of the artwork, where the eye already is, because
    /// the card body itself opens the detail page (「把播放按钮移动到媒体封面的中心」).
    /// </summary>
    Play,
    Watched,
    Favorite,
    More,
    Details
}

/// <summary>
/// What <see cref="PosterCard.ActionRequested"/> carries. The card itself is not in here: the sender is
/// the <see cref="PosterCard"/>, whose <see cref="PosterCard.Card"/> is the only copy that is certain
/// to be the one on screen after a container has been recycled.
/// </summary>
public sealed class CardActionEventArgs(CardAction action, FrameworkElement anchor) : EventArgs
{
    public CardAction Action { get; } = action;

    /// <summary>The button that was pressed, so 更多 opens its menu at the button and not at the card.</summary>
    public FrameworkElement Anchor { get; } = anchor;
}

/// <summary>
/// The control every card in the app is drawn with. It owns the shape; <see cref="CardItem"/> owns
/// the content.
/// <para>
/// The two size properties are set from the DataTemplate rather than bound, so one control covers both
/// the 2:3 poster in a library grid and the 16:9 still in a 继续观看 row without a second copy of the
/// markup.
/// </para>
/// <para>
/// It is also where the recycling contract is honoured. A container is realized, handed a card, later
/// detached and handed a different one; each of those three moments has to move the artwork along with
/// it, and the events below are the only places that know when they happen.
/// </para>
/// </summary>
public sealed partial class PosterCard : UserControl
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card),
        typeof(CardItem),
        typeof(PosterCard),
        new PropertyMetadata(null, OnCardChanged));

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth),
        typeof(double),
        typeof(PosterCard),
        new PropertyMetadata(170d));

    public static readonly DependencyProperty PosterHeightProperty = DependencyProperty.Register(
        nameof(PosterHeight),
        typeof(double),
        typeof(PosterCard),
        new PropertyMetadata(255d));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(PosterCard), new PropertyMetadata(false));

    /// <summary>
    /// 这张卡压在一张剧照上，而不是站在页面那张纸上 —— 集页把同季那一带摆在头图底下的画面里
    /// （见 <c>DetailPage.PlaceEpisodes</c>）。底下那两行字于是得换成压在图上那一套不随主题走的墨：
    /// 主题自己的墨在晴昼（唯一那套浅色）下是近黑色，压在七成二的黑罩子上一个字都读不出来。
    /// </summary>
    public static readonly DependencyProperty OnScrimProperty = DependencyProperty.Register(
        nameof(OnScrim),
        typeof(bool),
        typeof(PosterCard),
        new PropertyMetadata(false, OnScrimChanged));

    /// <summary>
    /// Tracked rather than read from <c>IsLoaded</c>: the two events below are the authority on when a
    /// recycled container is in the tree, and a field cannot disagree with them.
    /// </summary>
    private bool _live;

    /// <summary>
    /// 「鼠标移出窗口后不会自动恢复」：the pointer's departure, checked against the OS rather than taken from
    /// the exit event alone. See <see cref="HoverWatch"/> for why the event is not enough.
    /// </summary>
    private readonly HoverWatch _watch;

    /// <summary>键盘焦点那一半，见 <see cref="FocusWatch"/>。</summary>
    private readonly FocusWatch _focus;

    /// <summary>
    /// 「用户此刻在这张卡上」的两个来源：指针停在这张卡上、或者键盘焦点落在它身上。分成两个旗子而不是一个
    /// bool：两者会同时成立，也会一前一后地走，谁都不能替对方回答 —— 用同一个 bool 的话，点了悬浮层里的按钮
    /// 再把指针移开，焦点还在这张卡上，封面却会退回去。
    /// </summary>
    private bool _hovered;

    private bool _focused;

    private bool _pressed;
    private Storyboard? _motion;
    private readonly ImageArrivalFade _posterArrival;
    private readonly ImageArrivalFade[] _tileArrivals;

    /// <summary>
    /// 用户令（2026-11-12，参考微软商店）：鼠标停在封面上一秒之后才浮出那块预览。一秒是**停留**，
    /// 不是「经过」—— 指针扫过一整排封面时一张都不该弹；离开卡片或按下卡片就把这一秒作废。
    /// </summary>
    private const int PreviewDwellMs = 1000;

    /// <summary>
    /// 卡片与面板之间那一点空隙的宽限：指针从卡片跨到面板上时先经过一小段谁都不属于的地方，
    /// 太短会在半路把面板收掉，太长又像卡住。收起动画本身还要 120 毫秒，两者不叠加。
    /// </summary>
    private const int PreviewCloseGraceMs = 180;

    /// <summary>
    /// 撑开之后那块预览的宽，以及窗口太窄时允许缩到的下限。400 是照商店那块与卡片的比例定的：
    /// 预览比一张竖版海报宽两倍出头，压住左右各一张卡，不至于横跨整条货架。
    /// </summary>
    private const double PreviewWidth = 400;
    private const double PreviewMinWidth = 300;

    /// <summary>面板离窗口左右两边至少留这么多，别贴着边。</summary>
    private const double PreviewMargin = 12;

    /// <summary>停留计时。只在指针停在卡片上时跑；一秒到了才去建面板，路过的卡片不为此多造一棵树。</summary>
    private DispatcherQueueTimer? _previewDwell;

    /// <summary>离开卡片之后到真正收起之间的宽限，指针进面板或回到卡片就取消。</summary>
    private DispatcherQueueTimer? _previewClose;

    private Popup? _previewHost;
    private CardPreview? _preview;
    private HoverWatch? _previewWatch;

    /// <summary>
    /// 面板打开之后去取那张宽图的那一趟。收起、换卡或离树就取消 —— 停一秒又走开的卡片不该在后台
    /// 接着下载一张没人看的图。
    /// </summary>
    private CancellationTokenSource? _previewArtwork;

    /// <summary>面板打开时盯住的那几条滚动：内容一动，卡片和面板就错位了，先收起再说。</summary>
    private readonly List<ScrollViewer> _previewScrolls = [];

    private bool _previewOpen;

    /// <summary>指针此刻在面板上。宽限到点时它也算「别收」—— 用户正读着那一块。</summary>
    private bool _previewHovered;

    internal bool PosterArrivalRunning => _posterArrival.IsRunning;

    /// <summary>删除确认后的离场动画；<see cref="ResetRemoval"/> 或换卡时停掉。</summary>
    private Storyboard? _removal;

    /// <summary>离场动画落定的信号。停掉动画也要兑现，等它的页面才不会一直等着。</summary>
    private TaskCompletionSource<bool>? _removalDone;

    public PosterCard()
    {
        InitializeComponent();

        _watch = new HoverWatch(Root, up =>
        {
            _hovered = up;
            if (!up) _pressed = false;

            // 停留一秒才轮到预览；离开就作废。收起那一下留一小段宽限，好让指针从卡片走到面板上。
            if (up)
            {
                CancelPreviewClose();
                ArmPreview();
            }
            else
            {
                _previewDwell?.Stop();
                SchedulePreviewClose();
            }
            AnimateCard();
        });

        _focus = new FocusWatch(Root, on =>
        {
            _focused = on;
            AnimateCard();
        });

        // Button 会把按压标成 handled；仍然监听，才能让卡片表面与内部操作按钮有相同的按压反馈。
        Root.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        Root.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        Root.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        Root.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnPointerReleased), true);

        // 绑定晚到就淡入，不依赖缓存 BitmapImage 不保证重发的 ImageOpened。
        _posterArrival = new ImageArrivalFade(Poster);
        _tileArrivals =
        [
            new(FnNasPosterBackground),
            new(FolderPoster0), new(FolderPoster1), new(FolderPoster2), new(FolderPoster3),
            new(LibraryPoster0), new(LibraryPoster1), new(LibraryPoster2),
        ];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 需求 6：one of the card's hover buttons was pressed. Raised rather than acted on, because a card
    /// has no session to call and no page to notify — see <see cref="ItemCommands"/>, which both grids
    /// hand this straight to.
    /// </summary>
    public event EventHandler<CardActionEventArgs>? ActionRequested;

    public CardItem? Card
    {
        get => (CardItem?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    /// <summary>Card width in device-independent pixels. Must match the layout's MinItemWidth.</summary>
    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    /// <summary>Height of the artwork above the two text lines.</summary>
    public double PosterHeight
    {
        get => (double)GetValue(PosterHeightProperty);
        set => SetValue(PosterHeightProperty, value);
    }

    /// <summary>Selection belongs to the ItemsView container; only its cover outline is drawn here.</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <inheritdoc cref="OnScrimProperty"/>
    public bool OnScrim
    {
        get => (bool)GetValue(OnScrimProperty);
        set => SetValue(OnScrimProperty, value);
    }

    /// <summary>
    /// 底下那两行字换一套墨。换的是样式而不是画刷：压在图上那两支是写死的浅墨，可主题那两支是
    /// <c>ThemeResource</c>，抄一份画刷引用过来就再也不跟着换主题了。两个键都 <c>BasedOn</c> 主题那一套，
    /// 只改前景 —— 所以字体和字号在两种底上是同一份。
    /// </summary>
    private static void OnScrimChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (PosterCard)sender;
        var scrim = (bool)args.NewValue;

        card.TitleLine.Style = Ink(scrim ? "EgOnScrimBodyStyle" : "EgBodyStyle");
        card.SubLine.Style = Ink(scrim ? "EgOnScrimDataStyle" : card.Card?.IsUpcoming == true ? "EgCaptionStyle" : "EgDataStyle");
        VisualStateManager.GoToState(card, scrim ? "CaptionOnScrim" : "CaptionPlain", false);

        static Style Ink(string key) => (Style)Application.Current.Resources[key];
    }

    private readonly Dictionary<TextBlock, SolidColorBrush> _backdropInk = [];

    internal void ApplyBackdropInk(Func<FrameworkElement, Momoka.Theming.ThemeColor>? read)
    {
        foreach (var text in new[] { TitleLine, SubLine, SourceLine, NoteLine })
        {
            if (read is null)
            {
                text.ClearValue(TextBlock.ForegroundProperty);
                continue;
            }
            if (!_backdropInk.TryGetValue(text, out var brush))
            {
                brush = new SolidColorBrush();
                _backdropInk.Add(text, brush);
            }
            brush.Color = ThemeHost.ToColor(read(text));
            text.Foreground = brush;
        }
    }

    private static void OnCardChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (PosterCard)sender;

        // 接手新卡：从这一刻起，这张卡的图归这个容器管（见 CardItem.ClaimCard）。
        (args.NewValue as CardItem)?.ClaimCard(card);

        // The card being replaced is still in its page's list and would otherwise keep its bitmap.
        // keepWaiting: false —— 「它失去容器了」是这一句说的：这个容器改去装另一张卡了。离树那一声
        // （OnUnloaded）不算，它会对一个仍然在屏上的元素喊出来，见 CardItem.ReleasePoster 上那段。
        //
        // **但「这个容器放手」不等于「它失去了容器」** —— 同一张卡会短暂地挂在两个容器上（回收池里那个还攥着它，
        // 2026-09-13 实测：容器 A 拿到「超常技能」的同一毫秒里，容器 B 正把它换成别的卡，B 那一放手就把 A 屏上
        // 那张卡的图清空了，成了三张「标题正常、封面永远灰着」的卡）。所以交给 ReleasePoster 判断，它只认持有者。
        (args.OldValue as CardItem)?.ReleasePoster(card, keepWaiting: false);
        card.SubLine.Style = (Style)Application.Current.Resources[
            card.OnScrim ? "EgOnScrimDataStyle" : card.Card?.IsUpcoming == true ? "EgCaptionStyle" : "EgDataStyle"];

        // ItemsRepeater 也会在仍然加载着的容器里换内容；新条目不能继承上一张的悬停、按压或淡入。
        // 预览面板写的是上一张的片名和概要，更不能跟着容器搬过去。
        card.ClosePreview(immediately: true);
        card._previewDwell?.Stop();
        card._watch.Leave();
        card.ResetMotion();

        // 已在树的容器始终可请求。**信不过 _live 这道门**：回收池会对看得到的容器喊一声 Unloaded 而此后 Loaded 再也不来
        // （2026-09-05、2026-09-12 两回实测，日志里那几批「加载图片失败一个字没有、灰占位卡却在屏上」的封面
        // 就是它），那之后 _live 永远是 false，绑到这个容器上的每一张卡都一次请求都不发。而 Card 会被赋值只有
        // 一种原因：回收池把这个容器交给这一条目摆上屏。首次赋卡尚未挂树时由 Loaded 再请求，
        // 已经显示却收到回收幻影的容器仍有 XamlRoot，Begin 不能把它拦住。
        card.Begin();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _live = true;
        _focus.Attach();

        // 这张卡站在什么底上，问的是它所在的那条带（ShelfStrip.OnScrim）。上树这一刻问是唯一一定问得到的时机：
        // 带那边翻开关时这张卡可能还没建出来，而建出来的那一刻它自己还没套上模板。找不到带就是别处用的卡片
        // （媒体库那一格的网格），保持本来那一档。
        if (Up<ShelfStrip>(this) is { } strip) OnScrim = strip.OnScrim;

        Begin();
    }

    /// <summary>往上找最近的一个 <typeparamref name="T"/>：卡片是模板里的内容，带在它外面好几层。</summary>
    private static T? Up<T>(DependencyObject node) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(node); current is not null;
            current = VisualTreeHelper.GetParent(current))
        {
            if (current is T found) return found;
        }

        return null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // **这一声信不过。** 回收池会对一个仍然画在屏上的容器喊 Unloaded，而 Loaded 从此不再来
        // （2026-09-05 那四张灰卡是它的上半场：在途的那趟靠「继续等着」救回；2026-09-12 这一批是下半场：
        // _live 从此永远 false，之后绑上来的每张卡都发不出请求，屏上是几张标题正常、封面永远灰着的卡）。
        // 所以「真离树没有」不听它的，按 XamlRoot 现问一遍：还在树上就是幻影，这一声整个不理 —— 对着一张
        // 看得到的卡放掉图、摘掉监听，它就永远停在灰占位上。真离了树（XamlRoot 没了）才走原来那一套；
        // 「容器真的换卡了」另有权威的一句，在 OnCardChanged。
        _live = XamlRoot is not null;
        if (_live) return;

        Card?.ReleasePoster(this);

        // A recycled container must not come back with the buttons already up: the pointer that revealed
        // them is nowhere near wherever this container is reused.
        _previewDwell?.Stop();
        ClosePreview(immediately: true);
        _watch.Leave();
        _hovered = false;
        _focus.Detach();
        ResetMotion();
    }

    /// <summary>
    /// 卡片、图片和操作层共享同一段短动画。中途换向从当前值继续，离开时立即停止命中，避免透明按钮拦住点击。
    /// 动画不写宽高和 Margin，虚拟化和横向滚动仍使用原来的卡片尺寸。
    /// </summary>
    private void AnimateCard()
    {
        var from = (CardMotion.ScaleX, PosterMotion.ScaleX,
            Hover.Opacity, HoverShade.Opacity, PlayMotion.ScaleX);
        _motion?.Stop();
        _motion = null;

        var enabled = HomeMotion.AnimationsEnabled;
        // 预览面板开着也算「这张卡正被看着」：指针移到面板上时卡片不退回静止，否则那一对读起来像
        // 两块无关的东西。操作层仍只跟指针走 —— 指针不在卡片上时那几颗按钮按不到，就不该留在屏上。
        var active = _hovered || _focused || _previewOpen;
        // 外层按钮和滚动视口仍按布局尺寸裁剪；整卡向外放大或上移会切掉描边。
        // 悬停只推近框内图片，按压仍可向内轻缩，边框始终留在原来的命中范围内。
        var scale = enabled && _pressed ? 0.985 : 1;
        var zoom = enabled && active ? 1.045 : 1;
        var opacity = _hovered ? 1 : 0;
        var shade = _hovered || _previewOpen ? 0.78 : 0;
        var play = enabled && !_hovered ? 0.92 : 1;

        Hover.IsHitTestVisible = _hovered;
        if (_hovered) Hover.Visibility = Visibility.Visible;

        void Settle()
        {
            CardMotion.ScaleX = CardMotion.ScaleY = scale;
            PosterMotion.ScaleX = PosterMotion.ScaleY = zoom;
            Hover.Opacity = opacity;
            HoverShade.Opacity = shade;
            PlayMotion.ScaleX = PlayMotion.ScaleY = play;
            if (!_hovered) Hover.Visibility = Visibility.Collapsed;
        }

        if (!_live || XamlRoot is null || !enabled)
        {
            Settle();
            return;
        }

        var board = new Storyboard();
        var duration = _pressed ? 90 : _hovered ? 240 : 180;
        Add(CardMotion, "ScaleX", from.Item1, scale);
        Add(CardMotion, "ScaleY", from.Item1, scale);
        Add(PosterMotion, "ScaleX", from.Item2, zoom);
        Add(PosterMotion, "ScaleY", from.Item2, zoom);
        Add(Hover, "Opacity", from.Item3, opacity);
        Add(HoverShade, "Opacity", from.Item4, shade);
        Add(PlayMotion, "ScaleX", from.Item5, play);
        Add(PlayMotion, "ScaleY", from.Item5, play);
        _motion = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_motion, board)) return;
            Settle();
            board.Stop();
            _motion = null;
        };
        board.Begin();

        void Add(DependencyObject target, string path, double start, double end)
        {
            var animation = new DoubleAnimation
            {
                From = start,
                To = end,
                Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, path);
            board.Children.Add(animation);
        }
    }

    private void ResetMotion()
    {
        _motion?.Stop();
        _motion = null;
        ResetRemoval();
        _hovered = _pressed = false;
        CardMotion.ScaleX = CardMotion.ScaleY = 1;
        PosterMotion.ScaleX = PosterMotion.ScaleY = 1;
        PlayMotion.ScaleX = PlayMotion.ScaleY = 0.92;
        Hover.Opacity = HoverShade.Opacity = 0;
        Hover.Visibility = Visibility.Collapsed;
        Hover.IsHitTestVisible = false;
        Paint();
    }

    /// <summary>
    /// 删除确认之后的离场：淡出并向内收半步，给「这一张已经没了」一个看得见的落点。页面等它落定才把卡从
    /// 集合里摘掉 —— 先摘再动的话，那一格是凭空消失的（用户令 2026-11-03：删除要有统一的 UI 反馈）。
    /// <para>
    /// 只动不透明度和 <see cref="CardMotion"/> 的缩放／位移，不碰宽高与 Margin：横向带子按布局尺寸摆放，
    /// 改布局的属性在这里既贵、又会让相邻卡片跳一格。
    /// </para>
    /// </summary>
    internal Task PlayRemovalAsync()
    {
        _motion?.Stop();
        _motion = null;
        _removal?.Stop();
        _removal = null;
        _hovered = _pressed = false;
        Hover.IsHitTestVisible = false;

        if (!_live || XamlRoot is null || !HomeMotion.AnimationsEnabled) return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>();
        var board = new Storyboard();
        Add(Root, "Opacity", Root.Opacity, 0, EasingMode.EaseIn);
        Add(CardMotion, "ScaleX", CardMotion.ScaleX, 0.92);
        Add(CardMotion, "ScaleY", CardMotion.ScaleY, 0.92);
        Add(CardMotion, "TranslateY", CardMotion.TranslateY, -8);

        _removal = board;
        _removalDone = completion;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_removal, board)) return;

            // 不在这里 Stop：Completed 之后 Storyboard 还按 HoldEnd 握着终值，Stop 会把不透明度、
            // 缩放一并弹回 1，等页面接上来的那一拍屏上会闪一下完整的卡。真正的复位在 ResetRemoval。
            _removalDone = null;
            completion.TrySetResult(true);
        };
        board.Begin();
        return completion.Task;

        void Add(DependencyObject target, string path, double from, double to, EasingMode easing = EasingMode.EaseOut)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(190)),
                EasingFunction = new CubicEase { EasingMode = easing }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, path);
            board.Children.Add(animation);
        }
    }

    /// <summary>
    /// 把离场动画留下的状态还原。容器是回收着用的：不还原的话，下一张绑上来的卡会带着透明的底和缩过的
    /// 尺寸出现，而屏幕上看着像「这张卡没图」。
    /// </summary>
    internal void ResetRemoval()
    {
        _removal?.Stop();
        _removal = null;
        _removalDone?.TrySetResult(true);
        _removalDone = null;
        Root.Opacity = 1;
        CardMotion.ScaleX = CardMotion.ScaleY = 1;
        CardMotion.TranslateY = 0;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;

        // 按下去这一下不是「看」，是「用」：预览立刻退场，别挡着接下来要打开的页面或菜单。
        ClosePreview(immediately: true);
        NavigationTiming.Press("card");
        _pressed = true;
        AnimateCard();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        AnimateCard();
    }

    /// <summary>
    /// 封面那圈线：文件夹卡换自己那一张皮，其余一律是词表里的静止档。
    /// <para>
    /// 2026-10-27 用户令「去掉主页所有封面四周绿色的框」之后，指针和键盘焦点都不再换框线：悬停由封面推近、
    /// 压暗和那层按钮说，键盘焦点由外层按钮自己的中性焦点环说。所以这里只剩「是不是文件夹」一个判断，
    /// 换卡时由 <see cref="ResetMotion"/> 叫一次。
    /// </para>
    /// </summary>
    private void Paint() =>
        Art.Style = Card?.FolderFrame == true ? (Style)Resources["FolderFrameStyle"]
            : Card?.EpisodeFrame == true ? (Style)Resources["EpisodeFrameStyle"]
            : (Style)Application.Current.Resources["EgFrameStyle"];

    /// <summary>
    /// Deliberately not awaited: a container coming into view must not block the layout pass on a
    /// download. Everything that can go wrong is handled inside <see cref="CardItem.EnsurePosterAsync"/>.
    /// </summary>
    private void Begin()
    {
        // 初次赋卡可能早于挂入页面；Loaded 会重试。回收幻影仍有 XamlRoot，不能用 _live 拦它。
        if (XamlRoot is null) return;
        _ = Card?.EnsurePosterAsync(BrowsePresentation.WaitForImagesAsync(this));
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _watch.Enter();

    /// <summary>
    /// Hides the hover buttons, but only once the pointer has really left the card.
    /// <para>
    /// PointerExited bubbles, so stepping off one of the buttons raises it here too, and a click's
    /// pointer capture raises it without the pointer having moved at all. Either would flicker the
    /// buttons out from under the one being pressed. Where the pointer actually is settles it.
    /// </para>
    /// <para>
    /// And where the event <em>says</em> the pointer is cannot settle it on its own: a pointer that leaves
    /// the window from over the card reports the last position inside it, so this returns and no further
    /// exit ever comes. <see cref="HoverWatch"/> is the net under that — this is only the fast path.
    /// </para>
    /// </summary>
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root).Position;

        if (point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight)
            return;

        _watch.Leave();
    }

    /// <summary>
    /// 这张卡值不值得给一块预览。导航卡（文件夹、媒体库、人物）没有可读的简介，选择模式下的卡片
    /// 只等着被勾，两者都不弹 —— 弹出来也只是一块把邻居盖住的空板。
    /// </summary>
    private bool PreviewEligible() => _live && XamlRoot is not null && Card is { IsNavigation: false, IsSelecting: false } card
        && !card.Item.IsDisplayOnly;

    /// <summary>指针停在卡片上：开始计那一秒。已经在预览的卡片不重计。</summary>
    private void ArmPreview()
    {
        if (_previewOpen || !PreviewEligible()) return;

        if (_previewDwell is null)
        {
            _previewDwell = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_previewDwell is null) return;

            _previewDwell.Interval = TimeSpan.FromMilliseconds(PreviewDwellMs);
            _previewDwell.IsRepeating = false;
            _previewDwell.Tick += OnPreviewDwellTick;
        }

        _previewDwell.Stop();
        _previewDwell.Start();
    }

    private void OnPreviewDwellTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();

        // 计时这一秒里指针可能已经走了，也可能有别的浮层弹开（更多菜单、对话框）：那两种都不该再弹一块。
        if (!_hovered || AnotherPopupOpen()) return;
        OpenPreview();
    }

    /// <summary>
    /// 把面板摆到屏上。面板本身第一次用到才建 —— 一张从没被停住的卡片不该为它多背一棵树。
    /// <para>
    /// 位置自己算，不用 <see cref="Popup.PlacementTarget"/> 的贴边模式：2026-11-12 第二轮用户要的是
    /// 「从卡片中心向两边扩散」，也就是面板中心必须压在卡片中心上，靠边时再夹回窗口里 —— 贴左／贴右那种
    /// 模式给不了这个位置。坐标只问一次 <see cref="UIElement.TransformToVisual"/>，卡片在货架里、在页面滚动里
    /// 都已经被它算进去了。
    /// </para>
    /// </summary>
    private void OpenPreview()
    {
        if (_previewOpen || !PreviewEligible() || AnotherPopupOpen()) return;

        if (_previewHost is null)
        {
            _preview = new CardPreview();
            _previewWatch = new HoverWatch(_preview, hovered =>
            {
                _previewHovered = hovered;
                if (hovered) CancelPreviewClose();
                else SchedulePreviewClose();
            });
            _preview.PointerEntered += OnPreviewPointerEntered;
            _preview.PointerExited += OnPreviewPointerExited;
            _preview.PlayRequested += (_, _) => Raise(_preview.PlayAnchor, CardAction.Play);
            _preview.DetailsRequested += (_, _) => Raise(_preview.DetailsAnchor, CardAction.Details);

            _previewHost = new Popup
            {
                // 面板里有播放键，按项目约定（CLAUDE.md「弹窗」）点外面就该收起；点到的若是另一张卡，
                // 那一下本来也是导航，两件事不冲突。
                IsLightDismissEnabled = true,
                ShouldConstrainToRootBounds = true,
                Child = _preview,

                // 代码里新建的 Popup 不在任何页面自己的 XAML 树里，XamlRoot 得点名给 —— 不给的话 IsOpen
                // 只是把它记成「想开」，屏上什么都不出来。要在挂进树之前给：挂进去之后再设会抛。
                XamlRoot = XamlRoot,
            };
            _previewHost.Closed += (_, _) =>
            {
                // Closed 可以晚于同一 Popup 的重开投递，不让旧事件收掉新面板。
                if (_previewHost.IsOpen) return;
                ClosePreview(immediately: true);
            };
            Root.Children.Add(_previewHost);
        }

        var content = (UIElement)XamlRoot!.Content;
        var rootWidth = content is FrameworkElement page ? page.ActualWidth : 0;
        var rootHeight = content is FrameworkElement frame ? frame.ActualHeight : 0;
        var width = Math.Min(PreviewWidth, Math.Max(PreviewMinWidth, rootWidth - (PreviewMargin * 2)));

        // 卡片左上角在窗口内容里的位置。面板以卡片中心为原点向左右长，纵向与卡片齐平往下长。
        var origin = Root.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0));

        _preview!.SetSize(width);
        // 面板至少要盖住原来那张卡：底下的片名和副标题不能从面板边缘露出来。
        _preview.MinHeight = Math.Max(Root.ActualHeight, 0);
        _preview.Show(Card!);

        // 量一遍才知道面板多高：图是固定的 2:1，字有几行却不一定（简介两行封顶，续播那块时有时无）。
        // 量出来是 0（还没进树、量不出来）就按「与卡片齐平」摆，底下的夹取自然也就退化成不夹。
        _preview.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        var height = _preview.DesiredSize.Height;

        _preview.IsHitTestVisible = true;
        _previewHost!.HorizontalOffset = CardPreviewPlacement.Horizontal(
            origin.X, Root.ActualWidth, width, rootWidth, PreviewMargin);
        _previewHost.VerticalOffset = CardPreviewPlacement.Vertical(origin.Y, height, rootHeight, PreviewMargin);
        var art = Art.TransformToVisual(content).TransformBounds(new Windows.Foundation.Rect(0, 0, Art.ActualWidth, Art.ActualHeight));
        _preview.Prepare(new Windows.Foundation.Rect(
            art.Left - origin.X - _previewHost.HorizontalOffset,
            art.Top - origin.Y - _previewHost.VerticalOffset, art.Width, art.Height), height);
        _previewHost.IsOpen = true;
        _previewOpen = true;

        // 原封面边框与图片撑开，内容只通过裁剪显露，文字不拉伸。
        _preview.PlayEnter();
        WatchPreviewScroll();
        AnimateCard();
        LoadPreviewArtwork(width);
        Log.Debug("封面预览", $"打开「{Card?.Title}」：offset=({_previewHost.HorizontalOffset:0},{_previewHost.VerticalOffset:0})，width={width:0}，height={height:0}，card={Root.ActualWidth:0}x{Root.ActualHeight:0}，origin=({origin.X:0},{origin.Y:0})");
    }

    /// <summary>
    /// 去取底下那块宽图（Thumb／Backdrop／Primary）。面板已经开着、字已经在了，这张图到得晚一点只是
    /// 把它自己换上去；取不到就留着手上的海报。
    /// </summary>
    private void LoadPreviewArtwork(double width)
    {
        _previewArtwork?.Cancel();
        var cts = new CancellationTokenSource();
        _previewArtwork = cts;
        _ = LoadPreviewArtworkAsync(Card!, _preview!, (int)Math.Round(width), cts);
    }

    private async Task LoadPreviewArtworkAsync(CardItem card, CardPreview preview, int width, CancellationTokenSource cts)
    {
        var logo = LoadPreviewLogoAsync(card, preview, cts.Token);
        try
        {
            var artwork = await card.LoadPreviewArtworkAsync(width, cts.Token).ConfigureAwait(true);

            // 收起之后到点的那一趟不再往上贴：面板可能已经装了另一张卡。
            if (cts.IsCancellationRequested || !_previewOpen || !ReferenceEquals(_preview, preview)) return;
            preview.SetArtwork(artwork);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug("封面预览", $"宽图加载失败（{card.Title}）：{error.Message}");
        }
        finally
        {
            await logo.ConfigureAwait(true);
            // 先清字段再释放：中间没有 await，别人看到的要么是空、要么是一个还能用的。
            if (ReferenceEquals(_previewArtwork, cts)) _previewArtwork = null;
            cts.Dispose();
        }
    }

    private async Task LoadPreviewLogoAsync(CardItem card, CardPreview preview, CancellationToken cancellationToken)
    {
        try
        {
            var logo = await card.LoadPreviewLogoAsync(220, cancellationToken).ConfigureAwait(true);
            if (!cancellationToken.IsCancellationRequested && _previewOpen && ReferenceEquals(Card, card)) preview.SetLogo(logo);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Debug("封面预览", $"徽标加载失败：{error.GetType().Name}"); }
    }

    /// <summary>
    /// 工具用（<c>--show-preview</c>）：不等那一秒，直接把预览摆出来给截图。这台机器注不进鼠标事件，
    /// 而「停满一秒才出现」正是这次要拍的那一块。
    /// </summary>
    internal string? ShowPreviewForShot()
    {
        OpenPreview();
        return _previewOpen ? Card?.Title : null;
    }

    /// <summary>指针离开卡片：给一小段宽限再收，好让指针跨到面板上。</summary>
    private void SchedulePreviewClose()
    {
        if (!_previewOpen) return;

        if (_previewClose is null)
        {
            _previewClose = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_previewClose is null) return;

            _previewClose.Interval = TimeSpan.FromMilliseconds(PreviewCloseGraceMs);
            _previewClose.IsRepeating = false;
            _previewClose.Tick += OnPreviewCloseTick;
        }

        _previewClose.Stop();
        _previewClose.Start();
    }

    private void CancelPreviewClose() => _previewClose?.Stop();

    private void OnPreviewCloseTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();

        // 宽限里指针可能已经站到面板上或回到卡片上 —— 那两个都不该收。
        if (_hovered || _previewHovered) return;
        ClosePreview();
    }

    private void OnPreviewPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _previewWatch?.Enter();
    }

    private void OnPreviewPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_preview is { } preview)
        {
            var point = e.GetCurrentPoint(preview).Position;
            if (point.X >= 0 && point.Y >= 0 && point.X < preview.ActualWidth && point.Y < preview.ActualHeight) return;
        }
        _previewWatch?.Leave();
    }

    /// <summary>
    /// 收起面板。面板是回收着用的：先停计时与滚动监听，再让动画把它带走；卡片自己按原来的节奏退回静止。
    /// </summary>
    private void ClosePreview(bool immediately = false)
    {
        CancelPreviewClose();
        if (!_previewOpen) return;

        Log.Debug("封面预览", $"收起「{Card?.Title}」：immediate={immediately}");
        _previewOpen = false;
        _previewWatch?.Leave();
        _previewHovered = false;
        _previewArtwork?.Cancel();
        UnwatchPreviewScroll();
        AnimateCard();

        if (_preview is null || _previewHost is null) return;

        // 淡出的那一百多毫秒里别再挡住底下的卡片：指针可能已经落到邻居上了。
        _preview.IsHitTestVisible = false;

        if (immediately || !HomeMotion.AnimationsEnabled)
        {
            _previewHost.IsOpen = false;
            _preview.Reset();
            return;
        }

        _preview.PlayExit(() =>
        {
            // 收起途中又被打开：这一次的落点作废，别把新打开的面板关掉。
            if (_previewOpen) return;

            _previewHost!.IsOpen = false;
            _preview!.Reset();
        });
    }

    /// <summary>
    /// 盯住卡片所在的那几条滚动：面板是照打开那一刻的卡片位置摆的，内容一动两者就错位。
    /// 卡片在横向货架里、货架又在页面的纵向滚动里，两条都要盯。
    /// </summary>
    private void WatchPreviewScroll()
    {
        if (_previewScrolls.Count > 0) return;

        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer viewer) continue;

            viewer.ViewChanged += OnPreviewScrolled;
            _previewScrolls.Add(viewer);
        }
    }

    private void UnwatchPreviewScroll()
    {
        foreach (var viewer in _previewScrolls) viewer.ViewChanged -= OnPreviewScrolled;
        _previewScrolls.Clear();
    }

    private void OnPreviewScrolled(object? sender, ScrollViewerViewChangedEventArgs e) => ClosePreview(immediately: true);

    /// <summary>
    /// 有没有别的浮层正开着（更多菜单、对话框、筛选面板）。面板自己不算，工具提示也不算 ——
    /// 提示是「指针正停在这颗按钮上」的副产品，它开着的时候预览该照常出现。
    /// </summary>
    private bool AnotherPopupOpen()
    {
        if (XamlRoot is not { } root) return false;

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (ReferenceEquals(popup, _previewHost)) continue;
            if (popup.Child is ToolTip) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 自检：reveals the hover buttons for one layout pass and reports what they are and where they
    /// landed, then puts them back. Measured rather than asserted from the markup because
    /// 「播放按钮移动到媒体封面的中心」 and 「已播放、收藏、更多移动到封面的右下角」 are claims about pixels,
    /// and the alignments that produce them are two nested layouts away from the artwork they are
    /// measured against.
    /// </summary>
    internal CardHoverProbe ProbeHover()
    {
        var restore = Hover.Visibility;

        Hover.Visibility = Visibility.Visible;
        UpdateLayout();

        var middle = PlayButton
            .TransformToVisual(Art)
            .TransformPoint(new Windows.Foundation.Point(PlayButton.ActualWidth / 2, PlayButton.ActualHeight / 2));

        // The icon group's own bottom-right corner, in the artwork's coordinates. Measured from that
        // corner rather than from the group's origin so the number does not move when a library card
        // drops two of the three buttons and the group becomes a third as wide.
        var corner = Actions
            .TransformToVisual(Art)
            .TransformPoint(new Windows.Foundation.Point(Actions.ActualWidth, Actions.ActualHeight));

        var (rest, hovered, borderless) = ProbeFrame();
        var (playRest, playHover) = ProbePlay();

        var probe = new CardHoverProbe(
            Card?.Title ?? "",
            PlayButton.Visibility == Visibility.Visible,
            WatchedButton.Visibility == Visibility.Visible,
            FavoriteButton.Visibility == Visibility.Visible,
            MoreButton.Visibility == Visibility.Visible,
            PlayButton.ActualWidth,
            middle.X - Art.ActualWidth / 2,
            middle.Y - Art.ActualHeight / 2,
            Art.ActualWidth,
            Art.ActualHeight,
            Art.ActualWidth - corner.X,
            Art.ActualHeight - corner.Y,
            Paints(WatchedButton) || Paints(FavoriteButton) || Paints(MoreButton),
            HostSurfaceIsTransparent(),
            _watch.Probe(),
            rest,
            hovered,
            playRest,
            playHover,
            _focus.Probe(),
            borderless);

        Hover.Visibility = restore;
        UpdateLayout();

        return probe;
    }

    /// <summary>
    /// 自检：静止、指针和焦点三态都不画封面外圈，框线角色保持一致。
    /// 实际描边厚度与颜色分别读回，不把「颜色没变」当成「没有描边」。
    /// <para>
    /// 自检既没有指针也没有焦点，所以两个旗子是摆出来的 —— 而且是先摆再读，包括静止那一次：真有指针停在
    /// 这张卡上的时候（自检跑起来鼠标就在屏幕上某处），不摆的话读到的「静止」就是亮的。读完照原样放回去。
    /// </para>
    /// </summary>
    private (Windows.UI.Color Still, Windows.UI.Color Hover, bool Borderless) ProbeFrame()
    {
        var (hovered, focused) = (_hovered, _focused);

        (_hovered, _focused) = (false, false);
        Paint();
        var rest = Colour(Art.BorderBrush);
        var borderless = Art.BorderThickness == new Thickness(0);

        (_hovered, _focused) = (true, false);
        Paint();
        var hover = Colour(Art.BorderBrush);
        borderless &= Art.BorderThickness == new Thickness(0);
        (_hovered, _focused) = (false, true);
        Paint();
        borderless &= Art.BorderThickness == new Thickness(0);

        (_hovered, _focused) = (hovered, focused);
        Paint();

        return (rest, hover, borderless);
    }

    /// <summary>
    /// 自检：中间那颗播放键停着与指针压上去的底色，都是照实读回来的。
    /// <para>
    /// 停着那一档读按钮自己的 <c>Background</c>（样式给的那支灰）；指针那一档得问模板 —— 框架在
    /// <c>PointerOver</c> 里把底色画在模板根上，按钮自己的 <c>Background</c> 一个字都不变。切完照原样放回
    /// <c>Normal</c>，免得自检在屏上留下一颗亮着的键。
    /// </para>
    /// </summary>
    private (Windows.UI.Color Still, Windows.UI.Color Active) ProbePlay()
    {
        PlayButton.ApplyTemplate();
        var still = Colour(PlayButton.Background);
        try
        {
            var applied = VisualStateManager.GoToState(PlayButton, "PointerOver", false);
            var active = applied ? Colour(Presenter(PlayButton)?.Background) : default;
            return (still, active);
        }
        finally
        {
            VisualStateManager.GoToState(PlayButton, "Normal", false);
        }

        static ContentPresenter? Presenter(Button button)
        {
            return VisualTreeHelper.GetChildrenCount(button) > 0
                ? VisualTreeHelper.GetChild(button, 0) as ContentPresenter
                : null;
        }
    }

    /// <summary>
    /// 一支不是纯色的画刷（渐变、图片）在这里就是「没读到颜色」，而全透明的黑正好是自检要判红的东西。
    /// 亚克力报它的 <c>TintColor</c>：一张玻璃没有「一个颜色」，而这里问的是「这套主题的哪一档色写进去了」，
    /// 答法和 ThemeHost.ColorOf 对玻璃那一支一样（2026-11-08 播放键改成玻璃之后多出来的情况）。
    /// </summary>
    private static Windows.UI.Color Colour(Brush? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color,
        AcrylicBrush acrylic => acrylic.TintColor,
        _ => default
    };

    /// <summary>
    /// 自检：whether one of the three corner buttons still fills itself in any of the states the pointer
    /// puts it through — 「仅保留白色的图标按钮」 with nothing behind the glyph.
    /// <para>
    /// The button's own <c>Background</c> answers only for the resting state; the other two are the
    /// template's, set from theme resources that a Style cannot reach and that this card overrides in the
    /// group's own <c>Resources</c> instead. So each state is switched on and the template root — which
    /// for Button *is* the ContentPresenter the states paint — is read back. Getting one of those key
    /// names wrong is silent, and its only symptom is the grey chip coming back under the pointer.
    /// </para>
    /// </summary>
    private static bool Paints(Button button)
    {
        if (button.BorderThickness is { Left: > 0 } or { Top: > 0 } or { Right: > 0 } or { Bottom: > 0 })
            return true;

        var painted = false;

        foreach (var state in new[] { "Normal", "PointerOver", "Pressed" })
        {
            VisualStateManager.GoToState(button, state, false);
            painted |= Filled(Root(button));
        }

        VisualStateManager.GoToState(button, "Normal", false);

        // The button's own brush as well, in case the template has no ContentPresenter to read.
        return painted || Filled(button.Background);

        static Brush? Root(Button button) => VisualTreeHelper.GetChildrenCount(button) > 0
            ? (VisualTreeHelper.GetChild(button, 0) as ContentPresenter)?.Background
            : null;

        // Transparent is a brush, and one that paints nothing; null is no brush at all.
        static bool Filled(Brush? brush) => brush is not null and not SolidColorBrush { Color.A: 0 };
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e) => Raise(sender, CardAction.Play);

    private void OnWatchedClicked(object sender, RoutedEventArgs e)
    {
        // 先给一下回音：服务器那一趟无论成败，按下去这一拍总得响。
        GlyphPulse.Play(WatchedGlyphs);
        Raise(sender, CardAction.Watched);
    }

    private void OnFavoriteClicked(object sender, RoutedEventArgs e)
    {
        GlyphPulse.Play(FavoriteGlyphs);
        Raise(sender, CardAction.Favorite);
    }

    private void OnMoreClicked(object sender, RoutedEventArgs e) => Raise(sender, CardAction.More);

    /// <summary>
    /// 操作层上的点击不往卡片外面走。媒体库的 <c>ItemContainer</c> 在点击落在卡片上时会把条目
    /// 「调用」一次（打开详情或下一层），而按钮自己已经做完那件事了；Tapped 不吃掉的话，点「更多」
    /// 会在菜单旁边同时打开这一项的页面。
    /// </summary>
    private void OnActionTapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    /// <summary>
    /// Nothing here has to mark the press handled: ButtonBase already claims the pointer events it acts
    /// on, so the ItemContainer in a library grid and the wrapping Button on the home page never see the
    /// press as an invoke and never start playing an item the user was only marking watched.
    /// </summary>
    private void Raise(object sender, CardAction action)
    {
        if (Card is null || Card.IsSelecting || sender is not FrameworkElement anchor) return;

        // 面板上的播放键按下去之后，这块预览就该退场：接下来起播、换页，它没有留下来的理由。
        ClosePreview(immediately: true);
        ActionRequested?.Invoke(this, new CardActionEventArgs(action, anchor));
    }
}

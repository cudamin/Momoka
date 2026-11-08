using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Momoka.Shell.Views;

/// <summary>
/// 左上角封面上的两层浮层：播放键（2026-11-08 用户令「给剧页面和电影页面左上角的封面加上播放按钮」）和
/// 集页那张悬停遮罩（同日「集页面鼠标移到左上角的封面的时候要显示遮罩」）。
/// <para>
/// 播放键和海报卡悬停时那颗是同一颗键：静止深色磨砂玻璃，指针移到键自己身上才换强调色玻璃。这一页只补两件
/// 海报卡那边由 <c>PosterCard</c> 自己做掉的事 —— 「指针在封面上就浮出来」和「离开就收回去」；遮罩那一层是
/// 同一件事的另一半，两者共用一次指针判定和一段动画。
/// </para>
/// <para>
/// 两半判断仍然分开：指针在不在封面上走 <see cref="HoverWatch"/>（事件只是快路径，鼠标从封面直接移出窗口
/// 时那一声指针离开报的还是窗内最后一个坐标，靠它兜底，同 PosterCard 与 ShelfStrip），而「这一页该不该有这
/// 两样」问视图模型的 <c>CoverPlayVisibility</c>／<c>CoverShadeVisibility</c>。播放键要两半都成立才可命中：
/// 透明按钮拦一次点击，代价是用户以为封面点不进去。
/// </para>
/// </summary>
public sealed partial class DetailPage
{
    /// <summary>
    /// 悬停遮罩的不透明度 —— 同 <c>PosterCard</c> 的 <c>HoverShade</c>（那一页是 0.78 与 <c>EgScrimBrush</c>
    /// 一起写在一个 Storyboard 里）。两层说的是同一件事：「指针压在这一格上」，所以数也该是同一个。
    /// </summary>
    private const double CoverShadeOpacity = 0.78;

    private HoverWatch? _coverWatch;
    private Storyboard? _coverPlayMotion;
    private bool _coverHovered;

    /// <summary>
    /// 挂在构造里而不是 Loaded 里：这一页的实例会被复用，进树一次就多一支订阅。视图模型与页面同寿，
    /// 所以不另退订；只在离树时把浮层收回去（<see cref="HideCoverHover"/>）。
    /// </summary>
    private void PrepareCoverHover()
    {
        _coverWatch = new HoverWatch(CoverSlot, up =>
        {
            _coverHovered = up;
            SyncCoverHover();
        });
        ViewModel.PropertyChanged += OnCoverHoverVisibilityChanged;
    }

    private void OnCoverHoverVisibilityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(ViewModels.DetailViewModel.CoverPlayVisibility)
            or nameof(ViewModels.DetailViewModel.CoverShadeVisibility))) return;
        // 两样都收着的时候（翻到季、或者图被删掉）指针还停在原处也不能让下一张封面带着上一张的亮度回来。
        if (ViewModel.CoverPlayVisibility == Visibility.Visible
            || ViewModel.CoverShadeVisibility == Visibility.Visible) return;
        _coverHovered = false;
        SyncCoverHover();
    }

    /// <summary>指针进封面：浮出。</summary>
    private void OnCoverPointerEntered(object sender, PointerRoutedEventArgs e) => _coverWatch?.Enter();

    /// <summary>
    /// 指针走开：收起。同 PosterCard —— 这个事件会从封面按钮上冒泡上来，按一下的那次指针捕获也会让它响，
    /// 所以先问「指针到底还在不在这一格里」，再决定收不收；真离开窗口的那一路由 <see cref="HoverWatch"/> 兜。
    /// </summary>
    private void OnCoverPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CoverSlot).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < CoverSlot.ActualWidth && point.Y < CoverSlot.ActualHeight)
            return;

        _coverWatch?.Leave();
    }

    /// <summary>页面离树：把浮层收回去，别让复用的实例带着一颗亮着的键、一层亮着的遮罩回来。</summary>
    private void HideCoverHover()
    {
        _coverHovered = false;
        // Leave 自己会把 reveal(false) 走一遍；还没装表（构造里早退）时才需要直接落值。
        if (_coverWatch is { } watch) watch.Leave();
        else SyncCoverHover();
    }

    /// <summary>
    /// 两样浮层的终值（播放键的不透明度与 0.92 的缩放、遮罩的不透明度）之间走一段短动画；中途换向从当前值继续，
    /// 同 PosterCard。不给 <c>Visibility</c> 写值：这两格收不收由视图模型决定，这里只管它们露得有多亮、
    /// 播放键能不能点。
    /// <para>
    /// 两样各有各的判据（<c>CoverPlayVisibility</c> 只管电影和剧、<c>CoverShadeVisibility</c> 只管集页），
    /// 所以一页上最多只有一样是亮的；写在同一个 Storyboard 里是因为它们共用「指针在不在封面上」这一个状态，
    /// 分开写就是两段各自起停、还会互相打断的动画。
    /// </para>
    /// </summary>
    private void SyncCoverHover()
    {
        var play = _coverHovered && ViewModel.CoverPlayVisibility == Visibility.Visible;
        var shade = _coverHovered && ViewModel.CoverShadeVisibility == Visibility.Visible;
        CoverPlayButton.IsHitTestVisible = play;

        var fromPlay = CoverPlayButton.Opacity;
        var fromScale = CoverPlayMotion.ScaleX;
        var fromShade = CoverShade.Opacity;
        var toPlay = play ? 1d : 0d;
        var toScale = play ? 1d : 0.92d;
        var toShade = shade ? CoverShadeOpacity : 0d;

        _coverPlayMotion?.Stop();
        _coverPlayMotion = null;

        void Settle()
        {
            CoverPlayButton.Opacity = toPlay;
            CoverPlayMotion.ScaleX = CoverPlayMotion.ScaleY = toScale;
            CoverShade.Opacity = toShade;
        }

        if (XamlRoot is null || !HomeMotion.AnimationsEnabled
            || Math.Abs(fromPlay - toPlay) < 0.001 && Math.Abs(fromScale - toScale) < 0.001
                && Math.Abs(fromShade - toShade) < 0.001)
        {
            Settle();
            return;
        }

        var board = new Storyboard();
        Add(CoverPlayButton, "Opacity", fromPlay, toPlay);
        Add(CoverPlayMotion, "ScaleX", fromScale, toScale);
        Add(CoverPlayMotion, "ScaleY", fromScale, toScale);
        Add(CoverShade, "Opacity", fromShade, toShade);
        _coverPlayMotion = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_coverPlayMotion, board)) return;
            Settle();
            board.Stop();
            _coverPlayMotion = null;
        };
        board.Begin();

        void Add(DependencyObject target, string path, double start, double end)
        {
            var animation = new DoubleAnimation
            {
                From = start,
                To = end,
                Duration = new Duration(TimeSpan.FromMilliseconds(play || shade ? 200 : 160)),
                EasingFunction = new CubicEase { EasingMode = play || shade ? EasingMode.EaseOut : EasingMode.EaseIn }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, path);
            board.Children.Add(animation);
        }
    }

    /// <summary>
    /// 受控样本：把浮层亮一帧给探针拍照，再放回去 —— 和海报卡那张「亮一帧」的样本同一条规矩，
    /// 不代表真实指针手势（自检没有指针）。两样一起亮：探针要拍的是「指针压在封面上」这一态。
    /// </summary>
    internal void ShowCoverHoverSample(bool show)
    {
        CoverPlayButton.Opacity = show ? 1 : 0;
        CoverPlayButton.IsHitTestVisible = show;
        CoverPlayMotion.ScaleX = CoverPlayMotion.ScaleY = show ? 1 : 0.92;
        CoverShade.Opacity = show ? CoverShadeOpacity : 0;
    }

    /// <summary>
    /// 自检：把封面播放键亮一帧，量它落在封面的哪儿、多大、静止与指针两态的底色，再把浮层放回原样。
    /// <para>
    /// 和 <see cref="PosterCard.ProbeHover"/> 同一个理由：「浮在封面正中」和「静止是中性的深色玻璃」
    /// 是关于像素的话，靠读标记证明不了。两态的底同样要问模板 —— 指针那一档是框架画在模板根上的，
    /// 按钮自己的 <c>Background</c> 一个字都不变。
    /// </para>
    /// </summary>
    internal (bool Shown, double Size, double OffsetX, double OffsetY,
        Windows.UI.Color RestTint, Windows.UI.Color HoverTint) ProbeCoverPlay()
    {
        var restoreOpacity = CoverPlayButton.Opacity;
        var restoreHit = CoverPlayButton.IsHitTestVisible;
        CoverPlayButton.Opacity = 1;
        CoverPlayButton.IsHitTestVisible = true;
        UpdateLayout();

        var middle = CoverPlayButton.TransformToVisual(PosterStill)
            .TransformPoint(new Windows.Foundation.Point(CoverPlayButton.ActualWidth / 2, CoverPlayButton.ActualHeight / 2));

        CoverPlayButton.ApplyTemplate();
        var rest = InkWash(CoverPlayButton.Background);
        var hover = default(Windows.UI.Color);
        try
        {
            if (VisualStateManager.GoToState(CoverPlayButton, "PointerOver", false))
                hover = InkWash(Presenter()?.Background);
        }
        finally
        {
            VisualStateManager.GoToState(CoverPlayButton, "Normal", false);
            CoverPlayButton.Opacity = restoreOpacity;
            CoverPlayButton.IsHitTestVisible = restoreHit;
            UpdateLayout();
        }

        return (CoverPlayButton.Visibility == Visibility.Visible, CoverPlayButton.ActualWidth,
            middle.X - PosterStill.ActualWidth / 2, middle.Y - PosterStill.ActualHeight / 2, rest, hover);

        ContentPresenter? Presenter() => VisualTreeHelper.GetChildrenCount(CoverPlayButton) > 0
            ? VisualTreeHelper.GetChild(CoverPlayButton, 0) as ContentPresenter
            : null;

        // 亚克力报 TintColor（同 ThemeHost.ColorOf 与 PosterCard.Colour）：这里问的是哪一档色写进去了。
        static Windows.UI.Color InkWash(Brush? brush) => brush switch
        {
            SolidColorBrush solid => solid.Color,
            AcrylicBrush acrylic => acrylic.TintColor,
            _ => default
        };
    }
}

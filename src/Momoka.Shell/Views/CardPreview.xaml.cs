using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Shell.Views;

/// <summary>
/// 封面悬停一秒之后撑开的那块预览（2026-11-12 用户令，参考微软商店）。
/// <para>
/// 只做三件事：把 <see cref="CardItem"/> 上已有的字段填进面板、按面板宽度摆好底下那块 2:1 缩略图、
/// 放开门／关门那两段短动画。位置、尺寸下限、存活与收起时机都归 <see cref="PosterCard"/> ——
/// 它才知道卡片多大、窗口多宽、指针在哪、容器有没有被回收。
/// </para>
/// <para>
/// 动画只动不透明度与 <c>RenderTransform</c>：面板的宽高由外面写死，动画不参与布局，也就不会在
/// 撑开的那一帧里把面板从别处「量」一遍。系统关掉动画时直接落定，与卡片自己的悬停动画同一条规矩。
/// </para>
/// <para>
/// 2026-11-12 第四轮（用户令「缩略图在下，播放按钮在右上角」）：版式照商店那块应用卡 —— 左上片名、
/// 右上操作键、中间简介、底下宽图；第五轮去掉左上角那张小海报。横向缩放的原点在面板正中，而面板正中就是卡片正中，于是它看起来是
/// 从卡片里向左右两边长开的。
/// </para>
/// </summary>
public sealed partial class CardPreview : UserControl
{
    /// <summary>撑开 200 毫秒、收回 130 毫秒：比卡片自己那段 240/180 的悬停动画略快一档。</summary>
    private const int EnterMs = 200;
    private const int ExitMs = 130;

    /// <summary>面板左右内边距，和 XAML 里那圈 <c>Padding="14"</c> 是同一个数 —— 缩略图要按扣掉它之后的宽算。</summary>
    private const double PanelPadding = 14;

    private Storyboard? _motion;
    private readonly ImageArrivalFade _artworkArrival;

    public CardPreview()
    {
        InitializeComponent();
        Panel.Opacity = 0;
        _artworkArrival = new ImageArrivalFade(PreviewImage);
    }

    /// <summary>面板里那颗播放键被按下。由 <see cref="PosterCard"/> 转成同一条 <see cref="PosterCard.ActionRequested"/>。</summary>
    internal event EventHandler? PlayRequested;

    /// <summary>播放键本身，给菜单／命令当锚点（和卡片上那三颗按钮同一个用法）。</summary>
    internal FrameworkElement PlayAnchor => PlayButton;

    /// <summary>
    /// 面板宽度，以及底下那块缩略图的高度。宽度由 <see cref="PosterCard"/> 按窗口算；缩略图按面板宽扣掉
    /// 左右内边距之后的 2:1 来摆 —— 所有卡片一个形状，横版继续观看和竖版海报都一样。
    /// </summary>
    internal void SetSize(double width)
    {
        Width = width;
        Media.MinHeight = CardPreviewPlacement.MediaHeight(Math.Max(0, width - (PanelPadding * 2)));
    }

    /// <summary>
    /// 换一张卡的内容。每一行都可能缺席：没有简介就整行收起，没有续播位置就收起进度 —— 留下空白比写一句
    /// 「暂无简介」诚实。
    /// <para>
    /// 缩略图先用手上这张卡自己的图（宽卡本来就是宽图，竖版海报会被裁进 2:1 的框），
    /// 宽图由 <see cref="PosterCard"/> 随后取回来换上去 —— 面板不为了等一张大图而晚开。
    /// </para>
    /// </summary>
    internal void Show(CardItem card)
    {
        var item = card.Item;

        PreviewImage.Source = card.Poster;

        TitleText.Text = card.Title;

        MetaText.Text = Join(CardPreviewText.Meta(item), CardPreviewText.Rating(item), CardPreviewText.Genres(item));
        MetaText.Visibility = Shown(MetaText.Text.Length > 0);

        OverviewText.Text = CardPreviewText.Overview(item);
        OverviewText.Visibility = Shown(OverviewText.Text.Length > 0);

        ResumeText.Text = CardPreviewText.Resume(item);
        ResumeBlock.Visibility = Shown(ResumeText.Text.Length > 0);

        var progress = CardPreviewText.Progress(item);
        ProgressDone.Width = new GridLength(progress, GridUnitType.Star);
        ProgressLeft.Width = new GridLength(Math.Max(0, 100 - progress), GridUnitType.Star);

        // 和卡片正中那颗键同一条判据：文件夹、人物和虚拟条目没有「播放」这回事。
        PlayButton.Visibility = card.PlayVisibility;
        AutomationProperties.SetName(PlayButton, card.PlayActionTip);
        ToolTipService.SetToolTip(PlayButton, card.PlayActionTip);
    }

    /// <summary>
    /// 换上一张更宽的图（<see cref="PosterCard"/> 取回来的 Thumb／Backdrop）。取不到时什么也不做，
    /// 面板留着 <see cref="Show"/> 那一刻的那张 —— 没有宽图不是「没有图」。
    /// </summary>
    internal void SetArtwork(BitmapImage? artwork)
    {
        if (artwork is null) return;
        PreviewImage.Source = artwork;
    }

    /// <summary>
    /// 撑开：从卡片那么宽的位置向两边长到整块面板，同时淡入。
    /// <paramref name="fromScaleX"/> 是「卡片宽 ÷ 面板宽」—— 面板的缩放原点在正中，所以起点看起来
    /// 正好压在原来那张卡上。
    /// </summary>
    internal void PlayEnter(double fromScaleX)
    {
        Stop();
        Settle();

        if (!HomeMotion.AnimationsEnabled)
        {
            Panel.Opacity = 1;
            return;
        }

        Panel.Opacity = 0;
        PanelMotion.ScaleX = Math.Clamp(fromScaleX, 0.2, 1);
        PanelMotion.ScaleY = 0.94;
        PanelMotion.TranslateY = 4;

        var board = new Storyboard();
        Add(board, Panel, "Opacity", 0, 1, EnterMs);
        Add(board, PanelMotion, "ScaleX", PanelMotion.ScaleX, 1, EnterMs);
        Add(board, PanelMotion, "ScaleY", 0.94, 1, EnterMs);
        Add(board, PanelMotion, "TranslateY", 4, 0, EnterMs);

        _motion = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_motion, board)) return;
            _motion = null;
            board.Stop();
            Settle();
            Panel.Opacity = 1;
        };
        board.Begin();
    }

    /// <summary>
    /// 收回：淡出并向卡片中心缩回去，落定后交给 <paramref name="closed"/> 去关掉 Popup。中途被重新
    /// 打开时这次落点仍然会执行 <paramref name="closed"/>，由调用方自己判断要不要真的关。
    /// </summary>
    internal void PlayExit(double toScaleX, Action closed)
    {
        Stop();
        Settle();

        if (!HomeMotion.AnimationsEnabled)
        {
            Panel.Opacity = 0;
            closed();
            return;
        }

        var board = new Storyboard();
        Add(board, Panel, "Opacity", 1, 0, ExitMs, EasingMode.EaseIn);
        Add(board, PanelMotion, "ScaleX", 1, Math.Clamp(toScaleX, 0.2, 1), ExitMs, EasingMode.EaseIn);
        Add(board, PanelMotion, "ScaleY", 1, 0.96, ExitMs, EasingMode.EaseIn);

        _motion = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_motion, board)) return;
            _motion = null;
            board.Stop();
            Settle();
            Panel.Opacity = 0;
            closed();
        };
        board.Begin();
    }

    /// <summary>收起到「没打开过」的样子；下次打开前由 <see cref="PlayEnter"/> 重新摆起点。</summary>
    internal void Reset()
    {
        Stop();
        Settle();
        Panel.Opacity = 0;
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);

    private void Stop()
    {
        _motion?.Stop();
        _motion = null;
    }

    /// <summary>动画落点：缩放与位移回正。不透明度由各段动画自己决定，别在这里替它们写。</summary>
    private void Settle()
    {
        PanelMotion.ScaleX = PanelMotion.ScaleY = 1;
        PanelMotion.TranslateY = 0;
    }

    private static void Add(Storyboard board, DependencyObject target, string path, double from, double to,
        int milliseconds, EasingMode easing = EasingMode.EaseOut)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = easing }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, path);
        board.Children.Add(animation);
    }

    /// <summary>三段读数接成一行；空的那几段直接跳过，全空就是空串。</summary>
    private static string Join(params string[] parts) =>
        string.Join("  ·  ", parts.Where(part => part.Length > 0));

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

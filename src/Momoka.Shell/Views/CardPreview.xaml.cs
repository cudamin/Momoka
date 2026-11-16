using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Media;
using Windows.Foundation;
using Windows.Media.Playback;
using MediaSource = Windows.Media.Core.MediaSource;

namespace Momoka.Shell.Views;

/// <summary>固定内容布局，独立合成动画只撑开边框、裁剪区和原封面；文字不缩放也不淡出。</summary>
public sealed partial class CardPreview : UserControl
{
    private const int EnterMs = 250;
    private const int ExitMs = 167;
    private const int ArtworkFadeMs = 200;
    private const double PanelPadding = 19;
    private CompositionPropertySet? _state;

    /// <summary>
    /// 弹簧静定之后把面板落定的那一拍。弹簧自己在合成线程上收敛，没有可靠的「完成」回调整到 UI 线程，
    /// 所以时长仍由调用方给的毫秒数决定 —— 与原来那段 keyframe 一样，只是曲线换成了弹簧。
    /// </summary>
    private DispatcherQueueTimer? _completion;

    private RectangleClip? _clip;
    private InsetClip? _hitClip;
    private Action? _exitCompleted;
    private BitmapImage? _pendingArtwork;
    private Storyboard? _artworkFade;
    private CardItem? _card;
    private bool _running;
    private bool _shown;
    private int _generation;
    private bool _watchingMotion;

    /// <summary>
    /// 悬停面板里的预告片：静音、循环、不接收指针，盖在静态宽图上面。没有预告、读取失败或面板收起时
    /// 整层藏起来，下面那张宽图原样露出来；播放器与流随面板复用，每次开合都重新建。
    /// </summary>
    private MediaPlayer? _trailerPlayer;
    private MediaPlayerElement? _trailerElement;
    private MediaSource? _trailerSource;
    private StoreTrailerPreview? _trailerPreview;
    private Storyboard? _trailerFade;
    private int _trailerGeneration;

    public CardPreview()
    {
        InitializeComponent();
        Panel.Visibility = Visibility.Collapsed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_watchingMotion) return;
        _watchingMotion = true;
        HomeMotion.PreferenceChanged += OnMotionPreferenceChanged;
        ShellPrefs.TrailerChanged += OnTrailerPreferenceChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        // Popup 快速关闭后重开也会迟到投递 Unloaded；仍在活动树上的新预览不能被旧事件清空。
        if (XamlRoot is not null) return;
        if (_watchingMotion) HomeMotion.PreferenceChanged -= OnMotionPreferenceChanged;
        ShellPrefs.TrailerChanged -= OnTrailerPreferenceChanged;
        _watchingMotion = false;
        Reset();
    }

    internal event EventHandler? PlayRequested;
    internal event EventHandler? DetailsRequested;
    internal FrameworkElement PlayAnchor => PlayButton;
    internal FrameworkElement DetailsAnchor => DetailsButton;

    internal void SetSize(double width)
    {
        Width = width;
        Height = double.NaN;
        Media.MinHeight = CardPreviewPlacement.MediaHeight(Math.Max(0, width - (PanelPadding * 2)));
    }

    internal void Show(CardItem card)
    {
        StopTrailer();
        Panel.Visibility = Visibility.Visible;
        if (!ReferenceEquals(_card, card))
        {
            StopArtworkFade();
            _pendingArtwork = null;
            PreviewImage.Source = card.Poster;
            _card = card;
        }
        LogoImage.Source = null;
        LogoSlot.Visibility = Shown(ItemArtwork.Plate(card.Item) is not null);
        TitleText.Text = card.Title;
        ApplyFacts(card.Item);
        ResumeText.Text = CardPreviewText.Resume(card.Item);
        ResumeBlock.Visibility = Shown(ResumeText.Text.Length > 0);
        ResumeProgress.Value = CardPreviewText.Progress(card.Item);
        AutomationProperties.SetName(ResumeProgress, $"观看进度 {ResumeText.Text}");
        PlayButton.Visibility = card.PlayVisibility;
        PlayText.Text = CardPreviewText.PlayLabel(card.Item);
        AutomationProperties.SetName(PlayButton, card.Item.HasResumePosition ? card.PlayActionTip : PlayText.Text);
        ToolTipService.SetToolTip(PlayButton, PlayText.Text);
        AutomationProperties.SetName(DetailsButton, $"查看{card.Title}详情");
        Heading.Margin = new Thickness(0, 0, card.PlayVisibility == Visibility.Visible ? 116 : 0, 0);
    }

    /// <summary>
    /// 预览的资料行：年份、题材、评分为一行，概要单独一行。媒体库列表按显示设置只点名卡片底下那几项，
    /// 悬停时会先补读详情再打开面板，所以这里拿到的已经是和主页同一份资料。
    /// </summary>
    private void ApplyFacts(EmbyItem item)
    {
        MetaText.Text = CardPreviewText.Date(item);
        MetaText.Visibility = Shown(MetaText.Text.Length > 0);
        GenresText.Text = CardPreviewText.Genres(item);
        GenresText.Visibility = Shown(GenresText.Text.Length > 0);
        RatingText.Text = CardPreviewText.Rating(item);
        RatingBlock.Visibility = Shown(RatingText.Text.Length > 0);
        AutomationProperties.SetName(RatingText, $"评分 {RatingText.Text}");
        FactsBlock.Visibility = Shown(MetaText.Text.Length + GenresText.Text.Length + RatingText.Text.Length > 0);
        OverviewText.Text = CardPreviewText.Overview(item);
        OverviewText.Visibility = Shown(OverviewText.Text.Length > 0);
    }

    internal void SetArtwork(BitmapImage? artwork)
    {
        if (artwork is null || !_shown) return;
        if (_running || _artworkFade is not null) { _pendingArtwork = artwork; return; }
        if (ReferenceEquals(PreviewImage.Source, artwork)) return;
        if (!HomeMotion.AnimationsEnabled || PreviewImage.Source is null)
        {
            PreviewImage.Source = artwork;
            return;
        }

        // 两张图共用随封面撑开的表面。新图在不透明旧图上渐显，混合时不会闪出底色。
        TransitionImage.Source = artwork;
        var board = new Storyboard();
        var animation = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(ArtworkFadeMs) };
        Storyboard.SetTarget(animation, TransitionImage);
        Storyboard.SetTargetProperty(animation, "Opacity");
        board.Children.Add(animation);
        _artworkFade = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_artworkFade, board)) return;
            PreviewImage.Source = artwork;
            StopArtworkFade();
            ApplyPendingArtwork();
        };
        try { board.Begin(); }
        catch (Exception)
        {
            // 离树或关闭过程中平台可能拒绝动画；新图仍正常显示，后续请求也不能卡在队列里。
            PreviewImage.Source = artwork;
            StopArtworkFade();
            ApplyPendingArtwork();
        }
    }

    private void ApplyPendingArtwork()
    {
        if (_running || _artworkFade is not null || !_shown) return;
        var artwork = _pendingArtwork;
        _pendingArtwork = null;
        SetArtwork(artwork);
    }

    private void StopArtworkFade()
    {
        var board = _artworkFade;
        _artworkFade = null;
        board?.Stop();
        TransitionImage.Opacity = 0;
        TransitionImage.Source = null;
    }

    internal void SetLogo(BitmapImage? logo) => LogoImage.Source = logo;

    /// <summary>
    /// 服务器内有本地预告时把它接到面板的媒体视口里：静音、循环、自动播放都按设置页「界面 → 预告片」
    /// 那三项来，不显示传输控件，也不接指针。流由调用方准备好并交出所有权；停止时这一层藏起来，静态宽图仍在下面。
    /// </summary>
    internal void PlayTrailer(StoreTrailerPreview preview)
    {
        StopTrailer();
        var preferences = ShellPrefs.Trailers;
        if (!_shown || !preferences.HoverAutoplay)
        {
            _ = DisposeTrailerAsync(preview);
            return;
        }

        var generation = ++_trailerGeneration;
        _trailerPreview = preview;
        var player = new MediaPlayer
        {
            IsMuted = preferences.HoverMuted,
            IsLoopingEnabled = preferences.HoverLoop,
            AutoPlay = true
        };
        player.CommandManager.IsEnabled = false;
        var source = MediaSource.CreateFromUri(preview.Source);
        var element = new MediaPlayerElement
        {
            AreTransportControlsEnabled = false,
            Stretch = Stretch.UniformToFill,
            IsHitTestVisible = false,
            IsTabStop = false
        };
        element.SetMediaPlayer(player);
        TrailerHost.Children.Add(element);
        _trailerPlayer = player;
        _trailerElement = element;
        _trailerSource = source;
        player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _trailerGeneration) RevealTrailer();
        });
        player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _trailerGeneration) StopTrailer();
        });
        player.Source = source;
    }

    private void RevealTrailer()
    {
        TrailerHost.Visibility = Visibility.Visible;
        if (!HomeMotion.AnimationsEnabled)
        {
            TrailerHost.Opacity = 1;
            return;
        }

        var board = new Storyboard();
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(ArtworkFadeMs) };
        Storyboard.SetTarget(fade, TrailerHost);
        Storyboard.SetTargetProperty(fade, "Opacity");
        board.Children.Add(fade);
        _trailerFade = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_trailerFade, board)) return;
            _trailerFade = null;
            TrailerHost.Opacity = 1;
        };
        try { board.Begin(); }
        catch (Exception)
        {
            // 离树或关闭过程中平台可能拒绝动画；视频本身照常显示。
            _trailerFade = null;
            TrailerHost.Opacity = 1;
        }
    }

    /// <summary>收起或换卡：停播、断开媒体源、释放流，整层重新藏到静态图后面。</summary>
    internal void StopTrailer()
    {
        ++_trailerGeneration;
        var board = _trailerFade;
        _trailerFade = null;
        board?.Stop();
        TrailerHost.Opacity = 0;
        TrailerHost.Visibility = Visibility.Collapsed;

        var player = _trailerPlayer;
        var element = _trailerElement;
        var source = _trailerSource;
        var preview = _trailerPreview;
        _trailerPlayer = null;
        _trailerElement = null;
        _trailerSource = null;
        _trailerPreview = null;

        preview?.Cancel();
        try { element?.SetMediaPlayer(null); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        TrailerHost.Children.Clear();
        try { player?.Pause(); if (player is not null) player.Source = null; }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        try { source?.Dispose(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        try { player?.Dispose(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException) { }
        if (preview is not null) _ = DisposeTrailerAsync(preview);
    }

    private static async Task DisposeTrailerAsync(StoreTrailerPreview preview)
    {
        try { await preview.DisposeAsync(); }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
    }

    private void OnMediaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PreviewImage.Width = TransitionImage.Width = e.NewSize.Width;
        PreviewImage.Height = TransitionImage.Height = e.NewSize.Height;
    }

    // 源矩形与目标同为 Popup 内的 DIP。先固定最终布局，再安装只在合成线程求值的表达式。
    internal void Prepare(Rect source, double height)
    {
        Height = height;
        // 可复用 Popup 的图片表面必须参加本次测量；撑开后再显现会偶发保留上一轮零尺寸。
        TransitionCover.Visibility = Visibility.Visible;
        Measure(new Size(Width, height));
        Arrange(new Rect(0, 0, Width, height));
        UpdateLayout();
        var compositor = ElementCompositionPreview.GetElementVisual(Panel).Compositor;
        _state ??= compositor.CreatePropertySet();
        if (!_running) _state.InsertScalar("Progress", _shown ? 1 : 0);
        _clip ??= compositor.CreateRectangleClip();
        _clip.TopLeftRadius = _clip.TopRightRadius = _clip.BottomLeftRadius = _clip.BottomRightRadius = new Vector2(16);
        ElementCompositionPreview.GetElementVisual(Panel).Clip = _clip;
        // XAML 命中测试会读取 InsetClip；RectangleClip 只负责圆角绘制。
        _hitClip ??= compositor.CreateInsetClip();
        ElementCompositionPreview.GetElementVisual(HitRoot).Clip = _hitClip;
        Bind(_hitClip, "LeftInset", (float)source.Left, 0);
        Bind(_hitClip, "TopInset", (float)source.Top, 0);
        Bind(_hitClip, "RightInset", (float)(Width - source.Right), 0);
        Bind(_hitClip, "BottomInset", (float)(height - source.Bottom), 0);
        Bind(_clip, "Left", (float)source.Left, 0);
        Bind(_clip, "Top", (float)source.Top, 0);
        Bind(_clip, "Right", (float)source.Right, (float)Width);
        Bind(_clip, "Bottom", (float)source.Bottom, (float)height);
        Morph(Surface, source, new Rect(0, 0, Width, height));

        var media = Media.TransformToVisual(Panel).TransformBounds(new Rect(0, 0, Media.ActualWidth, Media.ActualHeight));
        TransitionCover.Width = media.Width;
        TransitionCover.Height = media.Height;
        PreviewImage.Width = TransitionImage.Width = media.Width;
        PreviewImage.Height = TransitionImage.Height = media.Height;
        Canvas.SetLeft(TransitionCover, media.Left);
        Canvas.SetTop(TransitionCover, media.Top);
        TransitionCover.Measure(new Size(media.Width, media.Height));
        TransitionCover.Arrange(media);
        UpdateLayout();
        Morph(TransitionCover, source, media);
    }

    internal void PlayEnter()
    {
        if (_state is null) return;
        _shown = true;
        Panel.Visibility = Visibility.Visible;
        TransitionCover.Visibility = Visibility.Visible;
        Animate(1, EnterMs, null);
    }

    internal void PlayExit(Action closed)
    {
        if (_state is null) { Reset(); closed(); return; }
        _shown = false;
        // 新旧图片一起收回，保持当前交叉淡化画面，退出第一帧不换图。
        TransitionCover.Visibility = Visibility.Visible;
        Animate(0, ExitMs, closed);
    }

    private void Animate(float target, int milliseconds, Action? closed)
    {
        var generation = ++_generation;
        _completion?.Stop();
        _completion = null;
        _exitCompleted = closed;
        if (!HomeMotion.AnimationsEnabled)
        {
            Finish(target, closed);
            return;
        }
        _running = true;
        var compositor = _state!.Compositor;
        // 弹簧驱动同一个 Progress，边框、裁剪区、命中区和封面的表达式一个都不用改。
        // 参考 WinUI Gallery 的自然运动（Samples/XamlCompInterop，固定提交 6de91fe）：那一份把
        // CreateSpringVector3Animation 挂在元素的 Scale 上；这块面板的 Scale 是一组读 Progress 的
        // 表达式，所以弹簧落在 Progress 这一层。周期取原时长除以六，静定时间与原来那段 keyframe
        // 相当；撑开留一点回弹，收起用临界阻尼，免得进度冲过零再弹回来。
        using var spring = compositor.CreateSpringScalarAnimation();
        spring.Target = "Progress";
        spring.FinalValue = target;
        spring.DampingRatio = target >= 1 ? 0.82f : 1f;
        spring.Period = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds / 6.0));
        spring.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        _state.StartAnimation("Progress", spring);
        // 弹簧没有跨线程的完成回调；落定仍按原来那段 keyframe 的时长走，反转与关闭动画都在这里接住。
        var completion = DispatcherQueue.GetForCurrentThread().CreateTimer();
        completion.Interval = TimeSpan.FromMilliseconds(milliseconds);
        completion.IsRepeating = false;
        completion.Tick += (_, _) =>
        {
            if (generation != _generation) return;
            _completion = null;
            completion.Stop();
            Finish(target, closed);
        };
        _completion = completion;
        completion.Start();
    }

    private void Finish(float target, Action? closed)
    {
        _completion?.Stop();
        _completion = null;
        _state?.StopAnimation("Progress");
        _state?.InsertScalar("Progress", target);
        _running = false;
        _exitCompleted = null;
        Panel.Visibility = target == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (target == 1) ApplyPendingArtwork();
        closed?.Invoke();
    }

    internal void Reset()
    {
        _generation++;
        _completion?.Stop();
        _completion = null;
        _state?.StopAnimation("Progress");
        _state?.InsertScalar("Progress", 0);
        _running = _shown = false;
        _exitCompleted = null;
        _pendingArtwork = null;
        StopArtworkFade();
        StopTrailer();
        _card = null;
        Panel.Visibility = Visibility.Collapsed;
        PreviewImage.Source = TransitionImage.Source = LogoImage.Source = null;
    }

    /// <summary>设置页改了预告片那三项：已经在播的面板当场换静音/循环，总开关关掉就收掉视频层。</summary>
    private void OnTrailerPreferenceChanged(TrailerSettings preferences)
    {
        if (!preferences.HoverAutoplay)
        {
            StopTrailer();
            return;
        }
        if (_trailerPlayer is not { } player) return;
        player.IsMuted = preferences.HoverMuted;
        player.IsLoopingEnabled = preferences.HoverLoop;
    }

    private void OnMotionPreferenceChanged()
    {
        if (HomeMotion.AnimationsEnabled) return;
        var artwork = _pendingArtwork ?? TransitionImage.Source;
        _pendingArtwork = null;
        if (artwork is not null) PreviewImage.Source = artwork;
        StopArtworkFade();
        if (!_running) return;
        var closed = _exitCompleted;
        ++_generation;
        _completion?.Stop();
        _completion = null;
        Finish(_shown ? 1 : 0, closed);
    }

    private void Morph(FrameworkElement element, Rect source, Rect target)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        visual.CenterPoint = Vector3.Zero;
        using var scale = _state!.Compositor.CreateExpressionAnimation("Lerp(start, finish, state.Progress)");
        scale.SetReferenceParameter("state", _state);
        scale.SetVector3Parameter("start", new Vector3((float)(source.Width / target.Width), (float)(source.Height / target.Height), 1));
        scale.SetVector3Parameter("finish", Vector3.One);
        visual.StartAnimation("Scale", scale);
        using var offset = _state.Compositor.CreateExpressionAnimation("Lerp(start, finish, state.Progress)");
        offset.SetReferenceParameter("state", _state);
        offset.SetVector3Parameter("start", new Vector3((float)(source.Left - target.Left), (float)(source.Top - target.Top), 0));
        offset.SetVector3Parameter("finish", Vector3.Zero);
        visual.StartAnimation("Translation", offset);
    }

    private void Bind(CompositionObject target, string property, float start, float finish)
    {
        using var expression = _state!.Compositor.CreateExpressionAnimation("Lerp(start, finish, state.Progress)");
        expression.SetReferenceParameter("state", _state);
        expression.SetScalarParameter("start", start);
        expression.SetScalarParameter("finish", finish);
        target.StartAnimation(property, expression);
    }

    /// <summary>
    /// 把屏上那枚徽标和片名的位置交出去，好让详情页从同一个地方把它们接过去（2026-11-16）。
    /// 坐标是窗口内容坐标：面板在 Popup 里，偏移由调用方手里的两个数补上。只交真的画出来的那几样 ——
    /// 徽标没图或还没解出来时只有片名飞。
    /// </summary>
    internal PreviewHeadingCapture? CaptureHeading(double offsetX, double offsetY, EmbyItem item)
    {
        if (!_shown || !Drawn(TitleText) || !double.IsFinite(offsetX) || !double.IsFinite(offsetY)) return null;
        var title = Bounds(TitleText);
        Rect? plate = null;
        ArtworkRef? artwork = null;
        BitmapImage? picture = null;
        if (Drawn(LogoImage) && LogoImage.Source is BitmapImage source && ItemArtwork.Plate(item) is { } reference)
        {
            plate = Bounds(LogoImage);
            artwork = reference;
            picture = source;
        }
        return new PreviewHeadingCapture(item, title, TitleText.FontSize, plate, artwork, picture);

        // 面板里的字不吃动画，实际尺寸就是最终尺寸；量的是它自己的版面框，不是整个面板。
        static bool Drawn(FrameworkElement element) => element.XamlRoot is not null
            && element.ActualWidth > 0 && element.ActualHeight > 0;

        Rect Bounds(FrameworkElement element)
        {
            var bounds = element.TransformToVisual(Panel).TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return new Rect(bounds.X + offsetX, bounds.Y + offsetY, bounds.Width, bounds.Height);
        }
    }

    private void OnPlayClicked(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);
    private void OnDetailsClicked(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke(this, EventArgs.Empty);
    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

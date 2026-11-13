using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Infrastructure;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>固定内容布局，独立合成动画只撑开边框、裁剪区和原封面；文字不缩放也不淡出。</summary>
public sealed partial class CardPreview : UserControl
{
    private const int EnterMs = 250;
    private const int ExitMs = 167;
    private const double PanelPadding = 19;
    private CompositionPropertySet? _state;
    private CompositionScopedBatch? _batch;
    private RectangleClip? _clip;
    private InsetClip? _hitClip;
    private Action? _exitCompleted;
    private BitmapImage? _pendingArtwork;
    private bool _running;
    private bool _shown;
    private int _generation;

    public CardPreview()
    {
        InitializeComponent();
        Panel.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => HomeMotion.PreferenceChanged += OnMotionPreferenceChanged;
        Unloaded += (_, _) =>
        {
            HomeMotion.PreferenceChanged -= OnMotionPreferenceChanged;
            Reset();
        };
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
        Panel.Visibility = Visibility.Visible;
        PreviewImage.Source = card.Poster;
        TransitionImage.Source = card.Poster;
        LogoImage.Source = null;
        LogoSlot.Visibility = Shown(ItemArtwork.Plate(card.Item) is not null);
        TitleText.Text = card.Title;
        MetaText.Text = CardPreviewText.Date(card.Item);
        MetaText.Visibility = Shown(MetaText.Text.Length > 0);
        GenresText.Text = CardPreviewText.Genres(card.Item);
        GenresText.Visibility = Shown(GenresText.Text.Length > 0);
        RatingText.Text = CardPreviewText.Rating(card.Item);
        RatingBlock.Visibility = Shown(RatingText.Text.Length > 0);
        AutomationProperties.SetName(RatingText, $"评分 {RatingText.Text}");
        FactsBlock.Visibility = Shown(MetaText.Text.Length + GenresText.Text.Length + RatingText.Text.Length > 0);
        OverviewText.Text = CardPreviewText.Overview(card.Item);
        OverviewText.Visibility = Shown(OverviewText.Text.Length > 0);
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

    internal void SetArtwork(BitmapImage? artwork)
    {
        if (artwork is null) return;
        if (_running) _pendingArtwork = artwork;
        else PreviewImage.Source = artwork;
    }

    internal void SetLogo(BitmapImage? logo) => LogoImage.Source = logo;

    private void OnMediaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PreviewImage.Width = e.NewSize.Width;
        PreviewImage.Height = e.NewSize.Height;
    }

    // 源矩形与目标同为 Popup 内的 DIP。先固定最终布局，再安装只在合成线程求值的表达式。
    internal void Prepare(Rect source, double height)
    {
        Height = height;
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
        Canvas.SetLeft(TransitionCover, media.Left);
        Canvas.SetTop(TransitionCover, media.Top);
        Morph(TransitionCover, source, media);
    }

    internal void PlayEnter()
    {
        if (_state is null) return;
        if (_shown && !_running) TransitionImage.Source = PreviewImage.Source;
        _shown = true;
        Panel.Visibility = Visibility.Visible;
        TransitionCover.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        Animate(1, EnterMs, null);
    }

    internal void PlayExit(Action closed)
    {
        if (_state is null) { Reset(); closed(); return; }
        _shown = false;
        // 收回当前正在显示的图片，避免退出第一帧突然换回竖海报。
        TransitionImage.Source = PreviewImage.Source;
        TransitionCover.Visibility = Visibility.Visible;
        PreviewImage.Visibility = Visibility.Collapsed;
        Animate(0, ExitMs, closed);
    }

    private void Animate(float target, int milliseconds, Action? closed)
    {
        var generation = ++_generation;
        _batch?.Dispose();
        _batch = null;
        _exitCompleted = closed;
        if (!HomeMotion.AnimationsEnabled)
        {
            Finish(target, closed);
            return;
        }
        _running = true;
        var compositor = _state!.Compositor;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _batch = batch;
        using var ease = compositor.CreateCubicBezierEasingFunction(Vector2.Zero, new Vector2(0, 1));
        using var animation = compositor.CreateScalarKeyFrameAnimation();
        // 省略起始关键帧，替换正在运行的动画时由合成器接住当下值。
        animation.InsertKeyFrame(1, target, ease);
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        animation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        batch.Completed += (_, _) =>
        {
            if (generation != _generation) return;
            _batch = null;
            try { Finish(target, closed); }
            finally { batch.Dispose(); }
        };
        _state.StartAnimation("Progress", animation);
        batch.End();
    }

    private void Finish(float target, Action? closed)
    {
        _state?.StopAnimation("Progress");
        _state?.InsertScalar("Progress", target);
        _running = false;
        _exitCompleted = null;
        TransitionCover.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Visible;
        Panel.Visibility = target == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (target == 1 && _pendingArtwork is { } artwork) PreviewImage.Source = artwork;
        _pendingArtwork = null;
        closed?.Invoke();
    }

    internal void Reset()
    {
        _generation++;
        _batch?.Dispose();
        _batch = null;
        _state?.StopAnimation("Progress");
        _state?.InsertScalar("Progress", 0);
        _running = _shown = false;
        _exitCompleted = null;
        _pendingArtwork = null;
        Panel.Visibility = Visibility.Collapsed;
        TransitionCover.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewImage.Source = TransitionImage.Source = LogoImage.Source = null;
    }

    private void OnMotionPreferenceChanged()
    {
        if (HomeMotion.AnimationsEnabled || !_running) return;
        var closed = _exitCompleted;
        ++_generation;
        _batch?.Dispose();
        _batch = null;
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

    private void OnPlayClicked(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);
    private void OnDetailsClicked(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke(this, EventArgs.Empty);
    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

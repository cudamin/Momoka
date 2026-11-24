using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Infrastructure;
using Momoka.Shell.ViewModels;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSubscriptionPoster : UserControl
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(nameof(Card),
        typeof(MoviePilotSubscriptionCard), typeof(MoviePilotSubscriptionPoster), new PropertyMetadata(null, OnCardChanged));
    private readonly HoverWatch _watch;

    /// <summary>
    /// 悬停预览沿用媒体卡的触发条件（2026-11-24 用户令「给主页订阅列表也加悬浮」）：指针连续静止半秒才弹，
    /// 移动重计，按下、菜单、离卡都取消；面板本身与摆位复用 <see cref="CardPreview"/> 那一套。
    /// </summary>
    private readonly PosterHoverDwell _previewDwell = new();
    private DispatcherQueueTimer? _previewDwellTimer;
    private PreviewCloseDelay? _previewClose;
    private Popup? _previewHost;
    private CardPreview? _preview;
    private HoverWatch? _previewWatch;
    private PreviewScrollWatch? _previewScrollWatch;
    private bool _previewOpen;
    private bool _previewHovered;

    private const double PreviewWidth = 400;
    private const double PreviewMinWidth = 300;
    private const double PreviewMargin = 12;

    private Storyboard? _motion;
    private MoviePilotSubscriptionCard? _observedCard;
    private MoviePilotSubscriptionCard? _requestedCard;
    private bool _live;
    private bool _hovered;
    private bool _focused;
    private bool _pressed;

    public MoviePilotSubscriptionPoster()
    {
        InitializeComponent();
        _watch = new HoverWatch(Root, SetHovered);
        Root.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        Root.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        Root.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnReleased), true);
        Root.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnReleased), true);
        Root.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        Loaded += (_, _) => { _live = true; ObserveCard(Card); RequestPoster(); };
        Unloaded += (_, _) =>
        {
            if (XamlRoot is not null) return;
            _live = false; _requestedCard = null; ObserveCard(null); ResetMotion();
            ClosePreview(immediately: true);
        };
    }

    public MoviePilotSubscriptionCard? Card
    {
        get => (MoviePilotSubscriptionCard?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }
    public event EventHandler? OpenRequested;
    public event EventHandler? MenuRequested;
    public event EventHandler? PosterRequested;

    /// <summary>预览面板打开后向宿主取那张横版背景图；宿主读不到就留着手上的海报。</summary>
    public event EventHandler? PreviewRequested;

    /// <summary>点预览面板正文：进这条订阅的详情页（不是封面本身的编辑入口）。</summary>
    public event EventHandler? DetailsRequested;

    /// <summary>点预览面板右上角「编辑订阅」：先收起面板，再交给宿主打开编辑窗口。</summary>
    public event EventHandler? EditRequested;
    internal FrameworkElement MenuAnchor => SubscriptionMore.Visibility == Visibility.Visible ? SubscriptionMore : SubscriptionCover;
    internal double Zoom => PosterMotion.ScaleX;
    internal int PointerEntries { get; private set; }
    private static void OnCardChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var poster = (MoviePilotSubscriptionPoster)sender;
        poster.ClosePreview(immediately: true);
        poster._previewDwell.Reset();
        poster.ResetMotion();
        poster.ObserveCard(poster.Card);
        poster.RequestPoster();
    }

    private void ObserveCard(MoviePilotSubscriptionCard? card)
    {
        if (ReferenceEquals(card, _observedCard)) return;
        if (_observedCard is not null) _observedCard.PropertyChanged -= OnCardUpdated;
        _observedCard = card;
        if (card is not null) card.PropertyChanged += OnCardUpdated;
    }

    private void OnCardUpdated(object? sender, PropertyChangedEventArgs args)
    {
        // Update 的统一通知发生在新订阅快照赋值之后；清图和解码完成都不触发重试。
        if (string.IsNullOrEmpty(args.PropertyName))
        {
            RequestPoster(refresh: true);
            if (_previewOpen && Card is { } current) _preview?.ShowSubscription(current);
        }
        if (_previewOpen && args.PropertyName == nameof(MoviePilotSubscriptionCard.Backdrop) && Card?.Backdrop is { } backdrop)
            _preview?.SetArtwork(backdrop);
        if (_previewOpen && args.PropertyName == nameof(MoviePilotSubscriptionCard.Poster))
            _preview?.SetSubscriptionPoster(Card?.Poster);
    }

    private void RequestPoster(bool refresh = false)
    {
        if (PosterRequested is null || Card is null || !refresh && ReferenceEquals(Card, _requestedCard)) return;
        _requestedCard = Card;
        PosterRequested.Invoke(this, EventArgs.Empty);
    }

    private void OnOpen(object sender, RoutedEventArgs args) => OpenRequested?.Invoke(this, EventArgs.Empty);
    private void OnMore(object sender, RoutedEventArgs args) => MenuRequested?.Invoke(this, EventArgs.Empty);
    private void OnContext(UIElement sender, ContextRequestedEventArgs args)
    { args.Handled = true; MenuRequested?.Invoke(this, EventArgs.Empty); }
    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        PointerEntries++;
        _watch.Enter();
        OnPointerMoved(sender, args);
    }
    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Root).Position;
        if (point.X < 0 || point.Y < 0 || point.X >= Root.ActualWidth || point.Y >= Root.ActualHeight)
        {
            _watch.Leave();
            _previewDwell.Reset();
            SchedulePreviewClose();
        }
    }
    private void OnGotFocus(object sender, RoutedEventArgs args) { _focused = true; Animate(); }
    private void OnLostFocus(object sender, RoutedEventArgs args) { _focused = false; Animate(); }
    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        _pressed = true;
        SuppressPreview();
        Animate();
    }
    private void OnReleased(object sender, PointerRoutedEventArgs args) { _pressed = false; Animate(); }
    internal void SetHovered(bool value)
    {
        _hovered = value;
        SubscriptionMore.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (!value)
        {
            _pressed = false;
            _previewDwell.Reset();
            SchedulePreviewClose();
        }
        Animate();
    }

    private void Animate()
    {
        var fromZoom = PosterMotion.ScaleX;
        var fromPress = PressMotion.ScaleX;
        var fromShade = HoverShade.Opacity;
        _motion?.Stop();
        _motion = null;
        var active = _hovered || _focused;
        var enabled = HomeMotion.AnimationsEnabled;
        var zoom = enabled && active ? 1.045 : 1;
        var press = enabled && _pressed ? 0.985 : 1;
        var shade = _hovered ? 0.78 : 0;
        // 与媒体库封面使用相同的暗罩和过渡，关闭动画后仍提供悬停反馈。
        Art.Style = (Style)Application.Current.Resources["EgFrameStyle"];
        void Settle()
        {
            PosterMotion.ScaleX = PosterMotion.ScaleY = zoom;
            PressMotion.ScaleX = PressMotion.ScaleY = press;
            HoverShade.Opacity = shade;
        }
        if (!_live || XamlRoot is null || !enabled) { Settle(); return; }
        var board = new Storyboard();
        Add(PosterMotion, "ScaleX", fromZoom, zoom); Add(PosterMotion, "ScaleY", fromZoom, zoom);
        Add(PressMotion, "ScaleX", fromPress, press); Add(PressMotion, "ScaleY", fromPress, press);
        Add(HoverShade, "Opacity", fromShade, shade);
        _motion = board;
        board.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_motion, board)) return;
            Settle(); board.Stop(); _motion = null;
        };
        board.Begin();
        void Add(DependencyObject target, string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(_pressed ? 90 : active ? 240 : 180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
            board.Children.Add(animation);
        }
    }

    /// <summary>鼠标移动才重计时；触控与触控笔不弹预览，菜单和按压期间也不开始计时。</summary>
    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_live || Card is null || _previewOpen) return;
        if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        var point = args.GetCurrentPoint(Root);
        var buttons = point.Properties;
        if (buttons.IsLeftButtonPressed || buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed)
        {
            SuppressPreview();
            return;
        }
        var position = point.Position;
        if (position.X < 0 || position.Y < 0 || position.X >= Root.ActualWidth || position.Y >= Root.ActualHeight)
        {
            _previewDwell.Cancel();
            return;
        }
        if (!_previewDwell.Observe((int)position.X, (int)position.Y, Environment.TickCount64)) return;
        StartPreviewDwellTimer();
    }

    private void StartPreviewDwellTimer()
    {
        if (_previewDwellTimer is null)
        {
            _previewDwellTimer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_previewDwellTimer is null) return;
            _previewDwellTimer.IsRepeating = false;
            _previewDwellTimer.Tick += OnPreviewDwellTick;
        }
        _previewDwellTimer.Stop();
        _previewDwellTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Max(1, _previewDwell.RemainingMilliseconds(Environment.TickCount64)));
        _previewDwellTimer.Start();
    }

    private void OnPreviewDwellTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!_previewDwell.IsPending) return;
        if (!_hovered || !_live || _pressed || AnotherPopupOpen())
        {
            _previewDwell.Cancel();
            return;
        }
        if (!_previewDwell.TryComplete(Environment.TickCount64))
        {
            StartPreviewDwellTimer();
            return;
        }
        OpenPreview();
    }

    private void SuppressPreview()
    {
        _previewDwellTimer?.Stop();
        _previewDwell.Suppress();
        ClosePreview(immediately: true);
    }

    /// <summary>
    /// 与 <see cref="PosterCard"/> 同一套摆位：面板中心压在封面中心上，靠边再夹回窗口里；Popup 独立于
    /// 正在缩放的卡片，不把祖先变换再乘一遍进偏移。
    /// </summary>
    private void OpenPreview()
    {
        if (_previewOpen || !_live || XamlRoot is null || Card is not { } card || AnotherPopupOpen()) return;

        if (_previewHost is null)
        {
            _preview = new CardPreview();
            _previewWatch = new HoverWatch(_preview, hovered =>
            {
                _previewHovered = hovered;
                if (hovered) CancelPreviewClose();
                else SchedulePreviewClose();
            });
            _preview.PointerEntered += (_, _) => _previewWatch?.Enter();
            _preview.PointerExited += OnPreviewPointerExited;
            _preview.DetailsRequested += (_, _) =>
            {
                ClosePreview(immediately: true);
                DetailsRequested?.Invoke(this, EventArgs.Empty);
            };
            _preview.EditRequested += (_, _) =>
            {
                ClosePreview(immediately: true);
                EditRequested?.Invoke(this, EventArgs.Empty);
            };

            _previewHost = new Popup
            {
                IsLightDismissEnabled = true,
                ShouldConstrainToRootBounds = true,
                Child = _preview,
                XamlRoot = XamlRoot,
            };
            _previewHost.Closed += (_, _) =>
            {
                // Closed 可以晚于同一 Popup 的重开投递，不让旧事件收掉新面板。
                if (_previewHost.IsOpen) return;
                ClosePreview(immediately: true);
            };
        }

        var content = (UIElement)XamlRoot.Content;
        var rootWidth = content is FrameworkElement page ? page.ActualWidth : 0;
        var rootHeight = content is FrameworkElement frame ? frame.ActualHeight : 0;
        var width = Math.Min(PreviewWidth, Math.Max(PreviewMinWidth, rootWidth - (PreviewMargin * 2)));

        var art = Art.TransformToVisual(content).TransformBounds(new Rect(0, 0, Art.ActualWidth, Art.ActualHeight));

        _preview!.SetSize(width);
        _preview.MinHeight = Math.Max(Root.ActualHeight, 0);
        _preview.ShowSubscription(card);

        _preview.Measure(new Size(width, double.PositiveInfinity));
        var height = _preview.DesiredSize.Height;

        _preview.IsHitTestVisible = true;
        _previewHost!.HorizontalOffset = CardPreviewPlacement.Horizontal(
            art.Left, art.Width, width, rootWidth, PreviewMargin) + art.Left;
        _previewHost.VerticalOffset = CardPreviewPlacement.Vertical(
            art.Top, art.Height, height, rootHeight, PreviewMargin) + art.Top;
        _previewHost.IsOpen = true;
        _preview.Prepare(new Rect(
            art.Left - _previewHost.HorizontalOffset,
            art.Top - _previewHost.VerticalOffset, art.Width, art.Height), height);
        _previewOpen = true;

        _preview.PlayEnter();
        WatchPreviewScroll();
        if (card.Backdrop is { } backdrop) _preview.SetArtwork(backdrop);
        else PreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreviewPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (_preview is { } preview)
        {
            var point = args.GetCurrentPoint(preview).Position;
            if (point.X >= 0 && point.Y >= 0 && point.X < preview.ActualWidth && point.Y < preview.ActualHeight) return;
        }
        _previewWatch?.Leave();
    }

    private void SchedulePreviewClose()
    {
        if (!_previewOpen) return;
        (_previewClose ??= new PreviewCloseDelay(() => ClosePreview(), () => _hovered || _previewHovered)).Schedule();
    }

    private void CancelPreviewClose() => _previewClose?.Cancel();

    private void ClosePreview(bool immediately = false)
    {
        CancelPreviewClose();
        if (!_previewOpen) return;

        _previewOpen = false;
        _previewWatch?.Leave();
        _previewHovered = false;
        UnwatchPreviewScroll();

        if (_preview is null || _previewHost is null) return;
        _preview.IsHitTestVisible = false;

        if (immediately || !HomeMotion.AnimationsEnabled)
        {
            _previewHost.IsOpen = false;
            _preview.Reset();
            return;
        }

        _preview.PlayExit(() =>
        {
            if (_previewOpen) return;
            _previewHost!.IsOpen = false;
            _preview!.Reset();
        });
    }

    /// <summary>卡片在横向货架里、货架又在页面纵向滚动里：两条都盯，内容一动就收起面板。</summary>
    private void WatchPreviewScroll()
        => (_previewScrollWatch ??= new PreviewScrollWatch(this, () => ClosePreview(immediately: true))).Watch();

    private void UnwatchPreviewScroll() => _previewScrollWatch?.Stop();

    /// <summary>别的浮层（更多菜单、对话框、筛选面板）开着时不弹；工具提示不算。</summary>
    private bool AnotherPopupOpen() => PreviewPopupGate.AnotherOpen(XamlRoot, _previewHost);

    private void ResetMotion()
    {
        _watch?.Leave();
        _hovered = _focused = _pressed = false;
        SubscriptionMore.Visibility = Visibility.Collapsed;
        _motion?.Stop(); _motion = null;
        PosterMotion.ScaleX = PosterMotion.ScaleY = PressMotion.ScaleX = PressMotion.ScaleY = 1;
        HoverShade.Opacity = 0;
        Art.Style = (Style)Application.Current.Resources["EgFrameStyle"];
    }
}

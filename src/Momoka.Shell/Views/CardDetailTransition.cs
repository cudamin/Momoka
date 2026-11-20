using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>仅连接已显示的同项海报；弱源、Frame和账号身份限定寿命，缺目标直接沿原导航呈现。</summary>
internal static class CardDetailTransition
{
    private static readonly ConditionalWeakTable<Frame, Ticket> Tickets = new();

    internal static void Navigate(Page page, PosterCard? source, Action navigate)
    {
        var frame = page.Frame;
        var prepared = source is not null && Prepare(frame, source);
        try { navigate(); }
        finally
        {
            if (prepared && ReferenceEquals(frame?.Content, page)) Cancel(frame);
        }
    }

    internal static void Release(DetailPage page)
    {
        if (page.Frame is { } frame && Tickets.TryGetValue(frame, out var ticket)
            && !ticket.IsReturning && !ticket.DepartsFrom(page)) Cancel(frame);
    }

    internal static bool Prepare(Frame? frame, PosterCard? source, TimeProvider? clock = null)
    {
        if (frame is null) return RejectPreparation(ConnectedMotionReason.TargetUnavailable);
        Cancel(frame, ConnectedMotionReason.Superseded);
        // 取消回执可以同步发起另一导航；它创建的新票据不能被外层 Prepare 覆盖或取消。
        if (Tickets.TryGetValue(frame, out _)) return RejectPreparation(ConnectedMotionReason.Superseded);
        if (!HomeMotion.AnimationsEnabled) return RejectPreparation(ConnectedMotionReason.Disabled);
        if (source?.Card is not { LoadedPosterArtwork: { } artwork } card || !VideoDetail(card.Item))
            return RejectPreparation(ConnectedMotionReason.SourceUnavailable);
        if (card.Item.Source?.IsCurrent != true) return RejectPreparation(ConnectedMotionReason.IdentityChanged);
        if (PrimaryArtwork(card.Item) != artwork) return RejectPreparation(ConnectedMotionReason.ArtworkChanged);
        if (!VisibleSource(frame, source) || source.ConnectedPoster.Source is not BitmapSource picture)
            return RejectPreparation(ConnectedMotionReason.SourceUnavailable);
        var item = card.Item;
        Action? releaseSource = null;
        ConnectedAnimation? animation = null;
        Ticket? ticket = null;
        try
        {
            releaseSource = source.RetainConnectedPoster();
            // WinUI Gallery, Microsoft (MIT), 6de91feccadfbb0a796b71caedead5a2284f730e:
            // WinUIGallery/SampleSupport/SamplePages/ItemsRepeaterCollectionPage.xaml.cs
            // -> DetailedInfoPage.xaml.cs. Keep the sample's native forward/default and return/Direct.
            // Prepare before Suppress navigation, populate the same image, then TryStart.
            animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("Momoka.CardDetail." + Guid.NewGuid().ToString("N"), source.ConnectedPosterSurface);
            var motionClock = clock ?? TimeProvider.System;
            var preparedTimestamp = motionClock.GetTimestamp();
            ticket = new Ticket(frame, item, source, artwork, picture, animation, releaseSource, motionClock, preparedTimestamp);
            Tickets.Add(frame, ticket);
            ticket.Watch();
            frame.Navigating += OnNavigating;
            frame.NavigationStopped += OnNavigationStopped;
            frame.NavigationFailed += OnNavigationFailed;
            return true;
        }
        catch (Exception)
        {
            Cancel(frame, ConnectedMotionReason.PrepareFailed);
            if (ticket is not null)
            {
                ticket.Dispose(ConnectedMotionReason.PrepareFailed);
                return false; // 已有生命周期结算，不再写第二个 PrepareFailed 阶段。
            }
            else
            {
                try { animation?.Cancel(); }
                catch (Exception) { }
                try { releaseSource?.Invoke(); }
                catch (Exception) { }
            }
            return RejectPreparation(ConnectedMotionReason.PrepareFailed);
        }
    }

    private static bool RejectPreparation(ConnectedMotionReason reason)
    {
        ConnectedMotionLifecycle.PrepareRejected("card", "forward", reason);
        return false;
    }

    private static void OnNavigating(object sender, Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs args)
    {
        if (sender is Frame frame) NavigationStarted(frame, args.Parameter, args.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back);
    }

    private static void OnNavigationStopped(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
    {
        if (sender is Frame frame) Cancel(frame);
    }

    private static void OnNavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs args)
    {
        if (sender is Frame frame) Cancel(frame);
    }

    internal static bool IsPrepared(Frame frame, object? parameter) =>
        parameter is DetailRequest request && Tickets.TryGetValue(frame, out var ticket) && !ticket.IsResolved && ticket.Matches(request.Item);

    internal static void Attach(DetailPage page, EmbyItem item)
    {
        if (page.Frame is not { } frame || !Tickets.TryGetValue(frame, out var ticket)) return;
        if (!ticket.Matches(item)) { Cancel(frame, ConnectedMotionReason.IdentityChanged); return; }
        ticket.Attach(page);
    }

    internal static bool BeginReturn(DetailPage page)
    {
        if (page.Frame is not { } frame || !Tickets.TryGetValue(frame, out var ticket)) return false;
        return ticket.BeginReturn(page);
    }

    internal static void CompleteReturn(Frame frame)
    {
        if (Tickets.TryGetValue(frame, out var ticket)) ticket.CompleteReturn();
    }

    internal static bool Returning(Frame frame) => Tickets.TryGetValue(frame, out var ticket) && ticket.IsReturning;
    internal static bool Started(Frame frame) => Tickets.TryGetValue(frame, out var ticket) && ticket.Started;
    internal static Action? Observe(Frame frame, Action<CardDetailTransitionReceipt> changed)
    {
        if (!Tickets.TryGetValue(frame, out var ticket)) return null;
        ticket.Observed += changed;
        return () => ticket.Observed -= changed;
    }

    internal static void NavigationStarted(Frame frame, object? parameter, bool goingBack)
    {
        if (!Tickets.TryGetValue(frame, out var ticket)) return;
        if (goingBack && ticket.IsReturning || parameter is DetailRequest request && ticket.Matches(request.Item) && !ticket.IsResolved) return;
        Cancel(frame);
    }

    internal static void Cancel(Frame? frame, ConnectedMotionReason reason = ConnectedMotionReason.NavigationCancelled)
    {
        if (frame is null || !Tickets.TryGetValue(frame, out var ticket)) return;
        Tickets.Remove(frame);
        frame.Navigating -= OnNavigating;
        frame.NavigationStopped -= OnNavigationStopped;
        frame.NavigationFailed -= OnNavigationFailed;
        ticket.Dispose(reason);
    }

    private static bool VideoDetail(EmbyItem item) => item.Type is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season;

    internal static ArtworkRef? PrimaryArtwork(EmbyItem item) => EmbyImageStore.TagFor(item, EmbyImageStore.Primary) is { } tag
        ? new(EmbyImageStore.ImageItemId(item, EmbyImageStore.Primary), EmbyImageStore.Primary, tag) : null;

    internal static bool VisibleImage(Image image) => image.IsLoaded && ArrangedImage(image);

    /// <summary>
    /// Repeater 回收可能把仍在屏上的海报标为未 Loaded（与 PosterCard、ImageArrivalFade 的处理一致）。
    /// 来源以当前页里的真实几何为准；同时核对所属页面与 XamlRoot，不能借离树容器残留的尺寸起跑。
    /// 仅在准备前和返回落点使用，前进导航后来源正常离树不受此约束。
    /// </summary>
    private static bool VisibleSource(Frame frame, PosterCard source)
    {
        var image = source.ConnectedPoster;
        if (frame.Content is not FrameworkElement page || frame.XamlRoot is null
            || !ReferenceEquals(image.XamlRoot, frame.XamlRoot)) return false;

        for (DependencyObject? current = image; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, page)) return ArrangedImage(image);
        return false;
    }

    /// <summary>
    /// 已经排过版、画得出画面的目标图 —— 与 <see cref="VisibleImage"/> 只差「Loaded」那一位。
    /// Gallery 的样例在目标页 OnNavigatedTo 里就 TryStart，那时目标还没 Loaded 但已经在树里；
    /// 详情页在 <see cref="DetailPage.ForceConnectedLayout"/> 里先排过一遍，位置与尺寸是真的，
    /// 于是这里读到的是真实的落点，而不是一个还没量过的 0×0。
    /// </summary>
    internal static bool ArrangedImage(Image image)
    {
        if (image.Source is not BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } || image.XamlRoot?.Content is not UIElement root
            || image.ActualWidth <= 0 || image.ActualHeight <= 0) return false;
        for (DependencyObject? current = image; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        try
        {
            var bounds = image.TransformToVisual(root).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight));
            if (root is not FrameworkElement host) return false;
            var left = Math.Max(0, bounds.Left);
            var top = Math.Max(0, bounds.Top);
            var right = Math.Min(host.ActualWidth, bounds.Right);
            var bottom = Math.Min(host.ActualHeight, bounds.Bottom);
            for (var ancestor = VisualTreeHelper.GetParent(image); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is not FrameworkElement viewport || ancestor is not (ScrollView or ScrollViewer)) continue;
                var clip = viewport.TransformToVisual(root).TransformBounds(new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
                left = Math.Max(left, clip.Left);
                top = Math.Max(top, clip.Top);
                right = Math.Min(right, clip.Right);
                bottom = Math.Min(bottom, clip.Bottom);
            }
            return right > left && bottom > top;
        }
        catch (Exception) { return false; }
    }

    private sealed class Ticket : IDisposable
    {
        private readonly Frame _frame;
        private readonly EmbyItem _item;
        private readonly string _identity;
        private readonly object _owner;
        private readonly ArtworkRef _artwork;
        private readonly WeakReference<PosterCard> _source;
        private readonly WeakReference<FrameworkElement>? _sourcePage;
        private BitmapSource? _picture;
        private Action? _releaseSource;
        private readonly UISettings _settings = new();
        private readonly TimeProvider _clock;
        private ConnectedMotionLifecycle _lifecycle;
        private ConnectedAnimation? _animation;
        private TypedEventHandler<ConnectedAnimation, object>? _completed;
        private DetailPage? _target;
        private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromMilliseconds(700) };
        private bool _watching;
        private bool _disposed;
        private bool _resolved;
        private bool _returnQueued;
        private FrameworkElement? _returnPage;
        private RoutedEventHandler? _returnPageLoaded;
        private EventHandler<object>? _returnPageLayout;
        private bool _returnRetryQueued;
        private int _generation;
        private int _timeoutGeneration;
        private EventHandler<object>? _timedOut;
        private int _preferenceGeneration;
        private Action? _preferenceChanged;
        private TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? _systemPreferenceChanged;

        internal Ticket(Frame frame, EmbyItem item, PosterCard source, ArtworkRef artwork, BitmapSource picture,
            ConnectedAnimation animation, Action releaseSource, TimeProvider clock, long preparedTimestamp)
        {
            _frame = frame;
            _item = item;
            _identity = item.IdentityKey;
            _owner = item.Source!.UserStateOwner;
            _artwork = artwork;
            _source = new(source);
            _sourcePage = frame.Content is FrameworkElement page ? new(page) : null;
            _animation = animation;
            _picture = picture;
            _releaseSource = releaseSource;
            _clock = clock;
            _lifecycle = new("card", "forward", clock, preparedTimestamp);
            _lifecycle.Prepared();
        }

        internal bool IsReturning { get; private set; }
        internal bool IsResolved => _resolved;
        internal bool Started { get; private set; }
        internal event Action<CardDetailTransitionReceipt>? Observed;
        internal bool Matches(EmbyItem item) => !_disposed && _item.Source?.IsCurrent == true
            && ReferenceEquals(_owner, item.Source?.UserStateOwner) && _identity == item.IdentityKey;

        internal void Watch()
        {
            WatchPreferences();
            _frame.Unloaded += OnFrameUnloaded;
            BeginTimeout(700, () => Resolve(false, ConnectedMotionReason.PrepareTimeout));
        }

        internal void Attach(DetailPage page)
        {
            _target = page;
            // 先订阅再抢排：强制布局这一趟若自己抬起 LayoutUpdated，抢跑不会漏掉。
            page.Loaded += OnLayout;
            page.Unloaded += OnTargetUnloaded;
            page.LayoutUpdated += OnLayoutUpdated;

            if (PrimaryArtwork(_item) == _artwork && page.ViewModel.CurrentItem is { } current
                && Matches(current) && PrimaryArtwork(current) == _artwork && _picture is { } picture)
            {
                page.StopConnectedPosterArrival();
                // 封面那一格显不显示、多大，问的是视图模型的 PageWidth；平时它要等第一次布局量到
                // Body 才写。这一句先把这个数交过去，好让下面那次抢排把目标排对。
                page.SeedConnectedLayoutWidth();
                // x:Bind更新可能晚于Loaded；此处同步供平台生成目标快照。
                if (page.ViewModel.StillImage is not BitmapSource { PixelWidth: > 0, PixelHeight: > 0 }
                    && page.ViewModel.UseConnectedPoster(_item, picture)) page.ConnectedPoster.Source = picture;
                page.ForceConnectedLayout();
            }

            // 抢在 Loaded 之前起跑：平台的补间由合成器画，这一页接下来那点构造/提交不挡它。
            // 排不出来或身份对不上时这一句什么都不做，判定仍留给下面的 TryStart 与 Loaded／LayoutUpdated。
            TryStartEarly();
            TryStart();
        }

        /// <summary>
        /// Loaded 之前抢跑一次。Gallery 的样例就是在目标页的 OnNavigatedTo 里 TryStart 的，本项目的
        /// 详情页却一直等到 Loaded 才启动 —— 点击到动画起跑之间因此空着约两百毫秒，用户读作
        /// 「点击封面后会卡一下才会触发动画」（2026-11-15）。
        /// <para>
        /// 抢跑只在「目标已经排过版、图就在手上、身份与图版本都对得上」时发出去；任何一条不满足都只是
        /// 这一趟不抢 —— 判定与回退仍留给 Loaded／LayoutUpdated，新页还在创建时不能把票据判死
        /// （2026-11-13 的 Frame 创建打断回归）。
        /// </para>
        /// </summary>
        private void TryStartEarly()
        {
            if (_disposed || _resolved || _target is not { } target || target.IsLoaded) return;
            if (!HomeMotion.AnimationsEnabled) return;
            if (_item.Source?.IsCurrent != true) return;
            if (target.ViewModel.CurrentItem is not { } current || !Matches(current)) return;
            if (PrimaryArtwork(current) != _artwork) return;
            if (!_source.TryGetTarget(out var source) || source.Card is not { } card) return;
            if (!Matches(card.Item) || PrimaryArtwork(card.Item) != _artwork) return;
            if (!ArrangedImage(target.ConnectedPoster)) return;

            target.StopConnectedPosterArrival();
            var started = false;
            try
            {
                WatchAnimationCompletion();
                _lifecycle.StartAttempt();
                started = _animation?.TryStart(target.ConnectedPosterSurface) == true;
                if (started) _lifecycle.StartSucceeded();
            }
            catch (Exception) { }
            if (started) Resolve(true, ConnectedMotionReason.None);
        }

        private void OnLayout(object sender, RoutedEventArgs args) => TryStart();

        private void OnLayoutUpdated(object? sender, object args)
        {
            // 第一次布局落定时页面往往还没 Loaded：这一拍目标已经有真实位置，可以按 Gallery 的顺序
            // 抢跑；等 Loaded 才启动会白等一百多毫秒（见 TryStartEarly）。
            if (_target is { IsLoaded: false }) TryStartEarly();
            else TryStart();
        }
        private void OnTargetUnloaded(object sender, RoutedEventArgs args) => Cancel(_frame, ConnectedMotionReason.TargetUnavailable);

        private void TryStart()
        {
            // OnNavigatedTo尚在Frame事务里；取消/普通淡入同样等Loaded，不能拆正在创建的新页。
            if (_disposed || _resolved || _target is not { IsLoaded: true } target) return;
            if (!HomeMotion.AnimationsEnabled) { Resolve(false, ConnectedMotionReason.Disabled); return; }
            if (_item.Source?.IsCurrent != true || target.ViewModel.CurrentItem is { } item && !Matches(item))
            { Resolve(false, ConnectedMotionReason.IdentityChanged); return; }
            if (target.ViewModel.CurrentItem is { } current && PrimaryArtwork(current) != _artwork)
            { Resolve(false, ConnectedMotionReason.ArtworkChanged); return; }
            if (!_source.TryGetTarget(out var source) || source.Card is not { } card)
            { Resolve(false, ConnectedMotionReason.SourceUnavailable); return; }
            if (!Matches(card.Item)) { Resolve(false, ConnectedMotionReason.IdentityChanged); return; }
            if (PrimaryArtwork(card.Item) != _artwork) { Resolve(false, ConnectedMotionReason.ArtworkChanged); return; }
            if (target.ViewModel.CurrentItem is null || !VisibleImage(target.ConnectedPoster)) return;
            target.StopConnectedPosterArrival();
            var started = false;
            try
            {
                WatchAnimationCompletion();
                _lifecycle.StartAttempt();
                started = _animation?.TryStart(target.ConnectedPosterSurface) == true;
                if (started) _lifecycle.StartSucceeded();
            }
            catch (Exception) { }
            Resolve(started, started ? ConnectedMotionReason.None : ConnectedMotionReason.StartRejected);
        }

        private void Resolve(bool started, ConnectedMotionReason reason)
        {
            if (_disposed || _resolved) return;
            _resolved = true;
            Started = started;
            DetachTargetWait();
            StopTimeout();
            if (!started) StopAnimation();
            BrowsePresentation.ResolveConnectedMotion(_frame, started);
            var resolvedGeneration = _generation;
            Report(started, completed: false, reason, terminal: !started);
            if (_disposed || resolvedGeneration != _generation) return;
            if (!started) Cancel(_frame, reason);
            else BeginTimeout(1200, () =>
            {
                var generation = _generation;
                Report(started: true, completed: false, ConnectedMotionReason.CompletionTimeout, terminal: true);
                if (_disposed || generation != _generation) return;
                StopAnimation();
                StopWatchingPreferences();
            });
        }

        internal bool BeginReturn(DetailPage page)
        {
            if (_disposed || !_resolved || IsReturning) return false;
            if (!HomeMotion.AnimationsEnabled) return RejectReturn(ConnectedMotionReason.Disabled);
            if (page.ViewModel.CurrentItem is not { } item || !Matches(item))
                return RejectReturn(ConnectedMotionReason.IdentityChanged);
            if (PrimaryArtwork(item) != _artwork) return RejectReturn(ConnectedMotionReason.ArtworkChanged);
            if (!_source.TryGetTarget(out var source) || source.Card is not { } card || _sourcePage?.TryGetTarget(out _) != true)
                return RejectReturn(ConnectedMotionReason.SourceUnavailable);
            if (!Matches(card.Item)) return RejectReturn(ConnectedMotionReason.IdentityChanged);
            if (PrimaryArtwork(card.Item) != _artwork || card.LoadedPosterArtwork is { } loaded && loaded != _artwork)
                return RejectReturn(ConnectedMotionReason.ArtworkChanged);
            if (!VisibleImage(page.ConnectedPoster)) return RejectReturn(ConnectedMotionReason.TargetUnavailable);
            var returnPrepared = false;
            try
            {
                var previousGeneration = _generation;
                Report(Started, completed: false, ConnectedMotionReason.Superseded, terminal: true);
                if (_disposed || previousGeneration != _generation) return false;
                StopAnimation();
                // 落点必须是卡片的静止几何：指针还停在刚点过的卡上时，整卡 1.045 与图片推近那一层
                // 都还在，平台会按放大态算终点；指针一动卡片缩回 1，封面就跟着缩一下。
                source.SettleForConnectedReturn();
                _animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("Momoka.CardDetail.Return." + Guid.NewGuid().ToString("N"), page.ConnectedPosterSurface);
                _lifecycle = new("card", "return", _clock, _clock.GetTimestamp());
                _lifecycle.Prepared();
                IsReturning = true;
                Started = false;
                returnPrepared = true;
                _animation.Configuration = new DirectConnectedAnimationConfiguration();
                _returnQueued = false;
                _generation++;
                WatchPreferences();
                BeginTimeout(1200, () =>
                {
                    var generation = _generation;
                    var reason = Started ? ConnectedMotionReason.CompletionTimeout : ConnectedMotionReason.PrepareTimeout;
                    Report(Started, completed: false, reason, terminal: true);
                    if (_disposed || generation != _generation) return;
                    CancelCurrent(reason);
                });
                return true;
            }
            catch (Exception)
            {
                Cancel(_frame, ConnectedMotionReason.PrepareFailed);
                return returnPrepared ? false : RejectReturn(ConnectedMotionReason.PrepareFailed);
            }
        }

        private static bool RejectReturn(ConnectedMotionReason reason)
        {
            ConnectedMotionLifecycle.PrepareRejected("card", "return", reason);
            return false;
        }

        internal void CompleteReturn()
        {
            if (!IsReturning || _returnQueued) return;
            _returnQueued = true;
            var generation = _generation;
            if (!_frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => TryStartReturn(generation)))
                Cancel(_frame, ConnectedMotionReason.TargetUnavailable);
        }

        /// <summary>
        /// 返回的落点在主页从 Frame 缓存恢复之后才成立：第一次布局、滚动位置恢复都晚于 Navigated。
        /// 前进有四道机会（Early、Loaded、LayoutUpdated、TryStart），返回从前只排一次低优先级回调就判死，
        /// 于是最常见的结局是「封面没飞，整页淡一下」。这里让返回也等目标真正排好版再起跑；
        /// 只对「源还在、只是还没上树或还没量出尺寸」这类暂时状态重试，身份、图版本、源图缺失仍是终态。
        /// </summary>
        private void TryStartReturn(int generation)
        {
            if (_disposed || generation != _generation || !IsReturning || Started) return;

            var reason = ReturnFailure();
            if (reason == ConnectedMotionReason.None)
            {
                var started = false;
                if (_source.TryGetTarget(out var source))
                {
                    try
                    {
                        WatchAnimationCompletion();
                        _lifecycle.StartAttempt();
                        started = _animation?.TryStart(source.ConnectedPosterSurface) == true;
                        if (started) _lifecycle.StartSucceeded();
                    }
                    catch (Exception) { }
                }
                else reason = ConnectedMotionReason.SourceUnavailable;

                if (started)
                {
                    Started = true;
                    BrowsePresentation.ResolveConnectedMotion(_frame, true);
                    Report(true, completed: false, ConnectedMotionReason.None, terminal: false);
                    return;
                }
                if (reason == ConnectedMotionReason.None) reason = ConnectedMotionReason.StartRejected;
            }

            if (IsReturnRetryable(reason))
            {
                WatchReturnPage(generation);
                return;
            }

            Report(false, completed: false, reason, terminal: true);
            if (!_disposed) Cancel(_frame, reason);
        }

        /// <summary>源图还在、只是目标还没上树或还没排版；源图本身没了就不该等到超时。</summary>
        private bool IsReturnRetryable(ConnectedMotionReason reason) =>
            reason == ConnectedMotionReason.SourceUnavailable
            && _source.TryGetTarget(out var source)
            && source.ConnectedPoster.Source is BitmapSource;

        /// <summary>
        /// 订阅目标页的 Loaded 与 LayoutUpdated；两者任一落定都只排一次低优先级重试。
        /// 重试失败不会报回执，也不会在这里取消——1200ms 的准备超时仍是唯一的兜底。
        /// </summary>
        private void WatchReturnPage(int generation)
        {
            if (_returnPage is not null) return;
            if (_frame.Content is not FrameworkElement page) return;

            _returnPage = page;
            _returnPageLoaded = (_, _) => QueueReturnRetry(generation);
            _returnPageLayout = (_, _) => QueueReturnRetry(generation);
            page.Loaded += _returnPageLoaded;
            page.LayoutUpdated += _returnPageLayout;
        }

        private void QueueReturnRetry(int generation)
        {
            if (_disposed || generation != _generation || !IsReturning || Started || _returnRetryQueued) return;
            _returnRetryQueued = true;
            if (!_frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _returnRetryQueued = false;
                TryStartReturn(generation);
            })) _returnRetryQueued = false;
        }

        private void DetachReturnWait()
        {
            if (_returnPage is not { } page) return;
            if (_returnPageLoaded is not null) page.Loaded -= _returnPageLoaded;
            if (_returnPageLayout is not null) page.LayoutUpdated -= _returnPageLayout;
            _returnPage = null;
            _returnPageLoaded = null;
            _returnPageLayout = null;
            _returnRetryQueued = false;
        }

        private ConnectedMotionReason ReturnFailure()
        {
            if (!HomeMotion.AnimationsEnabled) return ConnectedMotionReason.Disabled;
            if (!ReturnPageMatches) return ConnectedMotionReason.TargetUnavailable;
            if (_item.Source?.IsCurrent != true) return ConnectedMotionReason.IdentityChanged;
            if (!_source.TryGetTarget(out var source) || source.Card is not { } card) return ConnectedMotionReason.SourceUnavailable;
            if (!Matches(card.Item)) return ConnectedMotionReason.IdentityChanged;
            if (card.LoadedPosterArtwork != _artwork || PrimaryArtwork(card.Item) != _artwork) return ConnectedMotionReason.ArtworkChanged;
            return VisibleSource(_frame, source) ? ConnectedMotionReason.None : ConnectedMotionReason.SourceUnavailable;
        }

        private void BeginTimeout(int milliseconds, Action expired)
        {
            StopTimeout();
            var generation = _timeoutGeneration;
            _timeout.Interval = TimeSpan.FromMilliseconds(milliseconds);
            _timedOut = (_, _) =>
            {
                if (_disposed || generation != _timeoutGeneration) return;
                StopTimeout();
                expired();
            };
            _timeout.Tick += _timedOut;
            _timeout.Start();
        }

        private void StopTimeout()
        {
            ++_timeoutGeneration;
            _timeout.Stop();
            if (_timedOut is not null) _timeout.Tick -= _timedOut;
            _timedOut = null;
        }

        private void WatchAnimationCompletion()
        {
            // 抢跑失败后 Loaded 还会再试一次；监听只挂一份，免得同一个动画上留一串重复回调。
            if (_animation is not { } animation || _completed is not null) return;
            var generation = _generation;
            _completed = (_, _) =>
            {
                if (_disposed || generation != _generation || !ReferenceEquals(_animation, animation)) return;
                StopAnimation();
                StopTimeout();
                Report(started: true, completed: true, ConnectedMotionReason.None, terminal: true);
                if (_disposed || generation != _generation) return;
                if (IsReturning) CancelCurrent();
                else StopWatchingPreferences();
            };
            animation.Completed += _completed;
        }

        private void StopAnimation()
        {
            var releaseSource = _releaseSource;
            _releaseSource = null;
            _picture = null;
            try { releaseSource?.Invoke(); }
            catch (Exception) { /* 来源已撤销保留资格；释放失败不能阻断原生取消。 */ }
            if (_animation is not { } animation) return;
            _animation = null;
            var completed = _completed;
            _completed = null;
            try { if (completed is not null) animation.Completed -= completed; }
            catch (Exception)
            {
                // 本地动画引用已撤销，平台退订失败也必须继续Cancel及页面回退；不声称native订阅已释放。
            }
            try { animation.Cancel(); }
            catch (Exception) { /* 页面继续按静态内容呈现；取消失败不阻塞导航。 */ }
        }

        private void Report(bool started, bool completed, ConnectedMotionReason reason, bool terminal)
        {
            if (_lifecycle.Report(started, completed, reason, terminal) is not { } result || Observed is not { } observers) return;
            var receipt = new CardDetailTransitionReceipt(IsReturning ? "return" : "forward", result.Started, result.Completed, result.TimedOut,
                result.Reason, result.Terminal, result.PrepareToStartMs, result.ElapsedMs);
            foreach (Action<CardDetailTransitionReceipt> observer in observers.GetInvocationList())
                try { observer(receipt); }
                catch (Exception) { /* 可选诊断不能影响导航或原生清理。 */ }
        }

        private void DetachTargetWait()
        {
            if (_target is not { } target) return;
            target.Loaded -= OnLayout;
            target.Unloaded -= OnTargetUnloaded;
            target.LayoutUpdated -= OnLayoutUpdated;
            _target = null;
        }

        private void WatchPreferences()
        {
            if (_watching) return;
            _watching = true;
            var generation = ++_preferenceGeneration;
            void ApplyCurrentPreference()
            {
                if (_disposed || !_watching || generation != _preferenceGeneration
                    || !Tickets.TryGetValue(_frame, out var ticket) || !ReferenceEquals(ticket, this)) return;
                if (!HomeMotion.AnimationsEnabled) Cancel(_frame, ConnectedMotionReason.Disabled);
            }
            _preferenceChanged = ApplyCurrentPreference;
            _systemPreferenceChanged = (_, _) =>
            {
                if (_disposed || !_watching || generation != _preferenceGeneration) return;
                if (_frame.DispatcherQueue.HasThreadAccess) ApplyCurrentPreference();
                else _frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyCurrentPreference);
            };
            HomeMotion.PreferenceChanged += _preferenceChanged;
            _settings.AnimationsEnabledChanged += _systemPreferenceChanged;
        }

        private void StopWatchingPreferences()
        {
            if (!_watching) return;
            _watching = false;
            ++_preferenceGeneration;
            HomeMotion.PreferenceChanged -= _preferenceChanged;
            _preferenceChanged = null;
            var systemChanged = _systemPreferenceChanged;
            _systemPreferenceChanged = null;
            try { _settings.AnimationsEnabledChanged -= systemChanged; }
            catch (Exception)
            {
                // 先撤销本地回调资格；平台退订失败不阻断动画清理与普通页面回退。
                // 这里只证明发起了退订，不能证明native已释放；保留的旧委托也不能取消后来的票据。
            }
        }

        private void OnFrameUnloaded(object sender, RoutedEventArgs args) => Cancel(_frame);

        private void CancelCurrent(ConnectedMotionReason reason = ConnectedMotionReason.NavigationCancelled)
        {
            if (Tickets.TryGetValue(_frame, out var current) && ReferenceEquals(current, this)) Cancel(_frame, reason);
        }

        internal bool ReturnPageMatches => _sourcePage?.TryGetTarget(out var page) == true && ReferenceEquals(_frame.Content, page);
        internal bool DepartsFrom(DetailPage page) => !_resolved && _sourcePage?.TryGetTarget(out var source) == true && ReferenceEquals(source, page);

        public void Dispose() => Dispose(ConnectedMotionReason.NavigationCancelled);

        internal void Dispose(ConnectedMotionReason reason)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            DetachTargetWait();
            DetachReturnWait();
            StopTimeout();
            _frame.Unloaded -= OnFrameUnloaded;
            StopAnimation();
            StopWatchingPreferences();
            BrowsePresentation.ResolveConnectedMotion(_frame, Started);
            Report(Started, completed: false, reason, terminal: true);
            Observed = null;
        }
    }
}

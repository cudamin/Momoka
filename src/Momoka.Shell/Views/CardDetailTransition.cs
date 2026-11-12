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

    internal static bool Prepare(Frame? frame, PosterCard? source)
    {
        if (frame is null) return false;
        Cancel(frame);
        if (source?.Card?.Item is not { Source.IsCurrent: true } item || !VideoDetail(item)
            || !HomeMotion.AnimationsEnabled || !VisibleImage(source.ConnectedPoster)) return false;
        try
        {
            var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("Momoka.CardDetail." + Guid.NewGuid().ToString("N"), source.ConnectedPoster);
            var ticket = new Ticket(frame, item, source, animation);
            Tickets.Add(frame, ticket);
            ticket.Watch();
            frame.Navigating += OnNavigating;
            frame.NavigationStopped += OnNavigationStopped;
            frame.NavigationFailed += OnNavigationFailed;
            return true;
        }
        catch (Exception)
        {
            Cancel(frame);
            return false;
        }
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
        if (!ticket.Matches(item)) { Cancel(frame); return; }
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

    internal static void NavigationStarted(Frame frame, object? parameter, bool goingBack)
    {
        if (!Tickets.TryGetValue(frame, out var ticket)) return;
        if (goingBack && ticket.IsReturning || parameter is DetailRequest request && ticket.Matches(request.Item) && !ticket.IsResolved) return;
        Cancel(frame);
    }

    internal static void Cancel(Frame? frame)
    {
        if (frame is null || !Tickets.TryGetValue(frame, out var ticket)) return;
        Tickets.Remove(frame);
        frame.Navigating -= OnNavigating;
        frame.NavigationStopped -= OnNavigationStopped;
        frame.NavigationFailed -= OnNavigationFailed;
        ticket.Dispose();
    }

    private static bool VideoDetail(EmbyItem item) => item.Type is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season or EmbyItemType.Episode;

    private static bool VisibleImage(Image image)
    {
        if (image.Source is not BitmapSource { PixelWidth: > 0, PixelHeight: > 0 } || image.XamlRoot?.Content is not UIElement root || !image.IsLoaded
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
        private readonly WeakReference<PosterCard> _source;
        private readonly WeakReference<FrameworkElement>? _sourcePage;
        private readonly UISettings _settings = new();
        private ConnectedAnimation? _animation;
        private TypedEventHandler<ConnectedAnimation, object>? _completed;
        private DetailPage? _target;
        private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromMilliseconds(700) };
        private bool _watching;
        private bool _disposed;
        private bool _resolved;
        private bool _returnQueued;
        private int _generation;
        private int _timeoutGeneration;
        private EventHandler<object>? _timedOut;
        private int _preferenceGeneration;
        private Action? _preferenceChanged;
        private TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? _systemPreferenceChanged;

        internal Ticket(Frame frame, EmbyItem item, PosterCard source, ConnectedAnimation animation)
        {
            _frame = frame;
            _item = item;
            _identity = item.IdentityKey;
            _owner = item.Source!.UserStateOwner;
            _source = new(source);
            _sourcePage = frame.Content is FrameworkElement page ? new(page) : null;
            _animation = animation;
        }

        internal bool IsReturning { get; private set; }
        internal bool IsResolved => _resolved;
        internal bool Started { get; private set; }
        internal bool Matches(EmbyItem item) => !_disposed && _item.Source?.IsCurrent == true
            && ReferenceEquals(_owner, item.Source?.UserStateOwner) && _identity == item.IdentityKey;

        internal void Watch()
        {
            WatchPreferences();
            _frame.Unloaded += OnFrameUnloaded;
            BeginTimeout(700, () => Resolve(false));
        }

        internal void Attach(DetailPage page)
        {
            _target = page;
            page.Loaded += OnLayout;
            page.Unloaded += OnTargetUnloaded;
            page.LayoutUpdated += OnLayoutUpdated;
            TryStart();
        }

        private void OnLayout(object sender, RoutedEventArgs args) => TryStart();
        private void OnLayoutUpdated(object? sender, object args) => TryStart();
        private void OnTargetUnloaded(object sender, RoutedEventArgs args) => Cancel(_frame);

        private void TryStart()
        {
            if (_disposed || _resolved || _target is not { } target) return;
            if (_item.Source?.IsCurrent != true || !HomeMotion.AnimationsEnabled || target.ViewModel.CurrentItem is { } item && !Matches(item))
            { Resolve(false); return; }
            if (target.ViewModel.CurrentItem is null || !VisibleImage(target.ConnectedPoster)) return;
            target.StopConnectedPosterArrival();
            var started = false;
            try
            {
                WatchAnimationCompletion();
                started = _animation?.TryStart(target.ConnectedPoster) == true;
            }
            catch (Exception) { }
            Resolve(started);
        }

        private void Resolve(bool started)
        {
            if (_disposed || _resolved) return;
            _resolved = true;
            Started = started;
            DetachTargetWait();
            StopTimeout();
            if (!started) StopAnimation();
            BrowsePresentation.ResolveConnectedMotion(_frame, started);
            if (!started) Cancel(_frame);
            else BeginTimeout(1200, () =>
            {
                StopAnimation();
                StopWatchingPreferences();
            });
        }

        internal bool BeginReturn(DetailPage page)
        {
            if (_disposed || !_resolved || !HomeMotion.AnimationsEnabled || page.ViewModel.CurrentItem is not { } item
                || !Matches(item) || !_source.TryGetTarget(out var source) || source.Card?.Item.IdentityKey != _identity
                || _sourcePage?.TryGetTarget(out _) != true || !VisibleImage(page.ConnectedPoster)) return false;
            try
            {
                StopAnimation();
                _animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("Momoka.CardDetail.Return." + Guid.NewGuid().ToString("N"), page.ConnectedPoster);
                IsReturning = true;
                _returnQueued = false;
                _generation++;
                WatchPreferences();
                BeginTimeout(1200, () => Cancel(_frame));
                return true;
            }
            catch (Exception) { Cancel(_frame); return false; }
        }

        internal void CompleteReturn()
        {
            if (!IsReturning || _returnQueued) return;
            _returnQueued = true;
            var generation = _generation;
            if (!_frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_disposed || generation != _generation) return;
                var started = false;
                if (ReturnPageMatches && HomeMotion.AnimationsEnabled && _item.Source?.IsCurrent == true && _source.TryGetTarget(out var source)
                    && source.Card?.Item.IdentityKey == _identity && VisibleImage(source.ConnectedPoster))
                {
                    try
                    {
                        WatchAnimationCompletion();
                        started = _animation?.TryStart(source.ConnectedPoster) == true;
                    }
                    catch (Exception) { }
                }
                BrowsePresentation.ResolveConnectedMotion(_frame, started);
                if (!started) Cancel(_frame);
            })) Cancel(_frame);
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
            if (_animation is not { } animation) return;
            var generation = _generation;
            _completed = (_, _) =>
            {
                if (_disposed || generation != _generation || !ReferenceEquals(_animation, animation)) return;
                StopAnimation();
                StopTimeout();
                if (IsReturning) Cancel(_frame);
                else StopWatchingPreferences();
            };
            animation.Completed += _completed;
        }

        private void StopAnimation()
        {
            if (_animation is not { } animation) return;
            _animation = null;
            if (_completed is not null) animation.Completed -= _completed;
            _completed = null;
            try { animation.Cancel(); }
            catch (Exception) { /* 页面继续按静态内容呈现；取消失败不阻塞导航。 */ }
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
                if (!HomeMotion.AnimationsEnabled) Cancel(_frame);
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

        internal bool ReturnPageMatches => _sourcePage?.TryGetTarget(out var page) == true && ReferenceEquals(_frame.Content, page);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            DetachTargetWait();
            StopTimeout();
            _frame.Unloaded -= OnFrameUnloaded;
            StopAnimation();
            StopWatchingPreferences();
            BrowsePresentation.ResolveConnectedMotion(_frame, started: false);
        }
    }
}

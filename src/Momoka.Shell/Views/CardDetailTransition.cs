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
            return root is FrameworkElement host && bounds.Right > 0 && bounds.Bottom > 0 && bounds.Left < host.ActualWidth && bounds.Top < host.ActualHeight;
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
        private int _generation;

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
            _timeout.Tick += OnTimeout;
            _timeout.Start();
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
            _timeout.Stop();
            if (!started) StopAnimation();
            BrowsePresentation.ResolveConnectedMotion(_frame, started);
            if (!started) Cancel(_frame);
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
                _generation++;
                WatchPreferences();
                return true;
            }
            catch (Exception) { Cancel(_frame); return false; }
        }

        internal void CompleteReturn()
        {
            if (!IsReturning) return;
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

        private void ApplyPreference()
        {
            if (!HomeMotion.AnimationsEnabled) Cancel(_frame);
        }
        private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        {
            if (_frame.DispatcherQueue.HasThreadAccess) ApplyPreference();
            else _frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyPreference);
        }
        private void OnTimeout(object? sender, object args) => Resolve(false);

        private void WatchAnimationCompletion()
        {
            if (_animation is not { } animation) return;
            var generation = _generation;
            _completed = (_, _) =>
            {
                if (_disposed || generation != _generation || !ReferenceEquals(_animation, animation)) return;
                StopAnimation();
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
            HomeMotion.PreferenceChanged += ApplyPreference;
            _settings.AnimationsEnabledChanged += OnSystemPreferenceChanged;
        }

        private void StopWatchingPreferences()
        {
            if (!_watching) return;
            _watching = false;
            HomeMotion.PreferenceChanged -= ApplyPreference;
            _settings.AnimationsEnabledChanged -= OnSystemPreferenceChanged;
        }

        private void OnFrameUnloaded(object sender, RoutedEventArgs args) => Cancel(_frame);

        internal bool ReturnPageMatches => _sourcePage?.TryGetTarget(out var page) == true && ReferenceEquals(_frame.Content, page);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            DetachTargetWait();
            _timeout.Stop();
            _timeout.Tick -= OnTimeout;
            _frame.Unloaded -= OnFrameUnloaded;
            StopAnimation();
            StopWatchingPreferences();
            BrowsePresentation.ResolveConnectedMotion(_frame, started: false);
        }
    }
}

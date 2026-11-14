using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>
/// 演职人员卡 → 人物页的关联动画。只做前进：详情页没有缓存，返回时人物页和演职人员排都已重建，
/// 没有可靠来源；返回仍走普通导航与页面淡入。源图在准备时保留，目标照片未到时先交接这张图；
/// 失败、超时、关动画或身份变化都回普通呈现。
/// </summary>
internal static class PersonTransition
{
    private static readonly ConditionalWeakTable<Frame, Ticket> Tickets = new();

    internal static void Navigate(Page page, PosterCard? source, Action navigate)
    {
        if (source?.Card?.Item.Type != EmbyItemType.Person)
        {
            navigate();
            return;
        }

        var frame = page.Frame;
        var prepared = Prepare(frame, source);
        try { navigate(); }
        finally
        {
            if (prepared && ReferenceEquals(frame?.Content, page)) Cancel(frame);
        }
    }

    internal static bool Prepare(Frame? frame, PosterCard? source, TimeProvider? clock = null)
    {
        if (frame is null) return RejectPreparation(ConnectedMotionReason.TargetUnavailable);
        Cancel(frame, ConnectedMotionReason.Superseded);
        if (!HomeMotion.AnimationsEnabled) return RejectPreparation(ConnectedMotionReason.Disabled);
        if (source?.Card is not { LoadedPosterArtwork: { } artwork } card || card.Item.Type != EmbyItemType.Person)
            return RejectPreparation(ConnectedMotionReason.SourceUnavailable);
        if (card.Item.Source?.IsCurrent != true) return RejectPreparation(ConnectedMotionReason.IdentityChanged);
        if (CardDetailTransition.PrimaryArtwork(card.Item) != artwork) return RejectPreparation(ConnectedMotionReason.ArtworkChanged);
        if (!CardDetailTransition.VisibleImage(source.ConnectedPoster) || source.ConnectedPoster.Source is not BitmapSource picture)
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
            // -> DetailedInfoPage.xaml.cs. Prepare before Suppress navigation, populate the same image,
            // then TryStart; forward uses the sample's default Gravity configuration.
            animation = ConnectedAnimationService.GetForCurrentView()
                .PrepareToAnimate("Momoka.Person." + Guid.NewGuid().ToString("N"), source.ConnectedPoster);
            var motionClock = clock ?? TimeProvider.System;
            var preparedTimestamp = motionClock.GetTimestamp();
            ticket = new Ticket(frame, item, artwork, picture, animation, releaseSource, motionClock, preparedTimestamp);
            Tickets.Add(frame, ticket);
            ticket.Watch();
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
        ConnectedMotionLifecycle.PrepareRejected("person", "forward", reason);
        return false;
    }

    internal static bool IsPrepared(Frame frame, EmbyItem? person) =>
        person is not null && Tickets.TryGetValue(frame, out var ticket)
        && !ticket.IsResolved && ticket.Matches(person) && CardDetailTransition.PrimaryArtwork(person) == ticket.Artwork;

    internal static void Attach(Page page, EmbyItem person, Image target, Func<BitmapSource, bool>? adopt = null)
    {
        if (page.Frame is not { } frame || !Tickets.TryGetValue(frame, out var ticket)) return;
        if (!ticket.Matches(person)) { Cancel(frame, ConnectedMotionReason.IdentityChanged); return; }
        if (CardDetailTransition.PrimaryArtwork(person) != ticket.Artwork) { Cancel(frame, ConnectedMotionReason.ArtworkChanged); return; }
        ticket.Attach(page, target, adopt);
    }

    internal static void Release(Page page)
    {
        if (page.Frame is { } frame) Cancel(frame);
    }

    internal static bool Started(Frame frame) => Tickets.TryGetValue(frame, out var ticket) && ticket.Started;

    internal static Action? Observe(Frame frame, Action<PersonTransitionReceipt> changed)
    {
        if (!Tickets.TryGetValue(frame, out var ticket)) return null;
        ticket.Observed += changed;
        return () => ticket.Observed -= changed;
    }

    private static void OnNavigationStopped(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
    {
        if (sender is Frame frame) Cancel(frame);
    }

    private static void OnNavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs args)
    {
        if (sender is Frame frame) Cancel(frame);
    }

    internal static void Cancel(Frame? frame, ConnectedMotionReason reason = ConnectedMotionReason.NavigationCancelled)
    {
        if (frame is null || !Tickets.TryGetValue(frame, out var ticket)) return;
        Tickets.Remove(frame);
        frame.NavigationStopped -= OnNavigationStopped;
        frame.NavigationFailed -= OnNavigationFailed;
        ticket.Dispose(reason);
    }

    private sealed class Ticket : IDisposable
    {
        private readonly Frame _frame;
        private readonly EmbyItem _item;
        private readonly string _identity;
        private readonly object _owner;
        private readonly ArtworkRef _artwork;
        private BitmapSource? _picture;
        private Action? _releaseSource;
        private readonly UISettings _settings = new();
        private readonly ConnectedMotionLifecycle _lifecycle;
        private ConnectedAnimation? _animation;
        private TypedEventHandler<ConnectedAnimation, object>? _completed;
        private Page? _targetPage;
        private Image? _targetImage;
        private Func<BitmapSource, bool>? _adopt;
        private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromMilliseconds(700) };
        private bool _watching;
        private bool _disposed;
        private bool _resolved;
        private int _generation;
        private int _timeoutGeneration;
        private EventHandler<object>? _timedOut;
        private int _preferenceGeneration;
        private Action? _preferenceChanged;
        private TypedEventHandler<UISettings, UISettingsAnimationsEnabledChangedEventArgs>? _systemPreferenceChanged;

        internal Ticket(Frame frame, EmbyItem item, ArtworkRef artwork, BitmapSource picture,
            ConnectedAnimation animation, Action releaseSource, TimeProvider clock, long preparedTimestamp)
        {
            _frame = frame;
            _item = item;
            _identity = item.IdentityKey;
            _owner = item.Source!.UserStateOwner;
            _artwork = artwork;
            _animation = animation;
            _picture = picture;
            _releaseSource = releaseSource;
            _lifecycle = new("person", "forward", clock, preparedTimestamp);
            _lifecycle.Prepared();
        }

        internal bool IsResolved => _resolved;
        internal ArtworkRef Artwork => _artwork;
        internal bool Started { get; private set; }
        internal event Action<PersonTransitionReceipt>? Observed;

        internal bool Matches(EmbyItem item) => !_disposed && _item.Source?.IsCurrent == true
            && ReferenceEquals(_owner, item.Source?.UserStateOwner) && _identity == item.IdentityKey;

        internal void Watch()
        {
            WatchPreferences();
            _frame.Unloaded += OnFrameUnloaded;
            BeginTimeout(700, () => Resolve(false, ConnectedMotionReason.PrepareTimeout));
        }

        internal void Attach(Page page, Image target, Func<BitmapSource, bool>? adopt)
        {
            _targetPage = page;
            _targetImage = target;
            _adopt = adopt;
            // x:Bind 更新可能晚于 Attach；先把来源图交出去，目标快照才有东西可抓。
            if (_picture is { } picture && target.Source is null)
            {
                _adopt?.Invoke(picture);
                if (target.Source is null) target.Source = picture;
            }
            page.Loaded += OnLayout;
            page.Unloaded += OnTargetUnloaded;
            page.LayoutUpdated += OnLayoutUpdated;
            TryStart();
        }

        private void OnLayout(object sender, RoutedEventArgs args) => TryStart();
        private void OnLayoutUpdated(object? sender, object args) => TryStart();
        private void OnTargetUnloaded(object sender, RoutedEventArgs args) => Cancel(_frame, ConnectedMotionReason.TargetUnavailable);

        private void TryStart()
        {
            if (_disposed || _resolved || _targetPage is not { IsLoaded: true } page || _targetImage is not { } target) return;
            if (!HomeMotion.AnimationsEnabled) { Resolve(false, ConnectedMotionReason.Disabled); return; }
            if (!ReferenceEquals(page.Frame, _frame) || !CardDetailTransition.VisibleImage(target))
            {
                Resolve(false, ConnectedMotionReason.TargetUnavailable);
                return;
            }
            if (_item.Source?.IsCurrent != true) { Resolve(false, ConnectedMotionReason.IdentityChanged); return; }
            if (CardDetailTransition.PrimaryArtwork(_item) != _artwork) { Resolve(false, ConnectedMotionReason.ArtworkChanged); return; }
            var started = false;
            try
            {
                WatchAnimationCompletion();
                _lifecycle.StartAttempt();
                started = _animation?.TryStart(target) == true;
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
            Report(started, completed: false, reason, terminal: !started);
            if (_disposed) return;
            if (!started) { Cancel(_frame, reason); return; }
            BeginTimeout(1200, () =>
            {
                Cancel(_frame, ConnectedMotionReason.CompletionTimeout);
            });
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
                StopTimeout();
                StopWatchingPreferences();
                Report(started: true, completed: true, ConnectedMotionReason.None, terminal: true);
                CancelCurrent();
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
                // 本地动画引用已撤销；平台退订失败也必须继续取消并回普通呈现。
            }
            try { animation.Cancel(); }
            catch (Exception) { /* 页面继续按静态内容呈现；取消失败不阻塞导航。 */ }
        }

        private void Report(bool started, bool completed, ConnectedMotionReason reason, bool terminal)
        {
            if (_lifecycle.Report(started, completed, reason, terminal) is not { } result || Observed is not { } observers) return;
            foreach (Action<PersonTransitionReceipt> observer in observers.GetInvocationList())
                try { observer(new(result.Started, result.Completed, result.TimedOut, result.Reason,
                    result.Terminal, result.PrepareToStartMs, result.ElapsedMs)); }
                catch (Exception) { /* 可选诊断不能影响导航或原生清理。 */ }
        }

        private void DetachTargetWait()
        {
            if (_targetPage is not { } target) return;
            target.Loaded -= OnLayout;
            target.Unloaded -= OnTargetUnloaded;
            target.LayoutUpdated -= OnLayoutUpdated;
            _targetPage = null;
            _targetImage = null;
            _adopt = null;
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
                // 先撤销本地回调资格；平台退订失败不阻断动画清理与普通页面呈现。
            }
        }

        private void OnFrameUnloaded(object sender, RoutedEventArgs args) => Cancel(_frame);

        private void CancelCurrent(ConnectedMotionReason reason = ConnectedMotionReason.NavigationCancelled)
        {
            if (Tickets.TryGetValue(_frame, out var current) && ReferenceEquals(current, this)) Cancel(_frame, reason);
        }

        public void Dispose() => Dispose(ConnectedMotionReason.NavigationCancelled);

        internal void Dispose(ConnectedMotionReason reason)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            DetachTargetWait();
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

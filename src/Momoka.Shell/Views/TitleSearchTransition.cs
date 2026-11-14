using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Momoka.Shell.Views;

/// <summary>
/// 侧边栏模式下点标题栏正中那颗搜索框，框顺着关联动画移进搜索页内容区那只输入框（2026-11-14 用户令
/// 「新增动画，点击标题的搜索栏后自动移到内容区」）。搜索页开着的时候标题框收起，离开这一页再出现 ——
/// 屏上始终只有一只搜索框，它就是移下来的那一只。
/// <para>
/// 用平台自己的 <see cref="ConnectedAnimationService"/>，和海报进详情页那条是同一套机制：源元素由窗口服务
/// 暂时藏起、目标落定后 <c>TryStart</c> 接上，移动补间由系统画。这里只补三件窗口服务不管的事 ——
/// 哪一帧该接、接不上时把源元素还回去、动画偏好关掉或目标迟迟不来时放弃。放弃之后页面照常呈现
/// （<see cref="LibraryPage"/> 退回原来的淡入）。
/// </para>
/// <para>
/// 票据按 Frame 记账：一次点击只可能对应一次导航；再次点击、导航失败、帧离树都会把上一张票收掉。
/// </para>
/// </summary>
internal static class TitleSearchTransition
{
    private static readonly ConditionalWeakTable<Frame, Ticket> Tickets = new();

    /// <summary>自检与探针读的最后一趟结果（prepared／started／skipped／canceled）。</summary>
    internal static string LastOutcome { get; private set; } = "idle";

    internal static void ResetOutcome() => LastOutcome = "idle";

    /// <summary>在导航之前调用。返回 false 表示这次不播动画，调用方照常导航即可。</summary>
    internal static bool Prepare(Frame? frame, UIElement? source)
    {
        if (frame is null || source is null) return false;
        Cancel(frame);
        if (!HomeMotion.AnimationsEnabled || source.XamlRoot is null
            || source is FrameworkElement { ActualWidth: <= 0 } or FrameworkElement { ActualHeight: <= 0 }) return false;
        try
        {
            var animation = ConnectedAnimationService.GetForCurrentView()
                .PrepareToAnimate("Momoka.TitleSearch." + Guid.NewGuid().ToString("N"), source);
            // Direct 是直线补间：这一趟要读成「这一条从上面移到下面」，默认的 Gravity 会先往上提一下。
            animation.Configuration = new DirectConnectedAnimationConfiguration();
            var ticket = new Ticket(frame, animation);
            Tickets.Add(frame, ticket);
            LastOutcome = "prepared";
            return true;
        }
        catch (Exception)
        {
            Cancel(frame);
            return false;
        }
    }

    /// <summary>
    /// 搜索页落定后把输入框交给待启动的动画。没有待启动的票时返回 false（普通导航，页面自己决定入场动画）；
    /// 有票时由这里在布局落定之后接上，并在接不上时回调 <paramref name="resolved"/> 且传 false。
    /// </summary>
    internal static bool Attach(Frame? frame, FrameworkElement target, Action<bool> resolved)
    {
        if (frame is null || !Tickets.TryGetValue(frame, out var ticket)) return false;
        ticket.Attach(target, resolved);
        return true;
    }

    internal static void Cancel(Frame? frame)
    {
        if (frame is null || !Tickets.TryGetValue(frame, out var ticket)) return;
        ticket.Resolve(started: false);
    }

    private static void Report(string outcome) => LastOutcome = outcome;

    private sealed class Ticket
    {
        private readonly Frame _frame;
        private ConnectedAnimation? _animation;
        private FrameworkElement? _target;
        private Action<bool>? _resolved;
        private DispatcherQueueTimer? _timeout;
        private bool _done;

        internal Ticket(Frame frame, ConnectedAnimation animation)
        {
            _frame = frame;
            _animation = animation;
            frame.NavigationStopped += OnNavigationStopped;
            frame.NavigationFailed += OnNavigationFailed;
            frame.Unloaded += OnFrameUnloaded;
            _timeout = frame.DispatcherQueue.CreateTimer();
            _timeout.Interval = TimeSpan.FromMilliseconds(900);
            _timeout.IsRepeating = false;
            _timeout.Tick += OnTimeout;
            _timeout.Start();
        }

        internal void Attach(FrameworkElement target, Action<bool> resolved)
        {
            if (_done) { resolved(false); return; }
            _target = target;
            _resolved = resolved;
            target.LayoutUpdated += OnLayoutUpdated;
            TryStart();
        }

        private void OnLayoutUpdated(object? sender, object args) => TryStart();

        private void TryStart()
        {
            if (_done || _target is not { } target) return;
            if (target.XamlRoot is null || !target.IsLoaded || target.ActualWidth <= 0 || target.ActualHeight <= 0) return;
            var started = false;
            try { started = _animation?.TryStart(target) == true; }
            catch (Exception) { }
            Resolve(started);
        }

        private void OnNavigationStopped(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) => Resolve(false);
        private void OnNavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs args) => Resolve(false);
        private void OnFrameUnloaded(object sender, RoutedEventArgs args) => Resolve(false);
        private void OnTimeout(object? sender, object args) => Resolve(false);

        internal void Resolve(bool started)
        {
            if (_done) return;
            _done = true;
            _timeout?.Stop();
            if (_timeout is not null) _timeout.Tick -= OnTimeout;
            _timeout = null;
            _frame.NavigationStopped -= OnNavigationStopped;
            _frame.NavigationFailed -= OnNavigationFailed;
            _frame.Unloaded -= OnFrameUnloaded;
            if (_target is { } target) target.LayoutUpdated -= OnLayoutUpdated;
            _target = null;
            if (!started) _animation?.Cancel();
            _animation = null;
            Tickets.Remove(_frame);
            var resolved = _resolved;
            _resolved = null;
            Report(started ? "started" : "skipped");
            resolved?.Invoke(started);
        }
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

/// <summary>
/// 悬停预览里那枚徽标和片名移进详情页（2026-11-16 用户令「鼠标悬浮呼出悬停页面之后，再点击下方的
/// 缩略图进入详情页时，新增徽标和片名移进详情页的动画」）。
/// <para>
/// 面板在点击那一拍就被收掉，源元素活不到详情页画出来的时候，所以这里不交元素、只交两样东西：它们在
/// 窗口内容里的最后矩形，以及徽标那张已经解好的图。详情页拿自己的标题与徽标从这两个矩形长回去（见
/// <see cref="DetailPage.BeginPreviewHeadingMotion"/>），屏上读作同一件东西移了过去。
/// </para>
/// <para>
/// 票据按 Frame 记账：一次点击只对应一次详情导航；身份对不上、导航失败、目标超时或来源页先走掉都作废，
/// 详情页照常呈现，绝不为了这段动效推迟导航。
/// </para>
/// </summary>
internal static class PreviewHeadingTransition
{
    private static readonly ConditionalWeakTable<Frame, Ticket> Tickets = new();

    /// <summary>导航之前调用：<paramref name="source"/> 上没有悬停交接时收掉上一张票，照常导航。</summary>
    internal static void Prepare(Frame? frame, PosterCard? source)
    {
        if (frame is null) return;
        Cancel(frame);
        if (source?.TakePreviewHeading() is not { } capture) return;
        if (!HomeMotion.AnimationsEnabled || !capture.Matches(capture.Item)) return;
        var ticket = new Ticket(frame, capture);
        Tickets.Add(frame, ticket);
        ticket.Watch();
    }

    /// <summary>详情页接入时登记目标；新页尚未挂树时保留原票，等Loaded／布局后起跑。</summary>
    internal static bool Attach(DetailPage page, EmbyItem item)
    {
        if (page.Frame is not { } frame || !Tickets.TryGetValue(frame, out var ticket)) return false;
        if (!ticket.Capture.Matches(item)) { Cancel(frame); return false; }
        return ticket.Attach(page);
    }

    /// <summary>只取消这张目标页持有的等待，不让来源页离开时误删刚为下一页准备的票。</summary>
    internal static void Release(DetailPage page)
    {
        if (page.Frame is { } frame && Tickets.TryGetValue(frame, out var ticket) && ticket.Targets(page))
            Cancel(frame);
    }

    internal static void Cancel(Frame? frame)
    {
        if (frame is null || !Tickets.TryGetValue(frame, out var ticket)) return;
        Tickets.Remove(frame);
        ticket.Cancel();
    }

    private sealed class Ticket
    {
        private readonly Frame _frame;
        private readonly DispatcherQueueTimer _timeout;
        private readonly UISettings _settings = new();
        private DetailPage? _target;
        private bool _starting;
        private bool _started;
        private bool _done;

        internal Ticket(Frame frame, PreviewHeadingCapture capture)
        {
            _frame = frame;
            Capture = capture;
            _timeout = frame.DispatcherQueue.CreateTimer();
            _timeout.Interval = TimeSpan.FromMilliseconds(700);
            _timeout.IsRepeating = false;
        }

        internal PreviewHeadingCapture Capture { get; }
        internal bool Targets(DetailPage page) => ReferenceEquals(_target, page);

        internal void Watch()
        {
            _frame.Navigating += OnNavigating;
            _frame.NavigationStopped += OnNavigationStopped;
            _frame.NavigationFailed += OnNavigationFailed;
            _frame.Unloaded += OnFrameUnloaded;
            HomeMotion.PreferenceChanged += OnPreferenceChanged;
            try { _settings.AnimationsEnabledChanged += OnSystemPreferenceChanged; }
            catch (Exception) { /* 订阅失败仍在起跑前读取当前偏好；有限等待仍由原超时收尾。 */ }
            _timeout.Tick += OnTimeout;
            _timeout.Start();
        }

        internal bool Attach(DetailPage page)
        {
            if (_done) return _started;
            if (_target is not null)
            {
                if (!Targets(page)) { CancelCurrent(); return false; }
                TryStart();
                return _started || !_done;
            }

            _target = page;
            // WinUI Gallery, Microsoft (MIT), 6de91feccadfbb0a796b71caedead5a2284f730e:
            // SampleSupport/SamplePages/ItemsRepeaterCollectionPage.xaml.cs -> DetailedInfoPage.xaml.cs
            // 保留先填目标再起跑的顺序。样例没有已收起Popup的矩形交接；沿用本项目关联封面的
            // Loaded／LayoutUpdated等待，先订再抢排，不把OnNavigatedTo里的空XamlRoot判成终态。
            page.Loaded += OnTargetLoaded;
            page.LayoutUpdated += OnTargetLayoutUpdated;
            page.Unloaded += OnTargetUnloaded;
            page.ViewModel.PropertyChanged += OnTargetChanged;
            TryStart();
            return _started || !_done;
        }

        private void TryStart()
        {
            if (_done || _starting || _target is not { } target) return;
            if (!HomeMotion.AnimationsEnabled || !Capture.Matches(Capture.Item))
            { CancelCurrent(); return; }
            if (target.ViewModel.CurrentItem is not { } current) return;
            if (!Capture.Matches(current)) { CancelCurrent(); return; }
            // 新页的Frame事务先Attach、后入树；只有本Frame当前页已挂树才调用真正的布局与Storyboard。
            if (!target.IsLoaded || target.XamlRoot is null || !ReferenceEquals(target.Frame, _frame)
                || !ReferenceEquals(_frame.Content, target)) return;

            _starting = true;
            try { _started = target.BeginPreviewHeadingMotion(Capture); }
            catch (Exception) { CancelCurrent(); }
            finally { _starting = false; }
            if (_done) return;
            // 强制布局可以同步通知换项或取消；重入被_starting挡住后在这里再核归属，不能留下旧等待。
            if (!HomeMotion.AnimationsEnabled || target.ViewModel.CurrentItem is not { } arranged || !Capture.Matches(arranged))
            { CancelCurrent(); return; }
            if (!_started) return;
            if (Tickets.TryGetValue(_frame, out var currentTicket) && ReferenceEquals(currentTicket, this)) Tickets.Remove(_frame);
            Detach();
        }

        internal void Cancel()
        {
            var target = _target;
            Detach();
            target?.StopPreviewHeadingMotion();
        }

        private void CancelCurrent()
        {
            if (!_done && Tickets.TryGetValue(_frame, out var ticket) && ReferenceEquals(ticket, this)) PreviewHeadingTransition.Cancel(_frame);
        }

        private void Detach()
        {
            if (_done) return;
            _done = true;
            _timeout.Stop();
            _timeout.Tick -= OnTimeout;
            _frame.Navigating -= OnNavigating;
            _frame.NavigationStopped -= OnNavigationStopped;
            _frame.NavigationFailed -= OnNavigationFailed;
            _frame.Unloaded -= OnFrameUnloaded;
            HomeMotion.PreferenceChanged -= OnPreferenceChanged;
            try { _settings.AnimationsEnabledChanged -= OnSystemPreferenceChanged; }
            catch (Exception) { /* 本地资格已撤，平台退订异常不挡住其余清理。 */ }
            if (_target is { } target)
            {
                target.Loaded -= OnTargetLoaded;
                target.LayoutUpdated -= OnTargetLayoutUpdated;
                target.Unloaded -= OnTargetUnloaded;
                target.ViewModel.PropertyChanged -= OnTargetChanged;
                _target = null;
            }
        }

        private void OnNavigating(object sender, Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs args)
        {
            // 这一趟若不是去这张票等的那一项，票据就作废；详情页的身份核对仍会再问一次。
            if (_target is null && args.Parameter is DetailRequest request && Capture.Matches(request.Item)) return;
            CancelCurrent();
        }

        private void OnTargetLoaded(object sender, RoutedEventArgs args) => TryStart();
        private void OnTargetLayoutUpdated(object? sender, object args) => TryStart();
        private void OnTargetChanged(object? sender, PropertyChangedEventArgs args) => TryStart();
        private void OnTargetUnloaded(object sender, RoutedEventArgs args)
        {
            if (_target?.XamlRoot is null) CancelCurrent();
        }
        private void OnNavigationStopped(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) => CancelCurrent();
        private void OnNavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs args) => CancelCurrent();
        private void OnFrameUnloaded(object sender, RoutedEventArgs args)
        {
            if (_frame.XamlRoot is null) CancelCurrent();
        }
        private void OnTimeout(object? sender, object args) => CancelCurrent();
        private void OnPreferenceChanged()
        {
            if (!HomeMotion.AnimationsEnabled) CancelCurrent();
        }
        private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        {
            if (_frame.DispatcherQueue.HasThreadAccess) OnPreferenceChanged();
            else _frame.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, OnPreferenceChanged);
        }
    }
}

/// <summary>
/// 悬停面板收起来之前量到的交接：片名与徽标在窗口内容里的矩形（Popup 偏移加面板内位置），
/// 外加徽标那一张已经解开、能立刻画的图。矩形为空就是这一样东西当时没画；动画只接画了的那几样。
/// </summary>
internal sealed class PreviewHeadingCapture(
    EmbyItem item, Rect title, double titleFontSize, Rect? plate, ArtworkRef? plateArtwork, BitmapImage? platePicture)
{
    private readonly string _identity = item.IdentityKey;
    private readonly object? _owner = item.Source?.UserStateOwner;
    internal EmbyItem Item { get; } = item;
    internal Rect Title { get; } = title;

    /// <summary>面板里片名的字号：详情页拿自己的字号比它得出起飞缩放，比按高度算不受折行行数影响。</summary>
    internal double TitleFontSize { get; } = titleFontSize;

    internal Rect? Plate { get; } = plate;
    internal ArtworkRef? PlateArtwork { get; } = plateArtwork;
    internal BitmapImage? PlatePicture { get; } = platePicture;

    internal bool Matches(EmbyItem other)
    {
        if (other.Source?.IsCurrent != true || Item.Source?.IsCurrent != true) return false;
        return ReferenceEquals(_owner, Item.Source?.UserStateOwner) && ReferenceEquals(Item.Source?.UserStateOwner, other.Source?.UserStateOwner)
            && _identity == Item.IdentityKey && _identity == other.IdentityKey;
    }
}

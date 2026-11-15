using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Windows.Foundation;

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
        if (capture.Item.Source?.IsCurrent != true) return;
        var ticket = new Ticket(frame, capture);
        Tickets.Add(frame, ticket);
    }

    /// <summary>详情页接入时取走票据；身份对不上就作废，页面照常呈现。</summary>
    internal static bool Attach(DetailPage page, EmbyItem item)
    {
        if (page.Frame is not { } frame || !Tickets.TryGetValue(frame, out var ticket)) return false;
        Tickets.Remove(frame);
        ticket.Detach();
        if (!ticket.Capture.Matches(item)) return false;
        return page.BeginPreviewHeadingMotion(ticket.Capture);
    }

    private static void Cancel(Frame? frame)
    {
        if (frame is null || !Tickets.TryGetValue(frame, out var ticket)) return;
        Tickets.Remove(frame);
        ticket.Detach();
    }

    private sealed class Ticket
    {
        private readonly Frame _frame;
        private readonly DispatcherQueueTimer _timeout;
        private bool _done;

        internal Ticket(Frame frame, PreviewHeadingCapture capture)
        {
            _frame = frame;
            Capture = capture;
            frame.Navigating += OnNavigating;
            frame.NavigationStopped += OnNavigationStopped;
            frame.NavigationFailed += OnNavigationFailed;
            frame.Unloaded += OnFrameUnloaded;
            _timeout = frame.DispatcherQueue.CreateTimer();
            _timeout.Interval = TimeSpan.FromMilliseconds(700);
            _timeout.IsRepeating = false;
            _timeout.Tick += OnTimeout;
            _timeout.Start();
        }

        internal PreviewHeadingCapture Capture { get; }

        internal void Detach()
        {
            if (_done) return;
            _done = true;
            _timeout.Stop();
            _timeout.Tick -= OnTimeout;
            _frame.Navigating -= OnNavigating;
            _frame.NavigationStopped -= OnNavigationStopped;
            _frame.NavigationFailed -= OnNavigationFailed;
            _frame.Unloaded -= OnFrameUnloaded;
        }

        private void OnNavigating(object sender, Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs args)
        {
            // 这一趟若不是去这张票等的那一项，票据就作废；详情页的身份核对仍会再问一次。
            if (args.Parameter is DetailRequest request && Capture.Matches(request.Item)) return;
            Cancel(_frame);
        }

        private void OnNavigationStopped(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) => Cancel(_frame);
        private void OnNavigationFailed(object sender, Microsoft.UI.Xaml.Navigation.NavigationFailedEventArgs args) => Cancel(_frame);
        private void OnFrameUnloaded(object sender, RoutedEventArgs args) => Cancel(_frame);
        private void OnTimeout(object? sender, object args) => Cancel(_frame);
    }
}

/// <summary>
/// 悬停面板收起来之前量到的交接：片名与徽标在窗口内容里的矩形（Popup 偏移加面板内位置），
/// 外加徽标那一张已经解开、能立刻画的图。矩形为空就是这一样东西当时没画；动画只接画了的那几样。
/// </summary>
internal sealed class PreviewHeadingCapture(
    EmbyItem item, Rect title, double titleFontSize, Rect? plate, ArtworkRef? plateArtwork, BitmapImage? platePicture)
{
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
        return ReferenceEquals(Item.Source?.UserStateOwner, other.Source?.UserStateOwner)
            && Item.IdentityKey == other.IdentityKey;
    }
}

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

/// <summary>
/// 悬停预览收起前那点宽限：指针离开封面后先等一小会儿，好让它跨到面板上；期间回到任一处就不收。
/// 媒体卡与订阅卡共用，计时器只在第一次排程时建。
/// </summary>
internal sealed class PreviewCloseDelay(Action close, Func<bool> keep)
{
    /// <summary>面板与卡片之间那点空隙的宽限，和原先两张卡各自写死的那一份相同。</summary>
    internal const int GraceMilliseconds = 180;
    private DispatcherQueueTimer? _timer;

    internal void Schedule()
    {
        if (_timer is null)
        {
            _timer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
            if (_timer is null) return;
            _timer.Interval = TimeSpan.FromMilliseconds(GraceMilliseconds);
            _timer.IsRepeating = false;
            _timer.Tick += OnTick;
        }
        _timer.Stop();
        _timer.Start();
    }

    internal void Cancel() => _timer?.Stop();

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (keep()) return;
        close();
    }
}

/// <summary>
/// 盯住卡片所在的那几条滚动：面板是照打开那一刻的卡片位置摆的，内容一动两者就错位。
/// 卡片在横向货架里、货架又在页面的纵向滚动里，两条都要盯。媒体卡与订阅卡共用。
/// </summary>
internal sealed class PreviewScrollWatch(FrameworkElement anchor, Action scrolled)
{
    private readonly List<ScrollViewer> _viewers = [];

    internal void Watch()
    {
        if (_viewers.Count > 0) return;
        for (var parent = VisualTreeHelper.GetParent(anchor); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollViewer viewer) continue;
            viewer.ViewChanged += OnScrolled;
            _viewers.Add(viewer);
        }
    }

    internal void Stop()
    {
        foreach (var viewer in _viewers) viewer.ViewChanged -= OnScrolled;
        _viewers.Clear();
    }

    private void OnScrolled(object? sender, ScrollViewerViewChangedEventArgs e) => scrolled();
}

internal static class PreviewPopupGate
{
    /// <summary>
    /// 有没有别的浮层正开着（更多菜单、对话框、筛选面板）。参数里的面板自己不算，工具提示也不算 ——
    /// 提示是「指针正停在这颗按钮上」的副产品，它开着的时候预览该照常出现。
    /// </summary>
    internal static bool AnotherOpen(XamlRoot? root, Popup? own)
    {
        if (root is not { } value) return false;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(value))
        {
            if (ReferenceEquals(popup, own)) continue;
            if (popup.Child is ToolTip) continue;
            return true;
        }
        return false;
    }
}

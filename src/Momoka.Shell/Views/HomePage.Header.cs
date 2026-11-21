using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Momoka.Shell.Views;

public sealed partial class HomePage
{
    private readonly HomeHeaderScrollState _headerScroll = new();
    private bool _restoringHeaderScroll;
    private bool _reflowingHeader;
    private int _headerRestoreCorrelation = -1;
    private double? _headerRestoreTarget;
    private double? _headerRestoreRequestedOffset;
    private bool _headerRestoreRequesting;
    private long _headerRestoreArmedAt;
    private DispatcherQueueTimer? _headerRestoreTimer;
    internal HomeHeaderMode HeaderMode => _headerScroll.Mode;

    internal void FocusHeaderScroll()
    {
        // 与 SettingsPage.FocusSettingsCards 同一策略：让视口自身接焦点，不把深处页面拉回首个按钮。
        var tabStop = Scroller.IsTabStop;
        Scroller.IsTabStop = true;
        try { Scroller.Focus(FocusState.Programmatic); }
        finally { Scroller.IsTabStop = tabStop; }
    }

    private void ResetHeaderScroll()
    {
        _headerScroll.Reset(BannerImmersive, Scroller.VerticalOffset);
        PaintInk();
    }

    // Gallery 6de91feccadfbb0a796b71caedead5a2284f730e:
    // Samples/ItemsView/ItemsViewPage.xaml.cs 的 ScrollView.ViewChanged 接线和 VerticalOffset 读取。
    // 样例没有方向隐藏操作条；方向滞回由本页拥有，横向货架不参与。
    private void OnHeaderViewChanged(ScrollView sender, object args)
    {
        if (_released) return;
        if (_restoringHeaderScroll) RestoreHeaderScroll();
        if (_reflowingHeader || _restoringHeaderScroll || _restoredScroll is not null)
            _headerScroll.Reset(BannerImmersive, sender.VerticalOffset);
        else
            _headerScroll.Update(BannerImmersive, sender.VerticalOffset);
        PaintInk();
    }

    private void OnHeaderScrollCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
    {
        if (_released || !_restoringHeaderScroll || args.CorrelationId != _headerRestoreCorrelation) return;
        _headerRestoreCorrelation = -1;
        _headerRestoreRequestedOffset = null;
        RestoreHeaderScroll();
        ResetHeaderScroll();
    }

    private void BeginHeaderScrollRestore(double offset)
    {
        StopHeaderScrollRestore();
        if (!double.IsFinite(offset) || DispatcherQueue is not { } dispatcher) return;
        _headerRestoreTarget = Math.Max(0, offset);
        _restoringHeaderScroll = true;
        _headerRestoreArmedAt = Environment.TickCount64;
        _headerRestoreTimer ??= dispatcher.CreateTimer();
        _headerRestoreTimer.IsRepeating = true;
        _headerRestoreTimer.Interval = TimeSpan.FromMilliseconds(50);
        _headerRestoreTimer.Tick -= OnHeaderRestoreTick;
        _headerRestoreTimer.Tick += OnHeaderRestoreTick;
        _headerRestoreTimer.Start();
        Scroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnHeaderRestorePointer), true);
        Scroller.AddHandler(PointerPressedEvent, new PointerEventHandler(OnHeaderRestorePointer), true);
        Scroller.AddHandler(KeyDownEvent, new KeyEventHandler(OnHeaderRestoreKey), true);
        RestoreHeaderScroll();
    }

    private void OnHeaderRestoreTick(DispatcherQueueTimer timer, object args) => RestoreHeaderScroll();

    private void RestoreHeaderScroll()
    {
        if (_headerRestoreTarget is not { } offset || _headerRestoreRequesting) return;
        if (_released || Scroller.State == ScrollingInteractionState.Interaction
            || Environment.TickCount64 - _headerRestoreArmedAt >= 600)
        {
            StopHeaderScrollRestore();
            return;
        }
        var target = Math.Clamp(offset, 0, Scroller.ScrollableHeight);
        if (Math.Abs(Scroller.VerticalOffset - target) < 1
            || _headerRestoreCorrelation >= 0 && _headerRestoreRequestedOffset is { } requested
                && Math.Abs(requested - target) < 1) return;
        // Loaded 后 ScrollPresenter 仍可能在重接视觉树；完成不代表目标已落实。
        // 保留原位置经过有限布局期，范围变化或迟到焦点滚动后再落实，用户输入立即接管。
        _headerRestoreCorrelation = -1;
        _headerRestoreRequestedOffset = target;
        _headerRestoreRequesting = true;
        try
        {
            _headerRestoreCorrelation = Scroller.ScrollTo(0, target,
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            if (_headerRestoreCorrelation < 0) _headerRestoreRequestedOffset = null;
        }
        finally { _headerRestoreRequesting = false; }
    }

    private void OnHeaderRestorePointer(object sender, PointerRoutedEventArgs args) => StopHeaderScrollRestore();

    private void OnHeaderRestoreKey(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down
            or Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Home
            or Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown)
            StopHeaderScrollRestore();
    }

    private void StopHeaderScrollRestore()
    {
        _headerRestoreTarget = null;
        _headerRestoreRequestedOffset = null;
        _headerRestoreCorrelation = -1;
        _restoringHeaderScroll = false;
        _headerRestoreTimer?.Stop();
        if (_headerRestoreTimer is not null) _headerRestoreTimer.Tick -= OnHeaderRestoreTick;
        Scroller.RemoveHandler(PointerWheelChangedEvent, new PointerEventHandler(OnHeaderRestorePointer));
        Scroller.RemoveHandler(PointerPressedEvent, new PointerEventHandler(OnHeaderRestorePointer));
        Scroller.RemoveHandler(KeyDownEvent, new KeyEventHandler(OnHeaderRestoreKey));
        if (!_released) ResetHeaderScroll();
    }
}

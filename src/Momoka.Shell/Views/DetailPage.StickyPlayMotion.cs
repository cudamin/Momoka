using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    private ConnectedAnimation? _headerPlayAnimation;
    private TypedEventHandler<ConnectedAnimation, object>? _headerPlayCompleted;
    private DispatcherTimer? _headerPlayTimeout;
    private EventHandler<object>? _headerPlayTimedOut;
    private Button? _headerPlayAnchor;
    private double _headerPlayOpacity;
    private bool _headerPlayHitTest;
    private bool _headerPlayTabStop;
    private bool _headerStickyTabStop;
    private AccessibilityView _headerPlayAccessibility;
    private AccessibilityView _headerStickyAccessibility;
    private bool _headerPlayReturning;
    private double _headerPlayWidth;
    private double _headerPlayHeight;
    private Rect _headerPlayAnchorBounds;
    private Rect _headerPlayTargetBounds;
    private int _headerPlayGeneration;

    private ConnectedAnimation? PrepareHeaderPlayMove(Button source)
    {
        if (!HeaderAnimationsEnabled || source.Visibility != Visibility.Visible
            || !source.IsLoaded || source.ActualWidth <= 0 || source.ActualHeight <= 0
            || XamlRoot is null || !ReferenceEquals(source.XamlRoot, XamlRoot)) return null;
        try
        {
            // WinUI Gallery v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7
            // SampleSupport/SamplePages/CardPage.xaml及.xaml.cs：同页Prepare→显示目标→TryStart，
            // 返回Completed后才收父层。MIT，Copyright (c) Microsoft Corporation。
            // Direct是平台150ms直达配置；不修改整个视图的DefaultDuration。
            var animation = ConnectedAnimationService.GetForCurrentView()
                .PrepareToAnimate("Momoka.DetailPlay." + Guid.NewGuid().ToString("N"), source);
            try { animation.Configuration = new DirectConnectedAnimationConfiguration(); }
            catch (Exception) { animation.Cancel(); throw; }
            return animation;
        }
        catch (Exception)
        {
            // 裁切区外的源可能没有composition node；按钮照常切换，不能为动画阻塞操作。
            return null;
        }
    }

    private void RememberHeaderPlayAnchor(Button anchor)
    {
        _headerPlayAnchor = anchor;
        _headerPlayOpacity = anchor.Opacity;
        _headerPlayHitTest = anchor.IsHitTestVisible;
        _headerPlayTabStop = anchor.IsTabStop;
        _headerStickyTabStop = StickyPlayButton.IsTabStop;
        _headerPlayAccessibility = AutomationProperties.GetAccessibilityView(anchor);
        _headerStickyAccessibility = AutomationProperties.GetAccessibilityView(StickyPlayButton);
        _headerPlayWidth = Body.ActualWidth;
        _headerPlayHeight = Body.ActualHeight;
        if (_headerPlayAnimation is not null)
        {
            _headerPlayAnchorBounds = HeaderPlayBounds(anchor, Body, Body.VerticalOffset);
            _headerPlayTargetBounds = HeaderPlayBounds(StickyPlayButton, StickyHeader, 0);
        }
    }

    private void BeginHeaderPlayMove(ConnectedAnimation? animation, Button anchor, bool returning)
    {
        if (animation is null)
        {
            if (!returning) SettleHeaderPlayPosition();
            return;
        }
        _headerPlayReturning = returning;
        _headerPlayAnimation = animation;
        var generation = ++_headerPlayGeneration;
        _headerPlayCompleted = (_, _) =>
        {
            if (generation == _headerPlayGeneration && ReferenceEquals(_headerPlayAnimation, animation))
                FinishHeaderPlayMove();
        };
        try
        {
            RememberHeaderPlayAnchor(anchor);
            animation.Completed += _headerPlayCompleted;
            if (returning)
            {
                StickyPlayButton.IsTabStop = false;
                AutomationProperties.SetAccessibilityView(StickyPlayButton, AccessibilityView.Raw);
            }
            else
            {
                // Prepare只缓存位置，快照到下一合成提交才冻结；此处不能提前把源Opacity改为0。
                anchor.IsHitTestVisible = false;
                anchor.IsTabStop = false;
                AutomationProperties.SetAccessibilityView(anchor, AccessibilityView.Raw);
                if (XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), anchor))
                    StickyPlayButton.Focus(FocusState.Keyboard);
            }
            if (!animation.TryStart(returning ? anchor : StickyPlayButton))
            {
                FinishHeaderPlayMove();
                return;
            }
            if (!ReferenceEquals(_headerPlayAnimation, animation)) return;
            _headerPlayTimeout ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _headerPlayTimedOut = (_, _) =>
            {
                if (generation == _headerPlayGeneration && ReferenceEquals(_headerPlayAnimation, animation))
                    FinishHeaderPlayMove();
            };
            _headerPlayTimeout.Tick += _headerPlayTimedOut;
            _headerPlayTimeout.Start();
        }
        catch (Exception)
        {
            if (ReferenceEquals(_headerPlayAnimation, animation)) FinishHeaderPlayMove();
        }
    }

    private void FinishHeaderPlayMove()
    {
        var returning = _headerPlayReturning;
        StopHeaderPlayAnimation();
        if (returning)
        {
            RestoreHeaderPlayAnchor();
            SyncHeaderMotion();
            StickyHeader.Visibility = Visibility.Collapsed;
        }
        else if (_headerShown && _headerPlayAnchor is { } anchor)
        {
            anchor.Opacity = 0;
            anchor.IsHitTestVisible = false;
            anchor.IsTabStop = false;
            AutomationProperties.SetAccessibilityView(anchor, AccessibilityView.Raw);
            StickyPlayButton.IsTabStop = _headerStickyTabStop;
            if (XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), anchor))
                StickyPlayButton.Focus(FocusState.Keyboard);
            SyncHeaderMotion();
        }
        else RestoreHeaderPlayAnchor();
    }

    private void StopHeaderPlayAnimation()
    {
        _headerPlayGeneration++;
        var animation = _headerPlayAnimation;
        _headerPlayAnimation = null;
        var completed = _headerPlayCompleted;
        _headerPlayCompleted = null;
        _headerPlayTimeout?.Stop();
        if (_headerPlayTimeout is not null && _headerPlayTimedOut is not null)
            _headerPlayTimeout.Tick -= _headerPlayTimedOut;
        _headerPlayTimedOut = null;
        if (animation is null) return;
        try { if (completed is not null) animation.Completed -= completed; }
        catch (Exception) { }
        try { animation.Cancel(); }
        catch (Exception) { }
    }

    private void RestoreHeaderPlayAnchor()
    {
        if (_headerPlayAnchor is not { } anchor) return;
        _headerPlayAnchor = null;
        anchor.Opacity = _headerPlayOpacity;
        anchor.IsHitTestVisible = _headerPlayHitTest;
        anchor.IsTabStop = _headerPlayTabStop;
        AutomationProperties.SetAccessibilityView(anchor, _headerPlayAccessibility);
        StickyPlayButton.IsTabStop = _headerStickyTabStop;
        AutomationProperties.SetAccessibilityView(StickyPlayButton, _headerStickyAccessibility);
    }

    private void ClearHeaderPlayMove()
    {
        StopHeaderPlayAnimation();
        RestoreHeaderPlayAnchor();
    }

    private static Rect HeaderPlayBounds(Button button, UIElement relativeTo, double scrollOffset)
    {
        var bounds = button.TransformToVisual(relativeTo)
            .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
        return new Rect(bounds.X, bounds.Y + scrollOffset, bounds.Width, bounds.Height);
    }

    private bool HeaderPlayLayoutChanged()
    {
        if (_headerPlayAnchor is not { } anchor) return false;
        if (!ReferenceEquals(anchor, HeaderAnchor) || Body.ActualWidth != _headerPlayWidth
            || Body.ActualHeight != _headerPlayHeight) return true;
        if (_headerPlayAnimation is null) return false;
        try
        {
            // 比内容坐标而非滚动中视口坐标；滚动继续不取消，换行/迟到海报改变落点才落定。
            return !HeaderPlayBoundsMatch(HeaderPlayBounds(anchor, Body, Body.VerticalOffset), _headerPlayAnchorBounds)
                || !HeaderPlayBoundsMatch(HeaderPlayBounds(StickyPlayButton, StickyHeader, 0), _headerPlayTargetBounds);
        }
        catch (Exception) { return true; }
    }

    private static bool HeaderPlayBoundsMatch(Rect left, Rect right) =>
        Math.Abs(left.X - right.X) < 0.5 && Math.Abs(left.Y - right.Y) < 0.5
        && Math.Abs(left.Width - right.Width) < 0.5 && Math.Abs(left.Height - right.Height) < 0.5;

    private void SettleHeaderPlayPosition()
    {
        ClearHeaderPlayMove();
        if (_headerShown && _headerActive)
        {
            var anchor = HeaderAnchor;
            RememberHeaderPlayAnchor(anchor);
            anchor.Opacity = 0;
            anchor.IsHitTestVisible = false;
            anchor.IsTabStop = false;
            AutomationProperties.SetAccessibilityView(anchor, AccessibilityView.Raw);
            if (XamlRoot is { } root && ReferenceEquals(FocusManager.GetFocusedElement(root), anchor))
                StickyPlayButton.Focus(FocusState.Keyboard);
        }
        else StickyHeader.Visibility = Visibility.Collapsed;
    }
}

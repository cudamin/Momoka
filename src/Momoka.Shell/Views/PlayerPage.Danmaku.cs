using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell.Views;

public sealed partial class PlayerPage
{
    private bool _danmakuComposing;

    private void OnDanmakuCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args) => _danmakuComposing = true;
    private void OnDanmakuCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args) => _danmakuComposing = false;

    private bool DanmakuInputFocused
    {
        get
        {
            if (XamlRoot is null) return false;
            var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            while (current is not null)
            {
                if (ReferenceEquals(current, DanmakuInputBar)) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }

    private void OnDanmakuInputGotFocus(object sender, RoutedEventArgs e) => Hold(true, ChromeHold.DanmakuInput);

    private void OnDanmakuInputLostFocus(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => Hold(DanmakuInputFocused, ChromeHold.DanmakuInput));

    private void OnDanmakuInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_danmakuComposing || e.Handled) return;
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (ViewModel.DanmakuSendEnabled) ViewModel.SendDanmakuCommand.Execute(null);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Focus(FocusState.Programmatic);
        }
    }

    private void OnDanmakuTitleSizeChanged(object sender, SizeChangedEventArgs e) => ArrangeDanmakuInput();

    private void ArrangeDanmakuInput()
    {
        var visible = Attached && ViewModel.DanmakuInputVisible;
        var width = TitleAndDanmaku.ActualWidth;
        if (!visible)
        {
            PlayerTitleColumn.MaxWidth = double.PositiveInfinity;
            TitleBox.MaxWidth = double.PositiveInfinity;
            if (DanmakuInputFocused) Focus(FocusState.Programmatic);
            Hold(false, ChromeHold.DanmakuInput);
            return;
        }
        var scale = BigChrome ? 1.3 : 1;
        var inputWidth = Math.Clamp(width * 0.48, 120, 280 * scale);
        PlayerTitleColumn.MaxWidth = Math.Max(0, width - inputWidth);
        TitleBox.MaxWidth = Math.Max(0, PlayerTitleColumn.MaxWidth - TitleBox.Margin.Left);
        DanmakuInputStatus.MaxWidth = Math.Max(0, inputWidth - 16);
        DanmakuInputBox.Width = Math.Max(44, inputWidth - (ViewModel.DanmakuCanSend ? 70 : 16));
        DanmakuInputBar.Margin = new Thickness(8, BigChrome ? 6 : 5, 4, 0);
        DanmakuInputBox.Height = BigChrome ? 40 : 30;
        SendDanmakuButton.Height = DanmakuInputBox.Height;
        DanmakuInputStatus.Margin = new Thickness(8, BigChrome ? 50 : 40, 8, 0);
    }

    private Flyout? _danmakuFlyout;
    private DanmakuPanel? _danmakuPanel;

    private void OnDanmakuClick(object sender, RoutedEventArgs e)
    {
        if (!Attached || !ViewModel.DanmakuAvailable) return;
        CloseDanmakuPanel();
        var panel = new DanmakuPanel();
        panel.Attach(ViewModel);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.Top };
        _danmakuPanel = panel;
        _danmakuFlyout = flyout;
        flyout.Opened += (_, _) =>
        {
            if (ReferenceEquals(_danmakuFlyout, flyout)) Hold(true, ChromeHold.Menu);
        };
        flyout.Closed += (_, _) => OnDanmakuFlyoutClosed(flyout, panel);
        flyout.ShowAt(DanmakuButton);
    }

    private void OnDanmakuFlyoutClosed(Flyout flyout, DanmakuPanel panel)
    {
        // Hide 的 Closed 可以晚于下一次打开；旧浮层不能关掉新面板或取消新搜索。
        if (!ReferenceEquals(_danmakuFlyout, flyout)) return;
        _danmakuFlyout = null;
        _danmakuPanel = null;
        panel.Close();
        Hold(false, ChromeHold.Menu);
    }

    private void CloseDanmakuPanel()
    {
        var flyout = _danmakuFlyout;
        var panel = _danmakuPanel;
        _danmakuFlyout = null;
        _danmakuPanel = null;
        panel?.Close();
        flyout?.Hide();
        if (flyout is not null) Hold(false, ChromeHold.Menu);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Momoka.Shell.Views;

public sealed partial class PlayerPage
{
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

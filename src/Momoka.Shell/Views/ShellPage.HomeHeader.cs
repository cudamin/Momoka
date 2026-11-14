using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool _homeHeaderHidden;

    public void RefreshHomeHeader() => SyncHomeHeaderChrome();

    private void SyncHomeHeaderChrome()
    {
        var mode = !_sidebarMode && !_sidebarPlaying && Chrome.Visibility == Visibility.Visible
            && ContentHost.Visibility == Visibility.Visible && ContentFrame.Content is HomePage home
            ? home.HeaderMode : HomeHeaderMode.Top;
        var hidden = mode == HomeHeaderMode.Hidden;
        if (hidden && !_homeHeaderHidden)
        {
            // 收起真正的控件，不能只改透明度留下键盘焦点或不可见按钮。
            AccountMenu.Hide();
            if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && InHeader(focused))
                (ContentFrame.Content as HomePage)?.FocusHeaderScroll();
        }
        _homeHeaderHidden = hidden;
        AppTitleBar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        AccountCluster.Visibility = !hidden && Chrome.Visibility == Visibility.Visible
            && AccountButton.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        HomeHeaderBackground.Visibility = mode == HomeHeaderMode.Revealed ? Visibility.Visible : Visibility.Collapsed;
        if (_window is not null) _window.BrowseTitleBarHidden = hidden;
        ReportTitleBarHoles();

        bool InHeader(DependencyObject element)
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, AppTitleBar) || ReferenceEquals(node, AccountCluster)) return true;
            return false;
        }
    }
}

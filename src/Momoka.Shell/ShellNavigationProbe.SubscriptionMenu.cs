using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task CheckSubscriptionWallMenuAnchorAsync(MoviePilotSubscriptionWallView wall, string kind)
    {
        var card = Descendants(wall).OfType<MoviePilotSubscriptionWallCard>().First();
        var button = Get<Button>(card, "CardMore");
        Require(ReferenceEquals(card.MenuAnchor, button), "订阅墙菜单没有使用更多按钮作锚点");
        Require(button.Visibility == Visibility.Visible && button.ActualWidth > 0 && button.ActualHeight > 0,
            "订阅墙更多按钮没有实际可见尺寸");

        // UIA 执行真实 Click → OnMore → MenuRequested → OnCardMenu，不能直接对测试按钮 ShowAt 冒充产品接线。
        var existingPopups = VisualTreeHelper.GetOpenPopupsForXamlRoot(wall.XamlRoot).ToArray();
        var window = _window ?? throw new InvalidOperationException("探针窗口尚未创建");
        var topMost = window.TopMost;
        MenuFlyoutPresenter? presenter = null;
        try
        {
            // 在开菜单前置顶；开完才改变原生层级会盖住 Popup 或触发平台收起，不能把那张图当作菜单证据。
            window.TopMost = true;
            await LayoutAsync(wall);
            InvokeSubscriptionButton(button);
            await UntilAsync(() =>
            {
                presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(wall.XamlRoot)
                    .Where(popup => !existingPopups.Contains(popup))
                    .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().SingleOrDefault();
                return presenter is { ActualWidth: > 0, ActualHeight: > 0 };
            });
            Require(presenter is not null, "订阅墙更多菜单没有打开");
            await LayoutAsync(presenter!);
            var anchor = Bounds(button, _root);
            var menu = Bounds(presenter!, _root);
            var horizontalGap = Math.Max(0, Math.Max(anchor.Left - menu.Right, menu.Left - anchor.Right));
            var verticalGap = Math.Max(0, Math.Max(anchor.Top - menu.Bottom, menu.Top - anchor.Bottom));
            // 允许平台翻到按钮上/下/左/右；一轴必须相邻，另一轴必须重叠，整卡定位的远偏不能通过。
            Require(horizontalGap <= 12 && verticalGap <= 12 && (horizontalGap == 0 || verticalGap == 0),
                $"订阅墙菜单远离更多按钮：{kind}，button={anchor}，menu={menu}，gap={horizontalGap:0.#}/{verticalGap:0.#}");
            var menuPopup = VisualTreeHelper.GetOpenPopupsForXamlRoot(wall.XamlRoot)
                .Single(popup => Descendants(popup.Child).Contains(presenter!));
            Require(menuPopup.IsLightDismissEnabled,
                "订阅墙菜单失去了原生点外关闭");
            await Task.Delay(300);
            Require(menuPopup.IsOpen, "取屏前订阅墙菜单已收起");
            await CompositionPlaybackProbe.SaveScreenAsync(window,
                Path.Combine(_options.Paths.LogDirectory, $"mp-wall-menu-{kind}-screen.png"));
        }
        finally
        {
            // 仅收本次更多菜单；不关闭检查台或页面上无关的 Popup。
            try
            {
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(wall.XamlRoot).ToArray())
                    if (!existingPopups.Contains(popup) && Descendants(popup.Child).OfType<MenuFlyoutPresenter>().Any())
                        popup.IsOpen = false;
            }
            finally { window.TopMost = topMost; }
        }
    }
}

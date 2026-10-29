using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>保留左侧动作图标，用设置页同款白色 MP 标志标记订阅菜单项的右侧服务归属；文本仍是可访问的真实菜单 Text。</summary>
internal static class MoviePilotMenuBrand
{
    internal static void Apply(MenuFlyoutItem item) =>
        item.Style = (Style)Application.Current.Resources["EgMoviePilotMenuItemStyle"];
}

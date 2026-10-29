using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>保留左侧动作图标，用设置页同款白色 MP 标志标记右侧服务归属；文本仍是可访问的真实菜单 Text。</summary>
internal static class MoviePilotMenuBrand
{
    internal static void Apply(MenuFlyoutItemBase item, bool branded = true)
    {
        if (!branded) return;
        var key = item is MenuFlyoutSubItem ? "EgMoviePilotMenuSubItemStyle" : "EgMoviePilotMenuItemStyle";
        item.Style = (Style)Application.Current.Resources[key];
    }
}

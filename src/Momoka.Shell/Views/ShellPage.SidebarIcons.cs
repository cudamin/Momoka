using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private readonly Dictionary<NavigationViewItem, (IconElement Outline, IconElement Filled)> _sidebarIcons = [];

    private void InitializeSidebarIcons()
    {
        // Gallery 6de91feccadfbb0a796b71caedead5a2284f730e:
        // Samples/Iconography/IconsData.json 的 Home/HomeSolid、Heart/HeartFill、Video/VideoSolid。
        // MainWindow 与 Samples/NavigationView 只同步 SelectedItem，没有换实心图标的现成样例；
        // 保留原生导航与设置动画，图标跟随 SyncSidebar 最终解析的页面（含详情所属库与返回历史）。
        RegisterSidebarIcon(SidebarHome, new FontIcon { Glyph = "\uE80F" }, new FontIcon { Glyph = "\uEA8A" });
        RegisterSidebarIcon(SidebarFavorites, SidebarFavorites.Icon, new FontIcon { Glyph = "\uEB52" });
        // 延续 ShellPage.xaml 已核对的 MDI 外形，只去掉选中图标的内部孔。
        RegisterSidebarIcon(SidebarMoviePilotMovies, SidebarMoviePilotMovies.Icon, SidebarPathIcon(
            "F1 M 13.89,1.45 L 11.27,1.97 L 13.1,4.33 L 14.41,4.07 L 13.89,1.45 M 9.31,2.36 L 8,2.62 L 9.83,4.97 L 11.14,4.71 L 9.31,2.36 M 6.05,3 L 4.73,3.27 L 6.57,5.63 L 7.87,5.37 L 6.05,3 M 2.77,3.67 L 2.12,3.79 C 1.4,3.93,0.93,4.64,1.07,5.36 L 1.33,6.67 L 4.6,6.02 L 2.77,3.67 M 14.67,6.67 H 1.33 V 13.33 C 1.33,14.07,1.93,14.67,2.67,14.67 H 13.33 C 14.07,14.67,14.67,14.07,14.67,13.33 V 6.67 Z"));
        RegisterSidebarIcon(SidebarMoviePilotSeries, SidebarMoviePilotSeries.Icon, SidebarPathIcon(
            "F1 M 14,2 H 2 A 1.33,1.33 0 0 0 0.67,3.33 V 11.33 A 1.33,1.33 0 0 0 2,12.67 H 5.33 V 14 H 10.67 V 12.67 H 14 A 1.33,1.33 0 0 0 15.33,11.33 V 3.33 A 1.33,1.33 0 0 0 14,2 Z"));
    }

    private void RegisterSidebarLibraryIcon(NavigationViewItem item, string? collectionType)
    {
        if (collectionType == "tvshows")
            RegisterSidebarIcon(item, new FontIcon { Glyph = "\uE714" }, new FontIcon { Glyph = "\uEA0C" });
        else
            // Movies (E8B2) 没有同形实心码位；核对 Segoe Fluent Icons 的实际圆角胶片轮廓，
            // 在 16×16 图标格内填实中间并留下齿孔，不拿 MDL2 的直角胶片替代。
            RegisterSidebarIcon(item, new FontIcon { Glyph = "\uE8B2" }, SidebarPathIcon(
                "F0 M 3.9375,15 Q 3.3515625,15 2.81640625,14.76171875 Q 2.28125,14.5234375 1.87890625,14.12109375 Q 1.4765625,13.71875 1.23828125,13.18359375 Q 1,12.6484375 1,12.0625 V 3.9375 Q 1,3.3515625 1.23828125,2.81640625 Q 1.4765625,2.28125 1.87890625,1.87890625 Q 2.28125,1.4765625 2.81640625,1.23828125 Q 3.3515625,1 3.9375,1 H 12.0625 Q 12.6484375,1 13.18359375,1.23828125 Q 13.71875,1.4765625 14.12109375,1.87890625 Q 14.5234375,2.28125 14.76171875,2.81640625 Q 15,3.3515625 15,3.9375 V 12.0625 Q 15,12.6484375 14.76171875,13.18359375 Q 14.5234375,13.71875 14.12109375,14.12109375 Q 13.71875,14.5234375 13.18359375,14.76171875 Q 12.6484375,15 12.0625,15 Z M 3,3.5 A .5,.5 0 0 1 4,3.5 V 4.5 A .5,.5 0 0 1 3,4.5 Z M 12,3.5 A .5,.5 0 0 1 13,3.5 V 4.5 A .5,.5 0 0 1 12,4.5 Z M 3,7.5 A .5,.5 0 0 1 4,7.5 V 8.5 A .5,.5 0 0 1 3,8.5 Z M 12,7.5 A .5,.5 0 0 1 13,7.5 V 8.5 A .5,.5 0 0 1 12,8.5 Z M 3,11.5 A .5,.5 0 0 1 4,11.5 V 12.5 A .5,.5 0 0 1 3,12.5 Z M 12,11.5 A .5,.5 0 0 1 13,11.5 V 12.5 A .5,.5 0 0 1 12,12.5 Z"));
    }

    private void RegisterSidebarIcon(NavigationViewItem item, IconElement outline, IconElement filled)
    {
        _sidebarIcons.Add(item, (outline, filled));
        item.Icon = outline;
    }

    private void SyncSidebarIcons(NavigationViewItem? selected)
    {
        foreach (var (item, icons) in _sidebarIcons)
        {
            var icon = ReferenceEquals(item, selected) ? icons.Filled : icons.Outline;
            if (!ReferenceEquals(item.Icon, icon)) item.Icon = icon;
        }
    }

    /// <summary>
    /// 侧栏该选中哪一项。顶层目的地直接用 <paramref name="current"/>；详情、人物、子网格这些下钻页
    /// 没有目的地，用面包屑第一节认出它所属的媒体库或收藏。**第一节是「主页」时不算所属库** —— 那只是
    /// 从主页卡片下钻的起点，不是详情所在的媒体库，侧栏不该继续亮着主页（2026-11-14 用户报）。
    /// 纯函数留在这一份文件里，便于页面契约测试直接验证；选中项同时决定实心图标和
    /// <c>NavigationView.SelectedItem</c>。
    /// </summary>
    internal static string? ResolveSidebarTag(string? current, string? firstCrumbTag) =>
        current ?? (firstCrumbTag is { Length: > 0 } tag && tag != "home" ? tag : null);

    // 只解析本文件的固定路径，每个状态各持有自己的 Geometry；切页时复用对象，不重复解析或共享几何。
    private static PathIcon SidebarPathIcon(string data)
    {
        var icon = (PathIcon)XamlReader.Load(
            $"<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{data}\" />");
        if (data.StartsWith("F0", StringComparison.Ordinal))
        {
            // 胶片按完整字框给尺寸；MDI 两颗沿用它们原来的自然测量。
            icon.Width = 16;
            icon.Height = 16;
        }
        return icon;
    }
}

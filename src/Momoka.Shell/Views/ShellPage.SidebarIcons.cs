using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Momoka.Music;

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
        RegisterSidebarIcon(SidebarMoviePilotCalendar, SidebarMoviePilotCalendar.Icon, new FontIcon { Glyph = "\uEA89" });
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
        else if (MusicCatalog.IsLibrary(collectionType))
            // Gallery 同一提交 Samples/Iconography/IconsData.json 的 Audio（E8D6），与音乐页保持一致。
            // 系统没有同形实心配对；按 SegoeIcons.ttf 1.56 的 E8D6 外轮廓填实，去掉三个内孔。
            // 字体 2048 em 缩为 16，反转 Y 轴；两种状态保留相同的双音符外形。
            RegisterSidebarIcon(item, new FontIcon { Glyph = "\uE8D6" }, SidebarPathIcon(
                "F0 M 14,0.859375 V 12.5546875 Q 14,13.0625 13.796875,13.5078125 Q 13.59375,13.953125 13.25,14.28515625 Q 12.90625,14.6171875 12.453125,14.80859375 Q 12,15 11.5,15 Q 10.9765625,15 10.5234375,14.8046875 Q 10.0703125,14.609375 9.73046875,14.26953125 Q 9.390625,13.9296875 9.1953125,13.4765625 Q 9,13.0234375 9,12.5 Q 9,12 9.19140625,11.546875 Q 9.3828125,11.09375 9.71484375,10.75 Q 10.046875,10.40625 10.4921875,10.203125 Q 10.9375,10 11.4453125,10 Q 11.8671875,10 12.265625,10.12109375 Q 12.6640625,10.2421875 13,10.5 V 4.71875 L 6,7.34375 V 13.5546875 Q 6,14.0625 5.796875,14.5078125 Q 5.59375,14.953125 5.25,15.28515625 Q 4.90625,15.6171875 4.453125,15.80859375 Q 4,16 3.5,16 Q 2.9765625,16 2.5234375,15.8046875 Q 2.0703125,15.609375 1.73046875,15.26953125 Q 1.390625,14.9296875 1.1953125,14.4765625 Q 1,14.0234375 1,13.5 Q 1,13 1.19140625,12.546875 Q 1.3828125,12.09375 1.71484375,11.75 Q 2.046875,11.40625 2.4921875,11.203125 Q 2.9375,11 3.4453125,11 Q 3.8671875,11 4.26171875,11.12109375 Q 4.65625,11.2421875 4.9921875,11.4921875 L 4.9609375,3.6796875 Q 4.9609375,3.453125 5.09765625,3.25390625 Q 5.234375,3.0546875 5.453125,2.96875 L 12.9921875,0.15625 Q 13.109375,0.109375 13.25,0.109375 Q 13.5625,0.109375 13.78125,0.328125 Q 14,0.546875 14,0.859375 Z"));
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
            // 系统胶片和音符按完整字框给尺寸；MDI 两颗沿用它们原来的自然测量。
            icon.Width = 16;
            icon.Height = 16;
        }
        return icon;
    }
}

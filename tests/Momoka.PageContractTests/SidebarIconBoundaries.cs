using System.Xml.Linq;
using Microsoft.UI.Xaml.Controls;

// 链接正式图标状态实现；只替代 WinUI 对象和固定 XAML 解析边界，不宣称像素、布局或真实输入覆盖。
namespace Microsoft.UI.Xaml.Controls
{
    public class IconElement;
    public sealed class FontIcon : IconElement
    {
        public string Glyph { get; set; } = "";
    }
    public sealed class PathIcon(string data) : IconElement
    {
        public string Data { get; } = data;
        public double Width { get; set; }
        public double Height { get; set; }
    }
    public sealed class NavigationViewItem
    {
        private IconElement _icon = new FontIcon();
        public int IconWrites { get; private set; }
        public IconElement Icon
        {
            get => _icon;
            set { _icon = value; IconWrites++; }
        }
    }
}

namespace Microsoft.UI.Xaml.Markup
{
    public static class XamlReader
    {
        public static object Load(string source) => new PathIcon(XElement.Parse(source).Attribute("Data")!.Value);
    }
}

namespace Momoka.Shell.Views
{
    public sealed partial class ShellPage
    {
        private NavigationViewItem SidebarHome { get; } = new();
        private NavigationViewItem SidebarFavorites { get; } = new() { Icon = new FontIcon { Glyph = "\uEB51" } };
        private NavigationViewItem SidebarMoviePilotMovies { get; } = new() { Icon = new PathIcon("movie outline") };
        private NavigationViewItem SidebarMoviePilotSeries { get; } = new() { Icon = new PathIcon("series outline") };
        private NavigationViewItem SidebarMoviePilotCalendar { get; } = new() { Icon = new FontIcon { Glyph = "\uE787" } };

        internal ShellPage() => InitializeSidebarIcons();
        internal NavigationViewItem[] ContractItems => _sidebarIcons.Keys.ToArray();
        internal NavigationViewItem ContractMoviePilotMovies => SidebarMoviePilotMovies;
        internal NavigationViewItem ContractMoviePilotSeries => SidebarMoviePilotSeries;
        internal (IconElement Outline, IconElement Filled) ContractPair(NavigationViewItem item) => _sidebarIcons[item];
        internal void ContractSelect(NavigationViewItem? item) => SyncSidebarIcons(item);
        internal NavigationViewItem ContractLibrary(string? collectionType)
        {
            var item = new NavigationViewItem();
            RegisterSidebarLibraryIcon(item, collectionType);
            return item;
        }
    }
}

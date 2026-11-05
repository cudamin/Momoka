using Microsoft.UI.Xaml;
using Momoka.Shell.Views;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("继续观看没有分类仍收起所有空行并留28DIP内容间距", () =>
        {
            var page = new LibraryPage();
            page.Apply();
            Assert.Equal(0d, page.PageRoot.RowSpacing);
            Assert.Equal(0d, page.HeaderGrid.RowSpacing);
            Assert.Equal(0d, page.HeaderLayout.RowSpacing);
            Assert.Equal(0d, ((Microsoft.UI.Xaml.Controls.Grid)page.UpcomingScroll.Parent!).RowSpacing);
            Assert.Equal(28d, page.EmbyContent.Margin.Top);
            Assert.Equal("EgSectionTitleStyle", page.LibraryTitle.Style!.Key);
        });
        Test("非搜索分类与工具栏在各后端和内容页保持左对齐", () =>
        {
            foreach (var kind in new[] { "emby", "jellyfin", "fnnas", "favorites", "upcoming" })
            {
                var page = new LibraryPage();
                page.ViewModel.IsSectionLibrary = kind != "fnnas";
                page.ViewModel.IsFnNas = kind == "fnnas";
                page.ViewModel.IsFavoritesPage = kind == "favorites";
                page.ViewModel.IsUpcomingPage = kind == "upcoming";
                page.Apply();
                Assert.Equal(HorizontalAlignment.Left, page.LibrarySectionsSurface.HorizontalAlignment, kind);
                Assert.Equal(HorizontalAlignment.Left, page.ToolbarSurface.HorizontalAlignment, kind);
                Assert.False(page.LibrarySections.CenterRows, kind);
                Assert.False(page.Toolbar.CenterRows, kind);
                Assert.Equal(28d, page.EmbyContent.Margin.Top, kind);
            }
        });
        Test("文件夹由面包屑承担标题且工具栏不留隐藏标题间距", () =>
        {
            var page = new LibraryPage();
            page.ViewModel.IsFolderDrillDown = true;
            page.Apply();
            Assert.Equal(Visibility.Collapsed, page.LibraryTitle.Visibility);
            Assert.Equal(0d, page.ToolbarSurface.Margin.Top);
            Assert.Equal(0, page.ToolbarSurface.Row);
        });
        Test("搜索保留顶部安全区与独立内容间距并能恢复普通浏览", () =>
        {
            var page = new LibraryPage();
            page.Apply(search: true);
            Assert.Equal(40d, page.PageRoot.Margin.Top);
            Assert.Equal(8d, page.EmbyContent.Margin.Top);
            Assert.Equal(Visibility.Collapsed, page.LibraryTitle.Visibility);
            Assert.Equal(900d, page.SearchSurface.Width);
            Assert.Equal(480d, page.SourceTabs.Width);
            page.Apply();
            Assert.Equal(Visibility.Visible, page.LibraryTitle.Visibility);
            Assert.Equal(28d, page.EmbyContent.Margin.Top);
        });
        Test("只有Jellyfin字母条使用独立Auto列且切回Emby恢复覆盖", () =>
        {
            var page = new LibraryPage();
            page.ViewModel.IsJellyfin = true;
            page.Apply();
            Assert.Equal(1, page.Alpha.Column);
            page.ViewModel.IsJellyfin = false;
            page.Apply();
            Assert.Equal(0, page.Alpha.Column);
        });
        Test("Attach未结束或布局未测量时不提前改动页头", () =>
        {
            var page = new LibraryPage();
            page.Apply(attaching: true);
            Assert.Equal(0, page.HeaderApplies);
            page.HeaderLayout.ActualWidth = 0;
            page.Apply();
            Assert.Equal(0, page.HeaderApplies);
        });
        Test("字母条Auto列没有固定最小宽度", () =>
        {
            var root = Root();
            var document = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/LibraryPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var alpha = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "Alpha");
            var columns = alpha.Parent!.Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().ToArray();
            Assert.Equal("Auto", (string?)columns[1].Attribute("Width"));
            Assert.Null(columns[1].Attribute("MinWidth"));
            Assert.Null(alpha.Attribute("Width"));
            Assert.Null(alpha.Attribute("MinWidth"));
            var control = XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views/AlphaBar.xaml"));
            Assert.Null(control.Root!.Attribute("Width"));
            Assert.Null(control.Root.Attribute("MinWidth"));
        });
        Test("真实AlphaBar回调在376DIP折叠轨道并在900DIP恢复", () =>
        {
            var alpha = new AlphaBar();
            alpha.SetCurrent("A");
            alpha.Resize(900);
            Assert.Equal(Visibility.Visible, alpha.Rail.Visibility);
            alpha.Resize(376);
            Assert.Equal(Visibility.Collapsed, alpha.Rail.Visibility);
            alpha.Resize(900);
            Assert.Equal(Visibility.Visible, alpha.Rail.Visibility);
            Assert.Equal(new Thickness(0, 5, 0, 5), alpha.Rail.Padding);
            Assert.Equal(new CornerRadius(13), alpha.Rail.CornerRadius);
            Assert.Equal("EgAlphaCurrentStyle", alpha.ButtonStyle("A"));
        });
        Test("真实AlphaBar回调在400到509DIP使用紧凑样式且恢复选中字母", () =>
        {
            var alpha = new AlphaBar();
            alpha.SetCurrent("B");
            foreach (var height in new[] { 400d, 509d })
            {
                alpha.Resize(height);
                Assert.Equal(Visibility.Visible, alpha.Rail.Visibility);
                Assert.Equal(new Thickness(0, 3, 0, 3), alpha.Rail.Padding);
                Assert.Equal(new CornerRadius(12), alpha.Rail.CornerRadius);
                Assert.Equal("EgAlphaCompactCurrentStyle", alpha.ButtonStyle("B"));
                Assert.Equal("EgAlphaCompactButtonStyle", alpha.ButtonStyle("A"));
            }
            alpha.Resize(399);
            Assert.Equal(Visibility.Collapsed, alpha.Rail.Visibility);
            alpha.Resize(510);
            Assert.Equal(Visibility.Visible, alpha.Rail.Visibility);
            Assert.Equal(new Thickness(0, 5, 0, 5), alpha.Rail.Padding);
            Assert.Equal("EgAlphaCurrentStyle", alpha.ButtonStyle("B"));
            Assert.Equal("EgAlphaButtonStyle", alpha.ButtonStyle("A"));
        });
        AgreementsTests.Register();
        return Run();
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到当前工作树");
    }
}

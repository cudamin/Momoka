using System.Xml.Linq;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SidebarIconTests
{
    internal static void Register()
    {
        Test("侧栏初始为空心且主页与收藏使用同形系统配对", () =>
        {
            var shell = new ShellPage();
            Assert.Equal(4, shell.ContractItems.Length);
            foreach (var item in shell.ContractItems)
                Assert.True(ReferenceEquals(item.Icon, shell.ContractPair(item).Outline));
            var home = shell.ContractPair(shell.ContractItems[0]);
            var heart = shell.ContractPair(shell.ContractItems[1]);
            Assert.Equal("\uE80F", ((FontIcon)home.Outline).Glyph);
            Assert.Equal("\uEA8A", ((FontIcon)home.Filled).Glyph);
            Assert.Equal("\uEB51", ((FontIcon)heart.Outline).Glyph);
            Assert.Equal("\uEB52", ((FontIcon)heart.Filled).Glyph);
        });
        Test("侧栏切页及返回只有当前项实心、旧项恢复空心", () =>
        {
            var shell = new ShellPage();
            var music = shell.ContractLibrary("music");
            var movie = shell.ContractLibrary("movies");
            var shows = shell.ContractLibrary("tvshows");
            foreach (var selected in shell.ContractItems.Concat(new[] { music, movie, shows, music, shell.ContractItems[0] }))
            {
                shell.ContractSelect(selected);
                foreach (var item in shell.ContractItems)
                    Assert.True(ReferenceEquals(item.Icon, ReferenceEquals(item, selected)
                        ? shell.ContractPair(item).Filled : shell.ContractPair(item).Outline));
            }
        });
        Test("侧栏无对应目的地时全部空心且重复同步不改图标对象", () =>
        {
            var shell = new ShellPage();
            var selected = shell.ContractItems[0];
            shell.ContractSelect(selected);
            var writes = shell.ContractItems.Select(item => item.IconWrites).ToArray();
            shell.ContractSelect(selected);
            Assert.True(writes.SequenceEqual(shell.ContractItems.Select(item => item.IconWrites)));
            shell.ContractSelect(null);
            foreach (var item in shell.ContractItems)
                Assert.True(ReferenceEquals(item.Icon, shell.ContractPair(item).Outline));
        });
        Test("侧栏动态库图标保持电视剧配对与胶片外形", () =>
        {
            var shell = new ShellPage();
            var shows = shell.ContractPair(shell.ContractLibrary("tvshows"));
            Assert.Equal("\uE714", ((FontIcon)shows.Outline).Glyph);
            Assert.Equal("\uEA0C", ((FontIcon)shows.Filled).Glyph);
            foreach (var type in new string?[] { "movies", "music", null })
            {
                var pair = shell.ContractPair(shell.ContractLibrary(type));
                Assert.Equal("\uE8B2", ((FontIcon)pair.Outline).Glyph);
                Assert.True(((PathIcon)pair.Filled).Data.StartsWith("F0 M 3.9375,15", StringComparison.Ordinal));
                Assert.Equal(16d, ((PathIcon)pair.Filled).Width);
                Assert.Equal(16d, ((PathIcon)pair.Filled).Height);
            }
        });
        Test("侧栏订阅填充图标移除内孔并保留独立对象", () =>
        {
            var shell = new ShellPage();
            var movies = shell.ContractPair(shell.ContractItems[2]);
            var shows = shell.ContractPair(shell.ContractItems[3]);
            Assert.True(!ReferenceEquals(movies.Outline, movies.Filled));
            Assert.True(!ReferenceEquals(shows.Outline, shows.Filled));
            Assert.True(!((PathIcon)movies.Filled).Data.Contains("M 13.33,8", StringComparison.Ordinal));
            Assert.True(!((PathIcon)shows.Filled).Data.Contains("M 14,11.33", StringComparison.Ordinal));
        });
        Test("侧栏选中目的地：顶层与详情所属库，从主页下钻不亮主页", () =>
        {
            Assert.Equal("home", ShellPage.ResolveSidebarTag("home", null));
            Assert.Equal("home", ShellPage.ResolveSidebarTag("home", "home"));
            Assert.Equal("library:movies", ShellPage.ResolveSidebarTag(null, "library:movies"));
            Assert.Equal("favorites", ShellPage.ResolveSidebarTag(null, "favorites"));
            Assert.Null(ShellPage.ResolveSidebarTag(null, "home"));
            Assert.Null(ShellPage.ResolveSidebarTag(null, ""));
            Assert.Null(ShellPage.ResolveSidebarTag(null, null));
            Assert.Equal("search", ShellPage.ResolveSidebarTag("search", "home"));
        });
        Test("侧栏图标同步接在最终导航状态并清理旧媒体库", () =>
        {
            var source = Read("ShellPage.Sidebar.cs");
            Assert.Contains("InitializeSidebarIcons();", source);
            Assert.Contains("SyncSidebarIcons(selected);", source);
            Assert.Contains("ResolveSidebarTag(_current, _trail.FirstOrDefault()?.Tag)", source);
            Assert.Contains("_sidebarIcons.Remove(item);", source);
            Assert.Contains("RegisterSidebarLibraryIcon(item, library.CollectionType);", source);
            Assert.True(source.IndexOf("SyncSidebarIcons(selected);", StringComparison.Ordinal)
                > source.IndexOf("Sidebar.SelectedItem = selected", StringComparison.Ordinal));
            var page = XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var settings = page.Descendants().Single(element => (string?)element.Attribute(xaml + "Name") == "SidebarSettings");
            Assert.Equal("False", (string?)settings.Attribute("SelectsOnInvoked"));
        });
        Test("折叠图标由面板状态变化同步、宽窄及按钮共用路径", () =>
        {
            var source = Read("ShellPage.Sidebar.cs");
            Assert.Contains("RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) => ApplySidebarLayout())", source);
            Assert.Contains("NavPaneExpandedIcon.Visibility = Sidebar.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;", source);
            Assert.Contains("NavPaneCollapsedIcon.Visibility = Sidebar.IsPaneOpen ? Visibility.Collapsed : Visibility.Visible;", source);
            var selfCheck = Read("ShellPage.SelfCheck.cs");
            Assert.Contains("backGlyph.Width == (_sidebarMode ? 20 : 26)", selfCheck);
            Assert.Contains("forwardGlyph.Width == (_sidebarMode ? 20 : 26)", selfCheck);
            Assert.Contains("NavToggleButton.Content is Viewbox { Width: 20, Height: 20 }", selfCheck);
        });
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", file));
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到当前工作树");
    }
}

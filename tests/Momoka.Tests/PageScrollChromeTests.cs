using System.Globalization;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

// Page geometry contracts. These check ownership and the right edge; actual pixels remain runtime acceptance.
internal static class PageScrollChromeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("页面滚动条：所有主滚动视口贴右沿，内容留白放在内部", () =>
        {
            foreach (var (file, owner) in new[]
            {
                ("HomePage.xaml", "Scroller"), ("DetailPage.xaml", "Body"),
                ("PersonPage.xaml", "Body"), ("LibraryPage.xaml", "TableScroll"),
                ("LibraryPage.xaml", "SearchGroupsScroll"), ("LibraryPage.xaml", "RecommendedScroll"),
                ("LibraryPage.xaml", "FavoritesScroll"), ("LibraryPage.xaml", "UpcomingScroll"),
                ("LibraryPage.xaml", "RecentSearchList"),
                ("MusicLibraryPage.xaml", "MusicRows"), ("MusicLibraryPage.xaml", "MusicWall"),
                ("LocalLibraryPage.xaml", "LocalLibraryGroups"), ("LocalMediaPage.xaml", "LocalMediaScroll"),
                ("MoviePilotCalendarPage.xaml", "CalendarScroll"), ("MoviePilotSubscriptionPage.xaml", "DetailScroll"),
                ("MoviePilotSubscriptionPage.xaml", "DetailErrorState"), ("SettingsPage.xaml", "SettingsScroll"),
                ("FnNasLibrariesPage.xaml", "FnNasLibrariesList"), ("FnNasLibrariesPage.xaml", "FnNasLibraryEditorScroll"),
                ("FnNasUsersPage.xaml", "FnNasUsersList"), ("FnNasUsersPage.xaml", "FnNasUserEditorScroll"),
                ("FnNasSettingsPage.xaml", "FnNasSettingsScroll"), ("FnNasTasksPage.xaml", "FnNasTasksScroll")
            })
            {
                var node = Named(Page(file), owner);
                RequireRightEdge(node, file + ":" + owner);
                if (node.Name.LocalName is "ScrollView" or "ScrollViewer")
                    Assert.Equal(0d, Right(node.Attribute("Padding")?.Value), file + ":" + owner + " 在视口外裁掉了背景");
            }
            foreach (var file in new[] { "FnNasPasswordPage.xaml", "FnNasPreferencesPage.xaml", "NotificationsPage.xaml", "ServersPage.xaml", "MoviePilotSubscriptionsPage.xaml" })
            {
                var scroll = Page(file).Descendants().Single(node => node.Name.LocalName is "ScrollView" or "ScrollViewer");
                RequireRightEdge(scroll, file);
                Assert.Equal(0d, Right(scroll.Attribute("Padding")?.Value), file + " 把背景裁切放在了外层");
                Assert.True(Right(scroll.Elements().Single().Attribute("Padding")?.Value) >= 20, file + " 未留内容安全带");
            }
        });
        Test("页面滚动条：原生形状和边距统一，拒绝局部模板与资源覆盖", () =>
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Momoka.Shell", "Views"), "*.xaml"))
            {
                var doc = XDocument.Load(file);
                Assert.False(doc.Descendants().Any(node => node.Name.LocalName == "Style" && node.Attribute("TargetType")?.Value == "ScrollBar"), file);
                Assert.False(doc.Descendants().Any(node => node.Attribute(Xaml + "Key")?.Value is "ScrollViewerScrollBarMargin" or "ScrollViewScrollBarsMargin"), file);
            }
            var styles = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Theme", "Styles.xaml"));
            Assert.False(styles.Contains("EgNarrowScrollBarStyle", StringComparison.Ordinal));
            var home = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "HomePage.xaml.cs"));
            Assert.False(home.Contains("PlaceScrollBar", StringComparison.Ordinal));
        });
        Test("页面滚动条：背景铺满与详情正文对称留白保留", () =>
        {
            var detail = Page("DetailPage.xaml");
            Assert.Equal("10,0,10,0", Named(detail, "BodySheet").Attribute("Margin")?.Value);
            Assert.True(Named(detail, "BodyPaper").Attribute("Margin") is null);
            Assert.Equal("0", Named(Page("SettingsPage.xaml"), "HostedFrame").Attribute("Margin")?.Value);
            foreach (var (file, name) in new[]
            {
                ("HomePage.xaml", "Overlay"), ("LibraryPage.xaml", "CardWall"),
                ("LibraryPage.xaml", "RecommendedRows"), ("LibraryPage.xaml", "FavoritesGroups"),
                ("LibraryPage.xaml", "UpcomingGroups"),
                ("MusicLibraryPage.xaml", "MusicRows"), ("MusicLibraryPage.xaml", "MusicWall"),
                ("MoviePilotCalendarPage.xaml", "MonthGrid"), ("SettingsPage.xaml", "SettingsCardsContent")
            })
            {
                var node = Named(Page(file), name);
                Assert.Equal(20d, Math.Max(Right(node.Attribute("Padding")?.Value), Right(node.Attribute("Margin")?.Value)), file + ":" + name);
            }
        });
        Test("页面滚动条：横向拥有者、虚拟化和媒体库实际内容宽度保留", () =>
        {
            var library = Page("LibraryPage.xaml");
            Assert.Equal("Disabled", Named(library, "TableScroll").Attribute("VerticalScrollMode")?.Value);
            Assert.Equal("ItemsView", Named(library, "CardWall").Name.LocalName);
            Assert.True(Named(library, "RecentSearchSection").Attribute("MaxWidth") is null);
            var source = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "LibraryPage.xaml.cs"));
            Assert.Contains("-PageRoot.Margin.Right", source);
            Assert.Contains("TableScroll.ActualWidth - CardWall.Padding.Right", source);
            Assert.Contains("UpcomingScroll.ActualWidth - UpcomingGroups.Margin.Right", source);
            Assert.Contains("FavoritesScroll.ActualWidth - FavoritesGroups.Margin.Right", source);
            Assert.Contains("SearchGroupsScroll.ActualWidth - ((StackPanel)SearchGroupsScroll.Content).Padding.Right", source);
            Assert.Equal(20d, Right(Named(library, "Alpha").Attribute("Margin")?.Value));
            Assert.Contains("TableScroll.HorizontalScrollMode = table ? ScrollMode.Enabled : ScrollMode.Disabled", source);
            Assert.Equal("Hidden", Named(Page("ShelfStrip.xaml"), "Scroller").Attribute("VerticalScrollBarVisibility")?.Value);
        });
        Test("页面滚动条：几何检查会拒绝额外右移和外层裁切回归", () =>
        {
            var local = Page("LocalLibraryPage.xaml");
            Named(local, "LocalLibraryGroups").SetAttributeValue("Margin", "0");
            Assert.Throws<AssertionException>(() => RequireRightEdge(Named(local, "LocalLibraryGroups"), "broken"));
        });
    }

    private static void RequireRightEdge(XElement owner, string label)
    {
        var inset = Right(owner.Attribute("Margin")?.Value);
        foreach (var parent in owner.Ancestors())
        {
            inset += Right(parent.Attribute("Margin")?.Value) + Right(parent.Attribute("Padding")?.Value);
            if (parent.Name.LocalName == "Page") break;
        }
        Assert.True(Math.Abs(inset) <= 0.01, label + " 距页面右沿 " + inset.ToString(CultureInfo.InvariantCulture));
    }

    private static double Right(string? value)
    {
        if (value is null) return 0;
        if (value == "{StaticResource EgPageMargin}") return 32;
        if (value == "{StaticResource EgScrollBarLane}") return 20;
        if (value.StartsWith('{')) return 0; // VM-owned detail decoration does not position the viewport.
        var parts = value.Split(',').Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        return parts.Length switch { 1 => parts[0], 2 => parts[0], 4 => parts[2], _ => throw new InvalidOperationException(value) };
    }

    private static XDocument Page(string file) => XDocument.Load(Path.Combine(Root(), "src", "Momoka.Shell", "Views", file));
    private static XElement Named(XDocument doc, string name) => doc.Descendants().Single(node => node.Attribute(Xaml + "Name")?.Value == name);
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("当前工作树缺失");
    }
}

using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Checks the search-branch integration against the current tree, not rendered WinUI.</summary>
internal static class SearchCompactMergeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register(string sourceRoot)
    {
        Test("搜索合并：同行站点入口保留中性焦点与原生查询事件", () =>
            CheckSearchEntry(Load(sourceRoot, "LibraryPage.xaml")));

        Test("搜索合并：订阅结果保留点击入口与滚动条车道，不恢复来源筛选", () =>
            CheckSubscriptionPanel(Load(sourceRoot, "MoviePilotSearchPanel.xaml")));

        Test("搜索合并：焦点恢复与迟到结果守卫不依赖来源栏显隐", () =>
        {
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/LibraryPage.xaml.cs"));
            Assert.Contains("SearchInput.AddHandler(GettingFocusEvent", source);
            Assert.Contains("SearchInput.AddHandler(PointerPressedEvent", source);
            Assert.Contains("args.FocusState is FocusState.Keyboard or FocusState.Pointer", source);
            Assert.Contains("presentation == _searchPresentationVersion", source);
            Assert.Contains("SearchForm.RowSpacing = SourceTabs.Visibility == Visibility.Visible ? 8 : 0", source);
            // Hiding the selector must not silently reroute the next request to the media library.
            Assert.Contains("SearchSource => ViewModel.MoviePilotTabsVisibility == Visibility.Visible", source);
            Assert.DoesNotContain("SearchSource => SourceTabs.Visibility", source);
            Assert.Contains("host.ColumnDefinitions.Insert(column, new ColumnDefinition { Width = GridLength.Auto })", source);
            Assert.Contains("Grid.SetColumn(SearchSitePicker, column)", source);
        });

        Test("搜索合并：紧凑探针与来源退役检查共存", () =>
        {
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/ShellNavigationProbe.SearchLayout.cs"));
            Assert.Contains("await CompactSearchHeaderAsync(page)", source);
            Assert.Contains("picker.Name == \"SourceFilter\"", source);
            Assert.Contains("NewMediaSearches(mp.Paths, beforeReturnSearch)", source);
            Assert.Contains("NewMediaSearches(mp.Paths, beforeRepeatSearch)", source);
            Assert.DoesNotContain("subscriptionPanel.ViewModel.SourceChoices", source);
            // The merged selector adds 4/8 DIP at the top; probes must read that live margin
            // while keeping the caption-button clearance and tight content gap strict.
            Assert.Contains("Math.Abs(box.Top - search.Margin.Top) <= 1", source);
            Assert.Contains("search.Margin.Top is 4 or 8", source);
            Assert.Contains("Get<Grid>(page, \"PageRoot\").Margin.Top == 40", source);
            Assert.Contains("Bounds(search, page).Top >= 32", source);
            var aniBt = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/ShellNavigationProbe.AniBt.cs"));
            Assert.Contains("Math.Abs(top - surface.Margin.Top) <= 1", aniBt);
            Assert.Contains("surface.Margin.Top is 4 or 8", aniBt);
            Assert.Contains("pageRoot.Margin.Top == 40", aniBt);
            Assert.Contains("TransformToVisual(page).TransformPoint(default).Y >= 32", aniBt);
            Assert.Contains("gap is >= 7 and <= 9", aniBt);
        });

        Test("搜索合并：焦点契约拒绝误恢复强调色", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            xml.Descendants().Single(node => node.Attribute(Xaml + "Key")?.Value == "TextControlBorderBrushFocused")
                .SetAttributeValue("ResourceKey", "EgAccentBrush");
            Assert.Throws<AssertionException>(() => CheckSearchEntry(xml));
        });

        Test("搜索合并：订阅契约拒绝旧分支重新带回来源筛选", () =>
        {
            var xml = Load(sourceRoot, "MoviePilotSearchPanel.xaml");
            Named(xml, "SearchContent").Add(new XElement(xml.Root!.Name.Namespace + "ComboBox",
                new XAttribute(Xaml + "Name", "SourceFilter")));
            Assert.Throws<AssertionException>(() => CheckSubscriptionPanel(xml));
        });
    }

    private static XDocument Load(string root, string file) =>
        XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Views", file));

    private static XElement Named(XDocument xml, string name) =>
        xml.Descendants().Single(node => node.Attribute(Xaml + "Name")?.Value == name);

    private static void CheckSearchEntry(XDocument xml)
    {
        var entry = Named(xml, "SearchEntry");
        var input = Named(xml, "SearchInput");
        Assert.Equal("SearchEntry", input.Parent?.Attribute(Xaml + "Name")?.Value);
        Assert.Equal("SearchEntry", Named(xml, "SearchSitePicker").Parent?.Attribute(Xaml + "Name")?.Value);
        Assert.Equal("Grid", entry.Name.LocalName);
        Assert.Equal("OnSearchSubmitted", input.Attribute("QuerySubmitted")?.Value);
        Assert.Equal("OnSearchTextChanged", input.Attribute("TextChanged")?.Value);
        Assert.Equal("{x:Bind ViewModel.SearchText, Mode=OneWay}", input.Attribute("Text")?.Value);
        Assert.Equal("EgBorderBrush", input.Descendants().Single(node =>
            node.Attribute(Xaml + "Key")?.Value == "TextControlBorderBrushFocused").Attribute("ResourceKey")?.Value);
        Assert.Equal("1", input.Descendants().Single(node =>
            node.Attribute(Xaml + "Key")?.Value == "TextControlBorderThemeThicknessFocused").Value);
    }

    private static void CheckSubscriptionPanel(XDocument xml)
    {
        Assert.False(xml.Descendants().Any(node => node.Attribute(Xaml + "Name")?.Value == "SourceFilter"));
        var results = Named(xml, "Results");
        Assert.Equal("True", results.Attribute("IsItemInvokedEnabled")?.Value);
        Assert.Equal("OnResultInvoked", results.Attribute("ItemInvoked")?.Value);
        Assert.Equal("{StaticResource EgScrollBarLane}", results.Attribute("Padding")?.Value);
        var content = Named(xml, "SearchContent");
        Assert.True(content.Attribute("RowSpacing") is null or { Value: "0" });
        var heading = content.Elements().Single(node => node.Name.LocalName == "StackPanel"
            && node.Attribute("Grid.Row")?.Value == "2");
        Assert.Equal("0,0,0,12", heading.Attribute("Margin")?.Value);
    }
}

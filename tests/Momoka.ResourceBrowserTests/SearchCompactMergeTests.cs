using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Checks the search-branch integration against the current tree, not rendered WinUI.</summary>
internal static class SearchCompactMergeTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register(string sourceRoot)
    {
        Test("搜索返回：工具栏由页面来源统一显隐，绑定不再覆盖", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            Assert.Equal("Collapsed", Named(xml, "ToolbarSurface").Attribute("Visibility")?.Value);
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/LibraryPage.xaml.cs"));
            Assert.Contains("if (e.PropertyName == nameof(LibraryViewModel.ToolbarVisibility))", source);
            Assert.Contains("ToolbarSurface.Visibility = _request?.IsSearch == true ? Visibility.Collapsed : ViewModel.ToolbarVisibility", source);
        });
        Test("搜索合并：同行站点入口保留中性焦点与原生查询事件", () =>
            CheckSearchEntry(Load(sourceRoot, "LibraryPage.xaml")));

        Test("搜索合并：订阅结果保留点击入口与滚动条车道，不恢复来源筛选", () =>
            CheckSubscriptionPanel(Load(sourceRoot, "MoviePilotSearchPanel.xaml")));

        Test("搜索来源：固定四入口，不因完成、焦点或连接改变导航", () =>
        {
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/LibraryPage.xaml.cs"));
            Assert.Contains("SourceTabs.Visibility = ViewModel.MoviePilotTabsVisibility", source);
            Assert.DoesNotContain("_searchOptionsCollapsed", source);
            Assert.DoesNotContain("_searchPresentationVersion", source);
            Assert.Contains("SearchForm.RowSpacing = SourceTabs.Visibility == Visibility.Visible ? 8 : 0", source);
            Assert.Contains("get => Math.Clamp(SourceTabs.SelectedIndex, 0, 3)", source);
            Assert.Contains("AniBtSourceTab.Visibility = Visibility.Visible", source);
            Assert.Contains("if (!SearchSourceAvailable)", source);
            Assert.Contains("SearchUnavailableSurface.Visibility = available ? Visibility.Collapsed : Visibility.Visible", source);
            Assert.DoesNotContain("SearchSource => SourceTabs.Visibility", source);
            // 模板里插那一列的做法只有一份（SearchSiteSlot），搜索页与独立窗口都用它。
            Assert.Contains("SearchSiteSlot.Place(SearchInput, SearchSitePicker)", source);
            var slot = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/SearchSiteSlot.cs"));
            Assert.Contains("host.ColumnDefinitions.Insert(column, new ColumnDefinition { Width = GridLength.Auto })", slot);
            Assert.Contains("Grid.SetColumn(picker, column)", slot);
            Assert.Contains("query.Width = 64", slot);
            Assert.Contains("query.MinWidth = 64", slot);
            Assert.Contains("query.IsTabStop = true", slot);
            Assert.Contains("AutomationProperties.SetAccessibilityView(query, AccessibilityView.Content)", slot);
            var vm = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/ViewModels/LibraryViewModel.cs"));
            Assert.Contains("public Visibility MoviePilotTabsVisibility =>\n        Show(_request?.IsSearch == true)", vm.Replace("\r\n", "\n"));
        });

        Test("搜索合并：紧凑探针与来源退役检查共存", () =>
        {
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/ShellNavigationProbe.SearchLayout.cs"));
            Assert.Contains("await PersistentSearchHeaderAsync(page)", source);
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
            Assert.Contains("focusResults.Focus(FocusState.Programmatic)", aniBt);
            var inputGuard = aniBt.IndexOf("selectionInput == \"离线动画\"", StringComparison.Ordinal);
            var identityGuard = aniBt.IndexOf("panel.ViewModel.Results.Contains(firstAnime)", StringComparison.Ordinal);
            var open = aniBt.IndexOf("await firstAnime.OpenCommand.ExecuteAsync(null)", StringComparison.Ordinal);
            Assert.True(inputGuard >= 0 && identityGuard > inputGuard && open > identityGuard,
                "番剧资源验证必须先核当前输入与原行身份，不得绕过结果守卫");
        });

        Test("搜索输入：占位与实际输入垂直居中，保留原生文本框模板", () =>
            CheckSearchTextAlignment(Load(sourceRoot, "LibraryPage.xaml"), LoadStyles(sourceRoot)));

        Test("搜索输入：居中契约拒绝退回顶部对齐", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            Named(xml, "SearchInput").SetAttributeValue("VerticalContentAlignment", "Top");
            Assert.Throws<AssertionException>(() => CheckSearchTextAlignment(xml, LoadStyles(sourceRoot)));
        });

        Test("搜索合并：版本窗口的搜索区与搜索页同一套圆角与框内站点", () =>
        {
            var xml = Load(sourceRoot, "MoviePilotVersionsView.xaml");
            var input = Named(xml, "SearchInput");
            Assert.Equal("40", input.Attribute("MinHeight")?.Value);
            Assert.Equal("{StaticResource EgSearchCornerRadius}", input.Attribute("CornerRadius")?.Value);
            Assert.Equal("{StaticResource EgSearchTextBoxStyle}", input.Attribute("TextBoxStyle")?.Value);
            Assert.True(input.Attribute("Header") is null, "窗口里仍挂着搜索框上方的标题");
            Assert.Equal("SearchEntry", Named(xml, "SearchSitePicker").Parent?.Attribute(Xaml + "Name")?.Value);
            Assert.False(xml.Descendants().Any(node => node.Name.LocalName == "TextBlock"
                && (string?)node.Attribute("Text") == "搜索 MoviePilot 资源"), "窗口里仍有「搜索 MoviePilot 资源」这行字");
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/MoviePilotVersionsView.xaml.cs"));
            Assert.Contains("SearchSiteSlot.Place(SearchInput, SearchSitePicker)", source);
        });

        Test("搜索胶囊：隐藏搜索标题但保留普通媒体库标题绑定", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            var title = Named(xml, "LibraryTitle");
            Assert.Equal("{x:Bind ViewModel.Heading, Mode=OneWay}", title.Attribute("Text")?.Value);
            Assert.Equal("{x:Bind local:LibraryPage.BrowseHintVisibility(ViewModel.SearchVisibility), Mode=OneWay}",
                title.Attribute("Visibility")?.Value);
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/LibraryPage.xaml.cs"));
            Assert.Contains("LibraryTitle.Visibility = search ? Visibility.Collapsed : Visibility.Visible", source);
            Assert.Contains("Grid.SetRow(SearchSurface, search ? 0 : 1)", source);
            Assert.DoesNotContain("HeaderTitle(", source);
        });

        Test("搜索胶囊：外卡不绘制也不占内边距", () =>
            CheckSearchCapsules(Load(sourceRoot, "LibraryPage.xaml")));

        Test("搜索胶囊：更紧凑的卡片保留标准字号与原搜索框尺寸", () =>
            CheckSourceTabSize(Load(sourceRoot, "LibraryPage.xaml")));

        Test("搜索胶囊：尺寸契约拒绝恢复旧高度", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            Named(xml, "SourceTabs").Descendants().Single(node => node.Name.LocalName == "Setter"
                && node.Attribute("Property")?.Value == "Height").SetAttributeValue("Value", "28");
            Assert.Throws<AssertionException>(() => CheckSourceTabSize(xml));
        });

        Test("搜索胶囊：保留原生模板与四个稳定来源入口", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            var tabs = Named(xml, "SourceTabs");
            var items = tabs.Elements().Where(node => node.Name.LocalName == "SegmentedItem").ToArray();
            Assert.Equal(4, items.Length);
            Assert.Equal("媒体库|MoviePilot 订阅|MoviePilot 资源|AniBT",
                string.Join('|', items.Select(item => item.Elements().Single().Attribute("Text")?.Value)));
            Assert.True(items.All(item => item.Attribute("AutomationProperties.Name") is not null));
            Assert.True(items.All(item => item.Elements().Single().Attribute("TextWrapping")?.Value == "NoWrap"));
            Assert.True(tabs.Descendants().Any(node => node.Attribute("BasedOn")?.Value == "{StaticResource ButtonSegmentedStyle}"));
            Assert.True(tabs.Descendants().Any(node => node.Attribute("BasedOn")?.Value == "{StaticResource ButtonSegmentedItemStyle}"));
            Assert.False(tabs.Descendants().Any(node => node.Name.LocalName == "ControlTemplate"),
                "来源胶囊不应替换框架的单选、键盘和辅助功能模板");
            Assert.Equal("EgAccentSoftBrush", tabs.Descendants().Single(node =>
                node.Attribute(Xaml + "Key")?.Value == "ButtonItemBackgroundSelected").Attribute("ResourceKey")?.Value);
            Assert.Equal("EgAccentBrush", tabs.Descendants().Single(node =>
                node.Attribute(Xaml + "Key")?.Value == "ButtonItemForegroundSelected").Attribute("ResourceKey")?.Value);
        });

        Test("搜索胶囊：窄窗宽度扣除内边距和间隔且不会回退查询来源", () =>
        {
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/LibraryPage.xaml.cs"));
            Assert.Contains("SearchEntry.Width = formWidth", source);
            Assert.Contains("SourceTabs.Width = Math.Min(480, formWidth)", source);
            Assert.Contains("(SourceTabs.Width - 14) / Math.Max(1, visibleTabs.Length)", source);
            Assert.Contains("horizontalPadding = formWidth < 520 ? 4 : 8", source);
            Assert.Contains("item.Padding = new Thickness(horizontalPadding, 2, horizontalPadding, 2)", source);
            Assert.Contains("item.MaxWidth - horizontalPadding * 2", source);
            Assert.Contains("1 => formWidth < 520 ? \"MP 订阅\" : \"MoviePilot 订阅\"", source);
            Assert.Contains("2 => formWidth < 520 ? \"MP 资源\" : \"MoviePilot 资源\"", source);
        });

        Test("搜索胶囊：真实父级和数据项辅助功能接到原生单选", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            Assert.Equal("SearchSourceTabs", Named(xml, "SourceTabs").Name.LocalName);
            var source = File.ReadAllText(Path.Combine(sourceRoot, "src/Momoka.Shell/Views/SearchSourceTabs.cs"));
            Assert.Contains("OnCreateAutomationPeer() => new SourceTabsAutomationPeer(this)", source);
            Assert.Contains("ListViewBaseAutomationPeer(owner)", source);
            Assert.Contains("OnCreateItemAutomationPeer(object item)", source);
            Assert.Contains("new ListViewItemDataAutomationPeer(item, this)", source);
        });

        Test("搜索胶囊：契约拒绝重新套外卡", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            Named(xml, "SearchSurface").SetAttributeValue("Background", "{ThemeResource EgSurfaceBrush}");
            Assert.Throws<AssertionException>(() => CheckSearchCapsules(xml));
        });

        Test("搜索胶囊：契约拒绝方形来源栏", () =>
        {
            var xml = Load(sourceRoot, "LibraryPage.xaml");
            Named(xml, "SourceTabs").Descendants().Single(node => node.Name.LocalName == "Setter"
                && node.Attribute("Property")?.Value == "CornerRadius" && node.Attribute("Value")?.Value == "16")
                .SetAttributeValue("Value", "0");
            Assert.Throws<AssertionException>(() => CheckSearchCapsules(xml));
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

    /// <summary>共享样式表：搜索框那一条和「在 MoviePilot 搜索版本」窗口共用同一份。</summary>
    private static XDocument LoadStyles(string root) =>
        XDocument.Load(Path.Combine(root, "src/Momoka.Shell/Theme/Styles.xaml"));

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

    private static void CheckSearchTextAlignment(XDocument xml, XDocument styles)
    {
        var input = Named(xml, "SearchInput");
        Assert.Equal("Center", input.Attribute("VerticalContentAlignment")?.Value);
        Assert.Equal("{StaticResource EgSearchTextBoxStyle}", input.Attribute("TextBoxStyle")?.Value);
        var style = styles.Descendants().Single(node => node.Attribute(Xaml + "Key")?.Value == "EgSearchTextBoxStyle");
        Assert.Equal("TextBox", style.Attribute("TargetType")?.Value);
        Assert.Equal("{StaticResource AutoSuggestBoxTextBoxStyle}", style.Attribute("BasedOn")?.Value);
        Assert.Equal("10,0,6,0", style.Elements().Single(node =>
            node.Attribute("Property")?.Value == "Padding").Attribute("Value")?.Value);
        Assert.False(style.Descendants().Any(node => node.Name.LocalName == "ControlTemplate"));
    }

    private static void CheckSearchCapsules(XDocument xml)
    {
        var surface = Named(xml, "SearchSurface");
        Assert.Equal("0", surface.Attribute("Padding")?.Value);
        Assert.Equal("0", surface.Attribute("BorderThickness")?.Value);
        Assert.True(surface.Attribute("Background") is null && surface.Attribute("BorderBrush") is null);
        var input = Named(xml, "SearchInput");
        Assert.Equal("40", input.Attribute("MinHeight")?.Value);
        Assert.Equal("{StaticResource EgSearchCornerRadius}", input.Attribute("CornerRadius")?.Value);
        var tabs = Named(xml, "SourceTabs");
        Assert.True(tabs.Descendants().Any(node => node.Name.LocalName == "Setter"
            && node.Attribute("Property")?.Value == "CornerRadius" && node.Attribute("Value")?.Value == "16"));
        Assert.True(tabs.Descendants().Any(node => node.Name.LocalName == "Setter"
            && node.Attribute("Property")?.Value == "CornerRadius" && node.Attribute("Value")?.Value == "12"));
    }

    private static void CheckSourceTabSize(XDocument xml)
    {
        var style = Named(xml, "SourceTabs").Elements().Single(node =>
            node.Name.LocalName == "SearchSourceTabs.ItemContainerStyle");
        string? Setter(string property) => style.Descendants().Single(node => node.Name.LocalName == "Setter"
            && node.Attribute("Property")?.Value == property).Attribute("Value")?.Value;
        Assert.Equal("24", Setter("MinHeight"));
        Assert.Equal("24", Setter("Height"));
        Assert.Equal("8,2", Setter("Padding"));
        Assert.Equal("{StaticResource EgCaptionFontSize}", Setter("FontSize"));
        Assert.Equal("40", Named(xml, "SearchInput").Attribute("MinHeight")?.Value);
    }

    private static void CheckSubscriptionPanel(XDocument xml)
    {
        Assert.False(xml.Descendants().Any(node => node.Attribute(Xaml + "Name")?.Value == "SourceFilter"));
        var results = Named(xml, "Results");
        Assert.Equal("False", results.Attribute("IsItemInvokedEnabled")?.Value);
        Assert.Null(results.Attribute("ItemInvoked"));
        Assert.Equal("{StaticResource EgScrollBarLane}", results.Attribute("Padding")?.Value);
        var content = Named(xml, "SearchContent");
        Assert.True(content.Attribute("RowSpacing") is null or { Value: "0" });
        var heading = content.Elements().Single(node => node.Name.LocalName == "StackPanel"
            && node.Attribute("Grid.Row")?.Value == "2");
        Assert.Equal("0,0,0,12", heading.Attribute("Margin")?.Value);
    }
}

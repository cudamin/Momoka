using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>标题栏导航的层级与可访问性契约；真实导航和收藏显隐由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("账号按钮：嵌套文字提供明确可访问名称并随身份刷新", () =>
        {
            var account = Named(Shell(), "AccountButton");
            Assert.Equal("ShellPage_AccountButton", account.Attribute(Xaml + "Uid")?.Value);
            var resources = ShellResources();
            var name = resources.Root!.Elements("data").Single(entry =>
                entry.Attribute("name")?.Value == "ShellPage_AccountButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name");
            Assert.Equal("账号与服务器", name.Element("value")?.Value);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent!;
            var source = File.ReadAllText(Path.Combine(root.FullName, "src", "Momoka.Shell", "Views", "ShellPage.xaml.cs"));
            var method = source[source.IndexOf("public void ShowAccount(", StringComparison.Ordinal)..];
            method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];
            Assert.Contains("AutomationProperties.SetName(AccountButton,", method);
            Assert.Contains("UiText.Format(\"账号与服务器：{0} · {1}\", AccountUser.Text, AccountServer.Text)", method);
            Assert.Contains("UiText.Translate(\"未登录\") : user", method);
            Assert.Contains("UiText.Translate(\"未连接服务器\") : server", method);
        });

        TestHarness.Test("标题栏那一排：返回、沉浸前进、商店折叠、主页、收藏、设置、搜索", () =>
        {
            var panel = Named(Shell(), "TitleActions");
            var order = panel.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            // 2026-11-12 晚用户令「主页不显示返回按钮，进入媒体页面或者媒体库页面的时候显示，放左上角，折叠侧边栏
            // 按钮移到它右边」「标题栏左上角写 Momoka」，并选定「前进键退役」；2026-11-13 又令「移除左上角的
            // momoka」，那一格整块删掉。这一排按模式插入前进/折叠，沉浸前进于2026-11-14补回，
            // Alt+→ 与鼠标侧键仍在（GoForward）。
            Assert.True(order.SequenceEqual(new[]
            {
                "BackButton", "ForwardButton", "NavToggleButton", "HomeButton", "FavoritesButton", "TitleUtilities"
            }), "标题栏这一排的顺序不对：" + string.Join('、', order));

            var utilities = Named(Shell(), "TitleUtilities");
            Assert.Equal("StackPanel", utilities.Name.LocalName);
            var buttons = utilities.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            Assert.True(buttons.SequenceEqual(new[] { "SettingsButton", "SearchButton" }), "工具按钮顺序不对");
        });
        TestHarness.Test("标题栏折叠键：面板图形，主页上它就是这一排的第一格", () =>
        {
            var toggle = Named(Shell(), "NavToggleButton");
            Assert.Equal("Button", toggle.Name.LocalName);
            Assert.Equal("OnNavToggleClicked", toggle.Attribute("Click")?.Value);
            Assert.Equal("Collapsed", toggle.Attribute("Visibility")?.Value);
            // 2026-11-14 新图：外框与内孔相同，展开是贯穿分隔线，收起是左侧短竖线，两档都不填实左栏。
            var expanded = Named(Shell(), "NavPaneExpandedIcon");
            var collapsed = Named(Shell(), "NavPaneCollapsedIcon");
            Assert.Equal(2, toggle.Descendants().Count(element => element.Name.LocalName == "PathIcon"));
            var openData = expanded.Attribute("Data")!.Value;
            var closedData = collapsed.Attribute("Data")!.Value;
            Assert.Equal(openData[..openData.LastIndexOf(" Z M ", StringComparison.Ordinal)],
                closedData[..closedData.LastIndexOf(" Z M ", StringComparison.Ordinal)]);
            Assert.Contains("H 8.45 V 16.7 H 6.75", openData);
            Assert.Contains("V 13.8 A 0.85,0.85", closedData);
            Assert.Equal("Collapsed", expanded.Attribute("Visibility")?.Value);
            Assert.True(collapsed.Attribute("Visibility") is null);
            // 2026-11-13 起字形居中、不再靠左：托盘 40 宽、字形 28，缩进 8，字形中线落在 28 ——
            // 和栏里那些图标同一条竖线（用户令「返回键出现时要和下面那一列对齐」）。靠左摆会落到 22。
            Assert.True(toggle.Attribute("HorizontalContentAlignment") is null, "折叠键又靠左摆了");
            var toggleBox = toggle.Descendants().Single(element => element.Name.LocalName == "Viewbox");
            Assert.Equal("20", toggleBox.Attribute("Width")?.Value);
            Assert.Equal("20", toggleBox.Attribute("Height")?.Value);
            // 内容也用 20 字框，不能再让隐式 24 PathIcon 在 Viewbox 里缩小一档。
            var panel = toggleBox.Elements().Single();
            Assert.Equal("20", panel.Attribute("Width")?.Value);
            Assert.Equal("20", panel.Attribute("Height")?.Value);
            foreach (var icon in new[] { expanded, collapsed })
            {
                Assert.Equal("20", icon.Attribute("Width")?.Value);
                Assert.Equal("20", icon.Attribute("Height")?.Value);
                Assert.Contains("L 19.4,14.8", icon.Attribute("Data")!.Value);
                Assert.Contains("L 0.6,5.2", icon.Attribute("Data")!.Value);
            }
            var backBox = Named(Shell(), "BackButton").Descendants().Single(element => element.Name.LocalName == "Viewbox");
            Assert.Equal("20", backBox.Attribute("Width")?.Value);
            Assert.Equal("20", backBox.Attribute("Height")?.Value);
        });
        TestHarness.Test("标题栏应用名在导航按钮右边，保留拖动区域", () =>
        {
            var label = Named(Shell(), "AppTitleLabel");
            Assert.Equal("Momoka", label.Attribute("Text")?.Value);
            Assert.Equal("False", label.Attribute("IsHitTestVisible")?.Value);
            Assert.Equal("Center", label.Attribute("VerticalAlignment")?.Value);
            Assert.Equal("Collapsed", label.Attribute("Visibility")?.Value);
            var sidebar = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.Sidebar.cs"));
            Assert.Contains("AppTitleLabel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed", sidebar);
            Assert.True(!Named(Shell(), "TitleActions").Descendants().Any(element =>
                element.Attribute(Xaml + "Name")?.Value == "AppTitleLabel"), "标题文字进了按钮命中区");
        });
        TestHarness.Test("标题栏搜索框点击后把输入移进内容区", () =>
        {
            var box = Named(Shell(), "TitleSearchBox");
            Assert.Equal("AutoSuggestBox", box.Name.LocalName);
            Assert.Equal("OnTitleSearchGotFocus", box.Attribute("GotFocus")?.Value);
            Assert.Null(box.Attribute("TextChanged"));
            Assert.Null(box.Attribute("QuerySubmitted"));
            Assert.Equal("Find", box.Attribute("QueryIcon")?.Value);
            Assert.Equal("Center", box.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal("Collapsed", box.Attribute("Visibility")?.Value);
            Assert.Equal("搜索媒体库、订阅等", box.Attribute("PlaceholderText")?.Value);
        });
        TestHarness.Test("标题栏搜索：商店侧栏宽窗使用居中框、窄窗放开左上放大镜，搜索页均收起", () =>
        {
            var source = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.xaml.cs"));
            var method = source[source.IndexOf("    private void SyncTitleSearch()", StringComparison.Ordinal)..];
            method = method[..method.IndexOf("\n    }", StringComparison.Ordinal)];
            Assert.Contains("SidebarShown && Chrome.Visibility == Visibility.Visible && TitleSearchPage is null", method);
            Assert.Contains("ActualWidth >= 700", method);
            Assert.Contains("ActualWidth < 700", method);
            Assert.Contains("TitleSearchBox.Visibility =", method);
            Assert.Contains("SearchButton.Visibility =", method);
            Assert.Contains("TitleUtilities.Visibility =", method);
            Assert.DoesNotContain("1000", method);
            var search = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "ShellPage.Search.cs"));
            Assert.Contains("TitleSearchTransition.Prepare(ContentFrame, TitleSearchBox)", search);
            var page = File.ReadAllText(Path.Combine(Root(), "src", "Momoka.Shell", "Views", "LibraryPage.xaml.cs"));
            Assert.Contains("TitleSearchTransition.Attach(Frame, SearchInput", page);
        });
        TestHarness.Test("标题栏收藏：爱心字形默认收着，没有收藏时不出现在那一排", () =>
        {
            var heart = Named(Shell(), "FavoritesButton");
            Assert.Equal("Button", heart.Name.LocalName);
            Assert.Equal("Collapsed", heart.Attribute("Visibility")?.Value);
            Assert.Equal("OnFavoritesClicked", heart.Attribute("Click")?.Value);
            Assert.Equal("ShellPage_FavoritesButton", heart.Attribute(Xaml + "Uid")?.Value);
            // v0.2.5固定2ff1224的双闭合轮廓空心爱心，和其他入口共用26放大框。
            var icon = heart.Descendants().Single(element => element.Name.LocalName == "PathIcon");
            Assert.True(icon.Attribute("Data")?.Value.StartsWith("F0 M 12,21.4", StringComparison.Ordinal) == true);
            Assert.True(!heart.Descendants().Any(element => element.Name.LocalName == "FontIcon"));
            Assert.Equal("26", heart.Descendants().Single(element => element.Name.LocalName == "Viewbox").Attribute("Width")?.Value);
        });
        TestHarness.Test("标题栏收藏：可访问名称与悬停提示走本地化资源", () =>
        {
            var resources = ShellResources();
            foreach (var property in new[]
            {
                "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
                "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"
            })
            {
                var value = resources.Descendants("data")
                    .FirstOrDefault(element => element.Attribute("name")?.Value == "ShellPage_FavoritesButton." + property)
                    ?.Element("value")?.Value;
                Assert.Equal("收藏", value);
            }
        });
        TestHarness.Test("沉浸标题栏前进：原生按钮、自动化名称与原历史入口恢复", () =>
        {
            var document = Shell();
            var button = Named(document, "ForwardButton");
            Assert.Equal("Button", button.Name.LocalName);
            Assert.Equal("ShellPage_ForwardButton", button.Attribute(Xaml + "Uid")?.Value);
            Assert.Equal("OnForwardClicked", button.Attribute("Click")?.Value);
            Assert.Equal("Collapsed", button.Attribute("Visibility")?.Value);
            Assert.Equal("False", button.Attribute("IsEnabled")?.Value);
            foreach (var property in new[]
            {
                "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
                "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"
            })
                Assert.Equal("前进", ShellResources().Descendants("data").Single(element =>
                    element.Attribute("name")?.Value == "ShellPage_ForwardButton." + property).Element("value")?.Value);
        });
        TestHarness.Test("标题栏导航：首页/收藏胶囊已退役，两支快捷键挂在外壳上", () =>
        {
            var document = Shell();
            Assert.True(!document.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "HomeNavigation"));
            // 胶囊退了役，但两支箭头都不自带加速器 —— Alt+← / Alt+→ 挂在外壳上，按钮停用也照样能走。
            var accelerators = document.Descendants().Where(element => element.Name.LocalName == "KeyboardAccelerator").ToArray();
            Assert.Equal(2, accelerators.Length);
            foreach (var accelerator in accelerators)
            {
                Assert.Equal("UserControl.KeyboardAccelerators", accelerator.Parent?.Name.LocalName);
                Assert.Equal("Menu", accelerator.Attribute("Modifiers")?.Value);
                Assert.True(!string.IsNullOrWhiteSpace(accelerator.Attribute("Invoked")?.Value));
            }
            Assert.Equal("Left", accelerators[0].Attribute("Key")?.Value);
            Assert.Equal("Right", accelerators[1].Attribute("Key")?.Value);
        });
        TestHarness.Test("外壳快捷键：不向整页生成Alt提示，返回按钮仍保留正常提示", () =>
        {
            Assert.Equal("Hidden", Shell().Root?.Attribute("KeyboardAcceleratorPlacementMode")?.Value);
            Assert.Equal("Auto", Named(Shell(), "Root").Attribute("KeyboardAcceleratorPlacementMode")?.Value);
            var tip = ShellResources().Descendants("data").Single(element =>
                element.Attribute("name")?.Value == "ShellPage_BackButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip");
            Assert.True(tip.Element("value")?.Value.Contains("返回", StringComparison.Ordinal) == true);
        });
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attribute(Xaml + "Name")?.Value == name);

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到源码树");
    }

    private static XDocument Shell()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return XDocument.Load(Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", "ShellPage.xaml"));
        throw new InvalidOperationException("找不到源码树");
    }

    private static XDocument ShellResources()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return XDocument.Load(Path.Combine(directory.FullName,
                    "src", "Momoka.Shell", "Strings", "zh-Hans", "Resources.resw"));
        throw new InvalidOperationException("找不到源码树");
    }
}

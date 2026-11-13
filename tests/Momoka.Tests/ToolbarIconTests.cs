using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>标题栏导航的层级与可访问性契约；真实导航和收藏显隐由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("标题栏那一排：返回、折叠、主页、收藏、设置、搜索按这个次序", () =>
        {
            var panel = Named(Shell(), "TitleActions");
            var order = panel.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            // 2026-11-12 晚用户令「主页不显示返回按钮，进入媒体页面或者媒体库页面的时候显示，放左上角，折叠侧边栏
            // 按钮移到它右边」「标题栏左上角写 Momoka」，并选定「前进键退役」；2026-11-13 又令「移除左上角的
            // momoka」，那一格整块删掉。这一排从此是 返回 · 折叠 · 主页 · 收藏 · 设置/搜索。前进那颗整颗删掉，
            // Alt+→ 与鼠标侧键仍在（GoForward）。
            Assert.True(order.SequenceEqual(new[]
            {
                "BackButton", "NavToggleButton", "HomeButton", "FavoritesButton", "TitleUtilities"
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
            // 2026-11-12 晚用户给了图：一个圆角面板、左栏实心。三笔几何 —— 外框、内孔、左栏 —— 都在同一段 Data 里。
            var icon = toggle.Descendants().Single(element => element.Name.LocalName == "PathIcon");
            var data = icon.Attribute("Data")?.Value ?? string.Empty;
            Assert.True(data.StartsWith("F1 M 6.2,3.6", StringComparison.Ordinal), "折叠键的图标不是那块面板：" + data);
            Assert.Equal(3, data.Split('Z').Length - 1, "面板图标不是三笔（外框、内孔、左栏）");
            // 2026-11-13 起字形居中、不再靠左：托盘 40 宽、字形 28，缩进 8，字形中线落在 28 ——
            // 和栏里那些图标同一条竖线（用户令「返回键出现时要和下面那一列对齐」）。靠左摆会落到 22。
            Assert.True(toggle.Attribute("HorizontalContentAlignment") is null, "折叠键又靠左摆了");
            var toggleBox = toggle.Descendants().Single(element => element.Name.LocalName == "Viewbox");
            Assert.Equal("28", toggleBox.Attribute("Width")?.Value);
            Assert.Equal("28", toggleBox.Attribute("Height")?.Value);
        });
        TestHarness.Test("标题栏不再写应用名：侧边栏模式那一排只有返回与折叠", () =>
        {
            Assert.True(!Shell().Descendants().Any(element =>
                element.Attribute(Xaml + "Name")?.Value == "AppTitleText"), "左上角又写回了 Momoka");
        });
        TestHarness.Test("标题栏搜索框：标题栏正中一颗按钮，写着搜索媒体库、订阅等", () =>
        {
            var box = Named(Shell(), "TitleSearchBox");
            Assert.Equal("Button", box.Name.LocalName);
            Assert.Equal("OnSearchBoxClicked", box.Attribute("Click")?.Value);
            Assert.Equal("Center", box.Attribute("HorizontalAlignment")?.Value);
            Assert.Equal("Collapsed", box.Attribute("Visibility")?.Value);

            // 它只是个按钮，不是输入框：搜索页自己有输入框与来源栏。
            Assert.True(!box.Descendants().Any(element => element.Name.LocalName is "TextBox" or "AutoSuggestBox"),
                "标题栏那颗搜索框变成了输入框");
            var text = box.Descendants().Single(element => element.Name.LocalName == "TextBlock");
            Assert.Equal("搜索媒体库、订阅等", text.Attribute("Text")?.Value);
        });
        TestHarness.Test("标题栏收藏：爱心字形默认收着，没有收藏时不出现在那一排", () =>
        {
            var heart = Named(Shell(), "FavoritesButton");
            Assert.Equal("Button", heart.Name.LocalName);
            Assert.Equal("Collapsed", heart.Attribute("Visibility")?.Value);
            Assert.Equal("OnFavoritesClicked", heart.Attribute("Click")?.Value);
            Assert.Equal("ShellPage_FavoritesButton", heart.Attribute(Xaml + "Uid")?.Value);
            // 2026-11-11 用户令「不要星星，改回之前的爱心」：EB51 是 Segoe 的空心心形，和详情页、音乐播放栏、
            // 侧栏收藏入口同一颗；这一排里其余图标仍是 24 网格的 PathIcon，收藏这颗是字体字形。
            var icon = heart.Descendants().Single(element => element.Name.LocalName == "FontIcon");
            Assert.Equal("\uEB51", icon.Attribute("Glyph")?.Value);
            Assert.True(!heart.Descendants().Any(element => element.Name.LocalName == "PathIcon"), "收藏又画成路径图标了");
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
        TestHarness.Test("标题栏前进：按钮已退役，Alt+→ 与鼠标侧键仍走同一条 GoForward", () =>
        {
            var document = Shell();
            // 2026-11-12 晚用户选定「前进键退役」：标题栏不再有那颗按钮，那一格让给折叠与标题。
            // 「刚退回去那一步还能补回来」这件事本身没删 —— 两支快捷键和鼠标侧键仍在，见下一条。
            Assert.True(!document.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "ForwardButton"),
                "前进按钮又回到了标题栏");
            Assert.True(!document.Descendants().Any(element =>
                    element.Name.LocalName == "Button" && element.Attribute(Xaml + "Uid")?.Value == "ShellPage_ForwardButton"),
                "前进按钮的本地化键还在");
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

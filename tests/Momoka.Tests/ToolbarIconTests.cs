using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>标题栏导航的层级与可访问性契约；真实导航和收藏显隐由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("标题栏那一排：主页、收藏、设置、搜索、返回、前进按这个次序", () =>
        {
            var panel = Named(Shell(), "TitleActions");
            var order = panel.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            // 2026-11-11 用户令「左上角统一留主页、收藏（没有收藏时隐藏该按钮）、设置、搜索、返回」，当晚又令
            // 「把左上角的前进键加回去」：这一排不再按页型换脸，声明次序就是视觉次序里的固定几格（收藏自己
            // 收起时不占宽），前进站在返回右边补回刚退回去的那一步。
            Assert.True(order.SequenceEqual(new[]
            {
                "NavToggleButton", "HomeButton", "FavoritesButton", "TitleUtilities", "BackButton", "ForwardButton"
            }), "标题栏这一排的顺序不对：" + string.Join('、', order));

            var utilities = Named(Shell(), "TitleUtilities");
            Assert.Equal("StackPanel", utilities.Name.LocalName);
            var buttons = utilities.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            Assert.True(buttons.SequenceEqual(new[] { "SettingsButton", "SearchButton" }), "工具按钮顺序不对");
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
        TestHarness.Test("标题栏前进：加回的按钮站在返回右边，走不动时只是变暗", () =>
        {
            var forward = Named(Shell(), "ForwardButton");
            Assert.Equal("Button", forward.Name.LocalName);
            Assert.Equal("TitleActions", forward.Parent?.Attribute(Xaml + "Name")?.Value);
            Assert.Equal("OnForwardClicked", forward.Attribute("Click")?.Value);
            Assert.Equal("ShellPage_ForwardButton", forward.Attribute(Xaml + "Uid")?.Value);
            // 与返回同一条规矩：默认停用、走不动时暗着而不是收起 —— 这一排就是窗口挖洞的位置。
            Assert.Equal("False", forward.Attribute("IsEnabled")?.Value);
            Assert.Equal(null, forward.Attribute("Visibility")?.Value);
            Assert.True(forward.Descendants().Any(element => element.Name.LocalName == "PathIcon"), "前进没有画成路径图标");
            Assert.True(!forward.Descendants().Any(element => element.Name.LocalName == "KeyboardAccelerator"),
                "前进按钮自带加速器：按钮停用后 Alt+→ 会一起失灵，快捷键应挂在外壳上");
            foreach (var property in new[]
            {
                "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
                "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip"
            })
            {
                var value = ShellResources().Descendants("data")
                    .FirstOrDefault(element => element.Attribute("name")?.Value == "ShellPage_ForwardButton." + property)
                    ?.Element("value")?.Value;
                Assert.Equal("前进", value);
            }
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

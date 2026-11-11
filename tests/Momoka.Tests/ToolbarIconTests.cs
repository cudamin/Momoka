using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>标题栏导航的层级与可访问性契约；真实导航和收藏显隐由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("标题栏那一排：主页、收藏、设置、搜索、返回按这个次序", () =>
        {
            var panel = Named(Shell(), "TitleActions");
            var order = panel.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            // 2026-11-11 用户令「左上角统一留主页、收藏（没有收藏时隐藏该按钮）、设置、搜索、返回」：这一排
            // 不再按页型换脸，声明次序就是视觉次序里的固定几格（收藏自己收起时不占宽）。
            Assert.True(order.SequenceEqual(new[]
            {
                "NavToggleButton", "HomeButton", "FavoritesButton", "TitleUtilities", "BackButton"
            }), "标题栏这一排的顺序不对：" + string.Join('、', order));

            var utilities = Named(Shell(), "TitleUtilities");
            Assert.Equal("StackPanel", utilities.Name.LocalName);
            var buttons = utilities.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            Assert.True(buttons.SequenceEqual(new[] { "SettingsButton", "SearchButton" }), "工具按钮顺序不对");
        });
        TestHarness.Test("标题栏收藏：空心星标默认收着，没有收藏时不出现在那一排", () =>
        {
            var star = Named(Shell(), "FavoritesButton");
            Assert.Equal("Button", star.Name.LocalName);
            Assert.Equal("Collapsed", star.Attribute("Visibility")?.Value);
            Assert.Equal("OnFavoritesClicked", star.Attribute("Click")?.Value);
            Assert.Equal("ShellPage_FavoritesButton", star.Attribute(Xaml + "Uid")?.Value);
            // 空心星：填充规则 F0 + 内圈镂空，和外圈那条 F1 的实心图标区分开。
            var icon = star.Descendants().Single(element => element.Name.LocalName == "PathIcon");
            Assert.True(icon.Attribute("Data")?.Value.StartsWith("F0 ", StringComparison.Ordinal) == true, "收藏不是空心星标");
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
        TestHarness.Test("标题栏导航：前进按钮与首页/收藏胶囊已退役，快捷键仍在外壳上", () =>
        {
            var document = Shell();
            Assert.True(!document.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "ForwardButton"));
            Assert.True(!document.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "HomeNavigation"));
            // 前进按钮退了役，Alt+→ 与鼠标侧键照旧 —— 加速器挂在外壳上，不跟着按钮一起消失。
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

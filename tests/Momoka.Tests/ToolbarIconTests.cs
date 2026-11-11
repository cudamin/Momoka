using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>标题栏导航的层级与可访问性契约；真实导航和收藏显隐由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("标题栏首页收藏：胶囊在左上角，占的是主页按钮的位置", () =>
        {
            var panel = Named(Shell(), "TitleActions");
            var order = panel.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            var pill = Array.IndexOf(order, "HomeNavigation");
            Assert.True(pill > 0, "标题栏里找不到首页/收藏胶囊");
            // 胶囊 · 后退 · 前进 · 主页：媒体库与详情页收起前进，剩下「后退(左) + 主页(右)」。
            Assert.Equal("BackButton", order[pill + 1]);
            Assert.Equal("ForwardButton", order[pill + 2]);
            Assert.Equal("HomeButton", order[pill + 3]);
            Assert.Equal("NavToggleButton", order[pill - 1]);

            var bar = Named(Shell(), "HomeNavigation");
            Assert.Equal("SelectorBar", bar.Name.LocalName);
            Assert.Equal("Collapsed", bar.Attribute("Visibility")?.Value);
            Assert.Equal("首页与收藏", bar.Attribute("AutomationProperties.Name")?.Value);
            var items = bar.Elements().Where(element => element.Name.LocalName == "SelectorBarItem").ToArray();
            Assert.Equal(2, items.Length);
            Assert.Equal("HomeNavigationHome", items[0].Attribute(Xaml + "Name")?.Value);
            Assert.Equal("HomeNavigationFavorites", items[1].Attribute(Xaml + "Name")?.Value);
            Assert.Equal("首页", items[0].Attribute("Text")?.Value);
            Assert.Equal("收藏", items[1].Attribute("Text")?.Value);
            Assert.Equal("首页", items[0].Attribute("AutomationProperties.Name")?.Value);
            Assert.Equal("收藏", items[1].Attribute("AutomationProperties.Name")?.Value);
        });
        TestHarness.Test("标题栏导航：星标退役而前进按钮回来", () =>
        {
            var document = Shell();
            Assert.True(!document.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "FavoritesButton"));
            var order = Named(document, "TitleActions").Elements()
                .Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            // 后退在前进左边、主页在最右：媒体库那一档收起前进，剩下的正是「后退(左) + 主页(右)」。
            Assert.True(Array.IndexOf(order, "BackButton") < Array.IndexOf(order, "ForwardButton")
                && Array.IndexOf(order, "ForwardButton") < Array.IndexOf(order, "HomeButton"), "三颗按钮的先后不对");
        });
        TestHarness.Test("标题栏工具：设置与搜索排在左上这一排、主页右边", () =>
        {
            var panel = Named(Shell(), "TitleActions");
            var order = panel.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            Assert.True(order.SequenceEqual(new[]
            {
                "NavToggleButton", "HomeNavigation", "BackButton", "ForwardButton", "HomeButton", "TitleUtilities"
            }), "标题栏这一排的顺序不对：" + string.Join('、', order));
            var utilities = Named(Shell(), "TitleUtilities");
            Assert.Equal("StackPanel", utilities.Name.LocalName);
            var buttons = utilities.Elements().Select(element => element.Attribute(Xaml + "Name")?.Value).ToArray();
            Assert.True(buttons.SequenceEqual(new[] { "SettingsButton", "SearchButton" }), "工具按钮顺序不对");
            Assert.Equal("0", Named(Shell(), "AccountButton").Attribute("Grid.Column")?.Value);
        });
        TestHarness.Test("标题栏导航：两颗箭头的快捷键挂在外壳而不是按钮上", () =>
        {
            var document = Shell();
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
}

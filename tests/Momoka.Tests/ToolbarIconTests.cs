using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>标题栏导航的层级与可访问性契约；真实导航和收藏显隐由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("标题栏收藏：入口在左上角且跟随收藏状态隐藏", () =>
        {
            var document = Shell();
            var button = Named(document, "FavoritesButton");
            Assert.Equal("Button", button.Name.LocalName);
            Assert.Equal("TitleActions", button.Parent?.Attribute(Xaml + "Name")?.Value);
            Assert.Equal("{x:Bind Favorites.Visibility, Mode=OneWay}", button.Attribute("Visibility")?.Value);
            Assert.Equal("收藏", button.Attribute("AutomationProperties.Name")?.Value);
            Assert.Equal("收藏", button.Attribute("ToolTipService.ToolTip")?.Value);
            Assert.True(button.Descendants().Any(element => element.Name.LocalName == "PathIcon"));
        });
        TestHarness.Test("标题栏导航：旧胶囊与前进按钮没有残留控件", () =>
        {
            var document = Shell();
            var retired = new[] { "ForwardButton", "HomeNavigation", "HomeNavigationHost", "HomeNavigationHome", "HomeNavigationFavorites" };
            Assert.True(!document.Descendants().Any(element => retired.Contains(element.Attribute(Xaml + "Name")?.Value)));
            Assert.Equal("Button", Named(document, "HomeButton").Name.LocalName);
            Assert.Equal("Button", Named(document, "BackButton").Name.LocalName);
        });
        TestHarness.Test("标题栏导航：前进快捷键独立于已移除的按钮", () =>
        {
            var accelerator = Shell().Descendants().Single(element => element.Name.LocalName == "KeyboardAccelerator"
                && element.Attribute("Key")?.Value == "Right" && element.Attribute("Modifiers")?.Value == "Menu");
            Assert.Equal("UserControl.KeyboardAccelerators", accelerator.Parent?.Name.LocalName);
            Assert.True(!string.IsNullOrWhiteSpace(accelerator.Attribute("Invoked")?.Value));
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

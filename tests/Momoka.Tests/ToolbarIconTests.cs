using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>主页导航的层级与可访问性契约；真实导航、布局与选中态由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("主页导航：首页收藏共用内容层胶囊且移除标题栏爱心", () =>
        {
            var document = Shell();
            Assert.True(!document.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "FavoritesButton"));
            var navigation = Named(document, "HomeNavigation");
            Assert.Equal("SelectorBar", navigation.Name.LocalName);
            Assert.True(navigation.Ancestors().Any(element => element.Attribute(Xaml + "Name")?.Value == "ContentHost"));
            Assert.True(!navigation.Ancestors().Any(element => element.Attribute(Xaml + "Name")?.Value == "AppTitleBar"));
            Assert.Equal("Center", navigation.Attribute("HorizontalAlignment")?.Value);
            var glass = navigation.Descendants().Where(element => element.Name.LocalName == "AcrylicBrush").ToArray();
            Assert.Equal(2, glass.Length);
            Assert.True(glass.All(brush => brush.Attribute("TintOpacity")?.Value == "0.12"
                && brush.Attribute("TintLuminosityOpacity")?.Value == "0.12"));
        });
        TestHarness.Test("主页导航：两个原生选择项有稳定名称和文字", () =>
        {
            var document = Shell();
            foreach (var (name, text) in new[] { ("HomeNavigationHome", "首页"), ("HomeNavigationFavorites", "收藏") })
            {
                var item = Named(document, name);
                Assert.Equal("SelectorBarItem", item.Name.LocalName);
                Assert.Equal(text, item.Attribute("Text")?.Value);
                Assert.Equal(text, item.Attribute("AutomationProperties.Name")?.Value);
            }
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

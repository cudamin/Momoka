using System.Xml.Linq;

namespace Momoka.Tests;

/// <summary>顶部图标的缩放与轮廓契约；真实像素和视觉线重由 Shell 探针验证。</summary>
internal static class ToolbarIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        TestHarness.Test("顶部收藏：爱心与主页和设置共用图标尺寸", () =>
        {
            var document = Shell();
            var heart = IconFrame(document, "FavoritesButton");
            foreach (var name in new[] { "HomeButton", "SettingsButton" })
            {
                var neighbor = IconFrame(document, name);
                Assert.Equal(neighbor.Attribute("Width")?.Value, heart.Attribute("Width")?.Value);
                Assert.Equal(neighbor.Attribute("Height")?.Value, heart.Attribute("Height")?.Value);
            }
            Assert.True(heart.Elements().Single().Name.LocalName == "PathIcon",
                "爱心应使用固定轮廓，避免系统图标字体改变尺寸和线重");
        });
        TestHarness.Test("顶部收藏：爱心保留空心轮廓与按钮状态前景", () =>
        {
            var document = Shell();
            var button = Named(document, "FavoritesButton");
            var icon = IconFrame(document, "FavoritesButton").Elements().Single();
            var data = icon.Attribute("Data")?.Value ?? string.Empty;
            Assert.True(data.StartsWith("F0 ", StringComparison.Ordinal), "两道闭合轮廓以奇偶规则挖空爱心");
            Assert.Equal(2, data.Count(character => character == 'M'));
            Assert.Equal(2, data.Count(character => character == 'Z'));
            Assert.True(icon.Attribute("Foreground") is null, "图标继续继承停用、主题与高对比度前景");
            Assert.Equal("OnFavoritesClicked", button.Attribute("Click")?.Value);
            Assert.Equal("Collapsed", button.Attribute("Visibility")?.Value);
        });
    }

    private static XElement IconFrame(XDocument document, string name)
    {
        var frame = Named(document, name).Elements().Single();
        Assert.Equal("Viewbox", frame.Name.LocalName);
        return frame;
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

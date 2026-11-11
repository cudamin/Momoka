using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MicaSurfaceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Register()
    {
        Test("云母层级：SDK颜色保留透明度，高对比使用系统色", () =>
        {
            var document = Read("Theme/Mica.xaml");
            foreach (var theme in new[] { "Dark", "Light", "HighContrast" })
            {
                var dictionary = document.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == theme);
                foreach (var (key, token) in new[]
                {
                    ("EgMicaCommandBrush", "LayerOnMicaBaseAltFillColorDefault"),
                    ("EgMicaContentBrush", "LayerFillColorDefault"),
                    ("EgMicaCardBrush", "CardBackgroundFillColorDefault")
                })
                {
                    var brush = dictionary.Elements().Single(element => element.Attribute(Xaml + "Key")?.Value == key);
                    Assert.Equal("SolidColorBrush", brush.Name.LocalName);
                    Assert.Equal("{ThemeResource " + (theme == "HighContrast" ? "SystemColorWindowColor" : token) + "}",
                        brush.Attribute("Color")?.Value);
                }
            }
        });
        Test("云母页面：主页影视本地音乐库均保留命令层和内容层", () =>
        {
            foreach (var page in new[] { "HomePage", "LibraryPage", "LocalLibraryPage", "MusicLibraryPage" })
            {
                var document = Read("Views/" + page + ".xaml");
                Assert.Equal("{ThemeResource EgMicaCommandBrush}", document.Root!.Attribute("Background")?.Value);
                Assert.True(document.Descendants().Any(element => element.Attribute("Background")?.Value == "{ThemeResource EgMicaContentBrush}"), page);
                Assert.True(!document.Descendants().Any(element => element.Name.LocalName == "SystemBackdropElement"), page);
            }
        });
        Test("云母设置：页面透明，卡片从局部字典解析", () =>
        {
            var page = Read("Views/SettingsPage.xaml");
            Assert.Equal("Transparent", page.Root!.Attribute("Background")?.Value);
            Assert.True(page.Descendants().Any(element => element.Attribute("Source")?.Value == "ms-appx:///Theme/MicaCards.xaml"));
            var cards = Read("Theme/MicaCards.xaml");
            Assert.True(!cards.Descendants().Any(element => element.Name.LocalName == "AcrylicBrush"));
            foreach (var theme in new[] { "Dark", "Light", "HighContrast" })
            {
                var dictionary = cards.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == theme);
                var brush = dictionary.Elements().Single(element => element.Attribute(Xaml + "Key")?.Value == "CardBackgroundFillColorDefaultBrush");
                Assert.Equal("{ThemeResource " + (theme == "HighContrast" ? "SystemColorWindowColor" : "LayerFillColorDefault") + "}", brush.Attribute("Color")?.Value);
            }
        });
    }

    private static XDocument Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return XDocument.Load(Path.Combine(directory.FullName, "src/Momoka.Shell", relative));
        throw new InvalidOperationException("本树源码缺失");
    }
}

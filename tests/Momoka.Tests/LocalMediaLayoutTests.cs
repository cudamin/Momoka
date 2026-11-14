using System.Xml.Linq;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LocalMediaLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("本机整理卡片：原控件保留页面名称范围、双向输入和事件", () =>
        {
            var page = Page("LocalMediaPage.xaml");
            var cardStyle = page.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == "LocalMediaCardStyle");
            Assert.Equal("Grid", cardStyle.Attribute("TargetType")?.Value);
            foreach (var (cardName, inputs) in new (string, string[])[]
            {
                ("LocalLibraryCard", ["LocalLibraryPicker", "LocalLibraryNew", "LocalLibraryName"]),
                ("LocalMediaRecognitionCard", ["LocalMediaProvider", "LocalMediaMethod", "LocalMediaToken", "LocalMediaSaveToken"]),
                ("LocalMediaDirectoriesCard", ["LocalMediaSource", "LocalMediaTarget", "LocalMediaRecursive"]),
                ("LocalLibraryAutomationCard", ["LocalLibraryMonitor", "LocalLibrarySave", "LocalLibraryRemove", "LocalLibraryStatus"])
            })
            {
                var card = Named(page, cardName);
                Assert.Equal("Grid", card.Name.LocalName);
                Assert.True(card.Descendants().Any(element => element.Name.LocalName == "FontIcon"));
                Assert.True(card.Descendants().Any(element => element.Attribute("AutomationProperties.HeadingLevel")?.Value == "Level2"));
                foreach (var input in inputs)
                {
                    var element = Named(page, input);
                    Assert.True(element.Ancestors().Contains(card), input);
                    Assert.False(element.Ancestors().Any(parent => parent.Name.LocalName == "DataTemplate"), input);
                }
            }
            foreach (var input in new[] { "LocalLibraryName", "LocalMediaSource", "LocalMediaTarget" })
            {
                Assert.True(Named(page, input).Attribute("Text")!.Value.Contains("Mode=TwoWay, UpdateSourceTrigger=PropertyChanged", StringComparison.Ordinal), input);
                Assert.Equal("{StaticResource EgTextBoxStyle}", Named(page, input).Attribute("Style")?.Value);
            }
            foreach (var input in new[] { "LocalLibraryPicker", "LocalMediaProvider", "LocalMediaMethod" })
                Assert.Equal("{StaticResource EgComboBoxStyle}", Named(page, input).Attribute("Style")?.Value);
            Assert.Equal("{x:Bind ViewModel.SelectedLibrary, Mode=TwoWay}", Named(page, "LocalLibraryPicker").Attribute("SelectedItem")?.Value);
            Assert.Equal("{x:Bind ViewModel.ProviderIndex, Mode=TwoWay}", Named(page, "LocalMediaProvider").Attribute("SelectedIndex")?.Value);
            Assert.Equal("{x:Bind ViewModel.MethodIndex, Mode=TwoWay}", Named(page, "LocalMediaMethod").Attribute("SelectedIndex")?.Value);
            Assert.Equal("{x:Bind ViewModel.MonitorEnabled, Mode=TwoWay}", Named(page, "LocalLibraryMonitor").Attribute("IsOn")?.Value);
            Assert.Equal("OnSaveToken", Named(page, "LocalMediaSaveToken").Attribute("Click")?.Value);
            Assert.Equal("OnPickSource", Named(page, "LocalMediaPickSource").Attribute("Click")?.Value);
            Assert.Equal("OnPickTarget", Named(page, "LocalMediaPickTarget").Attribute("Click")?.Value);
            Assert.Equal("{StaticResource DefaultToggleSwitchStyle}", Named(page, "LocalLibraryMonitor").Attribute("Style")?.Value);
        });
        Test("本机整理卡片：从属区域整组收起、结果列表保持单层卡片", () =>
        {
            var page = Page("LocalMediaPage.xaml");
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.IsMomoka), Mode=OneWay}", Named(page, "LocalMediaTmdbCredentials").Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.IsMoviePilot), Mode=OneWay}", Named(page, "LocalMediaMpNotice").Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.NeedsTarget), Mode=OneWay}", Named(page, "LocalMediaTargetRow").Attribute("Visibility")?.Value);
            Assert.True(Named(page, "LocalMediaDirectoriesCard").Attribute("Visibility") is null);
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.ShowCorrection), Mode=OneWay}", Named(page, "LocalMediaCorrection").Parent?.Attribute("Visibility")?.Value);
            var files = Named(page, "LocalMediaFiles");
            Assert.False(files.Ancestors().Any(element => element.Name.LocalName is "Border" or "SettingsCard"));
            Assert.Equal(1, page.Descendants().Count(element => element.Name.LocalName == "ScrollViewer"));
            Assert.Equal("{x:Bind ViewModel.Rows, Mode=OneWay}", files.Attribute("ItemsSource")?.Value);
        });
        Test("设置图标：本机整理有专属语义、诊断通知与控制台可区分", () =>
        {
            var converter = new SettingsCategoryGlyphConverter();
            var fallback = converter.Convert("未知分类", typeof(string), null!, "zh-CN");
            var categories = new[] { "本机整理", "诊断", "通知", "控制台", "字幕", "快捷键" };
            var glyphs = categories.Select(category => (string)converter.Convert(category, typeof(string), null!, "zh-CN")).ToArray();
            Assert.Equal(glyphs.Length, glyphs.Distinct(StringComparer.Ordinal).Count());
            foreach (var glyph in glyphs)
            {
                Assert.False(Equals(fallback, glyph));
                Assert.Equal(1, glyph.Length);
            }
            Assert.Equal(fallback, converter.Convert(null!, typeof(string), null!, "zh-CN"));
            var navigation = Named(Page("SettingsPage.xaml"), "CategoryNavigation");
            var icon = navigation.Descendants().Single(element => element.Name.LocalName == "FontIcon"
                && element.Attribute("Glyph")?.Value == "{x:Bind Label, Converter={StaticResource CategoryGlyph}}");
            Assert.Equal("{x:Bind Label, Converter={StaticResource CategoryGlyph}}", icon.Attribute("Glyph")?.Value);
            Assert.Equal("Raw", icon.Parent?.Attribute("AutomationProperties.AccessibilityView")?.Value);
            Assert.True(icon.Attribute("Foreground") is null, "导航图标应继承选择和高对比度颜色");
        });
    }

    private static XDocument Page(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return XDocument.Load(Path.Combine(directory.FullName, "src", "Momoka.Shell", "Views", file));
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attribute(Xaml + "Name")?.Value == name);
}

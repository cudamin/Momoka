using System.Xml.Linq;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LocalMediaLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("本机整理页面：取消卡片分区，目录列表置顶", () =>
        {
            var page = Page("LocalMediaPage.xaml");
            Assert.False(page.Descendants().Any(element => element.Attribute(Xaml + "Key")?.Value == "LocalMediaCardStyle"),
                "本机整理不应再声明卡片分区样式");
            var surface = Named(page, "LocalMediaSurface");
            Assert.Equal("Border", surface.Name.LocalName);
            Assert.Equal("{ThemeResource CardBackgroundFillColorDefaultBrush}", surface.Attribute("Background")?.Value);
            Assert.True(surface.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == "LocalMediaProvider"),
                "背景卡片没有包住识别区域");
            foreach (var removed in new[] { "LocalLibraryCard", "LocalMediaRecognitionCard", "LocalMediaDirectoriesCard", "LocalLibraryAutomationCard",
                "LocalLibraryPicker", "LocalMediaMethod", "LocalMediaTarget", "LocalMediaPickTarget", "LocalMediaTargetRow" })
                Assert.False(page.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == removed), "旧分区控件仍在：" + removed);
            var names = page.Descendants().Select(element => element.Attribute(Xaml + "Name")?.Value).Where(name => name is not null).ToArray();
            Assert.True(Array.IndexOf(names, "LocalMediaLibraryNew") < Array.IndexOf(names, "LocalMediaProvider"), "添加目录没有排在识别之前");
            Assert.True(Array.IndexOf(names, "LocalMediaLibraryList") < Array.IndexOf(names, "LocalMediaProvider"), "目录列表没有排在识别之前");
            Assert.True(Array.IndexOf(names, "LocalMediaProvider") < Array.IndexOf(names, "LocalMediaMetadataDirectory"), "元数据目录没有排在识别之后");
            var add = Named(page, "LocalMediaLibraryNew");
            Assert.Equal("OnNewLibrary", add.Attribute("Click")?.Value);
            Assert.Equal("{x:Bind ViewModel.Libraries, Mode=OneWay}", Named(page, "LocalMediaLibraryList").Attribute("ItemsSource")?.Value);
        });
        Test("本机整理页面：目录行逐行保存名称、目录与监控", () =>
        {
            var page = Page("LocalMediaPage.xaml");
            var template = page.Descendants().Single(element => element.Attribute(Xaml + "Key")?.Value == "LocalLibraryTemplate");
            foreach (var name in new[] { "LibraryNameBox", "LibraryDirectoryBox", "LibraryPick", "LibraryRecursive", "LibraryMonitor", "LibrarySave", "LibraryRemove", "LibraryStatus" })
                Assert.True(template.Descendants().Any(element => element.Attribute(Xaml + "Name")?.Value == name), "目录行缺少控件：" + name);
            var nameBox = template.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "LibraryNameBox");
            Assert.Equal("媒体库名称", nameBox.Attribute("Header")?.Value);
            Assert.Equal("{x:Bind Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", nameBox.Attribute("Text")?.Value);
            var directory = template.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "LibraryDirectoryBox");
            Assert.Equal("{x:Bind Directory, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", directory.Attribute("Text")?.Value);
            Assert.Equal("OnSaveLibraryRow", template.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "LibrarySave").Attribute("Click")?.Value);
            Assert.Equal("OnRemoveLibraryRow", template.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "LibraryRemove").Attribute("Click")?.Value);
            Assert.Equal("OnPickLibraryDirectory", template.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "LibraryPick").Attribute("Click")?.Value);
            Assert.Equal("{x:Bind MonitorEnabled, Mode=TwoWay}", template.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "LibraryMonitor").Attribute("IsOn")?.Value);
        });
        Test("本机整理页面：API 读访问令牌采用中文名，元数据目录可自定义", () =>
        {
            var page = Page("LocalMediaPage.xaml");
            Assert.Equal("API 读访问令牌", Named(page, "LocalMediaToken").Attribute("Header")?.Value);
            Assert.Equal("{x:Bind ViewModel.MetadataDirectory, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", Named(page, "LocalMediaMetadataDirectory").Attribute("Text")?.Value);
            Assert.Equal("OnSaveMetadataDirectory", Named(page, "LocalMediaMetadataSave").Attribute("Click")?.Value);
            Assert.Equal("OnResetMetadataDirectory", Named(page, "LocalMediaMetadataReset").Attribute("Click")?.Value);
            Assert.Equal("OnPickMetadataDirectory", Named(page, "LocalMediaMetadataPick").Attribute("Click")?.Value);
            Assert.Equal("{x:Bind ViewModel.DefaultMetadataDirectory, Mode=OneWay}", Named(page, "LocalMediaMetadataDefault").Attribute("Text")?.Value);
        });
        Test("本机整理页面：从属区域整组收起、结果列表保持单层卡片", () =>
        {
            var page = Page("LocalMediaPage.xaml");
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.IsMomoka), Mode=OneWay}", Named(page, "LocalMediaTmdbCredentials").Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.IsMoviePilot), Mode=OneWay}", Named(page, "LocalMediaMpNotice").Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.ShowCorrection), Mode=OneWay}", Named(page, "LocalMediaCorrection").Parent?.Attribute("Visibility")?.Value);
            Assert.Equal("{x:Bind local:LocalMediaPage.Show(ViewModel.CanApply), Mode=OneWay}", Named(page, "LocalMediaApply").Attribute("Visibility")?.Value);
            var files = Named(page, "LocalMediaFiles");
            Assert.False(files.Ancestors().Any(element => element.Name.LocalName is "Border" or "SettingsCard"
                && element.Attribute(Xaml + "Name")?.Value != "LocalMediaSurface"), "结果列表不应再套分区卡片");
            Assert.Equal(1, page.Descendants().Count(element => element.Name.LocalName == "ScrollViewer"));
            Assert.Equal("{x:Bind ViewModel.Rows, Mode=OneWay}", files.Attribute("ItemsSource")?.Value);
        });
        Test("本地库播放接线：ShellPage 把条目交给本机播放路径", () =>
        {
            var source = Source("src/Momoka.Shell/Views/ShellPage.xaml.cs");
            Assert.Contains("entry => PlayLocalLibraryEntryAsync(entry)", source);
            Assert.Contains("IsLocal = true", source);
            Assert.Contains("Path = path", source);
            Assert.Contains("await PlayAsync(item)", source);
            var transport = Source("src/Momoka.Shell/ViewModels/PlayerViewModel.Transport.cs");
            Assert.Contains("if (item.IsLocal)", transport);
            Assert.Contains("StartLocalPlaybackAsync", transport);
            Assert.Contains("LocalPlaybackTicket", transport);
            Assert.Contains("_playback.PlayLocalAsync(ticket", transport);
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

    private static string Source(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
                return File.ReadAllText(Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attribute(Xaml + "Name")?.Value == name);
}

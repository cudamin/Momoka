using System.Text.Json;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class RemovedLocalMediaTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        foreach (var legacy in new[] { "null", "{}", "42", "{\"Provider\":\"moviepilot\",\"ProtectedTmdbToken\":\"anonymous-placeholder\",\"Libraries\":[{\"MonitorEnabled\":true,\"SourceDirectory\":\"unused\"}]}" })
        {
            Test("退役本机整理：忽略旧配置且保留其他偏好 " + legacy, () =>
            {
                var json = "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion
                    + ",\"LocalMedia\":" + legacy
                    + ",\"Playback\":{\"ResumeFromSavedPosition\":false},\"Network\":{\"ProxyServer\":\"http://127.0.0.1:8686\"}}";
                var settings = SettingsMigration.FromJson(json, PassthroughSecretProtector.Instance);
                Assert.False(settings.Playback.ResumeFromSavedPosition);
                Assert.Equal("http://127.0.0.1:8686", settings.Network.ProxyServer);
                Assert.False(JsonSerializer.Serialize(settings).Contains("LocalMedia", StringComparison.Ordinal));
                Assert.False(JsonSerializer.Serialize(SettingsPreferences.ToBackupDocument(settings)).Contains("LocalMedia", StringComparison.Ordinal));
            });
        }
        Test("退役本机整理：正常读写保留已有索引和元数据，不再创建默认元数据目录", () =>
        {
            var root = Directory.CreateTempSubdirectory("momoka-retired-local-media-").FullName;
            try
            {
                var paths = new AppPaths(root);
                paths.EnsureCreated();
                var metadata = Path.Combine(root, "metadata");
                Assert.False(Directory.Exists(metadata));
                Directory.CreateDirectory(metadata);
                var poster = Path.Combine(metadata, "sentinel.txt");
                var index = Path.Combine(root, "local-library.json");
                File.WriteAllText(poster, "preserved metadata");
                File.WriteAllText(index, "preserved index");
                File.WriteAllText(paths.SettingsFile, "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion
                    + ",\"LocalMedia\":{\"Libraries\":[{\"SourceDirectory\":\"unused\",\"MonitorEnabled\":true}]},\"Playback\":{\"ResumeFromSavedPosition\":false}}");
                var store = new SettingsStore(paths, PassthroughSecretProtector.Instance);
                var settings = store.LoadStrict();
                Assert.False(settings.Playback.ResumeFromSavedPosition);
                store.Save(settings);
                Assert.False(File.ReadAllText(paths.SettingsFile).Contains("LocalMedia", StringComparison.Ordinal));
                Assert.Equal("preserved index", File.ReadAllText(index));
                Assert.Equal("preserved metadata", File.ReadAllText(poster));
                Assert.False(new SettingsStore(paths, PassthroughSecretProtector.Instance).Load().Playback.ResumeFromSavedPosition);
            }
            finally { Directory.Delete(root, recursive: true); }
        });
        Test("退役本机整理：专用页面后台探针均已移除，服务器整理仍在", () =>
        {
            Assert.Null(typeof(AppSettings).GetProperty("LocalMedia"));
            Assert.False(ShellProbeSections.IsKnown("local-media"));
            var root = Root();
            foreach (var file in new[]
            {
                "src/Momoka.Shell/Views/LocalMediaPage.xaml", "src/Momoka.Shell/Views/LocalLibraryPage.xaml",
                "src/Momoka.Shell/Services/LocalLibraryCoordinator.cs", "src/Momoka.Shell/ShellNavigationProbe.LocalMedia.cs"
            }) Assert.False(File.Exists(Path.Combine(root, file)), file);
            Assert.False(Source("src/Momoka.Shell/ViewModels/SettingsViewModel.cs").Contains("本机整理", StringComparison.Ordinal));
            Assert.False(Source("src/Momoka.Shell/Views/SettingsPage.xaml.cs").Contains("LocalMediaPage", StringComparison.Ordinal));
            Assert.False(Source("src/Momoka.Shell/App.xaml.cs").Contains("LocalLibraryCoordinator", StringComparison.Ordinal));
            Assert.True(Source("src/Momoka.Shell/ShellNavigationProbe.ManualOrganize.cs").Contains("MoviePilotManualOrganizeAsync", StringComparison.Ordinal));
        });
        Test("设置图标：诊断通知与控制台可区分", () =>
        {
            var converter = new SettingsCategoryGlyphConverter();
            var fallback = converter.Convert("未知分类", typeof(string), null!, "zh-CN");
            var categories = new[] { "诊断", "通知", "控制台", "字幕", "快捷键" };
            var glyphs = categories.Select(category => (string)converter.Convert(category, typeof(string), null!, "zh-CN")).ToArray();
            Assert.Equal(glyphs.Length, glyphs.Distinct(StringComparer.Ordinal).Count());
            foreach (var glyph in glyphs)
            {
                Assert.False(Equals(fallback, glyph));
                Assert.Equal(1, glyph.Length);
            }
            Assert.Equal(fallback, converter.Convert(null!, typeof(string), null!, "zh-CN"));
            var page = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/SettingsPage.xaml"));
            var navigation = page.Descendants().Single(element => element.Attribute(Xaml + "Name")?.Value == "CategoryNavigation");
            var icon = navigation.Descendants().Single(element => element.Name.LocalName == "FontIcon"
                && element.Attribute("Glyph")?.Value == "{x:Bind Label, Converter={StaticResource CategoryGlyph}}");
            Assert.Equal("Raw", icon.Parent?.Attribute("AutomationProperties.AccessibilityView")?.Value);
            Assert.True(icon.Attribute("Foreground") is null, "导航图标应继承选择和高对比度颜色");
        });
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(Root(), path));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("未找到当前 Momoka 工作树");
    }
}

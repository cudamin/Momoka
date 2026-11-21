using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Shell.Localization;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class UiLanguageTests
{
    internal static void Register()
    {
        Test("界面语言：默认跟随系统，中文地区回简中，其他语言回英语", () =>
        {
            Assert.Equal(UiLanguages.System, new UiSettings().Language);
            foreach (var language in new[] { "zh", "zh-CN", "zh-TW", "zh-Hant-HK", "ZH-sg" })
                Assert.Equal(UiLanguages.Chinese, UiLanguages.Resolve(UiLanguages.System, language));
            foreach (var language in new[] { "en-US", "en-GB", "ja-JP", "fr-FR", "", null })
                Assert.Equal(UiLanguages.English, UiLanguages.Resolve(UiLanguages.System, language));
            foreach (ushort languageId in new ushort[] { 0x0804, 0x0404, 0x0c04, 0x1004, 0x1404 })
                Assert.Equal(UiLanguages.Chinese, UiLanguages.ResolveWindowsDisplayLanguage(UiLanguages.System, languageId));
            foreach (ushort languageId in new ushort[] { 0x0409, 0x0411, 0x1400, 0x1000, 0 })
            {
                Assert.Equal(UiLanguages.English, UiLanguages.ResolveWindowsDisplayLanguage(UiLanguages.System, languageId));
                Assert.Equal(UiLanguages.Chinese, UiLanguages.ResolveWindowsDisplayLanguage(UiLanguages.Chinese, languageId));
                Assert.Equal(UiLanguages.English, UiLanguages.ResolveWindowsDisplayLanguage(UiLanguages.English, languageId));
            }
        });
        Test("界面语言：手动优先，大小写归一化，非法偏好回系统", () =>
        {
            Assert.Equal(UiLanguages.English, UiLanguages.Resolve(" en-gb ", "zh-CN"));
            Assert.Equal(UiLanguages.Chinese, UiLanguages.Resolve("ZH-hans", "en-US"));
            foreach (var value in new[] { "", "  ", "fr-FR", "english", null })
            {
                Assert.Equal(UiLanguages.System, UiLanguages.Normalize(value));
                Assert.Equal(UiLanguages.Chinese, UiLanguages.Resolve(value, "zh-CN"));
            }
        });
        Test("界面语言：旧配置、保存重读、偏好备份导入和恢复默认", () =>
        {
            var old = SettingsMigration.FromJson("{\"SchemaVersion\":1}", PassthroughSecretProtector.Instance);
            Assert.Equal(UiLanguages.System, old.Ui.Language);
            var source = new AppSettings();
            source.Ui.Language = "EN-us";
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(source), PassthroughSecretProtector.Instance);
            Assert.Equal(UiLanguages.English, restored.Ui.Language);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1500;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(restored));
            Assert.Equal(UiLanguages.English, live.Ui.Language);
            Assert.Equal(1500d, live.Ui.WindowWidth);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal(UiLanguages.System, live.Ui.Language);
            live.Ui.Language = "broken";
            SettingsMigration.Normalize(live);
            Assert.Equal(UiLanguages.System, live.Ui.Language);
        });
        Test("界面语言：中英资源键、非空译文与格式占位符一致", () =>
        {
            var root = Root();
            var chinese = Resources(Path.Combine(root, "src/Momoka.Shell/Strings/zh-Hans/Resources.resw"));
            var english = Resources(Path.Combine(root, "src/Momoka.Shell/Strings/en-US/Resources.resw"));
            Assert.True(chinese.Count > 1000);
            Assert.Equal(string.Join('\n', chinese.Keys.Order()), string.Join('\n', english.Keys.Order()));
            foreach (var entry in chinese)
            {
                Assert.True(!string.IsNullOrWhiteSpace(english[entry.Key]), entry.Key);
                Assert.False(Regex.IsMatch(english[entry.Key], "[\u4e00-\u9fff]"), entry.Key);
                Assert.Equal(Placeholders(entry.Value), Placeholders(english[entry.Key]));
                if (entry.Key.EndsWith(".OffContent", StringComparison.Ordinal) && entry.Value == "关闭")
                    Assert.Equal("Off", english[entry.Key]);
            }
        });
        Test("界面语言：英语标签、中文回退与外部数据保持原样", () =>
        {
            var original = UiText.Language;
            try
            {
                UiText.Language = UiLanguages.English;
                Assert.Equal("Interface language", UiText.Translate("界面语言"));
                Assert.Equal("English", UiText.Translate("English"));
                Assert.Equal("用户自定义电影名称甲", UiText.Translate("用户自定义电影名称甲"));
                Assert.Equal("Interface language\nmpv-option=auto", UiText.Translate("界面语言\nmpv-option=auto"));
                Assert.Equal("Interface language (mpv: video-sync)", UiText.Translate("界面语言（mpv：video-sync）"));
                Assert.Equal("mpv: video-sync", UiText.Translate("mpv：video-sync"));
                Assert.Equal("Unknown 7", UiText.Format("Unknown {0}", 7));
                UiText.Language = UiLanguages.Chinese;
                Assert.Equal("界面语言", UiText.Translate("界面语言"));
            }
            finally { UiText.Language = original; }
        });
        Test("界面语言：播放器导航、队列选片与待播管理有英文文案", () =>
        {
            var original = UiText.Language;
            try
            {
                UiText.Language = UiLanguages.English;
                Assert.Equal("Navigation", UiText.Translate("导航"));
                Assert.Equal("Session play queue", UiText.Translate("本次播放队列"));
                Assert.Equal("Search source", UiText.Translate("搜索来源"));
                Assert.Equal("Add to queue", UiText.Translate("加入本次队列"));
                Assert.Equal("Manage queued items…", UiText.Translate("管理待播项…"));
                Assert.Equal("No queued items", UiText.Translate("没有待播项"));
                Assert.Equal("Move up", UiText.Translate("上移"));
                Assert.Equal("Move down", UiText.Translate("下移"));
                Assert.Equal("Remove", UiText.Translate("移除"));
                UiText.Language = UiLanguages.Chinese;
                Assert.Equal("管理待播项…", UiText.Translate("管理待播项…"));
            }
            finally { UiText.Language = original; }
        });
        Test("界面语言：动态词典没有空译文且格式占位符一致", () =>
        {
            var files = Directory.GetFiles(Path.Combine(Root(), "src/Momoka.Shell/Localization"), "*English.json");
            Assert.True(files.Length >= 2);
            var translations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in document.RootElement.EnumerateObject())
                {
                    Assert.True(keys.Add(entry.Name), "duplicate key in " + file);
                    var value = entry.Value.GetString()!;
                    Assert.True(!string.IsNullOrWhiteSpace(value), entry.Name);
                    Assert.False(Regex.IsMatch(value, "[\u4e00-\u9fff]"), entry.Name);
                    Assert.Equal(Placeholders(entry.Name), Placeholders(value));
                    if (translations.TryGetValue(entry.Name, out var previous)) Assert.Equal(previous, value);
                    else translations.Add(entry.Name, value);
                }
            }
        });
    }

    private static Dictionary<string, string> Resources(string path) => XDocument.Load(path).Descendants("data")
        .ToDictionary(element => (string)element.Attribute("name")!, element => (string)element.Element("value")!, StringComparer.Ordinal);

    private static string Placeholders(string value) => string.Join(',', Regex.Matches(value, @"(?<!\{)\{\d+(?:[^{}]*)?\}(?!\})")
        .Select(match => match.Value).Order(StringComparer.Ordinal));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found");
    }
}

using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Theming;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SystemThemeSettingsTests
{
    internal static void Register()
    {
        Test("系统主题：大小写规整保留跟随语义，默认及已删除主题回退不变", () =>
        {
            foreach (var id in new[] { "system", "SYSTEM" })
            {
                var settings = new AppSettings();
                settings.Ui.Theme = id;
                SettingsMigration.Normalize(settings);
                Assert.Equal(UiThemes.SystemId, settings.Ui.Theme);
            }
            foreach (var id in new[] { "daylight", "不存在" })
            {
                var settings = new AppSettings();
                settings.Ui.Theme = id;
                SettingsMigration.Normalize(settings);
                Assert.Equal(UiThemes.DefaultId, settings.Ui.Theme);
            }
            Assert.Equal(UiThemes.DefaultId, new AppSettings().Ui.Theme);
        });
        Test("系统主题：保存重读和偏好导入保留系统模式及自定义外观，恢复默认不改窗口", () =>
        {
            var source = new AppSettings();
            source.Ui.Theme = UiThemes.SystemId;
            source.Ui.ControlBackgroundColor = "#A1B2C3";
            source.Ui.BackdropColor = "#123456";
            source.Ui.ControlBackgroundOpacity = 37.5;
            source.Ui.BackdropTintOpacity = 15.25;
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(source), PassthroughSecretProtector.Instance);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1510;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(restored));
            Assert.Equal(UiThemes.SystemId, live.Ui.Theme);
            Assert.Equal("#A1B2C3", live.Ui.ControlBackgroundColor);
            Assert.Equal("#123456", live.Ui.BackdropColor);
            Assert.Equal(37.5, live.Ui.ControlBackgroundOpacity);
            Assert.Equal(15.25, live.Ui.BackdropTintOpacity);
            Assert.Equal(1510d, live.Ui.WindowWidth);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal(UiThemes.DefaultId, live.Ui.Theme);
            Assert.Equal(1510d, live.Ui.WindowWidth);
        });
    }
}

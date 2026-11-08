using System.Reflection;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;

namespace Momoka.Tests;

internal static class AppearanceColorSettingsTests
{
    public static void Register()
    {
        TestHarness.Test("外观颜色：缺键和损坏颜色回各自默认，旧透明度原样保留", () =>
        {
            foreach (var raw in new[] { "null", "\"\"", "\"#FFF\"", "\"#FF808080\"", "\"无颜色\"" })
            {
                var settings = SettingsMigration.FromJson("{\"SchemaVersion\":28,\"Ui\":{\"ControlBackgroundColor\":"
                    + raw + ",\"BackdropColor\":" + raw + ",\"ControlBackgroundOpacity\":0,\"BackdropTintOpacity\":15.25}}",
                    PassthroughSecretProtector.Instance);
                Assert.Equal("#808080", Color(settings.Ui, "ControlBackgroundColor"));
                Assert.Equal("#404040", Color(settings.Ui, "BackdropColor"));
                Assert.Equal(0d, settings.Ui.ControlBackgroundOpacity);
                Assert.Equal(15.25, settings.Ui.BackdropTintOpacity);
            }
            var missing = SettingsMigration.FromJson("{\"SchemaVersion\":28}", PassthroughSecretProtector.Instance);
            Assert.Equal("#808080", Color(missing.Ui, "ControlBackgroundColor"));
            Assert.Equal("#404040", Color(missing.Ui, "BackdropColor"));
            Assert.Equal(UiSettings.DefaultBackdropTintOpacity, missing.Ui.BackdropTintOpacity);
        });
        TestHarness.Test("外观颜色：保存、偏好导入与恢复默认保留独立RGB，窗口记录不变", () =>
        {
            var source = SettingsMigration.FromJson("""
                {"SchemaVersion":28,"Ui":{"ControlBackgroundColor":"a1b2c3","BackdropColor":"#123456",
                "ControlBackgroundOpacity":37.5,"BackdropTintOpacity":0,"ControlTintOpacity":100}}
                """, PassthroughSecretProtector.Instance);
            Assert.Equal("#A1B2C3", Color(source.Ui, "ControlBackgroundColor"));
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(source), PassthroughSecretProtector.Instance);
            Assert.Equal("#123456", Color(restored.Ui, "BackdropColor"));
            var live = new AppSettings();
            live.Ui.WindowWidth = 1510;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(restored));
            Assert.Equal("#A1B2C3", Color(live.Ui, "ControlBackgroundColor"));
            Assert.Equal("#123456", Color(live.Ui, "BackdropColor"));
            Assert.Equal(37.5, live.Ui.ControlBackgroundOpacity);
            Assert.Equal(0d, live.Ui.BackdropTintOpacity);
            Assert.Equal(100d, live.Ui.ControlTintOpacity);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal("#808080", Color(live.Ui, "ControlBackgroundColor"));
            Assert.Equal("#404040", Color(live.Ui, "BackdropColor"));
            Assert.Equal(1510d, live.Ui.WindowWidth);
        });
    }

    private static string Color(UiSettings ui, string property)
    {
        var member = typeof(UiSettings).GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(member is not null, "外观颜色缺少持久化属性 " + property);
        return (string)member!.GetValue(ui)!;
    }
}

using System.Reflection;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;

namespace Momoka.Tests;

internal static class AppearanceColorSettingsTests
{
    public static void Register()
    {
        TestHarness.Test("亚克力参数：新建与旧配置缺键四项均默认55%，背景默认取主图", () =>
        {
            foreach (var ui in new[] { new UiSettings(), SettingsMigration.FromJson(
                "{\"SchemaVersion\":26,\"Ui\":{}}", PassthroughSecretProtector.Instance).Ui })
            {
                foreach (var name in TintProperties) Assert.Equal(55d, Tint(ui, name));
                Assert.True(FromArtwork(ui));
            }
        });
        TestHarness.Test("亚克力参数：JSON往返与偏好导入保留独立四参数和颜色来源", () =>
        {
            var source = SettingsMigration.FromJson("""
                {"SchemaVersion":27,"Ui":{"ControlTintOpacity":12.5,"ControlTintLuminosityOpacity":0,
                "BackdropTintOpacity":100,"BackdropTintLuminosityOpacity":78.25,"BackdropTintFromArtwork":false,
                "ControlBackgroundColor":"#A1B2C3","BackdropColor":"#123456"}}
                """, PassthroughSecretProtector.Instance);
            var restored = SettingsMigration.FromJson(JsonSerializer.Serialize(source), PassthroughSecretProtector.Instance);
            var live = new AppSettings();
            live.Ui.WindowWidth = 1510;
            SettingsPreferences.Apply(live, SettingsPreferences.ToBackupDocument(restored));
            foreach (var ui in new[] { source.Ui, restored.Ui, live.Ui })
            {
                Assert.Equal(12.5, Tint(ui, "ControlTintOpacity"));
                Assert.Equal(0d, Tint(ui, "ControlTintLuminosityOpacity"));
                Assert.Equal(100d, Tint(ui, "BackdropTintOpacity"));
                Assert.Equal(78.25, Tint(ui, "BackdropTintLuminosityOpacity"));
                Assert.False(FromArtwork(ui));
                Assert.Equal("#A1B2C3", ui.ControlBackgroundColor);
                Assert.Equal("#123456", ui.BackdropColor);
            }
            SettingsPreferences.Apply(live, new AppSettings());
            foreach (var name in TintProperties) Assert.Equal(55d, Tint(live.Ui, name));
            Assert.True(FromArtwork(live.Ui));
            Assert.Equal(1510d, live.Ui.WindowWidth);
        });
        TestHarness.Test("亚克力参数：有限值夹取0至100，小数与边界保留，无效值恢复55", () =>
        {
            foreach (var name in TintProperties)
                foreach (var (value, expected) in new[]
                {
                    (-1d, 0d), (101d, 100d), (0d, 0d), (100d, 100d), (12.5, 12.5),
                    (double.NaN, 55d), (double.PositiveInfinity, 55d), (double.NegativeInfinity, 55d)
                })
                {
                    var settings = new AppSettings();
                    var member = typeof(UiSettings).GetProperty(name);
                    Assert.NotNull(member, "缺少亚克力参数 " + name);
                    member!.SetValue(settings.Ui, value);
                    SettingsMigration.Normalize(settings);
                    Assert.Equal(expected, Tint(settings.Ui, name));
                    _ = JsonSerializer.Serialize(settings);
                }
        });
        TestHarness.Test("外观颜色：缺键和损坏颜色回各自默认，旧透明度原样保留", () =>
        {
            foreach (var raw in new[] { "null", "\"\"", "\"#FFF\"", "\"#FF808080\"", "\"无颜色\"" })
            {
                var settings = SettingsMigration.FromJson("{\"SchemaVersion\":27,\"Ui\":{\"ControlBackgroundColor\":"
                    + raw + ",\"BackdropColor\":" + raw + ",\"ControlBackgroundOpacity\":0,\"BackdropOpacity\":15.25}}",
                    PassthroughSecretProtector.Instance);
                Assert.Equal("#FFFFFF", Color(settings.Ui, "ControlBackgroundColor"));
                Assert.Equal("#808080", Color(settings.Ui, "BackdropColor"));
                Assert.Equal(0d, settings.Ui.ControlBackgroundOpacity);
                Assert.Equal(15.25, settings.Ui.BackdropOpacity);
            }
            var missing = SettingsMigration.FromJson("{\"SchemaVersion\":27}", PassthroughSecretProtector.Instance);
            Assert.Equal("#FFFFFF", Color(missing.Ui, "ControlBackgroundColor"));
            Assert.Equal("#808080", Color(missing.Ui, "BackdropColor"));
        });
        TestHarness.Test("外观颜色：保存、偏好导入与恢复默认保留独立RGB，窗口记录不变", () =>
        {
            var source = SettingsMigration.FromJson("""
                {"SchemaVersion":27,"Ui":{"ControlBackgroundColor":"a1b2c3","BackdropColor":"#123456",
                "ControlBackgroundOpacity":37.5,"BackdropOpacity":0}}
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
            Assert.Equal(0d, live.Ui.BackdropOpacity);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.Equal("#FFFFFF", Color(live.Ui, "ControlBackgroundColor"));
            Assert.Equal("#808080", Color(live.Ui, "BackdropColor"));
            Assert.Equal(1510d, live.Ui.WindowWidth);
        });
    }

    private static readonly string[] TintProperties =
        ["ControlTintOpacity", "ControlTintLuminosityOpacity", "BackdropTintOpacity", "BackdropTintLuminosityOpacity"];

    private static double Tint(UiSettings ui, string property)
    {
        var member = typeof(UiSettings).GetProperty(property);
        Assert.NotNull(member, "缺少亚克力参数 " + property);
        return (double)member!.GetValue(ui)!;
    }

    private static bool FromArtwork(UiSettings ui)
    {
        var member = typeof(UiSettings).GetProperty("BackdropTintFromArtwork");
        Assert.NotNull(member, "缺少背景染色来源设置");
        return (bool)member!.GetValue(ui)!;
    }

    private static string Color(UiSettings ui, string property)
    {
        var member = typeof(UiSettings).GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
        Assert.True(member is not null, "外观颜色缺少持久化属性 " + property);
        return (string)member!.GetValue(ui)!;
    }
}

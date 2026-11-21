using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class SettingsTests
{
    private static void RegisterHomeLibraryCoverSettings()
    {
        Test("媒体库封面设置：旧配置保持原封面并默认选择叠卡", () =>
        {
            var json = JsonSerializer.Serialize(new { SchemaVersion = AppSettings.CurrentSchemaVersion, Ui = new { ShowHomeBanner = true } });
            var settings = SettingsMigration.FromJson(json, Protector);
            Assert.False(settings.Ui.HomeLibraryCoverOverride);
            Assert.Equal(HomeLibraryCoverPresets.Stack, settings.Ui.HomeLibraryCoverPreset);
            Assert.False(new UiSettings().HomeLibraryCoverOverride);
        });

        Test("媒体库封面设置：四套预设往返保存，关闭覆盖仍保留选择", () =>
        {
            var directory = Directory.CreateTempSubdirectory("momoka-home-cover-settings").FullName;
            try
            {
                var store = new SettingsStore(new AppPaths(directory), Protector);
                foreach (var preset in new[]
                {
                    HomeLibraryCoverPresets.Stack, HomeLibraryCoverPresets.Diagonal,
                    HomeLibraryCoverPresets.Wall, HomeLibraryCoverPresets.Gradient
                })
                {
                    var settings = new AppSettings();
                    settings.Ui.HomeLibraryCoverOverride = true;
                    settings.Ui.HomeLibraryCoverPreset = preset;
                    store.Save(settings);
                    var saved = store.Load();
                    Assert.True(saved.Ui.HomeLibraryCoverOverride);
                    Assert.Equal(preset, saved.Ui.HomeLibraryCoverPreset);

                    saved.Ui.HomeLibraryCoverOverride = false;
                    store.Save(saved);
                    var disabled = store.Load();
                    Assert.False(disabled.Ui.HomeLibraryCoverOverride);
                    Assert.Equal(preset, disabled.Ui.HomeLibraryCoverPreset);
                }
            }
            finally { Directory.Delete(directory, recursive: true); }
        });

        Test("媒体库封面设置：空值和未知预设回到叠卡，不改变开关", () =>
        {
            foreach (var value in new string?[] { null, "", "unknown-preset", "STACK" })
            {
                var settings = new AppSettings();
                settings.Ui.HomeLibraryCoverOverride = true;
                settings.Ui.HomeLibraryCoverPreset = value!;
                SettingsMigration.Normalize(settings);
                Assert.True(settings.Ui.HomeLibraryCoverOverride);
                Assert.Equal(HomeLibraryCoverPresets.Stack, settings.Ui.HomeLibraryCoverPreset);
            }
        });

        Test("媒体库封面设置：备份恢复和默认就地覆盖偏好，保留窗口位置", () =>
        {
            var source = new AppSettings();
            source.Ui.HomeLibraryCoverOverride = true;
            source.Ui.HomeLibraryCoverPreset = HomeLibraryCoverPresets.Wall;
            var backup = SettingsPreferences.ToBackupDocument(source);
            var live = new AppSettings();
            var ui = live.Ui;
            ui.WindowLeft = 137;
            ui.WindowTop = 81;

            SettingsPreferences.Apply(live, backup);
            Assert.True(ReferenceEquals(ui, live.Ui));
            Assert.True(ui.HomeLibraryCoverOverride);
            Assert.Equal(HomeLibraryCoverPresets.Wall, ui.HomeLibraryCoverPreset);
            Assert.Equal(137, ui.WindowLeft);
            Assert.Equal(81, ui.WindowTop);

            SettingsReset.Restore(live);
            Assert.True(ReferenceEquals(ui, live.Ui));
            Assert.False(ui.HomeLibraryCoverOverride);
            Assert.Equal(HomeLibraryCoverPresets.Stack, ui.HomeLibraryCoverPreset);
            Assert.Equal(137, ui.WindowLeft);
            Assert.Equal(81, ui.WindowTop);
        });
    }
}

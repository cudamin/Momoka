using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class SettingsTests
{
    private static void RegisterReviewMigration()
    {
        Test("迁移：染色参数键保持大小写兼容及有效小数", () =>
        {
            var loaded = SettingsMigration.FromJson("""{"SchemaVersion":28,"ui":{"backdroptintopacity":37.5}}""", Protector);
            Assert.Equal(37.5d, loaded.Ui.BackdropTintOpacity);
        });
        Test("迁移：已退役的背景浓度与效果键被忽略，不写回保存文档", () =>
        {
            var loaded = SettingsMigration.FromJson(
                """{"SchemaVersion":26,"Ui":{"BackdropOpacity":37.5,"UseGaussianBackdrop":true}}""", Protector);
            var saved = JsonSerializer.Serialize(loaded, SettingsSerializer.WriteOptions);
            foreach (var key in new[] { "BackdropOpacity", "UseGaussianBackdrop", "UseAcrylicBackdrop" })
                Assert.False(saved.Contains(key, StringComparison.OrdinalIgnoreCase), "迁移后仍在写退役键：" + key);
        });
        foreach (var schema in new[] { 1, 2, 13, 17, 26, 27 })
        {
            foreach (var section in new[] { "", ",\"Ui\":{}", ",\"Ui\":null" })
            {
                Test($"迁移：schema{schema}染色缺值{section}保持各自出厂值", () =>
                {
                    var json = "{\"SchemaVersion\":" + schema + section + "}";
                    var loaded = SettingsMigration.FromJson(json, Protector);
                    Assert.Equal(UiSettings.DefaultBackdropTintOpacity, loaded.Ui.BackdropTintOpacity);
                    Assert.Equal(UiSettings.DefaultBackdropTintLuminosityOpacity, loaded.Ui.BackdropTintLuminosityOpacity);
                    var saved = JsonSerializer.Serialize(loaded, SettingsSerializer.WriteOptions);
                    Assert.Equal(UiSettings.DefaultBackdropTintOpacity,
                        SettingsMigration.FromJson(saved, Protector).Ui.BackdropTintOpacity);
                });
            }
        }

        foreach (var schema in new[] { 12, 13, 17, 18 })
        {
            Test($"迁移：schema{schema}空播放节保留其他设置与主文件", () =>
            {
                var directory = Directory.CreateTempSubdirectory("momoka-review-null-playback").FullName;
                try
                {
                    var paths = new AppPaths(directory);
                    var json = "{\"SchemaVersion\":" + schema
                        + ",\"Playback\":null,\"DeviceId\":\"review-device\",\"Ui\":{\"PageSize\":77}}";
                    File.WriteAllText(paths.SettingsFile, json);
                    var loaded = new SettingsStore(paths, Protector).Load();
                    Assert.Equal(77, loaded.Ui.PageSize);
                    Assert.Equal("review-device", loaded.DeviceId);
                    Assert.True(loaded.Playback is not null);
                    Assert.Equal(json, File.ReadAllText(paths.SettingsFile));
                    Assert.Equal(0, Directory.GetFiles(directory, "*corrupt*").Length);
                }
                finally { Directory.Delete(directory, recursive: true); }
            });
        }
    }
}

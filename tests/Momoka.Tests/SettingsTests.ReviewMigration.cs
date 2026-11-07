using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class SettingsTests
{
    private static void RegisterReviewMigration()
    {
        Test("迁移：旧背景读数保持大小写兼容及有效小数", () =>
        {
            var loaded = SettingsMigration.FromJson("""{"SchemaVersion":26,"ui":{"backdropOpacity":37.5}}""", Protector);
            Assert.Equal(62.5d, loaded.Ui.BackdropOpacity);
        });
        foreach (var schema in new[] { 1, 2, 13, 17, 26, 27 })
        {
            foreach (var section in new[] { "", ",\"Ui\":{}", ",\"Ui\":null" })
            {
                Test($"迁移：schema{schema}背景缺值{section}使用当前默认20", () =>
                {
                    var json = "{\"SchemaVersion\":" + schema + section + "}";
                    var loaded = SettingsMigration.FromJson(json, Protector);
                    Assert.Equal(20d, loaded.Ui.BackdropOpacity);
                    var saved = JsonSerializer.Serialize(loaded, SettingsSerializer.WriteOptions);
                    Assert.Equal(20d, SettingsMigration.FromJson(saved, Protector).Ui.BackdropOpacity);
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

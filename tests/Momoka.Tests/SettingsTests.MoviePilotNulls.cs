using System.Text.Json.Nodes;
using Momoka.Configuration;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class SettingsTests
{
    private static void RegisterMoviePilotNulls()
    {
        foreach (var enabled in new[] { false, true })
        {
            Test($"R02-003：MoviePilot启用{enabled}的null密文隔离并回到默认", () =>
                WithR007Store((store, protector) =>
                {
                    var damaged = MoviePilotNullDocument(enabled);
                    File.WriteAllText(store.Paths.SettingsFile, damaged);
                    var loaded = store.Load();
                    Assert.False(loaded.MoviePilot.HasSavedPassword);
                    Assert.Equal(100, loaded.Ui.PageSize);
                    AssertR007Quarantine(store.Paths, damaged);
                    Assert.Equal(0, protector.ProtectCalls);
                    Assert.Equal(0, protector.UnprotectCalls);
                }));

            Test($"R02-003：MoviePilot启用{enabled}的null密文不遮住有效备份", () =>
                WithR007Store((store, protector) =>
                {
                    var damaged = MoviePilotNullDocument(enabled);
                    var backupSettings = R007Settings("backup", 220);
                    backupSettings.MoviePilot.ProtectedPassword = "fake:moviepilot-password";
                    var backup = R007Document(backupSettings);
                    File.WriteAllText(store.Paths.SettingsFile, damaged);
                    File.WriteAllText(store.Paths.SettingsBackupFile, backup);
                    var loaded = store.Load();
                    Assert.Equal("device-backup", loaded.DeviceId);
                    Assert.Equal(220, loaded.Ui.PageSize);
                    Assert.True(loaded.MoviePilot.HasSavedPassword);
                    Assert.Equal("fake:moviepilot-password", loaded.MoviePilot.ProtectedPassword);
                    AssertR007Quarantine(store.Paths, damaged);
                    Assert.Equal(backup, File.ReadAllText(store.Paths.SettingsBackupFile));
                    Assert.Equal(0, protector.ProtectCalls);
                    Assert.Equal(0, protector.UnprotectCalls);
                    Assert.True(store.Load().MoviePilot.HasSavedPassword);
                }));

            Test($"R02-003：MoviePilot启用{enabled}的null密文严格读取拒绝且不改输入", () =>
                WithR007Store((store, protector) =>
                {
                    var damaged = MoviePilotNullDocument(enabled);
                    File.WriteAllText(store.Paths.SettingsFile, damaged);
                    Assert.Throws<InvalidDataException>(() => SettingsMigration.FromJson(damaged, protector));
                    Assert.Throws<InvalidDataException>(() => store.LoadStrict());
                    Assert.Throws<InvalidDataException>(() => store.ReadBackup(store.Paths.SettingsFile));
                    Assert.Equal(damaged, File.ReadAllText(store.Paths.SettingsFile));
                    Assert.Equal(0, Directory.GetFiles(store.Paths.Root, "*.corrupt-*").Length);
                }));

            foreach (var field in new[] { "", ",\"ProtectedPassword\":\"\"" })
            {
                Test($"R02-003：MoviePilot启用{enabled}的缺键或空密文{field}保持有效", () =>
                    WithR007Store((store, protector) =>
                    {
                        var json = "{\"SchemaVersion\":" + AppSettings.CurrentSchemaVersion
                            + ",\"DeviceId\":\"keep-device\",\"Ui\":{\"PageSize\":180},\"MoviePilot\":{\"Enabled\":"
                            + (enabled ? "true" : "false") + field + "}}";
                        File.WriteAllText(store.Paths.SettingsFile, json);
                        var loaded = store.Load();
                        Assert.Equal(180, loaded.Ui.PageSize);
                        Assert.Equal("keep-device", loaded.DeviceId);
                        Assert.Equal(enabled, loaded.MoviePilot.Enabled);
                        Assert.False(loaded.MoviePilot.HasSavedPassword);
                        Assert.Equal(json, File.ReadAllText(store.Paths.SettingsFile));
                        Assert.Equal(0, Directory.GetFiles(store.Paths.Root, "*.corrupt-*").Length);
                    }));
            }
        }
    }

    private static string MoviePilotNullDocument(bool enabled)
    {
        var document = JsonNode.Parse(R007Document(R007Settings("main", 180)))!.AsObject();
        document["MoviePilot"]!["Enabled"] = enabled;
        document["MoviePilot"]!["ProtectedPassword"] = null;
        return document.ToJsonString(SettingsSerializer.WriteOptions);
    }
}

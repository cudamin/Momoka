using Momoka.Configuration;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class SettingsTests
{
    private static void RegisterDirectoryMigration()
    {
        Test("数据迁移：首个主文件被锁时继续可读候选，不带入失败来源的备份", () =>
        {
            var root = Directory.CreateTempSubdirectory("momoka-migration").FullName;
            try
            {
                var first = Path.Combine(root, "first");
                var second = Path.Combine(root, "second");
                Directory.CreateDirectory(first);
                Directory.CreateDirectory(second);
                var lockedFile = Path.Combine(first, "settings.json");
                File.WriteAllText(lockedFile, "{\"DeviceId\":\"first\"}");
                File.WriteAllText(Path.Combine(first, "settings.backup.json"), "{\"DeviceId\":\"wrong-backup\"}");
                File.WriteAllText(Path.Combine(first, "first-only.txt"), "failed-source");
                const string wanted = "{\"SchemaVersion\":27,\"DeviceId\":\"second\",\"Ui\":{\"PageSize\":77}}";
                File.WriteAllText(Path.Combine(second, "settings.json"), wanted);
                var paths = new AppPaths(Path.Combine(root, "current"));
                var store = new SettingsStore(paths, Protector);
                using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.Equal(second, store.PrepareStartupData([first, second]));
                    Assert.Equal(wanted, File.ReadAllText(paths.SettingsFile));
                    Assert.False(File.Exists(paths.SettingsBackupFile));
                    Assert.False(File.Exists(Path.Combine(paths.Root, "first-only.txt")));
                    var loaded = store.Load();
                    Assert.Equal("second", loaded.DeviceId);
                    Assert.Equal(77, loaded.Ui.PageSize);
                    Assert.Equal(0, Directory.GetFiles(paths.Root, "*.tmp").Length);
                }
                Assert.Equal("{\"DeviceId\":\"first\"}", File.ReadAllText(lockedFile));
            }
            finally { Directory.Delete(root, recursive: true); }
        });

        Test("数据迁移：主文件失败不留下目标，解锁后仍可重试同一来源", () =>
        {
            var root = Directory.CreateTempSubdirectory("momoka-migration").FullName;
            try
            {
                var prior = Path.Combine(root, "prior");
                Directory.CreateDirectory(prior);
                var source = Path.Combine(prior, "settings.json");
                const string contents = "{\"DeviceId\":\"retry\"}";
                File.WriteAllText(source, contents);
                var paths = new AppPaths(Path.Combine(root, "current"));
                using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.True(paths.MigrateFrom(prior) is null);
                    Assert.False(File.Exists(paths.SettingsFile));
                    Assert.Equal(0, Directory.GetFiles(paths.Root, "*.tmp").Length);
                }
                Assert.Equal(prior, paths.MigrateFrom(prior));
                Assert.Equal(contents, File.ReadAllText(paths.SettingsFile));
                Assert.Equal(contents, File.ReadAllText(source));
            }
            finally { Directory.Delete(root, recursive: true); }
        });

        Test("数据迁移：主文件成功后附属目录冲突不撤销迁移结果", () =>
        {
            var root = Directory.CreateTempSubdirectory("momoka-migration").FullName;
            try
            {
                var prior = Path.Combine(root, "prior");
                Directory.CreateDirectory(Path.Combine(prior, "cache"));
                const string contents = "{\"DeviceId\":\"complete\"}";
                File.WriteAllText(Path.Combine(prior, "settings.json"), contents);
                File.WriteAllText(Path.Combine(prior, "cache", "image"), "optional");
                var paths = new AppPaths(Path.Combine(root, "current"));
                Directory.CreateDirectory(paths.Root);
                File.WriteAllText(Path.Combine(paths.Root, "cache"), "existing-file");
                Assert.Equal(prior, paths.MigrateFrom(prior));
                Assert.Equal(contents, File.ReadAllText(paths.SettingsFile));
                Assert.Equal("existing-file", File.ReadAllText(Path.Combine(paths.Root, "cache")));
                Assert.Equal(0, Directory.GetFiles(paths.Root, "*.tmp").Length);
            }
            finally { Directory.Delete(root, recursive: true); }
        });

        Test("数据迁移：主文件目标是目录时不误报成功或复制附属文件", () =>
        {
            var root = Directory.CreateTempSubdirectory("momoka-migration").FullName;
            try
            {
                var prior = Path.Combine(root, "prior");
                Directory.CreateDirectory(prior);
                File.WriteAllText(Path.Combine(prior, "settings.json"), "{}");
                File.WriteAllText(Path.Combine(prior, "side.txt"), "optional");
                var paths = new AppPaths(Path.Combine(root, "current"));
                Directory.CreateDirectory(paths.SettingsFile);
                Assert.True(paths.MigrateFrom(prior) is null);
                Assert.True(Directory.Exists(paths.SettingsFile));
                Assert.False(File.Exists(Path.Combine(paths.Root, "side.txt")));
                Assert.Equal(0, Directory.GetFiles(paths.Root, "*.tmp").Length);
            }
            finally { Directory.Delete(root, recursive: true); }
        });
    }
}

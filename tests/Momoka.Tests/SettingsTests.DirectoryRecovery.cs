using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class SettingsTests
{
    private static void RegisterDirectoryRecovery()
    {
        foreach (var autoConnect in new[] { false, true })
            Test($"R-009：当前备份优先于旧主文件并保留选择与自动连接={autoConnect}", () =>
            {
                var fixture = NewDirectoryRecovery();
                var backup = RecoveryDocument("current", 240, autoConnect);
                var old = RecoveryDocument("old", 80, !autoConnect);
                File.WriteAllText(fixture.Paths.SettingsBackupFile, backup);
                File.WriteAllText(fixture.Legacy.SettingsFile, old);
                File.WriteAllText(Path.Combine(fixture.Legacy.Root, "old-only.txt"), "must not copy");

                Assert.Null(fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
                AssertRecoveryChoice(fixture.Store.Load(), "current", 240, autoConnect);
                Assert.False(File.Exists(fixture.Paths.SettingsFile));
                Assert.False(File.Exists(Path.Combine(fixture.Paths.Root, "old-only.txt")));
                Assert.Equal(backup, File.ReadAllText(fixture.Paths.SettingsBackupFile));
                Assert.Equal(old, File.ReadAllText(fixture.Legacy.SettingsFile));
            });

        Test("R-009：坏主已隔离但还没保存，重启迁移仍选择当前备份", () =>
        {
            var fixture = NewDirectoryRecovery();
            const string damaged = "{broken-main";
            var backup = RecoveryDocument("current", 240, false);
            File.WriteAllText(fixture.Paths.SettingsFile, damaged);
            File.WriteAllText(fixture.Paths.SettingsBackupFile, backup);
            File.WriteAllText(fixture.Legacy.SettingsFile, RecoveryDocument("old", 80, true));

            AssertRecoveryChoice(fixture.Store.Load(), "current", 240, false);
            Assert.False(File.Exists(fixture.Paths.SettingsFile));
            var preserved = Directory.GetFiles(fixture.Paths.Root, "settings.json.corrupt-*");
            Assert.Equal(1, preserved.Length);
            Assert.Equal(damaged, File.ReadAllText(preserved[0]));
            Assert.Null(fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
            AssertRecoveryChoice(new SettingsStore(fixture.Paths, Protector).Load(), "current", 240, false);
            Assert.Equal(backup, File.ReadAllText(fixture.Paths.SettingsBackupFile));
            Assert.Equal(damaged, File.ReadAllText(preserved[0]));
        });

        Test("R-009：多个旧目录候选不能遮蔽当前可读备份", () =>
        {
            var fixture = NewDirectoryRecovery();
            var second = new AppPaths(Path.Combine(fixture.Root, "legacy-second"));
            Directory.CreateDirectory(second.Root);
            File.WriteAllText(fixture.Paths.SettingsBackupFile, RecoveryDocument("current", 240, false));
            File.WriteAllText(fixture.Legacy.SettingsFile, RecoveryDocument("old", 80, true));
            File.WriteAllText(second.SettingsFile, RecoveryDocument("older", 60, true));
            Assert.Null(fixture.Store.PrepareStartupData([fixture.Legacy.Root, second.Root]));
            AssertRecoveryChoice(fixture.Store.Load(), "current", 240, false);
        });

        Test("R-009：已有当前主文件仍优先，主备和旧源都不改写", () =>
        {
            var fixture = NewDirectoryRecovery();
            var main = RecoveryDocument("main", 300, true);
            var backup = RecoveryDocument("current", 240, false);
            var old = RecoveryDocument("old", 80, false);
            File.WriteAllText(fixture.Paths.SettingsFile, main);
            File.WriteAllText(fixture.Paths.SettingsBackupFile, backup);
            File.WriteAllText(fixture.Legacy.SettingsFile, old);
            Assert.Null(fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
            AssertRecoveryChoice(fixture.Store.Load(), "main", 300, true);
            Assert.Equal(main, File.ReadAllText(fixture.Paths.SettingsFile));
            Assert.Equal(backup, File.ReadAllText(fixture.Paths.SettingsBackupFile));
            Assert.Equal(old, File.ReadAllText(fixture.Legacy.SettingsFile));
        });

        foreach (var damaged in new[] { "", "{bad-json", "[]", "null", "7", "\"text\"" })
            Test("R-009：无效备份不封死旧目录迁移：" + damaged, () =>
            {
                var fixture = NewDirectoryRecovery();
                var old = RecoveryDocument("old", 80, false);
                File.WriteAllText(fixture.Paths.SettingsBackupFile, damaged);
                File.WriteAllText(fixture.Legacy.SettingsFile, old);
                Assert.Equal(fixture.Legacy.Root, fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
                AssertRecoveryChoice(fixture.Store.Load(), "old", 80, false);
                Assert.Equal(damaged, File.ReadAllText(fixture.Paths.SettingsBackupFile));
                Assert.Equal(old, File.ReadAllText(fixture.Legacy.SettingsFile));
            });

        Test("R-009：null 必需字段的不可用备份仍允许迁移旧有效主文件", () =>
        {
            var fixture = NewDirectoryRecovery();
            var damaged = RecoveryDocument("current", 240, false).Replace("\"UserId\":\"remote-current\"", "\"UserId\":null", StringComparison.Ordinal);
            Assert.Contains("\"UserId\":null", damaged);
            File.WriteAllText(fixture.Paths.SettingsBackupFile, damaged);
            File.WriteAllText(fixture.Legacy.SettingsFile, RecoveryDocument("old", 80, true));
            Assert.Equal(fixture.Legacy.Root, fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
            AssertRecoveryChoice(fixture.Store.Load(), "old", 80, true);
            Assert.Equal(damaged, File.ReadAllText(fixture.Paths.SettingsBackupFile));
        });

        Test("R-009：备份暂不可读时迁移旧有效配置，不消费被锁的文件", () =>
        {
            var fixture = NewDirectoryRecovery();
            var backup = RecoveryDocument("current", 240, false);
            File.WriteAllText(fixture.Paths.SettingsBackupFile, backup);
            File.WriteAllText(fixture.Legacy.SettingsFile, RecoveryDocument("old", 80, true));
            using (var locked = new FileStream(fixture.Paths.SettingsBackupFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(fixture.Legacy.Root, fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
                AssertRecoveryChoice(fixture.Store.Load(), "old", 80, true);
            }
            Assert.Equal(backup, File.ReadAllText(fixture.Paths.SettingsBackupFile));
        });

        Test("R-009：新装无当前主备仍复制旧目录，保留旧源与附属缓存", () =>
        {
            var fixture = NewDirectoryRecovery();
            var old = RecoveryDocument("old", 80, true);
            File.WriteAllText(fixture.Legacy.SettingsFile, old);
            var sourceCache = Path.Combine(fixture.Legacy.Root, "cache");
            Directory.CreateDirectory(sourceCache);
            File.WriteAllText(Path.Combine(sourceCache, "only.txt"), "synthetic cache");
            Assert.Equal(fixture.Legacy.Root, fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
            AssertRecoveryChoice(fixture.Store.Load(), "old", 80, true);
            Assert.Equal(old, File.ReadAllText(fixture.Legacy.SettingsFile));
            Assert.Equal("synthetic cache", File.ReadAllText(Path.Combine(fixture.Paths.Root, "cache", "only.txt")));
            Assert.Null(fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
        });

        Test("R-009：无有效主备也无旧配置时默认可读，保留无效备份供恢复", () =>
        {
            var fixture = NewDirectoryRecovery();
            File.WriteAllText(fixture.Paths.SettingsBackupFile, "{bad-json");
            Assert.Null(fixture.Store.PrepareStartupData([fixture.Legacy.Root]));
            Assert.Equal(100, fixture.Store.Load().Ui.PageSize);
            Assert.Equal("{bad-json", File.ReadAllText(fixture.Paths.SettingsBackupFile));
            Assert.False(File.Exists(fixture.Paths.SettingsFile));
        });
    }

    private static DirectoryRecoveryFixture NewDirectoryRecovery()
    {
        // All fake source/target files stay below the current tree's ignored test output; retain them.
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "r009", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(Path.Combine(root, "current"));
        var legacy = new AppPaths(Path.Combine(root, "legacy"));
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(legacy.Root);
        return new(root, paths, legacy, new SettingsStore(paths, Protector));
    }

    private static string RecoveryDocument(string identity, int pageSize, bool autoConnect)
    {
        var account = new AccountProfile
        {
            Id = "account-" + identity,
            Username = identity,
            UserId = "remote-" + identity,
            AutoConnect = autoConnect
        };
        var server = new ServerProfile
        {
            Id = "server-" + identity,
            Name = identity,
            Url = "http://configuration.invalid:8096",
            Accounts = [account]
        };
        return JsonSerializer.Serialize(new AppSettings
        {
            Servers = [server],
            DeviceId = "device-" + identity,
            LastServerId = server.Id,
            LastAccountId = account.Id,
            Ui = new UiSettings { PageSize = pageSize }
        });
    }

    private static void AssertRecoveryChoice(AppSettings settings, string identity, int pageSize, bool autoConnect)
    {
        Assert.Equal(pageSize, settings.Ui.PageSize);
        Assert.Equal("device-" + identity, settings.DeviceId);
        Assert.Equal("server-" + identity, settings.LastServerId);
        Assert.Equal("account-" + identity, settings.LastAccountId);
        Assert.Equal(identity, settings.ResolveLastAccount()!.Username);
        Assert.Equal<bool?>(autoConnect, settings.ResolveLastAccount()!.AutoConnect);
    }

    private sealed record DirectoryRecoveryFixture(string Root, AppPaths Paths, AppPaths Legacy, SettingsStore Store);
}

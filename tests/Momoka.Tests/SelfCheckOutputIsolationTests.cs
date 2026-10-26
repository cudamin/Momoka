using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class SelfCheckOutputIsolationTests
{
    private static readonly ISecretProtector Protector = new NoSecretsProtector();

    public static void Register()
    {
        Test("自检输出：真实复制、严格重载和建目录不触碰源或外部截图目录", () => SelfCheckTests.WithFixture((source, root) =>
        {
            var external = Path.Combine(root, "outside-run", "custom", "screenshots");
            WriteSettings(source, external);
            var before = Snapshot(source.Root);
            var primaryAttributes = File.GetAttributes(source.SettingsFile);
            var backupAttributes = File.GetAttributes(source.SettingsBackupFile);
            File.SetAttributes(source.SettingsFile, primaryAttributes | FileAttributes.ReadOnly);
            File.SetAttributes(source.SettingsBackupFile, backupAttributes | FileAttributes.ReadOnly);
            try
            {
                // Holding read-only handles rules out write-then-restore, not just a final byte comparison.
                using var primary = new FileStream(source.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var backup = new FileStream(source.SettingsBackupFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var run = SelfCheckRun.Create(source, Path.Combine(root, "isolated"), Protector);
                var copied = new SettingsStore(run.Paths, Protector).LoadStrict();
                var resolved = AppPaths.ResolveScreenshotDirectory(copied.Mpv.ScreenshotDirectory, run.Paths.ScreenshotDirectory)!;
                Assert.True(IsWithin(resolved, root), "修前复现也只能写本树的假目录");
                Directory.CreateDirectory(resolved); // same resolver and IO as App startup; no WinUI/native call
                var reportPath = Path.Combine(run.Paths.LogDirectory, "selfcheck-output-isolation.txt");
                AtomicFile.WriteAllText(reportPath, "[通过] 假配置输出边界\n", new UTF8Encoding(false));

                Assert.False(Directory.Exists(Path.Combine(root, "outside-run")), "外部路径和父目录都不能创建");
                Assert.Equal(run.Paths.ScreenshotDirectory, resolved);
                Assert.Equal(run.Paths.ScreenshotDirectory, run.Settings.Mpv.ScreenshotDirectory);
                Assert.Equal(run.Paths.ScreenshotDirectory, copied.Mpv.ScreenshotDirectory, "重定向必须已写入副本");
                Assert.False(copied.Ui.WindowMaximized, "DI 再严格加载不能拿回最大化设置");
                Assert.Equal("webp", copied.Mpv.ScreenshotFormat);
                Assert.Equal(41, copied.Mpv.ScreenshotWebpQuality);
                Assert.Equal("opaque-placeholder", copied.Servers[0].Accounts[0].ProtectedAccessToken);
                Assert.Equal("opaque-password-placeholder", copied.Servers[0].Accounts[0].ProtectedPassword);
                Assert.Equal("[通过] 假配置输出边界\n", File.ReadAllText(reportPath));
                Assert.True(IsWithin(reportPath, run.Paths.Root));
                new SettingsStore(run.Paths, Protector).Save(copied);
                Assert.Equal(before, Snapshot(source.Root), "原配置、备份、缓存字节和目录不变");
                Assert.True((File.GetAttributes(source.SettingsFile) & FileAttributes.ReadOnly) != 0);
                Assert.True((File.GetAttributes(source.SettingsBackupFile) & FileAttributes.ReadOnly) != 0);
            }
            finally
            {
                File.SetAttributes(source.SettingsFile, primaryAttributes);
                File.SetAttributes(source.SettingsBackupFile, backupAttributes);
            }
        }));

        Test("自检输出：生产根内的自定义子目录也不能被创建", () => SelfCheckTests.WithFixture((source, root) =>
        {
            var custom = Path.Combine(source.Root, "not-created", "screenshots");
            WriteSettings(source, custom);
            var before = Snapshot(source.Root);
            using var run = SelfCheckRun.Create(source, Path.Combine(root, "production-path"), Protector);
            var copied = new SettingsStore(run.Paths, Protector).LoadStrict();
            var resolved = AppPaths.ResolveScreenshotDirectory(copied.Mpv.ScreenshotDirectory, run.Paths.ScreenshotDirectory)!;
            Assert.True(IsWithin(resolved, root));
            Directory.CreateDirectory(resolved);
            Assert.False(Directory.Exists(Path.Combine(source.Root, "not-created")));
            Assert.Equal(before, Snapshot(source.Root));
        }));

        Test("自检输出：空白、引号、相对、网络及畸形配置均不参与落点解析", () => SelfCheckTests.WithFixture((source, root) =>
        {
            var external = Path.Combine(root, "never-created", "screenshots");
            var configuredPaths = new[]
            {
                external, "  \"" + external + "\"  ",
                Path.GetRelativePath(Environment.CurrentDirectory, external),
                "..\\outside-selfcheck\\screenshots", "", "  ",
                @"\\fixture.invalid\share\screenshots", @"\\?\C:\fixture\screenshots",
                @"C:\fixture:stream", "\0bad-path"
            };
            for (var index = 0; index < configuredPaths.Length; index++)
            {
                WriteSettings(source, configuredPaths[index]);
                var before = Snapshot(source.Root);
                using var run = SelfCheckRun.Create(source, Path.Combine(root, "path-" + index), Protector);
                var copied = new SettingsStore(run.Paths, Protector).LoadStrict();
                Assert.Equal(run.Paths.ScreenshotDirectory, copied.Mpv.ScreenshotDirectory, "先断言再 IO，绝不探测网络/设备路径");
                var resolved = AppPaths.ResolveScreenshotDirectory(copied.Mpv.ScreenshotDirectory, run.Paths.ScreenshotDirectory)!;
                Assert.True(IsWithin(resolved, run.Paths.Root));
                Directory.CreateDirectory(resolved);
                Assert.Equal(before, Snapshot(source.Root));
                Assert.False(Directory.Exists(Path.Combine(root, "never-created")));
            }
        }));

        Test("自检输出：普通加载保留自定义目录、引号原值与最大化偏好", () => SelfCheckTests.WithFixture((source, root) =>
        {
            var custom = Path.Combine(root, "normal-custom", "screenshots");
            var configured = "  \"" + custom + "\"  ";
            WriteSettings(source, configured);
            var before = Snapshot(source.Root);
            var store = new SettingsStore(source, Protector);
            foreach (var settings in new[] { store.Load(), store.LoadStrict() })
            {
                Assert.Equal(configured, settings.Mpv.ScreenshotDirectory);
                Assert.True(settings.Ui.WindowMaximized);
                var resolved = AppPaths.ResolveScreenshotDirectory(settings.Mpv.ScreenshotDirectory, source.ScreenshotDirectory)!;
                Assert.Equal(custom, resolved);
                Assert.True(IsWithin(resolved, root));
                Directory.CreateDirectory(resolved);
            }
            Assert.True(Directory.Exists(custom), "正常启动仍创建用户指定的假目录");
            Assert.Equal(before, Snapshot(source.Root));
        }));

        Test("自检输出：复制和严格解析失败不建外部目录，释放锁后原目标能成功", () => SelfCheckTests.WithFixture((source, root) =>
        {
            var external = Path.Combine(root, "failed-outside", "screenshots");
            WriteSettings(source, external);
            var before = Snapshot(source.Root);
            var target = Path.Combine(root, "copy-failure");
            using (var locked = new FileStream(source.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Throws<IOException>(() => SelfCheckRun.Create(source, target, Protector));
            Assert.False(Directory.Exists(target));
            Assert.False(Directory.Exists(Path.Combine(root, "failed-outside")));
            Assert.Equal(before, Snapshot(source.Root));
            using (var recovered = SelfCheckRun.Create(source, target, Protector))
                Assert.Equal(recovered.Paths.ScreenshotDirectory, new SettingsStore(recovered.Paths, Protector).LoadStrict().Mpv.ScreenshotDirectory);
            Assert.Throws<IOException>(() => SelfCheckRun.Create(source, target, Protector), "成功证据目录不能复用");

            var valid = File.ReadAllText(source.SettingsFile);
            foreach (var invalid in new[] { valid[..^1], valid.Replace($"\"SchemaVersion\":{AppSettings.CurrentSchemaVersion}", "\"SchemaVersion\":999", StringComparison.Ordinal) })
            {
                File.WriteAllText(source.SettingsFile, invalid, new UTF8Encoding(false));
                var failedBefore = Snapshot(source.Root);
                var failed = Path.Combine(root, "strict-failure");
                Assert.Throws<Exception>(() => SelfCheckRun.Create(source, failed, Protector));
                Assert.False(Directory.Exists(failed));
                Assert.False(Directory.Exists(Path.Combine(root, "failed-outside")));
                Assert.Equal(failedBefore, Snapshot(source.Root));
            }
            File.WriteAllText(source.SettingsFile, valid, new UTF8Encoding(false));
            using var next = SelfCheckRun.Create(source, Path.Combine(root, "strict-failure"), Protector);
            Assert.Equal(next.Paths.ScreenshotDirectory, new SettingsStore(next.Paths, Protector).LoadStrict().Mpv.ScreenshotDirectory);
        }));

        Test("自检输出：DI 再严格加载用同一隔离策略，所有 AppPaths 输出仍在运行根内", () => SelfCheckTests.WithFixture((source, root) =>
        {
            var external = Path.Combine(root, "di-outside", "screenshots");
            WriteSettings(source, external);
            var before = Snapshot(source.Root);
            using var run = SelfCheckRun.Create(source, Path.Combine(root, "di-reload"), Protector);
            // Simulate a copy edited before the real DI factory's strict reload, not a new policy model.
            WriteSettings(run.Paths, external);
            var copied = new SettingsStore(run.Paths, Protector).LoadStrict();
            Assert.Equal(external, copied.Mpv.ScreenshotDirectory);
            SelfCheckRun.IsolateSettings(run.Paths, copied);
            SelfCheckRun.IsolateSettings(run.Paths, copied); // reapplication is idempotent
            Assert.Equal(run.Settings.Mpv.ScreenshotDirectory, copied.Mpv.ScreenshotDirectory);
            Assert.False(copied.Ui.WindowMaximized);
            var resolved = AppPaths.ResolveScreenshotDirectory(copied.Mpv.ScreenshotDirectory, run.Paths.ScreenshotDirectory)!;
            Assert.True(IsWithin(resolved, run.Paths.Root));
            Directory.CreateDirectory(resolved);
            var report = Path.Combine(run.Paths.LogDirectory, "selfcheck-di-isolation.txt");
            AtomicFile.WriteAllText(report, "运行内报告", new UTF8Encoding(false));
            new SettingsStore(run.Paths, Protector).Save(copied);
            foreach (var path in new[] { run.Paths.SettingsFile, run.Paths.SettingsBackupFile, report,
                run.Paths.ImageCacheDirectory, run.Paths.ShaderCacheDirectory, run.Paths.LogDirectory,
                run.Paths.ScreenshotDirectory, run.Paths.WebViewDirectory })
                Assert.True(IsWithin(path, run.Paths.Root), "所有应用自有输出都沿 AppPaths 的运行根");
            Assert.False(Directory.Exists(Path.Combine(root, "di-outside")));
            Assert.Equal(before, Snapshot(source.Root));
        }));

        Test("自检输出：重复运行各用自己的截图根，不继承前次副本或原配置", () => SelfCheckTests.WithFixture((source, root) =>
        {
            var external = Path.Combine(root, "repeat-outside", "screenshots");
            WriteSettings(source, external);
            var before = Snapshot(source.Root);
            string first;
            using (var run = SelfCheckRun.Create(source, Path.Combine(root, "first-run"), Protector))
            {
                first = run.Settings.Mpv.ScreenshotDirectory;
                Assert.Equal(run.Paths.ScreenshotDirectory, first);
            }
            using var second = SelfCheckRun.Create(source, Path.Combine(root, "second-run"), Protector);
            var copied = new SettingsStore(second.Paths, Protector).LoadStrict();
            Assert.Equal(second.Paths.ScreenshotDirectory, copied.Mpv.ScreenshotDirectory);
            Assert.False(first.Equals(copied.Mpv.ScreenshotDirectory, StringComparison.OrdinalIgnoreCase));
            Assert.False(Directory.Exists(Path.Combine(root, "repeat-outside")));
            Assert.Equal(before, Snapshot(source.Root));
        }));

        Test("自检输出：无源设置的全新账户也保存同一运行内落点且不生成源配置", () => SelfCheckTests.WithFixture((source, root) =>
        {
            File.Delete(source.SettingsFile);
            File.Delete(source.SettingsBackupFile);
            var before = Snapshot(source.Root);
            using var run = SelfCheckRun.Create(source, Path.Combine(root, "fresh-account"), Protector);
            var copied = new SettingsStore(run.Paths, Protector).LoadStrict();
            Assert.Equal(run.Paths.ScreenshotDirectory, copied.Mpv.ScreenshotDirectory);
            Assert.False(copied.Ui.WindowMaximized);
            Assert.Equal(before, Snapshot(source.Root));
            Assert.False(File.Exists(source.SettingsFile));
            Assert.False(File.Exists(source.SettingsBackupFile));
        }));
    }

    private static void WriteSettings(AppPaths paths, string screenshotDirectory)
    {
        var json = JsonSerializer.Serialize(new
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DeviceId = "fixture",
            Ui = new { WindowMaximized = true },
            Mpv = new { ScreenshotDirectory = screenshotDirectory, ScreenshotFormat = "webp", ScreenshotWebpQuality = 41 },
            Servers = new[]
            {
                new { Url = "https://selfcheck.invalid", Accounts = new[]
                {
                    new { ProtectedAccessToken = "opaque-placeholder", ProtectedPassword = "opaque-password-placeholder" }
                } }
            }
        });
        File.WriteAllText(paths.SettingsFile, json, new UTF8Encoding(false));
    }

    private static bool IsWithin(string path, string root) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Snapshot(string root) => string.Join("\n",
        Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(path => "dir=" + Path.GetRelativePath(root, path))
            .Concat(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path =>
                Path.GetRelativePath(root, path) + "=" + Convert.ToHexString(File.ReadAllBytes(path))))
            .OrderBy(value => value, StringComparer.Ordinal));

    private sealed class NoSecretsProtector : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("输出隔离不应重新加密凭据");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("输出隔离不应解密凭据");
    }
}

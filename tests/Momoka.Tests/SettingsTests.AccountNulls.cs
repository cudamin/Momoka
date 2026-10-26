using System.Text.Json;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class SettingsTests
{
    private static void RegisterAccountNulls()
    {
        // Only non-null profile fields. Last*Id, ServerId and AutoConnect are intentionally nullable.
        foreach (var field in new[]
        {
            "Server.Id", "Account.Id", "Account.Username", "Account.UserId",
            "Account.ProtectedPassword", "Account.ProtectedAccessToken"
        })
        {
            Test($"R-007：当前预选账号 {field}=null 无备份时隔离并安全读取 Core 依赖", () =>
                WithR007Store((store, protector) =>
                {
                    var damaged = R007NullDocument(R007Settings("main", 180), field);
                    File.WriteAllText(store.Paths.SettingsFile, damaged);

                    var loaded = store.Load();
                    AssertR007ReadDependencies(loaded, protector);
                    Assert.Equal(0, loaded.Servers.Sum(server => server.Accounts.Count), "坏身份不能作为成功读取结果穿透");
                    Assert.Equal(100, loaded.Ui.PageSize);
                    AssertR007Quarantine(store.Paths, damaged);
                    Assert.False(File.Exists(store.Paths.SettingsBackupFile));
                    Assert.Equal(0, protector.ProtectCalls, "当前格式的损坏文件不能触发凭据重新包装");
                    Assert.Equal(0, protector.UnprotectCalls, "默认设置没有任何凭据可解");

                    var reloaded = store.Load();
                    AssertR007ReadDependencies(reloaded, protector);
                    Assert.Equal(100, reloaded.Ui.PageSize);
                    AssertR007Quarantine(store.Paths, damaged);
                }));

            Test($"R-007：当前预选账号 {field}=null 不遮住有效备份且保留原文", () =>
                WithR007Store((store, protector) =>
                {
                    var damaged = R007NullDocument(R007Settings("main", 180), field);
                    var backup = R007Document(R007Settings("backup", 220));
                    File.WriteAllText(store.Paths.SettingsFile, damaged);
                    File.WriteAllText(store.Paths.SettingsBackupFile, backup);

                    var loaded = store.Load();
                    AssertR007ReadDependencies(loaded, protector);
                    AssertR007RecoveredBackup(loaded, protector);
                    AssertR007Quarantine(store.Paths, damaged);
                    Assert.Equal(backup, File.ReadAllText(store.Paths.SettingsBackupFile), "恢复不能消耗或改写备份");

                    var reloaded = store.Load();
                    AssertR007ReadDependencies(reloaded, protector);
                    AssertR007RecoveredBackup(reloaded, protector);
                    AssertR007Quarantine(store.Paths, damaged);
                    Assert.Equal(backup, File.ReadAllText(store.Paths.SettingsBackupFile));
                    Assert.Equal(0, protector.ProtectCalls, "读取当前主备不重新包装任何凭据");
                }));

            Test($"R-007：当前格式 {field}=null 的直接及严格读取拒绝但不改输入", () =>
                WithR007Store((store, protector) =>
                {
                    var damaged = R007NullDocument(R007Settings("main", 180), field);
                    File.WriteAllText(store.Paths.SettingsFile, damaged);

                    Assert.Throws<InvalidDataException>(() => SettingsMigration.FromJson(damaged, protector));
                    Assert.Throws<InvalidDataException>(() => store.LoadStrict());
                    Assert.Throws<InvalidDataException>(() => store.ReadBackup(store.Paths.SettingsFile));
                    Assert.Equal(damaged, File.ReadAllText(store.Paths.SettingsFile));
                    Assert.Equal(0, Directory.GetFiles(store.Paths.Root, "*.corrupt-*").Length, "严格读取不隔离或回退");
                    Assert.Equal(0, protector.ProtectCalls);
                    Assert.Equal(0, protector.UnprotectCalls);
                }));
        }

        Test("R-007：两项凭据和所有关键账号字段同时 null 仍恢复备份", () =>
            WithR007Store((store, protector) =>
            {
                var damaged = R007NullDocument(R007Settings("main", 180),
                    "Server.Id", "Account.Id", "Account.Username", "Account.UserId",
                    "Account.ProtectedPassword", "Account.ProtectedAccessToken");
                var backup = R007Document(R007Settings("backup", 220));
                File.WriteAllText(store.Paths.SettingsFile, damaged);
                File.WriteAllText(store.Paths.SettingsBackupFile, backup);

                var loaded = store.Load();
                AssertR007ReadDependencies(loaded, protector);
                AssertR007RecoveredBackup(loaded, protector);
                AssertR007Quarantine(store.Paths, damaged);
                Assert.Equal(backup, File.ReadAllText(store.Paths.SettingsBackupFile));
            }));

        Test("R-007：非预选账号的 null 也在 Load 内拒绝，不留到以后切换才崩", () =>
            WithR007Store((store, protector) =>
            {
                var settings = R007Settings("main", 180);
                settings.LastAccountId = settings.Servers[0].Accounts[0].Id;
                var damaged = R007NullDocument(settings, "Account.ProtectedPassword");
                var backup = R007Document(R007Settings("backup", 220));
                File.WriteAllText(store.Paths.SettingsFile, damaged);
                File.WriteAllText(store.Paths.SettingsBackupFile, backup);

                var loaded = store.Load();
                AssertR007ReadDependencies(loaded, protector);
                AssertR007RecoveredBackup(loaded, protector);
                AssertR007Quarantine(store.Paths, damaged);
            }));

        Test("R-007：只有坏备份时用默认值且保留备份原文，不把 null 当成成功", () =>
            WithR007Store((store, protector) =>
            {
                var damagedBackup = R007NullDocument(R007Settings("backup", 220), "Account.ProtectedAccessToken");
                File.WriteAllText(store.Paths.SettingsBackupFile, damagedBackup);

                var loaded = store.Load();
                AssertR007ReadDependencies(loaded, protector);
                Assert.Equal(100, loaded.Ui.PageSize);
                Assert.Equal(0, loaded.Servers.Sum(server => server.Accounts.Count));
                Assert.False(File.Exists(store.Paths.SettingsFile));
                Assert.Equal(damagedBackup, File.ReadAllText(store.Paths.SettingsBackupFile));
                Assert.Equal(0, Directory.GetFiles(store.Paths.Root, "*.corrupt-*").Length, "备份不被隔离或吞掉");
            }));

        Test("R-007：主备都含坏字段时隔离主文件，保留备份并安全回到默认", () =>
            WithR007Store((store, protector) =>
            {
                var damaged = R007NullDocument(R007Settings("main", 180), "Account.ProtectedPassword");
                var damagedBackup = R007NullDocument(R007Settings("backup", 220), "Account.Username");
                File.WriteAllText(store.Paths.SettingsFile, damaged);
                File.WriteAllText(store.Paths.SettingsBackupFile, damagedBackup);

                var loaded = store.Load();
                AssertR007ReadDependencies(loaded, protector);
                Assert.Equal(100, loaded.Ui.PageSize);
                Assert.Equal(0, loaded.Servers.Sum(server => server.Accounts.Count));
                AssertR007Quarantine(store.Paths, damaged);
                Assert.Equal(damagedBackup, File.ReadAllText(store.Paths.SettingsBackupFile));
            }));

        Test("R-007：合法旧格式缺字段和空字符串仍读入，不为校验丢弃账号", () =>
        {
            foreach (var version in new[] { 2, AppSettings.CurrentSchemaVersion })
            {
                var protector = new R007SecretProtector();
                var settings = SettingsMigration.FromJson($$"""
                    {
                      "SchemaVersion": {{version}}, "LastServerId": "server-empty", "LastAccountId": "account-empty",
                      "Servers": [{ "Id": "server-empty", "Url": "http://192.0.2.70:8096", "Accounts": [
                        { "Id": "account-empty", "Username": "", "UserId": "", "ProtectedPassword": "", "ProtectedAccessToken": "" },
                        { "Username": "missing-fields" }
                      ] }]
                    }
                    """, protector);

                AssertR007ReadDependencies(settings, protector);
                Assert.Equal(2, settings.Servers[0].Accounts.Count);
                Assert.Equal("account-empty", settings.LastAccountId);
                Assert.Equal("", settings.ResolveLastAccount()!.Username);
                Assert.False(settings.ResolveLastAccount()!.HasSavedPassword);
                Assert.False(settings.ResolveLastAccount()!.HasSavedToken);
                Assert.True(settings.Servers[0].Accounts[1].Id.Length > 0, "缺键沿用属性初始化器，不凭空拒绝合法旧配置");
                Assert.Equal("", settings.Servers[0].Accounts[1].UserId);
                Assert.Equal(0, protector.ProtectCalls);
                Assert.Equal(0, protector.UnprotectCalls);
            }
        });

        Test("R-007：可空选择和服务器远端身份仍合法，DeviceId=null 沿用原修复", () =>
        {
            var protector = new R007SecretProtector();
            var settings = R007Settings("main", 180);
            settings.LastServerId = null;
            settings.LastAccountId = null;
            settings.DeviceId = null!;
            settings.Servers[0].ServerId = null;
            settings.Servers[0].ServerVersion = null;
            foreach (var account in settings.Servers[0].Accounts) account.AutoConnect = null;

            var loaded = SettingsMigration.FromJson(R007Document(settings), protector);
            AssertR007ReadDependencies(loaded, protector);
            Assert.True(loaded.DeviceId.Length > 0);
            Assert.Null(loaded.Servers[0].ServerId);
            Assert.Null(loaded.Servers[0].ServerVersion);
            Assert.Equal(2, loaded.Servers[0].Accounts.Count);
            Assert.True(loaded.Servers[0].Accounts.All(account => account.AutoConnect is null));
            Assert.Equal(180, loaded.Ui.PageSize);
        });

        Test("R-007：有效主文件优先且读取不包装凭据或改动主备", () =>
            WithR007Store((store, protector) =>
            {
                var main = R007Document(R007Settings("main", 180));
                var backup = R007Document(R007Settings("backup", 220));
                File.WriteAllText(store.Paths.SettingsFile, main);
                File.WriteAllText(store.Paths.SettingsBackupFile, backup);

                var loaded = store.Load();
                AssertR007ReadDependencies(loaded, protector);
                Assert.Equal("device-main", loaded.DeviceId);
                Assert.Equal("server-main", loaded.LastServerId);
                Assert.Equal("account-main", loaded.LastAccountId);
                Assert.Equal(180, loaded.Ui.PageSize);
                Assert.Equal("fake:main-password", loaded.ResolveLastAccount()!.ProtectedPassword);
                Assert.Equal("fake:main-token", loaded.ResolveLastAccount()!.ProtectedAccessToken);
                Assert.Equal(0, protector.ProtectCalls);
                Assert.Equal(main, File.ReadAllText(store.Paths.SettingsFile));
                Assert.Equal(backup, File.ReadAllText(store.Paths.SettingsBackupFile));
            }));

        Test("R-007：旧 v1 密文密码原样保留，旧明文令牌仅经假 protector 包装一次", () =>
        {
            var protector = new R007SecretProtector();
            var settings = SettingsMigration.FromJson("""
                {
                  "LastServerId": "server-legacy", "LastAccountId": "account-legacy",
                  "Servers": [{ "Id": "server-legacy", "Url": "http://192.0.2.70:8096", "Accounts": [
                    { "Id": "account-legacy", "Username": "legacy-user", "UserId": "legacy-user-id",
                      "ProtectedPassword": "fake:legacy-password", "AccessToken": "legacy-token" }
                  ] }]
                }
                """, protector);

            AssertR007ReadDependencies(settings, protector);
            var account = settings.ResolveLastAccount()!;
            Assert.Equal("fake:legacy-password", account.ProtectedPassword, "已有 legacy 熵的密文不重新包装");
            Assert.Equal("fake:legacy-token", account.ProtectedAccessToken);
            Assert.True(account.RememberPassword);
            Assert.Equal(1, protector.ProtectCalls);
        });

        Test("R-007：有效字段的假解密失败仍要求重新输入，不回退备份或明文", () =>
            WithR007Store((store, protector) =>
            {
                var main = R007Document(R007Settings("main", 180));
                var backup = R007Document(R007Settings("backup", 220));
                File.WriteAllText(store.Paths.SettingsFile, main);
                File.WriteAllText(store.Paths.SettingsBackupFile, backup);
                protector.FailUnprotect = true;

                var loaded = store.Load();
                var account = loaded.ResolveLastAccount()!;
                var vault = new CredentialVault(protector);
                Assert.True(account.HasSavedPassword);
                Assert.True(account.HasSavedToken);
                Assert.Equal("", vault.GetPassword(account));
                Assert.Equal("", vault.GetAccessToken(account));
                Assert.Equal(180, loaded.Ui.PageSize, "解密失败不是读取损坏字段，不意外换到另一个身份");
                Assert.Equal(main, File.ReadAllText(store.Paths.SettingsFile));
                Assert.Equal(backup, File.ReadAllText(store.Paths.SettingsBackupFile));
                Assert.Equal(0, protector.ProtectCalls);
            }));

        Test("R-007：主文件暂不可读仍按现有 Load 契约恢复备份且不破坏存储", () =>
            WithR007Store((store, protector) =>
            {
                var main = R007Document(R007Settings("main", 180));
                var backup = R007Document(R007Settings("backup", 220));
                File.WriteAllText(store.Paths.SettingsFile, main);
                File.WriteAllText(store.Paths.SettingsBackupFile, backup);
                using (var held = new FileStream(store.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var loaded = store.Load();
                    AssertR007ReadDependencies(loaded, protector);
                    AssertR007RecoveredBackup(loaded, protector);
                    Assert.Equal(backup, File.ReadAllText(store.Paths.SettingsBackupFile));
                }

                Assert.Equal(main, File.ReadAllText(store.Paths.SettingsFile), "读/隔离失败都不能清空原文件");
                Assert.Equal(0, Directory.GetFiles(store.Paths.Root, "*.corrupt-*").Length);
                var reloaded = store.Load();
                AssertR007ReadDependencies(reloaded, protector);
                Assert.Equal(180, reloaded.Ui.PageSize, "解除锁后可继续读取原主文件");
            }));
    }

    private static AppSettings R007Settings(string identity, int pageSize)
    {
        var server = new ServerProfile
        {
            Id = $"server-{identity}",
            Name = $"夹具-{identity}",
            Url = "http://192.0.2.70:8096",
            Accounts =
            [
                new AccountProfile { Id = $"spare-{identity}", Username = $"spare-{identity}" },
                new AccountProfile
                {
                    Id = $"account-{identity}", Username = $"{identity}-user", UserId = $"remote-{identity}",
                    ProtectedPassword = $"fake:{identity}-password", ProtectedAccessToken = $"fake:{identity}-token",
                    RememberPassword = true, AutoConnect = true
                }
            ]
        };
        return new AppSettings
        {
            DeviceId = $"device-{identity}",
            LastServerId = server.Id,
            LastAccountId = server.Accounts[1].Id,
            Servers = [server],
            Ui = new UiSettings { PageSize = pageSize }
        };
    }

    private static string R007Document(AppSettings settings) =>
        JsonSerializer.Serialize(settings, SettingsSerializer.WriteOptions);

    private static string R007NullDocument(AppSettings settings, params string[] fields)
    {
        var document = JsonNode.Parse(R007Document(settings))!.AsObject();
        var server = document["Servers"]![0]!.AsObject();
        var account = server["Accounts"]![1]!.AsObject();
        foreach (var field in fields)
        {
            var parts = field.Split('.');
            (parts[0] == "Server" ? server : account)[parts[1]] = null;
        }
        return document.ToJsonString(SettingsSerializer.WriteOptions);
    }

    private static void WithR007Store(Action<SettingsStore, R007SecretProtector> run)
    {
        var root = Directory.CreateTempSubdirectory("momoka-r007-").FullName;
        try
        {
            var protector = new R007SecretProtector();
            run(new SettingsStore(new AppPaths(root), protector), protector);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertR007Quarantine(AppPaths paths, string original)
    {
        Assert.False(File.Exists(paths.SettingsFile), "坏主文件必须离开正常读取路径");
        var preserved = Directory.GetFiles(paths.Root, "settings.json.corrupt-*");
        Assert.Equal(1, preserved.Length);
        Assert.Equal(original, File.ReadAllText(preserved[0]), "隔离保留完整原文，可恢复其他账号和偏好");
    }

    private static void AssertR007RecoveredBackup(AppSettings settings, R007SecretProtector protector)
    {
        Assert.Equal("device-backup", settings.DeviceId);
        Assert.Equal("server-backup", settings.LastServerId);
        Assert.Equal("account-backup", settings.LastAccountId);
        Assert.Equal(220, settings.Ui.PageSize);
        var account = settings.ResolveLastAccount()!;
        Assert.Equal("backup-user", account.Username);
        Assert.Equal("remote-backup", account.UserId);
        Assert.Equal("fake:backup-password", account.ProtectedPassword);
        Assert.Equal("fake:backup-token", account.ProtectedAccessToken);
        Assert.True(account.RememberPassword);
        Assert.Equal<bool?>(true, account.AutoConnect);
        var vault = new CredentialVault(protector);
        Assert.Equal("backup-password", vault.GetPassword(account));
        Assert.Equal("backup-token", vault.GetAccessToken(account));
    }

    // Executes the Core selection/credential facts used by Prepare, not the real VM, session or UI.
    private static void AssertR007ReadDependencies(AppSettings settings, R007SecretProtector protector)
    {
        var server = settings.ResolveLastServer();
        var account = settings.ResolveLastAccount(server);
        var address = server?.Url ?? "";
        var username = account?.Username ?? "";
        var vault = new CredentialVault(protector);
        var saved = EmbySignInIdentity.SavedAccount(settings.Servers, address, username);
        _ = saved?.HasSavedPassword == true ? vault.GetPassword(saved) : "";
        _ = account?.HasSavedToken == true ? vault.GetAccessToken(account) : "";

        foreach (var profile in settings.Servers)
        {
            Assert.NotNull(profile.Id);
            foreach (var candidate in profile.Accounts)
            {
                Assert.NotNull(candidate.Id);
                Assert.NotNull(candidate.Username);
                Assert.NotNull(candidate.UserId);
                Assert.NotNull(candidate.ProtectedPassword);
                Assert.NotNull(candidate.ProtectedAccessToken);
                Assert.Equal(candidate.ProtectedPassword.Length > 0, candidate.HasSavedPassword);
                Assert.Equal(candidate.ProtectedAccessToken.Length > 0, candidate.HasSavedToken);
                if (candidate.HasSavedPassword) _ = vault.GetPassword(candidate);
                if (candidate.HasSavedToken) _ = vault.GetAccessToken(candidate);
            }
        }
    }

    private sealed class R007SecretProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }
        public bool FailUnprotect { get; set; }

        public string Protect(string plainText)
        {
            ProtectCalls++;
            return $"fake:{plainText}";
        }

        public string Unprotect(string cipherText)
        {
            UnprotectCalls++;
            if (FailUnprotect || !cipherText.StartsWith("fake:", StringComparison.Ordinal))
                throw new InvalidOperationException("假 protector 模拟解密失败");
            return cipherText["fake:".Length..];
        }
    }
}

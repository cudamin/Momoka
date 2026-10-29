using System.Reflection;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual Store saves with anonymous documents and files below this test output.</summary>
internal static class SettingsSaveSnapshotTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly EmbyConnection Connection = new(new Uri("https://settings.invalid/emby/"),
        "", "fixture-user", "fixture", "fixture", new DeviceIdentity("fixture", "fixture", "fixture-device", "1"));

    internal static void Register()
    {
        Test("保存快照：规范化磁盘文档，不改活对象及其集合", SaveDoesNotEditLive);
        Test("保存快照：旧 Schema 标签不会重跑历史迁移", SaveDoesNotMigrate);
        Test("保存快照：不再把 Normalize 捕获的旧搜索覆盖回活设置", RejectOldHistoryWriteback);
        Test("保存快照：后台提交期间的新搜索由后续保存完整落盘", EditWhileCommitting);
        Test("保存快照：文件失败保活值、旧文件和欠项，重试恢复", FailedCommitKeepsEdit);
    }

    private static void SaveDoesNotEditLive()
    {
        using var fixture = new Fixture();
        var settings = fixture.Settings;
        settings.Ui.PageSize = 900;
        settings.Playback.AudioLanguages = ["ja", "ja"];
        var languages = settings.Playback.AudioLanguages;
        var ui = settings.Ui;
        var history = RecentSearch.Read(ui, Connection);
        fixture.Store.Save(settings);
        Assert.Equal(900, ui.PageSize, "存储层不能回写编辑状态");
        Assert.True(ReferenceEquals(ui, settings.Ui));
        Assert.True(ReferenceEquals(languages, settings.Playback.AudioLanguages));
        Assert.Equal(2, languages.Count);
        Assert.True(ReferenceEquals(history, RecentSearch.Read(ui, Connection)));
        var saved = fixture.Store.Load();
        Assert.Equal(500, saved.Ui.PageSize);
        Assert.Equal(1, saved.Playback.AudioLanguages.Count);
        Assert.Equal(settings.DeviceId, saved.DeviceId);
    }

    private static void SaveDoesNotMigrate()
    {
        using var fixture = new Fixture();
        fixture.Settings.SchemaVersion = 2;
        fixture.Settings.Ui.WindowWidth = 1234;
        fixture.Settings.Ui.WindowHeight = 789;
        fixture.Store.Save(fixture.Settings);
        var saved = fixture.Store.Load();
        Assert.Equal(2, fixture.Settings.SchemaVersion, "保存不修改调用者持有的文档");
        Assert.Equal(AppSettings.CurrentSchemaVersion, saved.SchemaVersion);
        Assert.Equal(1234, saved.Ui.WindowWidth, "v6 历史迁移会把已选窗口尺寸清零");
        Assert.Equal(789, saved.Ui.WindowHeight);
    }

    private static void RejectOldHistoryWriteback()
    {
        using var fixture = new Fixture();
        var ui = fixture.Settings.Ui;
        var comparer = new EditOnLookupComparer();
        ui.RecentSearches = new Dictionary<string, List<RecentSearchEntry>>(ui.RecentSearches, comparer);
        comparer.Edit = () => RecentSearch.Remember(ui, Connection, "new-search");

        // The previous Save enters this comparer when Normalize assigns clean after ToArray captured
        // old entries. The nested actual edit deterministically stands in for the UI running there;
        // its one-shot guard prevents recursion. A read-only snapshot never consults this comparer.
        fixture.Store.Save(fixture.Settings);
        var editedDuringSave = comparer.Edited;
        if (!editedDuringSave) comparer.RunEdit();
        Assert.Equal("new-search", RecentSearch.Read(ui, Connection)[0].Term,
            "旧 Normalize 的迟到 clean 写回覆盖了已完成的新搜索");
        Assert.False(editedDuringSave, "保存不能写活字典，甚至不应向它查询条目");
        fixture.Store.Save(fixture.Settings);
        Assert.Equal("new-search", RecentSearch.Read(fixture.Store.Load().Ui, Connection)[0].Term);
    }

    private static void EditWhileCommitting()
    {
        using var fixture = new Fixture();
        var gate = typeof(AtomicFile).GetField("CommitGate", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("实际 AtomicFile 提交锁不存在");
        Task? background = null;
        Task<bool>? later = null;
        using var laterStarted = new ManualResetEventSlim();
        Monitor.Enter(gate);
        try
        {
            background = Task.Run(() => fixture.Store.Save(fixture.Settings));
            Assert.True(SpinWait.SpinUntil(() => HasCompleteTemporary(fixture), Timeout), "后台实际 JSON 快照未到达提交边界");
            RecentSearch.Remember(fixture.Settings.Ui, Connection, "new-search");
            later = Task.Run(() => { laterStarted.Set(); return fixture.Service.TrySave(); });
            Assert.True(laterStarted.Wait(Timeout));
            Assert.Equal("new-search", RecentSearch.Read(fixture.Settings.Ui, Connection)[0].Term);
            Assert.False(later.IsCompleted, "后续保存应等待当前文件提交");
        }
        finally
        {
            Monitor.Exit(gate);
            Task.WhenAll(background ?? Task.CompletedTask, later ?? Task.CompletedTask)
                .WaitAsync(Timeout).GetAwaiter().GetResult();
            if (later is not null) Assert.True(later.GetAwaiter().GetResult());
        }
        Assert.False(fixture.Store.HasUnsavedChanges);
        Assert.Equal("new-search", RecentSearch.Read(fixture.Settings.Ui, Connection)[0].Term);
        Assert.Equal("new-search", RecentSearch.Read(fixture.Store.Load().Ui, Connection)[0].Term);
        var previous = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(fixture.Store.Paths.SettingsBackupFile))!;
        Assert.Equal("old-search", RecentSearch.Read(previous.Ui, Connection)[0].Term, "备份是前一份完整旧快照");
    }

    private static bool HasCompleteTemporary(Fixture fixture)
    {
        foreach (var path in Directory.GetFiles(fixture.Root, "settings.json.*.tmp"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.GetProperty("Ui").GetProperty("RecentSearches").EnumerateObject().Any();
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        return false;
    }

    private static void FailedCommitKeepsEdit()
    {
        using var fixture = new Fixture();
        fixture.Store.Save(fixture.Settings);
        var prior = File.ReadAllText(fixture.Store.Paths.SettingsFile);
        RecentSearch.Remember(fixture.Settings.Ui, Connection, "new-search");
        using (var locked = new FileStream(fixture.Store.Paths.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(fixture.Service.TrySave());
        Assert.True(fixture.Store.HasUnsavedChanges);
        Assert.Equal(prior, File.ReadAllText(fixture.Store.Paths.SettingsFile));
        Assert.Equal("new-search", RecentSearch.Read(fixture.Settings.Ui, Connection)[0].Term);
        Assert.True(fixture.Service.TrySave());
        Assert.False(fixture.Store.HasUnsavedChanges);
        Assert.Equal("new-search", RecentSearch.Read(fixture.Store.Load().Ui, Connection)[0].Term);
    }

    private sealed class EditOnLookupComparer : IEqualityComparer<string>
    {
        internal Action? Edit { get; set; }
        internal bool Edited { get; private set; }
        internal void RunEdit()
        {
            if (Edited || Edit is not { } edit) return;
            Edited = true;
            edit();
        }
        public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y);
        public int GetHashCode(string value) { RunEdit(); return StringComparer.Ordinal.GetHashCode(value); }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, "settings-save-snapshot", Guid.NewGuid().ToString("N"));
        internal AppSettings Settings { get; } = new() { DeviceId = "fixture-device" };
        internal SettingsStore Store { get; }
        internal SettingsService Service { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            Store = new SettingsStore(new AppPaths(Root), new DenySecrets());
            Service = new SettingsService(Store, Settings);
            RecentSearch.Remember(Settings.Ui, Connection, "old-search");
        }
        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "settings-save-snapshot")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("拒绝清理测试输出目录以外的路径");
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class DenySecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("测试不允许凭据写入");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("测试不允许凭据读取");
    }
}

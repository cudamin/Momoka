using System.Reflection;
using System.Runtime.Loader;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class LocalLibraryCoordinatorTests
{
    internal static int Run(string shell, string root)
    {
        _ = new SettingsPageStateTests.ShellFixture(shell, root);
        var assembly = AssemblyLoadContext.Default.Assemblies.Single(item => item.GetName().Name == "Momoka");
        var coordinatorType = assembly.GetType("Momoka.Shell.Services.LocalLibraryCoordinator", true)!;
        var dispatcherType = assembly.GetType("Momoka.Shell.Platform.IUiDispatcher", true)!;
        foreach (var throws in new[] { false, true })
            Test("多库协调：保存" + (throws ? "异常" : "失败") + "不能留下自动执行规则", async () =>
            {
                using var fixture = new Fixture(coordinatorType, dispatcherType, root);
                fixture.Settings.Succeed = false;
                fixture.Settings.Throws = throws;
                var failed = false;
                try { await fixture.SaveAsync(fixture.Configuration("a", monitor: true)); }
                catch (IOException) { failed = true; }
                Assert.True(failed);
                Assert.Equal(0, fixture.Settings.Settings.LocalMedia.Libraries.Count);
                await fixture.CallAsync("StartAsync");
                Assert.False(Directory.Exists(fixture.Output("a")));
            });
        Test("多库协调：目录逐库保存、名字独立、重复目录拒绝", async () =>
        {
            using var fixture = new Fixture(coordinatorType, dispatcherType, root);
            await fixture.SaveAsync(fixture.Configuration("a"));
            await fixture.SaveAsync(fixture.Configuration("b"));
            Assert.Equal(2, fixture.Settings.Settings.LocalMedia.Libraries.Count);
            var duplicate = fixture.Configuration("c");
            duplicate.SourceDirectory = fixture.Source("a");
            var rejected = false;
            try { await fixture.SaveAsync(duplicate); }
            catch (InvalidOperationException) { rejected = true; }
            Assert.True(rejected);
            Assert.Equal(2, fixture.Settings.Settings.LocalMedia.Libraries.Count);
        });
        Test("多库协调：启动后已有与新增电影自动链接、NFO和索引归各库", async () =>
        {
            using var fixture = new Fixture(coordinatorType, dispatcherType, root);
            File.WriteAllText(Path.Combine(fixture.Source("a"), "Example.2026.mkv"), "existing movie fixture");
            await fixture.SaveAsync(fixture.Configuration("a", monitor: true));
            await fixture.SaveAsync(fixture.Configuration("b", monitor: true));
            await fixture.CallAsync("StartAsync");
            File.WriteAllText(Path.Combine(fixture.Source("b"), "Example.2026.mkv"), "new movie fixture");
            await WaitAsync(() => fixture.Entries("a") == 1 && fixture.Entries("b") == 1);
            Assert.Equal(1, Directory.GetFiles(fixture.Output("a"), "*.nfo", SearchOption.AllDirectories).Length);
            Assert.Equal(1, Directory.GetFiles(fixture.Output("b"), "*.mkv", SearchOption.AllDirectories).Length);
            await fixture.CallAsync("StopAsync");
            File.WriteAllText(Path.Combine(fixture.Source("a"), "Stopped.2026.mkv"), "must stay unprocessed");
            await Task.Delay(200);
            Assert.Equal(1, fixture.Entries("a"));
        });
        Test("多库协调：启动时源目录暂不可用仍启动监控等待恢复", async () =>
        {
            using var fixture = new Fixture(coordinatorType, dispatcherType, root);
            await fixture.SaveAsync(fixture.Configuration("offline", monitor: true));
            Directory.Delete(fixture.Source("offline"), recursive: true);
            await fixture.CallAsync("StartAsync");
            Assert.Contains("不可用", fixture.Status("offline"));
            Assert.Equal(1, fixture.Settings.Settings.LocalMedia.Libraries.Count);
        });
        Test("多库协调：网络目录硬链接在保存前拒绝且不改配置", async () =>
        {
            using var fixture = new Fixture(coordinatorType, dispatcherType, root);
            var configuration = fixture.Configuration("net");
            configuration.SourceDirectory = @"\\server\share\source";
            configuration.TargetDirectory = @"\\server\share\output";
            configuration.Method = "hardlink";
            var rejected = false;
            try { await fixture.SaveAsync(configuration); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("网络目录不支持硬链接", StringComparison.Ordinal); }
            Assert.True(rejected);
            Assert.Equal(0, fixture.Settings.Settings.LocalMedia.Libraries.Count);
        });
        Test("多库协调：保存等待期间取消，不保存或启用规则", async () =>
        {
            using var fixture = new Fixture(coordinatorType, dispatcherType, root);
            var gate = (SemaphoreSlim)coordinatorType.GetField("_changes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Coordinator)!;
            await gate.WaitAsync();
            using var cancellation = new CancellationTokenSource();
            Task saving;
            try
            {
                saving = fixture.CallAsync("SaveAsync", fixture.Configuration("a", monitor: true), cancellation.Token);
                cancellation.Cancel();
            }
            finally { gate.Release(); }
            var cancelled = false;
            try { await saving; } catch (OperationCanceledException) { cancelled = true; }
            Assert.True(cancelled);
            Assert.Equal(0, fixture.Settings.Settings.LocalMedia.Libraries.Count);
        });
        return TestHarness.Run();
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        var end = Environment.TickCount64 + 10000;
        while (Environment.TickCount64 < end) { if (ready()) return; await Task.Delay(50); }
        throw new AssertionException("自动入库未在10秒内完成");
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        private readonly object _coordinator;
        public object Coordinator => _coordinator;
        private readonly Type _type;
        private readonly MoviePilotService _mp;
        public MemorySettings Settings { get; } = new();
        public Fixture(Type type, Type dispatcher, string root)
        {
            _type = type;
            _root = Path.Combine(root, "artifacts", "local-coordinator", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Source("a"); Source("b");
            Settings.Settings.MoviePilot.Enabled = true;
            Settings.Settings.MoviePilot.Url = "https://fixture.invalid:3001";
            Settings.Settings.MoviePilot.Username = "fixture";
            var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
            credentials.SetPassword(Settings.Settings.MoviePilot, "fixture");
            var transport = new StubTransport().Answer("login/access-token", """{"access_token":"fixture-token"}""")
                .Answer("dashboard/system", """{"version":"v3.0.8"}""");
            const string media = """{"title":"Example","year":"2026","type":"电影","media_source":"themoviedb","media_id":"42","tmdb_id":42}""";
            transport.Answer("media/recognize", "{\"meta_info\":{},\"media_info\":" + media + "}").Answer("media/42", media);
            _mp = new(new(transport), credentials, Settings.Settings);
            Assert.True(_mp.RefreshAvailabilityAsync().GetAwaiter().GetResult());
            var proxy = DispatchProxy.Create(dispatcher, typeof(LocalLibraryDispatcherProxy));
            _coordinator = Activator.CreateInstance(type, Settings, PassthroughSecretProtector.Instance, _mp, new AppPaths(_root), proxy)!;
        }
        public string Source(string id) { var path = Path.Combine(_root, "source-" + id); Directory.CreateDirectory(path); return path; }
        public string Output(string id) => Path.Combine(_root, "output-" + id);
        public LocalMediaLibrarySettings Configuration(string id, bool monitor = false) => new()
        { Id = id, Name = "媒体库" + id, SourceDirectory = Source(id), TargetDirectory = Output(id), MonitorEnabled = monitor, Provider = "moviepilot" };
        public Task SaveAsync(LocalMediaLibrarySettings value) => CallAsync("SaveAsync", value, CancellationToken.None);
        public Task CallAsync(string name, params object?[] args) => (Task)_type.GetMethod(name)!.Invoke(_coordinator, args)!;
        public int Entries(string id) => ((System.Collections.IEnumerable)_type.GetMethod("EntriesFor")!.Invoke(_coordinator, [id])!).Cast<object>().Count();
        public string Status(string id) => (string)_type.GetMethod("StatusFor")!.Invoke(_coordinator, [id])!;
        public void Dispose()
        {
            CallAsync("StopAsync").GetAwaiter().GetResult();
            ((IDisposable)_coordinator).Dispose();
            _mp.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
    internal sealed class MemorySettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public bool Succeed { get; set; } = true;
        public bool Throws { get; set; }
        public bool TrySave() => Throws ? throw new IOException("匿名保存失败") : Succeed;
        public void Save() { }
        public bool HasUnsavedChanges => !Succeed;
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public ServerProfile ResolveServer(string url) => throw new InvalidOperationException();
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException();
        public void ExportPreferences(string path) => throw new InvalidOperationException();
        public AppSettings ReadBackup(string path) => throw new InvalidOperationException();
    }
}

public class LocalLibraryDispatcherProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        // No UI is hosted. Notifications are intentionally dropped; direct result reads prove the worker.
        if (method?.Name == "Post") return true;
        return null;
    }
}

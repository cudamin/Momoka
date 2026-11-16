using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.MoviePilot;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>加载本树真实生成 VM，网络停在匿名 HTTP handler，写入范围只含本次临时文件。</summary>
internal static class LocalMediaViewModelTests
{
    internal static int Run(string shell, string root)
    {
        var loader = new SettingsPageStateTests.ShellFixture(shell, root);
        var assembly = AssemblyLoadContext.Default.Assemblies.Single(item => item.GetName().Name == "Momoka");
        var type = assembly.GetType("Momoka.Shell.ViewModels.LocalMediaViewModel", true)!;
        Test("本机整理 VM：网络目录提示硬链接不可用", () =>
        {
            using var fixture = new Fixture(type, loader, root);
            fixture.Set("MethodIndex", 0);
            fixture.Set("SourceDirectory", @"\\server\share\source");
            Assert.Contains("网络目录不支持硬链接", fixture.Get<string>(fixture.Vm, "MethodNote"));
            fixture.Set("MethodIndex", 1);
            Assert.DoesNotContain("网络目录不支持硬链接", fixture.Get<string>(fixture.Vm, "MethodNote"));
            fixture.Set("MethodIndex", 0);
            fixture.Set("SourceDirectory", Path.GetDirectoryName(fixture.Source)!);
            Assert.DoesNotContain("网络目录不支持硬链接", fixture.Get<string>(fixture.Vm, "MethodNote"));
        });
        Test("本机整理 VM：修改校正季集立即撤销旧预览", () =>
        {
            using var fixture = new Fixture(type, loader, root);
            fixture.Scan();
            fixture.Set("CurrentRow", fixture.Row);
            fixture.Set("SeasonText", "2");
            Assert.False(fixture.Get<bool>(fixture.Row, "Ready"), "修改季集后仍允许按旧目标提交");
            Assert.False(fixture.Get<bool>(fixture.Vm, "CanApply"));
        });
        Test("本机整理 VM：取消确认零文件写入，确认一次创建真实链接", () =>
        {
            using var fixture = new Fixture(type, loader, root);
            fixture.Scan();
            fixture.Set("Confirm", (Func<string, Task<bool>>)(_ => Task.FromResult(false)));
            fixture.Run("ApplyAsync");
            Assert.False(Directory.Exists(fixture.Target));
            fixture.Set("Confirm", (Func<string, Task<bool>>)(_ => Task.FromResult(true)));
            fixture.Run("ApplyAsync");
            Assert.True(File.Exists(fixture.Source));
            Assert.Equal(1, Directory.GetFiles(fixture.Target, "*.mkv", SearchOption.AllDirectories).Length);
            Assert.True(fixture.Get<string>(fixture.Row, "ResultDetails").Contains(".nfo", StringComparison.Ordinal), "逐项回执没有保留NFO结果");
            Assert.False(fixture.Get<bool>(fixture.Vm, "CanApply"));
        });
        Test("本机整理 VM：等待确认时切换整理方式不执行旧计划", () =>
        {
            using var fixture = new Fixture(type, loader, root);
            fixture.Scan();
            fixture.Set("Confirm", (Func<string, Task<bool>>)(_ => { fixture.Set("MethodIndex", 1); return Task.FromResult(true); }));
            fixture.Run("ApplyAsync");
            Assert.False(Directory.Exists(fixture.Target));
        });
        Test("本机整理 VM：扫描错误不发送服务器整理和刷新请求", () =>
        {
            using var fixture = new Fixture(type, loader, root);
            fixture.Transport.Answer("media/recognize", """{"meta_info":{},"media_info":null}""");
            fixture.Scan();
            Assert.False(fixture.Get<bool>(fixture.Vm, "CanApply"));
            Assert.False(Directory.Exists(fixture.Target));
            Assert.Equal(0, fixture.Transport.Count("transfer/"));
            Assert.Equal(0, fixture.Transport.Count("scrape"));
        });
        Test("本机整理 VM：识别请求不包含源根目录及Windows绝对路径", () =>
        {
            using var fixture = new Fixture(type, loader, root);
            fixture.Scan();
            var url = Uri.UnescapeDataString(fixture.Transport.Only("media/recognize").Url);
            Assert.True(url.EndsWith("title=Example.2026.mkv", StringComparison.Ordinal));
            Assert.DoesNotContain("source/", url);
            Assert.DoesNotContain("source\\", url);
        });
        Test("本机整理 VM：校正搜索候选成功后更新预览，失败不能恢复旧目标", () =>
        {
            using var fixture = new Fixture(type, loader, root);
            fixture.Scan();
            fixture.Set("CurrentRow", fixture.Row);
            fixture.Transport.Answer("media/search", """[{"title":"Other","year":"2026","type":"电影","media_source":"themoviedb","media_id":"77","tmdb_id":77}]""");
            fixture.Set("SearchText", "Other");
            fixture.Run("SearchAsync");
            var candidates = fixture.Get<IList>(fixture.Vm, "Candidates");
            Assert.Equal(1, candidates.Count);
            fixture.Set("Candidate", candidates[0]!);
            fixture.Run("CorrectAsync");
            Assert.False(fixture.Get<bool>(fixture.Row, "Ready"), "B详情404后仍可执行A的旧计划");
            fixture.Transport.Answer("media/77", """{"title":"Other","year":"2026","type":"电影","media_source":"themoviedb","media_id":"77","tmdb_id":77}""");
            fixture.Run("CorrectAsync");
            Assert.True(fixture.Get<bool>(fixture.Row, "Ready"));
            Assert.Contains("Other", fixture.Get<string>(fixture.Row, "Target"));
        });
        return TestHarness.Run();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SettingsPageStateTests.ShellFixture _reflection;
        private readonly string _directory;
        private readonly MoviePilotService _service;
        public object Vm { get; }
        public string Source { get; }
        public string Target { get; }
        public StubTransport Transport { get; } = new();
        public object Row => ((IList)_reflection.Get<object>(Vm, "Rows"))[0]!;
        public Fixture(Type type, SettingsPageStateTests.ShellFixture reflection, string root)
        {
            _reflection = reflection;
            _directory = Path.Combine(root, "artifacts", "local-media-vm", Guid.NewGuid().ToString("N"));
            var sourceDirectory = Path.Combine(_directory, "source");
            Directory.CreateDirectory(sourceDirectory);
            Source = Path.Combine(sourceDirectory, "Example.2026.mkv");
            File.WriteAllText(Source, "anonymous fixture video bytes");
            Target = Path.Combine(_directory, "output");
            var settings = new MemorySettings();
            settings.Settings.LocalMedia.SourceDirectory = sourceDirectory;
            settings.Settings.LocalMedia.TargetDirectory = Target;
            settings.Settings.LocalMedia.Provider = "moviepilot";
            settings.Settings.MoviePilot.Enabled = true;
            settings.Settings.MoviePilot.Url = "https://fixture.invalid:3001";
            settings.Settings.MoviePilot.Username = "fixture";
            var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
            credentials.SetPassword(settings.Settings.MoviePilot, "fixture-password");
            Transport.Answer("login/access-token", """{"access_token":"fixture-token"}""");
            Transport.Answer("dashboard/system", """{"version":"v3.0.8"}""");
            const string media = """{"title":"Example","year":"2026","type":"电影","media_source":"themoviedb","media_id":"42","tmdb_id":42}""";
            Transport.Answer("media/recognize", "{\"meta_info\":{},\"media_info\":" + media + "}");
            Transport.Answer("media/42", media);
            _service = new(new MoviePilotClient(Transport), credentials, settings.Settings);
            Assert.True(_service.RefreshAvailabilityAsync().GetAwaiter().GetResult());
            Vm = Activator.CreateInstance(type)!;
            _reflection.Invoke(Vm, "Attach", settings, PassthroughSecretProtector.Instance, _service);
        }
        public void Set(string name, object value) => _reflection.Set(Vm, name, value);
        public T Get<T>(object instance, string name) => _reflection.Get<T>(instance, name);
        public void Run(string name) => ((Task)_reflection.Invoke(Vm, name)!).GetAwaiter().GetResult();
        public void Scan() => Run("ScanAsync");
        public void Dispose()
        {
            ((IDisposable)Vm).Dispose();
            _service.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
    private sealed class MemorySettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 50;
        public void Save() { }
        public bool TrySave() => true;
        public bool HasUnsavedChanges => false;
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public ServerProfile ResolveServer(string url) => throw new InvalidOperationException();
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new InvalidOperationException();
        public void ExportPreferences(string path) => throw new InvalidOperationException();
        public AppSettings ReadBackup(string path) => throw new InvalidOperationException();
    }
}

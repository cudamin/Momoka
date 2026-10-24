using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DetailSubjectRemovalTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static int Run(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var root = Path.GetFullPath(sourceRoot);
            var dll = Path.GetFullPath(shellPath);
            if (!File.Exists(dll) || !dll.StartsWith(Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new AssertionException("必须提供本树当前Shell Release DLL");
            var directory = Path.GetDirectoryName(dll)!;
            AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(directory, name.Name + ".dll"))
                ? context.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
            foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Emby, MediaServerKind.Jellyfin })
            {
                Test("R025 主体成功删除后真VM目标与旧直接播放失效 " + kind, () =>
                {
                    using var f = new Fixture(assembly, root, kind);
                    var removal = f.Capture();
                    f.Transport.Missing = true;
                    removal();
                    Assert.Null(f.Get("PlayTarget"));
                    Assert.Null(f.Get("CurrentItem"));
                    Assert.Equal(0, ((System.Collections.ICollection)f.Get("Sources")!).Count);
                    Assert.Null(f.Get("SelectedSource"));
                    Assert.False(f.CanPlay());
                    f.Start();
                    f.Reload();
                    Assert.Equal(0, f.Actions.Plays);
                    Assert.Equal(0, f.Transport.Requests);
                    Assert.True(f.Get("NoticeOpen") is true);
                });
                Test("R025 用户取消删除不调用成功回调，原播放目标保留 " + kind, () =>
                {
                    using var f = new Fixture(assembly, root, kind);
                    _ = f.Capture(); // The dialog cancellation boundary never invokes this callback.
                    Assert.NotNull(f.Get("PlayTarget"));
                    Assert.True(f.CanPlay());
                    Assert.Equal(0, f.Transport.Requests);
                });
                Test("R025 普通刷新失败不能冒充主体删除 " + kind, () =>
                {
                    using var f = new Fixture(assembly, root, kind);
                    f.Transport.Missing = true;
                    f.Reload();
                    Assert.NotNull(f.Get("PlayTarget"));
                    Assert.NotNull(f.Get("CurrentItem"));
                    Assert.True(f.CanPlay());
                    Assert.Equal(1, f.Transport.Requests);
                });
                Test("R025 旧成功回调在Attach新条目后不能清新目标 " + kind, () =>
                {
                    using var f = new Fixture(assembly, root, kind);
                    var old = f.Capture();
                    f.Reattach("new-item");
                    old();
                    Assert.Equal("new-item", ((EmbyItem)f.Get("PlayTarget")!).Id);
                    Assert.Equal("new-item", ((EmbyItem)f.Get("CurrentItem")!).Id);
                    Assert.True(f.CanPlay());
                });
                Test("R025 已取消页面旧删除回调不修改只读目标 " + kind, () =>
                {
                    using var f = new Fixture(assembly, root, kind);
                    var target = f.Get("PlayTarget");
                    var old = f.Capture();
                    Invoke(f.Vm, "Cancel");
                    old();
                    Assert.True(ReferenceEquals(target, f.Get("PlayTarget")));
                    Assert.False(f.CanPlay());
                });
            }
            Test("R025 飞牛非最后版本删除后读取失败，立即保留存活源", () =>
            {
                using var f = new Fixture(assembly, root, MediaServerKind.FnNas);
                f.TwoVersions();
                var changed = f.CaptureReload();
                ((EmbyItem)f.Get("CurrentItem")!).MediaSources.RemoveAt(0);
                f.Transport.Missing = true;
                changed();
                Assert.NotNull(f.Get("PlayTarget"));
                Assert.Equal("version-2", f.SelectedSourceId());
                Assert.True(f.CanPlay());
                Assert.Equal(1, ((System.Collections.ICollection)f.Get("Sources")!).Count);
            });
            Test("R025 删除在飞期间正常刷新替换同ID详情，完成仍撤已删源", () =>
            {
                using var f = new Fixture(assembly, root, MediaServerKind.FnNas);
                f.TwoVersions();
                var before = (EmbyItem)f.Get("CurrentItem")!;
                var changed = f.CaptureReload();
                f.ReplaceSameDetail();
                before.MediaSources.RemoveAt(0);
                f.Transport.Missing = true;
                changed();
                Assert.Equal("version-2", f.SelectedSourceId());
                Assert.Equal(1, ((EmbyItem)f.Get("CurrentItem")!).MediaSources.Count);
                Assert.True(f.CanPlay());
            });
            Test("R025 删除确认后晚到详情仅含已删源，不复活播放目标", () =>
            {
                using var f = new Fixture(assembly, root, MediaServerKind.FnNas);
                f.TwoVersions();
                var changed = f.CaptureReload();
                var item = (EmbyItem)f.Get("CurrentItem")!;
                item.MediaSources.RemoveAt(0);
                f.Transport.Missing = true;
                changed();
                f.ReplaceWithOnlyDeletedSource();
                Assert.Null(f.Get("PlayTarget"));
                Assert.Null(f.Get("SelectedSource"));
                Assert.False(f.CanPlay());
                f.Start();
                Assert.Equal(0, f.Actions.Plays);
            });
            Test("R025 删除确认后晚到完整详情保留存活源且拒已删源", () =>
            {
                using var f = new Fixture(assembly, root, MediaServerKind.FnNas);
                f.TwoVersions();
                var changed = f.CaptureReload();
                ((EmbyItem)f.Get("CurrentItem")!).MediaSources.RemoveAt(0);
                f.Transport.Missing = true;
                changed();
                f.ReplaceWithStaleSources();
                Assert.Equal("version-2", f.SelectedSourceId());
                Assert.Equal(1, ((EmbyItem)f.Get("CurrentItem")!).MediaSources.Count);
                Assert.True(f.CanPlay());
            });
            Test("R025 已选择其它来源时主体删除非最后版本，仍去掉旧源但保留副本", () =>
            {
                using var f = new Fixture(assembly, root, MediaServerKind.FnNas);
                f.TwoVersions();
                var deletedRow = f.FirstSourceRow();
                f.SelectForeignVersion();
                var changed = f.CaptureReload();
                ((EmbyItem)f.Get("CurrentItem")!).MediaSources.RemoveAt(0);
                f.Transport.Missing = true;
                changed();
                Assert.Equal(2, ((System.Collections.ICollection)f.Get("Sources")!).Count);
                Assert.Equal("foreign-item", ((EmbyItem)f.Get("PlayTarget")!).Id);
                Assert.Equal("version-1", f.SelectedSourceId(), "不同来源相同源ID的有效副本不得误删");
                Assert.True(f.CanPlay());
                f.SelectSourceRow(deletedRow);
                f.Start();
                Assert.Equal(0, f.Actions.Plays, "旧主体版本行不可被集合成员守卫当作仍有效");
            });
            Test("R025 旧版本变化回调跨访问后不改新条目或读取新来源", () =>
            {
                using var f = new Fixture(assembly, root, MediaServerKind.FnNas);
                f.TwoVersions();
                var before = (EmbyItem)f.Get("CurrentItem")!;
                var changed = f.CaptureReload();
                f.Reattach("new-item");
                before.MediaSources.RemoveAt(0);
                changed();
                Assert.Equal("new-item", ((EmbyItem)f.Get("CurrentItem")!).Id);
                Assert.Equal("version-1", f.SelectedSourceId());
                Assert.Equal(0, f.Transport.Requests);
            });
            Test("R025 真实详情更多接入捕获删除回调，最后版本分支交接删除", () =>
            {
                var page = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "DetailPage.xaml.cs"));
                var start = page.IndexOf("    private void OnMoreClicked(", StringComparison.Ordinal);
                var end = page.IndexOf("    private void OnCardAction(", start, StringComparison.Ordinal);
                var more = page[start..(end > start ? end : Math.Min(page.Length, start + 2000))];
                Assert.Contains("removed: ViewModel.CaptureRemoval()", more);
                var commands = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "ItemCommands.FnNas.cs"));
                Assert.Contains("versions.Count == 1 && files.Count == 1 && removed is not null", commands);
                Assert.Contains("removed(card)", commands);
                var route = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", "ItemCommands.cs"));
                Assert.Contains("FnNasVersionsAsync(scope, shell, owner, card, changed, removed)", route);
            });
            return TestHarness.Run();
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static object? Invoke(object target, string name, params object?[] args)
    {
        try { return target.GetType().GetMethod(name, Members)!.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly Assembly _assembly;
        private readonly FakeSettings _settings = new();
        private readonly EmbySession _session;
        private readonly EmbySessionScope _scope;
        private readonly EmbyImageStore _images;
        public DeleteTransport Transport { get; } = new();
        public DetailDeleteActionsProxy Actions { get; }
        public object Vm { get; }
        public object Card { get; private set; } = null!;
        public Fixture(Assembly assembly, string root, MediaServerKind kind)
        {
            _assembly = assembly;
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var secrets = new NoSecrets();
            var output = Path.Combine(root, "artifacts", "r025", "fixture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            _session = new EmbySession(_settings.Settings, new SettingsStore(new AppPaths(output + "|no-settings"), secrets), new CredentialVault(secrets), device, Transport);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("https://delete-fixture.invalid/" + (kind == MediaServerKind.Emby ? "emby/" : kind == MediaServerKind.FnNas ? "v/" : "")),
                "placeholder-only", "fixture", "fixture", "fixture", device)
            { ServerKind = kind };
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = "fixture", ServerKind = kind }, new AccountProfile { Id = "fixture" });
            _scope = new EmbySessionScope(_session, identity);
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!).Add(_scope.Key, _scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, _scope);
            _images = new EmbyImageStore(_session, Path.Combine(output, "images"));
            Actions = (DetailDeleteActionsProxy)DispatchProxy.Create(assembly.GetType("Momoka.Shell.Views.IShellActions", true)!, typeof(DetailDeleteActionsProxy));
            Vm = Activator.CreateInstance(assembly.GetType("Momoka.Shell.ViewModels.DetailViewModel", true)!)!;
            Reattach("item");
        }
        public void Reattach(string id)
        {
            var item = _scope.Bind(new EmbyItem
            {
                Id = id,
                Name = "anonymous movie",
                Type = EmbyItemType.Movie,
                RunTimeTicks = 600_000_000,
                MediaSources = [new MediaSource { Id = "version-1", ItemId = id, Path = "/anonymous/one.mkv", Container = "mkv" }]
            });
            var request = Activator.CreateInstance(_assembly.GetType("Momoka.Shell.Views.DetailRequest", true)!, nonPublic: true)!;
            request.GetType().GetProperty("Item")!.SetValue(request, item);
            Invoke(Vm, "Attach", request, Actions, _settings, _session, _images);
            Invoke(Vm, "Apply", item); // Actual detail publication; no login or initial HTTP is needed.
            var cardType = _assembly.GetType("Momoka.Shell.Views.CardItem", true)!;
            var constructor = cardType.GetConstructors().Single();
            var args = constructor.GetParameters().Select(parameter => parameter.Name switch
            { "item" => (object)item, "images" => _images, "width" => 170, _ => parameter.DefaultValue }).ToArray();
            Card = constructor.Invoke(args);
            Assert.NotNull(Get("PlayTarget"));
            Assert.Null(Vm.GetType().GetField("_versionLoad", Members)!.GetValue(Vm));
        }
        public object FirstSourceRow() => ((System.Collections.IEnumerable)Get("Sources")!).Cast<object>().First();
        public void SelectSourceRow(object row) => Vm.GetType().GetProperty("SelectedSource", Members)!.SetValue(Vm, row);
        public void SelectForeignVersion()
        {
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("https://foreign.invalid/emby/"), "placeholder-only", "foreign", "foreign", "foreign", device);
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = "foreign" }, new AccountProfile { Id = "foreign" });
            var scope = new EmbySessionScope(_session, identity);
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!).Add(scope.Key, scope);
            var copy = scope.Bind(new EmbyItem
            {
                Id = "foreign-item",
                Name = "copy",
                Type = EmbyItemType.Movie,
                MediaSources =
                [new MediaSource { Id = "version-1", Path = "/foreign/copy.mkv", Container = "mkv" }]
            });
            var versionsField = Vm.GetType().GetField("_versions", Members)!;
            var current = (IReadOnlyList<Momoka.Playback.MediaVersion>)versionsField.GetValue(Vm)!;
            versionsField.SetValue(Vm, current.Append(new Momoka.Playback.MediaVersion(copy, copy.MediaSources[0])).ToArray());
            var row = new SourceRow(copy.MediaSources[0], "foreign-copy");
            ((IList<SourceRow>)Get("Sources")!).Add(row);
            SelectSourceRow(row);
        }
        public void TwoVersions()
        {
            var item = (EmbyItem)Get("CurrentItem")!;
            item.MediaSources.Add(new MediaSource { Id = "version-2", ItemId = item.Id, Path = "/anonymous/two.mkv", Container = "mkv" });
            Invoke(Vm, "ShowTarget", item);
        }
        public void ReplaceSameDetail()
        {
            var before = (EmbyItem)Get("CurrentItem")!;
            var current = _scope.Bind(new EmbyItem
            {
                Id = before.Id,
                Name = before.Name,
                Type = before.Type,
                RunTimeTicks = before.RunTimeTicks,
                MediaSources = before.MediaSources.Select(source => new MediaSource { Id = source.Id, ItemId = before.Id, Path = source.Path, Container = "mkv" }).ToList()
            });
            Invoke(Vm, "Apply", current);
        }
        public void ReplaceWithOnlyDeletedSource() => ReplaceWithStaleSources(onlyDeleted: true);
        public void ReplaceWithStaleSources(bool onlyDeleted = false)
        {
            var before = (EmbyItem)Get("CurrentItem")!;
            var sources = new List<MediaSource>
            { new() { Id = "version-1", ItemId = before.Id, Path = "/anonymous/one.mkv", Container = "mkv" } };
            if (!onlyDeleted) sources.Add(new() { Id = "version-2", ItemId = before.Id, Path = "/anonymous/two.mkv", Container = "mkv" });
            Invoke(Vm, "Apply", _scope.Bind(new EmbyItem
            {
                Id = before.Id,
                Name = before.Name,
                Type = before.Type,
                RunTimeTicks = before.RunTimeTicks,
                MediaSources = sources
            }));
        }
        public string? SelectedSourceId()
        {
            var row = Get("SelectedSource");
            return row is null ? null : ((MediaSource)row.GetType().GetProperty("Source")!.GetValue(row)!).Id;
        }
        public Action CaptureReload() => Vm.GetType().GetMethod("CaptureReload", Members) is { } method
            ? (Action)method.Invoke(Vm, null)! : () => Invoke(Vm, "Reload");
        public Action Capture()
        {
            var card = Card;
            var factory = Vm.GetType().GetMethod("CaptureRemoval", Members);
            if (factory is null) return () => ((Task)Invoke(Vm, "RemoveAsync", card)!).GetAwaiter().GetResult();
            var callback = (Delegate)factory.Invoke(Vm, null)!;
            return () => callback.DynamicInvoke(card);
        }
        public object? Get(string name) => Vm.GetType().GetProperty(name, Members)!.GetValue(Vm);
        public bool CanPlay() => ((System.Windows.Input.ICommand)Get("PlayCommand")!).CanExecute(null);
        public void Start() => ((Task)Invoke(Vm, "StartAsync", false)!).GetAwaiter().GetResult();
        public void Reload() => ((Task)Invoke(Vm, "ReloadAsync")!).GetAwaiter().GetResult();
        public void Dispose()
        {
            ((IDisposable)Vm).Dispose(); _session.Dispose();
        }
        private sealed class FakeSettings : ISettingsService
        {
            public AppSettings Settings { get; } = new();
            public int PageSize => 20;
            public event EventHandler? SaveStateChanged { add { } remove { } }
            public bool HasUnsavedChanges => false;
            public bool TrySave() => throw new AssertionException("不得保存设置");
            public void Save() => throw new AssertionException("不得保存设置");
            public ServerProfile ResolveServer(string url) => throw new AssertionException("不得读取账号");
            public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("不得读取账号");
            public void ExportPreferences(string path) => throw new AssertionException("不得设置IO");
            public AppSettings ReadBackup(string path) => throw new AssertionException("不得设置IO");
        }
        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string text) => throw new AssertionException("禁止凭据保护");
            public string Unprotect(string text) => throw new AssertionException("禁止凭据读取或登录");
        }
    }
    public sealed class DeleteTransport : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public bool Missing { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests++;
            if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "delete-fixture.invalid") throw new AssertionException("仅允许匿名详情GET");
            return Task.FromResult(new HttpResponseMessage(Missing ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") });
        }
    }
}
public class DetailDeleteActionsProxy : DispatchProxy
{
    public int Plays { get; private set; }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "PlayAsync": Plays++; return Task.CompletedTask;
            case "Notify": return null;
            case "RefreshFavorites": return null;
            case "get_MoviePilotEnabled": return false;
            default: throw new AssertionException("禁止未登记外部操作：" + targetMethod?.Name);
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class Program
{
    internal static string Root { get; private set; } = "";
    private static PermissionShellRuntime? _shell;
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length is < 1 or > 2 || !Path.IsPathFullyQualified(args[0]))
            throw new ArgumentException("传入本工作树绝对路径，可选本树当前 Release Shell DLL。");
        Root = Path.GetFullPath(args[0]);
        var project = Path.Combine(Root, "tests", "Momoka.CurrentPermissionSnapshotTests");
        Assert.True(typeof(Program).Assembly.Location.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (args.Length == 2) _shell = new PermissionShellRuntime(args[1]);
        Console.WriteLine("R-011 匿名当前权限／完整 FnNas VM＋真实生成器／可选实际 Settings VM＋原样 Page；禁止认证、设置IO、真实网络、App、native、播放。");
        RegisterCore();
        RegisterFnNas();
        RegisterSources();
        RegisterPermissionEdges();
        if (_shell is not null) { RegisterSettings(); RegisterSettingsEdges(); }
        var results = new List<object>();
        var failures = 0;
        var stopwatch = Stopwatch.StartNew();
        foreach (var (name, run) in Cases)
        {
            try
            {
                await run();
                results.Add(new { name, outcome = "passed" });
                Console.WriteLine("通过 " + name);
            }
            catch (Exception error)
            {
                failures++;
                results.Add(new { name, outcome = "failed", message = error.Message });
                Console.WriteLine("失败 " + name + " —— " + error.Message);
            }
        }
        var output = Path.GetFullPath(Environment.GetEnvironmentVariable("R011_RESULT_PATH")
            ?? Path.Combine(Root, "artifacts", "r011-current-permission", "result-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff") + ".json"));
        Assert.True(output.StartsWith(Path.Combine(Root, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(output), "不得覆盖红／绿证据");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new
        {
            startedAgainst = "anonymous fixture only",
            executed = Cases.Count,
            passed = Cases.Count - failures,
            failed = failures,
            skipped = 0,
            settingsRuntimeExecuted = _shell is not null,
            elapsedMs = stopwatch.ElapsedMilliseconds,
            cases = results,
            notExecuted = new[] { "Authenticate/TryReauthenticate/Restore/Password", "CredentialVault/DPAPI/settings IO", "WinUI/App/native/real network/playback", "full Core/selfcheck/user mutex", "publish/push/shortcut" }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failures}，失败 {failures}，跳过 0；证据 {output}");
        return failures == 0 ? 0 : 1;
    }

    private static void Case(string name, Func<Task> run) => Cases.Add((name, run));

    private static Task Done(Action run) { run(); return Task.CompletedTask; }

    private static void RegisterCore()
    {
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Jellyfin })
        {
            Case($"{kind} 当前Session/Scope权限 true→false→true，旧Connection/identity/lifetime保留", () => Done(() =>
            {
                using var state = new PermissionFixture(kind);
                var scope = state.Session.Capture();
                var connection = scope.Connection;
                var identity = state.Identity(scope);
                var lifetime = state.Lifetime(scope);
                var item = scope.Bind(new EmbyItem { Id = "fixture-item" });
                state.Replace(scope, false);
                Assert.Equal<bool?>(false, state.Session.Connection!.IsAdministrator);
                Assert.Equal<bool?>(false, state.Permission(scope), "旧scope应读取当前权限，不读捕获快照");
                Assert.True(ReferenceEquals(connection, scope.Connection));
                Assert.True(ReferenceEquals(identity, state.Identity(state.Session.Capture())));
                Assert.True(scope.IsCurrent && scope.IsAvailable && !lifetime.IsCancellationRequested);
                state.Replace(scope, true, marker: "restored");
                Assert.Equal<bool?>(true, state.Permission(scope));
                Assert.True(ReferenceEquals(scope, item.Source));
                Assert.Equal(lifetime, state.Lifetime(scope));
                Assert.True(ReferenceEquals(connection, scope.Connection));
                Assert.Equal(0, state.SourceNotifications, "同身份权限变化不能伪造来源列表变更");
            }));
            Case($"{kind} 已Probe权限缓存随同身份替换立即失效，不等下一次Probe", async () =>
            {
                using var state = new PermissionFixture(kind);
                await state.Capabilities.ProbeAsync();
                Assert.Equal<bool?>(true, state.Capabilities.IsAdministrator);
                var reads = state.Transport.Requests.Count;
                state.Replace(state.A, false);
                Assert.Equal<bool?>(false, state.Capabilities.IsAdministrator);
                state.Replace(state.A, true);
                Assert.Equal<bool?>(true, state.Capabilities.IsAdministrator);
                Assert.Equal(reads, state.Transport.Requests.Count, "已确认权限更新不需再读服务器");
            });
            Case($"{kind} 降权/恢复跨旧Probe在途，旧答复不能重新写回管理员缓存", async () =>
            {
                using var state = new PermissionFixture(kind);
                using var gate = new ReplyGate();
                var path = kind == MediaServerKind.FnNas ? "GET user/info" : "GET System/Info";
                state.Server(state.A).Gates.Add(path, gate);
                var probe = state.Capabilities.ProbeAsync();
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                state.Replace(state.A, false);
                gate.Release();
                await probe;
                Assert.Equal<bool?>(false, state.Capabilities.IsAdministrator, "旧Probe结果不属于新权限代次");
                state.Replace(state.A, true);
                Assert.Equal<bool?>(true, state.Capabilities.IsAdministrator);
            });
        }
        Case("权限通知来源键准确、降升及同值新确认各一次，不通知SourcesChanged且坏订阅不阻断后续", () => Done(() =>
        {
            using var state = new PermissionFixture(second: MediaServerKind.FnNas);
            var keys = new List<string>();
            using var bad = state.WatchPermissions(_ => throw new InvalidOperationException("fixture-subscriber-only"));
            using var good = state.WatchPermissions(source => keys.Add(source.Key));
            state.Replace(state.B!, false);
            state.Replace(state.B!, true);
            state.Replace(state.B!, true, marker: "same-permission");
            Assert.Equal(3, keys.Count, "同值新客户端也要使旧Probe缓存失效");
            Assert.True(keys.All(key => key == state.B!.Key));
            Assert.Equal(0, state.SourceNotifications);
            Assert.Equal<bool?>(true, state.Session.Connection!.IsAdministrator, "显式B不改默认A");
        }));
        Case("权限代次识别ABA，不改变identity/原令牌/取消寿命", () => Done(() =>
        {
            using var state = new PermissionFixture();
            var scope = state.Session.Capture();
            var before = state.Revision(scope);
            state.Replace(scope, false);
            var lowered = state.Revision(scope);
            state.Replace(scope, true);
            Assert.True(lowered > before && state.Revision(scope) > lowered);
            Assert.Equal<bool?>(true, scope.Connection.IsAdministrator, "捕获Connection仍是原快照");
            Assert.Equal("placeholder-old", scope.Connection.AccessToken);
            Assert.False(state.Lifetime(scope).IsCancellationRequested);
        }));
        Case("撤销scope权限必须fail-closed，不能借另一在线账号的管理员值", () => Done(() =>
        {
            using var state = new PermissionFixture(second: MediaServerKind.FnNas);
            var scope = state.Session.Capture();
            state.Select(state.B!);
            state.Revoke(scope);
            Assert.Null(state.Permission(scope));
            Assert.False(scope.IsAvailable);
            Assert.True(state.Lifetime(scope).IsCancellationRequested);
        }));
        Case("非同身份connection不得进入匿名权限替换缝，不重建任何来源", () => Done(() =>
        {
            using var state = new PermissionFixture();
            var client = state.Client(state.A);
            Assert.Throws<InvalidOperationException>(() => state.ReplaceConnection(state.A, client.Connection with { UserId = "different-anonymous-user" }));
            Assert.True(ReferenceEquals(client, state.Client(state.A)));
        }));
    }

    private static PageViewModel CreateAdmin(string kind) => kind switch
    {
        "libraries" => new FnNasLibrariesViewModel(),
        "users" => new FnNasUsersViewModel(),
        "settings" => new FnNasSettingsViewModel(),
        "tasks" => new FnNasTasksViewModel(),
        _ => throw new ArgumentException("Unknown fixture VM")
    };

    private static async Task Prepare(FnNasSessionViewModel vm, PermissionFixture state, string kind, string? sourceKey = null)
    {
        vm.Attach(state.Session, sourceKey);
        await vm.ReloadAsync();
        Assert.True(vm.CanUse && vm.IsReady && !vm.NoticeOpen, "完整VM假读取必须实际成功");
        switch (kind)
        {
            case "libraries":
                var libraries = (FnNasLibrariesViewModel)vm;
                await libraries.OpenAsync(libraries.Libraries[0]);
                break;
            case "users": ((FnNasUsersViewModel)vm).DefaultsCommand.Execute(null); break;
        }
        Assert.True((bool)vm.GetType().GetProperty("CanSave")!.GetValue(vm)!);
    }

    private static void RegisterFnNas()
    {
        foreach (var kind in new[] { "libraries", "users", "settings", "tasks" })
        {
            Case($"完整{kind} VM同页降权完整Content收起＋命令通知，升权恢复且不清集合/草稿", async () =>
            {
                using var state = new PermissionFixture();
                using var vm = (FnNasSessionViewModel)CreateAdmin(kind);
                await Prepare(vm, state, kind);
                var notices = new List<string>();
                vm.PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                var command = (IAsyncRelayCommand)vm.GetType().GetProperty("SaveCommand")!.GetValue(vm)!;
                var commandEvents = 0;
                command.CanExecuteChanged += (_, _) => commandEvents++;
                state.Replace(state.A, false);
                Assert.Equal(Visibility.Collapsed, vm.ContentVisibility);
                Assert.False(vm.CanUse || command.CanExecute(null));
                Assert.True(notices.Contains(nameof(FnNasSessionViewModel.ContentVisibility)) && commandEvents > 0);
                state.Replace(state.A, true);
                Assert.Equal(Visibility.Visible, vm.ContentVisibility);
                Assert.True(command.CanExecute(null));
                Assert.False(vm.NoticeOpen, "恢复后收掉此权限提示，不重置草稿");
            });
            Case($"完整{kind} VM绕过CanExecute直接执行已打开保存命令，降权本地零HTTP写", async () =>
            {
                using var state = new PermissionFixture();
                using var vm = (FnNasSessionViewModel)CreateAdmin(kind);
                await Prepare(vm, state, kind);
                // Fake server stays permissive to distinguish the client guard from server-side rejection.
                state.Replace(state.A, false, updateServer: false);
                await (Task)PermissionFixture.Invoke(vm, "SaveAsync")!;
                Assert.Equal(0, state.Transport.Writes.Count, "不得靠服务端拒绝掩盖旧VM命令守卫");
                state.Replace(state.A, true);
                await (Task)PermissionFixture.Invoke(vm, "SaveAsync")!;
                Assert.Equal(1, state.Transport.Writes.Count, "恢复后可用，真实VM实际假写一次");
            });
            Case($"完整{kind} VM非默认显式sourceKey与重入，A权限不覆盖B", async () =>
            {
                using var state = new PermissionFixture(second: MediaServerKind.FnNas);
                using var vm = (FnNasSessionViewModel)CreateAdmin(kind);
                await Prepare(vm, state, kind, state.B!.Key);
                state.Replace(state.B!, false);
                Assert.Equal(Visibility.Collapsed, vm.ContentVisibility);
                Assert.True(state.A.IsCurrent);
                vm.Attach(state.Session, state.B!.Key);
                Assert.False(vm.CanUse);
                state.Replace(state.B!, true);
                Assert.True(vm.CanUse);
                await vm.ReloadAsync();
                Assert.True(state.Transport.Requests.Where(request => request.Path != "user/info").All(request => request.ServerId == "b"));
            });
        }
        Case("管理VM在途读取true→false→true旧结果不回填，恢复后显式刷新可用", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = new FnNasTasksViewModel();
            vm.Attach(state.Session);
            using var gate = new ReplyGate();
            state.Server(state.A).Gates.Add("GET task/schedule/list", gate);
            var load = vm.ReloadAsync();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            state.Replace(state.A, false);
            state.Replace(state.A, true);
            gate.Release();
            await load;
            Assert.False(vm.Loaded, "ABA之后旧读取不能再成为可执行模板");
            state.Server(state.A).Gates.Clear();
            await vm.ReloadAsync();
            Assert.True(vm.Loaded && vm.CanSave);
        });
        Case("管理VM通知在背景线程排队，执行守卫立即降权，UI回填只能属于本次Attach", async () =>
        {
            using var state = new PermissionFixture(second: MediaServerKind.FnNas);
            using var vm = new FnNasTasksViewModel();
            var queue = new PermissionQueue();
            queue.InContext(() => vm.Attach(state.Session));
            await vm.ReloadAsync();
            var noticeThreads = new List<int>();
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(FnNasSessionViewModel.ContentVisibility)) noticeThreads.Add(Environment.CurrentManagedThreadId); };
            await Task.Run(() => state.Replace(state.A, false));
            Assert.False(vm.CanUse, "排队前执行守卫已经读当前权限");
            Assert.Equal(0, noticeThreads.Count, "不得在后台更新WinUI绑定");
            Assert.True(queue.Posts > 0);
            queue.InContext(() => vm.Attach(state.Session, state.B!.Key));
            var count = noticeThreads.Count;
            queue.Drain();
            Assert.Equal(count, noticeThreads.Count, "旧A通知不能借新B Attach回填");
            Assert.True(vm.CanUse);
        });
        Case("管理VM Cancel/Dispose退订权限通知，旧队列不借重开寿命", async () =>
        {
            using var state = new PermissionFixture();
            var vm = new FnNasTasksViewModel();
            var queue = new PermissionQueue();
            queue.InContext(() => vm.Attach(state.Session));
            await Task.Run(() => state.Replace(state.A, false));
            vm.Cancel();
            queue.Drain();
            Assert.Equal(Visibility.Collapsed, vm.ContentVisibility);
            state.Replace(state.A, true);
            Assert.False(vm.CanUse);
            vm.Dispose();
            var eventInfo = typeof(EmbySession).GetField("PermissionsChanged", PermissionFixture.Members);
            var subscribers = (Delegate?)eventInfo?.GetValue(state.Session);
            Assert.True(subscribers is null || subscribers.GetInvocationList().All(handler => !ReferenceEquals(handler.Target, vm)));
        });
        Case("四张管理页XAML把完整Content绑OneWay，并保留错误/刷新恢复入口", () => Done(() =>
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var name in new[] { "Libraries", "Users", "Settings", "Tasks" })
            {
                var path = Path.Combine(Root, "src", "Momoka.Shell", "Views", "FnNas" + name + "Page.xaml");
                var document = XDocument.Load(path);
                var regions = document.Descendants().Where(element => (element.Attribute("Visibility")?.Value ?? "").Contains("ViewModel.ContentVisibility", StringComparison.Ordinal)).ToArray();
                Assert.Equal(1, regions.Length, "完整管理区域只绑一个权限容器");
                Assert.Contains("Mode=OneWay", regions[0].Attribute("Visibility")!.Value);
                Assert.True(document.Descendants().Any(element => (element.Attribute("Command")?.Value ?? "").Contains("RefreshCommand", StringComparison.Ordinal)));
                Assert.True(document.Descendants().Any(element => element.Attribute(x + "Name")?.Value == "Notice" || element.Name.LocalName == "InfoBar"));
            }
        }));
    }

    private static void RegisterSources()
    {
        foreach (var explicitSource in new[] { false, true })
        {
            Case($"Jellyfin {(explicitSource ? "显式sourceKey" : "默认")} 插件等待期间降权，旧页面结果必须拒绝", async () =>
            {
                using var state = new PermissionFixture(MediaServerKind.Jellyfin, second: explicitSource ? MediaServerKind.Jellyfin : null);
                var source = explicitSource ? state.B! : state.A;
                using var gate = new ReplyGate();
                state.Server(source).Gates.Add("GET web/ConfigurationPages", gate);
                var pages = explicitSource ? state.Capabilities.GetAdminMenuPagesAsync(source.Key) : state.Capabilities.GetAdminMenuPagesAsync();
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                state.Replace(source, false);
                gate.Release();
                Assert.Equal(0, (await pages).Count);
            });
            Case($"Jellyfin {(explicitSource ? "显式sourceKey" : "默认")} 已确认降权时不发插件GET，恢复后可读", async () =>
            {
                using var state = new PermissionFixture(MediaServerKind.Jellyfin, second: explicitSource ? MediaServerKind.Jellyfin : null);
                var source = explicitSource ? state.B! : state.A;
                state.Replace(source, false);
                var pages = explicitSource ? await state.Capabilities.GetAdminMenuPagesAsync(source.Key) : await state.Capabilities.GetAdminMenuPagesAsync();
                Assert.Equal(0, pages.Count);
                Assert.Equal(0, state.Transport.Requests.Count);
                state.Replace(source, true);
                pages = explicitSource ? await state.Capabilities.GetAdminMenuPagesAsync(source.Key) : await state.Capabilities.GetAdminMenuPagesAsync();
                Assert.True(pages.Count > 0);
            });
        }
    }

    private static void RegisterPermissionEdges()
    {
        Case("管理入口读取Allowed后重入降权，新权限代次不能让失效请求开始", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = new PermissionDispatchVm();
            vm.Attach(state.Session);
            // Narrow reentrancy boundary around the actual Allowed/RunAsync, not a copied guard.
            vm.AfterAllowed = () => state.Replace(state.A, false);
            await vm.ReloadAsync();
            Assert.Equal(0, vm.ActionCalls, "首次管理员读取不能替代实际请求开始前的当前权限检查");
            Assert.False(vm.CanUse);
            Assert.Equal(0, state.Transport.Requests.Count);
        });
        foreach (var kind in new[] { MediaServerKind.FnNas, MediaServerKind.Jellyfin, MediaServerKind.Emby })
        {
            Case($"{kind} 新客户端权限未知null不能继承旧Probe true/false，恢复已知位不联网", async () =>
            {
                using var state = new PermissionFixture(kind);
                await state.Capabilities.ProbeAsync();
                state.Replace(state.A, false);
                state.Replace(state.A, null);
                Assert.Null(state.Capabilities.IsAdministrator);
                Assert.Null(state.Permission(state.A));
                state.Replace(state.A, true);
                Assert.Equal<bool?>(true, state.Capabilities.IsAdministrator);
            });
            Case($"{kind} 同值true新确认覆盖旧Probe false，版本/权限缓存寿命分开", async () =>
            {
                using var state = new PermissionFixture(kind);
                // GET reports ordinary rights but the separately confirmed replacement says admin.
                state.Server(state.A).Administrator = false;
                if (kind == MediaServerKind.Emby)
                {
                    state.Transport.RestrictEmbySystem = true;
                }
                await state.Capabilities.ProbeAsync();
                Assert.Equal<bool?>(false, state.Capabilities.IsAdministrator);
                var version = state.Capabilities.ServerVersion;
                state.Replace(state.A, true);
                Assert.Equal<bool?>(true, state.Capabilities.IsAdministrator);
                Assert.Equal(version, state.Capabilities.ServerVersion);
            });
        }
        Case("旧Scope执行无HTTP委托读同身份最新client但Connection/绑定播放来源仍原快照", async () =>
        {
            using var state = new PermissionFixture();
            var scope = state.Session.Capture();
            var snapshot = scope.Connection;
            var item = scope.Bind(new EmbyItem { Id = "offline-media" });
            state.Replace(scope, false);
            var current = await scope.ExecuteAsync((client, token) => Task.FromResult(client), CancellationToken.None);
            Assert.True(ReferenceEquals(current, state.Client(scope)));
            Assert.False(ReferenceEquals(current.Connection, snapshot));
            Assert.True(ReferenceEquals(snapshot, item.Source!.Connection));
            Assert.Equal(0, state.Transport.Requests.Count);
        });
        Case("撤销来源后匿名权限替换立即取消且不再发事件、不影响B", () => Done(() =>
        {
            using var state = new PermissionFixture(second: MediaServerKind.FnNas);
            var events = 0;
            using var watch = state.WatchPermissions(_ => events++);
            state.Revoke(state.A);
            state.Select(state.B!);
            Assert.Throws<OperationCanceledException>(() => state.Replace(state.A, false));
            Assert.Equal(0, events);
            Assert.Equal<bool?>(true, state.Permission(state.B!));
        }));
        Case("非默认scope权限遵循所属身份，切默认不改变它的权限或寿命", () => Done(() =>
        {
            using var state = new PermissionFixture(second: MediaServerKind.FnNas);
            var captured = state.Session.Capture();
            state.Select(state.B!);
            state.Replace(state.A, false);
            Assert.Equal<bool?>(false, state.Permission(captured));
            Assert.True(captured.IsAvailable && !captured.IsCurrent);
            Assert.Equal<bool?>(true, state.Session.Connection!.IsAdministrator);
            state.Replace(state.A, true);
            Assert.True(captured.IsAvailable && !state.Lifetime(captured).IsCancellationRequested);
        }));
        Case("未知权限null即时收起四个管理VM，升权通知恢复并收掉Attach时权限提示", async () =>
        {
            using var state = new PermissionFixture(administrator: null);
            using var vm = new FnNasTasksViewModel();
            vm.Attach(state.Session);
            Assert.Equal(Visibility.Collapsed, vm.ContentVisibility);
            Assert.True(vm.NoticeOpen);
            state.Replace(state.A, true);
            Assert.Equal(Visibility.Visible, vm.ContentVisibility);
            Assert.False(vm.NoticeOpen);
            await vm.ReloadAsync();
            Assert.True(vm.CanSave);
            state.Replace(state.A, null);
            Assert.Equal(Visibility.Collapsed, vm.ContentVisibility);
            Assert.False(vm.SaveCommand.CanExecute(null));
        });
        Case("权限通知同页保留任务集合与非默认偏好、不自动重读/重写", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = new FnNasTasksViewModel();
            await Prepare(vm, state, "tasks");
            vm.Enabled = false;
            var collection = vm.Tasks;
            var rows = vm.Tasks.ToArray();
            var reads = state.Transport.Requests.Count;
            state.Replace(state.A, false);
            state.Replace(state.A, true);
            Assert.False(vm.Enabled);
            Assert.True(ReferenceEquals(collection, vm.Tasks));
            Assert.True(rows.SequenceEqual(vm.Tasks));
            Assert.Equal(reads, state.Transport.Requests.Count);
            Assert.Equal(0, state.Transport.Writes.Count);
        });
        Case("后台旧权限通知排队后新代读取开始，旧UI回调不取消新请求", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = new FnNasTasksViewModel();
            var queue = new PermissionQueue();
            queue.InContext(() => vm.Attach(state.Session));
            await Task.Run(() => state.Replace(state.A, false));
            await Task.Run(() => state.Replace(state.A, true));
            using var gate = new ReplyGate();
            state.Server(state.A).Gates.Add("GET task/schedule/list", gate);
            var load = vm.ReloadAsync();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Drain();
            Assert.True(vm.Busy, "旧通知不能标完新读取");
            gate.Release();
            await load;
            Assert.True(vm.Loaded && vm.CanSave);
        });
        Case("确认框等待期间降权并立即恢复，旧管理命令仍零写", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = new FnNasSettingsViewModel();
            await Prepare(vm, state, "settings");
            using var gate = new ReplyGate();
            vm.UseConfirm(async (_, _, _) => { await gate.WaitAsync(); return true; });
            var pending = vm.RebuildCommand.ExecuteAsync(null);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            state.Replace(state.A, false);
            state.Replace(state.A, true);
            gate.Release();
            await pending;
            Assert.Equal(0, state.Transport.Writes.Count);
            Assert.False(vm.Rebuilding);
        });
        Case("显式同身份新client经SourcesChanged通知也刷新已打开管理VM，非管理账号页不订阅", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = new FnNasTasksViewModel();
            await Prepare(vm, state, "tasks");
            var notices = 0;
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(FnNasSessionViewModel.ContentVisibility)) notices++; };
            var identity = state.Identity(state.A);
            var connection = state.Client(state.A).Connection with { IsAdministrator = false };
            identity.GetType().GetProperty("Client")!.SetValue(identity, new EmbyClient((EmbyHttp)typeof(EmbySession).GetField("_http", PermissionFixture.Members)!.GetValue(state.Session)!, connection));
            PermissionFixture.Invoke(state.Session, "NotifySourcesChanged");
            Assert.False(vm.CanUse);
            Assert.True(notices > 0);
            using var account = new ReadOnlyAccountVm();
            account.Attach(state.Session);
            var permissions = typeof(EmbySession).GetField("PermissionsChanged", PermissionFixture.Members)?.GetValue(state.Session) as Delegate;
            Assert.True(permissions is null || permissions.GetInvocationList().All(handler => !ReferenceEquals(handler.Target, account)));
            Assert.Equal(Visibility.Visible, account.ContentVisibility);
        });
        Case("权限恢复前已Dispose的管理VM不重新Attach、无订阅或可执行命令", () => Done(() =>
        {
            using var state = new PermissionFixture();
            var vm = new FnNasTasksViewModel();
            vm.Attach(state.Session);
            vm.Dispose();
            vm.Attach(state.Session);
            Assert.False(vm.CanUse);
            state.Replace(state.A, false);
            state.Replace(state.A, true);
            Assert.Equal(Visibility.Collapsed, vm.ContentVisibility);
        }));
    }

    private sealed class PermissionDispatchVm : FnNasAdminViewModel
    {
        private Action? _afterAllowed;
        internal Action? AfterAllowed { set => _afterAllowed = value; }
        internal int ActionCalls { get; private set; }
        protected override bool Allowed(EmbySessionScope scope)
        {
            var allowed = base.Allowed(scope);
            if (allowed) Interlocked.Exchange(ref _afterAllowed, null)?.Invoke();
            return allowed;
        }
        public override Task ReloadAsync() => RunAsync("anonymous dispatch guard", (_, _) =>
        {
            ActionCalls++;
            return Task.CompletedTask;
        });
    }

    private sealed class ReadOnlyAccountVm : FnNasSessionViewModel
    {
        public override Task ReloadAsync() => Task.CompletedTask;
    }
    private static void RegisterSettings()
    {
        Case("实际SettingsVM/原样Page同页降升权目录、完整组、落点、草稿、重入一致", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = _shell!.Settings(state.Capabilities);
            _shell.AddDraft(vm.Actual);
            var sections = vm.Collection("Sections");
            using var page = new PermissionPage(vm, state.Session);
            await page.Refresh();
            Assert.True(vm.GroupNames.Contains("影视服务器"));
            vm.SelectedCategory = vm.Key("媒体库");
            state.Replace(state.A, false);
            page.DispatcherQueue.Drain();
            Assert.True(vm.Hidden);
            Assert.False(vm.GroupNames.Contains("影视服务器"));
            Assert.False(vm.Labels.Contains("媒体库"));
            Assert.True(vm.Labels.Contains("播放偏好"));
            Assert.True(page.Releases > 0, "已打开管理页退回本机卡");
            state.Replace(state.A, true);
            page.DispatcherQueue.Drain();
            Assert.False(vm.Hidden);
            Assert.True(vm.GroupNames.Contains("影视服务器") && vm.Labels.Contains("媒体库"));
            page.Hide();
            state.Replace(state.A, false);
            page.Open(state.Session);
            await page.Refresh();
            Assert.True(vm.Hidden);
            Assert.True(ReferenceEquals(sections, vm.Collection("Sections")));
            Assert.Equal(1, ((System.Collections.IList)sections).Count, "不重建所有设置卡或丢草稿");
            _shell.VerifyPageHash();
        });
        Case("实际SettingsVM显式B管理旧入口在队列刷新前就拒执行，默认A不被改", async () =>
        {
            using var state = new PermissionFixture(MediaServerKind.Jellyfin, second: MediaServerKind.FnNas);
            using var vm = _shell!.Settings(state.Capabilities);
            using var page = new PermissionPage(vm, state.Session);
            await page.Refresh();
            var oldKey = vm.Key("媒体库", state.B!.Key);
            vm.SelectedCategory = oldKey;
            state.Replace(state.B!, false);
            Assert.False(vm.CanOpenServerCategory(oldKey), "旧显式分类不能只凭SourceKey存在放行");
            page.DispatcherQueue.Drain();
            Assert.False(vm.LabelsFor(state.B!.Key).Contains("媒体库"));
            Assert.True(vm.LabelsFor(state.B!.Key).Contains("播放偏好"));
            state.Replace(state.B!, true);
            page.DispatcherQueue.Drain();
            Assert.True(vm.CanOpenServerCategory(vm.Key("媒体库", state.B!.Key)));
            Assert.True(state.A.IsCurrent);
        });
        Case("实际SettingsVM未知/失效显式管理键fail-closed，账号恢复入口仍可达", async () =>
        {
            using var state = new PermissionFixture(MediaServerKind.Jellyfin, second: MediaServerKind.FnNas);
            using var vm = _shell!.Settings(state.Capabilities);
            await vm.RefreshServerAdministrationAsync();
            var key = vm.Key("媒体库", state.B!.Key);
            state.Revoke(state.B!);
            Assert.False(vm.CanOpenServerCategory(key));
            Assert.False(vm.CanOpenServerCategory("unknown-source\u001f媒体库"));
        });
        Case("原样SettingsPage Hide旧权限回调不借重开，同身份重入当场读新权限", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = _shell!.Settings(state.Capabilities);
            using var page = new PermissionPage(vm, state.Session);
            await page.Refresh();
            state.Replace(state.A, false);
            page.Hide();
            state.Replace(state.A, true);
            page.Open(state.Session);
            var groups = vm.CategoryGroups;
            page.DispatcherQueue.Drain();
            Assert.True(ReferenceEquals(groups, vm.CategoryGroups), "旧排队回调不得重建新visit目录");
            await page.Refresh();
            Assert.False(vm.Hidden);
        });
    }
    private static void RegisterSettingsEdges()
    {
        Case("实际SettingsVM选择降权回退通知中SelectedEntry/Category已一致，不能再看旧页", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = _shell!.Settings(state.Capabilities);
            await vm.RefreshServerAdministrationAsync();
            vm.SelectedCategory = vm.Key("媒体库");
            var inconsistent = 0;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == "SelectedCategory")
                {
                    var entry = _shell.Get(vm.Actual, "SelectedEntry");
                    if (entry is not null && _shell.Get<string>(entry, "Key") != vm.SelectedCategory) inconsistent++;
                }
            };
            state.Replace(state.A, false);
            await vm.RefreshServerAdministrationAsync();
            Assert.Equal(0, inconsistent);
            Assert.False(vm.ShowsHosted);
        });
        Case("实际SettingsVM三产品已Probe缓存及打开Page同身份降升，Jellyfin/Emby旧整组不可见", async () =>
        {
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas })
            {
                using var state = new PermissionFixture(kind);
                await state.Capabilities.ProbeAsync();
                using var vm = _shell!.Settings(state.Capabilities);
                using var page = new PermissionPage(vm, state.Session);
                await page.Refresh();
                state.Replace(state.A, false);
                page.DispatcherQueue.Drain();
                Assert.True(vm.Hidden);
                Assert.False(vm.Labels.Contains("控制台"));
                state.Replace(state.A, true);
                page.DispatcherQueue.Drain();
                Assert.False(vm.Hidden);
            }
        });
        Case("原样Page后台通知只排队，降权CanOpen立即拒绝而UI按当前visit更新", async () =>
        {
            using var state = new PermissionFixture();
            using var vm = _shell!.Settings(state.Capabilities);
            using var page = new PermissionPage(vm, state.Session);
            await page.Refresh();
            var old = vm.Key("媒体库");
            await Task.Run(() => state.Replace(state.A, false));
            Assert.False(vm.CanOpenServerCategory(old));
            Assert.True(vm.Labels.Contains("媒体库"), "页面绑定只能在UI队列内重算");
            page.DispatcherQueue.Drain();
            Assert.False(vm.Labels.Contains("媒体库"));
        });
        Case("实际SettingsVM插件在途降权ABA，新代页面刷新不接回旧plugin", async () =>
        {
            using var state = new PermissionFixture(MediaServerKind.Jellyfin);
            using var vm = _shell!.Settings(state.Capabilities);
            using var gate = new ReplyGate();
            state.Server(state.A).PluginName = "old-plugin";
            state.Server(state.A).Gates.Add("GET web/ConfigurationPages", gate);
            var old = vm.RefreshServerAdministrationAsync();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            state.Replace(state.A, false);
            state.Replace(state.A, true);
            state.Server(state.A).PluginName = "new-plugin";
            state.Server(state.A).Gates.Clear();
            await vm.RefreshServerAdministrationAsync();
            gate.Release();
            await old;
            Assert.True(vm.Labels.Contains("new-plugin"));
            Assert.False(vm.Labels.Contains("old-plugin"));
        });
    }
}

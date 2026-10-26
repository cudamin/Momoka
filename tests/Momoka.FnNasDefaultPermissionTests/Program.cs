using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal static class Program
{
    private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<(string Name, Func<Task> Run)> Cases = [];
    private static string _root = "";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length != 1) throw new ArgumentException("必须传入当前工作树的绝对根路径。");
        _root = Path.GetFullPath(args[0]);
        var project = Path.Combine(_root, "tests", "Momoka.FnNasDefaultPermissionTests");
        Assert.True(Path.IsPathFullyQualified(args[0]) && typeof(Program).Assembly.Location.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(_root, "src", "Momoka.Shell", "ViewModels", "FnNasUsersViewModel.cs")));
        Console.WriteLine("R022 真实 Users/Session/Page/Rows + MVVM generator + 假 FnNas HTTP；无登录、真实网络或 UI。");
        Register();
        var failed = 0;
        foreach (var (name, run) in Cases)
        {
            try
            {
                await run();
                Console.WriteLine("通过  " + name);
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("失败  " + name + "\n" + error);
            }
        }
        Console.WriteLine($"共 {Cases.Count} 项，通过 {Cases.Count - failed}，失败 {failed}，跳过 0");
        return failed == 0 ? 0 : 1;
    }

    private static void Register()
    {
        Case("首次未读不是零权限，生成命令与直接入口均关闭", async () =>
        {
            using var state = new State();
            state.Defaults();
            state.NewUser();
            await state.Save();
            Assert.Equal(0, state.Http.Writes.Count);
            Assert.False(state.Vm.Editing);
            Assert.Equal(-1, state.Vm.Permission);
            Assert.Equal(Visibility.Collapsed, state.Vm.DefaultActionsVisibility);
            Assert.False(state.Vm.DefaultsCommand.CanExecute(null));
            Assert.False(state.Vm.NewUserCommand.CanExecute(null));
            Assert.True(state.Vm.RefreshCommand.CanExecute(null));
        });
        foreach (var failure in new[] { ReadFailure.Server, ReadFailure.Network })
        {
            Case($"库成功／模板{failure}失败，直接默认权限保存写数零", async () =>
            {
                using var state = new State();
                state.A.DefaultFailure = failure;
                await state.Vm.ReloadAsync();
                state.Defaults();
                await state.Save();
                Assert.Equal(0, state.Http.Writes.Count, "失败模板不可发零权限覆盖正文");
                Assert.False(state.Vm.Editing);
                Assert.Equal(-1, state.Vm.Permission);
                FailureState(state);
                Assert.True(state.Loaded("_librariesLoaded"));
                Assert.False(state.Loaded("_defaultPermissionLoaded"));
            });
            Case($"模板成功／库{failure}失败，直接默认权限保存写数零", async () =>
            {
                using var state = new State();
                state.A.LibraryFailure = failure;
                await state.Vm.ReloadAsync();
                state.Defaults();
                await state.Save();
                Assert.Equal(0, state.Http.Writes.Count);
                Assert.True(state.Http.Requests.Any(request => request.Path == "manager/template/permission" && request.Method == "GET"), "两项读数独立完成，不因库失败跳过模板");
                Assert.True(state.Loaded("_defaultPermissionLoaded"));
                Assert.False(state.Loaded("_librariesLoaded"));
                Assert.Equal(-1, state.Vm.Permission);
                FailureState(state);
            });
        }
        foreach (var failLibrary in new[] { false, true })
        {
            Case($"未读成功的{(failLibrary ? "库" : "模板")}不可 Open New 并创建", async () =>
            {
                using var state = new State();
                if (failLibrary) state.A.LibraryFailure = ReadFailure.Server;
                else state.A.DefaultFailure = ReadFailure.Server;
                await state.Vm.ReloadAsync();
                state.NewUser();
                state.SetNewIdentity();
                await state.Save();
                Assert.Equal(0, state.Http.Writes.Count);
                Assert.False(state.Vm.Editing);
            });
            Case($"绕过 CanExecute／伪造 Editing，{(failLibrary ? "库" : "模板")}失败仍零写", async () =>
            {
                using var state = new State();
                if (failLibrary) state.A.LibraryFailure = ReadFailure.Server;
                else state.A.DefaultFailure = ReadFailure.Server;
                await state.Vm.ReloadAsync();
                state.Vm.EditingDefault = true;
                state.Vm.Editing = true;
                state.Vm.Permission = 0;
                await state.Save();
                Assert.Equal(0, state.Http.Writes.Count, "执行方法必须重核已成功读取，不只依赖按钮 CanExecute");
            });
            Case($"旧成功后刷新{(failLibrary ? "库" : "模板")}失败，不用旧模板", async () =>
            {
                using var state = new State();
                await state.Vm.ReloadAsync();
                state.Defaults();
                Assert.Equal(2, state.Vm.Permission);
                if (failLibrary) state.A.LibraryFailure = ReadFailure.Server;
                else state.A.DefaultFailure = ReadFailure.Server;
                await state.Vm.ReloadAsync();
                state.Defaults();
                await state.Save();
                Assert.Equal(0, state.Http.Writes.Count);
                Assert.Equal(-1, state.Vm.Permission);
                Assert.Equal(0, state.Vm.Libraries.Count);
                FailureState(state);
            });
        }
        foreach (var source in new[] { "a", "b" })
        {
            Case($"同 VM 重附加 {source} 读取失败，旧模板／库名／输入清空", async () =>
            {
                using var state = new State();
                await state.Vm.ReloadAsync();
                state.Defaults();
                state.Vm.UserName = "old-draft";
                state.Vm.Password = state.Vm.ConfirmPassword = "fixture-old";
                state.Source(source).DefaultFailure = ReadFailure.Server;
                state.Attach(source);
                Assert.False(state.Loaded("_defaultPermissionLoaded"));
                Assert.False(state.Loaded("_librariesLoaded"));
                await state.Vm.ReloadAsync();
                state.Defaults();
                await state.Save();
                Assert.Equal(0, state.Http.Writes.Count);
                Assert.Equal(-1, state.Vm.Permission);
                Assert.Equal(0, state.Vm.Libraries.Count);
                Assert.Equal("", state.Vm.UserName);
                Assert.Equal("", state.Vm.Password);
                Assert.Equal("", state.Vm.ConfirmPassword);
                Assert.Equal(0, ((Dictionary<string, string>)typeof(FnNasUsersViewModel).GetField("_libraryNames", Members)!.GetValue(state.Vm)!).Count, "未完整读成功的旧库名不应污染下一来源");
            });
        }
        Case("库先成功／模板仍在途，未知状态与零写保留", async () =>
        {
            using var state = new State();
            using var gate = new ReplyGate();
            state.A.DefaultGate = gate;
            var load = state.Vm.ReloadAsync();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            state.Defaults();
            state.NewUser();
            await state.Save();
            Assert.Equal(0, state.Http.Writes.Count);
            Assert.True(state.Vm.Busy);
            Assert.Equal(-1, state.Vm.Permission);
            gate.Release();
            await load;
            state.Defaults();
            Assert.Equal(2, state.Vm.Permission);
        });
        Case("模板先成功／库仍在途再失败，不打开半加载编辑器", async () =>
        {
            using var state = new State();
            using var gate = new ReplyGate();
            state.A.LibraryGate = gate;
            state.A.LibraryFailure = ReadFailure.Server;
            var load = state.Vm.ReloadAsync();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(state.Http.Requests.Any(request => request.Path == "manager/template/permission"), "模板读不能被尚在途的库读串行挡住");
            state.Defaults();
            Assert.False(state.Vm.Editing);
            gate.Release();
            await load;
            await state.Save();
            Assert.Equal(0, state.Http.Writes.Count);
            FailureState(state);
        });
        foreach (var permission in new[] { 0, 2 })
        {
            Case($"真实权限 {permission} 正常保存一次并保留未知 JSON", async () =>
            {
                using var state = new State();
                state.A.Default = Template(permission, "a-movie", "gone");
                await state.Vm.ReloadAsync();
                state.Defaults();
                Assert.Equal(permission, state.Vm.Permission);
                Assert.Equal(Visibility.Collapsed, state.Vm.LibrariesVisibility);
                await state.Save();
                var write = state.Http.Writes.Single();
                Assert.Equal("r022-a.invalid", write.Host);
                Assert.Equal("manager/template/permission", write.Path);
                Assert.Equal(permission, write.Body["media_permission"]!.GetValue<int>());
                Assert.Equal(0, write.Body["mediadb_list"]!.AsArray().Count);
                AssertUnknown(write.Body);
                Assert.False(state.Vm.Editing);
                Assert.Equal(InfoBarSeverity.Success, state.Vm.NoticeSeverity);
            });
        }
        Case("真实部分权限保留未知库 id，编辑只改变指定字段且只写一次", async () =>
        {
            using var state = new State();
            state.A.Default = Template(1, "a-movie", "gone");
            state.A.Default["mediadb_list"] = new JsonArray(new JsonObject { ["guid"] = "a-movie" }, "gone");
            var before = state.A.Default.DeepClone();
            await state.Vm.ReloadAsync();
            state.Defaults();
            Assert.Equal(1, state.Vm.Permission);
            Assert.Equal(Visibility.Visible, state.Vm.LibrariesVisibility);
            Assert.True(state.Vm.Libraries.Single(row => row.Id == "gone").Selected);
            state.Vm.Libraries.Single(row => row.Id == "a-movie").Selected = false;
            state.Vm.Libraries.Single(row => row.Id == "a-tv").Selected = true;
            await state.Save();
            var write = state.Http.Writes.Single();
            Assert.Equal(1, write.Body["media_permission"]!.GetValue<int>());
            Assert.Equal("a-tv,gone", string.Join(",", FnNasJson.Strings(write.Body["mediadb_list"]).Order(StringComparer.Ordinal)));
            AssertUnknown(write.Body);
            Assert.True(JsonNode.DeepEquals(before, state.A.Default), "编辑不污染读回文档");
        });
        Case("成功读回的空库集合不等于未读，全部权限仍可保存", async () =>
        {
            using var state = new State();
            state.A.Libraries = new JsonArray();
            await state.Vm.ReloadAsync();
            state.Defaults();
            Assert.True(state.Vm.CanSave);
            await state.Save();
            Assert.Equal(1, state.Http.Writes.Count);
        });
        Case("失败后刷新恢复当前部分配置与新库，不再使用旧值", async () =>
        {
            using var state = new State();
            state.A.DefaultFailure = ReadFailure.Server;
            await state.Vm.ReloadAsync();
            state.A.DefaultFailure = ReadFailure.None;
            state.A.Default = Template(1, "fresh", "unknown-now");
            state.A.Libraries = new JsonArray(new JsonObject { ["guid"] = "fresh", ["name"] = "恢复库" });
            await state.Vm.RefreshCommand.ExecuteAsync(null);
            state.Defaults();
            Assert.Equal(1, state.Vm.Permission);
            Assert.Equal("fresh,unknown-now", string.Join(",", state.Vm.Libraries.Select(row => row.Id).Order(StringComparer.Ordinal)));
            await state.Save();
            Assert.Equal(1, state.Http.Writes.Count);
            Assert.Equal("fresh,unknown-now", string.Join(",", FnNasJson.Strings(state.Http.Writes.Single().Body["mediadb_list"]).Order(StringComparer.Ordinal)));
        });
        Case("保存失败保留部分权限草稿与错误，重试一次不重复隐式写入", async () =>
        {
            using var state = new State();
            state.A.Default = Template(1, "a-movie", "gone");
            await state.Vm.ReloadAsync();
            state.Defaults();
            state.Vm.Libraries.Single(row => row.Id == "a-tv").Selected = true;
            state.A.WriteFailure = true;
            await state.Save();
            Assert.Equal(1, state.Http.Writes.Count);
            Assert.True(state.Vm.Editing);
            Assert.True(state.Vm.Libraries.Single(row => row.Id == "a-tv").Selected);
            Assert.Equal(1, state.Vm.Permission);
            Assert.True(state.Vm.CanSave);
            Assert.Equal(InfoBarSeverity.Error, state.Vm.NoticeSeverity);
            state.A.WriteFailure = false;
            await state.Save();
            Assert.Equal(2, state.Http.Writes.Count);
            AssertUnknown(state.Http.Writes.Last().Body);
            Assert.False(state.Vm.Editing);
        });
        Case("保存失败后取消编辑，再打开沿用已读模板而非失败草稿", async () =>
        {
            using var state = new State();
            await state.Vm.ReloadAsync();
            state.Defaults();
            state.Vm.Permission = 1;
            state.A.WriteFailure = true;
            await state.Save();
            state.Back();
            Assert.False(state.Vm.Editing);
            state.Defaults();
            Assert.Equal(2, state.Vm.Permission);
            Assert.Equal(1, state.Http.Writes.Count);
        });
        Case("离页取消后直接保存零写，重附加保持未知状态", async () =>
        {
            using var state = new State();
            await state.Vm.ReloadAsync();
            state.Defaults();
            state.Vm.Cancel();
            await state.Save();
            Assert.Equal(0, state.Http.Writes.Count);
            state.Attach("b");
            Assert.Equal(-1, state.Vm.Permission);
            Assert.Equal(0, state.Vm.Libraries.Count);
            Assert.False(state.Vm.Editing);
        });
        Case("旧来源在途读遇取消，新来源失败不可接收旧模板", async () =>
        {
            using var state = new State();
            using var gate = new ReplyGate();
            state.A.DefaultGate = gate;
            var old = state.Vm.ReloadAsync();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            state.B.DefaultFailure = ReadFailure.Server;
            state.Attach("b");
            await state.Vm.ReloadAsync();
            gate.Release();
            await old;
            state.Defaults();
            await state.Save();
            Assert.Equal(0, state.Http.Writes.Count);
            Assert.Equal(-1, state.Vm.Permission);
            Assert.False(state.Vm.Editing);
        });
        Case("显式来源仍绑定原 scope，默认来源切换不迁移权限写", async () =>
        {
            using var state = new State();
            await state.Vm.ReloadAsync();
            state.Defaults();
            state.SelectDefault("b");
            await state.Save();
            Assert.Equal("r022-a.invalid", state.Http.Writes.Single().Host);
        });
        Case("默认权限写在途离页，新来源失败不接收旧成功缓存", async () =>
        {
            using var state = new State();
            await state.Vm.ReloadAsync();
            state.Defaults();
            using var gate = new ReplyGate();
            state.A.WriteGate = gate;
            var save = state.Save();
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            state.B.LibraryFailure = ReadFailure.Server;
            state.Attach("b");
            await state.Vm.ReloadAsync();
            gate.Release();
            await save;
            state.Defaults();
            await state.Save();
            Assert.Equal(1, state.Http.Writes.Count);
            Assert.Equal("r022-a.invalid", state.Http.Writes.Single().Host);
            Assert.Equal(-1, state.Vm.Permission);
            Assert.False(state.Vm.Editing);
        });
        Case("默认权限写成功／列表刷新失败，保存不可再次提交", async () =>
        {
            using var state = new State();
            await state.Vm.ReloadAsync();
            state.Defaults();
            state.A.UsersFailure = ReadFailure.Server;
            await state.Save();
            await state.Save();
            Assert.Equal(1, state.Http.Writes.Count);
            Assert.False(state.Vm.Editing);
            Assert.Equal(InfoBarSeverity.Error, state.Vm.NoticeSeverity);
        });
        Case("已读默认部分配置才允许新增，创建实际一次且字段正确", async () =>
        {
            using var state = new State();
            state.A.Default = Template(1, "a-movie", "gone");
            await state.Vm.ReloadAsync();
            state.NewUser();
            state.SetNewIdentity();
            await state.Save();
            var write = state.Http.Writes.Single();
            Assert.Equal("v2/manager/user/create", write.Path);
            Assert.Equal("fixture-new", write.Body["username"]!.GetValue<string>());
            Assert.Equal(0, write.Body["is_admin"]!.GetValue<int>());
            Assert.Equal(1, write.Body["media_permission"]!.GetValue<int>());
            Assert.Equal("a-movie,gone", string.Join(",", FnNasJson.Strings(write.Body["mediadb_list"]).Order(StringComparer.Ordinal)));
        });
        Case("缺失或未知默认权限不是零权限，可刷新但不可提交", async () =>
        {
            using var state = new State();
            state.A.Default = new JsonObject { ["mediadb_list"] = new JsonArray(), ["future"] = "keep" };
            await state.Vm.ReloadAsync();
            state.Defaults();
            await state.Save();
            Assert.Equal(0, state.Http.Writes.Count);
            Assert.Equal(-1, state.Vm.Permission);
            FailureState(state);
        });
        Case("权限改为未知实际零写，合法值恢复通知且取消不受保存门控绑死", async () =>
        {
            using var state = new State();
            await state.Vm.ReloadAsync();
            state.Defaults();
            var changes = 0;
            state.Vm.SaveCommand.CanExecuteChanged += (_, _) => changes++;
            state.Vm.Permission = -1;
            await state.Save();
            Assert.Equal(0, state.Http.Writes.Count);
            Assert.False(state.Vm.SaveCommand.CanExecute(null));
            Assert.True(state.Vm.BackCommand.CanExecute(null));
            Assert.True(changes > 0, "新增权限有效性守卫须同步生成命令通知");
            changes = 0;
            state.Vm.Permission = 1;
            Assert.True(state.Vm.SaveCommand.CanExecute(null));
            Assert.True(changes > 0);
            state.Back();
            Assert.False(state.Vm.Editing);
            Assert.Equal(0, state.Http.Writes.Count);
        });
        Case("换来源失败后刷新恢复只显示新来源库与模板", async () =>
        {
            using var state = new State();
            await state.Vm.ReloadAsync();
            state.Defaults();
            state.B.DefaultFailure = ReadFailure.Server;
            state.Attach("b");
            await state.Vm.ReloadAsync();
            state.B.DefaultFailure = ReadFailure.None;
            state.B.Default = Template(1, "b-tv", "b-unknown");
            await state.Vm.RefreshCommand.ExecuteAsync(null);
            state.Defaults();
            Assert.Equal(1, state.Vm.Permission);
            Assert.False(state.Vm.Libraries.Any(row => row.Id.StartsWith("a-", StringComparison.Ordinal)));
            await state.Save();
            var write = state.Http.Writes.Single();
            Assert.Equal("r022-b.invalid", write.Host);
            Assert.Equal("b-tv,b-unknown", string.Join(",", FnNasJson.Strings(write.Body["mediadb_list"]).Order(StringComparer.Ordinal)));
        });
        Case("列表／库绑定集合在刷新和重附加中保持实例稳定", async () =>
        {
            using var state = new State();
            var users = state.Vm.Users;
            var libraries = state.Vm.Libraries;
            await state.Vm.ReloadAsync();
            state.Defaults();
            state.Back();
            await state.Vm.ReloadAsync();
            state.Attach("b");
            await state.Vm.ReloadAsync();
            state.Defaults();
            Assert.True(ReferenceEquals(users, state.Vm.Users));
            Assert.True(ReferenceEquals(libraries, state.Vm.Libraries));
        });
        Case("真实页面仅隐藏未就绪的两入口，进度／错误／刷新接线保留", () =>
        {
            var document = XDocument.Load(Path.Combine(_root, "src", "Momoka.Shell", "Views", "FnNasUsersPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var nodes = document.Descendants().ToArray();
            foreach (var name in new[] { "FnNasUsersNew", "FnNasUsersDefaults" })
                Assert.Equal("{x:Bind ViewModel.DefaultActionsVisibility, Mode=OneWay}", (string?)nodes.Single(node => (string?)node.Attribute(x + "Name") == name).Attribute("Visibility"));
            var refresh = nodes.Single(node => (string?)node.Attribute(x + "Name") == "FnNasUsersRefresh");
            Assert.Equal("{x:Bind ViewModel.RefreshCommand}", (string?)refresh.Attribute("Command"));
            Assert.True(refresh.Attribute("Visibility") is null);
            Assert.Equal("{x:Bind ViewModel.BusyVisibility, Mode=OneWay}", (string?)nodes.Single(node => node.Name.LocalName == "ProgressBar").Attribute("Visibility"));
            Assert.Equal("{x:Bind ViewModel.NoticeVisibility, Mode=OneWay}", (string?)nodes.Single(node => node.Name.LocalName == "InfoBar").Attribute("Visibility"));
            return Task.CompletedTask;
        });
        foreach (var value in new JsonNode?[] { JsonValue.Create(9), JsonValue.Create("not-a-permission"), null })
            Case($"默认权限无效值 {value?.ToJsonString() ?? "null"} 不猜成零权限", async () =>
            {
                using var state = new State();
                state.A.Default["media_permission"] = value?.DeepClone();
                await state.Vm.ReloadAsync();
                state.Defaults();
                await state.Save();
                Assert.Equal(0, state.Http.Writes.Count);
                Assert.Equal(-1, state.Vm.Permission);
                FailureState(state);
            });
    }

    private static void Case(string name, Func<Task> run) => Cases.Add((name, run));
    private static void FailureState(State state)
    {
        Assert.True(state.Vm.NoticeOpen);
        Assert.Equal(InfoBarSeverity.Error, state.Vm.NoticeSeverity);
        Assert.False(state.Vm.Busy);
        Assert.True(state.Vm.RefreshCommand.CanExecute(null));
        Assert.False(state.Vm.DefaultsCommand.CanExecute(null));
        Assert.False(state.Vm.NewUserCommand.CanExecute(null));
        Assert.Equal(Visibility.Collapsed, state.Vm.DefaultActionsVisibility);
        Assert.True(typeof(FnNasUsersViewModel).GetField("_defaultPermission", Members)!.GetValue(state.Vm) is null);
    }
    private static JsonObject Template(int permission, params string[] ids) => new()
    {
        ["media_permission"] = permission,
        ["mediadb_list"] = FnNasJson.Array(ids),
        ["future"] = new JsonObject { ["switch"] = true, ["items"] = new JsonArray(3, "未知", null) },
        ["future_null"] = null
    };
    private static void AssertUnknown(JsonObject body)
    {
        Assert.True(JsonNode.DeepEquals(Template(2)["future"], body["future"]));
        Assert.True(body.ContainsKey("future_null") && body["future_null"] is null);
    }

    private sealed class State : IDisposable
    {
        private readonly EmbySession _session;
        private readonly Dictionary<string, EmbySessionScope> _scopes = new(StringComparer.Ordinal);
        private readonly NoSecrets _secrets = new();
        public FnNasUsersViewModel Vm { get; } = new();
        public Transport Http { get; } = new();
        public ServerData A => Http.Servers["r022-a.invalid"];
        public ServerData B => Http.Servers["r022-b.invalid"];

        public State()
        {
            var settings = new AppSettings();
            var paths = new AppPaths(Path.Combine(_root, "artifacts", "r022-default-permission", "forbidden-settings|no-IO"));
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
                [settings, new SettingsStore(paths, _secrets), new CredentialVault(_secrets), device, Http], null)!;
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!;
            var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
            foreach (var key in new[] { "a", "b" })
            {
                var url = $"https://r022-{key}.invalid/v/";
                var connection = new EmbyConnection(new Uri(url), "placeholder-only", key, "fixture", "fixture", device)
                { ServerKind = MediaServerKind.FnNas, IsAdministrator = true };
                var server = new ServerProfile { Id = key, Url = url, ServerKind = MediaServerKind.FnNas };
                var account = new AccountProfile { Id = key, Username = "fixture" };
                var identity = Activator.CreateInstance(identityType, Members, null, [new EmbyClient(http, connection), server, account], null)!;
                var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [_session, identity], null)!;
                sources.Add(scope.Key, scope);
                _scopes.Add(key, scope);
            }
            SelectDefault("a");
            Attach("a");
        }
        public bool Loaded(string name) => (bool)typeof(FnNasUsersViewModel).GetField(name, Members)!.GetValue(Vm)!;
        public ServerData Source(string key) => key == "a" ? A : B;
        public void SelectDefault(string key) => typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, _scopes[key]);
        public void Attach(string key) => Vm.Attach(_session, _scopes[key].Key);
        public void Defaults() => Invoke("Defaults");
        public void NewUser() => Vm.Open(null);
        public void Back() => Invoke("Back");
        public Task Save() => (Task)Invoke("SaveAsync")!;
        public void SetNewIdentity()
        {
            Vm.UserName = "fixture-new";
            Vm.Password = Vm.ConfirmPassword = "placeholder-password";
        }
        private object? Invoke(string name) => typeof(FnNasUsersViewModel).GetMethod(name, Members)!.Invoke(Vm, null);
        public void Dispose()
        {
            Vm.Cancel();
            Vm.Dispose();
            _session.Dispose();
            Assert.Equal(0, _secrets.Calls, "任何保护器／凭据调用都不是本题验证");
            Assert.Equal(0, Http.Rejected, "handler 必须拒绝未登记来源、auth 和真实网络，拒绝事件也不能当绿");
        }
    }

    private enum ReadFailure { None, Server, Network }
    private sealed class ServerData(string key)
    {
        public JsonObject Default { get; set; } = Template(2);
        public JsonArray Libraries { get; set; } = new(
            new JsonObject { ["guid"] = key + "-movie", ["name"] = key + " movie" },
            new JsonObject { ["guid"] = key + "-tv", ["name"] = key + " tv" });
        public ReadFailure DefaultFailure { get; set; }
        public ReadFailure LibraryFailure { get; set; }
        public ReadFailure UsersFailure { get; set; }
        public bool WriteFailure { get; set; }
        public ReplyGate? DefaultGate { get; set; }
        public ReplyGate? LibraryGate { get; set; }
        public ReplyGate? WriteGate { get; set; }
    }
    private sealed record Request(string Host, string Path, string Method);
    private sealed record Write(string Host, string Path, JsonObject Body);
    private sealed class Transport : HttpMessageHandler
    {
        public Dictionary<string, ServerData> Servers { get; } = new(StringComparer.Ordinal)
        {
            ["r022-a.invalid"] = new("a"),
            ["r022-b.invalid"] = new("b")
        };
        public ConcurrentQueue<Request> Requests { get; } = new();
        public ConcurrentQueue<Write> Writes { get; } = new();
        public int Rejected { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (!Servers.TryGetValue(uri.Host, out var server) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.Query.Length != 0 || !uri.AbsolutePath.StartsWith("/v/api/", StringComparison.Ordinal))
                throw Reject("未登记的假来源");
            var path = uri.AbsolutePath[7..];
            if (path.StartsWith("v1/", StringComparison.Ordinal)) path = path[3..];
            Requests.Enqueue(new Request(uri.Host, path, request.Method.Method));
            JsonNode data;
            var failure = ReadFailure.None;
            if (request.Method == HttpMethod.Get)
            {
                ReplyGate? gate = null;
                switch (path)
                {
                    case "mdb/list": data = server.Libraries.DeepClone(); failure = server.LibraryFailure; gate = server.LibraryGate; break;
                    case "manager/template/permission": data = server.Default.DeepClone(); failure = server.DefaultFailure; gate = server.DefaultGate; break;
                    case "manager/user/list": data = new JsonArray(); failure = server.UsersFailure; break;
                    case "user/info": data = new JsonObject { ["is_admin"] = 1 }; break;
                    default: throw Reject("未登记或认证接口：" + path);
                }
                if (gate is not null) await gate.Wait();
            }
            else if (request.Method == HttpMethod.Post && path == "manager/template/permission"
                || request.Method == HttpMethod.Put && path == "v2/manager/user/create")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                Writes.Enqueue(new Write(uri.Host, path, body));
                if (server.WriteGate is { } gate) await gate.Wait();
                failure = server.WriteFailure ? ReadFailure.Server : ReadFailure.None;
                data = new JsonObject();
            }
            else throw Reject("未登记的假写：" + path);
            if (failure == ReadFailure.Network) throw new HttpRequestException("fixture-only network failure");
            if (failure == ReadFailure.Server) return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("fixture-only") };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(new JsonObject { ["code"] = 0, ["data"] = data }.ToJsonString(), Encoding.UTF8, "application/json")
            };
        }
        private AssertionException Reject(string reason)
        {
            Rejected++;
            return new AssertionException(reason);
        }
    }
    private sealed class ReplyGate : IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Wait()
        {
            Entered.TrySetResult();
            await _release.Task;
        }
        public void Release() => _release.TrySetResult();
        public void Dispose() => Release();
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public int Calls { get; private set; }
        public string Protect(string plainText) { Calls++; throw new AssertionException("禁止保护器／凭据 IO"); }
        public string Unprotect(string cipherText) { Calls++; throw new AssertionException("禁止保护器／凭据 IO"); }
    }
}

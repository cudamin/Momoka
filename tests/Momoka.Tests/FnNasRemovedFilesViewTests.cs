using System.Collections;
using System.Reflection;
using System.Net;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.Shell.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnNasRemovedFilesViewTests
{
    internal static int Run(string shellPath, string root)
    {
        var shell = new SettingsPageStateTests.ShellFixture(shellPath, root);
        Test("飞牛恢复VM：根到目录只展示导航和移除行，取消零写", () =>
        {
            using var state = new State(shell);
            state.Load();
            var folder = state.Rows[0]!;
            state.Call("OpenAsync", folder);
            Assert.Equal("folder", state.Get<string>("FolderGuid"));
            Assert.Equal(1, state.Rows.Count);
            state.Set("Selected", state.Rows[0]!);
            state.Call("RestoreSelectedAsync", false);
            Assert.Equal(0, state.Writes);
            Assert.Equal(1, state.Rows.Count);
        });
        Test("飞牛恢复VM：确认成功后刷新503保留完成状态，点击旧项不重发且显式刷新只读", () =>
        {
            using var state = new State(shell);
            state.Load();
            state.Call("OpenAsync", state.Rows[0]!);
            state.Set("Selected", state.Rows[0]!);
            state.FailAfterWrite = true;
            state.Call("RestoreSelectedAsync", true);
            Assert.Equal(1, state.Writes);
            Assert.Equal(0, state.Rows.Count);
            Assert.False(state.Get<bool>("CanRestore"));
            Assert.Contains("已提交恢复", state.Get<string>("NoticeTitle"));
            state.Call("RestoreSelectedAsync", true);
            Assert.Equal(1, state.Writes);
            state.FailAfterWrite = false;
            state.Load();
            Assert.Equal(1, state.Writes);
        });
        Test("飞牛恢复VM：来源失效后不读取和写入，内容隐藏", () =>
        {
            using var state = new State(shell);
            state.Load();
            var reads = state.Reads;
            state.Scope.IdentityState.Lifetime.Cancel();
            state.Load();
            Assert.Equal(reads, state.Reads);
            Assert.Equal("Collapsed", state.Get<object>("ContentVisibility").ToString());
            Assert.Equal(0, state.Writes);
        });
        Test("飞牛恢复VM：管理员降级立即隐藏，恢复权限后保留目录再只读", () =>
        {
            using var state = new State(shell);
            state.Load();
            state.Call("OpenAsync", state.Rows[0]!);
            state.ChangePermission(false);
            Assert.Equal("Collapsed", state.Get<object>("ContentVisibility").ToString());
            Assert.False(state.Get<bool>("CanRestore"));
            var reads = state.Reads;
            state.Load();
            Assert.Equal(reads, state.Reads);
            state.ChangePermission(true);
            state.Load();
            Assert.Equal("folder", state.Get<string>("FolderGuid"));
            Assert.Equal("Visible", state.Get<object>("ContentVisibility").ToString());
            Assert.Equal(0, state.Writes);
        });
        Test("飞牛恢复VM：目录读取失败不提交新导航，旧目录与返回栈保持", () =>
        {
            using var state = new State(shell);
            state.Load();
            state.FailReads = true;
            state.Call("OpenAsync", state.Rows[0]!);
            Assert.Null(state.Get<object?>("FolderGuid"));
            Assert.False(state.Get<bool>("CanGoBack"));
            Assert.Equal("媒体库根目录", state.Get<string>("CurrentPath"));
        });
        Test("飞牛恢复VM：完整确认正文后离页保留已确认行移除，后续不补读", () =>
        {
            using var state = new State(shell);
            state.Load();
            state.Call("OpenAsync", state.Rows[0]!);
            state.Set("Selected", state.Rows[0]!);
            var reads = state.Reads;
            state.OnWriteCompleted = state.Cancel;
            state.Call("RestoreSelectedAsync", true);
            Assert.Equal(1, state.Writes);
            Assert.Equal(0, state.Rows.Count);
            Assert.False(state.Get<bool>("NoticeOpen"));
            Assert.Equal(reads, state.Reads);
            Assert.Equal("Collapsed", state.Get<object>("ContentVisibility").ToString());
        });
        return TestHarness.Run();
    }

    private sealed class State : IDisposable
    {
        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly SettingsPageStateTests.ShellFixture _shell;
        private readonly object _vm;
        private readonly EmbySession _session;
        internal EmbySessionScope Scope { get; }
        internal IList Rows => _shell.List(_vm, "Entries");
        internal int Writes { get; private set; }
        internal int Reads { get; private set; }
        internal bool FailAfterWrite { get; set; }
        internal bool FailReads { get; set; }
        internal Action? OnWriteCompleted { get; set; }
        internal State(SettingsPageStateTests.ShellFixture shell)
        {
            _shell = shell;
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = new(new AppSettings(), new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "recover|no-IO")), new NoSecrets()),
                new CredentialVault(new NoSecrets()), device, new Handler(this));
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            var connection = new EmbyConnection(new Uri("https://restore.invalid/v/"), "placeholder", "user", "user", "server", device)
            { ServerKind = MediaServerKind.FnNas, IsAdministrator = true };
            Scope = new(_session, new(new EmbyClient(http, connection), new ServerProfile { Id = "server", ServerKind = MediaServerKind.FnNas }, new AccountProfile { Id = "user" }));
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, Scope);
            _vm = shell.NewRow("FnNasRemovedFilesViewModel");
            // Instantiate the actual Shell service to match its loaded assembly's type identity.
            var service = Activator.CreateInstance(_vm.GetType().Assembly.GetType("Momoka.Shell.Services.FnNasLibraryRemovalService", true)!, new RemovedMedia())!;
            shell.Invoke(_vm, "AttachRemoval", _session, Scope.Key, service, "library", "电影");
        }
        internal T Get<T>(string name) => _shell.Get<T>(_vm, name);
        internal void Set(string name, object value) => _shell.Set(_vm, name, value);
        internal void Load() => Call("ReloadAsync");
        internal void Cancel() => _shell.Invoke(_vm, "Cancel");
        internal void ChangePermission(bool allowed)
        {
            _session.ReplaceClient(Scope, Scope.IdentityState.Client.Connection with { IsAdministrator = allowed });
            typeof(EmbySession).GetMethod("NotifyPermissionsChanged", Members)!.Invoke(_session, [Scope]);
        }
        internal void Call(string method, params object[] args) => ((Task)_shell.Invoke(_vm, method, args)!).GetAwaiter().GetResult();
        public void Dispose() { ((IDisposable)_vm).Dispose(); _session.Dispose(); }
        private sealed class Handler(State state) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.Method == HttpMethod.Post)
                {
                    state.Writes++;
                    if (state.OnWriteCompleted is { } complete)
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CompletingStream(complete)) });
                    return Task.FromResult(Reply(null));
                }
                if (request.RequestUri!.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal))
                    return Task.FromResult(Reply(new JsonObject { ["is_admin"] = 1 }));
                state.Reads++;
                if (state.FailReads || state.Writes > 0 && state.FailAfterWrite) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                var child = request.RequestUri.Query.Contains("fv_guid", StringComparison.Ordinal);
                return Task.FromResult(Reply(new JsonObject
                {
                    ["mdb_name"] = "电影",
                    ["dir"] = child ? "/media/电影" : "",
                    ["jump_list"] = new JsonArray(),
                    ["list"] = child ? new JsonArray(new JsonObject { ["guid"] = "movie", ["media_guid"] = "file", ["file_name"] = "a.mkv", ["type"] = "Movie", ["path"] = "/media/电影/a.mkv", ["blacked"] = 1 })
                        : new JsonArray(new JsonObject { ["guid"] = "folder", ["file_name"] = "电影", ["type"] = "Directory", ["path"] = "/media/电影", ["blacked"] = 0 })
                }));
            }
            private static HttpResponseMessage Reply(JsonNode? value) => new(HttpStatusCode.OK)
            { Content = new StringContent(new JsonObject { ["code"] = 0, ["data"] = value }.ToJsonString()) };
        }
        private sealed class CompletingStream(Action completed) : MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"code\":0,\"data\":null}"))
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var count = Read(buffer.Span);
                if (count == 0) completed();
                return ValueTask.FromResult(count);
            }
        }
        private sealed class NoSecrets : ISecretProtector
        {
            public string Protect(string plaintext) => throw new AssertionException("禁止凭据IO");
            public string Unprotect(string protectedText) => throw new AssertionException("禁止凭据IO");
        }
    }
}

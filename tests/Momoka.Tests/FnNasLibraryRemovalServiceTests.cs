using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Infrastructure;
using Momoka.Shell.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnNasLibraryRemovalServiceTests
{
    internal static void Register()
    {
        Test("飞牛移出服务：确认后恢复只解同项隐藏，物理删除记录不被清空", () =>
        {
            using var state = new State();
            var other = state.Scope.Bind(new EmbyItem { Id = "other", Type = EmbyItemType.Movie });
            state.Hidden.Remember(other);
            var ticket = state.Service.RemoveAsync(state.Scope, state.Item, [new("file-a", "/media/a.mkv", null, "")], null).GetAwaiter().GetResult();
            using (RemovedMedia.TemporaryRemoval(ticket.Ticket)) state.Hidden.Remember(state.Item);
            state.Service.RestoreAsync(state.Scope, "library", "/media/a.mkv").GetAwaiter().GetResult();
            Assert.Equal(2, state.Writes.Count);
            Assert.False(state.Hidden.Hides(state.Item));
            Assert.True(state.Hidden.Hides(other));
        });
        Test("飞牛移出服务：未知或普通权限、来源撤销、空版本清单零写", () =>
        {
            foreach (var admin in new bool?[] { null, false })
            {
                using var state = new State(admin);
                Assert.Throws<InvalidOperationException>(() => state.Service.RemoveAsync(state.Scope, state.Item, [new("file-a", "/media/a.mkv", null, "")], null).GetAwaiter().GetResult());
                Assert.Equal(0, state.Writes.Count);
            }
            using var valid = new State();
            Assert.Throws<InvalidOperationException>(() => valid.Service.RemoveAsync(valid.Scope, valid.Item, [], null).GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => valid.Service.RemoveAsync(valid.Scope, valid.Item, [new("file-a", " ", null, "")], null).GetAwaiter().GetResult());
            var unbound = new EmbyItem { Id = "foreign", Type = EmbyItemType.Movie };
            Assert.Throws<InvalidOperationException>(() => valid.Service.RemoveAsync(valid.Scope, unbound, [new("file-a", "/a.mkv", null, "")], null).GetAwaiter().GetResult());
            valid.Scope.IdentityState.Lifetime.Cancel();
            Assert.Throws<OperationCanceledException>(() => valid.Service.RestoreAsync(valid.Scope, "library", "/media/a.mkv").GetAwaiter().GetResult());
            Assert.Equal(0, valid.Writes.Count);
        });
        Test("飞牛移出服务：非最后版本只提交选中文件，失败恢复保留隐藏", () =>
        {
            using var state = new State();
            state.Service.RemoveAsync(state.Scope, state.Item,
                [new("file-a", "/media/a.mkv", null, ""), new("file-b", "/media/b.mkv", null, "")], "file-a").GetAwaiter().GetResult();
            Assert.Equal("{\"delete_file\":0,\"media_guids\":[\"file-a\"]}", state.Writes.Single());
            Assert.Equal(0, state.Hidden.Count);
            state.Hidden.Remember(state.Item);
            state.FailRestore = true;
            Assert.Throws<EmbyApiException>(() => state.Service.RestoreAsync(state.Scope, "library", "/media/a.mkv").GetAwaiter().GetResult());
            Assert.True(state.Hidden.Hides(state.Item));
            Assert.Equal(2, state.Writes.Count);
        });
        Test("飞牛移出服务：管理身份补读期间权限降级，后续DELETE零写", () =>
        {
            using var state = new State();
            state.BeforeAdminReply = () => state._session.ReplaceClient(state.Scope,
                state.Scope.IdentityState.Client.Connection with { IsAdministrator = false });
            Assert.Throws<OperationCanceledException>(() => state.Service.RemoveAsync(state.Scope, state.Item,
                [new("file-a", "/media/a.mkv", null, "")], null).GetAwaiter().GetResult());
            Assert.Equal(0, state.Writes.Count);
            Assert.False(state.Scope.IsAdministrator == true);
        });
        Test("飞牛移出服务：HTTP确认后来源撤销仍返回确认票，恢复也保留成功不重发", () =>
        {
            using var state = new State();
            state.AfterWriteReply = () => state.Scope.IdentityState.Lifetime.Cancel();
            Assert.NotNull(state.Service.RemoveAsync(state.Scope, state.Item, [new("file-a", "/a.mkv", null, "")], null).GetAwaiter().GetResult());
            Assert.Equal(1, state.Writes.Count);
            using var restore = new State();
            restore.AfterWriteReply = () => restore.Scope.IdentityState.Lifetime.Cancel();
            restore.Service.RestoreAsync(restore.Scope, "library", "/a.mkv").GetAwaiter().GetResult();
            Assert.Equal(1, restore.Writes.Count);
        });
        Test("飞牛移出服务：确认版本后旧列表不能重发，恢复后该版本可再次移出", () =>
        {
            using var state = new State();
            FnNasVersion[] versions = [new("file-a", "/media/a.mkv", null, ""), new("file-b", "/media/b.mkv", null, "")];
            state.Service.RemoveAsync(state.Scope, state.Item, versions, "file-a").GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => state.Service.RemoveAsync(state.Scope, state.Item, versions, "file-a").GetAwaiter().GetResult());
            Assert.Equal(1, state.Writes.Count);
            state.Service.RestoreAsync(state.Scope, "library", "/media/a.mkv").GetAwaiter().GetResult();
            state.Service.RemoveAsync(state.Scope, state.Item, versions, "file-a").GetAwaiter().GetResult();
            Assert.Equal(3, state.Writes.Count);
        });
        Test("飞牛移出服务：同路径其它媒体库恢复不解除原库票", () =>
        {
            using var state = new State();
            var ticket = state.Service.RemoveAsync(state.Scope, state.Item, [new("file-a", "/media/a.mkv", null, "")], null).GetAwaiter().GetResult();
            using (RemovedMedia.TemporaryRemoval(ticket.Ticket)) state.Hidden.Remember(state.Item);
            state.Service.RestoreAsync(state.Scope, "other-library", "/media/a.mkv").GetAwaiter().GetResult();
            Assert.True(state.Hidden.Hides(state.Item));
        });
        Test("飞牛恢复服务：恢复确认后重开旧列表不能重复POST", () =>
        {
            using var state = new State();
            state.Service.RestoreAsync(state.Scope, "library", "/a.mkv").GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => state.Service.RestoreAsync(state.Scope, "library", "/a.mkv").GetAwaiter().GetResult());
            Assert.Equal(1, state.Writes.Count);
        });
        Test("飞牛恢复服务：同账号重新登录恢复精确旧票，保留物理删除原因", () =>
        {
            using var state = new State();
            var receipt = state.Service.RemoveAsync(state.Scope, state.Item, [new("file-a", "/media/a.mkv", null, "")], null).GetAwaiter().GetResult();
            using (RemovedMedia.TemporaryRemoval(receipt.Ticket)) state.Hidden.Remember(state.Item);
            state.Relogin();
            state.Service.RestoreAsync(state.Scope, "library", "/media/a.mkv").GetAwaiter().GetResult();
            Assert.False(state.Hidden.Hides(state.Scope.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie })));
        });
        Test("飞牛恢复服务：同ID物理删除加重登恢复仍然保留物理原因", () =>
        {
            using var state = new State();
            var receipt = state.Service.RemoveAsync(state.Scope, state.Item, [new("file-a", "/media/a.mkv", null, "")], null).GetAwaiter().GetResult();
            using (RemovedMedia.TemporaryRemoval(receipt.Ticket)) state.Hidden.Remember(state.Item);
            state.Hidden.Remember(state.Item);
            state.Relogin();
            state.Service.RestoreAsync(state.Scope, "library", "/media/a.mkv").GetAwaiter().GetResult();
            Assert.True(state.Hidden.Hides(state.Scope.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie })));
        });
        Test("飞牛恢复服务：先移出两版后恢复第一版，解除最后一版主体隐藏并保留其它版防重", () =>
        {
            using var state = new State();
            FnNasVersion[] versions = [new("file-a", "/media/a.mkv", null, ""), new("file-b", "/media/b.mkv", null, "")];
            state.Service.RemoveAsync(state.Scope, state.Item, versions, "file-a").GetAwaiter().GetResult();
            var last = state.Service.RemoveAsync(state.Scope, state.Item, versions, "file-b").GetAwaiter().GetResult();
            Assert.True(last.SubjectRemoved);
            using (RemovedMedia.TemporaryRemoval(last.Ticket)) state.Hidden.Remember(state.Item);
            state.Service.RestoreAsync(state.Scope, "library", "/media/a.mkv").GetAwaiter().GetResult();
            Assert.False(state.Hidden.Hides(state.Item));
            Assert.Throws<InvalidOperationException>(() => state.Service.RemoveAsync(state.Scope, state.Item, versions, "file-b").GetAwaiter().GetResult());
        });
    }

    private sealed class State : IDisposable
    {
        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        internal readonly EmbySession _session;
        internal RemovedMedia Hidden { get; } = new();
        internal FnNasLibraryRemovalService Service { get; }
        internal List<string> Writes { get; } = [];
        internal bool FailRestore { get; set; }
        internal Action? BeforeAdminReply { get; set; }
        internal Action? AfterWriteReply { get; set; }
        internal EmbySessionScope Scope { get; private set; }
        internal EmbyItem Item { get; }
        internal State(bool? admin = true)
        {
            var transport = new Handler(this);
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = new EmbySession(new AppSettings(), new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "removal|no-IO")), new NoSecrets()),
                new CredentialVault(new NoSecrets()), device, transport);
            var connection = new EmbyConnection(new Uri("https://removal.invalid/v/"), "placeholder", "user", "user", "server", device)
            { ServerKind = MediaServerKind.FnNas, IsAdministrator = admin };
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            Scope = new EmbySessionScope(_session, new(new EmbyClient(http, connection),
                new ServerProfile { Id = "server", Url = "https://removal.invalid/v/", ServerKind = MediaServerKind.FnNas },
                new AccountProfile { Id = "user", Username = "user" }));
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!).Add(Scope.Key, Scope);
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, Scope);
            Item = Scope.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, FnNasLibraryGuid = "library" });
            Service = new(Hidden);
        }
        public void Dispose() => _session.Dispose();
        internal void Relogin()
        {
            var old = Scope;
            old.IdentityState.Lifetime.Cancel();
            var connection = old.IdentityState.Client.Connection;
            Scope = new(_session, new(old.IdentityState.Client, old.IdentityState.Server, old.IdentityState.Account));
            ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!)[Scope.Key] = Scope;
            typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, Scope);
        }
        private sealed class Handler(State state) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Get)
                {
                    state.BeforeAdminReply?.Invoke();
                    return Reply(new JsonObject { ["is_admin"] = 1 });
                }
                state.Writes.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (state.FailRestore && request.Method == HttpMethod.Post) return new(HttpStatusCode.Forbidden);
                if (state.AfterWriteReply is { } completed)
                    return new(HttpStatusCode.OK) { Content = new StreamContent(new CompletingStream("{\"code\":0,\"data\":null}", completed)) };
                return Reply(null);
            }
            private static HttpResponseMessage Reply(JsonNode? data) => new(HttpStatusCode.OK)
            { Content = new StringContent(new JsonObject { ["code"] = 0, ["data"] = data }.ToJsonString()) };
        }
        private sealed class CompletingStream(string body, Action completed) : MemoryStream(System.Text.Encoding.UTF8.GetBytes(body))
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

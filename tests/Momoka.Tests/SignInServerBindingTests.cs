using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual login VM/session and isolated settings IO; only HTTP and secret protection are substitutes.</summary>
internal static class SignInServerBindingTests
{
    private const string ServerA = "binding-server-a";
    private const string ServerB = "binding-server-b";
    private const string OldToken = "binding-placeholder-old-token";
    private const string OldPassword = "binding-placeholder-old-password";
    private const string NewToken = "binding-placeholder-new-token";
    private const string NewPassword = "binding-placeholder-new-password";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    internal static int Run(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var runtime = new VmRuntime(shellPath, sourceRoot);
        RegisterCore();
        RegisterVm(runtime);
        return TestHarness.Run();
    }

    private static void Case(string name, Func<Task> run) =>
        Test("R03-001 " + name, () => run().WaitAsync(Limit).GetAwaiter().GetResult());

    // This part also runs in the ordinary Core suite, without loading Shell or creating a window.
    internal static void RegisterCore()
    {
        RegisterMissingBinding();
        foreach (var (saved, allowedKinds) in new (MediaServerKind, MediaServerKind[])[]
        {
            (MediaServerKind.Emby, [MediaServerKind.Emby, MediaServerKind.Jellyfin]),
            (MediaServerKind.Jellyfin, [MediaServerKind.Emby, MediaServerKind.Jellyfin]),
            (MediaServerKind.FnNas, [MediaServerKind.FnNas]),
            (MediaServerKind.Plex, [MediaServerKind.Plex])
        })
            foreach (var confirmed in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
                Test($"R03-001 已知绑定产品纠正 {saved} 到 {confirmed}", () =>
                {
                    Assert.Equal(allowedKinds.Contains(confirmed), EmbySignInIdentity.CanRetainServerBinding(saved, ServerA, confirmed, ServerA),
                        "跨产品不能只因ID字符串相同重绑凭据；保留Emby/Jellyfin纠正");
                });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            foreach (var stage in new[] { "auth", "public", "public-with-auth-a" })
                Case($"{kind} {stage} 最终异服认证不能重绑其他账号", async () =>
                {
                    using var f = new Fixture(kind);
                    f.Transport.AuthId = stage == "auth" ? ServerB : stage == "public-with-auth-a" ? ServerA : null;
                    f.Transport.AfterId = stage.StartsWith("public", StringComparison.Ordinal) ? ServerB : ServerA;
                    var original = f.Checkpoint();
                    var account = f.Service.ResolveAccount(f.Server, "new-user");
                    await Fails<EmbyApiException>(f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None));
                    f.AssertBinding(original);
                    Assert.False(account.HasSavedToken);
                    Assert.Equal(0, f.Session.Sources.Count);
                    Assert.Equal(0, f.Protector.Writes);
                    await f.AssertReloadAndRestoreAsync(ServerB);
                });

            foreach (var missing in new string?[] { null, "", " " })
                Case($"{kind} 同产品缺失最终ID仍保留已知绑定 {MissingName(missing)}", async () =>
                {
                    using var f = new Fixture(kind);
                    f.Transport.AuthId = missing;
                    f.Transport.AfterId = missing;
                    var original = f.Checkpoint();
                    var account = f.Service.ResolveAccount(f.Server, "new-user");
                    await f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None);
                    f.AssertBinding(original);
                    Assert.True(account.HasSavedToken);
                    Assert.Equal(ServerA, f.Session.Connection!.ServerId);
                    await f.AssertReloadAndRestoreAsync(ServerB);
                });

            Case($"{kind} 同服新账号成功只保存自己的凭据", async () =>
            {
                using var f = new Fixture(kind);
                var original = f.Checkpoint();
                var account = f.Service.ResolveAccount(f.Server, "new-user");
                await f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None);
                f.AssertBinding(original);
                Assert.True(f.Vault.GetAccessToken(account) == NewToken, "新凭据只属于新账号");
                Assert.True(f.Vault.GetPassword(account) == NewPassword, "新密码只属于新账号");
                Assert.Equal(2, f.Server.Accounts.Count);
            });
        }

        foreach (var stage in new[] { "cancel", "address" })
            Case($"认证等待中 {stage} 仍拒绝迟到结果", async () =>
            {
                using var f = new Fixture(MediaServerKind.Emby);
                f.Transport.Hold = true;
                var original = f.Checkpoint();
                var account = f.Service.ResolveAccount(f.Server, "new-user");
                using var cancel = new CancellationTokenSource();
                var run = f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, cancel.Token);
                await f.Transport.Entered.Task.WaitAsync(Limit);
                if (stage == "cancel") cancel.Cancel();
                else f.Server.Url = "https://changed.invalid/media";
                f.Transport.Release.TrySetResult();
                await Fails<OperationCanceledException>(run);
                Assert.True(f.OldAccount.ProtectedAccessToken == original.Token, "旧令牌不变");
                Assert.True(f.OldAccount.ProtectedPassword == original.Password, "旧密码不变");
                Assert.False(account.HasSavedToken);
                Assert.Equal(0, f.Session.Sources.Count);
                Assert.Equal(0, f.Protector.Writes);
            });

        Case("认证等待中同URL绑定被编辑仍拒绝原结果", async () =>
        {
            using var f = new Fixture(MediaServerKind.Emby);
            f.Transport.Hold = true;
            var original = f.Checkpoint();
            var account = f.Service.ResolveAccount(f.Server, "new-user");
            var run = f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None);
            await f.Transport.Entered.Task.WaitAsync(Limit);
            f.Server.ServerId = ServerB;
            f.Transport.Release.TrySetResult();
            await Fails<EmbyApiException>(run);
            Assert.Equal(ServerB, f.Server.ServerId, "保留编辑后的绑定，不采纳旧响应");
            Assert.True(f.OldAccount.ProtectedAccessToken == original.Token, "旧令牌不变");
            Assert.True(f.OldAccount.ProtectedPassword == original.Password, "旧密码不变");
            Assert.False(account.HasSavedToken);
            Assert.Equal(0, f.Session.Sources.Count);
        });

        foreach (var id in new string?[] { ServerA, ServerB, null, "" })
            Case($"旧Emby产品纠正要求同服最终身份 {MissingName(id)}", async () =>
            {
                using var f = new Fixture(MediaServerKind.Emby);
                f.Transport.AfterKind = MediaServerKind.Jellyfin;
                f.Transport.AuthId = id;
                f.Transport.AfterId = id;
                var original = f.Checkpoint();
                var account = f.Service.ResolveAccount(f.Server, "new-user");
                var run = f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None);
                if (id == ServerA)
                {
                    await run;
                    Assert.Equal(MediaServerKind.Jellyfin, f.Server.ServerKind);
                    Assert.Equal("https://binding.invalid/media/emby", f.Server.Url, "无匿名入口证据时保留已认证的反代路径");
                    Assert.Equal(ServerA, f.Server.ServerId);
                    Assert.True(account.HasSavedToken);
                }
                else
                {
                    await Fails<EmbyApiException>(run);
                    f.AssertBinding(original);
                    Assert.False(account.HasSavedToken);
                    Assert.Equal(0, f.Protector.Writes);
                }
            });
    }

    private static void RegisterMissingBinding()
    {
        foreach (var (saved, withoutProof, withProof) in new (MediaServerKind, MediaServerKind[], MediaServerKind[])[]
        {
            (MediaServerKind.Emby, [MediaServerKind.Emby], [MediaServerKind.Emby, MediaServerKind.Jellyfin]),
            (MediaServerKind.Jellyfin, [MediaServerKind.Jellyfin], [MediaServerKind.Emby, MediaServerKind.Jellyfin]),
            (MediaServerKind.FnNas, [MediaServerKind.FnNas], [MediaServerKind.FnNas]),
            (MediaServerKind.Plex, [MediaServerKind.Plex], [MediaServerKind.Plex])
        })
            foreach (var confirmed in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
                foreach (var id in new string?[] { null, ServerA })
                    Test($"R03-001 旧ID缺失产品边界 {saved} 到 {confirmed} {MissingName(id)}", () =>
                    {
                        var allowedKinds = id is null ? withoutProof : withProof;
                        Assert.Equal(allowedKinds.Contains(confirmed), EmbySignInIdentity.CanRetainServerBinding(saved, null, confirmed, id),
                            "无旧ID也不能跨产品族；族内纠正需要本次非空ID");
                    });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            foreach (var authId in new[] { ServerA, ServerB })
                Case($"{kind} 无旧ID时认证与公开ID矛盾拒绝 {authId}", async () =>
                {
                    using var f = new Fixture(kind);
                    f.Server.ServerId = null;
                    f.Transport.AuthId = authId;
                    f.Transport.AfterId = authId == ServerA ? ServerB : ServerA;
                    var original = f.Checkpoint();
                    var account = f.Service.ResolveAccount(f.Server, "new-user");
                    await Fails<EmbyApiException>(f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None));
                    f.AssertBinding(original);
                    Assert.False(account.HasSavedToken);
                    Assert.Equal(0, f.Session.Sources.Count);
                    Assert.Equal(0, f.Protector.Writes);
                });

            foreach (var authKnown in new[] { true, false })
                foreach (var id in new[] { ServerA, ServerB })
                    Case($"{kind} 单侧已知ID authKnown={authKnown} {id}", async () =>
                    {
                        using var f = new Fixture(kind);
                        f.Transport.AuthId = authKnown ? id : null;
                        f.Transport.AfterId = authKnown ? null : id;
                        var original = f.Checkpoint();
                        var account = f.Service.ResolveAccount(f.Server, "new-user");
                        var run = f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None);
                        if (id == ServerA)
                        {
                            await run;
                            Assert.Equal(ServerA, f.Session.Connection!.ServerId);
                            Assert.True(account.HasSavedToken);
                        }
                        else
                        {
                            await Fails<EmbyApiException>(run);
                            Assert.False(account.HasSavedToken);
                            Assert.Equal(0, f.Protector.Writes);
                        }
                        f.AssertBinding(original);
                    });

            Case($"{kind} 等待期间补上的已知ID不被缺ID结果擦掉", async () =>
            {
                using var f = new Fixture(kind);
                f.Server.ServerId = null;
                f.Transport.AuthId = null;
                f.Transport.AfterId = null;
                f.Transport.Hold = true;
                var original = f.Checkpoint();
                var account = f.Service.ResolveAccount(f.Server, "new-user");
                var run = f.Session.SignInAsync(f.Server, account, NewPassword, "new-user", true, CancellationToken.None);
                await f.Transport.Entered.Task.WaitAsync(Limit);
                f.Server.ServerId = ServerA;
                f.Transport.Release.TrySetResult();
                await run;
                Assert.Equal(ServerA, f.Server.ServerId, "原锁内保留新近确认的服务器ID");
                Assert.Equal(ServerA, f.Session.Connection!.ServerId);
                Assert.True(f.OldAccount.ProtectedAccessToken == original.Token, "旧令牌不变");
                Assert.True(f.OldAccount.ProtectedPassword == original.Password, "旧密码不变");
                await f.AssertReloadAndRestoreAsync(ServerB);
            });
        }
    }

    private static void RegisterVm(VmRuntime runtime)
    {
        foreach (var savedKind in new[] { MediaServerKind.FnNas, MediaServerKind.Plex })
            foreach (var detectedKind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
                Case($"VM无旧ID {savedKind} 不因新账号重绑到 {detectedKind}", async () =>
                {
                    using var f = new Fixture(savedKind);
                    f.Server.ServerId = null;
                    f.Transport.InitialKind = detectedKind;
                    f.Transport.AfterKind = detectedKind;
                    f.Transport.InitialId = ServerA;
                    f.Transport.Reject = true;
                    var original = f.Checkpoint();
                    using var vm = runtime.Create(f);
                    await vm.ConnectAsync();
                    await vm.SignInAsync();
                    f.AssertBinding(original);
                    Assert.Equal(0, f.Transport.NewAuthenticationRequests);
                    Assert.Equal(1, f.Server.Accounts.Count);
                    Assert.Equal(0, vm.SignedIn);
                });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            foreach (var response in new[] { "wrong-password", "success", "cancel" })
                Case($"{kind} 已确认异服 {response} 不复用保存档案", async () =>
                {
                    using var f = new Fixture(kind);
                    f.Server.Url = kind == MediaServerKind.Emby ? "https://binding.invalid/media/emby/" : "https://binding.invalid/media/";
                    f.Transport.InitialId = ServerB;
                    f.Transport.AfterId = ServerB;
                    f.Transport.AuthId = ServerB;
                    f.Transport.Reject = response == "wrong-password";
                    var original = f.Checkpoint();
                    using var vm = runtime.Create(f);
                    await vm.ConnectAsync();
                    await vm.SignInAsync();
                    if (response == "cancel") vm.Detach();
                    f.AssertBinding(original);
                    Assert.Equal(1, f.Server.Accounts.Count, "拒绝异服时不创建新账号");
                    Assert.Equal(0, f.Transport.NewAuthenticationRequests, "已确认异服不发送任何账号密码");
                    Assert.Equal(0, vm.SignedIn);
                    Assert.True(vm.NoticeOpen, "身份错误提供恢复说明");
                    await f.AssertReloadAndRestoreAsync(ServerB);
                });

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            foreach (var missing in new string?[] { null, "", " " })
                Case($"{kind} 公开探测缺ID且密码错误保留已知绑定 {MissingName(missing)}", async () =>
                {
                    using var f = new Fixture(kind);
                    f.Transport.InitialId = missing;
                    f.Transport.Reject = true;
                    var original = f.Checkpoint();
                    using var vm = runtime.Create(f);
                    await vm.ConnectAsync();
                    await vm.SignInAsync();
                    f.AssertBinding(original);
                    Assert.Equal(1, f.Transport.NewAuthenticationRequests);
                    Assert.Equal(0, vm.SignedIn);
                    await f.AssertReloadAndRestoreAsync(ServerB);
                });

        foreach (var id in new string?[] { ServerA, ServerB, null, "" })
            Case($"VM旧Emby到Jellyfin纠正按确认身份 {MissingName(id)}", async () =>
            {
                using var f = new Fixture(MediaServerKind.Emby);
                f.Transport.InitialKind = MediaServerKind.Jellyfin;
                f.Transport.AfterKind = MediaServerKind.Jellyfin;
                f.Transport.InitialId = id;
                f.Transport.AfterId = id;
                f.Transport.AuthId = id;
                var original = f.Checkpoint();
                using var vm = runtime.Create(f);
                await vm.ConnectAsync();
                await vm.SignInAsync();
                if (id == ServerA)
                {
                    Assert.Equal(1, vm.SignedIn);
                    Assert.Equal(MediaServerKind.Jellyfin, f.Server.ServerKind);
                    Assert.Equal("https://binding.invalid/media", f.Server.Url);
                    Assert.Equal(ServerA, f.Server.ServerId);
                    Assert.True(f.OldAccount.ProtectedAccessToken == original.Token, "旧账号令牌不变");
                    Assert.True(f.OldAccount.ProtectedPassword == original.Password, "旧账号密码不变");
                }
                else
                {
                    f.AssertBinding(original);
                    Assert.Equal(0, f.Transport.NewAuthenticationRequests);
                    Assert.Equal(0, vm.SignedIn);
                }
            });

        foreach (var edit in new[] { "detach", "username" })
            Case($"VM同服认证等待中 {edit} 取消真实Core提交", async () =>
            {
                using var f = new Fixture(MediaServerKind.Emby);
                f.Transport.Hold = true;
                var original = f.Checkpoint();
                using var vm = runtime.Create(f);
                await vm.ConnectAsync();
                var run = vm.SignInAsync();
                await f.Transport.Entered.Task.WaitAsync(Limit);
                if (edit == "detach") vm.Detach();
                else vm.Set("Username", "another-draft");
                f.Transport.Release.TrySetResult();
                await run.WaitAsync(Limit);
                f.AssertBinding(original);
                Assert.Equal(0, f.Session.Sources.Count);
                Assert.Equal(0, vm.SignedIn);
                Assert.Equal(0, f.Protector.Writes);
            });
    }

    private static string MissingName(string? id) => id switch { null => "null", "" => "empty", " " => "blank", _ => id };

    private static async Task Fails<T>(Task run) where T : Exception
    {
        try { await run.WaitAsync(Limit); }
        catch (T) { return; }
        throw new AssertionException("未拒绝应失效的认证结果");
    }

    private sealed record Checkpoint(string Url, MediaServerKind Kind, string? Id, string Token, string Password);

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(AppContext.BaseDirectory, "signin-binding-fixtures", Guid.NewGuid().ToString("N"));
        internal PlaceholderProtector Protector { get; } = new();
        internal BindingTransport Transport { get; } = new();
        internal AppSettings Settings { get; } = new() { AggregateMode = true };
        internal ServerProfile Server { get; }
        internal AccountProfile OldAccount { get; } = new() { Id = "old-account", Username = "old-user", UserId = "old-user", AutoConnect = true };
        internal SettingsStore Store { get; }
        internal SettingsService Service { get; }
        internal CredentialVault Vault { get; }
        internal EmbySession Session { get; }

        internal Fixture(MediaServerKind kind)
        {
            Directory.CreateDirectory(Root);
            Server = new() { Id = "saved-server", Name = "saved", Url = "https://binding.invalid/media", ServerKind = kind, ServerId = ServerA, ServerVersion = "12.2.0" };
            Vault = new(Protector);
            Vault.SetAccessToken(OldAccount, OldToken);
            Vault.SetPassword(OldAccount, OldPassword, true);
            Protector.Writes = 0;
            Server.Accounts.Add(OldAccount);
            Settings.Servers.Add(Server);
            Settings.LastServerId = Server.Id;
            Settings.LastAccountId = OldAccount.Id;
            Store = new(new AppPaths(Root), Protector);
            Service = new(Store, Settings);
            Transport.InitialKind = kind;
            Transport.AfterKind = kind;
            Session = new(Settings, Store, Vault, DeviceIdentity.Create("binding-fixture", "0"), Transport);
        }

        internal Checkpoint Checkpoint() => new(Server.Url, Server.ServerKind, Server.ServerId, OldAccount.ProtectedAccessToken, OldAccount.ProtectedPassword);

        internal void AssertBinding(Checkpoint original)
        {
            Assert.Equal(original.Url, Server.Url, "原服务器地址不变");
            Assert.Equal(original.Kind, Server.ServerKind, "原服务器产品不变");
            Assert.Equal(original.Id, Server.ServerId, "已知服务器ID不能擦除或重绑");
            Assert.True(OldAccount.ProtectedAccessToken == original.Token, "旧账号令牌不变");
            Assert.True(OldAccount.ProtectedPassword == original.Password, "旧账号密码不变");
        }

        internal async Task AssertReloadAndRestoreAsync(string reportedId)
        {
            Service.Save();
            var loaded = Store.LoadStrict();
            var server = loaded.Servers.Single();
            var old = server.Accounts.Single(a => a.Id == OldAccount.Id);
            Assert.Equal(ServerA, server.ServerId, "正常退出保存并重载后仍绑定原服");
            Assert.True(old.ProtectedAccessToken == OldAccount.ProtectedAccessToken, "落盘旧令牌不变");
            Assert.True(old.ProtectedPassword == OldAccount.ProtectedPassword, "落盘旧密码不变");
            Transport.InitialId = reportedId;
            Transport.AfterId = reportedId;
            Transport.AuthId = reportedId;
            using var restored = new EmbySession(loaded, Store, Vault, DeviceIdentity.Create("binding-fixture", "0"), Transport);
            Assert.False(await restored.TryRestoreAsync(server, old, CancellationToken.None));
            Assert.Equal(0, Transport.OldTokenRequests, "异服不接收旧账号令牌");
            Assert.Equal(0, Transport.OldPasswordRequests, "异服不接收旧账号保存密码");
        }

        public void Dispose()
        {
            Session.Dispose();
            Service.Dispose();
            // Keep only anonymous test output for evidence; never restore or erase a user's settings.
        }
    }

    private sealed class PlaceholderProtector : ISecretProtector
    {
        internal int Writes { get; set; }
        public string Protect(string value)
        {
            Check(value);
            Writes++;
            return "wrapped:" + value;
        }
        public string Unprotect(string value)
        {
            Assert.True(value.StartsWith("wrapped:", StringComparison.Ordinal), "仅接受本夹具包装值");
            var plain = value[8..];
            Check(plain);
            return plain;
        }
        private static void Check(string value) => Assert.True(value is OldToken or OldPassword or NewToken or NewPassword, "保护器仅接受固定占位凭据");
    }

    private sealed class BindingTransport : HttpMessageHandler
    {
        internal MediaServerKind InitialKind { get; set; }
        internal MediaServerKind AfterKind { get; set; }
        internal string? InitialId { get; set; } = ServerA;
        internal string? AfterId { get; set; } = ServerA;
        internal string? AuthId { get; set; } = ServerA;
        internal bool Reject { get; set; }
        internal bool Hold { get; set; }
        internal int NewAuthenticationRequests { get; private set; }
        internal int OldPasswordRequests { get; private set; }
        internal int OldTokenRequests { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.True(request.RequestUri!.Host == "binding.invalid", "拒绝真实网络来源");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("System/Info/Public", StringComparison.Ordinal))
            {
                var after = NewAuthenticationRequests > 0;
                return Json(new { ProductName = (after ? AfterKind : InitialKind).ToString(), Id = after ? AfterId : InitialId, ServerName = "anonymous", Version = "12.2.0" });
            }
            if (path.EndsWith("Users/Public", StringComparison.Ordinal)) return Json(Array.Empty<object>());
            if (path.EndsWith("Users/AuthenticateByName", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root = body.RootElement;
                var name = root.TryGetProperty("UserName", out var embyName) ? embyName.GetString() : root.GetProperty("Username").GetString();
                if (name == "old-user")
                {
                    OldPasswordRequests++;
                    Assert.True(root.GetProperty("Pw").GetString() == OldPassword, "仅观察占位旧密码");
                    return Json(new { }, HttpStatusCode.Unauthorized);
                }
                Assert.Equal("new-user", name);
                Assert.True(root.GetProperty("Pw").GetString() == NewPassword, "提交密码必须属于新账号");
                NewAuthenticationRequests++;
                Entered.TrySetResult();
                if (Hold) await Release.Task.ConfigureAwait(false); // Deliberately deliver after local cancellation.
                return Reject ? Json(new { }, HttpStatusCode.Unauthorized)
                    : Json(new { AccessToken = NewToken, ServerId = AuthId, User = new { Id = "new-user", Name = "new-user" } });
            }
            Assert.True(path.EndsWith("/Views", StringComparison.Ordinal) || path.EndsWith("/UserViews", StringComparison.Ordinal)
                || path.EndsWith("/Users/Me", StringComparison.Ordinal), "仅接受恢复身份读取");
            var headers = string.Join(' ', request.Headers.SelectMany(h => h.Value));
            Assert.True(headers.Contains(OldToken, StringComparison.Ordinal), "仅观察占位旧令牌");
            OldTokenRequests++;
            return Json(new { }, HttpStatusCode.Unauthorized);
        }

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }

    private sealed class VmRuntime
    {
        private readonly Type _vm;
        internal VmRuntime(string shellPath, string sourceRoot)
        {
            var root = Path.GetFullPath(sourceRoot);
            var fullPath = Path.GetFullPath(shellPath);
            var releaseRoot = Path.Combine(root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
            if (!File.Exists(Path.Combine(root, "Momoka.sln")) || !fullPath.StartsWith(releaseRoot, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(fullPath) != "Momoka.dll") throw new InvalidOperationException("必须传当前树x64 Release Shell程序集");
            var directory = Path.GetDirectoryName(fullPath)!;
            var coreBytes = File.ReadAllBytes(typeof(EmbySession).Assembly.Location);
            Assert.True(SHA256.HashData(coreBytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "Momoka.Core.dll")))), "Shell和测试必须使用同一当前Core输出");
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var path = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            };
            var shell = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
            _vm = shell.GetType("Momoka.Shell.ViewModels.SignInViewModel", throwOnError: true)!;
        }
        internal Vm Create(Fixture f) => new(_vm, f);
    }

    private sealed class Vm : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly object _value;
        internal int SignedIn { get; private set; }
        internal bool NoticeOpen => (bool)Get("NoticeOpen")!;
        internal Vm(Type type, Fixture f)
        {
            _value = Activator.CreateInstance(type)!;
            type.GetEvent("SignedIn", Members)!.GetAddMethod(nonPublic: true)!.Invoke(_value, [(EventHandler)((_, _) => SignedIn++)]);
            Call("Attach", f.Service, f.Session, f.Vault);
        }
        internal void Set(string name, object value) => _value.GetType().GetProperty(name, Members)!.SetValue(_value, value);
        private object? Get(string name) => _value.GetType().GetProperty(name, Members)!.GetValue(_value);
        internal Task ConnectAsync() => Command("ConnectCommand");
        internal Task SignInAsync()
        {
            Set("Username", "new-user");
            Set("Password", NewPassword);
            return Command("SignInCommand");
        }
        internal void Detach() => Call("Detach");
        private Task Command(string name) => (Task)Invoke(Get(name)!, "ExecuteAsync", [null])!;
        private object? Call(string name, params object?[] arguments) => Invoke(_value, name, arguments);
        private static object? Invoke(object target, string name, object?[] arguments)
        {
            var method = target.GetType().GetMethods(Members).Single(m => m.Name == name && m.GetParameters().Length == arguments.Length);
            try { return method.Invoke(target, arguments); }
            catch (TargetInvocationException error) when (error.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }
        }
        public void Dispose() => ((IDisposable)_value).Dispose();
    }
}

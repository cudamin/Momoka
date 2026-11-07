using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Two real account restores share one anonymous profile; HTTP gates determine completion order.</summary>
internal static class RestoreParallelCorrectionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    internal static void Register()
    {
        foreach (var branch in new[] { "token", "expired-token", "password" })
            foreach (var first in new[] { "a", "b" })
                foreach (var prefix in new[] { "/media/", "/media/emby/" })
                    foreach (var knownId in new[] { true, false })
                        Test($"R03-003 同服纠正 {branch} {prefix} known={knownId} 先完成{first}", async () =>
                        {
                            using var f = new Fixture(branch, prefix, knownId);
                            var restoring = f.Session.RestoreAllAsync(CancellationToken.None);
                            await f.Transport.AllEntered().WaitAsync(Limit);
                            f.Transport.Release(first);
                            await f.OneOnline.Task.WaitAsync(Limit);
                            f.Transport.Release(first == "a" ? "b" : "a");
                            Assert.Equal(2, await restoring.WaitAsync(Limit), "同伴纠正产品与API入口不是用户改地址");
                            Assert.Equal("b", f.Session.Account!.Id, "默认账号不由响应完成顺序决定");
                            Assert.Equal(MediaServerKind.Jellyfin, f.Server.ServerKind);
                            Assert.Equal("same-server", f.Server.ServerId);
                            Assert.Equal("https://pair.invalid" + prefix, f.Session.Connection!.ApiBase.AbsoluteUri);
                            Assert.Equal(2, f.Session.Sources.Select(source => source.AccountProfileId).Distinct().Count());
                            Assert.True(await f.Session.TryRestoreAsync(f.Server, f.B, CancellationToken.None));
                            Assert.Equal(2, f.Session.Sources.Count, "显式重试不重复增加来源");
                            var loaded = f.Store.LoadStrict();
                            Assert.Equal("b", loaded.LastAccountId);
                            Assert.Equal(MediaServerKind.Jellyfin, loaded.Servers.Single().ServerKind);
                            Assert.Equal("same-server", loaded.Servers.Single().ServerId);
                        });

        foreach (var edit in new[] { "url", "id", "kind" })
            Test("R03-003 用户编辑绑定 " + edit + " 仍拒绝迟到恢复", async () =>
            {
                using var f = new Fixture("token", "/media/", true);
                var restoring = f.Session.RestoreAllAsync(CancellationToken.None);
                await f.Transport.AllEntered().WaitAsync(Limit);
                f.Transport.Release("a");
                await f.OneOnline.Task.WaitAsync(Limit);
                if (edit == "url") f.Server.Url = "https://changed.invalid/other";
                else if (edit == "id") f.Server.ServerId = "changed-server";
                else f.Server.ServerKind = MediaServerKind.FnNas;
                f.Transport.Release("b");
                Assert.Equal(1, await restoring.WaitAsync(Limit));
                Assert.False(f.Session.Sources.Any(source => source.AccountProfileId == "b"));
                Assert.Equal("placeholder-token-b", f.Vault.GetAccessToken(f.B), "迟到结果不能改保存凭据");
                Assert.Equal(edit == "id" ? "changed-server" : "same-server", f.Server.ServerId);
                Assert.Equal(0, f.Transport.PasswordRequests);
            });

        Test("R03-003 第二账号取消仍不采用迟到恢复", async () =>
        {
            using var f = new Fixture("token", "/media/", true);
            using var canceled = new CancellationTokenSource();
            var first = f.Session.TryRestoreAsync(f.Server, f.A, CancellationToken.None);
            var second = f.Session.TryRestoreAsync(f.Server, f.B, canceled.Token);
            await f.Transport.AllEntered().WaitAsync(Limit);
            f.Transport.Release("a");
            Assert.True(await first.WaitAsync(Limit));
            canceled.Cancel();
            f.Transport.Release("b");
            try { await second.WaitAsync(Limit); throw new AssertionException("取消后的恢复不应采用来源"); }
            catch (OperationCanceledException) { }
            Assert.Equal(1, f.Session.Sources.Count);
            Assert.Equal("placeholder-token-b", f.Vault.GetAccessToken(f.B));
        });

        Test("R03-003 原为Jellyfin同服多账号对照正常", async () =>
        {
            using var f = new Fixture("token", "/media/", true);
            f.Server.ServerKind = MediaServerKind.Jellyfin;
            var restoring = f.Session.RestoreAllAsync(CancellationToken.None);
            await f.Transport.AllEntered().WaitAsync(Limit);
            f.Transport.Release("b");
            await f.OneOnline.Task.WaitAsync(Limit);
            f.Transport.Release("a");
            Assert.Equal(2, await restoring.WaitAsync(Limit));
            Assert.Equal("b", f.Session.Account!.Id);
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "restore-parallel-fixtures", Guid.NewGuid().ToString("N"));
        internal AppSettings Settings { get; } = new() { AggregateMode = true };
        internal ServerProfile Server { get; }
        internal AccountProfile A { get; } = new() { Id = "a", Username = "a", UserId = "a", AutoConnect = true };
        internal AccountProfile B { get; } = new() { Id = "b", Username = "b", UserId = "b", AutoConnect = true };
        internal CredentialVault Vault { get; } = new(PassthroughSecretProtector.Instance);
        internal SettingsStore Store { get; }
        internal Transport Transport { get; }
        internal EmbySession Session { get; }
        internal TaskCompletionSource OneOnline { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Fixture(string branch, string prefix, bool knownId)
        {
            Directory.CreateDirectory(_root);
            Server = new() { Id = "shared-profile", Name = "anonymous", Url = "https://pair.invalid" + prefix.TrimEnd('/'),
                ServerKind = MediaServerKind.Emby, ServerId = knownId ? "same-server" : null };
            foreach (var account in new[] { A, B })
            {
                if (branch != "password") Vault.SetAccessToken(account, "placeholder-token-" + account.Id);
                Vault.SetPassword(account, "placeholder-password-" + account.Id, true);
                Server.Accounts.Add(account);
            }
            Settings.Servers.Add(Server);
            Settings.LastServerId = Server.Id;
            Settings.LastAccountId = B.Id;
            Store = new(new AppPaths(_root), PassthroughSecretProtector.Instance);
            Transport = new(branch, prefix);
            Session = new(Settings, Store, Vault, DeviceIdentity.Create("parallel-restore-fixture", "0"), Transport);
            Session.SourcesChanged += (_, _) => { if (Session.Sources.Count > 0) OneOnline.TrySetResult(); };
        }

        public void Dispose()
        {
            Session.Dispose();
            // Retain this tree's anonymous settings documents with the red/green evidence.
        }
    }

    private sealed class Transport(string branch, string prefix) : HttpMessageHandler
    {
        private readonly Dictionary<string, TaskCompletionSource> _entered = new()
        { ["a"] = new(TaskCreationOptions.RunContinuationsAsynchronously), ["b"] = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        private readonly Dictionary<string, TaskCompletionSource> _release = new()
        { ["a"] = new(TaskCreationOptions.RunContinuationsAsynchronously), ["b"] = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        internal int PasswordRequests { get; private set; }
        internal Task AllEntered() => Task.WhenAll(_entered.Values.Select(value => value.Task));
        internal void Release(string user) => _release[user].TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("pair.invalid", request.RequestUri!.Host, "仅匿名传输，不请求用户媒体服务器");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("System/Info/Public", StringComparison.Ordinal))
                return Json(new { ProductName = "Jellyfin", Id = "same-server", ServerName = "anonymous", Version = "12.2.0" });
            var authorization = string.Join(' ', request.Headers.SelectMany(header => header.Value));
            var user = authorization.Contains("placeholder-token-a", StringComparison.Ordinal) ? "a" : "b";
            if (path == prefix + "Users/Me") return Json(new { Id = user, Name = user, Policy = new { IsAdministrator = false } });
            if (path == prefix + "Users/AuthenticateByName")
            {
                PasswordRequests++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                user = body.RootElement.GetProperty("Username").GetString()!;
                Assert.True(body.RootElement.GetProperty("Pw").GetString() == "placeholder-password-" + user, "仅接受匿名占位密码");
                if (branch == "password") await Gate(user);
                return Json(new { AccessToken = "placeholder-fresh-" + user, ServerId = "same-server", User = new { Id = user, Name = user } });
            }
            Assert.Equal(prefix + "UserViews", path);
            await Gate(user);
            return branch == "expired-token" ? Json(new { }, HttpStatusCode.Unauthorized)
                : Json(new { Items = new[] { new { Id = "view-" + user, Name = user } }, TotalRecordCount = 1 });
        }

        private async Task Gate(string user)
        {
            _entered[user].TrySetResult();
            await _release[user].Task.ConfigureAwait(false); // A remote reply may arrive after local cancellation.
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}

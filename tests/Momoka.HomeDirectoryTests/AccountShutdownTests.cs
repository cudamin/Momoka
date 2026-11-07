using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal static class AccountShutdownTests
{
    internal static void Register(Action<string, Func<Task>> test)
    {
        foreach (var stage in new[] { "translation", "player" })
            foreach (var outcome in new[] { "success", "failure" })
                test($"R03-005 {stage}关闭等待中迟到登录{outcome}不接回外壳", () => Run(async () =>
                {
                    using var f = new Fixture(stage);
                    var signingIn = f.SignIn();
                    await f.Transport.Entered.Task;
                    var stopping = f.Shell.ShutdownAsync();
                    await f.StopEntered();
                    var canceledBeforeReply = f.Transport.Token.IsCancellationRequested;
                    f.Transport.Fail = outcome == "failure";
                    f.Transport.Release.TrySetResult();
                    await signingIn;
                    var repeated = f.Shell.ShutdownAsync();
                    f.ReleaseStop();
                    await stopping;
                    Assert.True(canceledBeforeReply, "首个关闭await前必须撤销真实登录票");
                    Assert.False(f.Session.IsSignedIn);
                    Assert.Equal(0, f.Transport.DirectoryRequests);
                    Assert.Equal(0, f.Shell.ContentFrame.Navigations);
                    Assert.False(f.Settings.Servers.Single().Accounts.Any(account => account.HasSavedToken));
                    Assert.True(ReferenceEquals(stopping, repeated), "重复关闭共用原收尾Task");
                }));

        foreach (var stage in new[] { "translation", "player" })
            test($"R03-005 {stage}关闭等待中不能重新提交登录", () => Run(async () =>
            {
                using var f = new Fixture(stage);
                var stopping = f.Shell.ShutdownAsync();
                await f.StopEntered();
                var signingIn = f.SignIn();
                f.Transport.Release.TrySetResult();
                await signingIn;
                f.ReleaseStop();
                await stopping;
                Assert.Equal(0, f.Transport.Authentications, "Dispose终止未来命令，单Cancel不能做到");
                Assert.False(f.Session.IsSignedIn);
            }));

        foreach (var entry in new[] { "enter", "show", "start", "switch", "retry", "aggregate", "source-event", "login-event" })
            test("R03-005 关闭后入口不复活 " + entry, () => Run(async () =>
            {
                using var f = new Fixture("player");
                f.Transport.Hold = false;
                await f.SignIn();
                await Task.Yield();
                Assert.True(f.Session.IsSignedIn, "关闭前合法登录是正常对照");
                Assert.True(f.Shell.ContentFrame.Navigations > 0);
                var stopping = f.Shell.ShutdownAsync();
                await f.StopEntered();
                var reads = f.Transport.DirectoryRequests;
                var sessionTextChanges = 0;
                f.Shell.Login.PropertyChanged += (_, changed) =>
                {
                    if (changed.PropertyName == nameof(f.Shell.Login.SignInText)) sessionTextChanges++;
                };
                switch (entry)
                {
                    case "enter": await f.Shell.Enter(); break;
                    case "show": f.Shell.RequestSignIn(); break;
                    case "start": await f.Shell.StartAsync(); break;
                    case "switch": await f.Shell.SwitchForTest(f.Server, f.Session.Account!); break;
                    case "retry": await f.Shell.Retry(); break;
                    case "aggregate": await f.Shell.AggregateForTest(); break;
                    case "source-event": f.Shell.ReplaySourcesChanged(); break;
                    case "login-event": f.Shell.ReplaySignedIn(); break;
                }
                await Task.Yield();
                f.ReleaseStop();
                await stopping;
                Assert.Equal(reads, f.Transport.DirectoryRequests, "关闭后不开始新目录读取");
                Assert.Equal(0, f.Shell.AggregateStarts, "关闭后不开始新的聚合连接");
                Assert.Equal(0, sessionTextChanges, "迟到来源通知不能更新已关闭登录VM");
                Assert.False(f.Shell.LoginVisible, "关闭后不能重建登录页");
                Assert.Null(f.Shell.ContentFrame.Content, "既有ReleaseContent清理保持，不重建Frame");
                Assert.Equal(0, f.Shell.SourceOperations);
            }));

        test("R03-005 已排队退出通知在关闭后不能重新显示登录", () => Run(async () =>
        {
            using var f = new Fixture("player");
            f.Transport.Hold = false;
            await f.SignIn();
            await Task.Yield();
            f.Shell.Notifications.Hold = true;
            f.Session.SignOut();
            var stopping = f.Shell.ShutdownAsync();
            await f.StopEntered();
            f.Shell.Notifications.Drain();
            f.Shell.ReplaySignedOut();
            f.ReleaseStop();
            await stopping;
            Assert.False(f.Shell.LoginVisible);
            Assert.Null(f.Shell.ContentFrame.Content);
        }));

        test("R03-005 关闭不撤销原来源操作及其计数收尾", () => Run(async () =>
        {
            using var f = new Fixture("player");
            f.Transport.Hold = false;
            await f.SignIn();
            await Task.Yield();
            var scope = f.Session.Capture();
            f.Shell.BeginSourceOperation();
            f.Transport.HoldItem = true;
            var read = scope.ExecuteAsync((client, token) => client.GetItemAsync("bound-item", token), CancellationToken.None);
            await f.Transport.ItemEntered.Task;
            var stopping = f.Shell.ShutdownAsync();
            await f.StopEntered();
            f.Transport.ItemRelease.TrySetResult();
            var item = await read;
            var availableDuringStop = scope.IsAvailable;
            f.Shell.FinishSourceOperation();
            f.ReleaseStop();
            await stopping;
            Assert.Equal("bound-item", item.Id);
            Assert.True(availableDuringStop, "Shell关闭不提前Dispose/SignOut真实来源");
            Assert.Equal(0, f.Shell.SourceOperations);
            Assert.False(f.Shell.PendingSourcesChange);
            Assert.Null(f.Shell.ContentFrame.Content);
        }));
    }

    private static Task Run(Func<Task> run)
    {
        using var queue = new UiQueue();
        queue.Run(run);
        return Task.CompletedTask;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _stage;
        internal AppSettings Settings { get; } = new();
        internal ServerProfile Server { get; } = new() { Id = "anonymous-profile", Url = "https://close.invalid", ServerId = "close-server" };
        internal AuthTransport Transport { get; } = new();
        internal EmbySession Session { get; }
        internal ShellPage Shell { get; }
        internal Momoka.Shell.Services.SubtitleTranslationController Translation { get; } = new();
        private readonly SettingsService _service;

        internal Fixture(string stage)
        {
            _stage = stage;
            var root = Path.Combine(AppContext.BaseDirectory, "shutdown-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Settings.Servers.Add(Server);
            var protector = PassthroughSecretProtector.Instance;
            var store = new SettingsStore(new AppPaths(root), protector);
            var vault = new CredentialVault(protector);
            _service = new(store, Settings);
            Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [Settings, store, vault, DeviceIdentity.Create("shutdown-fixture", "0"), Transport], null)!;
            Translation.Hold = stage == "translation";
            Shell = new(Session, Settings, _service, vault, Translation);
            Shell.PlayerStop.Hold = stage == "player";
        }

        internal Task SignIn()
        {
            Shell.Login.Address = Server.Url;
            Shell.Login.Username = "new-user";
            Shell.Login.Password = "placeholder-close-password";
            return Shell.Login.SignInCommand.ExecuteAsync(null);
        }
        internal Task StopEntered() => _stage == "translation" ? Translation.Entered.Task : Shell.PlayerStop.Entered.Task;
        internal void ReleaseStop()
        {
            Translation.Release.TrySetResult();
            Shell.PlayerStop.Release.TrySetResult();
        }
        public void Dispose()
        {
            Shell.DisposeLogin();
            Session.Dispose();
            _service.Dispose();
        }
    }

    private sealed class AuthTransport : HttpMessageHandler
    {
        internal bool Hold { get; set; } = true;
        internal bool Fail { get; set; }
        internal bool HoldItem { get; set; }
        internal int Authentications { get; private set; }
        internal int DirectoryRequests { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ItemEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ItemRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("close.invalid", request.RequestUri!.Host, "只接受匿名来源，不接触真实媒体服务器");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("System/Info/Public", StringComparison.Ordinal)) return Json(new { Id = "close-server", ProductName = "Emby", ServerName = "anonymous" });
            if (path.EndsWith("Users/AuthenticateByName", StringComparison.Ordinal))
            {
                Authentications++;
                Token = token;
                Entered.TrySetResult();
                if (Hold) await Release.Task.ConfigureAwait(false);
                return Fail ? Json(new { }, HttpStatusCode.Unauthorized)
                    : Json(new { AccessToken = "placeholder-close-token", ServerId = "close-server", User = new { Id = "new-user", Name = "new-user" } });
            }
            if (path.EndsWith("/Views", StringComparison.Ordinal))
            {
                DirectoryRequests++;
                return Json(new { Items = Array.Empty<object>(), TotalRecordCount = 0 });
            }
            Assert.True(path.EndsWith("Items/bound-item", StringComparison.Ordinal), "只接受已捕获来源的匿名读取");
            ItemEntered.TrySetResult();
            if (HoldItem) await ItemRelease.Task.ConfigureAwait(false);
            return Json(new { Id = "bound-item", Name = "anonymous" });
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}

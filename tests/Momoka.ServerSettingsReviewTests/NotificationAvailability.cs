using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.ViewModels;
using Momoka.Tests;

internal static class NotificationAvailability
{
    internal static async Task RunAsync()
    {
        const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var kind in Enum.GetValues<MediaServerKind>())
            foreach (bool? administrator in new bool?[] { null, true, false })
                foreach (var sourceBound in new[] { false, true })
                {
                    var settings = new AppSettings();
                    using var handler = new RejectNetwork();
                    var protector = new ForbiddenSecrets();
                    using var session = new EmbySession(settings, new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "forbidden|settings")), protector),
                        new CredentialVault(protector), new("fixture", "fixture", "fixture", "1"), handler);
                    using var http = new EmbyHttp(handler);
                    var connection = new EmbyConnection(new Uri("https://notification-availability.invalid/emby/"), "", "fixture", "fixture", "fixture",
                        new("fixture", "fixture", "fixture", "1"))
                    { ServerKind = kind, IsAdministrator = administrator };
                    var scope = new EmbySessionScope(session, new(new EmbyClient(http, connection), new ServerProfile { Id = "server" }, new AccountProfile { Id = "account" }));
                    ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", members)!.GetValue(session)!).Add(scope.Key, scope);
                    typeof(EmbySession).GetField("_current", members)!.SetValue(session, sourceBound ? null : scope);
                    using var vm = new NotificationsViewModel();
                    using var service = new SettingsService(null!, settings);
                    vm.Attach(session, service, sourceBound ? scope.Key : null);
                    var allowed = kind == MediaServerKind.Emby && administrator != false;
                    Assert.Equal(allowed, vm.CanRun);
                    Assert.Equal(allowed, vm.IsCurrentSession);
                    if (!allowed)
                    {
                        await vm.ReloadAsync();
                        await vm.AddNotificationCommand.ExecuteAsync(null);
                        await vm.RefreshCommand.ExecuteAsync(null);
                        Assert.Equal(0, handler.Requests, "不支持类型或非管理员不能借通知命令发请求");
                    }
                }
        await RejectRevokedEditorAsync();
        Console.WriteLine("通知后端守卫：全部服务器类型、管理员状态、来源绑定及编辑中降权零请求通过。");
    }

    private static async Task RejectRevokedEditorAsync()
    {
        var settings = new AppSettings();
        using var handler = new EmptyReads();
        var protector = new ForbiddenSecrets();
        using var session = new EmbySession(settings, new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "forbidden|settings")), protector),
            new CredentialVault(protector), new("fixture", "fixture", "fixture", "1"), handler);
        using var http = new EmbyHttp(handler);
        var connection = new EmbyConnection(new Uri("https://notification-availability.invalid/emby/"), "", "fixture", "fixture", "fixture",
            new("fixture", "fixture", "fixture", "1"))
        { IsAdministrator = true };
        var scope = new EmbySessionScope(session, new(new EmbyClient(http, connection), new ServerProfile { Id = "server" }, new AccountProfile { Id = "account" }));
        const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Instance;
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", members)!.GetValue(session)!).Add(scope.Key, scope);
        typeof(EmbySession).GetField("_current", members)!.SetValue(session, scope);
        using var vm = new NotificationsViewModel();
        using var service = new SettingsService(null!, settings);
        vm.Attach(session, service, scope.Key);
        var editorChecked = false;
        vm.ShowEditor = async context =>
        {
            session.ReplaceClient(scope, connection with { IsAdministrator = false });
            var requests = handler.Requests;
            Assert.False(vm.CanRun);
            Assert.False(vm.IsCurrentSession);
            await AssertCancelled(() => context.SaveAsync(context.Entry));
            await AssertCancelled(async () => { await context.TestAsync(context.Entry); });
            Assert.Equal(requests, handler.Requests, "降权后旧编辑器不得保存或测试");
            editorChecked = true;
        };
        var row = new NotificationRow(new UserNotificationInfo { Id = "fixture", NotifierKey = "fixture" }, "", vm);
        vm.Entries.Add(row);
        await vm.EditCommand.ExecuteAsync(row);
        Assert.True(editorChecked, "必须实际打开编辑上下文再降权");
        session.ReplaceClient(scope, connection);
        await vm.ReloadAsync();
        Assert.True(vm.CanRun, "权限恢复后可用性立即恢复");
    }

    private static async Task AssertCancelled(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("失效编辑器未拒绝操作");
    }

    private sealed class EmptyReads : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "notification-availability.invalid" || request.Method != HttpMethod.Get)
                throw new InvalidOperationException("仅允许匿名内存读取");
            Requests++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new InvalidOperationException("禁止真实请求");
        }
    }

    private sealed class ForbiddenSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("禁止凭据 IO");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("禁止凭据 IO");
    }
}

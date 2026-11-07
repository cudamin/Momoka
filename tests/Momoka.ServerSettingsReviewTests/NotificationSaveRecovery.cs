using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.ViewModels;

internal static class NotificationSaveRecovery
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "notification-save-recovery", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings();
        using var handler = new Capture();
        var protector = new ForbiddenSecrets();
        using var session = new EmbySession(settings, new SettingsStore(new AppPaths(root), protector), new CredentialVault(protector),
            new DeviceIdentity("fixture", "fixture", "fixture", "1"), handler);
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        var connection = new EmbyConnection(new Uri("https://notification-recovery.invalid/emby/"), "", "fixture", "fixture", "fixture", new("fixture", "fixture", "fixture", "1"));
        var scope = new EmbySessionScope(session, new(new EmbyClient(http, connection), new ServerProfile { Id = "server" }, new AccountProfile { Id = "account" }));
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!).Add(scope.Key, scope);
        typeof(EmbySession).GetField("_current", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session, scope);
        using var vm = new NotificationsViewModel();
        vm.Attach(session, new SettingsService(null!, settings));
        vm.PickService = _ => Task.FromResult<NotificationServiceInfo?>(null);
        vm.ShowEditor = context =>
        {
            context.Entry.EventIds = handler.Posts.Count == 0 ? ["B"] : context.Entry.EventIds;
            context.Entry.FriendlyName = "renamed";
            return context.SaveAsync(context.Entry);
        };
        vm.Entries.Add(new(new UserNotificationInfo { Id = "notification", NotifierKey = "anonymous", EventIds = ["A"] }, "A", vm));
        await vm.EditCommand.ExecuteAsync(vm.Entries[0]);
        if (!vm.Entries[0].Info.EventIds.SequenceEqual(new[] { "B" })) throw new InvalidOperationException("R15-003 confirmed notification not retained after read-back failure");
        await vm.EditCommand.ExecuteAsync(vm.Entries[0]);
        if (handler.Posts.Count != 2 || handler.Posts.Any(post => !post.EventIds.SequenceEqual(new[] { "B" })))
            throw new InvalidOperationException("R15-003 second edit sent obsolete events");
        handler.AllowReads = true;
        await vm.ReloadAsync();
        if (!vm.Entries[0].Info.EventIds.SequenceEqual(new[] { "B" })) throw new InvalidOperationException("R15-003 successful read-back lost confirmation");
        handler.AllowReads = false;
        vm.ShowEditor = context => context.SaveAsync(context.Entry);
        await vm.AddNotificationCommand.ExecuteAsync(null);
        if (vm.CanAdd) throw new InvalidOperationException("R15-003 unrefreshed confirmed create permits duplicate create");
        var writes = handler.Posts.Count;
        await vm.AddNotificationCommand.ExecuteAsync(null);
        if (handler.Posts.Count != writes) throw new InvalidOperationException("R15-003 confirmed create was repeated");
        handler.AllowReads = true;
        await vm.ReloadAsync();
        if (!vm.CanAdd) throw new InvalidOperationException("R15-003 successful refresh does not restore create");
        Console.WriteLine("R15-003 actual Notifications VM two saves retain confirmed events after failed refresh.");
    }
    private sealed class Capture : HttpMessageHandler
    {
        internal List<UserNotificationInfo> Posts { get; } = [];
        internal bool AllowReads { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "notification-recovery.invalid") throw new InvalidOperationException("only anonymous transport is allowed");
            if (request.Method == HttpMethod.Post)
            {
                Posts.Add(JsonSerializer.Deserialize<UserNotificationInfo>(await request.Content!.ReadAsStringAsync(cancellationToken))!);
                return new(HttpStatusCode.NoContent) { Content = new StringContent("") };
            }
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.Contains("Notifications", StringComparison.OrdinalIgnoreCase))
            {
                var path = request.RequestUri.AbsolutePath;
                if (path.EndsWith("Services", StringComparison.Ordinal))
                    return new(HttpStatusCode.OK) { Content = new StringContent("[{\"Id\":\"anonymous\"}]", Encoding.UTF8, "application/json") };
                if (path.EndsWith("Defaults", StringComparison.Ordinal))
                    return new(HttpStatusCode.OK) { Content = new StringContent("{\"NotifierKey\":\"anonymous\",\"Enabled\":true}", Encoding.UTF8, "application/json") };
                if (AllowReads)
                    return new(HttpStatusCode.OK) { Content = new StringContent(path.EndsWith("Configured", StringComparison.Ordinal)
                        ? JsonSerializer.Serialize(Posts.Take(1)) : "[]", Encoding.UTF8, "application/json") };
                return new(HttpStatusCode.InternalServerError) { Content = new StringContent("anonymous failure", Encoding.UTF8) };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ForbiddenSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new InvalidOperationException("no secret IO");
        public string Unprotect(string cipherText) => throw new InvalidOperationException("no secret IO");
    }
}

using System.Net;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;

internal static class PermissionCache
{
    public static async Task RunAsync()
    {
        var settings = new AppSettings();
        var server = new ServerProfile { Id = "server", Url = "http://permission.invalid" };
        var account = new AccountProfile { Id = "account", UserId = "u1", ProtectedAccessToken = "anonymous" };
        server.Accounts.Add(account);
        settings.Servers.Add(server);
        var store = new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "isolated-settings", Guid.NewGuid().ToString("N"))), PassthroughSecretProtector.Instance);
        using var handler = new PolicyTransport();
        using var session = new EmbySession(settings, store, new CredentialVault(PassthroughSecretProtector.Instance), DeviceIdentity.Create("anonymous", "1"), handler);
        var capabilities = new ServerCapabilities(session);
        if (!await session.TryRestoreAsync(server, account, default)) throw new InvalidOperationException("Restore failed");
        var old = session.Capture();
        await capabilities.ProbeAsync();
        Check(capabilities, old.Key, false);
        session.TakeOffline(server, account);
        handler.Allow = true;
        if (!await session.TryRestoreAsync(server, account, default)) throw new InvalidOperationException("Second restore failed");
        var current = session.Capture();
        if (ReferenceEquals(old.UserStateOwner, current.UserStateOwner) || old.Key != current.Key)
            throw new InvalidOperationException("Fixture did not replace the online owner");
        Check(capabilities, current.Key, null);
        handler.Fail = true;
        await capabilities.ProbeAsync();
        Check(capabilities, current.Key, null);
        handler.Fail = false;
        await capabilities.ProbeAsync();
        Check(capabilities, current.Key, true);

        handler.Hold = true;
        var pending = capabilities.ProbeAsync();
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.TakeOffline(server, account);
        handler.Hold = false;
        handler.Allow = false;
        if (!await session.TryRestoreAsync(server, account, default)) throw new InvalidOperationException("Third restore failed");
        await capabilities.ProbeAsync();
        handler.Release.SetResult();
        await pending;
        Check(capabilities, session.Capture().Key, false);
        Console.WriteLine("R17: relogin, failed/successful probe and late previous-owner response passed.");
    }

    private static void Check(ServerCapabilities capabilities, string key, bool? expected)
    {
        var permissions = capabilities.PermissionsFor(key);
        var menu = ItemMenu.For(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie }, permissions: permissions);
        if (permissions.CanDownload != expected || menu.Any(item => item.Command == ItemCommand.Download) != (expected != false))
            throw new InvalidOperationException("Menu permissions used the wrong online identity");
    }

    private sealed class PolicyTransport : HttpMessageHandler
    {
        public bool Allow { get; set; }
        public bool Fail { get; set; }
        public bool Hold { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "permission.invalid" || request.Method != HttpMethod.Get)
                throw new InvalidOperationException("Unexpected fixture request");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("System/Info/Public", StringComparison.Ordinal) || path.EndsWith("System/Info", StringComparison.Ordinal))
                return Json("{\"ServerName\":\"Anonymous\",\"Version\":\"4.10.1\",\"Id\":\"s1\"}");
            if (path.EndsWith("/Views", StringComparison.Ordinal)) return Json("{\"Items\":[]}");
            if (!path.EndsWith("/Users/u1", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture route");
            var allow = Allow;
            if (Hold) { Entered.TrySetResult(); await Release.Task; }
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return Json("{\"Id\":\"u1\",\"Name\":\"anonymous\",\"Policy\":{\"EnableContentDownloading\":" + (allow ? "true" : "false") + "}}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

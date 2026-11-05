using System.Net;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell.ViewModels;

var observations = new List<object>();
foreach (var returnedId in new[] { "server-a", "server-b" })
foreach (var testFirst in new[] { false, true })
{
    var settings = new AppSettings();
    var server = new ServerProfile { Id = "profile", Url = "https://identity-review.invalid", Name = "Saved A", ServerId = "server-a" };
    var account = new AccountProfile
    {
        Id = "account", Username = "anonymous", UserId = "user-a", AutoConnect = true,
        ProtectedAccessToken = "opaque-token", ProtectedPassword = "opaque-password", RememberPassword = true
    };
    server.Accounts.Add(account);
    settings.Servers.Add(server);
    var isolated = Path.Combine(AppContext.BaseDirectory, "isolated-settings", returnedId + (testFirst ? "-tested" : "-untested"));
    var protector = new OpaqueProtector();
    var store = new SettingsStore(new AppPaths(isolated), protector);
    using var service = new SettingsService(store, settings);
    using var handler = new ChangedServer(returnedId);
    using var session = new EmbySession(settings, store, new CredentialVault(protector),
        DeviceIdentity.Create("review-six-identity", "1"), handler);
    var boundary = new ServersViewModel(session, service);
    if (testFirst) await boundary.RunTest(new ServerRow(server));
    var idBeforeRestore = server.ServerId;
    var restored = await session.TryRestoreAsync(server, account, CancellationToken.None);
    var credentialsExpected = returnedId == "server-a";
    if (handler.TokenRequests != (credentialsExpected ? 1 : 0) || handler.PasswordRequests != (credentialsExpected ? 1 : 0))
        throw new InvalidOperationException("Credential request count differs from source expectation");
    if (restored) throw new InvalidOperationException("Scripted authentication rejection must not restore");
    if (testFirst && boundary.Notice != (returnedId == "server-a" ? "连接成功" : "连接失败")) throw new InvalidOperationException("Unexpected connection test feedback");
    observations.Add(new { returnedId, testFirst, originalServerId = "server-a", idBeforeRestore,
        restored, handler.AnonymousRequests, handler.TokenRequests, handler.PasswordRequests,
        profileIdChanged = idBeforeRestore != "server-a", testNotice = boundary.Notice });
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = 4, actualNetworkRequests = 0, observations }, new JsonSerializerOptions { WriteIndented = true }));

foreach (var returnedId in new[] { "", "server-b" })
foreach (var hasAccount in new[] { false, true })
{
    var settings = new AppSettings();
    var server = new ServerProfile { Url = "https://identity-review.invalid", ServerId = "server-a" };
    if (hasAccount) server.Accounts.Add(new AccountProfile { Id = "account" });
    settings.Servers.Add(server);
    var protector = new OpaqueProtector();
    var store = new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "isolated-settings", Guid.NewGuid().ToString("N"))), protector);
    using var service = new SettingsService(store, settings);
    using var handler = new ChangedServer(returnedId);
    using var session = new EmbySession(settings, store, new CredentialVault(protector), DeviceIdentity.Create("review-identity", "1"), handler);
    var boundary = new ServersViewModel(session, service);
    await boundary.RunTest(new ServerRow(server));
    var expectedId = !hasAccount && returnedId.Length > 0 ? returnedId : "server-a";
    if (server.ServerId != expectedId || boundary.Notice != (hasAccount ? "连接失败" : "连接成功"))
        throw new InvalidOperationException("Empty identity or account-free probe broke the saved identity contract");
    if (handler.TokenRequests != 0 || handler.PasswordRequests != 0)
        throw new InvalidOperationException("Connection test sent credentials");
}
Console.WriteLine("R28: 8 anonymous connection-test and restore cases passed.");
await PlexDiscovery.RunAsync();
await PermissionCache.RunAsync();
await PlexSearch.RunAsync();
await FnNasArtwork.RunAsync();
DisplaySettings.Run();
await NotificationEditor.RunAsync();
await MetadataEditor.RunAsync();
ReviewEditorProbeContract.Run();

sealed class OpaqueProtector : ISecretProtector
{
    public string Protect(string plainText) => "opaque-only";
    public string Unprotect(string cipherText) => "anonymous-placeholder";
}

sealed class ChangedServer(string returnedId) : HttpMessageHandler
{
    public int AnonymousRequests { get; private set; }
    public int TokenRequests { get; private set; }
    public int PasswordRequests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI");
        if (uri.Host != "identity-review.invalid") throw new InvalidOperationException("Unexpected host");
        if (uri.AbsolutePath.EndsWith("System/Info/Public", StringComparison.Ordinal))
        {
            if (request.Headers.Contains("X-Emby-Token")) throw new InvalidOperationException("Anonymous probe carried token");
            AnonymousRequests++;
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { Id = returnedId, ServerName = "Anonymous endpoint", Version = "4.10.1", ProductName = "Emby Server" }));
        }
        if (uri.AbsolutePath.EndsWith("Users/user-a/Views", StringComparison.Ordinal))
        {
            if (!request.Headers.Contains("X-Emby-Token")) throw new InvalidOperationException("Expected token restore attempt");
            TokenRequests++;
            return Json(HttpStatusCode.Unauthorized, "{}");
        }
        if (uri.AbsolutePath.EndsWith("Users/AuthenticateByName", StringComparison.Ordinal))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (string.IsNullOrEmpty(body.RootElement.GetProperty("Pw").GetString())) throw new InvalidOperationException("Password fallback was empty");
            PasswordRequests++;
            return Json(HttpStatusCode.Unauthorized, "{}");
        }
        throw new InvalidOperationException("Unexpected route: " + uri.AbsolutePath);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

namespace Momoka.Shell.ViewModels
{
    internal sealed record ServerRow(ServerProfile Profile);

    // Only UI load/notice boundaries are replaced. The contested TestAsync method is extracted
    // verbatim; the actual session, gateway, vault, store and HTTP implementations remain in Core.
    internal sealed partial class ServersViewModel(EmbySession session, ISettingsService settings)
    {
        private enum InfoBarSeverity { Error, Success }
        private const string Category = "服务器";
        private readonly EmbySession _session = session;
        private readonly ISettingsService _settings = settings;
        private readonly LoadGeneration _loads = new();
        private bool Attached => true;
        public string? Notice { get; private set; }
        public Task RunTest(ServerRow row) => TestAsync(row);
        private CancellationToken BeginLoad() => _loads.Begin();
        private bool IsCurrent(CancellationToken token) => _loads.IsCurrent(token);
        private void EndLoad(CancellationToken token) { }
        private void Refresh() { }
        private void Notify(string title, string message, InfoBarSeverity severity) => Notice = title;
        private void Report(string title, Exception error) => Notice = title;
    }
}

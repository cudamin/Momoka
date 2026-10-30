using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Infrastructure;
using Momoka.Shell.Platform;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Tests;

internal sealed class MoviePilotFixture : IDisposable
{
    internal AppSettings Settings { get; } = new();
    internal MoviePilotTransport Http { get; } = new();
    internal MoviePilotClient Client { get; }
    internal MoviePilotService Service { get; }
    internal MoviePilotFixture()
    {
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "https://search-lifetime.invalid";
        Settings.MoviePilot.Username = "anonymous-fixture";
        var credentials = new MoviePilotCredentials(PassthroughSecretProtector.Instance);
        credentials.SetPassword(Settings.MoviePilot, "placeholder-no-account");
        Client = new(Http);
        Service = new(Client, credentials, Settings);
    }
    internal Task ConnectAsync() => Service.RefreshAvailabilityAsync();
    internal int Subscribers => Handlers.Length;
    internal EventHandler[] Handlers => ((EventHandler?)typeof(MoviePilotService)
        .GetField("AvailabilityChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Service))?
        .GetInvocationList().Cast<EventHandler>().ToArray() ?? [];
    public void Dispose() { Service.Dispose(); Client.Dispose(); Http.Dispose(); }
}

internal sealed class Launcher : ISystemLauncher;
internal sealed class MoviePilotTransport : HttpMessageHandler
{
    internal sealed record Request(string Method, string Path, string Query);
    internal List<Request> Requests { get; } = [];
    internal Func<Request, CancellationToken, Task<HttpResponseMessage?>>? Before { get; set; }
    internal int Searches => Requests.Count(request => request.Path == "media/search");
    internal int Writes => Requests.Count(request => request.Method is not "GET" && request.Path != "login/access-token");
    internal string SearchJson { get; set; } = """[{"title":"匿名结果","type":"电影","media_source":"themoviedb","media_id":"42","poster_path":"https://poster.invalid/fixture.jpg"}]""";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri is not { Host: "search-lifetime.invalid" } uri)
            throw new InvalidOperationException("禁止真实主机与网络备援");
        var sent = new Request(request.Method.Method, uri.AbsolutePath.Replace("/api/v1/", "", StringComparison.Ordinal), uri.Query);
        Requests.Add(sent);
        if (Before is { } before && await before(sent, token) is { } intercept) return intercept;
        return sent.Path switch
        {
            "login/access-token" => Reply("""{"access_token":"placeholder-only"}"""),
            "dashboard/system" => Reply("""{"version":"anonymous-fixture"}"""),
            "media/search" => Reply(SearchJson),
            "subscribe/" when sent.Method == "GET" => Reply("[]"),
            _ => throw new InvalidOperationException("禁止未登记请求：" + sent.Method + " " + sent.Path)
        };
    }
    internal static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

// Real Core session, scope, client, FnNas signer/transport; anonymous identity installed only in memory.
// The HTTP handler below is the sole endpoint. No settings Load/Save, actual credentials or login occurs.
internal sealed class FnNasFixture : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly NoSecrets _secrets = new();
    internal FnNasTransport Http { get; } = new();
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal ShellActions Actions { get; } = new();
    internal CardItem Card { get; }
    internal FnNasFixture()
    {
        var settings = new AppSettings();
        var device = new DeviceIdentity("anonymous fixture", "fixture", "fixture", "0");
        var store = new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "inert-no-settings")), _secrets);
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [settings, store, new CredentialVault(_secrets), device, Http], null)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("https://fnnas-lifetime.invalid/v/"),
            "placeholder-only", "fixture-user", "anonymous", "anonymous", device)
        { ServerKind = MediaServerKind.FnNas };
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = "fixture-server", Name = "anonymous", ServerKind = MediaServerKind.FnNas },
                new AccountProfile { Id = "fixture-account" }], null)!;
        Scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!).Add(Scope.Key, Scope);
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, Scope);
        Card = new(Scope.Bind(new EmbyItem { Id = "item", Name = "匿名匹配条目", Type = EmbyItemType.Movie }));
    }
    internal Task MatchAsync(Action? changed = null) => ItemCommands.MatchFixture(Scope, Actions, new(), Card, changed);
    public void Dispose()
    {
        ContentDialog.ShowEndpoint = null;
        Session.Dispose();
        Http.Dispose();
        Check.Equal(0, _secrets.Calls);
    }
    private sealed class NoSecrets : ISecretProtector
    {
        internal int Calls { get; private set; }
        public string Protect(string value) { Calls++; throw new InvalidOperationException("禁止凭据保护/写盘"); }
        public string Unprotect(string value) { Calls++; throw new InvalidOperationException("禁止真实凭据/DPAPI/登录备援"); }
    }
}

internal sealed class ShellActions : IShellActions
{
    internal List<(string Text, InfoBarSeverity Severity)> Notices { get; } = [];
    public void Notify(string text, InfoBarSeverity severity = InfoBarSeverity.Informational) => Notices.Add((text, severity));
}

internal sealed class FnNasTransport : HttpMessageHandler
{
    internal sealed record Request(string Method, string Path, string Body, CancellationToken Token);
    internal List<Request> Requests { get; } = [];
    internal Func<Request, Task<HttpResponseMessage?>>? Before { get; set; }
    internal int Searches => Requests.Count(request => request.Path == "/v/api/v1/scrap/search");
    internal int Writes => Requests.Count(request => request.Path == "/v/api/v1/scrap/rescrap");
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri is not { Host: "fnnas-lifetime.invalid" } uri)
            throw new InvalidOperationException("禁止真实主机与网络备援");
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
        var sent = new Request(request.Method.Method, uri.AbsolutePath, body, token);
        Requests.Add(sent);
        if (Before is { } before && await before(sent) is { } intercept) return intercept;
        return sent.Path switch
        {
            "/v/api/v1/media/itemfile/item" when sent.Method == "GET" => Reply("""[{"media_guid":"version-1","file_path":"/anonymous/1.mkv"}]"""),
            "/v/api/v1/scrap/search" when sent.Method == "POST" => Reply("""[{"title":"匿名候选","source":"tmdb","source_id":"42","type":"Movie","release_date":"2026-01-01"}]"""),
            "/v/api/v1/scrap/rescrap" when sent.Method == "POST" => Reply("{}"),
            _ => throw new InvalidOperationException("禁止未登记请求：" + sent.Method + " " + sent.Path)
        };
    }
    internal static HttpResponseMessage Reply(string data) => MoviePilotTransport.Reply("{\"code\":0,\"msg\":\"\",\"data\":" + data + "}");
}

// A queue controlled by the test proves network-complete callbacks which have not yet resumed UI code.
internal sealed class QueuedContext : SynchronizationContext
{
    internal System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> Pending { get; } = new();
    public override void Post(SendOrPostCallback callback, object? state) => Pending.Enqueue((callback, state));
    internal void Drain()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try { while (Pending.TryDequeue(out var work)) work.Callback(work.State); }
        finally { SetSynchronizationContext(previous); }
    }
}

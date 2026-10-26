using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell.Views;

namespace Momoka.Tests;

internal sealed class LifetimeFixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<CancellationTokenSource> _lifetimes = [];
    private readonly Dictionary<string, EmbySessionScope> _sources;
    private readonly MoviePilotClient _moviePilotClient;
    private readonly MoviePilotService _moviePilot;
    public MemorySettings Settings { get; } = new();
    public FakeReads Reads { get; } = new();
    public AppPaths Paths { get; }
    public EmbySession Session { get; }
    public ServerCapabilities Capabilities { get; }
    public FixtureServices Services { get; } = new();
    public object Vm { get; }

    public LifetimeFixture(MediaServerKind kind = MediaServerKind.Jellyfin, bool? administrator = true,
        IServerCapabilities? capabilities = null)
    {
        Paths = new AppPaths(Path.Combine(ShellRuntime.Current.SourceRoot, "work", "r018-r026", "fixture", Guid.NewGuid().ToString("N")));
        var protector = new DenySecrets();
        var device = new DeviceIdentity("settings-lifetime", "offline", "offline", "1");
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
            [Settings.Settings, new SettingsStore(Paths, protector), new CredentialVault(protector), device, Reads], null)!;
        _sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!;
        var a = Add("server", "a", kind, administrator);
        Select(a);
        Capabilities = new ServerCapabilities(Session);
        Vm = ShellRuntime.Current.New("SettingsViewModel");
        ShellRuntime.Current.Attach(Vm, Settings, Paths, capabilities ?? Capabilities);
        _moviePilotClient = new MoviePilotClient(new RejectHttp());
        _moviePilot = new MoviePilotService(_moviePilotClient, new MoviePilotCredentials(protector), Settings.Settings);
        Services.Add(Session);
        Services.Add<ISettingsService>(Settings);
        Services.Add(_moviePilot);
        Services.Add<IServerCapabilities>(capabilities ?? Capabilities);
    }

    public EmbySessionScope Add(string serverId, string accountId, MediaServerKind kind, bool? administrator = true)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        var server = new ServerProfile { Id = serverId, Name = "假服务器 " + serverId, Url = "https://settings-lifetime.invalid/" };
        var account = new AccountProfile { Id = accountId, Username = "占位账号 " + accountId };
        var connection = new EmbyConnection(new Uri(server.Url), "offline-placeholder", accountId, account.Username, server.Name, Session.Device)
        {
            ServerKind = kind,
            ServerVersion = kind == MediaServerKind.Jellyfin ? "11.0.0" : "1.0.0",
            ServerId = serverId,
            IsAdministrator = administrator
        };
        var client = new EmbyClient(http, connection);
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [client, server, account], null)!;
        var source = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Private, null, [Session, identity], null)!;
        _lifetimes.Add((CancellationTokenSource)identityType.GetProperty("Lifetime")!.GetValue(identity)!);
        _sources[source.Key] = source;
        return source;
    }

    // Change only in-memory ownership; do not call SignIn, SelectSource, SignOut, vault or Persist.
    public void Select(EmbySessionScope source, bool notify = false)
    {
        typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, source);
        if (notify) Notify();
    }

    public void Remove(EmbySessionScope source, bool notify = false)
    {
        _sources.Remove(source.Key);
        var state = typeof(EmbySessionScope).GetProperty("IdentityState", Private)!.GetValue(source)!;
        ((CancellationTokenSource)state.GetType().GetProperty("Lifetime")!.GetValue(state)!).Cancel();
        if (notify) Notify();
    }

    public void Permission(EmbySessionScope source, bool? administrator)
    {
        var state = typeof(EmbySessionScope).GetProperty("IdentityState", Private)!.GetValue(source)!;
        var client = (EmbyClient)state.GetType().GetProperty("Client")!.GetValue(state)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        var replacement = new EmbyClient(http, client.Connection with { IsAdministrator = administrator });
        state.GetType().GetProperty("Client")!.SetValue(state, replacement);
    }

    public void Notify() => typeof(EmbySession).GetMethod("NotifySourcesChanged", Private)!.Invoke(Session, null);
    public int SourceSubscribers => ((Delegate?)typeof(EmbySession).GetField("SourcesChanged", Private)!.GetValue(Session))?.GetInvocationList().Length ?? 0;
    public EmbySessionScope Source(string account) => _sources.Values.Single(source => source.AccountProfileId == account);
    public Task Refresh() => (Task)ShellRuntime.Current.Invoke(Vm, "RefreshServerAdministrationAsync")!;
    public bool Hidden => ShellRuntime.Current.Get<bool>(Vm, "ServerAdministrationHidden");
    public string[] Labels => ShellRuntime.Current.List(Vm, "Categories").Cast<object>()
        .Select(entry => ShellRuntime.Current.Get<string>(entry, "Label")).ToArray();
    public void Cancel() => ShellRuntime.Current.Invoke(Vm, "Cancel");
    public void Attach() => ShellRuntime.Current.Attach(Vm, Settings, Paths, Capabilities);

    public SettingsPage NewCachedPage(string category)
    {
        ShellRuntime.Current.NextPageViewModel = Vm;
        ShellRuntime.Current.Draft(Vm);
        var page = new SettingsPage();
        page.PrepareCached(new SettingsRequest(Services, category));
        return page;
    }

    public void Dispose()
    {
        ShellRuntime.Current.Invoke(Vm, "Dispose");
        _moviePilot.Dispose();
        _moviePilotClient.Dispose();
        Session.Dispose();
        foreach (var lifetime in _lifetimes) lifetime.Dispose();
        Reads.Dispose();
    }
}

internal sealed class FixtureServices : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];
    public void Add<T>(T service) where T : class => _services[typeof(T)] = service;
    public object? GetService(Type serviceType) => _services.TryGetValue(serviceType, out var service) ? service : null;
}

internal sealed class ReadTicket(string account)
{
    public string Account { get; } = account;
    public TaskCompletionSource<HttpResponseMessage> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Complete(string name = "") => Reply.TrySetResult(FakeReads.Json(
        name.Length == 0 ? "[]" : "[{\"Name\":\"" + name + "\",\"DisplayName\":\"" + name + "\",\"EnableInMainMenu\":true}]"));
}

internal sealed class FakeReads : HttpMessageHandler
{
    private readonly ConcurrentQueue<ReadTicket> _plugins = new();
    private readonly ConcurrentQueue<ReadTicket> _systems = new();
    private readonly ConcurrentQueue<ReadTicket> _users = new();
    private readonly ConcurrentQueue<ReadTicket> _started = new();
    public int TotalReads;
    public int PreferenceReads;
    public int PluginReads;
    public int SystemReads;
    public ReadTicket HoldPlugin(string account) { var ticket = new ReadTicket(account); _plugins.Enqueue(ticket); return ticket; }
    public ReadTicket HoldSystem(string account) { var ticket = new ReadTicket(account); _systems.Enqueue(ticket); return ticket; }
    public ReadTicket HoldUser(string account) { var ticket = new ReadTicket(account); _users.Enqueue(ticket); return ticket; }
    public bool Started(ReadTicket ticket) => _started.Contains(ticket);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "settings-lifetime.invalid")
            throw new InvalidOperationException("只允许假 HTTP 指定读取，不允许登录、写入或其他地址");
        Interlocked.Increment(ref TotalReads);
        var route = request.RequestUri.AbsolutePath;
        if (route.EndsWith("/web/ConfigurationPages", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref PluginReads);
            return Reply(_plugins, request, "[]");
        }
        if (route.EndsWith("/System/Info", StringComparison.Ordinal) || route.EndsWith("/System/Info/Public", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref SystemReads);
            return Reply(_systems, request, "{\"Version\":\"11.0.0\",\"ServerName\":\"offline\"}");
        }
        if (route.EndsWith("/Users/a", StringComparison.Ordinal) || route.EndsWith("/Users/b", StringComparison.Ordinal)
            || route.EndsWith("/users/me", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Json("{\"Id\":\"a\",\"Policy\":{\"IsAdministrator\":true}}"));
        if (route == "/api/v1/user/info")
        {
            Interlocked.Increment(ref PreferenceReads);
            return Reply(_users, request, "{\"code\":0,\"data\":{\"guid\":\"offline\",\"username\":\"offline\",\"is_admin\":true,\"lan\":\"en\",\"audio_lan\":\"zh\"}}");
        }
        if (route == "/api/v1/sys/config")
        {
            Interlocked.Increment(ref PreferenceReads);
            return Task.FromResult(Json("{\"code\":0,\"data\":{\"supported_languages\":[\"en\",\"zh\"]}}"));
        }
        throw new InvalidOperationException("夹具不支持这个读取边界：" + route);
    }

    private Task<HttpResponseMessage> Reply(ConcurrentQueue<ReadTicket> pending, HttpRequestMessage request, string fallback)
    {
        if (!pending.TryDequeue(out var ticket)) return Task.FromResult(Json(fallback));
        // Placeholder account identity, never inspect or print authorization headers.
        var requested = request.RequestUri!.Query;
        if (requested.Contains("UserId=", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("UserId=" + ticket.Account, requested);
        _started.Enqueue(ticket);
        // Deliberately ignore cancellation: late transport completions must still be rejected by production.
        return ticket.Reply.Task;
    }

    internal static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

internal sealed class RejectHttp : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        throw new InvalidOperationException("禁止 MoviePilot 或额外网络请求");
}

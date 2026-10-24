using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Emby;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>Install anonymous identities only. Never call login, restore, reauthentication or persistence.</summary>
internal sealed class PermissionFixture : IDisposable
{
    internal const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly DenySecrets _secrets = new();
    private readonly EmbyHttp _http;
    private readonly Dictionary<string, EmbySessionScope> _sources;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly string _forbiddenSettings;
    internal EmbySession Session { get; }
    internal ServerCapabilities Capabilities { get; }
    internal PermissionTransport Transport { get; } = new();
    internal EmbySessionScope A { get; }
    internal EmbySessionScope? B { get; }
    internal int SourceNotifications { get; private set; }
    internal int SignOutNotifications { get; private set; }

    internal PermissionFixture(MediaServerKind kind = MediaServerKind.FnNas, bool? administrator = true,
        MediaServerKind? second = null)
    {
        _forbiddenSettings = Path.Combine(Program.Root, "artifacts", "r011-current-permission", "forbidden-settings|no-IO");
        var settings = new AppSettings { AggregateMode = true };
        var paths = new AppPaths(_forbiddenSettings);
        var device = new DeviceIdentity("fixture-device", "fixture", "fixture", "0");
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [settings, new SettingsStore(paths, _secrets), new CredentialVault(_secrets), device, Transport], null)!;
        _http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        _sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
        A = Add("a", kind, administrator);
        B = second is { } secondKind ? Add("b", secondKind, administrator: true) : null;
        Select(A);
        Capabilities = new ServerCapabilities(Session);
        Session.SourcesChanged += (_, _) => SourceNotifications++;
        Session.SignedOut += (_, _) => SignOutNotifications++;
    }

    internal EmbySessionScope Add(string id, MediaServerKind kind, bool? administrator)
    {
        var path = kind == MediaServerKind.FnNas ? "v/" : kind == MediaServerKind.Emby ? "emby/" : "";
        var address = new Uri($"https://r011-{id}.invalid/{path}");
        var server = new ServerProfile { Id = "server-" + id, Name = "anonymous-" + id, Url = address.ToString(), ServerKind = kind };
        var account = new AccountProfile { Id = "account-" + id, Username = "anonymous-" + id, UserId = "user-" + id };
        var connection = new EmbyConnection(address, "placeholder-old", account.UserId, account.Username, server.Name, Session.Device)
        {
            ServerKind = kind,
            ServerId = server.Id,
            ServerVersion = kind == MediaServerKind.Jellyfin ? "12.2.0" : "1.0.0",
            IsAdministrator = administrator
        };
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null, [new EmbyClient(_http, connection), server, account], null)!;
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
        _sources.Add(scope.Key, scope);
        Transport.Servers.Add(address.Host, new AnonymousServer(id, kind, administrator == true));
        return scope;
    }

    internal object Identity(EmbySessionScope scope) => typeof(EmbySessionScope).GetProperty("IdentityState", Members)!.GetValue(scope)!;
    internal EmbyClient Client(EmbySessionScope scope) => (EmbyClient)Identity(scope).GetType().GetProperty("Client")!.GetValue(Identity(scope))!;
    internal CancellationToken Lifetime(EmbySessionScope scope) => (CancellationToken)typeof(EmbySessionScope).GetProperty("Lifetime", Members)!.GetValue(scope)!;
    internal long Revision(EmbySessionScope scope) => (long?)typeof(EmbySessionScope).GetProperty("PermissionRevision")?.GetValue(scope) ?? 0;
    internal bool? Permission(EmbySessionScope scope) => typeof(EmbySessionScope).GetProperty("IsAdministrator") is { } property
        ? (bool?)property.GetValue(scope) : scope.Connection.IsAdministrator;
    internal AnonymousServer Server(EmbySessionScope scope) => Transport.Servers[scope.Connection.ApiBase.Host];

    internal void Select(EmbySessionScope scope, bool notify = false)
    {
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, scope);
        if (notify) Invoke(Session, "NotifySourcesChanged");
    }

    internal void Revoke(EmbySessionScope scope, bool notify = false)
    {
        _sources.Remove(scope.Key);
        ((CancellationTokenSource)Identity(scope).GetType().GetProperty("Lifetime")!.GetValue(Identity(scope))!).Cancel();
        if (notify) Invoke(Session, "NotifySourcesChanged");
    }

    /// <summary>
    /// Baseline installs the exact old/new anonymous client as allowed by the task. With the repair,
    /// execute the real credential-free replacement and notification seam under its ownership gate.
    /// The whole authentication method is never called, even on the baseline.
    /// </summary>
    internal void Replace(EmbySessionScope scope, bool? administrator, bool updateServer = true, string marker = "new")
    {
        var connection = Client(scope).Connection with { IsAdministrator = administrator, AccessToken = "placeholder-" + marker };
        ReplaceConnection(scope, connection, updateServer);
    }

    internal void ReplaceConnection(EmbySessionScope scope, EmbyConnection connection, bool updateServer = false)
    {
        var method = typeof(EmbySession).GetMethod("ReplaceClient", Members);
        var changed = false;
        var lifecycle = typeof(EmbySession).GetField("_lifecycleGate", Members)!.GetValue(Session)!;
        lock (lifecycle)
        {
            if (method is null)
            {
                var identity = Identity(scope);
                identity.GetType().GetProperty("Client")!.SetValue(identity, new EmbyClient(_http, connection));
            }
            else changed = Invoke(Session, "ReplaceClient", scope, connection) is EmbyClient;
        }
        if (updateServer) Server(scope).Administrator = connection.IsAdministrator == true;
        if (changed) Invoke(Session, "NotifyPermissionsChanged", scope);
    }

    internal IDisposable WatchPermissions(Action<EmbySessionScope> changed)
    {
        var eventInfo = typeof(EmbySession).GetEvent("PermissionsChanged");
        if (eventInfo is null) return new Subscription(() => { });
        EventHandler<EmbySessionScope> handler = (_, scope) => changed(scope);
        eventInfo.AddEventHandler(Session, handler);
        var subscription = new Subscription(() => eventInfo.RemoveEventHandler(Session, handler));
        _subscriptions.Add(subscription);
        return subscription;
    }

    internal static object? Invoke(object target, string method, params object?[] args)
    {
        var member = target.GetType().GetMethod(method, Members) ?? throw new AssertionException("Missing real method: " + method);
        try { return member.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions) subscription.Dispose();
        Session.Dispose();
        Assert.Equal(0, _secrets.Calls, "任何凭据保护器调用都不属于本专项");
        Assert.Equal(0, Transport.Rejected, "未登记／认证请求即失败，不能吞掉当绿");
        Assert.False(Directory.Exists(_forbiddenSettings), "禁止设置 IO");
        Assert.Equal(0, SignOutNotifications, "权限刷新不得退出身份");
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class DenySecrets : ISecretProtector
    {
        internal int Calls { get; private set; }
        public string Protect(string plainText) { Calls++; throw new AssertionException("禁止凭据写入"); }
        public string Unprotect(string cipherText) { Calls++; throw new AssertionException("禁止凭据读取"); }
    }
}

internal sealed class AnonymousServer(string id, MediaServerKind kind, bool administrator)
{
    internal string Id { get; } = id;
    internal MediaServerKind Kind { get; } = kind;
    internal bool Administrator { get; set; } = administrator;
    internal Dictionary<string, ReplyGate> Gates { get; } = new(StringComparer.Ordinal);
    internal string PluginName { get; set; } = "fixture-plugin";
}

internal sealed record AnonymousRequest(string ServerId, string Path, string Method);

/// <summary>No real HTTP fallback exists. Only anonymous capability/admin read and explicitly approved fake writes.</summary>
internal sealed class PermissionTransport : HttpMessageHandler
{
    internal Dictionary<string, AnonymousServer> Servers { get; } = new(StringComparer.Ordinal);
    internal ConcurrentQueue<AnonymousRequest> Requests { get; } = new();
    internal ConcurrentQueue<AnonymousRequest> Writes { get; } = new();
    internal int Rejected { get; private set; }
    internal bool RestrictEmbySystem { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw Reject("Missing request URI");
        if (!Servers.TryGetValue(uri.Host, out var server) || uri.Scheme != "https" || !uri.IsDefaultPort)
            throw Reject("Non-anonymous source");
        var prefix = server.Kind == MediaServerKind.FnNas ? "/v/api/v1/" : server.Kind == MediaServerKind.Emby ? "/emby/" : "/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) throw Reject("Unexpected protocol prefix");
        var path = uri.AbsolutePath[prefix.Length..];
        var key = request.Method.Method + " " + path;
        if (RestrictEmbySystem && server.Kind == MediaServerKind.Emby && key == "GET System/Info")
            return new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden) { Content = new StringContent("fixture permission refusal") };
        var entry = new AnonymousRequest(server.Id, path, request.Method.Method);
        Requests.Enqueue(entry);
        JsonNode data;
        if (server.Kind == MediaServerKind.FnNas)
        {
            data = key switch
            {
                "GET user/info" => new JsonObject { ["guid"] = "user-" + server.Id, ["username"] = "anonymous", ["is_admin"] = server.Administrator ? 1 : 0 },
                "GET mdb/list" => new JsonArray(
                    new JsonObject { ["guid"] = "library-" + server.Id, ["name"] = "fixture library", ["category"] = "Movie", ["dir_list"] = new JsonArray("/fixture") },
                    new JsonObject { ["guid"] = "library-2-" + server.Id, ["name"] = "second library", ["category"] = "Movie" }),
                "GET manager/template/permission" => new JsonObject { ["media_permission"] = 2, ["mediadb_list"] = new JsonArray(), ["unknown"] = "kept" },
                "GET manager/user/list" => new JsonArray(new JsonObject { ["guid"] = "guest-" + server.Id, ["username"] = "guest", ["is_admin"] = 0, ["media_permission"] = 2 }),
                "GET server/info" => new JsonObject { ["name"] = "fixture server", ["lan"] = "any", ["supported_languages"] = new JsonArray("any", "chs"), ["file_monitor"] = 1 },
                "GET server/allVols" => new JsonObject { ["vols"] = new JsonArray() },
                "GET search/indexStatus" => new JsonObject { ["rebuilding"] = 0 },
                "POST task/schedule/getSetting" => new JsonObject { ["task_switch"] = "1" },
                "GET task/schedule/list" => new JsonArray(new JsonObject { ["guid"] = "task-" + server.Id, ["name"] = "fixture task" }),
                "POST task/schedule/set" or "POST manager/template/permission" or "POST server/info" => ApproveWrite(entry, request),
                _ when key == "GET mdb/library-" + server.Id => new JsonObject
                {
                    ["guid"] = "library-" + server.Id,
                    ["name"] = "fixture library",
                    ["category"] = "Movie",
                    ["dir_list"] = new JsonArray("/fixture"),
                    ["skip_filesize"] = 3
                },
                _ when key == "POST mdb/library-" + server.Id => ApproveWrite(entry, request),
                _ => throw Reject("Forbidden or unregistered endpoint: " + key)
            };
        }
        else
        {
            if (request.Method != HttpMethod.Get) throw Reject("Only capability GET is allowed for Emby/Jellyfin");
            data = path switch
            {
                "Users/Me" or "Users/user-" + "a" or "Users/user-" + "b" => new JsonObject
                {
                    ["Id"] = "user-" + server.Id,
                    ["Name"] = "anonymous",
                    ["Policy"] = new JsonObject { ["IsAdministrator"] = server.Administrator }
                },
                "System/Info" or "System/Info/Public" => new JsonObject { ["Version"] = "1.0.0", ["ServerName"] = "anonymous" },
                "web/ConfigurationPages" => new JsonArray(new JsonObject { ["Name"] = server.PluginName, ["DisplayName"] = server.PluginName, ["EnableInMainMenu"] = true }),
                _ => throw Reject("Forbidden or unregistered capability endpoint: " + path)
            };
        }
        if (server.Gates.TryGetValue(key, out var gate)) await gate.WaitAsync();
        var result = server.Kind == MediaServerKind.FnNas
            ? new JsonObject { ["code"] = 0, ["data"] = data } : data;
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(result.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }

    private JsonNode ApproveWrite(AnonymousRequest request, HttpRequestMessage message)
    {
        if (message.Content is null) throw Reject("Fake write missing its body");
        Writes.Enqueue(request);
        return new JsonObject();
    }

    private AssertionException Reject(string reason)
    {
        Rejected++;
        return new AssertionException(reason);
    }
}

internal sealed class ReplyGate : IDisposable
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async Task WaitAsync()
    {
        Entered.TrySetResult();
        await _release.Task;
    }
    internal void Release() => _release.TrySetResult();
    public void Dispose() => Release();
}

/// <summary>Deterministic framework queue; no HWND, native dispatcher or real UI is involved.</summary>
internal sealed class PermissionQueue : SynchronizationContext
{
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly List<Task> _async = [];
    internal int Posts { get; private set; }
    public override void Post(SendOrPostCallback callback, object? state)
    {
        Posts++;
        _pending.Enqueue(() => callback(state));
    }
    internal bool TryEnqueue(Action action) { _pending.Enqueue(action); return true; }
    internal bool TryEnqueue(Func<Task> action)
    {
        _pending.Enqueue(() => _async.Add(action()));
        return true;
    }
    internal void InContext(Action action)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try { action(); }
        finally { SetSynchronizationContext(previous); }
    }
    internal void Drain()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            while (_pending.TryDequeue(out var action)) InContext(action);
            var ready = _async.Where(task => task.IsCompleted).ToArray();
            foreach (var task in ready) { _async.Remove(task); if (task.Exception is { } error) throw error; if (task.IsCanceled) throw new OperationCanceledException("Fixture callback cancelled"); }
            if (_pending.IsEmpty && _async.Count == 0) return;
            if (DateTime.UtcNow >= deadline) throw new AssertionException("Anonymous UI queue did not settle");
            Thread.Sleep(1);
        }
    }
}

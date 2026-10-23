using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Tests;

internal sealed class AnonymousSource : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly ForbiddenSecrets _secrets = new();
    private readonly ForbiddenHttp _http = new();
    private readonly DeviceIdentity _device = new("r019", "r019", "r019", "1");
    private readonly List<CancellationTokenSource> _lifetimes = [];
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal int ForbiddenCalls => _secrets.Calls + _http.Calls;

    internal AnonymousSource(MediaServerKind kind)
    {
        // Constructors only: no Load/Save/Restore/SignIn, real user directory, token or account IO.
        var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "r019-no-settings|io-forbidden"));
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [new AppSettings(), new SettingsStore(paths, _secrets), new CredentialVault(_secrets), _device, _http], null)!;
        Scope = Add("original", kind);
        SetDefault(Scope);
    }

    private EmbySessionScope Add(string name, MediaServerKind kind)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("http://r019-" + name + ".invalid/emby/"), "",
            "anonymous", "anonymous", "offline", _device)
        { ServerKind = kind };
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = name }, new AccountProfile { Id = name }], null)!;
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null,
            [Session, identity], null)!;
        _lifetimes.Add((CancellationTokenSource)identityType.GetProperty("Lifetime")!.GetValue(identity)!);
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
        sources.Add(scope.Key, scope);
        return scope;
    }

    private void SetDefault(EmbySessionScope source) =>
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, source);

    internal void SwitchDefault() => SetDefault(Add("other", Scope.Connection.ServerKind));
    internal void Revoke() => _lifetimes[0].Cancel();

    public void Dispose()
    {
        Session.Dispose();
        Assert.Equal(0, ForbiddenCalls, "夹具不能触及网络或凭据");
    }

    private sealed class ForbiddenHttp : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("R-019 forbids all HTTP, even anonymous fixture URLs");
        }
    }

    private sealed class ForbiddenSecrets : ISecretProtector
    {
        internal int Calls { get; private set; }
        public string Protect(string value) { Calls++; throw new InvalidOperationException("No credential IO"); }
        public string Unprotect(string value) { Calls++; throw new InvalidOperationException("No credential IO"); }
    }
}

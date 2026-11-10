using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Tests;

/// <summary>纯身份比较用真实会话内存来源；任何网络、凭据或设置读取都会失败。</summary>
internal sealed class AnonymousScopeFixture : IDisposable
{
    private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly EmbySession _session;

    internal AnonymousScopeFixture(EmbyConnection connection, ServerProfile server, AccountProfile account)
    {
        var noSecrets = new NoSecrets();
        var path = Path.Combine(Path.GetTempPath(), "anonymous-scope|IO-forbidden");
        _session = new EmbySession(new AppSettings(), new SettingsStore(new AppPaths(path), noSecrets),
            new CredentialVault(noSecrets), connection.Device, new RejectRequests());
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
        Scope = new EmbySessionScope(_session, new EmbySessionScope.Identity(new EmbyClient(http, connection), server, account));
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(_session)!;
        sources.Add(Scope.Key, Scope);
        typeof(EmbySession).GetField("_current", Members)!.SetValue(_session, Scope);
    }

    internal EmbySessionScope Scope { get; }

    // These existing tests compare keys/names only. A disposed real owner preserves identity but
    // explicitly has no online permissions, instead of constructing a production scope with null.
    internal static EmbySessionScope DetachedIdentity(EmbyConnection connection, ServerProfile server, AccountProfile account)
    {
        using var fixture = new AnonymousScopeFixture(connection, server, account);
        return fixture.Scope;
    }
    public void Dispose() => _session.Dispose();

    private sealed class RejectRequests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("身份比较夹具不允许HTTP");
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("身份比较夹具不允许凭据写入");
        public string Unprotect(string cipherText) => throw new AssertionException("身份比较夹具不允许凭据读取");
    }
}

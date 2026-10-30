using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Tests;

internal sealed class StoreFixture : IAsyncDisposable
{
    internal string Root { get; } = Path.Combine(ProductionPins.Root, "work", "perf-images", "fixtures", Guid.NewGuid().ToString("N"));
    internal ImageHandler Handler { get; } = new();
    internal EmbySession Session { get; }
    internal EmbySessionScope Scope { get; }
    internal EmbyImageStore Images { get; }
    internal SharedWork<byte[]?> Downloads { get; }

    internal StoreFixture()
    {
        Directory.CreateDirectory(Root);
        var protector = new NoSecrets();
        var device = new DeviceIdentity("perf-fixture", "perf-fixture", "perf-fixture", "1");
        Session = new EmbySession(new AppSettings(), new SettingsStore(new AppPaths(Root), protector),
            new CredentialVault(protector), device, Handler);
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("http://perf-images.invalid/emby/"), "", "anonymous", "fixture", "fixture", device);
        var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection),
            new ServerProfile { Id = "fixture-server" }, new AccountProfile { Id = "fixture-account" });
        Scope = new EmbySessionScope(Session, identity);
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession)
            .GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
        sources.Add(Scope.Key, Scope);
        Images = new EmbyImageStore(Session, Path.Combine(Root, "images"));
        Downloads = (SharedWork<byte[]?>)typeof(EmbyImageStore)
            .GetField("_downloads", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Images)!;
    }

    internal EmbyItem Item(string id) => Scope.Bind(new EmbyItem
    {
        Id = id, Name = "anonymous-fixture", Type = EmbyItemType.Movie,
        BackdropImageTags = ["tag"]
    });

    internal Task<byte[]?> Get(string id, CancellationToken token = default) =>
        Images.GetAsync(Scope, id, EmbyImageStore.Backdrop, "tag", 1280, token);

    public async ValueTask DisposeAsync()
    {
        Scope.IdentityState.Lifetime.Cancel();
        await Program.Eventually(() => Downloads.Running == 0, "夹具结束共享工作仍在途");
        Session.Dispose();
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("不应读写凭据");
        public string Unprotect(string cipherText) => throw new AssertionException("不应读写凭据");
    }
}

internal sealed class ImageHandler : HttpMessageHandler
{
    internal Func<string, int, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _, _) => Task.FromResult(Bytes([1, 2, 3]));
    internal ConcurrentDictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);
    internal ConcurrentQueue<string> Order { get; } = new();
    internal int Active;
    internal int PeakActive;
    internal int Total => Calls.Values.Sum();

    internal static HttpResponseMessage Bytes(byte[] bytes, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new ByteArrayContent(bytes) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri?.Host != "perf-images.invalid") throw new AssertionException("非匿名夹具地址");
        if (request.Method != HttpMethod.Get) throw new AssertionException("图片夹具禁止写请求");
        var parts = request.RequestUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var items = Array.IndexOf(parts, "Items");
        if (items < 0 || items + 1 >= parts.Length || !parts.Contains("Images")) throw new AssertionException("不是图片GET");
        var id = parts[items + 1];
        var count = Calls.AddOrUpdate(id, 1, (_, current) => current + 1);
        Order.Enqueue(id);
        var active = Interlocked.Increment(ref Active);
        int prior;
        do { prior = Volatile.Read(ref PeakActive); }
        while (active > prior && Interlocked.CompareExchange(ref PeakActive, active, prior) != prior);
        try { return await Respond(id, count, token).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref Active); }
    }
}

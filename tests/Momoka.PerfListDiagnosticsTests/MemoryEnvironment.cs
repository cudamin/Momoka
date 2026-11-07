using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.ViewModels;

namespace Momoka.Tests;

internal sealed class MemoryEnvironment : IDisposable
{
    private readonly EmbySession _session;
    private readonly EmbyHttp _http;
    private readonly DeviceIdentity _device = new("fixture", "fixture", "fixture", "0");
    private readonly Dictionary<string, EmbySessionScope> _sources;

    internal MemoryEnvironment()
    {
        var settings = Settings;
        var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "unused-perf-path"));
        var protector = PassthroughSecretProtector.Instance;
        _session = new EmbySession(settings, new SettingsStore(paths, protector), new CredentialVault(protector),
            _device, new NoNetwork());
        _http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
        _sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession)
            .GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
        Images = new EmbyImageStore(_session, Path.Combine(paths.Root, "unused-images"));
    }

    internal EmbyImageStore Images { get; }
    internal AppSettings Settings { get; } = new();
    internal EmbySession Session => _session;
    internal EmbySessionScope Source(string server, string account)
    {
        var connection = new EmbyConnection(new Uri("http://perf.invalid/emby/"), "", "fixture", "fixture", "fixture", _device);
        var identity = new EmbySessionScope.Identity(new EmbyClient(_http, connection),
            new ServerProfile { Id = server, Name = server }, new AccountProfile { Id = account });
        var scope = new EmbySessionScope(_session, identity);
        _sources.Add(scope.Key, scope);
        return scope;
    }
    internal void Revoke(EmbySessionScope scope) => _sources.Remove(scope.Key);
    public void Dispose() => _session.Dispose();
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new AssertionException("PERF 测试禁止任何 HTTP、登录或图片读取");
    }
}

internal sealed class SingleThreadPump : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
    internal static void Run(Func<Task> test)
    {
        var previous = Current;
        using var pump = new SingleThreadPump();
        SetSynchronizationContext(pump);
        try
        {
            var task = test().WaitAsync(TimeSpan.FromSeconds(20));
            while (!task.IsCompleted)
                if (pump._queue.TryTake(out var work, 30)) work.Callback(work.State);
            while (pump._queue.TryTake(out var work)) work.Callback(work.State);
            if (task.IsCanceled) throw new TaskCanceledException(task);
            if (task.Exception is { } error) ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
        }
        finally { SetSynchronizationContext(previous); }
    }
    public void Dispose() => _queue.Dispose();
}

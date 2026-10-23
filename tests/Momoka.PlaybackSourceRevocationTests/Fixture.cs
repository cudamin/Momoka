using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Tests;

internal sealed class Fixture : IAsyncDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<string, EmbySessionScope> _sources;
    private readonly List<CancellationTokenSource> _identities = [];
    private readonly List<Task<PlaybackResult>> _runs = [];
    private readonly List<FakeHandle> _handles = [];
    private readonly List<TaskCompletionSource> _holds = [];
    private readonly NoSecrets _secrets = new();
    public AppSettings Settings { get; } = new() { AggregateMode = true };
    public ReportTransport Transport { get; } = new();
    public FakeBackend Backend { get; } = new();
    public RecordingSink LogSink { get; } = new();
    public EmbySession Session { get; }
    public PlaybackService Service { get; }
    public EmbySessionScope A { get; }
    public EmbySessionScope B { get; }
    public EmbySessionScope C { get; }

    public Fixture()
    {
        Settings.Playback.ReportProgressToServer = true;
        Settings.Playback.ProgressReportIntervalSeconds = 60;
        Settings.Playback.StopReportEnabled = false;
        Log.UseSink(LogSink);
        var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
        // The session constructor only retains these owners. A null store refuses persistence before
        // any storage IO; all identity installation below is in memory, never SignIn/SelectSource.
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Members, null,
            [Settings, null, new CredentialVault(_secrets), device, Transport], null)!;
        _sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Members)!.GetValue(Session)!;
        A = Install("a");
        B = Install("b");
        C = Install("c");
        Default(A);
        Service = new PlaybackService(Session, Settings, () => Backend,
            new PlaybackPlanner(Settings, new ShaderGroupResolver(Settings.Shaders)));
    }

    public EmbySessionScope Install(string name)
    {
        var account = new AccountProfile { Id = name + "-account", Username = name + "-user" };
        var server = new ServerProfile { Id = name, Name = name, Url = "https://" + name + ".invalid", Accounts = [account] };
        var connection = new EmbyConnection(new Uri("https://" + name + ".invalid/emby/"),
            "placeholder-only", account.Username, account.Username, name, Session.Device);
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(Session)!;
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, Members, null,
            [new EmbyClient(http, connection), server, account], null)!;
        var lifetime = (CancellationTokenSource)identityType.GetProperty("Lifetime", Members)!.GetValue(identity)!;
        _identities.Add(lifetime);
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Members, null, [Session, identity], null)!;
        _sources[scope.Key] = scope;
        return scope;
    }

    public static CancellationTokenSource IdentityLifetime(EmbySessionScope scope)
    {
        var identity = typeof(EmbySessionScope).GetProperty("IdentityState", Members)!.GetValue(scope)!;
        return (CancellationTokenSource)identity.GetType().GetProperty("Lifetime", Members)!.GetValue(identity)!;
    }

    // Exercise the last, actual SignOut boundary only. Do not call EndSource/Persist or erase sources.
    public static void Revoke(EmbySessionScope scope) => IdentityLifetime(scope).Cancel();

    public void Default(EmbySessionScope scope)
    {
        typeof(EmbySession).GetField("_current", Members)!.SetValue(Session, scope);
        NotifySourcesChanged();
    }

    public void NotifySourcesChanged() => typeof(EmbySession).GetMethod("NotifySourcesChanged", Members)!.Invoke(Session, null);

    public FakeHandle Handle(bool swappable = false, bool controlled = true, bool? pictureInHostWindow = true,
        bool honorWaitCancellation = true)
    {
        var handle = new FakeHandle
        {
            Swappable = swappable,
            HasControlChannel = controlled,
            PictureInHostWindow = pictureInHostWindow,
            HonorWaitCancellation = honorWaitCancellation
        };
        _handles.Add(handle);
        return handle;
    }

    public TaskCompletionSource Hold()
    {
        var hold = Signals.New();
        _holds.Add(hold);
        return hold;
    }

    public PlaybackTicket Ticket(EmbySessionScope scope, string id = "movie", long startTicks = 0)
    {
        var source = new MediaSource { Id = "version-1", Name = "anonymous version", Container = "mkv" };
        return new PlaybackTicket
        {
            Item = scope.Bind(new EmbyItem
            {
                Id = id,
                Name = id,
                Type = EmbyItemType.Movie,
                RunTimeTicks = TimeSpan.FromSeconds(100).Ticks,
                MediaSources = [source]
            }),
            Source = source,
            StartTicks = startTicks,
            ExactStart = true
        };
    }

    public Task<PlaybackResult> Play(EmbySessionScope scope, string id = "movie", CancellationToken token = default,
        CancellationToken startup = default, bool explicitScope = true, long startTicks = 0)
    {
        var ticket = Ticket(scope, id, startTicks);
        return Play(ticket, explicitScope ? scope : null, token, startup);
    }

    public Task<PlaybackResult> Play(PlaybackTicket ticket, EmbySessionScope? scope, CancellationToken token = default,
        CancellationToken startup = default)
    {
        var task = Service.PlayAsync(ticket, scope, token, startup);
        _runs.Add(task);
        return task;
    }

    public int Finishes(string id) => LogSink.Entries.Count(entry => entry.Category == "playback"
        && entry.Level == LogLevel.Info && entry.Message.StartsWith("《" + id + "》", StringComparison.Ordinal)
        && entry.Message.Contains("，位置 ", StringComparison.Ordinal));

    public void AssertSources(EmbySessionScope revoked)
    {
        Assert.False(revoked.IsAvailable);
        Assert.True(B.IsAvailable && C.IsAvailable, "不撤销其它在线来源");
        Assert.Equal(3, Session.Sources.Count, "夹具只模拟取消最后一步，来源集合不得被播放服务改动");
        Assert.Equal(0, _secrets.Calls);
        Assert.Equal(0, Transport.UnexpectedRequests);
    }

    public async Task NextSucceeds(EmbySessionScope? source = null)
    {
        var next = Handle();
        Backend.Next = (_, _) => Task.FromResult<IPlaybackHandle>(next);
        var run = Play(source ?? B, "follow-up");
        await Signals.Await(next.Listening(0), "后续来源进入监视");
        Assert.False(next.RunningTokens.Single().IsCancellationRequested, "旧撤销不能传给新 operation");
        next.End(PlaybackEndReason.UserQuit, 5);
        var result = await Signals.Await(run, "后续播放完成");
        Assert.Equal(PlaybackEndReason.UserQuit, result.Exit.Reason);
        Assert.Equal(1, next.DisposeCount);
        Assert.Equal(1, Finishes("follow-up"));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hold in _holds) hold.TrySetResult();
        foreach (var handle in _handles) handle.ReleaseHolds();
        try
        {
            await Signals.Await(Service.StopAsync(), "夹具停止全部 operation");
            foreach (var run in _runs)
            {
                try { await Signals.Await(run, "夹具等候播放完成"); }
                catch (OperationCanceledException) { }
            }
        }
        finally
        {
            Session.Dispose();
            foreach (var identity in _identities) identity.Dispose();
            Log.UseSink(NullLogSink.Instance);
        }
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public int Calls { get; private set; }
        public string Protect(string value) { Calls++; throw new AssertionException("禁止保护凭据"); }
        public string Unprotect(string value) { Calls++; throw new AssertionException("禁止读凭据、DPAPI 或重新登录"); }
    }
}

internal sealed class RecordingSink : ILogSink
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();
    public void Write(in LogEntry entry) => Entries.Enqueue(entry);
}

internal sealed class ReportTransport : HttpMessageHandler
{
    internal sealed record Sent(string Host, string Path, string Body)
    {
        public bool IsStart => Path.EndsWith("/Sessions/Playing", StringComparison.Ordinal);
        public bool IsStop => Path.EndsWith("/Sessions/Playing/Stopped", StringComparison.Ordinal);
        public bool IsProgress => Path.EndsWith("/Sessions/Playing/Progress", StringComparison.Ordinal);
        public string ItemId
        {
            get
            {
                using var body = JsonDocument.Parse(Body);
                return body.RootElement.GetProperty("ItemId").GetString()!;
            }
        }
    }

    private int _unexpectedRequests;
    public int UnexpectedRequests => Volatile.Read(ref _unexpectedRequests);
    public ConcurrentQueue<Sent> Reports { get; } = new();
    public Func<Sent, CancellationToken, Task>? Reply { get; set; }
    public int Stops(string id) => Reports.Count(report => report.IsStop && report.ItemId == id);
    public int Starts(string id) => Reports.Count(report => report.IsStart && report.ItemId == id);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        var report = path is "/emby/Sessions/Playing" or "/emby/Sessions/Playing/Progress" or "/emby/Sessions/Playing/Stopped";
        var watched = path.StartsWith("/emby/Users/", StringComparison.Ordinal) && path.Contains("/PlayedItems/", StringComparison.Ordinal);
        if (request.Method != HttpMethod.Post || !uri.Host.EndsWith(".invalid", StringComparison.Ordinal) || !report && !watched)
        {
            Interlocked.Increment(ref _unexpectedRequests);
            throw new AssertionException("禁止未安排的请求、认证或真实网络");
        }
        if (report)
        {
            var sent = new Sent(uri.Host, path, await request.Content!.ReadAsStringAsync(cancellationToken));
            Reports.Enqueue(sent);
            if (Reply is { } reply) await reply(sent, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}

internal static class Signals
{
    public static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static async Task Await(Task task, string stage)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { throw new AssertionException("未完成：" + stage); }
    }
    public static async Task<T> Await<T>(Task<T> task, string stage)
    {
        try { return await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { throw new AssertionException("未完成：" + stage); }
    }
    public static async Task Cancelled(Task task)
    {
        try { await Await(task, "取消旧准备任务"); }
        catch (OperationCanceledException) { return; }
        throw new AssertionException("旧准备任务必须取消，不得宣布起播或转投默认来源");
    }
}

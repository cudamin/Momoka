using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;

namespace Momoka.Tests;

// Real PlaybackService, EmbySession and EmbyClient; install anonymous identities in memory only.
// No login/restore/store calls, DPAPI, real account, native backend startup, or network transport.
internal sealed class ServiceFixture : IAsyncDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly EmbySession _session;
    private readonly Type _identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
    private readonly List<object> _identities = [];
    private readonly EmbySessionScope _default;
    private readonly EmbySessionScope _otherDefault;
    internal HandleFixture Backend { get; } = new();
    internal AppSettings Settings { get; } = new();
    internal ReportBoundary Transport { get; } = new();
    internal PlaybackService Service { get; }
    internal EmbySessionScope Target { get; }
    internal Task<PlaybackResult>? Play { get; private set; }
    internal List<EmbyItem?> NowPlaying { get; } = [];
    internal EmbyItem Item { get; }
    internal PlaybackTicket Ticket { get; }
    internal int BackendStarts { get; private set; }

    internal ServiceFixture(MediaServerKind targetKind = MediaServerKind.Emby)
    {
        Settings.Playback.ReportProgressToServer = true;
        Settings.Playback.StopReportEnabled = true;
        Settings.Playback.ProgressReportIntervalSeconds = 60;
        Settings.Playback.AutoPlayNextEpisode = true;
        Settings.Playback.ResumeRewindSeconds = 0;
        var secrets = new NoSecrets();
        var paths = new AppPaths(Path.Combine(AppContext.BaseDirectory, "r048-no-config|no-secrets"));
        var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
        _session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
            [Settings, new SettingsStore(paths, secrets), new CredentialVault(secrets), device, Transport], null)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(_session)!;
        Target = Install("target", targetKind, http, device);
        _default = Install("default", MediaServerKind.Jellyfin, http, device);
        _otherDefault = Install("other", MediaServerKind.Jellyfin, http, device);
        Default(_default);
        Item = Target.Bind(new EmbyItem
        {
            Id = "episode",
            Type = EmbyItemType.Episode,
            Name = "R048 anonymous",
            SeasonId = "season",
            RunTimeTicks = TimeSpan.TicksPerSecond * 100
        });
        var source = new MediaSource { Id = "source", Container = "mkv", RunTimeTicks = Item.RunTimeTicks };
        Ticket = new PlaybackTicket { Item = Item, Source = source };
        Service = new PlaybackService(_session, Settings, () => new BackendBoundary(this),
            new PlaybackPlanner(Settings, new ShaderGroupResolver(Settings.Shaders)));
        Service.NowPlayingChanged += NowPlaying.Add;
    }

    private EmbySessionScope Install(string name, MediaServerKind kind, EmbyHttp http, DeviceIdentity device)
    {
        var connection = new EmbyConnection(new Uri("https://r048-" + name + ".invalid/" +
            (kind == MediaServerKind.Emby ? "emby/" : "")), "placeholder-only", name + "-user", name + "-user", name, device)
        { ServerKind = kind };
        var identity = Activator.CreateInstance(_identityType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [new EmbyClient(http, connection), new ServerProfile { Id = name, ServerKind = kind }, new AccountProfile { Id = name }], null)!;
        _identities.Add(identity);
        var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [_session, identity], null)!;
        var sources = (IDictionary)typeof(EmbySession).GetField("_sources", Private)!.GetValue(_session)!;
        sources.Add(scope.Key, scope);
        return scope;
    }

    private void Default(EmbySessionScope scope) => typeof(EmbySession).GetField("_current", Private)!.SetValue(_session, scope);
    internal void ChangeDefault() => Default(_otherDefault);

    internal void RevokeTarget()
    {
        var identity = typeof(EmbySessionScope).GetProperty("IdentityState", Private)!.GetValue(Target)!;
        ((CancellationTokenSource)_identityType.GetProperty("Lifetime")!.GetValue(identity)!).Cancel();
    }

    internal async Task StartAsync()
    {
        Backend.Ready(10);
        Play = Service.PlayAsync(Ticket, CancellationToken.None);
        await Transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(Service.IsPlaying);
        Assert.Equal(1, BackendStarts);
    }

    public async ValueTask DisposeAsync()
    {
        if (Play is { IsCompleted: false }) Backend.Dispatch(Momoka.Mpv.LibMpvNative.EventShutdown);
        if (Play is not null) await Play.WaitAsync(TimeSpan.FromSeconds(3));
        await Service.StopAsync();
        await Backend.DisposeAsync();
        _session.Dispose();
        foreach (var identity in _identities)
        {
            ((SemaphoreSlim)_identityType.GetProperty("Reauthentication")!.GetValue(identity)!).Dispose();
            ((SemaphoreSlim)_identityType.GetProperty("PasswordChanges")!.GetValue(identity)!).Dispose();
        }
    }

    private sealed class BackendBoundary(ServiceFixture owner) : IPlaybackBackend
    {
        public string DisplayName => "R048 inert source-linked handle";
        public string? Validate() => null;
        public Task<IPlaybackHandle> StartAsync(PlaybackRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            owner.BackendStarts++;
            return Task.FromResult<IPlaybackHandle>(owner.Backend.Handle);
        }
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string text) => throw new AssertionException("R048 不保护或保存凭据");
        public string Unprotect(string text) => throw new AssertionException("R048 不读 DPAPI/凭据或登录");
    }
}

internal sealed class ReportBoundary : HttpMessageHandler
{
    private static long _totalSent;
    internal static long TotalSent => Interlocked.Read(ref _totalSent);
    internal sealed record Sent(string Host, string Path, string Body)
    {
        internal bool IsStart => Path.EndsWith("/Sessions/Playing", StringComparison.Ordinal);
        internal bool IsStop => Path.EndsWith("/Sessions/Playing/Stopped", StringComparison.Ordinal);
        internal bool IsProgress => Path.EndsWith("/Sessions/Playing/Progress", StringComparison.Ordinal);
        internal bool IsWatched => Path.Contains("/PlayedItems/", StringComparison.Ordinal)
            || Path.Contains("/UserPlayedItems/", StringComparison.Ordinal);
        internal JsonElement Field(string name)
        {
            using var json = JsonDocument.Parse(Body);
            return json.RootElement.GetProperty(name).Clone();
        }
    }
    internal ConcurrentQueue<Sent> Requests { get; } = new();
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Progressed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new AssertionException("缺少匿名请求 URI");
        if (!uri.Host.StartsWith("r048-", StringComparison.Ordinal) || !uri.Host.EndsWith(".invalid", StringComparison.Ordinal))
            throw new AssertionException("R048 拒绝非匿名目标");
        var sent = new Sent(uri.Host, uri.AbsolutePath,
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        if (!sent.IsStart && !sent.IsStop && !sent.IsProgress && !sent.IsWatched)
            throw new AssertionException("R048 未安排的假 HTTP 操作：" + uri.AbsolutePath);
        Requests.Enqueue(sent);
        Interlocked.Increment(ref _totalSent);
        if (sent.IsStart) Started.TrySetResult();
        if (sent.IsProgress) Progressed.TrySetResult();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}

using System.Collections;
using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

// The real service start/HandOver/Swap/attempt-generation/Stop path, with one anonymous identity.
// No identity restore, login, authentication retry, actual HTTP, credential IO, or native Start.
internal sealed class ServiceFixture : IAsyncDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly EmbySession _session;
    private readonly object _identity;
    private readonly Type _identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
    private readonly List<Task<PlaybackResult>> _plays = [];
    private readonly List<HandleFixture> _handles = [];
    internal HandleFixture NativeHandle { get; private set; }
    internal PausingHandle Handle { get; private set; }
    internal PlaybackService Service { get; }
    internal AppSettings Settings { get; } = new();
    internal EmbySessionScope Scope { get; }
    internal int BackendStarts { get; private set; }
    internal List<string?> Subjects { get; } = [];
    internal DenyHttp Http { get; } = new();

    internal ServiceFixture()
    {
        Settings.Playback.ReportProgressToServer = false;
        Settings.Playback.StopReportEnabled = false;
        Settings.Audio.Device = "";
        Settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        var secrets = new DenySecrets();
        var device = new DeviceIdentity("r051", "r051", "r051", "0");
        _session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
            [Settings, new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "r051-no-config|no-secrets")), secrets),
                new CredentialVault(secrets), device, Http], null)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(_session)!;
        var connection = new EmbyConnection(new Uri("https://r051.invalid/emby/"), "placeholder-only",
            "user", "user", "r051", device);
        _identity = Activator.CreateInstance(_identityType, Private | BindingFlags.Public, null,
            [new EmbyClient(http, connection), new ServerProfile { Id = "r051", ServerKind = MediaServerKind.Emby },
                new AccountProfile { Id = "r051" }], null)!;
        Scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Private | BindingFlags.Public,
            null, [_session, _identity], null)!;
        ((IDictionary)typeof(EmbySession).GetField("_sources", Private)!.GetValue(_session)!).Add(Scope.Key, Scope);
        typeof(EmbySession).GetField("_current", Private)!.SetValue(_session, Scope);
        NativeHandle = NewHandle();
        Handle = new PausingHandle(NativeHandle);
        Service = new PlaybackService(_session, Settings, () => new FixtureBackend(this),
            new PlaybackPlanner(Settings, new ShaderGroupResolver(Settings.Shaders)));
        Service.NowPlayingChanged += item => Subjects.Add(item?.Id);
    }

    private HandleFixture NewHandle()
    {
        var value = new HandleFixture();
        _handles.Add(value);
        return value;
    }

    private PlaybackTicket Ticket(string file)
    {
        var item = Scope.Bind(new EmbyItem
        {
            Id = file,
            Name = file,
            Type = EmbyItemType.Movie,
            RunTimeTicks = TimeSpan.TicksPerSecond * 100
        });
        return new PlaybackTicket
        {
            Item = item,
            Source = new MediaSource { Id = file + "-source", Container = "mkv", RunTimeTicks = item.RunTimeTicks }
        };
    }

    internal async Task StartAsync(string file = "A")
    {
        _plays.Add(Service.PlayAsync(Ticket(file), Scope, CancellationToken.None));
        await UntilAsync(() => Subjects.Contains(file, StringComparer.Ordinal));
        Assert.True(Service.IsPlaying);
    }

    internal async Task SwapAsync(string file = "B")
    {
        var generation = Service.Generation;
        var nativeGeneration = NativeHandle.NativeGeneration;
        await StartAsync(file);
        NativeHandle.Ready();
        Assert.Equal(generation + 1, Service.Generation, "真实生命周期推进 service 代次");
        Assert.Equal(nativeGeneration + 2, NativeHandle.NativeGeneration, "真实 HandOver 和 Swap 分别推进 native 代次");
        Assert.Equal(1, BackendStarts, "同句柄接班不得另起后端");
        Assert.Equal(file, NativeHandle.Native.File);
    }

    internal async Task ReplaceHandleAsync()
    {
        NativeHandle.Dispatch(LibMpvNative.EventShutdown);
        await _plays[^1].WaitAsync(TimeSpan.FromSeconds(5));
        NativeHandle = NewHandle();
        Handle = new PausingHandle(NativeHandle);
        await StartAsync("B");
        Assert.Equal(2, BackendStarts);
    }

    internal static async Task UntilAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(1, deadline.Token);
    }

    public async ValueTask DisposeAsync()
    {
        Handle.Pause?.Release();
        foreach (var handle in _handles) handle.Dispatch(LibMpvNative.EventShutdown);
        await Service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(_plays).WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var handle in _handles) await handle.DisposeAsync();
        Assert.Equal(0, Http.Calls, "R051 不发送 HTTP");
        _session.Dispose();
        ((SemaphoreSlim)_identityType.GetProperty("Reauthentication")!.GetValue(_identity)!).Dispose();
        ((SemaphoreSlim)_identityType.GetProperty("PasswordChanges")!.GetValue(_identity)!).Dispose();
    }

    private sealed class FixtureBackend(ServiceFixture owner) : IPlaybackBackend
    {
        public string DisplayName => "R051 managed-only real handle";
        public string? Validate() => null;
        public Task<IPlaybackHandle> StartAsync(PlaybackRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            owner.BackendStarts++;
            // This does NOT call LibMpvHandle.Start or initialize/load native media.
            return Task.FromResult<IPlaybackHandle>(owner.Handle);
        }
    }
}

internal sealed class DenyHttp : HttpMessageHandler
{
    internal int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        throw new AssertionException("R051 禁止所有 HTTP 请求");
    }
}

internal sealed class DenySecrets : ISecretProtector
{
    public string Protect(string text) => throw new AssertionException("R051 禁止凭据保存");
    public string Unprotect(string text) => throw new AssertionException("R051 禁止读取凭据/DPAPI/恢复登录");
}

using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ServiceBoundaryTests
{
    internal static void Register()
    {
        Test("R11-004 实际Service按键命令在执行点拒绝旧原生文件", () =>
        {
            var settings = new AppSettings();
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var secrets = new NoSecrets();
            using var session = new EmbySession(settings, new SettingsStore(new AppPaths(Path.Combine(AppContext.BaseDirectory, "no-config|forbidden")), secrets),
                new CredentialVault(secrets), device);
            var service = new PlaybackService(session, settings, () => throw new AssertionException("不能启动播放器"),
                new PlaybackPlanner(settings, new ShaderGroupResolver(settings.Shaders)), allowPlayback: false);
            var handle = new FileHandle();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var current = typeof(PlaybackService).GetField("_current", flags)!;
            current.SetValue(service, handle);
            var connection = new EmbyConnection(new Uri("https://fixture.invalid/"), "placeholder", "fixture", "fixture", "fixture", device);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", flags)!.GetValue(session)!;
            var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
            var identity = Activator.CreateInstance(identityType, flags, null,
                [new EmbyClient(http, connection), new ServerProfile { Id = "fixture" }, new AccountProfile { Id = "fixture" }], null)!;
            var scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), flags, null, [session, identity], null)!;
            var request = new PlaybackRequest { MediaUrl = new Uri("https://fixture.invalid/not-loaded"), Title = "fixture" };
            var attemptType = typeof(PlaybackService).GetNestedType("PlayingAttempt", BindingFlags.NonPublic)!;
            var tracksType = typeof(PlaybackService).Assembly.GetType("Momoka.Playback.PlaybackTrackState", true)!;
            var tracks = Activator.CreateInstance(tracksType, flags, null, [new MediaSource(), request], null);
            var attempt = Activator.CreateInstance(attemptType, flags, null, [handle, request, 0L, tracks, scope], null);
            typeof(PlaybackService).GetField("_active", flags)!.SetValue(service, attempt);
            var pending = service.CommandForActiveFileAsync(0, 5, "script-message-to", "stats", "momoka-scroll", "down");
            handle.FileGeneration++;
            handle.Release.SetResult();
            Complete(pending);
            Assert.False(pending.Result);
            Assert.Equal(0, handle.Writes);
            Assert.Equal(0, handle.OrdinaryWrites);
            Assert.True(Complete(service.CommandForActiveFileAsync(0, 6, "script-message-to", "stats", "momoka-scroll", "up")));
            Assert.Equal(1, handle.Writes);
            Assert.False(Complete(service.CommandForActiveFileAsync(1, 6, "set", "pause", "no")));
            current.SetValue(service, null);
        });
    }
    private static T Complete<T>(Task<T> task)
    {
        using var finished = new ManualResetEventSlim();
        _ = task.ContinueWith(_ => finished.Set(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)), "实际Service匿名命令未结束");
        if (task.Exception is { } error) throw error.InnerException!;
        return task.Result;
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string value) => throw new AssertionException("禁止凭据IO");
        public string Unprotect(string value) => throw new AssertionException("禁止凭据IO");
    }
    private sealed class FileHandle : IPlaybackHandle, IPlayerControl, IFileScopedPlayerControl
    {
        public long FileGeneration { get; set; } = 5;
        public TaskCompletionSource Release { get; } = new();
        public int Writes { get; private set; }
        public int OrdinaryWrites { get; private set; }
        public bool HasControlChannel => true;
        public bool IsPaused => false;
        public PlayerStatus Status => new();
        public event Action<bool>? PauseChanged { add { } remove { } }
        public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
        public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
        public async Task<bool> CommandForFileAsync(long expected, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            await Release.Task;
            if (expected != FileGeneration) return false;
            Writes++; return true;
        }
        public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) { OrdinaryWrites++; return Task.FromResult(true); }
        public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => Task.FromResult<double?>(0);
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken) => throw new AssertionException("禁止起播");
        public Task StopAsync() => Task.CompletedTask;
        public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShowMessageAsync(string text) => Task.CompletedTask;
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
        public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(null);
        public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

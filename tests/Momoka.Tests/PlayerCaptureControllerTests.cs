using System.Collections;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>Actual capture controller with an inert file tool seam and anonymous playback handle.</summary>
internal static class PlayerCaptureControllerTests
{
    internal static void Register(SettingsPageStateTests.ShellFixture fixture)
    {
        TestHarness.Test("截图队列：裁切成功才加入，拼图失败保留原队列并允许重试", () =>
        {
            using var state = new State(fixture);
            state.Save().GetAwaiter().GetResult();
            state.Save().GetAwaiter().GetResult();
            Assert.Equal(2, state.Count);
            Assert.Throws<Exception>(() => state.Save(width: 64).GetAwaiter().GetResult());
            state.Fail = true;
            Assert.Null(state.Stitch().GetAwaiter().GetResult());
            Assert.Equal(2, state.Count);
            state.Fail = false;
            Assert.NotNull(state.Stitch().GetAwaiter().GetResult());
            Assert.Equal(0, state.Count);
        });
        TestHarness.Test("截图队列：旧处理结果不进入新片，取消撤销工具且清除队列", () =>
        {
            using var state = new State(fixture);
            state.Save().GetAwaiter().GetResult();
            var pending = new TaskCompletionSource<PlayerToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Pending = pending;
            var save = state.Save();
            state.Boundary();
            File.WriteAllText(state.LastPlan!.OutputPath, "late inert output");
            pending.SetResult(new(0, state.LastPlan.OutputPath));
            Assert.Null(save.GetAwaiter().GetResult());
            Assert.Equal(0, state.Count);
            Assert.False(File.Exists(state.LastPlan.OutputPath));
            state.Pending = null;
            state.Save().GetAwaiter().GetResult();
            state.Save().GetAwaiter().GetResult();
            state.Pending = new TaskCompletionSource<PlayerToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stitch = state.Stitch();
            state.Cancel();
            Assert.True(state.LastToken.IsCancellationRequested);
            state.Pending.SetCanceled(state.LastToken);
            Assert.Null(stitch.GetAwaiter().GetResult());
            Assert.Equal(0, state.Count);
        });
    }

    private sealed class State : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string _directory;
        private readonly SettingsPageStateTests.FakeSettings _settings = new();
        private readonly EmbySession _session;
        private readonly PlaybackService _playback;
        private readonly object _controller;
        private readonly Type _type;
        private readonly string _input;
        private readonly Handle _handle = new();
        internal bool Fail { get; set; }
        internal TaskCompletionSource<PlayerToolResult>? Pending { get; set; }
        internal PlayerToolPlan? LastPlan { get; private set; }
        internal CancellationToken LastToken { get; private set; }
        internal int Count => (int)_type.GetProperty("PendingCount")!.GetValue(_controller)!;

        internal State(SettingsPageStateTests.ShellFixture fixture)
        {
            _directory = Path.Combine(fixture.Root, "artifacts", "player-capture-controller", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _input = Path.Combine(_directory, "input.png");
            File.WriteAllText(_input, "inert image boundary; never decoded");
            var executable = Path.Combine(_directory, "ffmpeg.exe");
            File.WriteAllText(executable, "inert tool; never executed");
            var tools = _settings.Settings.GetType().GetProperty("PlayerTools")!.GetValue(_settings.Settings)!;
            tools.GetType().GetProperty("FfmpegPath")!.SetValue(tools, executable);
            tools.GetType().GetProperty("OutputDirectory")!.SetValue(tools, _directory);
            var paths = new AppPaths(_directory);
            _session = new EmbySession(_settings.Settings, new SettingsStore(paths, PassthroughSecretProtector.Instance),
                new CredentialVault(PassthroughSecretProtector.Instance), DeviceIdentity.Create("capture-fixture", "1"));
            _playback = new PlaybackService(_session, _settings.Settings, () => throw new InvalidOperationException("No playback"),
                new PlaybackPlanner(_settings.Settings, new ShaderGroupResolver(_settings.Settings.Shaders)), allowPlayback: false);
            Field(_playback, "_current", _handle);
            var shell = fixture.NewViewModel().GetType().Assembly;
            var uiType = shell.GetType("Momoka.Shell.Platform.IUiDispatcher")!;
            var dispatcher = DispatchProxy.Create(uiType, typeof(CaptureDispatcher));
            _type = shell.GetType("Momoka.Shell.PlayerCaptureController")!;
            _controller = Activator.CreateInstance(_type, Members, null,
                [_playback, _settings, paths, dispatcher, (Func<PlayerToolPlan, CancellationToken, Task<PlayerToolResult>>)Run], null)!;
        }

        internal Task<string?> Save(int width = 128)
        {
            var cropType = _type.Assembly.GetType("Momoka.Shell.PlayerCaptureGeometry")!.GetMethod("Default", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType;
            var crop = Activator.CreateInstance(cropType, [0, 0, width, 64]);
            return (Task<string?>)_type.GetMethod("SaveCropAsync", Members)!.Invoke(_controller,
                [_input, crop, true, _playback.Generation, _handle.FileGeneration, CancellationToken.None])!;
        }
        internal Task<string?> Stitch() => (Task<string?>)_type.GetMethod("StitchAsync")!.Invoke(_controller, null)!;
        internal void Cancel() => _type.GetMethod("Cancel")!.Invoke(_controller, null);
        internal void Boundary()
        {
            Field(_playback, "_playbackGeneration", _playback.Generation + 1);
            _handle.FileGeneration++;
            _type.GetMethod("OnNowPlaying", Members)!.Invoke(_controller,
                [new PlaybackUpdate<EmbyItem?>(_playback.Generation, new EmbyItem { Id = "new", Name = "anonymous successor" })]);
        }
        private Task<PlayerToolResult> Run(PlayerToolPlan plan, CancellationToken token)
        {
            LastPlan = plan;
            LastToken = token;
            if (Pending is { } pending) return pending.Task;
            if (Fail) throw new PlayerToolException("inert rejected output");
            File.WriteAllText(plan.OutputPath, "inert output file");
            return Task.FromResult(new PlayerToolResult(0, plan.OutputPath));
        }
        private static void Field(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
        public void Dispose()
        {
            ((IDisposable)_controller).Dispose();
            _session.Dispose();
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
    }

    private sealed class Handle : IPlaybackHandle, IPlayerControl, IFileScopedPlayerControl
    {
        public long FileGeneration { get; set; } = 1;
        public bool HasControlChannel => true;
        public bool IsPaused => false;
        public PlayerStatus Status { get; } = new();
        public event Action<bool>? PauseChanged { add { } remove { } }
        public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
        public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
        public Task<double?> GetPositionAsync(CancellationToken token) => Task.FromResult<double?>(0);
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken token) => throw new InvalidOperationException("No media");
        public Task StopAsync() => Task.CompletedTask;
        public Task ShowMessageAsync(string text) => Task.CompletedTask;
        public Task SetPropertyAsync(string name, object? value, CancellationToken token) => throw new InvalidOperationException("No native");
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
        public Task<double?> GetNumberAsync(string name, CancellationToken token) => Task.FromResult<double?>(null);
        public Task<string?> GetTextAsync(string name, CancellationToken token) => Task.FromResult<string?>(null);
        public Task<bool> CommandAsync(IReadOnlyList<string> command, CancellationToken token) => throw new InvalidOperationException("No native");
        public Task<bool> CommandForFileAsync(long generation, IReadOnlyList<string> command, CancellationToken token) => throw new InvalidOperationException("No native");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public class CaptureDispatcher : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ((Action)args![0]!).Invoke();
        return method!.ReturnType == typeof(bool) ? true : null;
    }
}

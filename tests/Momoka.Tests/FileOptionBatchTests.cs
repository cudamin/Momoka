using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FileOptionBatchTests
{
    public static void Register()
    {
        Test("R13-005 延迟微调读取内核当前值而不使用旧菜单缓存", () => Run(async () =>
        {
            using var state = new State();
            state.Handle.Options["sub-delay"] = "0";
            var method = typeof(PlaybackService).GetMethod("ChangeSubtitleDelayAsync")!;
            Assert.NotNull(method);
            var result = (Task<double?>)method.Invoke(state.Service, [0.1, true, false])!;
            Assert.Equal<double?>(0.1, await result);
            Assert.Equal("0.1", state.Handle.Options["sub-delay"]);
        }));
        Test("R12-001 外部GPU不下发无法实现的HDR曲线与恢复项", () =>
        {
            var video = new VideoSettings { Renderer = "gpu", ToneMapping = "spline", HdrContrastRecovery = 0.7, HdrReferenceWhiteNits = 203 };
            var options = MpvOutputOptions.Build(video, new AudioSettings());
            Assert.False(options.Any(pair => pair.Key is "tone-mapping" or "hdr-contrast-recovery" or "hdr-reference-white"));
            video.Renderer = "gpu-next";
            options = MpvOutputOptions.Build(video, new AudioSettings());
            Assert.True(options.Any(pair => pair.Key == "tone-mapping" && pair.Value == "spline"));
        });
        Test("R12-004 截图旧组不能覆盖较新PNG快照", () => Run(async () =>
        {
            using var state = new State();
            state.Settings.Mpv.ScreenshotFormat = "webp";
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Handle.Before = async () => { entered.TrySetResult(); await release.Task; };
            var old = state.Service.ApplyScreenshotOptionsAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            state.Handle.Before = null;
            state.Settings.Mpv.ScreenshotFormat = "png";
            state.Settings.Mpv.ScreenshotTagColorspace = true;
            var latest = state.Service.ApplyScreenshotOptionsAsync();
            release.TrySetResult();
            await Task.WhenAll(old, latest).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("png", state.Handle.Options["screenshot-format"]);
            Assert.Equal("yes", state.Handle.Options["screenshot-tag-colorspace"]);
        }));
        foreach (var subtitle in new[] { false, true })
            Test($"文件选项组：原生同窗换片拒绝旧{subtitle}组的所有余项", () => Run(async () =>
            {
                using var state = new State();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                state.Handle.Before = async () => { entered.TrySetResult(); await release.Task; };
                var pending = subtitle ? state.Service.ApplySubtitleStyleAsync() : state.Service.ApplyScreenshotOptionsAsync();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                state.Handle.FileGeneration++;
                release.TrySetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(0, state.Handle.Options.Count);
            }));
        foreach (var failure in new[] { "reject", "throw" })
            Test($"R12-005 截图组选项{failure}必须反馈未完整应用", () => Run(async () =>
            {
                using var state = new State();
                state.Handle.Failure = failure;
                Exception? observed = null;
                try { await state.Service.ApplyScreenshotOptionsAsync(); } catch (Exception error) { observed = error; }
                Assert.True(observed is InvalidOperationException);
            }));
    }

    private static void Run(Func<Task> work) => work().GetAwaiter().GetResult();
    private sealed class State : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly EmbySession _session;
        public AppSettings Settings { get; } = new();
        public Handle Handle { get; } = new();
        public PlaybackService Service { get; }
        public State()
        {
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = new EmbySession(Settings, null!, new CredentialVault(new NoSecrets()), device, new NoNetwork());
            Service = new(_session, Settings, () => throw new AssertionException("no playback"),
                new PlaybackPlanner(Settings, new ShaderGroupResolver(Settings.Shaders)));
            var connection = new EmbyConnection(new Uri("https://fixture.invalid/emby/"), "placeholder", "fixture", "fixture", "fixture", device);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Flags)!.GetValue(_session)!;
            var scope = new EmbySessionScope(_session, new EmbySessionScope.Identity(new EmbyClient(http, connection),
                new ServerProfile { Id = "fixture" }, new AccountProfile { Id = "fixture" }));
            var request = new PlaybackRequest { MediaUrl = new Uri("https://fixture.invalid/never"), Title = "fixture" };
            var active = Activator.CreateInstance(typeof(PlaybackService).GetNestedType("PlayingAttempt", Flags)!, Flags, null,
                [Handle, request, 0L, new PlaybackTrackState(new MediaSource(), request), scope], null);
            typeof(PlaybackService).GetField("_current", Flags)!.SetValue(Service, Handle);
            typeof(PlaybackService).GetField("_active", Flags)!.SetValue(Service, active);
        }
        public void Dispose() => _session.Dispose();
    }

    private sealed class Handle : IPlaybackHandle, IPlayerControl, IFileScopedPlayerControl
    {
        public long FileGeneration { get; set; } = 1;
        public bool HasControlChannel => true;
        public bool IsPaused => false;
        public PlayerStatus Status => new();
        public Dictionary<string, string> Options { get; } = [];
        public Func<Task>? Before { get; set; }
        public string? Failure { get; set; }
        public event Action<bool>? PauseChanged { add { } remove { } }
        public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
        public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
        private async Task<bool> Set(long? expected, IReadOnlyList<string> arguments)
        {
            if (Before is { } wait) await wait();
            if (expected is { } file && file != FileGeneration) return false;
            if (Failure == "throw") throw new IOException("fixture failure");
            if (Failure == "reject") return false;
            if (arguments[0] == "show-text") return true;
            Options[arguments[1]] = arguments[0] == "add"
                ? (double.Parse(Options.GetValueOrDefault(arguments[1], "0"), System.Globalization.CultureInfo.InvariantCulture)
                    + double.Parse(arguments[2], System.Globalization.CultureInfo.InvariantCulture)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                : arguments[2];
            return true;
        }
        public Task<bool> CommandForFileAsync(long file, IReadOnlyList<string> arguments, CancellationToken token) => Set(file, arguments);
        public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken token) => Set(null, arguments);
        public async Task SetPropertyAsync(string name, object? value, CancellationToken token) =>
            _ = await Set(null, ["set", name, value?.ToString() ?? ""]);
        public Task<string?> GetTextAsync(string name, CancellationToken token) => Task.FromResult<string?>("0");
        public Task<double?> GetNumberAsync(string name, CancellationToken token) => Task.FromResult<double?>(
            double.TryParse(Options.GetValueOrDefault(name), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0);
        public Task<bool?> HasOptionAsync(string name, CancellationToken token) => Task.FromResult<bool?>(true);
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
        public Task<double?> GetPositionAsync(CancellationToken token) => Task.FromResult<double?>(0);
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken token) => throw new AssertionException("no playback");
        public Task StopAsync() => Task.CompletedTask;
        public Task ShowMessageAsync(string text) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string text) => throw new AssertionException("no secrets");
        public string Unprotect(string text) => throw new AssertionException("no secrets");
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new AssertionException("no network");
    }
}

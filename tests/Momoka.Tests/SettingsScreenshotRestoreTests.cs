using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;

namespace Momoka.Tests;

/// <summary>
/// Runs the actual VM's common playback restore tail and PlaybackService screenshot writer.
/// Core reset/import is applied in memory; theme/full reload/file pickers are not executed.
/// Source contracts separately prove both production restore paths use this exact common tail.
/// </summary>
internal static class SettingsScreenshotRestoreTests
{
    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("恢复截图组选项真实 VM／服务回归（假设置／活跃句柄；无窗口、配置IO或播放）");
        try
        {
            var shell = new SettingsPageStateTests.ShellFixture(shellAssemblyPath, sourceRoot);
            Register(shell);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Register(SettingsPageStateTests.ShellFixture shell)
    {
        TestHarness.Test("R-059 恢复默认/导入仍共用恢复收尾，播放段正式接线（源码契约）", () =>
        {
            var source = Regex.Replace(shell.ViewModelSource, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);
            Assert.Equal(3, Regex.Matches(source, @"\bReapplyAndReloadAsync\(").Count);
            Assert.Equal(2, Regex.Matches(source, @"\bReapplyPlaybackPreferencesAsync\(").Count);
            Assert.True(source.Contains("await ReapplyPlaybackPreferencesAsync(notice)", StringComparison.Ordinal));
        });
        foreach (var originalFormat in new[] { "jpg", "webp" })
            Case($"R-059 {originalFormat}恢复默认，下一截图前完整应用PNG组选项", async () =>
            {
                using var state = new State(shell);
                state.Settings.Settings.Mpv.ScreenshotFormat = originalFormat;
                await state.Prime();
                var mpv = state.Settings.Settings.Mpv;
                SettingsReset.Restore(state.Settings.Settings);
                Assert.True(ReferenceEquals(mpv, state.Settings.Settings.Mpv));
                await state.Reapply();
                await state.Service.CommandAsync("screenshot", "video");
                Assert.Equal("png", state.Handle.LastScreenshot!["screenshot-format"]);
                Assert.Equal("no", state.Handle.LastScreenshot["screenshot-tag-colorspace"]);
                Assert.True(state.Handle.Writes.Any(pair => pair.Key == "screenshot-format" && pair.Value == "png"));
                Assert.Equal(1, state.ScreenshotPushes);
                Assert.Equal(1, state.SubtitlePushes);
                Assert.Equal(1, state.DirectoryPushes);
                Assert.False(state.Get<bool>("NoticeOpen") && state.Get<object>("NoticeSeverity").ToString() == "Error");
            });
        foreach (var format in new[] { "jpg", "webp", "png" })
            foreach (var lossless in format == "webp" ? new[] { false, true } : new[] { false })
                Case($"R-059 导入{format}无损{lossless}，适用组选项在首个截图前已写", async () =>
                {
                    using var state = new State(shell);
                    await state.Prime();
                    var imported = new AppSettings();
                    imported.Mpv.ScreenshotFormat = format;
                    imported.Mpv.ScreenshotWebpLossless = lossless;
                    imported.Mpv.ScreenshotWebpQuality = 37;
                    imported.Mpv.ScreenshotWebpCompression = 2;
                    imported.Mpv.ScreenshotJpegQuality = 41;
                    imported.Mpv.ScreenshotTagColorspace = true;
                    SettingsPreferences.Apply(state.Settings.Settings, imported);
                    await state.Reapply();
                    var expected = MpvBaseline.ScreenshotOptions.From(state.Settings.Settings.Mpv).ToMpvOptions();
                    Assert.Equal(string.Join('|', expected.Select(pair => pair.Key + "=" + pair.Value)),
                        string.Join('|', state.Handle.Writes.Select(pair => pair.Key + "=" + pair.Value)));
                    await state.Service.CommandAsync("screenshot", "video");
                    foreach (var (name, value) in expected) Assert.Equal(value, state.Handle.LastScreenshot![name]);
                    Assert.Equal(1, state.ScreenshotPushes);
                });
        Case("R-059 同格式JPG只改质量/色彩标记仍推送整组", async () =>
        {
            using var state = new State(shell);
            state.Settings.Settings.Mpv.ScreenshotFormat = "jpg";
            await state.Prime();
            state.Settings.Settings.Mpv.ScreenshotJpegQuality = 24;
            state.Settings.Settings.Mpv.ScreenshotTagColorspace = true;
            await state.Reapply();
            Assert.Equal("24", state.Handle.Properties["screenshot-jpeg-quality"]);
            Assert.Equal("yes", state.Handle.Properties["screenshot-tag-colorspace"]);
            Assert.Equal(3, state.Handle.Writes.Count);
        });
        Case("R-059 截图写入挂起时恢复任务不提前完成或提示成功", async () =>
        {
            using var state = new State(shell);
            state.Settings.Settings.Mpv.ScreenshotFormat = "webp";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Handle.BeforeSet = () => gate.Task;
            var pending = state.Reapply();
            Assert.False(pending.IsCompleted, "必须await截图组选项，不可fire-and-forget");
            Assert.Equal(1, state.ScreenshotPushes);
            Assert.False(state.Get<bool>("NoticeOpen"));
            gate.TrySetResult();
            await pending;
            Assert.Equal(5, state.Handle.Writes.Count);
            Assert.Equal("webp", state.Handle.Properties["screenshot-format"]);
        });
        Case("R-059 字幕更新失败仍应用截图组选项，不冒全成功", async () =>
        {
            using var state = new State(shell) { SubtitleError = new IOException("fixture subtitle failure") };
            await state.Reapply();
            Assert.Equal(1, state.ScreenshotPushes);
            Assert.Equal("png", state.Handle.Properties["screenshot-format"]);
            Assert.Contains("未完全更新", state.Get<string>("NoticeTitle"));
            Assert.Equal("Error", state.Get<object>("NoticeSeverity").ToString());
        });
        Case("R-059 截图委托失败留下错误提示，不打印完整恢复成功", async () =>
        {
            using var state = new State(shell) { ScreenshotError = new IOException("fixture screenshot failure") };
            await state.Reapply();
            Assert.Equal(1, state.ScreenshotPushes);
            Assert.True(state.Get<bool>("NoticeOpen"));
            Assert.Contains("截图", state.Get<string>("NoticeTitle"));
            Assert.Equal("Error", state.Get<object>("NoticeSeverity").ToString());
        });
        Case("R-059 截图取消不冒成功，已恢复的偏好保留", async () =>
        {
            using var state = new State(shell) { ScreenshotError = new OperationCanceledException() };
            state.Settings.Settings.Mpv.ScreenshotFormat = "webp";
            await state.Reapply();
            Assert.Equal(1, state.ScreenshotPushes);
            Assert.False(state.Get<bool>("NoticeOpen"));
            Assert.Equal("webp", state.Settings.Settings.Mpv.ScreenshotFormat);
        });
        Case("R-059 共享保存失败保留未保存警告，不多Save掩盖", async () =>
        {
            using var state = new State(shell);
            state.Settings.NextSaveSucceeds = false;
            Assert.False(state.Settings.TrySave());
            state.Invoke("SyncSaveState");
            await state.Reapply();
            Assert.Equal(1, state.ScreenshotPushes);
            Assert.Equal(1, state.Settings.TrySaveCalls);
            Assert.Equal("Warning", state.Get<object>("NoticeSeverity").ToString());
            Assert.Contains("尚未保存", state.Get<string>("NoticeMessage"));
        });
        Case("R-059 未连接截图推送/无在播句柄仍可恢复，不产生写入", async () =>
        {
            using var state = new State(shell, withScreenshotCallback: false);
            await state.Reapply();
            Assert.Equal(0, state.ScreenshotPushes);
            Assert.Equal(0, state.Handle.Writes.Count);
            state.DropHandle();
            await state.Service.ApplyScreenshotOptionsAsync();
            Assert.Equal(0, state.Handle.Writes.Count);
        });
    }

    private static void Case(string name, Func<Task> action) => TestHarness.Test(name, () => action().GetAwaiter().GetResult());

    private sealed class State : IDisposable
    {
        private readonly SettingsPageStateTests.ShellFixture _shell;
        private readonly EmbySession _session;
        private readonly object _vm;
        public SettingsPageStateTests.FakeSettings Settings { get; } = new();
        public ScreenshotHandle Handle { get; } = new();
        public PlaybackService Service { get; }
        public int SubtitlePushes { get; private set; }
        public int ScreenshotPushes { get; private set; }
        public int DirectoryPushes { get; private set; }
        public Exception? SubtitleError { get; set; }
        public Exception? ScreenshotError { get; set; }

        public State(SettingsPageStateTests.ShellFixture shell, bool withScreenshotCallback = true)
        {
            _shell = shell;
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "screenshot-restore|no-configuration-IO"));
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            _session = new EmbySession(Settings.Settings, new SettingsStore(paths, new NoSecrets()),
                new CredentialVault(new NoSecrets()), device, new NoNetwork());
            var request = new PlaybackRequest { MediaUrl = new Uri("https://fixture.invalid/never-loaded"), Title = "fixture" };
            var connection = new EmbyConnection(new Uri("https://fixture.invalid/emby/"), "placeholder", "fixture", "fixture", "fixture", device);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_session)!;
            var scope = new EmbySessionScope(_session, new EmbySessionScope.Identity(new EmbyClient(http, connection),
                new ServerProfile { Id = "fixture" }, new AccountProfile { Id = "fixture" }));
            Service = new PlaybackService(_session, Settings.Settings, () => throw new AssertionException("不允许启动后端"),
                new PlaybackPlanner(Settings.Settings, new ShaderGroupResolver(Settings.Settings.Shaders)));
            // Only the screenshot setter is called; no startup/monitor/report path or real playback.
            var attempt = typeof(PlaybackService).GetNestedType("PlayingAttempt", BindingFlags.NonPublic)!;
            var active = Activator.CreateInstance(attempt, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, [Handle, request, 0L, new PlaybackTrackState(new MediaSource(), request), scope], culture: null)!;
            typeof(PlaybackService).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, Handle);
            typeof(PlaybackService).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, active);
            _vm = shell.NewViewModel();
            Func<Task> screenshot = async () =>
            {
                ScreenshotPushes++;
                if (ScreenshotError is { } error) throw error;
                await Service.ApplyScreenshotOptionsAsync();
            };
            var arguments = shell.Method("Attach").GetParameters().Select(parameter => parameter.Name switch
            {
                "settings" => (object)Settings,
                "paths" => shell.Paths,
                "pushSubtitleStyle" => (Func<Task>)(() =>
                {
                    SubtitlePushes++;
                    return SubtitleError is { } error ? Task.FromException(error) : Task.CompletedTask;
                }),
                "pushScreenshotDirectory" => (Func<string, Task>)(_ => { DirectoryPushes++; return Task.CompletedTask; }),
                "pushScreenshotOptions" => withScreenshotCallback ? screenshot : null,
                _ => null
            }).ToArray();
            shell.Invoke(_vm, "Attach", arguments);
        }
        public async Task Prime()
        {
            await Service.ApplyScreenshotOptionsAsync();
            Handle.Writes.Clear();
        }
        public Task Reapply() => (Task)Invoke("ReapplyPlaybackPreferencesAsync", "fixture complete")!;
        public object? Invoke(string name, params object?[] args) => _shell.Invoke(_vm, name, args);
        public T Get<T>(string name) => _shell.Get<T>(_vm, name);
        public void DropHandle()
        {
            typeof(PlaybackService).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, null);
            typeof(PlaybackService).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, null);
        }
        public void Dispose()
        {
            DropHandle();
            ((IDisposable)_vm).Dispose();
            _session.Dispose();
        }
    }

    private sealed class ScreenshotHandle : IPlaybackHandle, IPlayerControl
    {
        public bool HasControlChannel => true;
        public bool IsPaused => false;
        public PlayerStatus Status { get; } = new();
        public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
        public ConcurrentQueue<KeyValuePair<string, string>> Writes { get; } = new();
        public IReadOnlyDictionary<string, string>? LastScreenshot { get; private set; }
        public Func<Task>? BeforeSet { get; set; }
        public event Action<bool>? PauseChanged { add { } remove { } }
        public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
        public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
        public async Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken)
        {
            if (BeforeSet is { } before) await before();
            var text = value?.ToString() ?? "";
            Properties[name] = text;
            Writes.Enqueue(new(name, text));
        }
        public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Assert.Equal("screenshot", arguments[0]);
            LastScreenshot = new Dictionary<string, string>(Properties, StringComparer.Ordinal);
            return Task.FromResult(true);
        }
        public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => Task.FromResult<double?>(0);
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken) => throw new AssertionException("不允许监视真实播放");
        public Task StopAsync() => Task.CompletedTask;
        public Task ShowMessageAsync(string text) => Task.CompletedTask;
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
        public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(null);
        public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("不允许配置/凭据IO");
        public string Unprotect(string cipherText) => throw new AssertionException("不允许配置/凭据IO");
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("不允许HTTP请求或上报");
    }
}

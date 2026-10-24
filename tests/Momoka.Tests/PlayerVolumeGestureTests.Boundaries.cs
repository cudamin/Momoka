using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;

namespace Momoka.Tests;

internal static partial class PlayerVolumeGestureTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class ShellFixture : IDisposable
    {
        private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _resolve;
        internal Type ViewModel { get; }
        internal Type Dispatcher { get; }
        internal string Root { get; }

        internal ShellFixture(string assemblyPath, string sourceRoot)
        {
            Root = Path.GetFullPath(sourceRoot);
            var assemblyFile = Path.GetFullPath(assemblyPath);
            var release = Path.Combine(Root, "src", "Momoka.Shell", "bin", "x64", "Release") + Path.DirectorySeparatorChar;
            if (!File.Exists(Path.Combine(Root, "Momoka.sln")) || !File.Exists(assemblyFile)
                || !assemblyFile.StartsWith(release, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(assemblyFile) != "Momoka.dll")
                throw new InvalidOperationException("必须显式提供本树当前 x64 Release Shell DLL，不能借其它树输出");
            var directory = Path.GetDirectoryName(assemblyFile)!;
            _resolve = (context, name) =>
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };
            AssemblyLoadContext.Default.Resolving += _resolve;
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyFile);
            ViewModel = assembly.GetType("Momoka.Shell.ViewModels.PlayerViewModel", throwOnError: true)!;
            Dispatcher = assembly.GetType("Momoka.Shell.Platform.IUiDispatcher", throwOnError: true)!;
            Console.WriteLine("Shell=" + assemblyFile);
            Console.WriteLine("Shell SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyFile))));
            Console.WriteLine("Core=" + typeof(PlaybackService).Assembly.Location);
        }

        internal object? Invoke(object vm, string name, params object?[] arguments)
        {
            try
            {
                return (ViewModel.GetMethod(name, Members) ?? throw new AssertionException("真实 VM 入口缺失：" + name))
                    .Invoke(vm, arguments);
            }
            catch (TargetInvocationException error) when (error.InnerException is { } inner)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }
        }
        public void Dispose() => AssemblyLoadContext.Default.Resolving -= _resolve;
    }

    private sealed class State : IAsyncDisposable
    {
        private readonly ShellFixture _shell;
        private readonly object _vm;
        private readonly EmbySession _session;
        private readonly PlayerVolumeDispatcherProxy _dispatcher;
        private readonly List<Handle> _handles = [];
        private readonly List<EmbySessionScope.Identity> _trackIdentities = [];
        internal FakeSettings Settings { get; } = new();
        internal Handle Handle { get; private set; }
        internal PlaybackService Service { get; }
        internal VolumeMemory Memory => Field<VolumeMemory>("_volumeMemory");
        internal bool Ready => Field<bool>("_volumeReady");
        internal double Volume => (double)_shell.ViewModel.GetProperty("Volume", Members)!.GetValue(_vm)!;

        internal State(ShellFixture shell, double actual)
        {
            _shell = shell;
            // A forbidden filename prevents any accidental configuration path from becoming real IO.
            var paths = new AppPaths(Path.Combine(shell.Root, "work", "r053-r054", "no-settings|no-secrets"));
            var secrets = new NoSecrets();
            _session = new EmbySession(Settings.Settings, new SettingsStore(paths, secrets), new CredentialVault(secrets),
                new DeviceIdentity("fixture", "fixture", "fixture", "0"), new NoNetwork());
            var shaders = new ShaderGroupResolver(Settings.Settings.Shaders);
            Service = new PlaybackService(_session, Settings.Settings, () => throw new AssertionException("禁止启动后端"),
                new PlaybackPlanner(Settings.Settings, shaders));
            Handle = new Handle { Actual = actual };
            _handles.Add(Handle);
            SetField(Service, "_current", Handle);
            var dispatcher = DispatchProxy.Create(shell.Dispatcher, typeof(PlayerVolumeDispatcherProxy));
            _dispatcher = (PlayerVolumeDispatcherProxy)dispatcher;
            _dispatcher.Queue = work => VolumeContext.Current!.Post(_ => work(), null);
            // Image service is not used by these entry points; constructing it would create a cache directory.
            _vm = Activator.CreateInstance(shell.ViewModel, Service, Settings, _session, null, shaders, dispatcher)!;
            Assert.Equal(100.0, Volume);
            Assert.False(Ready);
            Assert.Null(Memory.Pending);
        }

        private T Field<T>(string name) => (T)_shell.ViewModel.GetField(name, Members)!.GetValue(_vm)!;
        internal IReadOnlyList<MpvTrack> Tracks => Field<IReadOnlyList<MpvTrack>>("<Tracks>k__BackingField");
        internal Task PopulateTracks()
        {
            // GetTracksAsync requires an established non-ending attempt as well as a current handle.
            // Construct only that in-memory ownership boundary, never the startup/report path.
            var request = new PlaybackRequest { MediaUrl = new Uri("https://fixture.invalid/never-loaded"), Title = "fixture" };
            var device = new DeviceIdentity("fixture", "fixture", "fixture", "0");
            var connection = new EmbyConnection(new Uri("https://fixture.invalid/emby/"), "placeholder", "fixture", "fixture", "fixture", device);
            var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Members)!.GetValue(_session)!;
            var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection), new ServerProfile { Id = "fixture" }, new AccountProfile { Id = "fixture" });
            _trackIdentities.Add(identity);
            var scope = new EmbySessionScope(_session, identity);
            var attempt = typeof(PlaybackService).GetNestedType("PlayingAttempt", BindingFlags.NonPublic)!;
            var active = Activator.CreateInstance(attempt, Members, binder: null,
                [Handle, request, Service.Generation, new PlaybackTrackState(new MediaSource(), request), scope], culture: null)!;
            SetField(Service, "_active", active);
            return (Task)_shell.Invoke(_vm, "PopulateTracksAsync", Field<int>("_generation"))!;
        }
        internal void NotifyTracks(IReadOnlyList<MpvTrack> tracks, long? generation = null) =>
            _shell.Invoke(_vm, "OnTracksChanged", new PlaybackUpdate<IReadOnlyList<MpvTrack>>(generation ?? Service.Generation, tracks));
        internal void QueueTracks(IReadOnlyList<MpvTrack> tracks, long? generation = null)
        {
            _dispatcher.HoldRuns = true;
            try { NotifyTracks(tracks, generation); }
            finally { _dispatcher.HoldRuns = false; }
        }
        internal void DeliverTracks()
        {
            Assert.True(_dispatcher.Deferred.TryDequeue(out var work), "必须先排入真实轨道通知 UI 回调");
            work!();
        }
        internal void TrackFileBoundary(bool reuse)
        {
            Replace(reuse, actual: 37);
            SetField(_vm, "_generation", Field<int>("_generation") + 1);
            _shell.ViewModel.GetProperty("Tracks", Members)!.SetValue(_vm, Array.Empty<MpvTrack>());
        }
        internal void Nudge(double step) => _shell.Invoke(_vm, "NudgeVolume", step);
        internal void SetVolume(double value) => _shell.ViewModel.GetProperty("Volume", Members)!.SetValue(_vm, value);
        internal void Status(PlayerStatus status)
        {
            Handle.Current = status;
            _shell.Invoke(_vm, "ApplyStatus", status);
        }
        internal void QueueStatus(PlayerStatus status)
        {
            _dispatcher.HoldRuns = true;
            try { _shell.Invoke(_vm, "OnStatusChanged", new PlaybackUpdate<PlayerStatus>(Service.Generation, status)); }
            finally { _dispatcher.HoldRuns = false; }
        }
        internal void DeliverStatus()
        {
            Assert.True(_dispatcher.Deferred.TryDequeue(out var work), "必须先排入真实 OnStatusChanged 的 UI 回调");
            work!();
        }
        internal void Prime(int level)
        {
            Settings.Settings.Audio.Volume = level;
            Handle.Actual = level;
            Status(Known(level));
            Memory.Saved(Memory.Revision);
        }
        internal void Flush() => _shell.Invoke(_vm, "FlushVolume", false);
        internal void Replace(bool reuse, double actual)
        {
            if (!reuse)
            {
                Handle = new Handle();
                _handles.Add(Handle);
                SetField(Service, "_current", Handle);
            }
            Handle.Actual = actual;
            SetField(Service, "_playbackGeneration", Service.Generation + 1);
            // Only the lifetime boundary is simulated; actual command/read and VM return guards still run.
            Memory.ResetObservation();
            _shell.ViewModel.GetField("_volumeReady", Members)!.SetValue(_vm, false);
            Status(new PlayerStatus { Loaded = true, Volume = actual, VolumeKnown = false });
        }
        internal void CancelAndDropHandle()
        {
            SetField(Service, "_current", null);
            SetField(Service, "_active", null);
            Field<CancellationTokenSource>("_lifetime").Cancel();
        }
        internal void Trace(string label) => Console.WriteLine($"  {label}: commands={Handle.CommandText}; kernel={Handle.Actual}; display={Volume}; saved={Settings.SavedText}");
        public async ValueTask DisposeAsync()
        {
            CancelAndDropHandle();
            foreach (var handle in _handles) handle.ReleaseAll();
            if (Field<Task?>("_volumeSaveTask") is { } saving) await saving.WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var identity in _trackIdentities)
            {
                identity.Lifetime.Cancel();
                identity.Lifetime.Dispose();
                identity.Reauthentication.Dispose();
                identity.PasswordChanges.Dispose();
            }
            _session.Dispose();
        }
    }

    private static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, Members)!.SetValue(target, value);

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public int PageSize => 50;
        public bool SaveSucceeds { get; set; } = true;
        public event EventHandler? SaveStateChanged { add { } remove { } }
        public bool HasUnsavedChanges { get; private set; }
        public int SaveCalls { get; private set; }
        private readonly List<int> _saved = [];
        public string SavedText => string.Join(',', _saved);
        public bool TrySave()
        {
            SaveCalls++;
            HasUnsavedChanges = !SaveSucceeds;
            if (SaveSucceeds) _saved.Add(Settings.Audio.Volume);
            return SaveSucceeds;
        }
        public void Save() => throw new AssertionException("只允许内存 TrySave");
        public ServerProfile ResolveServer(string url) => throw new AssertionException("禁止账号或设置 IO");
        public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("禁止账号或设置 IO");
        public void ExportPreferences(string path) => throw new AssertionException("禁止设置 IO");
        public AppSettings ReadBackup(string path) => throw new AssertionException("禁止设置 IO");
    }

    private enum ReadFailure { None, Null, NonFinite, Exception }
    private sealed class ReadGate
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<double?> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class CommandGate
    {
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TrackReadGate
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<IReadOnlyList<MpvTrack>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Handle : IPlaybackHandle, IPlayerControl
    {
        private ReadGate? _read;
        private TrackReadGate? _trackRead;
        private readonly List<TrackReadGate> _trackReads = [];
        internal int TrackReads { get; private set; }
        internal TrackReadGate HoldTracks()
        {
            _trackRead = new TrackReadGate();
            _trackReads.Add(_trackRead);
            return _trackRead;
        }
        private CommandGate? _command;
        private readonly List<ReadGate> _reads = [];
        private readonly List<CommandGate> _commands = [];
        internal ConcurrentQueue<string[]> Commands { get; } = new();
        internal string CommandText => string.Join(',', Commands.Select(args => args[0] + ":" + args[2]));
        internal double Actual { get; set; }
        internal bool Control { get; set; } = true;
        internal bool Reject { get; set; }
        internal ReadFailure Failure { get; set; }
        internal PlayerStatus Current { get; set; } = new();
        internal ReadGate HoldRead() { _read = new ReadGate(); _reads.Add(_read); return _read; }
        internal CommandGate HoldCommand() { _command = new CommandGate(); _commands.Add(_command); return _command; }
        internal void ReleaseAll()
        {
            foreach (var read in _reads) read.Release.TrySetResult(null);
            foreach (var read in _trackReads) read.Release.TrySetResult(Array.Empty<MpvTrack>());
            foreach (var command in _commands) command.Release.TrySetResult(false);
        }
        public bool HasControlChannel => Control;
        public bool IsPaused => false;
        public PlayerStatus Status => Current;
        public event Action<bool>? PauseChanged { add { } remove { } }
        public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
        public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
        public async Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (arguments.Count != 3 || arguments[1] != "volume" || arguments[0] is not ("add" or "set"))
                throw new AssertionException("音量夹具拒绝其它播放领域命令");
            Commands.Enqueue([.. arguments]);
            var command = Interlocked.Exchange(ref _command, null);
            var accepted = command is null ? !Reject : await command.Release.Task.ConfigureAwait(false);
            if (!accepted) return false;
            var value = double.Parse(arguments[2], CultureInfo.InvariantCulture);
            Actual = Math.Clamp(arguments[0] == "add" ? Actual + value : value, 0, AudioSettings.MaxVolume);
            return true;
        }
        public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken)
        {
            if (name != "volume") throw new AssertionException("只能读取假音量");
            var read = Interlocked.Exchange(ref _read, null);
            if (read is not null) { read.Entered.TrySetResult(); return read.Release.Task; }
            return Failure switch
            {
                ReadFailure.Null => Task.FromResult<double?>(null),
                ReadFailure.NonFinite => Task.FromResult<double?>(double.NaN),
                ReadFailure.Exception => Task.FromException<double?>(new IOException("fixture read failure")),
                _ => Task.FromResult<double?>(Actual)
            };
        }
        public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => throw new AssertionException("禁止其它播放读取");
        public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => throw new AssertionException("禁止其它播放读取");
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken)
        {
            var read = Interlocked.Exchange(ref _trackRead, null) ?? throw new AssertionException("只有轨道专项可读取受控假列表");
            TrackReads++;
            read.Entered.TrySetResult();
            return read.Release.Task;
        }
        public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken) => throw new AssertionException("只允许音量命令");
        public Task ShowMessageAsync(string text) => throw new AssertionException("禁止通知或原生消息");
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken) => throw new AssertionException("禁止监控播放");
        public Task StopAsync() => throw new AssertionException("不启动或停止真实播放，仅替换边界句柄");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoSecrets : ISecretProtector
    {
        public string Protect(string plainText) => throw new AssertionException("禁止凭据／DPAPI");
        public string Unprotect(string cipherText) => throw new AssertionException("禁止凭据／DPAPI");
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("禁止 HTTP／登录／上报");
    }

    private sealed class VolumeContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Work, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        internal static void WaitForPosts(int count)
        {
            var context = (VolumeContext)Current!;
            Assert.True(SpinWait.SpinUntil(() => context._queue.Count >= count, TimeSpan.FromSeconds(10)), "真实 VM 返回未进入 UI 队列");
        }
        internal static void RunPosts(int count)
        {
            WaitForPosts(count);
            var context = (VolumeContext)Current!;
            for (var index = 0; index < count; index++) context.RunOne();
        }
        internal static void RunPostsReversed(int count)
        {
            WaitForPosts(count);
            var context = (VolumeContext)Current!;
            var queued = new List<(SendOrPostCallback Work, object? State)>();
            for (var index = 0; index < count; index++)
            {
                Assert.True(context._queue.TryTake(out var entry, TimeSpan.FromSeconds(10)), "音量逆序 UI 夹具超时");
                queued.Add(entry);
            }
            for (var index = queued.Count - 1; index >= 0; index--)
                queued[index].Work(queued[index].State);
        }
        private void RunOne()
        {
            Assert.True(_queue.TryTake(out var entry, TimeSpan.FromSeconds(10)), "音量 UI 夹具超时，不以跳过洗绿");
            entry.Work(entry.State);
        }
        internal static void Run(Func<Task> action)
        {
            var previous = Current;
            using var context = new VolumeContext();
            SetSynchronizationContext(context);
            try
            {
                var work = action();
                while (!work.IsCompleted) context.RunOne();
                work.GetAwaiter().GetResult();
            }
            finally { SetSynchronizationContext(previous); }
        }
        public void Dispose() => _queue.Dispose();
    }
}

/// <summary>Implements the real Shell UI contract; deferred actions let old status delivery follow confirmation.</summary>
public class PlayerVolumeDispatcherProxy : DispatchProxy
{
    internal Action<Action>? Queue { get; set; }
    internal bool HoldRuns { get; set; }
    internal ConcurrentQueue<Action> Deferred { get; } = new();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (args is not [Action work]) throw new AssertionException("未知 UI 边界");
        if (targetMethod?.Name == "Post") { Queue!(work); return true; }
        if (targetMethod?.Name != "Run") throw new AssertionException("未知 UI 边界");
        if (HoldRuns) Deferred.Enqueue(work);
        else work();
        return null;
    }
}

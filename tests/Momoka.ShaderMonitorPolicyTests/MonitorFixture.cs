using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;

namespace Momoka.Tests;

internal sealed class ShellRuntime
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal Type VmType { get; }
    private Type DispatcherType { get; }
    internal ShellRuntime(string shell, string root)
    {
        var expected = Path.Combine(Path.GetFullPath(root), "src", "Momoka.Shell", "bin", "x64", "Release",
            "net10.0-windows10.0.19041.0", "win-x64", "Momoka.dll");
        Assert.Equal(expected, Path.GetFullPath(shell), "only current workspace Release Shell may execute");
        var directory = Path.GetDirectoryName(expected)!;
        var core = Path.Combine(directory, "Momoka.Core.dll");
        Assert.Equal(Hash(core), Hash(typeof(PlaybackService).Assembly.Location), "VM and transaction must share current Core");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(expected);
        VmType = assembly.GetType("Momoka.Shell.ViewModels.PlayerViewModel", throwOnError: true)!;
        DispatcherType = assembly.GetType("Momoka.Shell.Platform.IUiDispatcher", throwOnError: true)!;
        Console.WriteLine($"SHELL {expected} SHA256={Hash(expected)} MVID={assembly.ManifestModule.ModuleVersionId}");
        Console.WriteLine($"CORE SHA256={Hash(typeof(PlaybackService).Assembly.Location)}; real VM plus real Core transaction; no App/native/network/media/config");
    }
    internal object Create(PlaybackService service, MonitorSettings settings, ShaderGroupResolver resolver) =>
        Activator.CreateInstance(VmType, service, settings, null, null, resolver,
            DispatchProxy.Create(DispatcherType, typeof(MonitorDispatcher)))!;
    internal object? Call(object vm, string name, params object?[] args)
    {
        var method = VmType.GetMethod(name, Members) ?? throw new AssertionException("actual VM method missing: " + name);
        try { return method.Invoke(vm, args); }
        catch (TargetInvocationException error) when (error.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }
    internal T Get<T>(object vm, string name) => (T)VmType.GetField(name, Members)!.GetValue(vm)!;
    internal void Set(object vm, string name, object? value) => VmType.GetField(name, Members)!.SetValue(vm, value);
    internal void Surface(object vm, Func<ShaderSurface> read) => VmType.GetProperty("MeasureSurface", Members)!.SetValue(vm, read);
    internal ShaderGroup? Active(object vm) => (ShaderGroup?)VmType.GetProperty("ActiveShader", Members)!.GetValue(vm);
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
}

internal sealed class MonitorFixture : IDisposable
{
    private readonly ShellRuntime _shell;
    private readonly object _vm;
    private readonly ShaderAutomationSettings _snapshot;
    internal MonitorSettings Settings { get; } = new();
    internal PlaybackService Service { get; }
    internal MonitorHandle Handle { get; } = new();
    internal EmbyItem Item { get; } = new() { Id = "anonymous", Name = "Anonymous film", Type = EmbyItemType.Movie };
    internal MediaSource Source { get; }
    internal ShaderSurface Surface { get; private set; }
    internal ShaderDecision Plan => _shell.Get<ShaderDecision>(_vm, "_shaderPlan");
    internal OutputWatch Watch => _shell.Get<OutputWatch>(_vm, "_outputWatch");
    internal ShaderGroup? Active => _shell.Active(_vm);
    internal Task Pending => _shell.Get<Task>(_vm, "_shaderSwitchTask");
    internal (int Width, int Height) LaunchOutput => _shell.Get<(int, int)>(_vm, "_launchOutput");

    internal MonitorFixture(ShellRuntime shell, ShaderSurface surface, int width = 1920, int height = 1080,
        bool enabled = true, string manual = "")
    {
        _shell = shell;
        Surface = surface;
        Source = new MediaSource { Id = "source", MediaStreams = [new MediaStream { Type = "Video", Width = width, Height = height }] };
        Settings.Settings.Shaders.Enabled = enabled;
        Settings.Settings.Shaders.ManualGroup = manual;
        _snapshot = Settings.Settings.Shaders.Snapshot();
        var resolver = new ShaderGroupResolver(Settings.Settings.Shaders);
        Service = new PlaybackService(null!, Settings.Settings, () => throw new AssertionException("No backend may start"),
            new PlaybackPlanner(Settings.Settings, resolver), allowPlayback: false);
        // Construct the actual VM before installing a handle, avoiding observation/persistence activity.
        _vm = shell.Create(Service, Settings, resolver);
        shell.Set(_vm, "_nowPlaying", Item);
        shell.Set(_vm, "_generation", 1);
        shell.Surface(_vm, () => Surface);
        shell.Call(_vm, "PrepareShaderPlans", Item, Source, null);
        typeof(PlaybackService).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, Handle);
        typeof(PlaybackService).GetField("_playbackGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, 1L);
        Handle.Seed(Active);
    }
    internal ShaderDecision Expected(ShaderSurface surface) => new ShaderGroupResolver(_snapshot).Resolve(Item, Source, null, surface.Monitor);
    internal void Move(ShaderSurface surface)
    {
        Surface = surface;
        _shell.Call(_vm, "NoteSurface", (Func<ShaderSurface>)(() => Surface));
    }
    internal async Task Settle()
    {
        for (var i = 0; i < 10; i++)
        {
            var pending = Pending;
            await pending;
            if (ReferenceEquals(pending, Pending)) return;
        }
        throw new AssertionException("shader queue did not settle");
    }
    internal async Task Restore()
    {
        _shell.Call(_vm, "RestoreShaderPlan");
        await Settle();
    }
    internal async Task Pin(ShaderGroup? group)
    {
        _shell.Call(_vm, "ApplyShaderGroup", group);
        await Settle();
    }
    internal void Tick() => _shell.Call(_vm, "Judge", Watch.Tick(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1)), "窗口尺寸变化");
    internal async Task Adopt(bool sourceChanged = false)
    {
        if (sourceChanged) Source.MediaStreams[0].Width = 1280;
        var plan = Expected(Surface);
        typeof(PlaybackService).GetField("<PlayingSource>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, Source);
        typeof(PlaybackService).GetField("<LaunchShaderDecision>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, (ShaderDecision?)plan);
        Handle.Seed(plan.Group);
        _shell.Call(_vm, "AdoptShaderLaunch", Item);
        await Settle();
    }
    public void Dispose()
    {
        Assert.Equal(0, Handle.Forbidden, "all player endpoints must remain within the transaction boundary");
        Assert.Equal(0, Settings.Writes, "monitor changes never persist preferences");
        _shell.Get<CancellationTokenSource>(_vm, "_lifetime").Cancel();
        typeof(PlaybackService).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, null);
    }
}

internal sealed class MonitorSettings : ISettingsService
{
    public AppSettings Settings { get; } = new();
    public int PageSize => 50;
    public bool HasUnsavedChanges => false;
    internal int Writes { get; private set; }
    public bool TrySave() { Writes++; throw new AssertionException("forbidden settings write"); }
    public void Save() { Writes++; throw new AssertionException("forbidden settings write"); }
    public ServerProfile ResolveServer(string url) => throw new AssertionException("forbidden account operation");
    public AccountProfile ResolveAccount(ServerProfile server, string username) => throw new AssertionException("forbidden account operation");
    public void Reload() => throw new AssertionException("forbidden real settings read");
    public void ExportPreferences(string path) => throw new AssertionException("forbidden settings export");
    public AppSettings ReadBackup(string path) => throw new AssertionException("forbidden settings import");
}

public class MonitorDispatcher : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (args is not [Action work]) throw new AssertionException("unknown UI boundary");
        work();
        return method?.Name == "Post" ? true : null;
    }
}

internal sealed class MonitorHandle : IPlaybackHandle, IPlayerControl
{
    private readonly Dictionary<string, string> _values = ShaderGroupCatalog.NeutralOptions.ToDictionary(p => p.Key, p => p.Value);
    private IReadOnlyList<string> _shaders = [];
    internal List<string[]> Writes { get; } = [];
    internal int Notices { get; private set; }
    internal int Forbidden { get; private set; }
    internal bool Reject { get; set; }
    internal int Transactions { get; private set; }
    private TaskCompletionSource<bool>? _writeGate;
    internal TaskCompletionSource WriteStarted { get; } = new();
    internal void HoldWrite() => _writeGate = new();
    private TaskCompletionSource<bool>? _heldWrite;
    internal void ReleaseWrite() => _heldWrite?.TrySetResult(true);
    public bool HasControlChannel => true;
    public bool IsPaused => false;
    public PlayerStatus Status => new();
    public event Action<bool>? PauseChanged { add { } remove { } }
    public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
    public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
    internal void Seed(ShaderGroup? group)
    {
        _shaders = group?.ResolveShaderPaths(ShaderGroupCatalog.ShaderRoot) ?? [];
        foreach (var pair in group?.Options ?? []) _values[pair.Key] = pair.Value;
    }
    public Task<bool> CommandAsync(IReadOnlyList<string> args, CancellationToken token)
    {
        if (args is ["show-text", _, _]) { Notices++; return Task.FromResult(true); }
        if (args.Count != 3 || args[0] != "set" || !_values.ContainsKey(args[1])) return Forbid<bool>();
        Writes.Add([.. args]);
        WriteStarted.TrySetResult();
        if (_writeGate is { } gate)
        {
            _writeGate = null;
            _heldWrite = gate;
            return AfterWait(gate);
        }
        return Task.FromResult(Apply());

        bool Apply()
        {
            if (Reject) return false;
            _values[args[1]] = args[2];
            if (args[1] == "glsl-shaders") _shaders = [];
            return true;
        }
        async Task<bool> AfterWait(TaskCompletionSource<bool> wait)
        {
            await wait.Task;
            return Apply();
        }
    }
    public Task<string?> GetTextAsync(string name, CancellationToken token)
    {
        if (name == "profile-list") { Transactions++; return Task.FromResult<string?>("[]"); }
        if (name.StartsWith("option-info/", StringComparison.Ordinal) && name.EndsWith("/default-value", StringComparison.Ordinal))
        {
            var key = name["option-info/".Length..^"/default-value".Length];
            if (_values.ContainsKey(key)) return Task.FromResult<string?>(ShaderGroupCatalog.NeutralOptions.Single(p => p.Key == key).Value);
        }
        if (_values.TryGetValue(name, out var value)) return Task.FromResult<string?>(value);
        return Forbid<string?>();
    }
    public Task<IReadOnlyList<string>?> GetStringListAsync(string name, CancellationToken token) => name == "glsl-shaders"
        ? Task.FromResult<IReadOnlyList<string>?>(_shaders) : Forbid<IReadOnlyList<string>?>();
    private Task<T> Forbid<T>()
    {
        Forbidden++;
        return Task.FromException<T>(new AssertionException("forbidden player/network/media boundary"));
    }
    public Task<double?> GetPositionAsync(CancellationToken token) => Forbid<double?>();
    public Task<double?> GetNumberAsync(string name, CancellationToken token) => Forbid<double?>();
    public Task SetPropertyAsync(string name, object? value, CancellationToken token) => Forbid<bool>();
    public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken token) => Forbid<IReadOnlyList<MpvTrack>>();
    public Task<PlaybackExit> WaitForExitAsync(CancellationToken token) => Forbid<PlaybackExit>();
    public Task StopAsync() => Forbid<bool>();
    public Task ShowMessageAsync(string text) => Forbid<bool>();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

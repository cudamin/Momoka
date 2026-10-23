using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

internal sealed class HandleFixture : IAsyncDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Process = typeof(LibMpvHandle).GetMethod("ProcessEvent", Private)!;
    internal NativeBoundary Native { get; } = new();
    internal LibMpvHandle Handle { get; }
    internal LibMpvLifetimeGate Gate => Field<LibMpvLifetimeGate>("_apiGate");
    internal long NativeGeneration => Gate.Generation;

    internal HandleFixture()
    {
        var signature = new PlaybackLaunchSignature(VideoPipelineKind.Standalone, [], [], [], []);
        Handle = new LibMpvHandle(Native.Context, null, signature, _ => signature);
        typeof(LibMpvHandle).GetField("_filmDefaults", Private)!.SetValue(Handle,
            InlineSwitch.PerFilmNames.ToDictionary(name => name,
                name => ShaderGroupCatalog.NeutralOptions.FirstOrDefault(pair => pair.Key == name).Value ?? "",
                StringComparer.Ordinal));
        Native.Shutdown = () => Dispatch(LibMpvNative.EventShutdown);
        Ready();
    }

    internal void Ready()
    {
        Dispatch(LibMpvNative.EventStartFile);
        Dispatch(LibMpvNative.EventFileLoaded);
    }

    internal bool Dispatch(int eventId) => Gate.Run(() =>
    {
        try { return (bool)Process.Invoke(Handle, [new LibMpvNative.MpvEvent { EventId = eventId }])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    });

    internal async Task SwapAsync(string file = "B")
    {
        var request = Request(file);
        Assert.True(Handle.CanSwapTo(request), "真实 handle 必须允许同句柄交接");
        var before = NativeGeneration;
        Handle.HandOver();
        Assert.Equal(before + 1, NativeGeneration, "HandOver 真实推进代次");
        Assert.True(await Handle.SwapToAsync(request, CancellationToken.None), "真实 Swap 必须成功");
        Assert.Equal(before + 2, NativeGeneration, "Swap 再推进自己的 native 代次");
        Ready();
        Assert.Equal(file, Native.File);
    }

    internal static PlaybackRequest Request(string file) => new()
    {
        MediaUrl = new Uri("https://r051.invalid/" + file),
        Title = file,
        ItemId = file,
        RunTimeTicks = TimeSpan.TicksPerSecond * 100
    };

    internal T Field<T>(string name) => (T)typeof(LibMpvHandle).GetField(name, Private)!.GetValue(Handle)!;

    public async ValueTask DisposeAsync()
    {
        await Handle.DisposeAsync();
        Assert.Equal(Native.NodeAllocations, Native.NodeFrees, "真实节点读取必须释放每个托管端点分配");
        Assert.Equal(0, Native.ForbiddenCalls, "禁止隐式扩张 native seam");
        NativeBoundary.Contexts.TryRemove(Native.Context, out _);
    }
}

// Only suspends argument enumeration. The real production CommandAsync/CommandForFileAsync
// copies this list itself, before native generation capture/RunFor. It never calls the gate here.
internal sealed class CommandPause : IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private int _entered;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Action? AtBoundary { get; set; }
    internal void Block()
    {
        if (Interlocked.Exchange(ref _entered, 1) != 0) return;
        AtBoundary?.Invoke();
        Entered.TrySetResult();
        if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new AssertionException("R051 命令入口停点未被释放");
    }
    internal void Release() => _release.Set();
    internal async Task WaitAsync() => await Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    public void Dispose() { Release(); _release.Dispose(); }
}

internal sealed class PausedArguments(IReadOnlyList<string> arguments, CommandPause pause) : IReadOnlyList<string>
{
    public int Count => arguments.Count;
    public string this[int index] => arguments[index];
    public IEnumerator<string> GetEnumerator()
    {
        pause.Block();
        foreach (var argument in arguments) yield return argument;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Transparent test decorator: delegates the optional generation capability when production
// implements it; otherwise delegates the untouched old CommandAsync (the red fixture path).
internal sealed class PausingHandle(HandleFixture owner) : IPlaybackHandle, IPlayerControl, IFileScopedPlayerControl
{
    private int _commands;
    internal int PauseAt { get; set; } = 1;
    internal CommandPause? Pause { get; set; }
    internal CommandPause? AfterSwap { get; set; }
    internal List<long> ExpectedGenerations { get; } = [];
    internal IPlaybackHandle Inner => owner.Handle;
    private IPlayerControl Control => owner.Handle;
    private IFileScopedPlayerControl? FileControl => (object)owner.Handle as IFileScopedPlayerControl;
    public long FileGeneration => FileControl?.FileGeneration ?? owner.NativeGeneration;
    public bool HasControlChannel => Inner.HasControlChannel;
    public bool? PictureInHostWindow => Inner.PictureInHostWindow;
    public bool IsPaused => Inner.IsPaused;
    public bool WasHandedOver => Inner.WasHandedOver;
    public PlayerStatus Status => Control.Status;
    public event Action<bool>? PauseChanged { add => Inner.PauseChanged += value; remove => Inner.PauseChanged -= value; }
    public event Action<PlayerStatus>? StatusChanged { add => Control.StatusChanged += value; remove => Control.StatusChanged -= value; }
    public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add => Control.TracksChanged += value; remove => Control.TracksChanged -= value; }
    public bool CanSwapTo(PlaybackRequest request) => Inner.CanSwapTo(request);
    public void HandOver() => Inner.HandOver();
    public async Task<bool> SwapToAsync(PlaybackRequest request, CancellationToken token)
    {
        var accepted = await Inner.SwapToAsync(request, token);
        AfterSwap?.Block();
        return accepted;
    }
    public Task<PlaybackExit> WaitForExitAsync(CancellationToken token) => Inner.WaitForExitAsync(token);
    public Task StopAsync() => Inner.StopAsync();
    public Task ShowMessageAsync(string text) => Inner.ShowMessageAsync(text);
    public Task SetPropertyAsync(string name, object? value, CancellationToken token) => Inner.SetPropertyAsync(name, value, token);
    public Task<double?> GetPositionAsync(CancellationToken token) => Inner.GetPositionAsync(token);
    public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken token) => Inner.GetTracksAsync(token);
    public Task<IReadOnlyList<SkipChapter>> GetChaptersAsync(CancellationToken token) => Inner.GetChaptersAsync(token);
    public Task<double?> GetNumberAsync(string name, CancellationToken token) => Inner.GetNumberAsync(name, token);
    public Task<string?> GetTextAsync(string name, CancellationToken token) => Inner.GetTextAsync(name, token);
    public Task<IReadOnlyList<string>?> GetStringListAsync(string name, CancellationToken token) => Inner.GetStringListAsync(name, token);
    public ValueTask DisposeAsync() => Inner.DisposeAsync();
    public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken token) => Control.CommandAsync(Wrap(arguments), token);
    public Task<bool> CommandForFileAsync(long expectedFileGeneration, IReadOnlyList<string> arguments, CancellationToken token)
    {
        ExpectedGenerations.Add(expectedFileGeneration);
        var wrapped = Wrap(arguments);
        return FileControl is { } scoped ? scoped.CommandForFileAsync(expectedFileGeneration, wrapped, token)
            : Control.CommandAsync(wrapped, token);
    }
    private IReadOnlyList<string> Wrap(IReadOnlyList<string> arguments) =>
        Interlocked.Increment(ref _commands) == PauseAt && Pause is { } pause ? new PausedArguments(arguments, pause) : arguments;
}

using System.Collections.Concurrent;
using Momoka.Playback;

namespace Momoka.Tests;

internal sealed class FakeBackend : IPlaybackBackend
{
    public string DisplayName => "R044 memory backend";
    public Func<PlaybackRequest, CancellationToken, Task<IPlaybackHandle>> Next { get; set; } =
        (_, _) => throw new AssertionException("未安排假后端");
    public ConcurrentQueue<PlaybackRequest> Starts { get; } = new();
    public ConcurrentQueue<CancellationToken> StartupTokens { get; } = new();
    public string? Validate() => null;
    public Task<IPlaybackHandle> StartAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        Starts.Enqueue(request);
        StartupTokens.Enqueue(cancellationToken);
        return Next(request, cancellationToken);
    }
}

internal sealed class FakeHandle : IPlaybackHandle, IPlayerControl, IPlayerHostMessages
{
    private readonly object _gate = new();
    private readonly List<TaskCompletionSource> _listening = [Signals.New(), Signals.New(), Signals.New()];
    private TaskCompletionSource<PlaybackExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<PlayerStatus>? _status;
    private Action<IReadOnlyList<MpvTrack>>? _tracks;
    private Action<string, string>? _host;
    private Action<bool>? _pause;
    private int _stopCount;
    private int _disposeCount;
    private int _swapCount;
    private int _handOverCount;
    public int StopCount => Volatile.Read(ref _stopCount);
    public int DisposeCount => Volatile.Read(ref _disposeCount);
    public int SwapCount => Volatile.Read(ref _swapCount);
    public int HandOverCount => Volatile.Read(ref _handOverCount);
    public bool HasControlChannel { get; init; } = true;
    public bool? PictureInHostWindow { get; init; } = true;
    public bool IsPaused { get; private set; }
    public bool Swappable { get; init; }
    public bool SwapSucceeds { get; init; } = true;
    public bool WasHandedOver { get; private set; }
    public bool HonorWaitCancellation { get; init; } = true;
    public double? Position { get; set; } = 5;
    public PlayerStatus Status { get; private set; } = new();
    public Func<string, CancellationToken, Task<string?>>? ReadText { get; set; }
    public Func<CancellationToken, Task<IReadOnlyList<MpvTrack>>>? ReadTracks { get; set; }
    public Func<CancellationToken, Task<bool>>? Swap { get; set; }
    public TaskCompletionSource? StopHold { get; set; }
    public TaskCompletionSource? DisposeHold { get; set; }
    public ManualResetEventSlim? SynchronousStopBarrier { get; set; }
    public bool ThrowOnStop { get; set; }
    public bool ThrowOnDispose { get; set; }
    public TaskCompletionSource StopEntered { get; } = Signals.New();
    public TaskCompletionSource DisposeEntered { get; } = Signals.New();
    public ConcurrentQueue<CancellationToken> RunningTokens { get; } = new();
    public Task Listening(int index) => _listening[index].Task;
    public event Action<PlayerStatus>? StatusChanged { add { lock (_gate) _status += value; } remove { lock (_gate) _status -= value; } }
    public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { lock (_gate) _tracks += value; } remove { lock (_gate) _tracks -= value; } }
    public event Action<string, string>? HostMessageReceived { add { lock (_gate) _host += value; } remove { lock (_gate) _host -= value; } }
    public event Action<bool>? PauseChanged { add { lock (_gate) _pause += value; } remove { lock (_gate) _pause -= value; } }

    public Action CaptureCallbacks()
    {
        lock (_gate)
        {
            var status = _status;
            var tracks = _tracks;
            var host = _host;
            var pause = _pause;
            return () =>
            {
                status?.Invoke(new PlayerStatus { Loaded = true, PictureStarted = true, Position = 99, Duration = 100 });
                tracks?.Invoke([new MpvTrack(9, "audio", "eng", "late", false, true)]);
                host?.Invoke("late-synthetic", "old");
                pause?.Invoke(true);
            };
        }
    }

    public void Publish(double seconds)
    {
        Action<PlayerStatus>? status;
        lock (_gate)
        {
            Status = new PlayerStatus { Loaded = true, PictureStarted = true, Position = seconds, Duration = 100 };
            status = _status;
        }
        status?.Invoke(Status);
    }

    public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var index = RunningTokens.Count;
            RunningTokens.Enqueue(cancellationToken);
            _listening[index].TrySetResult();
            return HonorWaitCancellation ? _exit.Task.WaitAsync(cancellationToken) : _exit.Task;
        }
    }

    public void End(PlaybackEndReason reason = PlaybackEndReason.UserQuit, double? position = 5)
    {
        lock (_gate) _exit.TrySetResult(new PlaybackExit(reason, position, 0, reason == PlaybackEndReason.Error ? "synthetic" : null));
    }

    public Task StopAsync()
    {
        Interlocked.Increment(ref _stopCount);
        StopEntered.TrySetResult();
        // A deliberately synchronous foreign preamble proves revocation does not join backend work.
        SynchronousStopBarrier?.Wait(TimeSpan.FromSeconds(4));
        return CompleteStopAsync();
    }

    private async Task CompleteStopAsync()
    {
        if (StopHold is { } hold) await hold.Task;
        End(PlaybackEndReason.Stopped, Position);
        if (ThrowOnStop) throw new IOException("synthetic stop failure");
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        DisposeEntered.TrySetResult();
        if (DisposeHold is { } hold) await hold.Task;
        if (ThrowOnDispose) throw new IOException("synthetic disposal failure");
    }

    public bool CanSwapTo(PlaybackRequest request) => Swappable;
    public void HandOver()
    {
        Interlocked.Increment(ref _handOverCount);
        WasHandedOver = true;
        End(PlaybackEndReason.Stopped, Position);
    }
    public async Task<bool> SwapToAsync(PlaybackRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _swapCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (Swap is { } swap && !await swap(cancellationToken) || !SwapSucceeds) return false;
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            WasHandedOver = false;
            _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return true;
    }

    public void ReleaseHolds()
    {
        SynchronousStopBarrier?.Set();
        StopHold?.TrySetResult();
        DisposeHold?.TrySetResult();
    }
    public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => Task.FromResult(Position);
    public Task ShowMessageAsync(string text) => Task.CompletedTask;
    public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) =>
        ReadTracks?.Invoke(cancellationToken) ?? Task.FromResult<IReadOnlyList<MpvTrack>>([]);
    public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(null);
    public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) =>
        ReadText?.Invoke(name, cancellationToken) ?? Task.FromResult<string?>("no");
    public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => Task.FromResult(true);
}

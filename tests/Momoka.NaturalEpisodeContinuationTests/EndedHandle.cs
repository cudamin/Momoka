using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Tests;

// Only a fallback backend boundary. It never initializes mpv, plays media or performs HTTP.
internal sealed class EndedHandle : IPlaybackHandle
{
    private readonly TaskCompletionSource<PlaybackExit> _exit = new();
    internal bool Stopped { get; private set; }
    internal int Disposes { get; private set; }
    public bool HasControlChannel => false;
    public bool IsPaused => false;
    public event Action<bool>? PauseChanged { add { } remove { } }
    internal void End() => _exit.TrySetResult(new PlaybackExit(PlaybackEndReason.EndOfFile, 100, 0, null));
    public Task<PlaybackExit> WaitForExitAsync(CancellationToken token) => _exit.Task.WaitAsync(token);
    public Task StopAsync()
    {
        Stopped = true;
        _exit.TrySetResult(new PlaybackExit(PlaybackEndReason.Stopped, 100, 0, null));
        return Task.CompletedTask;
    }
    public Task<double?> GetPositionAsync(CancellationToken token) => Task.FromResult<double?>(100);
    public Task<string?> GetTextAsync(string name, CancellationToken token) => Task.FromResult<string?>(null);
    public Task<double?> GetNumberAsync(string name, CancellationToken token) => Task.FromResult<double?>(null);
    public Task SetPropertyAsync(string name, object? value, CancellationToken token) => Task.CompletedTask;
    public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
    public Task ShowMessageAsync(string text) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
}

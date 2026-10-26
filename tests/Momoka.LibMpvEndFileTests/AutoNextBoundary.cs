using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Playback;

namespace Momoka.Tests;

// Only the actual VM auto-next decision is source-linked. No real VM, UI dispatch or window.
internal sealed partial class AutoNextBoundary : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    internal AppSettings Settings { get; } = new();
    internal bool HasAggregateQueue { get; set; }
    internal List<EmbyItem> Episodes { get; } = [];
    internal int Decisions { get; private set; }
    internal EpisodeDestination? Next(PlaybackResult result, EmbyItem item)
    {
        Decisions++;
        return NextEpisodeToAutoPlay(result, item);
    }
    internal void Cancel() => _lifetime.Cancel();
    public void Dispose() => _lifetime.Dispose();
}

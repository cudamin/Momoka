using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

// No whole VM/UI facade: only values consumed by the extracted real reload/lifetime methods.
internal partial class PageViewModel : IDisposable
{
    private readonly LoadGeneration _loads = new();
    internal bool Busy { get; set; }
    internal bool IsReady { get; set; }
    internal bool NoticeOpen { get; set; }
    internal List<string> Reports { get; } = [];
    protected void Report(string what, Exception error) => Reports.Add(what);
    protected void Notify(string? title, string? message, InfoBarSeverity severity) => Reports.Add(title ?? "");
    public void Dispose() => _loads.Dispose();
}

internal sealed partial class DetailViewModel : PageViewModel
{
    private const string Category = "ui";
    private bool _active;
    private bool _actionsReady;
    // The extracted Cancel path also drops image diagnostics/cache tickets; this fixture has no images.
    internal object? _heroTiming;
    internal object? _heroCacheTicket;
    // Movies have no episode shelf; retain the real Cancel call without constructing a UI collection.
    private EpisodeShelfBoundary? EpisodeShelf => null;
    private EmbySessionScope? _scope;
    private EmbyItem? _seed;
    private readonly RemovedMedia _removed = new();
    internal bool FixtureDeleted { get; private set; }
    internal void FixtureRemove(EmbyItem item) => _removed.Remember(item);
    private FixtureSettings? _settings;
    private long _lifetime;
    private readonly CancellationTokenSource? _versionLoad = null;
    private readonly CancellationTokenSource? _art = null;
    private readonly IShellActions? _actions = null;
    private readonly IReadOnlyList<MediaVersion> _versions = [];
    internal EmbyItem? PlayTarget { get; private set; }
    private void ShowTarget(EmbyItem? target) => PlayTarget = target;
    internal EmbyItem? Applied { get; private set; }
    internal bool FixtureActionsReady => _actionsReady;

    internal void FixtureAttach(EmbySessionScope scope, EmbyItem item)
    {
        _active = true;
        _actionsReady = true;
        _scope = scope;
        _seed = item;
        _settings = new(new AppSettings());
        _settings.Settings.Playback.MediaSourceOrder = MediaSourceOrder.Default;
        Applied = item; // Initial displayed value, not a replacement for the real reload algorithm.
    }

    // Rendering/other media-type loads are outside this movie-only contract and cannot send requests.
    private void Preview(EmbyItem seed) { }
    // The real LoadAsync chooses this terminal presentation boundary before issuing any GET.
    private void ClearDeletedSubject()
    {
        Cancel();
        _seed = Applied = PlayTarget = null;
        FixtureDeleted = true;
    }
    private void Apply(EmbyItem item)
    {
        Applied = item;
        _actionsReady = true;
    }
    private static Task LoadSimilarAsync(EmbyItem item, CancellationToken token) => Task.CompletedTask;
    private static Task LoadSeriesAsync(EmbyItem item, CancellationToken token) => throw new InvalidOperationException("Movie-only fixture");
    private static Task LoadSeasonSeriesAsync(EmbyItem item, CancellationToken token) => throw new InvalidOperationException("Movie-only fixture");
    private static Task LoadSeasonShelfAsync(EmbyItem item, CancellationToken token) => throw new InvalidOperationException("Movie-only fixture");
    private static Task LoadEpisodesAsync(EmbyItem? item, bool retarget, CancellationToken token) => throw new InvalidOperationException("Movie-only fixture");
    private void BusyChanged() { }
    private void AnnounceAffordances() { }
    private sealed record FixtureSettings(AppSettings Settings);
    private sealed class EpisodeShelfBoundary
    {
        public void Clear() => throw new InvalidOperationException("Movie-only fixture has no episode shelf");
    }
}

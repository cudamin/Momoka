using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Controls;

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
    private EmbySessionScope? _scope;
    private EmbyItem? _seed;
    private FixtureSettings? _settings;
    private bool _switchingSeason;
    private long _lifetime;
    private readonly CancellationTokenSource? _versionLoad = null;
    private readonly CancellationTokenSource? _art = null;
    private readonly IShellActions? _actions = null;
    private readonly IReadOnlyList<MediaVersion> _versions = [];
    internal EmbyItem? PlayTarget { get; private set; }
    private void ShowTarget(EmbyItem? target) => PlayTarget = target;
    internal EmbyItem? Applied { get; private set; }
    internal bool FixtureSwitchingSeason => _switchingSeason;

    internal void FixtureAttach(EmbySessionScope scope, EmbyItem item)
    {
        _active = true;
        _scope = scope;
        _seed = item;
        _settings = new(new AppSettings());
        _settings.Settings.Playback.MediaSourceOrder = MediaSourceOrder.Default;
        Applied = item; // Initial displayed value, not a replacement for the real reload algorithm.
    }

    // Rendering/other media-type loads are outside this movie-only contract and cannot send requests.
    private void Preview(EmbyItem seed) { }
    private void Apply(EmbyItem item) => Applied = item;
    private static Task LoadSimilarAsync(EmbyItem item, CancellationToken token) => Task.CompletedTask;
    private static Task LoadSeriesAsync(EmbyItem item, CancellationToken token) => throw new InvalidOperationException("Movie-only fixture");
    private static Task LoadEpisodesAsync(EmbyItem? item, bool retarget, CancellationToken token) => throw new InvalidOperationException("Movie-only fixture");
    private void BusyChanged() { }
    private void AnnounceAffordances() { }
    private sealed record FixtureSettings(AppSettings Settings);
}

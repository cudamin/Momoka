using System.Collections.ObjectModel;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;

namespace DanmakuReview;

internal static class Program
{
    private static int Main()
    {
        foreach (var switched in new[] { false, true })
            Momoka.Tests.TestHarness.Test($"danmaku source late search switched={switched}", () =>
            {
                using var context = new QueueContext();
                SynchronizationContext.SetSynchronizationContext(context);
                var endpoint = new PanelEndpoint();
                var panel = new Panel(endpoint);
                panel.ChooseSource(DanmakuSource.DanmuApi);
                panel.StartSearch();
                if (switched) panel.ChooseSource(DanmakuSource.DandanPlay);
                endpoint.CompleteSearch(new DandanPlayEpisode(230001, "anonymous custom result", "episode 1"));
                context.Drain();
                Momoka.Tests.Assert.Equal(switched ? 0 : 1, panel.Count);
                Momoka.Tests.Assert.True(panel.SearchEnabled);
                if (!switched)
                {
                    panel.LoadFirst();
                    Momoka.Tests.Assert.Equal(DanmakuSource.DanmuApi, endpoint.LoadSource!.Value);
                    Momoka.Tests.Assert.Equal(230001L, endpoint.LoadedId!.Value);
                }
            });
        Momoka.Tests.TestHarness.Test("danmaku rejects stale displayed instance result", () =>
        {
            using var context = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(context);
            var endpoint = new PanelEndpoint();
            var panel = new Panel(endpoint);
            panel.StartSearch();
            endpoint.CompleteSearch(new DandanPlayEpisode(230001, "old instance", "episode 1"));
            context.Drain();
            endpoint.DanmakuConnectionVersion++;
            panel.LoadFirst();
            Momoka.Tests.Assert.Null(endpoint.LoadSource);
        });
        Momoka.Tests.TestHarness.Test("danmaku rejects in-flight changed instance response", () =>
        {
            using var context = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(context);
            var endpoint = new PanelEndpoint();
            var panel = new Panel(endpoint);
            panel.StartSearch();
            endpoint.DanmakuConnectionVersion++;
            endpoint.CompleteSearch(new DandanPlayEpisode(230001, "old instance", "episode 1"));
            context.Drain();
            Momoka.Tests.Assert.Equal(0, panel.Count);
            Momoka.Tests.Assert.True(panel.SearchEnabled);
        });
        foreach (var change in new[] { "related", "address", "token" })
        foreach (var dandan in new[] { false, true })
            Momoka.Tests.TestHarness.Test($"danmaku reload identity {change} dandan={dandan}", () =>
            {
                var settings = Settings();
                settings.Danmaku.Source = dandan ? DanmakuSourceMode.Both : DanmakuSourceMode.DanmuApi;
                var host = new Host(settings);
                host.Seed(new DandanPlayEpisode(10002, "anonymous A result", "episode 1"));
                if (dandan) host.SeedDandan(new DandanPlayEpisode(20001, "dandan result", "episode 1"));
                if (change == "address") settings.Danmaku.DanmuApiBaseUrl = "https://source-b.invalid/";
                else if (change == "token") settings.Danmaku.ProtectedDanmuApiToken = "different anonymous token";
                else settings.Danmaku.WithRelated = !settings.Danmaku.WithRelated;
                host.Change();
                Momoka.Tests.Assert.Equal(change == "related", host.LoadSelections.Any(x => x.Source == DanmakuSource.DanmuApi));
                Momoka.Tests.Assert.Equal(dandan, host.LoadSelections.Any(x => x.Source == DanmakuSource.DandanPlay));
                Momoka.Tests.Assert.Equal(change != "related" && !dandan ? 1 : 0, host.AutoMatches);
                Momoka.Tests.Assert.Equal((0, 0, "", ""), host.ResetSnapshot);
            });
        foreach (var pipeline in new[] { VideoPipelineKind.Integrated, VideoPipelineKind.Standalone })
        foreach (var source in new[] { DanmakuSourceMode.DandanPlay, DanmakuSourceMode.DanmuApi, DanmakuSourceMode.Both })
            Momoka.Tests.TestHarness.Test($"danmaku input preference {pipeline} {source}", () =>
            {
                var settings = Settings();
                settings.Danmaku.Source = source;
                settings.Mpv.Pipeline = pipeline;
                settings.Danmaku.ShowInput = true;
                Momoka.Tests.Assert.Equal(DanmakuSourceRules.CanSend(source), new SettingsRows(settings).Labels().Contains("显示弹幕输入栏"));
                settings.Danmaku.Show = false;
                Momoka.Tests.Assert.False(new SettingsRows(settings).Labels().Contains("显示弹幕输入栏"));
                settings.Danmaku.Source = DanmakuSourceMode.DandanPlay;
                settings.Danmaku.Show = true;
                Momoka.Tests.Assert.True(new SettingsRows(settings).Labels().Contains("显示弹幕输入栏"));
                Momoka.Tests.Assert.True(settings.Danmaku.ShowInput);
            });
        return Momoka.Tests.TestHarness.Run();
    }

    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.Danmaku.Enabled = true;
        settings.Danmaku.AutoMatch = true;
        settings.Danmaku.DanmuApiBaseUrl = "https://source-a.invalid/";
        return settings;
    }
}

internal sealed class QueueContext : SynchronizationContext, IDisposable
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
    internal void Drain() { while (_queue.TryDequeue(out var work)) work.Callback(work.State); }
    public void Dispose() => SynchronizationContext.SetSynchronizationContext(null);
}

internal enum Visibility { Visible, Collapsed }
internal sealed class RoutedEventArgs : EventArgs;
internal sealed class SelectionChangedEventArgs : EventArgs;
internal sealed class Control
{
    public bool IsEnabled { get; set; } = true;
    public Visibility Visibility { get; set; }
    public string Text { get; set; } = "anonymous title";
    public object? Content { get; set; }
    public object? SelectedItem { get; set; }
}

internal sealed class PanelEndpoint
{
    private readonly TaskCompletionSource<DandanPlaySearchResult> _pending = new();
    internal int DanmakuConnectionVersion { get; set; }
    internal DanmakuSource SearchSource { get; private set; }
    internal DanmakuSource? LoadSource { get; private set; }
    internal long? LoadedId { get; private set; }
    internal int Cancellations { get; private set; }
    internal Task<DandanPlaySearchResult> SearchDanmakuAsync(string title, string episode, DanmakuSource source)
    { SearchSource = source; return _pending.Task; }
    internal Task LoadDanmakuAsync(DandanPlayEpisode episode, DanmakuSource source)
    { LoadSource = source; LoadedId = episode.EpisodeId; return Task.CompletedTask; }
    internal void CompleteSearch(DandanPlayEpisode episode) => _pending.SetResult(new([episode], false));
    internal void CancelDanmakuSearch() => Cancellations++;
}

internal sealed partial class Panel(PanelEndpoint endpoint)
{
    private readonly PanelEndpoint? _viewModel = endpoint;
    private readonly ObservableCollection<DandanPlayEpisode> _results = [];
    private readonly bool _closed = false;
    private readonly bool _sourceSeeding = false;
    private int _searchRequest;
    private DanmakuSource _searchSource;
    private DanmakuSource? _resultsSource;
    private int _resultsConnectionVersion;
    private readonly Control SearchButton = new(), TitleBox = new(), EpisodeBox = new(), Results = new(), LoadButton = new(), SourceBox = new();
    private bool Current() => !_closed;
    internal int Count => _results.Count;
    internal bool SearchEnabled => SearchButton.IsEnabled;
    internal int Version => _searchRequest;
    internal void StartSearch() => OnSearch(this, new());
    internal void ChooseSource(DanmakuSource source)
    { SourceBox.SelectedItem = new DanmakuSourceOption(source.ToString(), source); OnSourceChanged(this, new()); }
    internal void LoadFirst()
    { Results.SelectedItem = _results.Single(); OnLoad(this, new()); }
}

internal sealed partial class Host(AppSettings settings)
{
    private AppSettings Settings { get; } = settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly EmbyItem? _nowPlaying = new() { Type = EmbyItemType.Movie, Name = "anonymous movie" };
    private string _danmakuConnection = "";
    private string _danmuApiConnection = "";
    internal int DanmakuConnectionVersion { get; private set; }
    private DandanPlayEpisode? _danmakuEpisode;
    private DandanPlayEpisode? _danmakuDanmuApiEpisode;
    private IReadOnlyList<DanmakuComment> _danmakuComments = [];
    private bool _danmakuFetched;
    private int _danmakuDandanCount, _danmakuDanmuApiCount;
    private string _danmakuLabel = "", _danmakuWarning = "", _danmakuSourceSummary = "";
    private long _danmakuTransfer;
    private bool DanmakuAvailable => DanmakuSourceRules.IsAvailable(Settings);
    private string DanmakuStatus { get; set; } = "";
    private string DefaultDanmakuStatus => "anonymous default";
    internal IReadOnlyList<(DanmakuSource Source, DandanPlayEpisode Episode)> LoadSelections { get; private set; } = [];
    internal string? LoadEndpoint { get; private set; }
    internal int AutoMatches { get; private set; }
    internal (int DandanCount, int CustomCount, string Label, string Warning) ResetSnapshot =>
        (_danmakuDandanCount, _danmakuDanmuApiCount, _danmakuLabel, _danmakuWarning);
    internal void Seed(DandanPlayEpisode episode)
    {
        _danmakuDanmuApiEpisode = episode;
        _danmakuDandanCount = 2;
        _danmakuDanmuApiCount = 3;
        _danmakuLabel = "previous label";
        _danmakuWarning = "previous warning";
        _danmakuConnection = DanmakuConnection();
        _danmuApiConnection = DanmuApiConnection();
    }
    internal void SeedDandan(DandanPlayEpisode episode) => _danmakuEpisode = episode;
    internal void Change() => DanmakuSettingsChanged();
    private void RefreshDanmakuProperties() { }
    private void CancelDanmakuRequests() { }
    private void ResetDanmakuInput() { }
    private Task ClearDanmakuAsync() => Task.CompletedTask;
    private Task PingDanmakuAsync() => Task.CompletedTask;
    private Task LoadSourcesAsync(IReadOnlyList<(DanmakuSource Source, DandanPlayEpisode Episode)> selections)
    { LoadSelections = selections; LoadEndpoint = Settings.Danmaku.DanmuApiBaseUrl; return Task.CompletedTask; }
    private Task AutoMatchDanmakuAsync(EmbyItem item) { AutoMatches++; return Task.CompletedTask; }
    private Task ScheduleDanmakuPushAsync() => Task.CompletedTask;
    private Task PushDanmakuVisibilityAsync() => Task.CompletedTask;
}

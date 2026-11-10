using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

// The console harness has no UI synchronization context; blocking stays at this test boundary.
#pragma warning disable RS0030

namespace DanmakuReview;

internal static class BilibiliPlayerTests
{
    internal static void Register()
    {
        Test("Existing two source panel keeps manually edited search text on source switch", () =>
        {
            var panel = new Panel(new PanelEndpoint());
            panel.SetQuery("edited manual keyword", "5");
            panel.ChooseSource(DanmakuSource.DanmuApi);
            Momoka.Tests.Assert.Equal("edited manual keyword", panel.Query);
            Momoka.Tests.Assert.Equal("5", panel.EpisodeQuery);
        });
        Test("Bilibili panel accepts manual links and clears local metadata on source change", () =>
        {
            var endpoint = new PanelEndpoint();
            var panel = new Panel(endpoint);
            panel.SeedForTest();
            panel.SetQuery("private local title", "3");
            endpoint.Sources = [new("哔哩哔哩", DanmakuSource.Bilibili)];
            endpoint.DanmakuConnectionVersion++;
            panel.ConnectionChanged();
            Momoka.Tests.Assert.Equal("", panel.Query);
            Momoka.Tests.Assert.Equal("", panel.EpisodeQuery);
            Momoka.Tests.Assert.False(panel.EpisodeVisible);
            Momoka.Tests.Assert.True(panel.QueryMaximum >= 2048);
            Momoka.Tests.Assert.Contains("BV", panel.QueryPlaceholder);
        });
        Test("Bilibili panel sends only manually entered query and ignores hidden episode value", () =>
        {
            using var context = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(context);
            var endpoint = new PanelEndpoint { Sources = [new("哔哩哔哩", DanmakuSource.Bilibili)] };
            var panel = new Panel(endpoint);
            panel.SeedForTest();
            panel.SetQuery("https://www.bilibili.com/video/BV1fixture", "3");
            panel.StartSearch();
            Momoka.Tests.Assert.Equal(DanmakuSource.Bilibili, endpoint.SearchSource);
            Momoka.Tests.Assert.Equal("https://www.bilibili.com/video/BV1fixture", endpoint.Query);
            Momoka.Tests.Assert.Equal("", endpoint.Episode);
            panel.Close();
            endpoint.CompleteSearch(new(7, "manual work", "part two"));
            context.Drain();
        });
        Test("Bilibili default search reaches configured source without sending local title", () =>
        {
            using var host = new PlaybackHost(Settings());
            Momoka.Tests.Assert.Equal(DanmakuSource.Bilibili, host.SearchSource);
            Momoka.Tests.Assert.Equal("", host.DanmakuSearchTitle);
            Momoka.Tests.Assert.Equal("", host.DanmakuSearchEpisode);
        });
        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode })
            Test("Bilibili auto match keeps manual selection and no external calls " + type, () =>
            {
                using var host = new PlaybackHost(Settings());
                host.MatchAsync(new() { Name = "private local title", SeriesName = "private series", Type = type,
                    ParentIndexNumber = 2, IndexNumber = 3 }).GetAwaiter().GetResult();
                Momoka.Tests.Assert.Equal(0, host.ExternalCalls);
                Momoka.Tests.Assert.Contains("手动", host.DanmakuStatus);
                Momoka.Tests.Assert.Contains("哔哩哔哩", host.DanmakuStatus);
            });
        Test("Bilibili successful load is counted separately and cannot establish private send target", () =>
        {
            using var host = new PlaybackHost(Settings());
            host.LoadDanmakuAsync(new(7, "manual work", "part two"), DanmakuSource.Bilibili).GetAwaiter().GetResult();
            Momoka.Tests.Assert.True(host.Fetched);
            Momoka.Tests.Assert.Equal(1, host.Applied.Count);
            Momoka.Tests.Assert.Equal(0, host.PrivateCount);
            Momoka.Tests.Assert.Equal(0, host.CustomCount);
            Momoka.Tests.Assert.False(host.HasPrivateTarget);
            Momoka.Tests.Assert.False(host.DanmakuSendSupported);
            Momoka.Tests.Assert.Contains("哔哩哔哩 1 条", host.DanmakuStatus);
            Momoka.Tests.Assert.Equal(DanmakuSource.Bilibili, host.Client.Source);
            Momoka.Tests.Assert.False(host.Client.WithRelated);
        });
        foreach (var change in new[] { "related", "cookie", "account" })
            Test("Bilibili selected cid is scoped to account connection " + change, () =>
            {
                var settings = Settings();
                var host = new Host(settings);
                host.SeedBilibili(new(7, "manual work", "part two"));
                var originalVersion = host.DanmakuConnectionVersion;
                if (change == "cookie") settings.Danmaku.ProtectedBilibiliCookie = "different protected cookie";
                else if (change == "account") settings.Danmaku.BilibiliUserId = 43;
                else settings.Danmaku.WithRelated = !settings.Danmaku.WithRelated;
                host.Change();
                Momoka.Tests.Assert.True(host.DanmakuConnectionVersion > originalVersion);
                Momoka.Tests.Assert.Equal(change == "related", host.LoadSelections.Any(x => x.Source == DanmakuSource.Bilibili));
                Momoka.Tests.Assert.False(host.LoadSelections.Any(x => x.Source == DanmakuSource.DandanPlay));
                Momoka.Tests.Assert.False(host.LoadSelections.Any(x => x.Source == DanmakuSource.DanmuApi));
            });
        foreach (var displayed in new[] { false, true })
            Test("Bilibili panel rejects old account search result displayed=" + displayed, () =>
            {
                using var context = new QueueContext();
                SynchronizationContext.SetSynchronizationContext(context);
                var endpoint = new PanelEndpoint { Sources = [new("哔哩哔哩", DanmakuSource.Bilibili)] };
                var panel = new Panel(endpoint);
                panel.SeedForTest();
                panel.SetQuery("ep42", "");
                panel.StartSearch();
                if (!displayed) endpoint.DanmakuConnectionVersion++;
                endpoint.CompleteSearch(new(7, "manual work", "part two"));
                context.Drain();
                if (displayed)
                {
                    endpoint.DanmakuConnectionVersion++;
                    panel.LoadFirst();
                    Momoka.Tests.Assert.Null(endpoint.LoadSource);
                }
                else Momoka.Tests.Assert.Equal(0, panel.Count);
                panel.Close();
            });
        foreach (var switched in new[] { false, true })
            Test("Bilibili pending load rejects changed playback identity " + switched, () =>
            {
                using var host = new PlaybackHost(Settings());
                host.Client.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var load = host.LoadDanmakuAsync(new(7, "manual work", "part two"), DanmakuSource.Bilibili);
                if (switched) host.Generation++;
                host.Client.Pending.SetResult([new(1, 1, 0xFFFFFF, "fixture")]);
                load.GetAwaiter().GetResult();
                Momoka.Tests.Assert.Equal(!switched, host.Fetched);
                Momoka.Tests.Assert.Equal(switched ? 0 : 1, host.Applied.Count);
            });
    }

    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.Danmaku.Enabled = true;
        settings.Danmaku.Source = DanmakuSourceMode.Bilibili;
        settings.Danmaku.AutoMatch = true;
        settings.Danmaku.WithRelated = true;
        settings.Danmaku.ProtectedBilibiliCookie = "protected anonymous fixture";
        settings.Danmaku.BilibiliUserId = 42;
        return settings;
    }
}

// Methods are extracted verbatim from production. Only playback and transport boundaries are substitutes.
internal sealed partial class PlaybackHost(AppSettings settings) : IDisposable
{
    private AppSettings Settings { get; } = settings;
    private readonly EmbyItem _nowPlaying = new() { Type = EmbyItemType.Episode, SeriesName = "private local series", IndexNumber = 3, ParentIndexNumber = 1 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PlaybackTransport _dandanPlay = new();
    private readonly PlaybackBoundary _playback = new();
    private CancellationTokenSource? _danmakuSearch, _danmakuLoad;
    private int _danmakuSearchRequest, _danmakuRequest, _danmakuTransfer;
    private readonly int _danmakuSerial = 1;
    private IReadOnlyList<DanmakuComment> _danmakuComments = [];
    private DandanPlayEpisode? _danmakuEpisode, _danmakuDanmuApiEpisode;
    private DandanPlayEpisode? _danmakuBilibiliEpisode;
    private bool _danmakuFetched;
    private int _danmakuDandanCount, _danmakuDanmuApiCount;
    private int _danmakuBilibiliCount;
    private string _danmakuLabel = "", _danmakuWarning = "", _danmakuSourceSummary = "";
    internal string DanmakuStatus { get; private set; } = "";
    private bool DanmakuAvailable => DanmakuSourceRules.IsAvailable(Settings);
    internal DanmakuSource SearchSource => DefaultSearchSource();
    internal bool Fetched => _danmakuFetched;
    internal IReadOnlyList<DanmakuComment> Applied => _danmakuComments;
    internal int PrivateCount => _danmakuDandanCount;
    internal int CustomCount => _danmakuDanmuApiCount;
    internal bool HasPrivateTarget => _danmakuEpisode is not null;
    internal PlaybackTransport Client => _dandanPlay;
    internal int ExternalCalls { get; private set; }
    internal int Generation { get; set; }
    internal Task MatchAsync(EmbyItem item) => AutoMatchDanmakuAsync(item);
    private int CaptureInteraction() => Generation;
    private bool IsCurrentPlayback(int context) => context == Generation;
    private void CancelDanmakuSearch() { _danmakuSearchRequest++; _danmakuSearch?.Cancel(); }
    private void ResetDanmakuInput(bool preserveDraft = false) { }
    private void RefreshDanmakuInput() { }
    private Task ClearDanmakuAsync() => Task.CompletedTask;
    private Task PingDanmakuAsync() => Task.CompletedTask;
    private Task PushDanmakuAsync() => Task.CompletedTask;
    private Task<int?> DanmakuSeriesYearAsync(EmbyItem item, CancellationToken token) { ExternalCalls++; return Task.FromResult<int?>(null); }
    private Task<(DandanPlaySearchResult Result, string Error)> SearchSourceAsync(string title, string? episode, DanmakuSource source, CancellationToken token)
    { ExternalCalls++; return Task.FromResult((DandanPlaySearchResult.Empty, "")); }
    public void Dispose() { _lifetime.Cancel(); _danmakuSearch?.Dispose(); _danmakuLoad?.Dispose(); _lifetime.Dispose(); }
    private sealed class PlaybackBoundary { internal bool IsPlaying => true; }
}

internal sealed class PlaybackTransport
{
    internal DanmakuSource? Source { get; private set; }
    internal bool WithRelated { get; private set; }
    internal TaskCompletionSource<IReadOnlyList<DanmakuComment>>? Pending { get; set; }
    internal Task<IReadOnlyList<DanmakuComment>> GetCommentsAsync(long id, bool withRelated, DanmakuSource source, CancellationToken token)
    { Source = source; WithRelated = withRelated; return Pending?.Task ?? Task.FromResult<IReadOnlyList<DanmakuComment>>([new(1, 1, 0xFFFFFF, "fixture")]); }
    internal Task<DandanPlayEpisode?> MatchAsync(string fileName, CancellationToken token) => Task.FromResult<DandanPlayEpisode?>(null);
}

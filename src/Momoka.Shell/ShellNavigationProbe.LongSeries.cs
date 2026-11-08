using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Real Frame/ItemsRepeater access against 2000 anonymous episodes, without playback.</summary>
    private async Task PerformanceLongSeriesAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var show = new EmbyItem { Id = "p3-series", Name = "2000 集匿名长剧", Type = EmbyItemType.Series };
        var seasons = Enumerable.Range(0, 20).Select(number => new EmbyItem
        {
            Id = "p3-season-" + number,
            Name = number == 0 ? "特别篇" : "第 " + number + " 季",
            Type = EmbyItemType.Season,
            SeriesId = show.Id,
            SeriesName = show.Name,
            IndexNumber = number
        }).ToArray();
        var episodes = Enumerable.Range(0, 2000).Select(index => LongEpisode(index, show.Id,
            seasons[index / 100])).ToArray();
        var longShow = new EmbyItem { Id = "p3-long-season-series", Name = "匿名单季长剧", Type = EmbyItemType.Series };
        var longSeason = new EmbyItem
        {
            Id = "p3-long-season",
            Name = "2000 集长季",
            Type = EmbyItemType.Season,
            SeriesId = longShow.Id,
            SeriesName = longShow.Name,
            IndexNumber = 1
        };
        var seasonEpisodes = Enumerable.Range(0, 2000)
            .Select(index => LongEpisode(index, longShow.Id, longSeason)).ToArray();
        var known = new[] { show, longShow, longSeason }.Concat(seasons).Concat(episodes)
            .Concat(seasonEpisodes).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var reads = new List<object>();
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "长剧性能探针禁止服务器写入或播放");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var marker = path.IndexOf("/Items/", StringComparison.Ordinal);
            if (marker >= 0 && known.TryGetValue(path[(marker + 7)..], out var item)) return Task.FromResult(Json(item));
            if (path.EndsWith("/Seasons", StringComparison.Ordinal))
                return Task.FromResult(Json(new ItemsResult
                { Items = [.. path.Contains(longShow.Id, StringComparison.Ordinal) ? [longSeason] : seasons] }));
            if (path.EndsWith("/Episodes", StringComparison.Ordinal))
            {
                var seasonId = Query(request, "SeasonId");
                var values = path.Contains(longShow.Id, StringComparison.Ordinal) ? seasonEpisodes
                    : seasonId is null ? episodes : episodes.Where(episode => episode.SeasonId == seasonId).ToArray();
                reads.Add(new
                {
                    series = path.Contains(longShow.Id, StringComparison.Ordinal) ? "long-season" : "series",
                    seasonId,
                    count = values.Length
                });
                // Deliberately reverse the whole series. The full business index must use air order,
                // while an actual season keeps the server's sequence without a background re-sort.
                return Task.FromResult(Json(new ItemsResult
                { Items = [.. seasonId is null ? values.Reverse() : values], TotalRecordCount = values.Length }));
            }
            if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Task.FromResult(Items(0, 0));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var frame = new Frame();
        _root.Children.Add(frame);
        var evidence = new List<object>();
        var timingPath = Path.Combine(_options.Paths.LogDirectory, "performance-long-series-timing.ndjson");
        using var sink = new NavigationTimingFileSink(timingPath);
        NavigationTiming.Start(new NavigationTimingRecorder(sink));
        var runs = new List<string>();
        try
        {
            ResizeProbeClient(1280, 900);
            var series = await Enter(show);
            var view = series.ViewModel;
            Require(view.Episodes.Count == 2000 && view.EpisodeShelf is { Cards.Count: 2000 }, "长剧全量业务索引或卡片数量缺失");
            Require(view.Episodes[0].Id == "p3-series-episode-0" && view.Episodes[1500].Id == "p3-series-episode-1500",
                "全剧没有保持播出顺序的全局索引");
            Require(view.PlayTarget?.Id == "p3-series-episode-1500", "全剧最早未看集落点改变");
            Require(view.SeasonShelf?.Cards[0].Item.Id == seasons[0].Id, "S00 封面入口丢失");
            var strip = Get<ShelfStrip>(series, "EpisodeStrip");
            var repeater = Get<ItemsRepeater>(strip, "Repeater");
            var cards = view.EpisodeShelf!.Cards;
            Require(repeater.ItemsSourceView?.Count == 2000, "真实WinRT ItemsSourceView没有完整Count");
            Require(cards.MaterializedCount < 160, $"首屏创建了过多集卡：{cards.MaterializedCount}/2000");
            evidence.Add(new { phase = "series-first", fullCount = cards.Count, materialized = cards.MaterializedCount });

            ScrollLongEpisodePanel(series);
            await LayoutAsync(series);
            var deepWatch = Stopwatch.StartNew();
            strip.JumpTo(1500);
            await LayoutAsync(series);
            await UntilAsync(() => Descendants(strip).OfType<PosterCard>().Any(card => card.Card?.Item.Id == episodes[1500].Id));
            deepWatch.Stop();
            Require(cards.MaterializedCount < 200, $"跳到第1500项创建了整个前缀：{cards.MaterializedCount}/2000");
            Require(repeater.ItemsSourceView!.GetAt(1500) is CardItem { Item.Id: "p3-series-episode-1500" },
                "真实WinRT GetAt没有按绝对索引取卡");
            evidence.Add(new
            {
                phase = "series-deep",
                globalIndex = 1500,
                fullCount = cards.Count,
                materialized = cards.MaterializedCount,
                layoutMs = deepWatch.Elapsed.TotalMilliseconds
            });
            await SaveProbeScreenAsync("performance-long-series-deep-1280-screen.png");

            // 合并后的详情排序会重挂同一面板。深处的索引和滚动状态不能因此退回首屏或物化整个前缀。
            var originalLayout = fixture.Settings.Ui.SeriesCards;
            fixture.Settings.Ui.SeriesCards = DetailLayout.For(fixture.Settings.Ui, show.Type).Reverse()
                .Select(card => new Momoka.Configuration.DetailCardSetting { Key = card.Key, Visible = card.Visible }).ToList();
            var episodePreference = fixture.Settings.Ui.SeriesCards.Single(card => card.Key == "episodes");
            var scroller = Get<ScrollView>(strip, "Scroller");
            var deepOffset = scroller.HorizontalOffset;
            var deepSeason = view.SelectedSeason?.Id;
            try
            {
                ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
                ScrollLongEpisodePanel(series);
                await LayoutAsync(series);
                Require(ReferenceEquals(Get<ShelfStrip>(series, "EpisodeStrip"), strip)
                    && Math.Abs(scroller.HorizontalOffset - deepOffset) < 1,
                    "深处重排重建了集带或丢失横向位置");
                episodePreference.Visible = false;
                ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
                await LayoutAsync(series);
                Require(Get<StackPanel>(series, "EpisodePanel").Visibility == Visibility.Collapsed, "长剧集卡未隐藏");
                episodePreference.Visible = true;
                fixture.Settings.Ui.SeriesCards = originalLayout;
                ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
                ScrollLongEpisodePanel(series);
                await LayoutAsync(series);
                Require(scroller.HorizontalOffset > 0 && Math.Abs(scroller.HorizontalOffset - deepOffset) < 1
                    && view.SelectedSeason?.Id == deepSeason, "恢复长剧集卡丢失深处位置或季索引");
                Require(cards.Count == 2000 && cards.MaterializedCount < 240
                    && repeater.ItemsSourceView!.GetAt(1500) is CardItem { Item.Id: "p3-series-episode-1500" },
                    "长剧自定义布局物化整个前缀或改变全局索引");
                evidence.Add(new
                {
                    phase = "deep-customization-restored",
                    materialized = cards.MaterializedCount,
                    horizontalOffset = scroller.HorizontalOffset,
                    season = deepSeason
                });
                await SaveProbeScreenAsync("performance-long-series-customization-restored-screen.png");
            }
            finally
            {
                fixture.Settings.Ui.SeriesCards = originalLayout;
                ShellPrefs.ApplyDetailLayout(fixture.Settings.Ui);
            }

            await SelectPerformanceSeasonAsync(series, seasons[0], strip);
            Require(view.SelectedSeason?.Id == seasons[0].Id, "换季锚点跳到S00失败");
            await SelectPerformanceSeasonAsync(series, seasons[19], strip);
            Require(view.SelectedSeason?.Id == seasons[19].Id, "换季锚点跳到最后季失败");
            Require(view.Episodes.Count == 2000 && cards.Count == 2000 && cards.MaterializedCount < 240,
                "季跳改变全局索引或物化整个前缀");

            // Home/End share this exact indexed focus entry. Exercise both endpoints through the real
            // ItemsRepeater; this is keyboard-focus control flow, not simulated physical key delivery.
            Call(strip, "FocusItem", 1999);
            await LayoutAsync(series);
            await UntilAsync(() => FocusedLongIndex(series, repeater) == 1999);
            Require(repeater.TryGetElement(1999) is { } last
                && Descendants(last).OfType<PosterCard>().Any(card => card.Card?.Item.Id == episodes[1999].Id),
                "End对应正式焦点入口没有实现最后一集容器");
            Call(strip, "FocusItem", 0);
            await LayoutAsync(series);
            await UntilAsync(() => FocusedLongIndex(series, repeater) == 0);
            Require(repeater.TryGetElement(0) is { } first
                && Descendants(first).OfType<PosterCard>().Any(card => card.Card?.Item.Id == episodes[0].Id),
                "Home对应正式焦点入口没有实现第一集容器");
            Require(cards.MaterializedCount < 280, "两端键盘焦点创建整剧卡片");
            evidence.Add(new
            {
                phase = "season-and-keyboard-ends",
                fullCount = cards.Count,
                materialized = cards.MaterializedCount,
                s00Reachable = true,
                lastSeasonReachable = true,
                indexedFocusEnds = true,
                physicalKeys = false
            });
            ResizeProbeClient(760, 900);
            await LayoutAsync(series);
            strip.JumpTo(1500);
            await LayoutAsync(series);
            await SaveProbeScreenAsync("performance-long-series-deep-760-screen.png");
            var abandoned = cards;
            series.Release();
            Require(abandoned.Count == 0 && abandoned.MaterializedCount == 0, "离页仍保留旧按需工厂和卡");
            frame.Content = null;

            ResizeProbeClient(1280, 900);
            var episodePage = await Enter(episodes[1500]);
            Require(episodePage.ViewModel.EpisodeFocus == 0 && episodePage.ViewModel.Episodes.Count == 2000
                    && episodePage.ViewModel.ShownEpisodes.Count == 100,
                "深处集页当前集定位或播放上下文缺失");
            Require(episodePage.ViewModel.EpisodeShelf!.Cards.MaterializedCount < 180,
                "深处集页从头创建整个前缀");
            evidence.Add(new
            {
                phase = "episode-deep-first",
                globalIndex = episodePage.ViewModel.EpisodeFocus,
                fullCount = episodePage.ViewModel.Episodes.Count,
                materialized = episodePage.ViewModel.EpisodeShelf.Cards.MaterializedCount
            });
            episodePage.Release();
            frame.Content = null;

            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 900);
                var seasonPage = await Enter(longSeason);
                var seasonCards = seasonPage.ViewModel.EpisodeShelf!.Cards;
                var rows = Get<ItemsRepeater>(seasonPage, "EpisodeList");
                Require(rows.Visibility == Visibility.Visible && rows.ItemsSourceView?.Count == 2000
                    && seasonPage.ViewModel.Episodes.Count == 2000, "长季竖列表缺失全量索引");
                Require(seasonCards.MaterializedCount < 180,
                    $"长季首屏在{width}宽无界Measure创建所有集卡：{seasonCards.MaterializedCount}/2000");
                ScrollLongEpisodePanel(seasonPage);
                await LayoutAsync(seasonPage);
                Require(seasonCards.MaterializedCount < 220,
                    $"长季竖排滚入首屏创建过多卡：{seasonCards.MaterializedCount}/2000");
                Require(seasonPage.ViewModel.Episodes[0].Id == "p3-long-season-series-episode-0",
                    "季页面没有保留服务端清单顺序");
                evidence.Add(new
                {
                    phase = "season-first",
                    width,
                    fullCount = seasonCards.Count,
                    materialized = seasonCards.MaterializedCount
                });
                await SaveProbeScreenAsync($"performance-long-season-{width}-screen.png");
                seasonPage.Release();
                Require(seasonCards.Count == 0, "长季离页没有清理按需源");
                frame.Content = null;
            }
            Require(fixture.Actions.Plays == 0, "长剧探针触发了播放");
            sink.Flush();
            sink.Dispose();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var timing = File.ReadAllLines(timingPath)
                .Select(line => JsonSerializer.Deserialize<NavigationTimingEvent>(line, jsonOptions)).ToArray();
            foreach (var run in runs)
            {
                PerformanceProbeRules.TimingSpan(timing, run, "episodes-sort", 2000);
                PerformanceProbeRules.TimingSpan(timing, run, "episodes-publish", 2000);
            }
            await WritePerformanceEvidenceAsync("performance-long-series-runtime.json", new
            {
                phases = evidence,
                episodeReads = reads,
                fullNetworkReadsRetained = true,
                realNetwork = false,
                realPlayback = false,
                fullDtoCount = 2000,
                uiSelectionSortStillPresent = true
            });
        }
        finally
        {
            NavigationTiming.Stop();
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            frame.BackStack.Clear();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }

        async Task<DetailPage> Enter(EmbyItem item)
        {
            var run = NavigationTiming.Click("probe-long-series")!;
            runs.Add(run.RunId);
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item)), "长剧真实Frame导航失败");
            var page = (DetailPage)frame.Content!;
            await UntilAsync(() => page.IsReady);
            await LayoutAsync(page);
            await WaitForDiagnosticsFrameAsync();
            return page;
        }
    }

    private static EmbyItem LongEpisode(int index, string series, EmbyItem season) => new()
    {
        Id = series + "-episode-" + index,
        Name = "第 " + (index + 1) + " 集",
        Type = EmbyItemType.Episode,
        SeriesId = series,
        SeriesName = "匿名长剧",
        SeasonId = season.Id,
        SeasonName = season.Name,
        ParentIndexNumber = season.IndexNumber,
        IndexNumber = index % 100 + 1,
        PremiereDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index),
        RunTimeTicks = TimeSpan.FromMinutes(23).Ticks,
        Overview = "长剧按需布局的匿名集内容。全量业务索引保留，卡片只在被布局或交互访问时创建。",
        UserData = new EmbyUserData { Played = index < 1500 },
        MediaSources = [new MediaSource
        {
            Id = "source-" + index,
            ItemId = series + "-episode-" + index,
            Path = "/anonymous/long-episode.mkv",
            Container = "mkv",
            MediaStreams = [new MediaStream { Index = 0, Type = "Video", Codec = "h264", Width = 1920, Height = 1080 }]
        }]
    };

    private static void ScrollLongEpisodePanel(DetailPage page)
    {
        var body = Get<ScrollView>(page, "Body");
        body.UpdateLayout();
        if (body.Content is not UIElement content) return;
        var panel = Get<StackPanel>(page, "EpisodePanel");
        body.ScrollTo(0, panel.TransformToVisual(content).TransformPoint(default).Y,
            new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
    }

    private static int FocusedLongIndex(DetailPage page, ItemsRepeater repeater)
    {
        var current = FocusManager.GetFocusedElement(page.XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (current is UIElement element && repeater.GetElementIndex(element) is >= 0 and var index) return index;
            current = VisualTreeHelper.GetParent(current);
        }
        return -1;
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task AniBtScrapeAsync()
    {
        var moviePilot = new AniBtMoviePilotProbeServer { EnableBangumi = true };
        var aniBt = new AniBtProbeServer();
        using var fixture = CreateFixture(moviePilot, aniBt);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var item = new EmbyItem
        {
            Id = "anime-episode",
            Name = "离线示例动画 · 第一集",
            Type = EmbyItemType.Episode,
            Genres = ["动画"],
            Path = "/library/Test.S01E01.mkv",
            IndexNumber = 1,
            ParentIndexNumber = 1
        };
        fixture.Transport.Reply = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/Items/anime-episode", StringComparison.Ordinal)
            ? Json(item) : FakeTransport.Standard(request));
        var service = (AniBtService)fixture.Services.GetService(typeof(AniBtService))!;
        var mp = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var scope = fixture.Session.Capture();
        AniBtScrapeDialog? dialog = null;
        Task<ContentDialogResult>? shown = null;
        try
        {
            dialog = new AniBtScrapeDialog(service, scope, item) { XamlRoot = _root.XamlRoot };
            shown = MoviePilotDialog.ShowAsync(dialog, mp);
            await UntilAsync(() => dialog.ViewModel.IsReady);
            Require(dialog.ViewModel.Rows.Count == 1 && dialog.ViewModel.CanSubmit && !dialog.ViewModel.NoticeOpen,
                "AniBT 刮削确认没有载入可确认的识别结果");
            var row = dialog.ViewModel.Rows[0];
            Require(row.SourceName == "Test.S01E01.mkv" && row.TargetPath == item.Path && row.Selected?.BgmId == 123,
                "刮削确认的源文件名、目标路径或自动选中的番剧不正确");
            Require(aniBt.MatchNames.SequenceEqual(["Test.S01E01.mkv"]), "识别接口没有只收到源文件名");
            foreach (var width in new[] { 1040, 360 })
            {
                var scale = _root.XamlRoot.RasterizationScale;
                ResizeInspect((int)(width * scale), (int)(800 * scale));
                await LayoutAsync(dialog);
                var choice = Descendants(dialog).OfType<ComboBox>().Single(box => box.Name == "AnimeChoice");
                Require(choice.Items.Count == 1 && choice.SelectedItem is AniBtAnime { BgmId: 123 }
                    && choice.IsEnabled && dialog.IsPrimaryButtonEnabled,
                    "AniBT 候选没有绑定到实际选择控件或确认按钮");
                await SaveProbeScreenAsync($"anibt-scrape-{width}.png");
            }
            var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(await shown == ContentDialogResult.None && moviePilot.MutationAttempts == 0,
                "关闭 AniBT 识别确认仍然提交了刮削");

            dialog = new AniBtScrapeDialog(service, scope, item) { XamlRoot = _root.XamlRoot };
            shown = MoviePilotDialog.ShowAsync(dialog, mp);
            await UntilAsync(() => dialog.ViewModel.IsReady && dialog.ViewModel.Rows.Count == 1);
            moviePilot.Offline = true;
            Require(!await mp.RefreshAvailabilityAsync(), "刮削确认的假 MP 断线没有被检测到");
            await UntilAsync(() => shown.IsCompleted);
            Require(await shown == ContentDialogResult.None && moviePilot.MutationAttempts == 0 && fixture.Actions.Plays == 0,
                "MP 断线没有关闭未提交的 AniBT 确认，或意外提交了操作");
        }
        finally
        {
            aniBt.CancelDelays();
            dialog?.Hide();
            if (shown is not null) await shown;
            ResizeInspect(1420, 980);
            _window?.Activate();
        }
    }

    private async Task AniBtSearchAsync()
    {
        var moviePilot = new AniBtMoviePilotProbeServer();
        var aniBt = new AniBtProbeServer();
        using var fixture = CreateFixture(moviePilot, aniBt);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            var tabs = Get<Segmented>(page, "SourceTabs");
            Require(tabs.Visibility == Visibility.Visible && tabs.Items.Count == 4, "连接后未显示 AniBT 搜索栏");
            tabs.SelectedIndex = 3;
            var panel = Get<AniBtSearchPanel>(page, "AniBtPanel");
            await UntilAsync(() => panel.Visibility == Visibility.Visible);
            Require(Get<Grid>(page, "EmbyContent").Visibility == Visibility.Collapsed
                && Get<MoviePilotSearchPanel>(page, "MoviePilotPanel").Visibility == Visibility.Collapsed
                && Get<MoviePilotVersionsView>(page, "MoviePilotResources").Visibility == Visibility.Collapsed,
                "AniBT 面板与其他搜索内容重叠");
            var embyRequests = fixture.Transport.Requests.Count;
            await page.SearchTypingAsync("离线动画");
            Require(aniBt.SearchCount == 0, "AniBT 键入时未经提交就发出搜索");
            await page.SubmitSearchAsync("离线动画");
            Require(panel.ViewModel.Results.Count == 2 && !panel.ViewModel.Busy, "AniBT 搜索结果未进入番剧列表");
            Require(aniBt.Requests.Any(path => path.StartsWith("/api/bgm/search?q=" + Uri.EscapeDataString("离线动画"), StringComparison.Ordinal)),
                "搜索词没有交给 AniBT");
            Require(fixture.Transport.Requests.Count == embyRequests && moviePilot.Requests.All(path =>
                !path.Contains("/search/", StringComparison.Ordinal) && !path.Contains("/media/search", StringComparison.Ordinal)),
                "AniBT 搜索误发 Emby 或 MoviePilot 搜索请求");
            Require(Get<Button>(panel, "LoadMoreAnime").Visibility == Visibility.Collapsed,
                "不支持分页的 AniBT 结果仍显示加载更多");

            var results = panel.ViewModel.Results;
            var firstAnime = results[0];
            foreach (var width in new[] { 1040, 360 })
            {
                await SizeAsync(page, width);
                var search = Get<Border>(page, "SearchSurface");
                Require(tabs.ActualWidth <= search.ActualWidth + 1, "四个搜索栏超出搜索区域");
                var list = Get<ListView>(panel, "AnimeResults");
                Require(Descendants(list).OfType<Button>().Any(button => button.Name == "OpenAnimeResources"
                    && AutomationProperties.GetName(button).Contains(firstAnime.Title, StringComparison.Ordinal)),
                    "番剧行的资源按钮没有可定位的自动化名称");
                await SaveProbeScreenAsync($"anibt-search-{width}.png");
            }

            await firstAnime.OpenCommand.ExecuteAsync(null);
            Require(panel.ViewModel.SelectedAnime?.BgmId == 123 && panel.ViewModel.Browser.Rows.Count == 1,
                "选择番剧后没有读取对应 AniBT 资源");
            Require(aniBt.Requests.Any(path => path == "/rss/anime.xml?bgmId=123"),
                "资源查询没有绑定所选番剧的 Bangumi 编号");
            var row = panel.ViewModel.Browser.Rows[0];
            Require(row.CanDownload && row.Resource.MediaInfo.ValueKind != System.Text.Json.JsonValueKind.Object,
                "未开启 Bangumi 来源时没有保留可交给 MP 的公开种子");
            panel.ViewModel.Browser.Groups[0].IsExpanded = true;
            foreach (var width in new[] { 1040, 360 })
            {
                await SizeAsync(page, width);
                await SaveProbeScreenAsync($"anibt-resources-{width}.png");
            }

            await SizeAsync(page, 1040);
            var cancelled = row.DownloadCommand.ExecuteAsync(null);
            var dialog = await DownloadDialogAsync();
            await UntilAsync(() => !dialog.ViewModel.Busy);
            Require(dialog.DefaultButton == ContentDialogButton.Close && !dialog.ViewModel.NoticeOpen,
                "AniBT 下载确认未正确载入选项或默认键不是取消");
            await SaveProbeScreenAsync("anibt-download-confirm.png");
            var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            await cancelled;
            Require(moviePilot.MutationAttempts == 0 && row.State == MoviePilotDownloadState.Idle,
                "取消 AniBT 下载仍然发送写请求或锁住结果行");

            panel.ViewModel.ReturnToResultsCommand.Execute(null);
            Require(ReferenceEquals(results, panel.ViewModel.Results) && ReferenceEquals(firstAnime, results[0])
                && panel.ViewModel.SelectedAnime is null && panel.ViewModel.Browser.Rows.Count == 0,
                "返回番剧没有恢复原列表或残留了旧资源");
            await page.SubmitSearchAsync("空结果");
            Require(panel.ViewModel.Results.Count == 0 && panel.ViewModel.ShowEmptyNotice && !panel.ViewModel.Busy,
                "AniBT 空结果没有展示空态");
            await page.SubmitSearchAsync("接口失败");
            Require(panel.ViewModel.NoticeOpen && panel.ViewModel.Results.Count == 0 && !panel.ViewModel.Busy,
                "AniBT 接口失败被误报为正常空结果");

            var late = aniBt.DelayNextSearch();
            var oldQuery = page.SubmitSearchAsync("旧请求");
            await UntilAsync(() => late.Entered.Task.IsCompleted);
            await page.SubmitSearchAsync("新请求");
            late.Response.TrySetResult(AniBtProbeServer.SearchReply("迟到的旧动画", 456));
            await oldQuery;
            Require(panel.ViewModel.Results.Count == 2 && panel.ViewModel.Results[0].Anime.BgmId == 123
                && !panel.ViewModel.Busy, "迟到的 AniBT 结果覆盖了新查询");

            var cleared = aniBt.DelayNextSearch();
            var clearingQuery = page.SubmitSearchAsync("清空前请求");
            await UntilAsync(() => cleared.Entered.Task.IsCompleted);
            await page.SubmitSearchAsync("");
            cleared.Response.TrySetResult(AniBtProbeServer.SearchReply("不该复活的动画", 456));
            await clearingQuery;
            Require(panel.ViewModel.Results.Count == 0 && panel.ViewModel.ShowEmptyNotice && !panel.ViewModel.Busy,
                "清空关键词后旧 AniBT 结果重新出现");

            await page.SubmitSearchAsync("断线前请求");
            moviePilot.Offline = true;
            Require(!await service.RefreshAvailabilityAsync(), "假 MP 断线后仍被判为可用");
            await UntilAsync(() => tabs.Visibility == Visibility.Collapsed && tabs.SelectedIndex == 0);
            Require(panel.Visibility == Visibility.Collapsed && panel.ViewModel.Results.Count == 0
                && Get<Grid>(page, "EmbyContent").Visibility == Visibility.Visible,
                "MP 断线后 AniBT 未收起、查询未清空或媒体库未恢复");
            await SaveProbeScreenAsync("anibt-offline.png");

            moviePilot.Offline = false;
            Require(await service.RefreshAvailabilityAsync(), "假 MP 恢复后仍不可用");
            await UntilAsync(() => tabs.Visibility == Visibility.Visible);
            await page.SubmitSearchAsync("");
            tabs.SelectedIndex = 3;
            var departed = aniBt.DelayNextSearch();
            var leavingQuery = page.SubmitSearchAsync("离页请求");
            await UntilAsync(() => departed.Entered.Task.IsCompleted);
            page.Release();
            departed.Response.TrySetResult(AniBtProbeServer.SearchReply("离页后旧动画", 456));
            await leavingQuery;
            Require(panel.ViewModel.Results.Count == 0 && panel.ViewModel.Browser.Rows.Count == 0 && !panel.ViewModel.Busy,
                "离开搜索页后 AniBT 查询仍回填");
            Require(moviePilot.MutationAttempts == 0 && fixture.Actions.Plays == 0,
                "AniBT 只读与取消探针不应下载、刮削或播放");
            Require(aniBt.Requests.All(path => path.StartsWith("/api/bgm/search?", StringComparison.Ordinal)
                || path.StartsWith("/rss/anime.xml?", StringComparison.Ordinal)), "探针不应下载 AniBT 种子文件");
        }
        finally
        {
            aniBt.CancelDelays();
            foreach (var dialog in OpenDownloads()) dialog.Hide();
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
            _window?.Activate();
        }

        async Task SizeAsync(LibraryPage page, int width)
        {
            var scale = _root.XamlRoot.RasterizationScale;
            ResizeInspect((int)(width * scale), (int)(800 * scale));
            await LayoutAsync(page);
        }

        IEnumerable<MoviePilotDownloadDialog> OpenDownloads() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<MoviePilotDownloadDialog>().Distinct().ToArray();

        async Task<MoviePilotDownloadDialog> DownloadDialogAsync()
        {
            MoviePilotDownloadDialog? dialog = null;
            await UntilAsync(() => (dialog = OpenDownloads().FirstOrDefault()) is not null);
            await LayoutAsync(dialog!);
            return dialog!;
        }
    }

    private sealed class AniBtMoviePilotProbeServer : HttpMessageHandler
    {
        private int _mutationAttempts;
        internal bool Offline { get; set; }
        internal bool EnableBangumi { get; set; }
        internal int MutationAttempts => Volatile.Read(ref _mutationAttempts);
        internal ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "AniBT 的 MP 探针拒绝真实服务器");
            var path = request.RequestUri.AbsolutePath;
            Requests.Enqueue(request.RequestUri.PathAndQuery);
            if (Offline) throw new HttpRequestException("offline fixture");
            if (path.EndsWith("/login/access-token", StringComparison.Ordinal))
                return Task.FromResult(Json(new { access_token = "fixture-token" }));
            if (request.Method != HttpMethod.Get)
            {
                Interlocked.Increment(ref _mutationAttempts);
                throw new InvalidOperationException("AniBT 搜索和取消确认不得发送写请求");
            }
            if (path.EndsWith("/dashboard/system", StringComparison.Ordinal)) return Task.FromResult(Json(new { version = "fixture" }));
            if (path.EndsWith("/media/source", StringComparison.Ordinal)) return Task.FromResult(EnableBangumi
                ? Json(new[] { new { name = "Bangumi", media_source = "bangumi", media_types = new[] { "电视剧" } } })
                : Json(Array.Empty<object>()));
            if (path.EndsWith("/history/transfer", StringComparison.Ordinal)) return Task.FromResult(Json(new { list = Array.Empty<object>(), total = 0 }));
            if (path.EndsWith("/download/clients", StringComparison.Ordinal))
                return Task.FromResult(Json(new[] { new { name = "动画下载器" }, new { name = "备用下载器" } }));
            if (path.EndsWith("/download/paths", StringComparison.Ordinal))
                return Task.FromResult(Json(new[] { new { save_path = "/downloads/anime" } }));
            throw new InvalidOperationException("未登记的 AniBT MP 读取请求");
        }
    }

    private sealed class AniBtProbeServer : HttpMessageHandler
    {
        internal sealed record DelayedSearch(TaskCompletionSource<bool> Entered, TaskCompletionSource<HttpResponseMessage> Response);
        private readonly ConcurrentQueue<DelayedSearch> _delays = new();
        private readonly List<DelayedSearch> _allDelays = [];
        internal ConcurrentQueue<string> Requests { get; } = new();
        internal ConcurrentQueue<string> MatchNames { get; } = new();
        internal int SearchCount => Requests.Count(path => path.StartsWith("/api/bgm/search?", StringComparison.Ordinal));

        internal DelayedSearch DelayNextSearch()
        {
            var delay = new DelayedSearch(Pending<bool>(), Pending<HttpResponseMessage>());
            _allDelays.Add(delay);
            _delays.Enqueue(delay);
            return delay;
        }

        internal void CancelDelays()
        {
            foreach (var delay in _allDelays) delay.Response.TrySetCanceled();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.RequestUri!.Host == "anibt.net", "AniBT 探针拒绝其他服务器");
            Require(request.Headers.Authorization is null && !request.Headers.Contains("Cookie")
                && !request.Headers.Contains("X-Emby-Token"), "AniBT 请求携带了服务器凭据");
            Requests.Enqueue(request.RequestUri.PathAndQuery);
            if (request.RequestUri.AbsolutePath == "/api/animes/torrent-match" && request.Method == HttpMethod.Post)
                return MatchReplyAsync(request, cancellationToken);
            Require(request.Method == HttpMethod.Get, "AniBT 探针只接受搜索、RSS 与只读识别");
            if (request.RequestUri.AbsolutePath == "/api/bgm/search")
            {
                if (_delays.TryDequeue(out var delayed))
                {
                    delayed.Entered.TrySetResult(true);
                    // Deliberately ignore cancellation until the probe releases the response, exercising stale-result guards.
                    return delayed.Response.Task;
                }
                return Task.FromResult(Query(request, "q") switch
                {
                    "空结果" => Json(new { ok = true, data = Array.Empty<object>() }),
                    "接口失败" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                    _ => SearchReply()
                });
            }
            if (request.RequestUri.AbsolutePath == "/rss/anime.xml")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <rss xmlns:anibt="https://anibt.net/xmlns/rss/1.0/"><channel><item>
                          <title>[离线字幕组] 离线示例动画 - EPISODE [1080p][HEVC]</title><guid>rel_one</guid>
                          <anibt:bgmId>123</anibt:bgmId><anibt:groupName>离线字幕组</anibt:groupName>
                          <anibt:resolution>1080p</anibt:resolution><anibt:episodeKey>EPISODE</anibt:episodeKey>
                          <enclosure url="https://anibt.net/api/torrent/rel_one.torrent" length="1234567890" />
                        </item></channel></rss>
                        """.Replace("EPISODE", int.TryParse(Query(request, "episode"), out var episode) ? episode.ToString("00") : "01", StringComparison.Ordinal),
                        Encoding.UTF8, "application/rss+xml")
                });
            throw new InvalidOperationException("探针不应请求 AniBT 种子文件或未登记的接口");
        }

        private async Task<HttpResponseMessage> MatchReplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            MatchNames.Enqueue(body.RootElement.GetProperty("rawTitle").GetString() ?? "");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "version":3,"content":{"kind":"anime_episode","confidence":"high"},
                      "structure":{"season":1},
                      "decision":{"kind":"auto","confidence":"high","reason":"top_ranked_candidate","animeId":"ani_one","bgmId":123,"vetoes":[]},
                      "candidates":[{"animeId":"ani_one","bgmId":123,"matchScore":100,
                        "anime":{"_id":"ani_one","bgmId":123,"title":{"chinese":"离线示例动画","native":"Test Anime"},"episodes":12,"seasonYear":2026,"format":"TV"}}]
                    }
                    """, Encoding.UTF8, "application/json")
            };
        }

        internal static HttpResponseMessage SearchReply(string title = "离线示例动画 · 星光下的旅程", long bgmId = 123) => Json(new
        {
            ok = true,
            data = new[]
            {
                new { bgmId, nameCn = title, name = "Offline Animation: A Journey Under the Stars", seasonYear = 2026, totalEpisodes = 12 },
                new { bgmId = bgmId + 1, nameCn = "另一部同名动画", name = "Another Animation", seasonYear = 2024, totalEpisodes = 24 }
            }
        });
    }
}

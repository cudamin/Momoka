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
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

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
                Require(choice.Items.Count == 2 && choice.SelectedItem is AniBtAnime { BgmId: 123 }
                    && choice.IsEnabled && dialog.IsPrimaryButtonEnabled,
                    "AniBT 候选没有绑定到实际选择控件或确认按钮");
                await SaveProbeScreenAsync($"anibt-scrape-{width}.png");
            }
            var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(await shown == ContentDialogResult.None && moviePilot.MutationAttempts == 0,
                "关闭 AniBT 识别确认仍然提交了重新整理");

            // 没有自动结论时也必须直接显示第一个候选，主按钮已是「重新整理」。
            aniBt.ManualCandidates = true;
            dialog = new AniBtScrapeDialog(service, scope, item) { XamlRoot = _root.XamlRoot };
            shown = MoviePilotDialog.ShowAsync(dialog, mp);
            await UntilAsync(() => dialog.ViewModel.IsReady && dialog.ViewModel.Rows.Count == 1);
            Require(dialog.PrimaryButtonText == "重新整理" && dialog.IsPrimaryButtonEnabled,
                "AniBT 识别确认的主按钮没有改名为「重新整理」");
            var manualRow = dialog.ViewModel.Rows[0];
            var manualChoice = Descendants(dialog).OfType<ComboBox>().Single(box => box.Name == "AnimeChoice");
            Require(manualRow.Selected?.BgmId == 123 && manualChoice.Items.Count == 2
                && manualChoice.SelectedItem is AniBtAnime { BgmId: 123 },
                "多个候选时没有直接显示第一个识别结果");
            var manualScale = _root.XamlRoot.RasterizationScale;
            ResizeInspect((int)(1040 * manualScale), (int)(800 * manualScale));
            await LayoutAsync(dialog);
            await SaveProbeScreenAsync("anibt-scrape-candidates.png");
            // 本机 XAML 岛拍不进 BitBlt；另存一张对话框自身的渲染帧，报告里按「渲染帧」记。
            await SaveSettledFrameAsync(dialog, "anibt-scrape-candidates-render.png");
            close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            await shown;
            Require(moviePilot.MutationAttempts == 0, "关闭多候选的 AniBT 识别仍然提交了重新整理");
            aniBt.ManualCandidates = false;

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
            Require(aniBt.Requests.Any(path => path.StartsWith("/api/animes/search?q=" + Uri.EscapeDataString("离线动画"), StringComparison.Ordinal)),
                "搜索词没有交给 AniBT");
            Require(fixture.Transport.Requests.Count == embyRequests && moviePilot.Requests.All(path =>
                !path.Contains("/search/", StringComparison.Ordinal) && !path.Contains("/media/search", StringComparison.Ordinal)),
                "AniBT 搜索误发 Emby 或 MoviePilot 搜索请求");
            var results = panel.ViewModel.Results;
            var firstAnime = results[0];
            using var imageStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(imageStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(await FixtureImageAsync(400, 600));
                await writer.StoreAsync();
            }
            imageStream.Seek(0);
            var poster = new BitmapImage();
            await poster.SetSourceAsync(imageStream);
            Field(firstAnime, "_artwork", poster);
            panel.ViewModel.SelectedFormat = "剧场版";
            Require(panel.ViewModel.VisibleResults.Count == 1 && panel.ViewModel.VisibleResults[0].Anime.Format == "MOVIE",
                "番剧格式筛选没有更新海报网格");
            panel.ViewModel.SelectedYear = "2026";
            Require(panel.ViewModel.VisibleResults.Count == 0 && panel.ViewModel.NoFilteredResultsVisibility == Visibility.Visible,
                "组合筛选的无结果提示没有显示");
            panel.ViewModel.SelectedFormat = "全部格式";
            panel.ViewModel.SelectedYear = "全部年份";
            Require(panel.ViewModel.VisibleResults.Count == 2 && firstAnime.Description.Contains("7.5", StringComparison.Ordinal),
                "清除筛选没有恢复海报或评分没有转为十分制");
            foreach (var width in new[] { 1040, 360 })
            {
                await SizeAsync(page, width);
                var search = Get<Border>(page, "SearchSurface");
                Require(tabs.ActualWidth <= search.ActualWidth + 1, "四个搜索栏超出搜索区域");
                var list = Get<GridView>(panel, "AnimeResults");
                Require(Descendants(list).OfType<Button>().Any(button => button.Name == "OpenAnimeResources"
                    && AutomationProperties.GetName(button).Contains(firstAnime.Title, StringComparison.Ordinal)),
                    "番剧行的资源按钮没有可定位的自动化名称");
                // 本机这个窗口的 XAML 岛拍不进 BitBlt（实屏是一整块窗口底色）；用页面自身渲染留视觉证据，
                // 文件名带 -render，报告里也按「渲染帧，不是实屏」记。
                await SaveSettledFrameAsync(page, $"anibt-search-{width}-render.png");
            }

            await firstAnime.OpenCommand.ExecuteAsync(null);
            panel.ViewModel.SelectedArtwork = poster;
            var browser = panel.ViewModel.Browser;
            Require(panel.ViewModel.SelectedAnime?.BgmId == 123 && browser.Rows.Count == 3,
                $"选择番剧后没有读取对应 AniBT 资源：Rows={browser.Rows.Count}，Empty={browser.EmptyMessage}，"
                + $"Busy={panel.ViewModel.Busy}，提示开={panel.ViewModel.NoticeOpen}，提示={panel.ViewModel.NoticeMessage}，"
                + $"请求={string.Join("、", aniBt.Requests)}，筛选=({browser.SelectedEpisode}|{browser.SelectedResolution}|{browser.SelectedGroup}|{browser.Text})，"
                + $"计数={browser.CountText}");
            Require(aniBt.Requests.Any(path => path == "/api/anime/groups?bgmId=123"),
                "资源查询没有绑定所选番剧的 Bangumi 编号");
            Require(panel.ViewModel.SelectedTitle == firstAnime.Title && panel.ViewModel.SelectedBgm == "Bangumi #123",
                "番剧头部没有用上所选番剧的标题或 Bangumi 身份");
            Require(panel.ViewModel.SummaryVisibility == Visibility.Visible && browser.ProgressGroups.Count == 2
                && browser.ProgressGroups[0].PublishedText == "4 / 12 集"
                && browser.ProgressGroups[0].Cells.Single(cell => cell.Episode == 5).Published
                && !browser.ProgressGroups[0].Cells.Single(cell => cell.Episode == 2).Published,
                "简介或完整发布进度没有显示，或按最近资源错误推断进度");
            Require(browser.ProgressGroups[0].RssUri?.Query.Contains("groupSlug=offline", StringComparison.Ordinal) == true,
                "字幕组 RSS 没有保持组范围");
            Require(browser.Episodes.SequenceEqual(new[] { AniBtBrowserViewModel.AllEpisodes, "01", "02", AniBtReleaseLabels.UnknownEpisode }),
                "集号筛选没有按 RSS 集号给出选项：" + string.Join("、", browser.Episodes));
            Require(browser.Resolutions.SequenceEqual(new[] { AniBtBrowserViewModel.AllResolutions, "1080p", "720p" }),
                "清晰度筛选没有按 RSS 清晰度给出选项：" + string.Join("、", browser.Resolutions));
            Require(browser.Groups.Contains("离线字幕组") && browser.Groups.Contains("另一个字幕组"),
                "字幕组筛选没有按 RSS 字幕组给出选项：" + string.Join("、", browser.Groups));

            browser.SelectedEpisode = "02";
            await LayoutAsync(panel);
            Require(browser.Rows.Count == 1 && browser.CountText.Contains("1 / 3", StringComparison.Ordinal),
                "按集筛选没有只留下第 2 集：" + browser.CountText);
            browser.Text = "不存在的词";
            Require(browser.Rows.Count == 0 && browser.NoMatchesVisibility == Visibility.Visible,
                "关键词与集号组合筛选没有显示无匹配状态");
            Require(browser.ResourceGroups.Count == 0, "筛选无匹配后空字幕组仍可见");
            browser.ResetFiltersCommand.Execute(null);
            Require(browser.Rows.Count == 3 && browser.Text.Length == 0 && browser.SelectedEpisode == AniBtBrowserViewModel.AllEpisodes,
                "清除筛选没有恢复全部发布");

            var row = browser.Rows[0];
            Require(row.CanDownload && row.Resource.MediaInfo.ValueKind != System.Text.Json.JsonValueKind.Object,
                "未开启 Bangumi 来源时没有保留可交给 MP 的公开种子");
            Require(row.MetaLine.Contains("1080p", StringComparison.Ordinal) && row.MetaLine.Contains("离线字幕组", StringComparison.Ordinal),
                "发布行没有显示 AniBT 的字幕组或清晰度：" + row.MetaLine);
            await LayoutAsync(panel);
            Require(ReferenceEquals(Get<ItemsRepeater>(Get<AniBtResourceBrowser>(panel, "ResourceBrowser"), "ReleaseList").ItemsSource, browser.ResourceGroups)
                && browser.ResourceGroups.Count == 2, "AniBT 发布列表没有绑定字幕组");
            foreach (var width in new[] { 1040, 360 })
            {
                await SizeAsync(page, width);
                var detailScroll = Get<ScrollViewer>(panel, "AnimeDetailScroll");
                var resourceBrowser = Get<AniBtResourceBrowser>(panel, "ResourceBrowser");
                browser.ViewIndex = 0;
                await LayoutAsync(panel);
                await SaveSettledFrameAsync(page, $"anibt-detail-{width}-render.png");
                detailScroll.ChangeView(null, width < 600 ? resourceBrowser.TransformToVisual(Get<StackPanel>(panel, "AnimeResourcesView"))
                    .TransformPoint(new Windows.Foundation.Point()).Y : 0, null, true);
                await LayoutAsync(panel);
                await SaveSettledFrameAsync(page, $"anibt-progress-{width}-render.png");
                browser.ViewIndex = 1;
                await LayoutAsync(panel);
                detailScroll.ChangeView(null, resourceBrowser.TransformToVisual(Get<StackPanel>(panel, "AnimeResourcesView"))
                    .TransformPoint(new Windows.Foundation.Point()).Y, null, true);
                await LayoutAsync(panel);
                await SaveSettledFrameAsync(page, $"anibt-resources-{width}-render.png");
                detailScroll.ChangeView(null, 0, null, true);
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
            await firstAnime.OpenCommand.ExecuteAsync(null);
            await LayoutAsync(panel);
            Require(Get<ScrollViewer>(panel, "AnimeDetailScroll").VerticalOffset < 1, "重新打开番剧保留了上一页滚动位置");
            var groupRows = Enumerable.Range(1, 7).Select(index => new AniBtResourceRow(row.Item with
            {
                Release = row.Release with { ReleaseId = $"rel_expand_{index}" }
            }, _ => Task.CompletedTask)).ToArray();
            groupRows[0].State = MoviePilotDownloadState.Done;
            var expandGroup = new AniBtResourceGroup("展开测试组", groupRows, null, 12);
            Require(expandGroup.VisibleRows.Count == 5 && expandGroup.MoreVisibility == Visibility.Visible, "字幕组默认未限制五条");
            expandGroup.ToggleMoreCommand.Execute(null);
            Require(expandGroup.VisibleRows.Count == 7 && ReferenceEquals(expandGroup.VisibleRows[0], groupRows[0])
                && expandGroup.VisibleRows[0].State == MoviePilotDownloadState.Done, "展开字幕组丢失行对象或下载状态");
            expandGroup.ToggleMoreCommand.Execute(null);
            Require(expandGroup.VisibleRows.Count == 5, "字幕组不能重新收起");
            panel.ViewModel.ReturnToResultsCommand.Execute(null);
            await page.SubmitSearchAsync("分页");
            var beforePage = panel.ViewModel.Results[0];
            Require(panel.ViewModel.HasMore, "目录分页未提供加载入口");
            aniBt.FailNextPage = true;
            await panel.ViewModel.LoadMoreCommand.ExecuteAsync(null);
            Require(panel.ViewModel.Results.Count == 2 && panel.ViewModel.HasMore && panel.ViewModel.NoticeOpen,
                "分页失败丢失现有结果或重试入口");
            await panel.ViewModel.LoadMoreCommand.ExecuteAsync(null);
            Require(panel.ViewModel.Results.Count == 4 && !panel.ViewModel.HasMore && ReferenceEquals(beforePage, panel.ViewModel.Results[0]),
                "分页重试没有追加结果或替换了旧行");
            Require(aniBt.Requests.Any(path => path.Contains("cursor=next%3A2", StringComparison.Ordinal)), "分页没有回传原始游标");
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
            Require(aniBt.Requests.All(path => path.StartsWith("/api/animes/search?", StringComparison.Ordinal)
                || path.StartsWith("/rss/anime.xml?", StringComparison.Ordinal)
                || path.StartsWith("/api/anime/groups?", StringComparison.Ordinal)
                || path.StartsWith("/api/public/v1/anime?", StringComparison.Ordinal)), "探针不应下载 AniBT 种子文件");
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
        internal bool ManualCandidates { get; set; }
        internal bool FailNextPage { get; set; }
        internal int SearchCount => Requests.Count(path => path.StartsWith("/api/animes/search?", StringComparison.Ordinal));

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
            if (request.RequestUri.AbsolutePath == "/api/animes/search")
            {
                if (_delays.TryDequeue(out var delayed))
                {
                    delayed.Entered.TrySetResult(true);
                    // Deliberately ignore cancellation until the probe releases the response, exercising stale-result guards.
                    return delayed.Response.Task;
                }
                if (Query(request, "cursor") is not null)
                {
                    if (FailNextPage)
                    {
                        FailNextPage = false;
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                    }
                    return Task.FromResult(SearchReply("分页追加番剧", 125));
                }
                return Task.FromResult(Query(request, "q") switch
                {
                    "空结果" => Json(new { page = Array.Empty<object>(), isDone = true, continueCursor = "" }),
                    "接口失败" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                    "分页" => SearchReply(more: true),
                    _ => SearchReply()
                });
            }
            if (request.RequestUri.AbsolutePath == "/api/public/v1/anime")
                return Task.FromResult(Json(new { initialAnime = Anime(), initialExternalIds = Array.Empty<object>() }));
            if (request.RequestUri.AbsolutePath == "/api/anime/groups") return Task.FromResult(GroupsReply());
            if (request.RequestUri.AbsolutePath == "/rss/anime.xml")
            {
                var requested = Query(request, "episode");
                var items = requested is { } episode
                    ? new[] { ReleaseItem("rel_one", int.TryParse(episode, out var number) ? number.ToString("00", System.Globalization.CultureInfo.InvariantCulture) : "01",
                        "离线字幕组", "1080p", 1234567890, "Sun, 18 Oct 2026 12:00:00 +0800") }
                    : new[]
                    {
                        ReleaseItem("rel_one", "01", "离线字幕组", "1080p", 1234567890, "Sun, 18 Oct 2026 12:00:00 +0800"),
                        ReleaseItem("rel_two", "02", "另一个字幕组", "720p", 456789012, "Sat, 17 Oct 2026 12:00:00 +0800"),
                        ReleaseItem("rel_batch", "", "离线字幕组", "1080p", 9876543210, "Sat, 10 Oct 2026 12:00:00 +0800")
                    };
                var xml = new StringBuilder("""<rss xmlns:anibt="https://anibt.net/xmlns/rss/1.0/"><channel>""");
                foreach (var item in items) xml.Append(item);
                xml.Append("</channel></rss>");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml.ToString(), Encoding.UTF8, "application/rss+xml")
                });
            }
            throw new InvalidOperationException("探针不应请求 AniBT 种子文件或未登记的接口");
        }

        private async Task<HttpResponseMessage> MatchReplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            MatchNames.Enqueue(body.RootElement.GetProperty("rawTitle").GetString() ?? "");
            return Json(new
            {
                version = 3,
                content = new { kind = "anime_episode", confidence = "high" },
                structure = new { season = 1 },
                decision = ManualCandidates
                    ? (object)new { kind = "manual", confidence = "low", reason = "ambiguous", vetoes = Array.Empty<string>() }
                    : new { kind = "auto", confidence = "high", reason = "top_ranked_candidate", animeId = "ani_one", bgmId = 123, vetoes = Array.Empty<string>() },
                candidates = new object[]
                {
                    new { animeId = "ani_one", bgmId = 123, matchScore = 100,
                        anime = new { _id = "ani_one", bgmId = 123, title = new { chinese = "离线示例动画", native = "Test Anime" },
                            episodes = 12, seasonYear = 2026, format = "TV" } },
                    new { animeId = "ani_two", bgmId = 124, matchScore = 80,
                        anime = new { _id = "ani_two", bgmId = 124, title = new { chinese = "另一部离线动画", native = "Another Offline Anime" },
                            episodes = 24, seasonYear = 2025, format = "TV" } }
                }
            });
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };

        private static string ReleaseItem(string id, string episodeKey, string group, string resolution, long size, string date)
        {
            var suffix = episodeKey.Length == 0 ? " 合集" : " - " + episodeKey;
            return $"""
                <item>
                  <title>[{group}] 离线示例动画{suffix} [{resolution}][HEVC]</title><guid>{id}</guid>
                  <anibt:bgmId>123</anibt:bgmId><anibt:groupName>{group}</anibt:groupName>
                  <anibt:resolution>{resolution}</anibt:resolution><anibt:episodeKey>{episodeKey}</anibt:episodeKey>
                  <enclosure url="https://anibt.net/api/torrent/{id}.torrent" length="{size}" />
                  <pubDate>{date}</pubDate>
                </item>
                """;
        }

        private static object Anime(string title = "离线示例动画 · 星光下的旅程", long bgmId = 123, string format = "TV", int year = 2026) => new
        {
            _id = bgmId == 123 ? "ani_one" : "ani_two",
            bgmId,
            title = new { chinese = title, native = "Offline Animation: A Journey Under the Stars", english = "A Journey Under the Stars" },
            seasonYear = year,
            season = "FALL",
            episodes = 12,
            format,
            status = "RELEASING",
            averageScore = 75,
            description = "一群性格各异的学生在校园中相遇，从平凡的日常走向意想不到的冒险。这是一段关于友情、勇气和成长的旅程。",
            genres = new[] { "校园", "日常", "喜剧" },
            officialSite = "https://example.invalid/anime"
        };

        internal static HttpResponseMessage SearchReply(string title = "离线示例动画 · 星光下的旅程", long bgmId = 123, bool more = false) => Json(new
        {
            page = new[] { Anime(title, bgmId), Anime("另一部同名动画", bgmId + 1, "MOVIE", 2024) },
            isDone = !more,
            continueCursor = more ? "next:2" : ""
        });

        private static HttpResponseMessage GroupsReply() => Json(new
        {
            ok = true,
            data = new
            {
                bgmId = 123,
                animeId = "ani_one",
                title = "离线示例动画 · 星光下的旅程",
                groups = new[]
                {
                    new { groupId = "group_one", name = "离线字幕组", slug = "offline", publishedEpisodes = new[] { 1, 3, 4, 5 }, totalReleases = 12,
                        items = new[] { GroupRelease("rel_one", "01", "1080p", 1234567890, 1792296000000), GroupRelease("rel_batch", "", "1080p", 9876543210, 1791604800000) } },
                    new { groupId = "group_two", name = "另一个字幕组", slug = "another", publishedEpisodes = new[] { 2 }, totalReleases = 1,
                        items = new[] { GroupRelease("rel_two", "02", "720p", 456789012, 1792209600000) } }
                }
            }
        });

        private static object GroupRelease(string releaseId, string episodeKey, string resolution, long size, long publishedAt) => new
        {
            releaseId,
            title = $"[离线字幕组] 离线示例动画 - {episodeKey} [{resolution}][HEVC][AAC]",
            episodeKey,
            resolution,
            size,
            publishedAt,
            format = "MKV",
            language = new[] { "简中", "繁中" },
            subtitle = "内封字幕",
            customTags = new[] { "Web", "HEVC", "AAC" },
            torrentStorageId = "fixture-torrent"
        };
    }
}

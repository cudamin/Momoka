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
            SeriesId = "anime-series",
            Genres = ["动画"],
            Path = "/library/Test.S01E01.mkv",
            IndexNumber = 1,
            ParentIndexNumber = 1
        };
        var series = new EmbyItem
        {
            Id = "anime-series",
            Name = "离线库内整剧",
            Type = EmbyItemType.Series,
            ProductionYear = 2024,
            Genres = ["动画"]
        };
        fixture.Transport.Reply = async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Items/RemoteSearch/Series", StringComparison.Ordinal))
            {
                Require(request.Method == HttpMethod.Post, "Emby 候选读取没有使用 RemoteSearch 契约");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var search = body.RootElement.GetProperty("SearchInfo");
                Require(body.RootElement.GetProperty("ItemId").GetString() == series.Id
                    && search.GetProperty("Year").GetInt32() == 2026
                    && search.GetProperty("ProviderIds").EnumerateObject().Count() == 0
                    && search.GetProperty("Name").GetString() is "离线示例动画" or "Test Anime",
                    "AniBT 预览没有按所选番剧标题、年份读取实际整剧候选，或猜用了跨来源编号");
                return Json(new[]
                {
                    new RemoteSearchResult { Name = "无关动画", ProductionYear = 2026 },
                    new RemoteSearchResult { Name = "Test Anime", ProductionYear = 2026,
                        SearchProviderName = "TheMovieDb", ProviderIds = new() { ["Tmdb"] = "fixture-anime" } }
                });
            }
            // RemoteSearch 的 POST 是只读；Apply、Refresh 与其他写入绝不能落到共享的默认回复。
            Require(request.Method == HttpMethod.Get, "AniBT 识别探针在确认前或取消后不得发送 Emby 写入");
            return path.EndsWith("/Items/anime-episode", StringComparison.Ordinal) ? Json(item)
                : path.EndsWith("/Items/anime-series", StringComparison.Ordinal) ? Json(series) : FakeTransport.Standard(request);
        };
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
            Require(dialog.ViewModel.Rows.Count == 1 && dialog.ViewModel.CanPreview && !dialog.ViewModel.CanSubmit
                && !dialog.ViewModel.NoticeOpen,
                "AniBT 刮削确认没有载入可预览的识别结果，或尚未预览就能提交");
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
                    && choice.IsEnabled && dialog.IsPrimaryButtonEnabled && dialog.PrimaryButtonText == "预览计划",
                    "AniBT 候选没有绑定到实际选择控件或第一阶段预览按钮");
                await SaveProbeScreenAsync($"anibt-scrape-{width}.png");
            }
            await PreviewAsync(dialog);
            await SaveProbeScreenAsync("anibt-scrape-preview.png");
            var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(await shown == ContentDialogResult.None && moviePilot.MutationAttempts == 0
                && !dialog.ViewModel.CanPreview && !dialog.ViewModel.CanSubmit,
                "关闭 AniBT 已预览计划没有撤销确认，或仍然提交了重新整理");
            RequireReadOnly();

            // 没有自动结论时也直接显示第一个候选，但仍须先预览、再独立确认，不能退回一阶段提交。
            aniBt.ManualCandidates = true;
            dialog = new AniBtScrapeDialog(service, scope, item) { XamlRoot = _root.XamlRoot };
            shown = MoviePilotDialog.ShowAsync(dialog, mp);
            await UntilAsync(() => dialog.ViewModel.IsReady);
            var manualScale = _root.XamlRoot.RasterizationScale;
            ResizeInspect((int)(1040 * manualScale), (int)(800 * manualScale));
            await LayoutAsync(dialog);
            Require(dialog.PrimaryButtonText == "预览计划" && dialog.IsPrimaryButtonEnabled
                && dialog.ViewModel.CanPreview && !dialog.ViewModel.CanSubmit,
                "AniBT 多候选没有保持先预览、后确认的主按钮契约");
            var manualRow = dialog.ViewModel.Rows[0];
            var manualChoice = Descendants(dialog).OfType<ComboBox>().Single(box => box.Name == "AnimeChoice");
            Require(manualRow.Selected?.BgmId == 123 && manualChoice.Items.Count == 2
                && manualChoice.SelectedItem is AniBtAnime { BgmId: 123 },
                "多个候选时没有直接显示第一个识别结果");
            await SaveProbeScreenAsync("anibt-scrape-candidates.png");
            // 本机 XAML 岛拍不进 BitBlt；另存一张对话框自身的渲染帧，报告里按「渲染帧」记。
            await SaveSettledFrameAsync(dialog, "anibt-scrape-candidates-render.png");
            await PreviewAsync(dialog);
            var cancelPreview = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CancelPreview");
            ((IInvokeProvider)new ButtonAutomationPeer(cancelPreview).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(dialog);
            Require(dialog.PrimaryButtonText == "预览计划" && dialog.ViewModel.CanPreview && !dialog.ViewModel.CanSubmit
                && !dialog.ViewModel.NoticeOpen && manualRow.PreviewDetails.Length == 0 && manualRow.Selected?.BgmId == 123
                && cancelPreview.Visibility == Visibility.Collapsed
                && Descendants(dialog).OfType<StackPanel>().Single(panel => panel.Name == "PreparedPlan").Visibility == Visibility.Collapsed,
                "取消 AniBT 多候选预览没有撤销原确认、收起实际计划或保留选择");
            close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(await shown == ContentDialogResult.None && moviePilot.MutationAttempts == 0
                && !dialog.ViewModel.CanPreview && !dialog.ViewModel.CanSubmit,
                "关闭多候选的 AniBT 识别仍然提交了重新整理或保留了确认");
            RequireReadOnly();
            aniBt.ManualCandidates = false;

            dialog = new AniBtScrapeDialog(service, scope, item) { XamlRoot = _root.XamlRoot };
            shown = MoviePilotDialog.ShowAsync(dialog, mp);
            await UntilAsync(() => dialog.ViewModel.IsReady);
            await LayoutAsync(dialog);
            await PreviewAsync(dialog);
            moviePilot.Offline = true;
            Require(!await mp.RefreshAvailabilityAsync(), "刮削确认的假 MP 断线没有被检测到");
            await UntilAsync(() => shown.IsCompleted);
            Require(await shown == ContentDialogResult.None && moviePilot.MutationAttempts == 0 && fixture.Actions.Plays == 0
                && !dialog.ViewModel.CanPreview && !dialog.ViewModel.CanSubmit,
                "MP 断线没有关闭已预览但未提交的 AniBT 确认，或意外提交了操作");
            RequireReadOnly();
        }
        finally
        {
            aniBt.CancelDelays();
            dialog?.Hide();
            if (shown is not null) await shown;
            ResizeInspect(1420, 980);
            _window?.Activate();
        }

        async Task PreviewAsync(AniBtScrapeDialog current)
        {
            Require(current.ViewModel.CanPreview && !current.ViewModel.CanSubmit && current.PrimaryButtonText == "预览计划",
                "AniBT 第一击必须是只读预览，不得调用确认提交");
            var primary = Descendants(current).OfType<Button>().Single(button => button.Name == "PrimaryButton"
                && Equals(button.Content, current.PrimaryButtonText));
            var started = false;
            void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(PageViewModel.Busy) && current.ViewModel.Busy) started = true;
            }
            current.ViewModel.PropertyChanged += Changed;
            try
            {
                ((IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke)).Invoke();
                // Invoke 可排队；先确认本次 Busy 已开始，避免把载入阶段的旧 IsReady 当成本次结束。
                // 不等 CanSubmit 变真：读取失败也应结束等待，再由下面的原强断言如实报错。
                await UntilAsync(() => started && current.ViewModel.IsReady && !current.ViewModel.Busy);
            }
            finally { current.ViewModel.PropertyChanged -= Changed; }
            var row = current.ViewModel.Rows.Single();
            Require(current.ViewModel.CanSubmit && current.PrimaryButtonText == "确认执行"
                && current.ViewModel.NoticeOpen && current.ViewModel.NoticeSeverity == InfoBarSeverity.Warning
                && row.PreviewDetails.Contains(series.Id, StringComparison.Ordinal)
                && row.PreviewDetails.Contains(series.Name, StringComparison.Ordinal)
                && row.PreviewDetails.Contains("整部剧", StringComparison.Ordinal)
                && row.PreviewDetails.Contains("Test Anime", StringComparison.Ordinal)
                && row.PreviewDetails.Contains("2026", StringComparison.Ordinal)
                && row.PreviewDetails.Contains("元数据", StringComparison.Ordinal) && row.PreviewDetails.Contains("图片", StringComparison.Ordinal),
                "AniBT 只读预览没有封存实际整剧、精确候选及覆盖影响：" + current.ViewModel.NoticeMessage);
            await LayoutAsync(current);
            Require(current.IsPrimaryButtonEnabled
                && Descendants(current).OfType<StackPanel>().Single(panel => panel.Name == "PreparedPlan").Visibility == Visibility.Visible
                && Descendants(current).OfType<TextBlock>().Single(text => text.Name == "PreviewDetailsText").Text == row.PreviewDetails
                && Descendants(current).OfType<Button>().Single(button => button.Name == "CancelPreview").Visibility == Visibility.Visible,
                "AniBT 封存计划、取消预览和第二阶段确认没有绑定到实际控件");
            RequireReadOnly();
        }

        void RequireReadOnly() => Require(moviePilot.MutationAttempts == 0 && fixture.Actions.Plays == 0
            && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)
                || request.StartsWith("POST ", StringComparison.Ordinal) && request.Contains("/Items/RemoteSearch/Series", StringComparison.Ordinal)),
            "AniBT 载入、预览、取消和断线不得发送整理、Emby Apply/Refresh 或播放");
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
                // 本机这个窗口的 XAML 岛拍不进 BitBlt（实屏是一整块窗口底色）；用页面自身渲染留视觉证据，
                // 文件名带 -render，报告里也按「渲染帧，不是实屏」记。
                await SaveSettledFrameAsync(page, $"anibt-search-{width}-render.png");
            }

            await firstAnime.OpenCommand.ExecuteAsync(null);
            var browser = panel.ViewModel.Browser;
            Require(panel.ViewModel.SelectedAnime?.BgmId == 123 && browser.Rows.Count == 3,
                $"选择番剧后没有读取对应 AniBT 资源：Rows={browser.Rows.Count}，Empty={browser.EmptyMessage}，"
                + $"Busy={panel.ViewModel.Busy}，提示开={panel.ViewModel.NoticeOpen}，提示={panel.ViewModel.NoticeMessage}，"
                + $"请求={string.Join("、", aniBt.Requests)}，筛选=({browser.SelectedEpisode}|{browser.SelectedResolution}|{browser.SelectedGroup}|{browser.Text})，"
                + $"计数={browser.CountText}");
            Require(aniBt.Requests.Any(path => path == "/rss/anime.xml?bgmId=123"),
                "资源查询没有绑定所选番剧的 Bangumi 编号");
            Require(panel.ViewModel.SelectedTitle == firstAnime.Title && panel.ViewModel.SelectedBgm == "Bangumi #123",
                "番剧头部没有用上所选番剧的标题或 Bangumi 身份");
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
            browser.ResetFiltersCommand.Execute(null);
            Require(browser.Rows.Count == 3 && browser.Text.Length == 0 && browser.SelectedEpisode == AniBtBrowserViewModel.AllEpisodes,
                "清除筛选没有恢复全部发布");

            var row = browser.Rows[0];
            Require(row.CanDownload && row.Resource.MediaInfo.ValueKind != System.Text.Json.JsonValueKind.Object,
                "未开启 Bangumi 来源时没有保留可交给 MP 的公开种子");
            Require(row.MetaLine.Contains("1080p", StringComparison.Ordinal) && row.MetaLine.Contains("离线字幕组", StringComparison.Ordinal),
                "发布行没有显示 AniBT 的字幕组或清晰度：" + row.MetaLine);
            await LayoutAsync(panel);
            Require(Get<ListView>(Get<AniBtResourceBrowser>(panel, "ResourceBrowser"), "ReleaseList").Items.Count == 3,
                "AniBT 发布列表没有绑定筛选后的三行");
            foreach (var width in new[] { 1040, 360 })
            {
                await SizeAsync(page, width);
                await SaveSettledFrameAsync(page, $"anibt-resources-{width}-render.png");
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
        internal bool ManualCandidates { get; set; }
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

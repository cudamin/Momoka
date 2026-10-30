using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using Momoka.AniBt;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Platform;
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
        var originalTheme = ThemeHost.Current;
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
            // Deliberately misclassified: identification must not depend on an anime genre.
            Genres = ["剧情"],
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
            Genres = ["剧情"]
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
        MenuFlyout? menu = null;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        var anchor = new Button
        {
            Content = "非动漫剧集菜单",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(120, 80, 0, 0)
        };
        _root.Children.Add(shell);
        _root.Children.Add(anchor);
        var topMost = _window!.TopMost;
        try
        {
            _window.TopMost = true;
            foreach (var width in new[] { 1040, 760 })
            {
                ResizeProbeClient(width, 800);
                await LayoutAsync(anchor);
                foreach (var type in new[] { EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
                {
                    var subject = scope.Bind(new EmbyItem { Id = "non-anime-menu", Type = type, Genres = ["剧情"] });
                    menu = ItemCommands.Build(fixture.Session, shell, anchor, new CardItem(subject, fixture.Images, 170));
                    var reorganize = menu.Items.OfType<MenuFlyoutSubItem>().Single(row => row.Tag is ItemCommand.MoviePilotReorganizeMenu);
                    var entry = reorganize.Items.OfType<MenuFlyoutItem>().Single(row => row.Tag is ItemCommand.AniBtScrape);
                    Require(entry.Text == "AniBT 识别"
                        && !menu.Items.OfType<MenuFlyoutSubItem>().Any(row => row.Tag is ItemCommand.MoviePilotVersions),
                        "非动漫剧、季或单集没有识别入口，或误放宽 AniBT 版本搜索");
                    menu.ShowAt(anchor);
                    await LayoutAsync(anchor);
                    ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(reorganize)
                        .GetPattern(PatternInterface.ExpandCollapse)).Expand();
                    await LayoutAsync(anchor);
                    Require(FrameworkElementAutomationPeer.CreatePeerForElement(entry).GetName() == "AniBT 识别",
                        "非动漫识别菜单没有可定位的 UIA 名称");
                    await SaveProbeScreenAsync($"anibt-non-anime-menu-{type}-{width}.png");
                    var closed = Pending<bool>();
                    menu.Closed += (_, _) => closed.TrySetResult(true);
                    menu.Hide();
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await UntilAsync(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                        .Any(popup => popup.Child is MenuFlyoutPresenter));
                }
            }
            _root.Children.Remove(anchor);
            _root.Children.Remove(shell);
            dialog = new AniBtScrapeDialog(service, scope, item) { XamlRoot = _root.XamlRoot };
            shown = MoviePilotDialog.ShowAsync(dialog, mp);
            await UntilAsync(() => dialog.ViewModel.IsReady);
            Require(dialog.ViewModel.Rows.Count == 1 && dialog.ViewModel.CanSubmit
                && !dialog.ViewModel.Busy && !dialog.ViewModel.NoticeOpen,
                "AniBT 识别没有自动读取并显示可整理的实际目标");
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
                    && choice.IsEnabled && dialog.IsPrimaryButtonEnabled && dialog.PrimaryButtonText == "重新整理",
                    "AniBT 候选没有绑定到实际选择控件或重新整理按钮");
                await SaveProbeScreenAsync($"anibt-scrape-{width}.png");
            }
            await PreviewAsync(dialog);
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(dialog);
                var title = Descendants(dialog).OfType<TextBlock>().Single(text => text.Name == "PreviewHeadingText");
                var episodeText = Descendants(dialog).OfType<TextBlock>().Single(text => text.Name == "EpisodeLabelText");
                var fileText = Descendants(dialog).OfType<TextBlock>().Single(text => text.Name == "DestinationFileText");
                var methodText = Descendants(dialog).OfType<TextBlock>().Single(text => text.Name == "TransferMethodText");
                var primary = Descendants(dialog).OfType<Button>().Single(button => button.Name == "PrimaryButton");
                Require(title.Foreground is SolidColorBrush titleBrush && titleBrush.Color == ThemeHost.ToColor(theme.Colors.Accent)
                    && episodeText.Foreground is SolidColorBrush episodeBrush && episodeBrush.Color == ThemeHost.ToColor(theme.Colors.Accent)
                    && fileText.Foreground is SolidColorBrush fileBrush && fileBrush.Color == ThemeHost.ToColor(theme.Colors.Accent)
                    && methodText.Text == row.TransferMethod
                    && primary.Background is SolidColorBrush buttonBrush && buttonBrush.Color == ThemeHost.ToColor(theme.Colors.Accent),
                    "AniBT 关键字段没有随当前主题更新或仍显示日志说明");
                await SaveProbeScreenAsync($"anibt-scrape-{theme.Id}-screen.png");
            }
            ThemeHost.Apply(originalTheme);
            await LayoutAsync(dialog);
            await SaveProbeScreenAsync("anibt-scrape-preview.png");
            var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(await shown == ContentDialogResult.None && moviePilot.MutationAttempts == 0
                && !dialog.ViewModel.CanPreview && !dialog.ViewModel.CanSubmit,
                "关闭 AniBT 已预览计划没有撤销确认，或仍然提交了重新整理");
            RequireReadOnly();

            // 没有自动结论时仍显示第一个候选，实际目标自动读取，打开/改选都不写入。
            aniBt.ManualCandidates = true;
            dialog = new AniBtScrapeDialog(service, scope, item) { XamlRoot = _root.XamlRoot };
            shown = MoviePilotDialog.ShowAsync(dialog, mp);
            await UntilAsync(() => dialog.ViewModel.IsReady);
            var manualScale = _root.XamlRoot.RasterizationScale;
            ResizeInspect((int)(1040 * manualScale), (int)(800 * manualScale));
            await LayoutAsync(dialog);
            Require(dialog.PrimaryButtonText == "重新整理" && dialog.IsPrimaryButtonEnabled
                && dialog.ViewModel.CanSubmit && !dialog.ViewModel.Busy,
                "AniBT 多候选没有自动显示实际目标与一键重新整理按钮");
            var manualRow = dialog.ViewModel.Rows[0];
            await LayoutAsync(dialog);
            await UntilAsync(() => Descendants(dialog).OfType<ComboBox>().Any(box => box.Name == "AnimeChoice"));
            var manualChoice = Descendants(dialog).OfType<ComboBox>().Single(box => box.Name == "AnimeChoice");
            Require(manualRow.Selected?.BgmId == 123 && manualChoice.Items.Count == 2
                && manualChoice.SelectedItem is AniBtAnime { BgmId: 123 },
                "多个候选时没有直接显示第一个识别结果");
            await SaveProbeScreenAsync("anibt-scrape-candidates.png");
            // 本机 XAML 岛拍不进 BitBlt；另存一张对话框自身的渲染帧，报告里按「渲染帧」记。
            await SaveSettledFrameAsync(dialog, "anibt-scrape-candidates-render.png");
            await PreviewAsync(dialog);
            // 改选为空立即撤销旧凭证；选回后自动读取，不需点击预览。
            manualChoice.SelectedItem = null;
            await LayoutAsync(dialog);
            Require(!dialog.ViewModel.CanSubmit && manualRow.PreviewDetails.Length == 0
                && Descendants(dialog).OfType<StackPanel>().Single(panel => panel.Name == "PreparedPlan").Visibility == Visibility.Collapsed,
                "取消候选没有立即撤销旧整理目标");
            manualChoice.SelectedItem = manualRow.Candidates[0];
            await UntilAsync(() => dialog.ViewModel.IsReady && !dialog.ViewModel.Busy);
            await PreviewAsync(dialog);
            await SaveProbeScreenAsync("anibt-scrape-selection-restored.png");
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
            menu?.Hide();
            _window.TopMost = topMost;
            ThemeHost.Apply(originalTheme);
            _root.Children.Remove(anchor);
            _root.Children.Remove(shell);
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            aniBt.CancelDelays();
            dialog?.Hide();
            if (shown is not null) await shown;
            ResizeInspect(1420, 980);
            _window?.Activate();
        }

        async Task PreviewAsync(AniBtScrapeDialog current)
        {
            await UntilAsync(() => current.ViewModel.IsReady && !current.ViewModel.Busy);
            var row = current.ViewModel.Rows.Single();
            Require(current.ViewModel.CanSubmit && current.PrimaryButtonText == "重新整理"
                && !current.ViewModel.NoticeOpen
                && row.PreviewDetails.Contains(series.Id, StringComparison.Ordinal)
                && row.PreviewDetails.Contains(series.Name, StringComparison.Ordinal)
                && row.PreviewDetails.Contains("整部剧", StringComparison.Ordinal)
                && row.PreviewDetails.Contains("Test Anime", StringComparison.Ordinal)
                && row.PreviewDetails.Contains("2026", StringComparison.Ordinal)
                && row.PreviewDetails.Contains("元数据", StringComparison.Ordinal) && row.PreviewDetails.Contains("图片", StringComparison.Ordinal),
                "AniBT 自动目标没有封存实际整剧、精确候选及覆盖影响：" + current.ViewModel.NoticeMessage);
            await LayoutAsync(current);
            Require(current.IsPrimaryButtonEnabled
                && Descendants(current).OfType<StackPanel>().Single(panel => panel.Name == "PreparedPlan").Visibility == Visibility.Visible
                && Descendants(current).OfType<TextBlock>().Single(text => text.Name == "PreviewHeadingText").Text == row.DestinationTitle
                && Descendants(current).OfType<TextBlock>().Single(text => text.Name == "TargetWarningText").Text == row.TargetWarning
                && Descendants(current).OfType<Button>().Single(button => button.Name == "RetryPrepare").Visibility == Visibility.Collapsed,
                "AniBT 自动整理目标与一键重新整理没有绑定到实际控件");
            RequireReadOnly();
        }

        void RequireReadOnly() => Require(moviePilot.MutationAttempts == 0 && fixture.Actions.Plays == 0
            && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)
                || request.StartsWith("POST ", StringComparison.Ordinal) && request.Contains("/Items/RemoteSearch/Series", StringComparison.Ordinal)),
            "AniBT 载入、预览、取消和断线不得发送整理、Emby Apply/Refresh 或播放");
    }

    private async Task AniBtSearchAsync()
    {
        var originalTheme = ThemeHost.Current;
        var moviePilot = new AniBtMoviePilotProbeServer();
        var aniBt = new AniBtProbeServer();
        using var fixture = CreateFixture(moviePilot, aniBt);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var frame = new Frame();
        var launcher = new AniBtRecordingLauncher();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            var tabs = Get<Segmented>(page, "SourceTabs");
            Require(tabs.Visibility == Visibility.Visible && tabs.Items.Count == 4, "连接后未显示 AniBT 搜索栏");
            var aniBtTab = tabs.Items.OfType<SegmentedItem>().Single(item => item.Name == "AniBtSourceTab");
            Require(aniBtTab.Visibility == Visibility.Visible, "默认设置下 AniBT 搜索来源被隐藏");
            fixture.Settings.MoviePilot.AniBtEnabled = false;
            await UntilAsync(() => aniBtTab.Visibility == Visibility.Collapsed);
            Require(tabs.SelectedIndex == 0 && Get<Grid>(page, "EmbyContent").Visibility == Visibility.Visible,
                "关闭 AniBT 后搜索页仍停在 AniBT 面板");
            fixture.Settings.MoviePilot.AniBtEnabled = true;
            await UntilAsync(() => aniBtTab.Visibility == Visibility.Visible);
            tabs.SelectedIndex = 3;
            var panel = Get<AniBtSearchPanel>(page, "AniBtPanel");
            Field(panel.ViewModel, "_launcher", launcher);
            panel.ViewModel.OpenAnimeSiteCommand.Execute("anibt");
            Require(launcher.Urls.Count == 0, "未选番剧时不应打开 AniBT");
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
            // The VM completes before the enabled/visible bindings reach the list. Settle
            // that real layout before moving focus out of the submitted search field.
            await LayoutAsync(page);
            var focusResults = Get<GridView>(panel, "AnimeResults");
            Require(focusResults.IsEnabled && focusResults.ActualHeight > 0
                && focusResults.Focus(FocusState.Programmatic),
                "搜索完成并布局后番剧列表无法接收验证焦点");
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
                CheckCompactSearch(page, panel);
                var search = Get<Border>(page, "SearchSurface");
                page.ExpandSearchOptions();
                await LayoutAsync(page);
                Require(tabs.ActualWidth <= search.ActualWidth + 1, "四个搜索栏超出搜索区域");
                var list = Get<GridView>(panel, "AnimeResults");
                Require(Descendants(list).OfType<Button>().Any(button => button.Name == "OpenAnimeResources"
                    && AutomationProperties.GetName(button).Contains(firstAnime.Title, StringComparison.Ordinal)),
                    "番剧行的资源按钮没有可定位的自动化名称");
                // 本机这个窗口的 XAML 岛拍不进 BitBlt（实屏是一整块窗口底色）；用页面自身渲染留视觉证据，
                // 文件名带 -render，报告里也按「渲染帧，不是实屏」记。
                await SaveSettledFrameAsync(page, $"anibt-search-{width}-render.png");
            }

            var selectionInput = Get<AutoSuggestBox>(page, "SearchInput").Text;
            Require(selectionInput == "离线动画" && page.ViewModel.SearchText == "离线动画"
                && panel.ViewModel.Results.Count == 2 && panel.ViewModel.Results.Contains(firstAnime)
                && panel.ViewModel.CanChooseAnime,
                $"打开资源前搜索状态已变化：输入={selectionInput}，VM输入={page.ViewModel.SearchText}，"
                + $"结果={panel.ViewModel.Results.Count}，原行仍在={panel.ViewModel.Results.Contains(firstAnime)}，"
                + $"可选择={panel.ViewModel.CanChooseAnime}");
            await firstAnime.OpenCommand.ExecuteAsync(null);
            panel.ViewModel.SelectedArtwork = poster;
            var browser = panel.ViewModel.Browser;
            // 图片绑定不走 HTTP handler；只给实际模板本地夹具图，拒绝触发外站下载。
            var logoFile = Path.Combine(_options.Paths.LogDirectory, "anibt-group-logo.png");
            await File.WriteAllBytesAsync(logoFile, await FixtureImageAsync(48, 48));
            using var logoStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(logoStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(await File.ReadAllBytesAsync(logoFile));
                await writer.StoreAsync();
            }
            logoStream.Seek(0);
            var logoBitmap = new BitmapImage();
            await logoBitmap.SetSourceAsync(logoStream);
            var fixtureRows = browser.Rows.ToArray();
            var fixtureGroups = browser.ProgressGroups.Select((group, index) => new AniBtReleaseGroup(
                "group_" + index, group.Name, "offline", group.Cells.Where(cell => cell.Published).Select(cell => cell.Episode).ToArray(),
                group.Total, group.Rows.Select(release => release.Release).ToArray(), group.RssUri,
                index == 0 ? new Uri(logoFile).AbsoluteUri : null)).ToArray();
            browser.SetResults(fixtureRows, "", fixtureGroups, panel.ViewModel.SelectedAnime);
            Require(panel.ViewModel.SelectedAnime?.BgmId == 123 && browser.Rows.Count == 3,
                $"选择番剧后没有读取对应 AniBT 资源：Rows={browser.Rows.Count}，Empty={browser.EmptyMessage}，"
                + $"Busy={panel.ViewModel.Busy}，提示开={panel.ViewModel.NoticeOpen}，提示={panel.ViewModel.NoticeMessage}，"
                + $"请求={string.Join("、", aniBt.Requests)}");
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
            Require(browser.Rows.Select(row => row.Release.ReleaseId).SequenceEqual(["rel_one", "rel_two", "rel_batch"]),
                "移除筛选后没有完整保留按最新发布排序的资源");
            var selected = panel.ViewModel.SelectedAnime!;
            foreach (var id in new[] { selected.BgmId, 505550L })
            {
                panel.ViewModel.SelectedAnime = selected with { BgmId = id };
                foreach (var view in new[] { 0, 1 })
                {
                    browser.ViewIndex = view;
                    panel.ViewModel.OpenAnimeSiteCommand.Execute("anibt");
                    var expected = $"https://anibt.net/anime/{id}" + (view == 1 ? "?tab=releases" : "");
                    Require(launcher.Urls.LastOrDefault() == expected, "打开 AniBT 没有使用当前番剧及页签地址");
                }
            }
            panel.ViewModel.SelectedAnime = selected;
            panel.ViewModel.SelectedArtwork = poster;

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
                CheckCompactSearch(page, panel);
                Require(!Descendants(panel).OfType<FrameworkElement>().Any(element => element.Name is
                    "ReleaseFilter" or "CopyAnimeRss" or "CopyGroupRss" or "CopyGroupProgressRss" or "RssNotice"
                    or "EpisodeFilter" or "GroupFilter" or "ResolutionFilter" or "SortOrder" or "ResetFilters"),
                    "AniBT 搜索仍保留已删除的 RSS 或资源筛选组件");
                var openAniBt = Get<Button>(panel, "OpenAniBtSite");
                var returnToAnime = Get<Button>(panel, "ReturnToAnime");
                var heroActions = (WrapRow)VisualTreeHelper.GetParent(openAniBt);
                Require(heroActions.Children.IndexOf(openAniBt) < heroActions.Children.IndexOf(returnToAnime)
                    && Equals(openAniBt.Content, "打开 AniBT"), "打开 AniBT 没有放在返回番剧之前");
                browser.ViewIndex = 0;
                await LayoutAsync(panel);
                CheckReleaseTabs(resourceBrowser, 0);
                await InvokeOpenAniBtAsync(openAniBt, false);
                SetProbeGroupLogos(resourceBrowser, logoBitmap);
                await SaveSettledFrameAsync(page, $"anibt-detail-{width}-render.png");
                await SaveProbeScreenAsync($"anibt-detail-{width}-screen.png");
                detailScroll.ChangeView(null, width < 600 ? resourceBrowser.TransformToVisual(Get<StackPanel>(panel, "AnimeResourcesView"))
                    .TransformPoint(new Windows.Foundation.Point()).Y : 0, null, true);
                await LayoutAsync(panel);
                await SaveSettledFrameAsync(page, $"anibt-progress-{width}-render.png");
                Get<Segmented>(resourceBrowser, "ReleaseTabs").SelectedIndex = 1;
                await LayoutAsync(panel);
                Require(browser.ViewIndex == 1, "资源页签选择没有回写视图模型");
                CheckReleaseTabs(resourceBrowser, 1);
                await InvokeOpenAniBtAsync(openAniBt, true);
                detailScroll.ChangeView(null, resourceBrowser.TransformToVisual(Get<StackPanel>(panel, "AnimeResourcesView"))
                    .TransformPoint(new Windows.Foundation.Point()).Y, null, true);
                await LayoutAsync(panel);
                SetProbeGroupLogos(resourceBrowser, logoBitmap);
                await LayoutAsync(panel);
                var releaseList = Get<ItemsRepeater>(resourceBrowser, "ReleaseList");
                var releaseRows = Descendants(releaseList).OfType<Grid>().Where(grid => grid.Name == "ReleaseRow").ToArray();
                Require(releaseRows.Length == 3, "AniBT 行尾按钮检查没有取得全部发布行");
                foreach (var releaseRow in releaseRows) CheckReleaseRow(releaseRow);
                var listY = releaseList.TransformToVisual(resourceBrowser).TransformPoint(default).Y;
                var tabsElement = Get<Segmented>(resourceBrowser, "ReleaseTabs");
                var tabsBottom = tabsElement.TransformToVisual(resourceBrowser).TransformPoint(default).Y + tabsElement.ActualHeight;
                Require(listY - tabsBottom is >= 0 and <= 9, "删除筛选后页签和资源列表之间仍有空槽");
                if (width == 1040)
                    Require(releaseRows.All(release => release.ActualHeight <= 70), "AniBT 宽窗发布行仍不够紧凑");
                var logos = Descendants(releaseList).OfType<Image>().Where(image => image.Name == "GroupLogo").ToArray();
                var shownLogo = logos.Single(image => image.Tag is AniBtResourceGroup { LogoUrl: not null });
                Require(browser.ResourceGroups[0].LogoVisibility == Visibility.Visible
                    && browser.ResourceGroups[1].LogoVisibility == Visibility.Collapsed,
                    "AniBT 有头像和无头像的小组没有分别显示与收起");
                await UntilAsync(() => shownLogo.Source is BitmapImage { PixelWidth: > 0 });
                Require(shownLogo.ActualWidth == 24 && shownLogo.ActualHeight == 24,
                    $"AniBT 小组头像没有保持紧凑的正方形：{shownLogo.ActualWidth}×{shownLogo.ActualHeight}");
                var stateButton = Descendants(releaseList).OfType<Button>().Single(button =>
                    button.Name == "DownloadResource" && ReferenceEquals(button.Command, row.DownloadCommand));
                foreach (var state in new[] { MoviePilotDownloadState.Working, MoviePilotDownloadState.Done,
                    MoviePilotDownloadState.Failed, MoviePilotDownloadState.Uncertain, MoviePilotDownloadState.Idle })
                {
                    row.State = state;
                    await LayoutAsync(panel);
                    Require(Equals(ToolTipService.GetToolTip(stateButton), row.DownloadLabel)
                        && stateButton.Content is FontIcon icon && icon.Glyph == row.DownloadGlyph
                        && AutomationProperties.GetName(stateButton) == row.DownloadAutomationName,
                        "AniBT 下载图标、悬停说明或辅助名称没有跟随状态更新");
                    Require(stateButton.ActualWidth == 36 && stateButton.ActualHeight == 36,
                        "AniBT 下载状态变化改变了方钮尺寸");
                }
                await SaveSettledFrameAsync(page, $"anibt-resources-{width}-render.png");
                await SaveProbeScreenAsync($"anibt-resources-{width}-screen.png");
                if (width == 1040)
                {
                    foreach (var theme in Momoka.Theming.UiThemes.All)
                    {
                        ThemeHost.Apply(theme);
                        await LayoutAsync(panel);
                        CheckReleaseTabs(resourceBrowser, 1);
                        foreach (var action in releaseRows.SelectMany(release => Descendants(release).OfType<Button>()))
                            Require(action.Background is SolidColorBrush background
                                && background.Color == ThemeHost.ToColor(theme.Colors.AccentSoft)
                                && action.Foreground is SolidColorBrush foreground
                                && foreground.Color == ThemeHost.ToColor(theme.Colors.Accent),
                                "AniBT 行尾方钮没有采用当前主题的 MP 资源角色色");
                        await SaveProbeScreenAsync($"anibt-resources-{theme.Id}-1040-screen.png");
                    }
                    ThemeHost.Apply(originalTheme);
                    await LayoutAsync(panel);
                }
                detailScroll.ChangeView(null, 0, null, true);
            }

            await SizeAsync(page, 1040);
            var avatarBrowser = Get<AniBtResourceBrowser>(panel, "ResourceBrowser");
            browser.ViewIndex = 1;
            await LayoutAsync(panel);
            var logoToFail = Descendants(Get<ItemsRepeater>(avatarBrowser, "ReleaseList")).OfType<Image>()
                .Single(image => image.Tag is AniBtResourceGroup { LogoUrl: not null });
            logoToFail.Source = new BitmapImage(new Uri(Path.Combine(_options.Paths.LogDirectory, "missing-anibt-group-logo.png")));
            await UntilAsync(() => browser.ResourceGroups[0].LogoVisibility == Visibility.Collapsed);
            await LayoutAsync(panel);
            Require(Descendants(avatarBrowser).OfType<Border>().Where(border => border.Name == "GroupLogoSurface")
                .All(border => border.Visibility == Visibility.Collapsed),
                "AniBT 头像加载失败后没有完整收起头像容器");
            var cancelled = row.DownloadCommand.ExecuteAsync(null);
            var dialog = await DownloadDialogAsync();
            await UntilAsync(() => !dialog.ViewModel.Busy);
            Require(dialog.DefaultButton == ContentDialogButton.None && !dialog.ViewModel.NoticeOpen,
                "AniBT 下载确认未正确载入选项或普通回车会触发默认按钮");
            Require(dialog.ViewModel.CanSubmit && dialog.ViewModel.Options.Media is null, "未识别的 AniBT 资源不能按默认方式下载");
            await dialog.ViewModel.ToggleAdvancedCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.CanSubmit && dialog.ViewModel.MediaType == MoviePilotDownloadViewModel.AutomaticType,
                "打开高级选项误改未识别 AniBT 资源的类型");
            await dialog.ViewModel.ToggleAdvancedCommand.ExecuteAsync(null);
            Require(dialog.ViewModel.CanSubmit && dialog.ViewModel.Options.Media is null, "收起高级选项误阻止默认 AniBT 下载");
            await SaveProbeScreenAsync("anibt-download-confirm.png");
            var close = Descendants(dialog).OfType<Button>().Single(button => button.Name == "CloseButton" && Equals(button.Content, dialog.CloseButtonText));
            ((IInvokeProvider)new ButtonAutomationPeer(close).GetPattern(PatternInterface.Invoke)).Invoke();
            await cancelled;
            Require(moviePilot.MutationAttempts == 0 && row.State == MoviePilotDownloadState.Idle,
                "取消 AniBT 下载仍然发送写请求或锁住结果行");

            panel.ViewModel.ReturnToResultsCommand.Execute(null);
            var openedBeforeReturn = launcher.Urls.Count;
            panel.ViewModel.OpenAnimeSiteCommand.Execute("anibt");
            Require(launcher.Urls.Count == openedBeforeReturn, "返回番剧列表后旧网页命令仍打开链接");
            Require(ReferenceEquals(results, panel.ViewModel.Results) && ReferenceEquals(firstAnime, results[0])
                && panel.ViewModel.SelectedAnime is null && panel.ViewModel.Browser.Rows.Count == 0,
                "返回番剧没有恢复原列表或残留了旧资源");
            var searchText = Get<AutoSuggestBox>(page, "SearchInput").Text;
            Require(searchText == "离线动画" && page.ViewModel.SearchText == "离线动画"
                && panel.ViewModel.Results.Count == 2 && panel.ViewModel.Results.Contains(firstAnime)
                && panel.ViewModel.CanChooseAnime,
                $"打开资源前搜索状态已变化：输入={searchText}，VM输入={page.ViewModel.SearchText}，"
                + $"结果={panel.ViewModel.Results.Count}，原行仍在={panel.ViewModel.Results.Contains(firstAnime)}，"
                + $"可选择={panel.ViewModel.CanChooseAnime}");
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
            var openedBeforeOffline = launcher.Urls.Count;
            panel.ViewModel.OpenAnimeSiteCommand.Execute("anibt");
            Require(launcher.Urls.Count == openedBeforeOffline, "断线后旧网页命令仍打开链接");
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
            ThemeHost.Apply(originalTheme);
            aniBt.CancelDelays();
            foreach (var dialog in OpenDownloads()) dialog.Hide();
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
            _window?.Activate();
        }

        static void CheckReleaseTabs(AniBtResourceBrowser resourceBrowser, int selectedIndex)
        {
            var releaseTabs = Get<Segmented>(resourceBrowser, "ReleaseTabs");
            Require(releaseTabs.SelectedIndex == selectedIndex && releaseTabs.BorderThickness == new Thickness(0)
                && (releaseTabs.Background is null || releaseTabs.Background is SolidColorBrush { Color.A: 0 }),
                "发布进度和资源页签仍有外框或背景");
            foreach (var item in releaseTabs.Items.OfType<SegmentedItem>())
            {
                // Pivot item 继承的边框属性不参与绘制，检查实际模板的 Grid。
                var templateRoot = Descendants(item).OfType<Grid>().Single(grid => grid.Name == "PART_Root");
                Require(templateRoot.BorderThickness == new Thickness(0)
                    && (templateRoot.Background is null || templateRoot.Background is SolidColorBrush { Color.A: 0 }),
                    "页签实际模板仍有外框或背景");
            }
            var selectedItem = (SegmentedItem)releaseTabs.Items[selectedIndex];
            var underline = Descendants(selectedItem).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>()
                .Single(shape => shape.Name == "PART_Pill");
            Require(underline.ActualHeight > 0 && underline.Fill is SolidColorBrush brush
                && brush.Color == ThemeHost.ToColor(ThemeHost.Current.Colors.Accent),
                "无框页签没有当前主题的下划线");
        }

        async Task InvokeOpenAniBtAsync(Button button, bool releases)
        {
            var before = launcher.Urls.Count;
            var peer = new ButtonAutomationPeer(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => launcher.Urls.Count == before + 1);
            Require(launcher.Urls[^1] == "https://anibt.net/anime/123" + (releases ? "?tab=releases" : ""),
                "打开 AniBT 按钮没有执行当前页签网页路径");
        }

        static void CheckCompactSearch(LibraryPage page, AniBtSearchPanel panel)
        {
            var surface = Get<Border>(page, "SearchSurface");
            var pageRoot = Get<Grid>(page, "PageRoot");
            Require(Get<TextBlock>(page, "LibraryTitle").Visibility == Visibility.Collapsed
                && surface.BorderThickness == new Thickness(0) && surface.Padding == new Thickness(0)
                && surface.Background is null, "搜索标题或外层卡片未删除");
            var top = surface.TransformToVisual(pageRoot).TransformPoint(default).Y;
            Require(surface.Margin.Top is 4 or 8 && Math.Abs(top - surface.Margin.Top) <= 1
                && pageRoot.Margin.Top == 40,
                "搜索组件没有整体上移或侵入窗口按钮区域");
            Require(surface.TransformToVisual(page).TransformPoint(default).Y >= 32,
                "搜索输入与原生窗口按钮重叠");
            var bottom = surface.TransformToVisual(page).TransformPoint(default).Y + surface.ActualHeight;
            var gap = panel.TransformToVisual(page).TransformPoint(default).Y - bottom;
            Require(gap is >= 7 and <= 9, $"搜索区到 AniBT 内容仍有空槽：{gap:F1}");
            var content = Get<Grid>(panel, "AnimeContent");
            var root = Get<Grid>(panel, "AnimeSearchRoot");
            Require(Math.Abs(content.TransformToVisual(root).TransformPoint(default).Y) <= 1,
                "AniBT 内置输入、进度或提示收起后仍占空行间距");
        }

        static void SetProbeGroupLogos(AniBtResourceBrowser resourceBrowser, BitmapImage bitmap)
        {
            foreach (var image in Descendants(resourceBrowser).OfType<Image>().Where(image => image.Name == "GroupLogo"
                && image.Tag is AniBtResourceGroup { LogoUrl: not null })) image.Source = bitmap;
        }

        void CheckReleaseRow(Grid releaseRow)
        {
            var actions = Descendants(releaseRow).OfType<StackPanel>().Single(panel => panel.Name == "ReleaseActions");
            var text = Descendants(releaseRow).OfType<StackPanel>().Single(panel => panel.Name == "ReleaseText");
            var actionX = actions.TransformToVisual(releaseRow).TransformPoint(new Windows.Foundation.Point()).X;
            Require(Math.Abs(actionX + actions.ActualWidth - releaseRow.ActualWidth) < 1
                && text.ActualWidth + 11 <= actionX, "AniBT 行尾操作没有靠右或遮住标题规格");
            var buttons = actions.Children.OfType<Button>().ToArray();
            Require(buttons.Select(button => button.Name).SequenceEqual(new[] { "OpenTorrentPage", "DownloadResource" }),
                "AniBT 发布页和下载按钮顺序或局部句柄不正确");
            foreach (var button in buttons)
            {
                Require(button.ActualWidth == 36 && button.ActualHeight == 36
                    && button.CornerRadius.TopLeft > 0 && button.CornerRadius.TopLeft < 18,
                    "AniBT 行尾按钮不是圆角正方形");
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                Require(peer is not null && peer.GetName().Length > 0 && peer.GetAutomationId() == button.Name,
                    "AniBT 图标按钮没有可定位的 UIA 句柄或辅助名称");
            }
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

    private sealed class AniBtRecordingLauncher : ISystemLauncher
    {
        internal List<string> Urls { get; } = [];
        public void OpenFolder(string path) => throw new InvalidOperationException("AniBT 链接探针不打开目录");
        public void OpenUrl(string url) => Urls.Add(url);
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
            // 壳层的 MoviePilot 定时能力检查在探针超过 30 秒时也会走这里；只答只读身份，不授予管理能力。
            if (path.EndsWith("/user/current", StringComparison.Ordinal))
                return Task.FromResult(Json(new { is_active = true, is_superuser = false, permissions = new { manage = false } }));
            if (path.EndsWith("/system/global/user", StringComparison.Ordinal))
                return Task.FromResult(Json(new { AI_AGENT_ENABLE = false }));
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

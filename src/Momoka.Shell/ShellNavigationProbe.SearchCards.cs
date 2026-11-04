using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotSearchCardsAsync()
    {
        using var posters = new SearchPosterServer(await FixtureImageAsync(400, 600));
        var server = new SearchProbeServer
        {
            MediaResults = Enumerable.Range(0, 8).Select(index => (object)new
            {
                title = index == 2 ? "一念永恒：长标题与缺失海报" : "一念永恒",
                media_source = index % 2 == 0 ? "themoviedb" : "douban",
                media_id = index == 7 ? null : (index + 1).ToString(),
                type = "电视剧",
                vote_average = index % 2 == 0 ? 8.8 : 0,
                year = 2026,
                poster_path = index == 2 ? null : posters.Url
            }).ToArray()
        };
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var panel = new MoviePilotSearchPanel { Margin = new Thickness(24) };
        var resourceView = new MoviePilotResourceBrowser { Margin = new Thickness(24) };
        var browser = new MoviePilotResourceBrowserViewModel();
        var opened = new List<(MoviePilotMedia Media, MoviePilotSubscription? Subscription)>();
        resourceView.Attach(browser);
        panel.Attach(service, fixture.Launcher, (media, subscription) => opened.Add((media, subscription)));
        await panel.SearchAsync("一念永恒");
        _root.Children.Add(panel);
        await LayoutAsync(panel);
        // 初次绑定来源目录会触发 SelectionChanged；等该查询结束再装视觉夹具。
        await UntilAsync(() => !panel.ViewModel.Busy);
        await UntilAsync(() => panel.ViewModel.Results.Count == 8 && panel.ViewModel.Results[0].Poster?.PixelWidth > 0);
        panel.ViewModel.Results[1].State = MoviePilotSubscribeState.Done;
        panel.ViewModel.Results[3].State = MoviePilotSubscribeState.Uncertain;
        // 点卡片=进这部媒体的订阅页（2026-10-28 用户令）；这里没有订阅列表，走的正是媒体页那一支。
        var firstResult = panel.ViewModel.Results[0];
        panel.OpenResult(firstResult);
        Require(!Get<ItemsView>(panel, "Results").IsItemInvokedEnabled
            && opened.Count == 1 && ReferenceEquals(opened[0].Media, firstResult.Media) && opened[0].Subscription is null,
            "点卡片没有打开对应媒体的订阅页");
        Require(Descendants(panel).OfType<Button>().Any(button => button.Name == "OpenMedia"), "卡片缺少独立详情按钮");
        var alien = new MoviePilotResult(firstResult.Media with { MediaId = "999" },
            _ => Task.CompletedTask, _ => Task.CompletedTask);
        panel.OpenResult(alien);
        Require(opened.Count == 1, "不在当前结果集的卡片仍能打开订阅页");
        _root.Children.Remove(panel);
        var rows = Enumerable.Range(0, 12).Select(index => new MoviePilotResourceRow(new MoviePilotResource
        {
            Title = $"The Affair Was Just The Beginning S01E{index + 1:00} 2026 1080p friDay WEB-DL AAC2.0 H.264-MWeb",
            RecognizedTitle = index % 3 == 0 ? "Affairs Not The Only Secret" : "The Affair Was Just The Beginning",
            SiteName = index % 2 == 0 ? "站点甲" : "站点乙",
            Description = "现在不是出轨的问题 / 2026 第1季全集 / 1080p SDR / 中文字幕 / 完整发布描述与长文本换行验证。",
            SeasonEpisode = $"S01 E{index + 1:00}",
            Seasons = [index < 8 ? 1 : 2],
            Episodes = [index + 1],
            Size = 2147483648L + index * 10240000,
            Seeders = 368 - index * 13,
            Peers = index,
            Resolution = index % 2 == 0 ? "1080p" : "2160p",
            VideoCodec = index % 2 == 0 ? "H264" : "H265",
            Edition = "WEB-DL",
            ReleaseGroup = "MWeb",
            WebSource = "friDay",
            ResourceType = "WEB-DL",
            ResourceEffect = "HDR10",
            DownloadFactor = index % 2 == 0 ? 0.5 : 0,
            Labels = ["中字"],
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            PageUrl = "https://site.invalid/details",
            TorrentInfo = JsonSerializer.SerializeToElement(new { title = "fixture" })
        }, _ => Task.CompletedTask, _ => { })).ToArray();
        browser.SetResults(rows, autoEpisode: 5);
        Require(browser.Rows.Count == 1 && browser.AdvancedSummary.Contains("E05", StringComparison.Ordinal), "自动集号没有显示在综合筛选摘要");
        browser.SeasonFacet.Choices.Single(choice => choice.Value == "2").Selected = true;
        Require(browser.SelectedEpisode == MoviePilotResourceBrowserViewModel.AllEpisodes && browser.Rows.Count == 4,
            "切季仍残留自动集号");
        browser.ResetCommand.Execute(null);
        rows[0].State = MoviePilotDownloadState.Uncertain;
        browser.SiteFacet.Choices[0].Selected = true;
        browser.SiteFacet.Choices[1].Selected = true;
        Require(browser.Rows.Count == 12 && browser.ActiveChoices.Count == 2, "同维度多选不是并集");
        browser.VideoFacet.Choices.Single(choice => choice.Value == "H264").Selected = true;
        Require(browser.Rows.Count == 6, "跨维度多选不是交集");
        browser.Text = "不存在的关键词";
        Require(browser.Rows.Count == 0 && browser.NoMatchesVisibility == Visibility.Visible, "零匹配没有空态");
        browser.ResetCommand.Execute(null);
        Require(ReferenceEquals(browser.Rows[0], rows[0]) && rows[0].State == MoviePilotDownloadState.Uncertain, "清除筛选丢失下载状态");
        try
        {
            foreach (var width in new[] { 1280, 760, 360 })
            {
                ResizeInspect(width, 860);
                _root.Children.Add(panel);
                await LayoutAsync(panel);
                QuietTooltips(panel);
                var cards = Descendants(panel).OfType<Grid>().Where(grid => grid.Name == "MediaPoster").ToArray();
                Require(cards.Length > 0 && cards.All(card => Math.Abs(card.ActualHeight / card.ActualWidth - 1.5) < 0.02), "海报未保持 2:3");
                var action = Descendants(panel).OfType<Button>().First(button => button.Name == "SearchResources" && button.Visibility == Visibility.Visible);
                Require(action.Focus(FocusState.Keyboard) && AutomationProperties.GetName(action).Contains("搜索资源", StringComparison.Ordinal), "海报操作不能键盘聚焦或缺少名称");
                RequireScrollBarLane(Get<ItemsView>(panel, "Results"), "MediaPoster", $"订阅结果 {width}", requireBar: false);
                await SaveReturnFrameAsync(panel, $"mp-search-posters-{width}-render.png");
                await SaveProbeScreenAsync($"mp-search-posters-{width}-screen.png");
                _root.Children.Remove(panel);
                _root.Children.Add(resourceView);
                await LayoutAsync(resourceView);
                QuietTooltips(resourceView);
                Get<DropDownButton>(resourceView, "SortOrder").Focus(FocusState.Keyboard);
                await Task.Delay(250);
                var resourceCards = Descendants(resourceView).OfType<Grid>().Where(grid => grid.Name == "ResourceCard").ToArray();
                var paintedCards = resourceCards.Where(card => card.ActualWidth > 0 && card.ActualHeight > 0).ToArray();
                Require(paintedCards.Length > 0 && paintedCards.All(card => card.ActualWidth >= 235),
                    $"资源卡宽度不足：window={width}, cards={string.Join(',', resourceCards.Select(card => card.ActualWidth.ToString("0.##")))}");
                RequireScrollBarLane(Get<ItemsView>(resourceView, "ResourceList"), "ResourceCard", $"资源结果 {width}", requireBar: true);
                var toolbar = Get<WrapRow>(resourceView, "FilterToolbar");
                foreach (var child in toolbar.Children.OfType<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible))
                {
                    var origin = child.TransformToVisual(toolbar).TransformPoint(default);
                    Require(origin.X >= -1 && origin.X + child.ActualWidth <= toolbar.ActualWidth + 1, "筛选工具栏超出窗口");
                }
                await SaveReturnFrameAsync(resourceView, $"mp-search-resources-{width}-render.png");
                await SaveProbeScreenAsync($"mp-search-resources-{width}-screen.png");
                await MoviePilotResourceSortMenuAsync(resourceView, browser, rows, width);
                Get<Flyout>(resourceView, "AdvancedFilters").ShowAt(Get<DropDownButton>(resourceView, "FilterExpander"));
                await LayoutAsync(resourceView);
                await SaveProbeScreenAsync($"mp-search-filters-{width}-screen.png");
                resourceView.ClosePopups();
                await Task.Delay(250);
                _root.Children.Remove(resourceView);
            }
            ResizeInspect(1280, 900);
            _root.Children.Add(resourceView);
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme.Id);
                await LayoutAsync(resourceView);
                QuietTooltips(resourceView);
                await SaveProbeScreenAsync($"mp-search-resources-{theme.Id}-screen.png");
            }
            browser.SetResults([new MoviePilotResourceRow(new MoviePilotResource { Title = "无元数据资源", TorrentInfo = JsonSerializer.SerializeToElement(new { }) }, _ => Task.CompletedTask)]);
            await LayoutAsync(resourceView);
            Require(Get<MoviePilotResourceFacetPicker>(resourceView, "VideoFilter").Visibility == Visibility.Collapsed &&
                Get<MoviePilotResourceFacetPicker>(resourceView, "SeasonFilter").Visibility == Visibility.Collapsed,
                "无元数据筛选仍占据工具栏");
        }
        finally
        {
            panel.Release();
            resourceView.ClosePopups();
            _root.Children.Remove(panel);
            _root.Children.Remove(resourceView);
            ThemeHost.Apply(_options.Theme ?? "midnight");
            ResizeInspect(1420, 980);
        }

        static void RequireScrollBarLane(ItemsView list, string cardName, string label, bool requireBar)
        {
            // 滚动条是 Framework 模板里的覆盖层：它贴右还不够，卡片也不能伸到它底下
            //（2026-10-28 用户令「资源搜索页面的滚动条要靠右，别跟卡片重叠」）。这里量的就是
            // 两者在同一个坐标系里的右缘／左缘，宽度档每次重排后都要成立。
            var scroll = list.ScrollView;
            Require(scroll is not null, $"{label}：取不到 ItemsView 的 ScrollView");
            var bar = Descendants(scroll!).OfType<ScrollBar>()
                .FirstOrDefault(candidate => candidate.Orientation == Orientation.Vertical && candidate.ActualWidth > 0);
            // 海报间距固定、条数少时这一档可能根本不滚（没有滚动条可量）；资源卡是拉伸铺满的，
            // 调用方用 requireBar 把「这一档必须有滚动条」钉死。
            if (bar is null && !requireBar) return;
            Require(bar is not null, $"{label}：找不到竖向滚动条");
            var barBox = bar!.TransformToVisual(list)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, bar.ActualWidth, bar.ActualHeight));
            Require(barBox.Right >= list.ActualWidth - 2,
                $"{label}：滚动条没有贴右（右缘 {barBox.Right:0.#}，容器 {list.ActualWidth:0.#}）");
            var cards = Descendants(scroll!).OfType<FrameworkElement>()
                .Where(element => element.Name == cardName && element.ActualWidth > 0).ToArray();
            Require(cards.Length > 0, $"{label}：没有可量的卡片");
            var cardRight = cards.Max(card => card.TransformToVisual(list)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, card.ActualWidth, card.ActualHeight)).Right);
            Require(cardRight <= barBox.Left + 0.5,
                $"{label}：卡片伸进滚动条（卡片右缘 {cardRight:0.#}，滚动条左缘 {barBox.Left:0.#}）");
        }

        static void QuietTooltips(DependencyObject view)
        {
            // 物理鼠标停在旧卡片位置时，重排会自动打开提示并挡住实屏采样点。
            // 只在匿名截图夹具中去掉提示，不移动用户指针，也不改产品提示或采样保护。
            foreach (var element in Descendants(view).OfType<FrameworkElement>())
                ToolTipService.SetToolTip(element, null);
        }
    }

    /// <summary>BitmapImage 使用平台 HTTP；只在回环上提供一张匿名 PNG，不经过真实服务或外网。</summary>
    private sealed class SearchPosterServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        internal string Url { get; }
        internal SearchPosterServer(byte[] png)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/poster.png";
            _worker = ServeAsync(png);
        }
        private async Task ServeAsync(byte[] png)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    var valid = request == "GET /poster.png HTTP/1.1";
                    var header = valid ? $"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {png.Length}\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header), _stop.Token);
                    if (valid) await stream.WriteAsync(png, _stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        }
    }
}

using CommunityToolkit.WinUI.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SearchHeaderLayoutAsync()
    {
        using var fixture = CreateFixture(new SearchProbeServer());
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var poster = await FixtureImageAsync();
        var sample = Series();
        sample.Name = "一念永恒";
        sample.ImageTags["Primary"] = "fixture";
        fixture.Transport.Reply = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(poster) }
            : Query(request, "SearchTerm") is not null ? Json(new { Items = new[] { sample }, TotalRecordCount = 1 }) : FakeTransport.Standard(request));
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var page = (LibraryPage)frame.Content;
            await page.ViewModel.SearchAsync("离线测试");
            foreach (var width in new[] { 1600, 1040, 760, 520, 360 })
            {
                var scale = _root.XamlRoot.RasterizationScale;
                ResizeInspect((int)(width * scale), (int)(800 * scale));
                await LayoutAsync(page);
                var header = Get<Grid>(page, "HeaderLayout");
                var search = Get<Border>(page, "SearchSurface");
                var box = Bounds(search, header);
                Require(Math.Abs(box.Left + box.Width / 2 - header.ActualWidth / 2) <= 1, "搜索未居中");
                Require(box.Left >= -1 && box.Right <= header.ActualWidth + 1, "搜索超出内容区");
                Require(Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Collapsed, "搜索页仍显示浏览工具栏");
                var categories = Get<SelectorBar>(page, "SearchCategories");
                var categoryBox = Bounds(categories, page);
                Require(categoryBox.Left >= -1 && categoryBox.Right <= page.ActualWidth + 1, "搜索分类被裁切");
                var tabs = Get<Segmented>(page, "SourceTabs");
                Require(tabs.ActualWidth <= search.ActualWidth, "来源切换超出搜索区");
                await SaveReturnFrameAsync(page, $"search-layout-{width}.png");
                if (width is 1040 or 360)
                    await SaveProbeScreenAsync($"search-screen-{width}.png");
            }
            page.ViewModel.RememberSearch(sample);
            await page.ViewModel.SearchAsync("");
            await LayoutAsync(page);
            Require(Get<Grid>(page, "RecentSearchSection").Visibility == Visibility.Visible, "清空后未显示最近搜索");
            await SaveReturnFrameAsync(page, "search-recent.png");
            await SaveProbeScreenAsync("search-recent-screen-narrow.png");
            ResizeInspect(1040, 880);
            await LayoutAsync(page);
            await SaveProbeScreenAsync("search-recent-screen-wide.png");
            page.Release();
            fixture.Settings.MoviePilot.Enabled = false;
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            Require(Get<Segmented>(page, "SourceTabs").Visibility == Visibility.Collapsed, "未启用 MoviePilot 仍显示来源");
            page.Release();
            frame.Navigate(typeof(LibraryPage), new LibraryRequest { Services = fixture.Services, Title = "离线媒体库", ParentId = "library" });
            page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            Require(Get<Border>(page, "SearchSurface").Visibility == Visibility.Collapsed
                && Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Visible, "普通媒体库工具栏被移除");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }

        static Rect Bounds(FrameworkElement element, UIElement relative)
        {
            var origin = element.TransformToVisual(relative).TransformPoint(default);
            return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
        }
    }

    private async Task SearchInteractionAsync()
    {
        var mp = new SearchProbeServer();
        using var fixture = CreateFixture(mp);
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        await ((MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!).RefreshAvailabilityAsync();
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            var old = page.SearchTypingAsync("旧词");
            var newest = page.SearchTypingAsync("新词");
            await Task.WhenAll(old, newest);
            Require(fixture.Transport.Requests.All(request => !request.Contains(Uri.EscapeDataString("旧词"), StringComparison.Ordinal)), "防抖仍发送旧词");
            Require(page.LoadedCount == 2 && page.ViewModel.SearchText == "新词",
                $"键入后没有结果：rows={page.LoadedCount}, text={page.ViewModel.SearchText}, source={Get<Segmented>(page, "SourceTabs").SelectedIndex}, busy={page.ViewModel.Busy}, notice={page.ViewModel.NoticeMessage}, requests={string.Join(';', fixture.Transport.Requests)}");
            Get<SelectorBar>(page, "SearchCategories").SelectedItem = Get<SelectorBarItem>(page, "SeriesCategory");
            await UntilAsync(() => !page.ViewModel.Busy);
            Require(fixture.Transport.Requests.Last().Contains("IncludeItemTypes=Series", StringComparison.Ordinal), "节目分类没有查询剧集");
            await page.SubmitSearchAsync("新词");
            page.ViewModel.Open(page.ViewModel.Cards[0]);
            await page.SubmitSearchAsync("");
            Require(page.ViewModel.RecentSearches.Count == 1 && page.ViewModel.RecentSearches[0].Entry.ItemId is not null,
                "选中结果未替换关键词历史");
            page.ViewModel.RecentSearches[0].RemoveCommand.Execute(null);
            Require(page.ViewModel.RecentSearches.Count == 0, "最近搜索不能逐条移除");
            var input = Get<AutoSuggestBox>(page, "SearchInput");
            input.Text = "订阅关键词";
            var tabs = Get<Segmented>(page, "SourceTabs");
            tabs.SelectedIndex = 1;
            await UntilAsync(() => Get<MoviePilotSearchPanel>(page, "MoviePilotPanel").ViewModel.Results.Count == 1);
            Require(mp.Paths.Any(path => path.Contains("media/search?title=", StringComparison.Ordinal)), "订阅未走媒体搜索");
            Require(Get<MoviePilotSearchPanel>(page, "MoviePilotPanel").ViewModel.SourceChoices.Count > 1, "MoviePilot 来源目录被取消");
            var subscriptionPanel = Get<MoviePilotSearchPanel>(page, "MoviePilotPanel");
            await LayoutAsync(subscriptionPanel);
            var sourceFilter = Get<ComboBox>(subscriptionPanel, "SourceFilter");
            sourceFilter.SelectedItem = subscriptionPanel.ViewModel.SourceChoices.Single(source => source.Id == "douban");
            try { await UntilAsync(() => subscriptionPanel.ViewModel.SelectedSource?.Id == "douban"); }
            catch (TimeoutException)
            {
                throw new InvalidOperationException($"来源选择没有落入视图模型：items={sourceFilter.Items.Count}, index={sourceFilter.SelectedIndex}, selected={sourceFilter.SelectedItem}, vm={subscriptionPanel.ViewModel.SelectedSource?.Id}");
            }
            tabs.SelectedIndex = 2;
            await UntilAsync(() => mp.Paths.Any(path => path.Contains("search/title?keyword=", StringComparison.Ordinal)));
            await UntilAsync(() => !Get<MoviePilotVersionsView>(page, "MoviePilotResources").ViewModel.Busy);
            Require(Get<MoviePilotSearchPanel>(page, "MoviePilotPanel").Visibility == Visibility.Collapsed, "资源与订阅面板重叠");
            mp.PendingSources = Pending<HttpResponseMessage>();
            tabs.SelectedIndex = 1;
            await UntilAsync(() => !subscriptionPanel.ViewModel.Busy);
            mp.PendingSources.TrySetResult(SearchProbeServer.Sources());
            await SettleAsync();
            Require(subscriptionPanel.ViewModel.SelectedSource.Id == "douban"
                && mp.Paths.Last(path => path.Contains("media/search?", StringComparison.Ordinal)).Contains("media_source=douban", StringComparison.Ordinal),
                $"迟到的来源目录重置了用户选择：source={subscriptionPanel.ViewModel.SelectedSource?.Id}, last={mp.Paths.Last(path => path.Contains("media/search?", StringComparison.Ordinal))}");
            mp.PendingSources = null;
            tabs.SelectedIndex = 2;
            await UntilAsync(() => !Get<MoviePilotVersionsView>(page, "MoviePilotResources").ViewModel.Busy);
            await page.SearchTypingAsync("新的资源词");
            var count = mp.Paths.Count;
            await page.SubmitSearchAsync("新的资源词");
            Require(mp.Paths.Count == count + 1 && mp.Paths.Last().Contains(Uri.EscapeDataString("新的资源词"), StringComparison.Ordinal), "资源没有显式提交新关键词");
            tabs.SelectedIndex = 0;
            await UntilAsync(() => !page.ViewModel.Busy);
            Require(page.LoadedCount == 2 && Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Collapsed, "切回媒体库未恢复查询");
            var pending = page.SearchTypingAsync("离页词");
            page.Release();
            await pending;
            Require(!fixture.Transport.Requests.Any(path => path.Contains(Uri.EscapeDataString("离页词"), StringComparison.Ordinal)), "离页后仍发出防抖搜索");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
        }
    }

    private sealed class SearchProbeServer : HttpMessageHandler
    {
        internal bool Offline { get; set; }
        internal List<string> Paths { get; } = [];
        internal TaskCompletionSource<HttpResponseMessage>? PendingSources { get; set; }
        internal TaskCompletionSource<HttpResponseMessage>? PendingResource { get; set; }
        internal static HttpResponseMessage Sources() => Json(new[]
        {
            new { name = "TMDB", media_source = "themoviedb", media_types = new[] { "电影", "电视剧" } },
            new { name = "豆瓣", media_source = "douban", media_types = new[] { "电影", "电视剧" } }
        });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "搜索探针拒绝真实网络");
            var path = request.RequestUri.PathAndQuery;
            Paths.Add(path);
            if (Offline) throw new HttpRequestException("offline fixture");
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Task.FromResult(Json(new { access_token = "fixture-token" }));
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Task.FromResult(Json(new { version = "fixture" }));
            Require(request.Method == HttpMethod.Get, "搜索不应提交写请求");
            if (path.Contains("/site/", StringComparison.Ordinal)) return Task.FromResult(Json(new[]
            {
                new { id = 1, name = "站点甲", is_active = true },
                new { id = 2, name = "站点乙", is_active = true },
                new { id = 3, name = "站点丙 · 用于检查较长站点名称的显示", is_active = true },
                new { id = 4, name = "已停用站点", is_active = false }
            }));
            if (path.Contains("media/source", StringComparison.Ordinal)) return PendingSources?.Task ?? Task.FromResult(Sources());
            if (path.Contains("media/search?", StringComparison.Ordinal)) return Task.FromResult(Json(new[]
            { new { title = "测试电影", type = "电影", media_source = "themoviedb", media_id = "123", tmdb_id = 123 } }));
            if (path.Contains("search/title?", StringComparison.Ordinal) || path.Contains("search/media/", StringComparison.Ordinal))
                return PendingResource?.Task ?? Task.FromResult(Json(Array.Empty<object>()));
            throw new InvalidOperationException("非搜索请求");
        }
    }
}

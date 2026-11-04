using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using Momoka.Theming;
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
        var movie = new EmbyItem
        {
            Id = "movie-offline",
            Name = "离线电影",
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "fixture" }
        };
        var episode = new EmbyItem
        {
            Id = "episode-offline-1",
            Name = "离线单集 1",
            Type = EmbyItemType.Episode,
            SeriesId = "show",
            SeriesName = "离线测试剧集",
            SeasonId = "season-1",
            ParentIndexNumber = 1,
            IndexNumber = 1,
            ImageTags = new() { ["Thumb"] = "fixture" }
        };
        var nextEpisode = new EmbyItem
        {
            Id = "episode-offline-2",
            Name = "离线单集 2",
            Type = EmbyItemType.Episode,
            SeriesId = "show",
            SeriesName = "离线测试剧集",
            SeasonId = "season-1",
            ParentIndexNumber = 1,
            IndexNumber = 2,
            ImageTags = new() { ["Thumb"] = "fixture" }
        };
        var results = new[] { sample, movie, episode, nextEpisode };
        fixture.Transport.Reply = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(poster) }
            : Query(request, "SearchTerm") is not null
                ? Json(new { Items = results, TotalRecordCount = results.Length })
                : FakeTransport.Standard(request));
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var page = (LibraryPage)frame.Content;
            foreach (var width in new[] { 1040, 360 })
            {
                ResizeProbeClient(width, 800);
                await LayoutAsync(page);
                RequireSearchTextCentered(page);
                await SaveProbeScreenAsync($"search-placeholder-centered-{width}-screen.png");
            }
            await page.ViewModel.SearchAsync("离线测试");
            await LayoutAsync(page);
            Require(page.ViewModel.SearchGroups.Select(group => group.Title).SequenceEqual(["节目", "影片", "单集"]),
                $"搜索结果分组标题不对：{string.Join('、', page.ViewModel.SearchGroups.Select(group => group.Title))}");
            Require(page.ViewModel.SearchGroups.Select(group => group.Cards.Count).SequenceEqual([1, 1, 2]),
                "搜索结果分组条数不对");
            foreach (var width in new[] { 1600, 1040, 760, 520, 360 })
            {
                var scale = _root.XamlRoot.RasterizationScale;
                ResizeInspect((int)(width * scale), (int)(800 * scale));
                await LayoutAsync(page);
                var header = Get<Grid>(page, "HeaderLayout");
                var search = Get<Border>(page, "SearchSurface");
                var box = Bounds(search, header);
                RequireSearchHeader(page);
                var pageBox = Bounds(search, page);
                await File.AppendAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "search-title-upshift.txt"),
                    $"width={width}; title={Get<TextBlock>(page, "LibraryTitle").Visibility}; row={Grid.GetRow(search)}; searchTop={pageBox.Top:F1}; searchBottom={pageBox.Bottom:F1}\n");
                if (width == 1040)
                {
                    var title = Get<TextBlock>(page, "LibraryTitle");
                    title.Visibility = Visibility.Visible;
                    var rejected = false;
                    try { RequireSearchHeader(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    finally { title.Visibility = Visibility.Collapsed; }
                    Require(rejected, "搜索标题重新显示的反例未被检测");
                    await LayoutAsync(page);
                    RequireSearchHeader(page);
                    await RequireSearchGroupsAsync(page);
                }
                Require(Math.Abs(box.Left + box.Width / 2 - header.ActualWidth / 2) <= 1, "搜索未居中");
                Require(box.Left >= -1 && box.Right <= header.ActualWidth + 1, "搜索超出内容区");
                Require(Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Collapsed, "搜索页仍显示浏览工具栏");
                var categories = Get<SelectorBar>(page, "SearchCategories");
                var categoryBox = Bounds(categories, page);
                Require(categoryBox.Left >= -1 && categoryBox.Right <= page.ActualWidth + 1, "搜索分类被裁切");
                var tabs = Get<Segmented>(page, "SourceTabs");
                Require(tabs.ActualWidth <= search.ActualWidth, "来源切换超出搜索区");
                Require(tabs.CornerRadius == new CornerRadius(16) && tabs.BorderThickness == new Thickness(0)
                    && Colour(tabs.Background) == Role("EgSurfaceHoverBrush"), "来源栏仍是长方形边框");
                Require(tabs.ActualWidth <= 480 && tabs.ActualHeight == 32,
                    $"{width} 来源栏没有缩小到 480 DIP 内、32 DIP 高");
                var selected = (SegmentedItem)tabs.Items[tabs.SelectedIndex];
                RequirePill(selected, selected: true);
                foreach (var item in tabs.Items.OfType<SegmentedItem>())
                {
                    var itemBox = Bounds(item, tabs);
                    Require(item.CornerRadius == new CornerRadius(12) && item.ActualHeight == 24
                        && itemBox.Left >= 0 && itemBox.Right <= tabs.ActualWidth,
                        $"{width} 来源胶囊不是圆角或超出切换栏");
                    var label = (TextBlock)item.Content;
                    Require(item.FontSize == (double)Application.Current.Resources["EgCaptionFontSize"],
                        $"{width} 来源胶囊没有使用紧凑字号");
                    Require(label.TextWrapping == TextWrapping.NoWrap && !label.IsTextTrimmed,
                        $"{width} 来源胶囊标签换行或被截断：{label.Text}");
                }
                // 原生辅助功能选择必须触发真实 SelectionChanged，不只直接修改索引。
                if (width == 1040)
                {
                    foreach (var index in new[] { 1, 2, 3, 0 })
                    {
                        var item = (SegmentedItem)tabs.Items[index];
                        var parentPeer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(tabs);
                        var name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(item);
                        var children = parentPeer?.GetChildren();
                        var peer = children?.SingleOrDefault(child => child.GetName() == name)
                            ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(item);
                        var selection = peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem)
                            as Microsoft.UI.Xaml.Automation.Provider.ISelectionItemProvider;
                        await File.AppendAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "search-pill-uia.txt"),
                            $"index={index}; parent={parentPeer?.GetType().Name}; children={children?.Count}; itemPeer={peer?.GetType().Name}; name={peer?.GetName()}; selection={selection is not null}\n");
                        Require(selection is not null && !string.IsNullOrWhiteSpace(peer!.GetName()), "来源胶囊缺少辅助功能选择入口");
                        selection!.Select();
                        await LayoutAsync(page);
                        Require(tabs.SelectedIndex == index && item.IsSelected, "来源胶囊辅助功能未切换实际来源");
                        RequirePill(item, selected: true);
                    }
                    var probeItem = (SegmentedItem)tabs.Items[0];
                    var radius = probeItem.CornerRadius;
                    probeItem.CornerRadius = new CornerRadius(0);
                    var rejected = false;
                    try { RequirePill(probeItem, selected: true); }
                    catch (InvalidOperationException) { rejected = true; }
                    finally { probeItem.CornerRadius = radius; }
                    Require(rejected, "来源胶囊被改成方形的反例未被检测");
                    await LayoutAsync(page);
                }

                // 每个尺寸只聚焦和核对一次；宽帧另外覆盖全部存活主题。
                var focusInput = Get<AutoSuggestBox>(page, "SearchInput");
                var focusField = Descendants(focusInput).OfType<TextBox>().First();
                RequireSearchTextCentered(page);
                if (width == 1040)
                {
                    focusInput.VerticalContentAlignment = VerticalAlignment.Top;
                    await LayoutAsync(page);
                    var rejected = false;
                    try { RequireSearchTextCentered(page); }
                    catch (InvalidOperationException) { rejected = true; }
                    finally { focusInput.VerticalContentAlignment = VerticalAlignment.Center; }
                    Require(rejected, "搜索文字退回顶部的反例未被检测");
                    await LayoutAsync(page);
                    RequireSearchTextCentered(page);
                }
                Require(focusField.Focus(FocusState.Programmatic), "搜索框不能获得焦点");
                await LayoutAsync(page);
                var focusEdge = Descendants(focusField).OfType<Border>().Single(border => border.Name == "BorderElement");
                Require(Colour(focusEdge.BorderBrush) == Role("EgBorderBrush") && focusEdge.BorderThickness == new Thickness(1),
                    $"{width} 搜索框聚焦仍有主题色外圈或加粗底边");
                if (width == 1040)
                {
                    var themeBefore = ThemeHost.Current;
                    try
                    {
                        foreach (var theme in UiThemes.All)
                        {
                            ThemeHost.Apply(theme);
                            await LayoutAsync(page);
                            Require(Colour(focusEdge.BorderBrush) == Role("EgBorderBrush") && focusEdge.BorderThickness == new Thickness(1),
                                $"主题 {theme.Id} 搜索框出现强调色边框");
                            RequirePill((SegmentedItem)tabs.Items[tabs.SelectedIndex], selected: true);
                            await SaveProbeScreenAsync($"search-focus-neutral-{theme.Id}-screen.png");
                        }
                    }
                    finally { ThemeHost.Apply(themeBefore); }
                }
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
            await PersistentSearchHeaderAsync(page);
            Get<Segmented>(page, "SourceTabs").SelectedIndex = 1;
            await page.SearchTypingAsync("");
            var subscriptionPanel = page.MoviePilotPanel
                ?? throw new InvalidOperationException("订阅来源没有创建搜索面板");
            foreach (var width in new[] { 1040, 360 })
            {
                var scale = _root.XamlRoot.RasterizationScale;
                ResizeInspect((int)(width * scale), (int)(800 * scale));
                await LayoutAsync(page);
                Require(subscriptionPanel.Visibility == Visibility.Visible, "订阅搜索面板未显示");
                Require(!Descendants(subscriptionPanel).OfType<ComboBox>().Any(picker => picker.Name == "SourceFilter"),
                    $"{width} 订阅搜索仍保留来源筛选");
                var heading = Get<Grid>(subscriptionPanel, "SearchContent").Children.OfType<StackPanel>()
                    .Single(panel => Grid.GetRow(panel) == 2);
                Require(Math.Abs(heading.ActualWidth - subscriptionPanel.ActualWidth) <= 1,
                    $"{width} 搜索标题仍给已删除的来源筛选留空列");
                await SaveReturnFrameAsync(page, $"search-subscription-no-source-{width}.png");
                await SaveProbeScreenAsync($"search-subscription-no-source-{width}-screen.png");
            }
            page.Release();
            fixture.Settings.MoviePilot.Enabled = false;
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            Require(Get<Segmented>(page, "SourceTabs").Visibility == Visibility.Visible, "未启用 MoviePilot 时固定来源栏消失");
            page.Release();
            frame.Navigate(typeof(LibraryPage), new LibraryRequest { Services = fixture.Services, Title = "离线媒体库", ParentId = "library" });
            page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            Require(Get<Border>(page, "SearchSurface").Visibility == Visibility.Collapsed
                && Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Visible, "普通媒体库工具栏被移除");
            Require(Get<TextBlock>(page, "LibraryTitle").Visibility == Visibility.Visible
                && Get<TextBlock>(page, "LibraryTitle").Text == page.ViewModel.Heading,
                "普通媒体库标题没有恢复");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }

        async Task RequireSearchGroupsAsync(LibraryPage page)
        {
            var list = Get<ItemsRepeater>(page, "SearchGroupsList");
            var scroll = Get<ScrollViewer>(page, "SearchGroupsScroll");
            Require(scroll.Visibility == Visibility.Visible
                && Get<ScrollViewer>(page, "TableScroll").Visibility == Visibility.Collapsed,
                "搜索分组没有取代平铺卡片墙");
            await LayoutAsync(page);

            var topTexts = Descendants(list).OfType<TextBlock>()
                .Where(text => text.Visibility == Visibility.Visible).Select(text => text.Text).ToArray();
            Require(topTexts.Contains("节目") && topTexts.Contains("影片"),
                $"分组标题没有画出来：{string.Join('、', topTexts)}");
            await SaveProbeScreenAsync("search-groups-1040-screen.png");

            var movie = Descendants(list).OfType<PosterCard>()
                .FirstOrDefault(card => card.Card?.Item.Type == EmbyItemType.Movie);
            Require(movie is not null, "影片分组没有实现卡片");
            var movieProbe = movie!.ProbeHover();
            Require(movieProbe.FrameBorderless, "非单集卡也画了框线");
            Require(movieProbe.ArtWidth / movieProbe.ArtHeight < 0.8, "影片封面不是竖版海报");

            scroll.UpdateLayout();
            scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
            await UntilAsync(() => scroll.VerticalOffset > 0 || scroll.ScrollableHeight <= 0);
            await LayoutAsync(page);
            Require(scroll.VerticalOffset > 0, "单集分组没有滚入视口");
            var scrollTop = Bounds(scroll, page).Top;
            var firstHeader = Descendants(list).OfType<TextBlock>()
                .First(text => text.Visibility == Visibility.Visible && text.Text == "节目");
            Require(Bounds(firstHeader, page).Top < scrollTop, "滚动后分组内容没有随滚动器上移");

            var episodes = Descendants(list).OfType<PosterCard>()
                .Where(card => card.Card?.Item.Type == EmbyItemType.Episode).ToArray();
            Require(episodes.Length > 0, "单集分组没有实现卡片");
            var episodeProbe = episodes[0].ProbeHover();
            Require(!episodeProbe.FrameBorderless && episodeProbe.FrameRest == Role("EgAccentBrush"),
                "单集卡没有使用强调色粗框");
            Require(Math.Abs(episodeProbe.ArtWidth / episodeProbe.ArtHeight - 16.0 / 9.0) <= 0.02,
                $"单集封面不是 16:9：{episodeProbe.ArtWidth:F0}×{episodeProbe.ArtHeight:F0}");
            // 静止的探针窗口里，滚动位置变了而 DWM 还停在上一帧；先渲染一帧返帧把合成逼上去，
            // 紧接着的真实屏幕帧才是这一屏该有的样子。
            await SaveReturnFrameAsync(page, "search-groups-episodes-1040-render.png");
            await SaveProbeScreenAsync("search-groups-episodes-1040-screen.png");

            scroll.ChangeView(null, 0, null, disableAnimation: true);
            await UntilAsync(() => scroll.VerticalOffset == 0);
            await LayoutAsync(page);
            await SaveReturnFrameAsync(page, "search-groups-top-restored-1040-render.png");
        }

        void RequireSearchHeader(LibraryPage page)
        {
            var search = Get<Border>(page, "SearchSurface");
            var box = Bounds(search, Get<Grid>(page, "HeaderLayout"));
            Require(Get<TextBlock>(page, "LibraryTitle").Visibility == Visibility.Collapsed,
                "搜索页左上标题仍可见");
            Require(search.Padding == new Thickness(0) && search.BorderThickness == new Thickness(0)
                && search.Background is null && search.BorderBrush is null, "搜索框仍套在长方形外卡中");
            Require(search.Margin.Top is 4 or 8 && Grid.GetRow(search) == 0
                && Math.Abs(box.Top - search.Margin.Top) <= 1,
                "搜索标题收起后仍占一行空白");
            Require(Get<Grid>(page, "PageRoot").Margin.Top == 40 && Bounds(search, page).Top >= 32,
                "搜索组件没有整体上移或侵入窗口按钮区域");
        }

        void RequireSearchTextCentered(LibraryPage page)
        {
            var input = Get<AutoSuggestBox>(page, "SearchInput");
            RequireSearchRadius(input);
            var field = Descendants(input).OfType<TextBox>().Single();
            var content = Descendants(field).OfType<ScrollViewer>().Single(node => node.Name == "ContentElement");
            var placeholder = Descendants(field).OfType<ContentControl>()
                .Single(node => node.Name == "PlaceholderTextContentPresenter");
            Require(input.VerticalContentAlignment == VerticalAlignment.Center
                && field.VerticalContentAlignment == VerticalAlignment.Center
                && content.VerticalAlignment == VerticalAlignment.Center
                && placeholder.VerticalContentAlignment == VerticalAlignment.Center,
                "搜索输入与占位文字没有采用原生垂直居中");
            Require(field.Padding.Top == 0 && field.Padding.Bottom == 0,
                "搜索输入仍有上下内边距偏移");
            var contentBox = Bounds(content, field);
            Require(Math.Abs(contentBox.Top + contentBox.Height / 2 - field.ActualHeight / 2) <= 1,
                "搜索输入文字内容区域没有垂直居中");
        }

        void RequirePill(SegmentedItem item, bool selected)
        {
            var shape = Descendants(item).OfType<Grid>().Single(node => node.Name == "PART_Root");
            Require(shape.CornerRadius == new CornerRadius(12), "来源项仍是长方形");
            if (selected)
                Require(Colour(shape.Background) == Role("EgAccentSoftBrush"), "来源胶囊选中底色未跟随主题");
        }

        static Rect Bounds(FrameworkElement element, UIElement relative)
        {
            var origin = element.TransformToVisual(relative).TransformPoint(default);
            return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
        }

        // 角色表里那一支现在是什么色。ThemeHost 换主题改的是同一支画刷的 Color，所以每次现问。
        Windows.UI.Color Role(string key) =>
            Application.Current.Resources[key] is Microsoft.UI.Xaml.Media.SolidColorBrush brush
                ? brush.Color
                : default;

        static Windows.UI.Color Colour(Microsoft.UI.Xaml.Media.Brush? brush) =>
            brush is Microsoft.UI.Xaml.Media.SolidColorBrush solid ? solid.Color : default;
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

            // 换来源只换面板，不替用户搜一次（2026-10-15 用户令「切换时不要自动触发搜索」）：切过去之后
            // 搜索请求数必须原封不动，搜不搜由回车或搜索图标决定。
            var quiet = Searches(mp.Paths);
            tabs.SelectedIndex = 1;
            await SettleAsync();
            Require(Searches(mp.Paths) == quiet, $"切到订阅来源时自动发出了搜索：{string.Join(';', mp.Paths)}");

            // 键入也一样归回车：随输入更新只属于「媒体库」这一个来源（2026-10-15 用户令）。订阅来源上键入只收起
            // 旧结果，一个搜索请求都不该发；占位文案得把这件事说出来，否则用户敲完字只会以为坏了。
            await page.SearchTypingAsync("订阅关键词");
            await SettleAsync();
            Require(Searches(mp.Paths) == quiet, $"订阅来源键入时自动发出了搜索：{string.Join(';', mp.Paths)}");
            Require(input.PlaceholderText.Contains("按回车", StringComparison.Ordinal),
                $"订阅来源的占位文案没有提示回车：{input.PlaceholderText}");
            // 占位文案只有框空着时才看得见：拍这一帧之前先把框清掉（程序改 Text 不走键入那条路），拍完放回去。
            input.Text = string.Empty;
            await SaveReturnFrameAsync(page, "search-subscription-idle.png");
            input.Text = "订阅关键词";

            await page.SubmitSearchAsync("订阅关键词");
            var subscriptionPanel = page.MoviePilotPanel
                ?? throw new InvalidOperationException("订阅来源没有创建搜索面板");
            await UntilAsync(() => subscriptionPanel.ViewModel.Results.Count == 1);
            Require(mp.Paths.Any(path => path.Contains("media/search?title=", StringComparison.Ordinal)), "订阅未走媒体搜索");
            Require(tabs.Visibility == Visibility.Visible, "提交搜索后固定来源选项消失");
            await LayoutAsync(subscriptionPanel);
            Require(!Descendants(subscriptionPanel).OfType<ComboBox>().Any(picker => picker.Name == "SourceFilter"),
                "MoviePilot 订阅搜索仍显示右侧来源筛选");
            Require(NewMediaSearches(mp.Paths, 0).All(path => !path.Contains("media_source=", StringComparison.Ordinal)),
                "MoviePilot 订阅搜索未使用服务器默认来源范围");
            await SettleAsync();
            quiet = Searches(mp.Paths);
            tabs.SelectedIndex = 2;
            await SettleAsync();
            Require(Searches(mp.Paths) == quiet, $"切到资源来源时自动发出了搜索：{string.Join(';', mp.Paths)}");
            await page.SubmitSearchAsync("订阅关键词");
            await UntilAsync(() => mp.Paths.Any(path => path.Contains("search/title?keyword=", StringComparison.Ordinal)));
            var resourcePanel = page.MoviePilotResources
                ?? throw new InvalidOperationException("资源来源没有创建搜索面板");
            await UntilAsync(() => !resourcePanel.ViewModel.Busy);
            Require(subscriptionPanel.Visibility == Visibility.Collapsed, "资源与订阅面板重叠");

            // 移除来源筛选后，切回订阅和重复提交仍由服务器决定搜索范围，不携带隐蔽的来源限制。
            var beforeReturnSearch = mp.Paths.Count;
            tabs.SelectedIndex = 1;
            await page.SubmitSearchAsync("订阅关键词");
            var returnedSearches = NewMediaSearches(mp.Paths, beforeReturnSearch);
            Require(returnedSearches.Count == 1 && !returnedSearches[0].Contains("media_source=", StringComparison.Ordinal),
                "切回订阅后仍携带已移除的来源筛选");
            var beforeRepeatSearch = mp.Paths.Count;
            await page.SubmitSearchAsync("订阅关键词");
            var repeatedSearches = NewMediaSearches(mp.Paths, beforeRepeatSearch);
            Require(repeatedSearches.Count == 1 && !repeatedSearches[0].Contains("media_source=", StringComparison.Ordinal),
                "重复提交订阅搜索改变了默认来源范围");
            tabs.SelectedIndex = 2;
            await UntilAsync(() => !resourcePanel.ViewModel.Busy);
            await page.SearchTypingAsync("新的资源词");
            var count = mp.Paths.Count;
            await page.SubmitSearchAsync("新的资源词");
            var resourceSearches = mp.Paths.Skip(count).Where(path => path.Contains("search/title?", StringComparison.Ordinal)).ToArray();
            Require(resourceSearches.Length == 1 && resourceSearches[0].Contains(Uri.EscapeDataString("新的资源词"), StringComparison.Ordinal),
                "资源没有显式提交新关键词；SSE能力探测不算重复搜索");
            Require(tabs.Visibility == Visibility.Visible && tabs.SelectedIndex == 2, "完成搜索后固定入口或 MP 资源选择丢失");
            // 用户重新编辑期间到达的旧结果不能再次收起来源栏。
            mp.PendingResource = Pending<HttpResponseMessage>();
            var delayedSearch = page.SubmitSearchAsync("等待资源");
            mp.PendingResource.TrySetResult(Json(Array.Empty<object>()));
            await delayedSearch;
            Require(tabs.Visibility == Visibility.Visible, "迟到结果收起了用户重新展开的选项");
            mp.PendingResource = null;

            // 输入法组字时的回车只留下 keyup（keydown 被输入法吞掉），页面得替这颗回车补一次提交
            // （2026-10-24 用户报「搜索页面 在搜索栏按回车要支持触发搜索」）。两段：组合上屏后按上屏的词提交，
            // 以及一直没上屏时按框里现有的词提交。
            // 输入法回车来自当前持有焦点的输入框。前面的来源栏布局与异步选择通知先落定，
            // 不把它们迟到引发的 LostFocus 当成这一趟组字回车（离焦按产品规则会取消补提交）。
            var imeField = Descendants(input).OfType<TextBox>().First();
            input.Text = "组合词";
            await LayoutAsync(page);
            Require(imeField.Focus(FocusState.Programmatic), "组字回车夹具无法聚焦实际输入框");
            await UntilAsync(() => ReferenceEquals(
                Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), imeField));
            await LayoutAsync(page);
            Require(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), imeField),
                "组字回车开始前焦点已离开实际输入框");
            var imeFocus = new List<string>();
            input.LostFocus += (_, _) => imeFocus.Add(
                $"lost:{(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot) as FrameworkElement)?.Name}");
            var beforeIme = Searches(mp.Paths);
            var committed = page.SubmitImeEnterAsync();
            await Task.Delay(120);
            input.Text = "组合上屏";
            await committed;
            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "search-ime-focus.txt"),
                $"focused={(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot) as FrameworkElement)?.Name}; "
                + $"events={string.Join(',', imeFocus)}; searches={Searches(mp.Paths) - beforeIme}; text={input.Text}\n");
            Require(Searches(mp.Paths) == beforeIme + 1
                && mp.Paths.Last().Contains(Uri.EscapeDataString("组合上屏"), StringComparison.Ordinal),
                $"组字回车没有按上屏后的词提交：{mp.Paths.LastOrDefault()}");
            var beforeFallback = Searches(mp.Paths);
            input.Text = "没有上屏";
            await page.SubmitImeEnterAsync();
            Require(Searches(mp.Paths) == beforeFallback + 1
                && mp.Paths.Last().Contains(Uri.EscapeDataString("没有上屏"), StringComparison.Ordinal),
                $"组字回车在文本没变时没有按框里的词提交：{mp.Paths.LastOrDefault()}");
            quiet = Searches(mp.Paths);
            tabs.SelectedIndex = 0;
            await SettleAsync();
            Require(Searches(mp.Paths) == quiet, $"切回媒体库时自动发出了搜索：{string.Join(';', mp.Paths)}");
            Require(Get<Grid>(page, "EmbyContent").Visibility == Visibility.Visible
                && Get<Grid>(page, "ToolbarSurface").Visibility == Visibility.Collapsed, "切回媒体库没有换回媒体库面板");
            await page.SubmitSearchAsync("新词");
            Require(page.LoadedCount == 2 && page.ViewModel.SearchText == "新词", "切回媒体库后显式搜索没有结果");
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

    private async Task PersistentSearchHeaderAsync(LibraryPage page)
    {
        var tabs = Get<Segmented>(page, "SourceTabs");
        var input = Get<AutoSuggestBox>(page, "SearchInput");
        var search = Get<Border>(page, "SearchSurface");
        var sites = Get<MoviePilotSitePicker>(page, "SearchSitePicker");
        var submit = Descendants(input).OfType<Button>().Single(button => button.Name == "QueryButton");
        foreach (var width in new[] { 1040, 360 })
        {
            var scale = _root.XamlRoot.RasterizationScale;
            ResizeInspect((int)(width * scale), (int)(880 * scale));
            tabs.SelectedIndex = 2;
            await LayoutAsync(page);
            var resources = page.MoviePilotResources
                ?? throw new InvalidOperationException("MP 资源来源没有创建面板");
            Require(tabs.Visibility == Visibility.Visible, $"{width} 宽帧的来源栏未展开");
            var expanded = search.ActualHeight;
            await page.SubmitSearchAsync("离线资源布局");
            Require(tabs.Visibility == Visibility.Visible, "资源搜索结束后固定来源栏消失");
            Require(sites.Visibility == Visibility.Visible, "MP 资源搜索完成后站点入口消失");
            Require(Get<MoviePilotSitePicker>(resources, "SearchSitePicker").Visibility == Visibility.Collapsed,
                "资源结果上方仍有重复站点入口");
            resources.ViewModel.Browser.SetResults(Enumerable.Range(1, 6).Select(index => new ViewModels.MoviePilotResourceRow(
                new MoviePilotResource
                {
                    Title = $"离线资源 S01E{index:00} 1080p WEB-DL",
                    SiteName = "站点甲",
                    Seeders = 12,
                    Size = 2147483648,
                    Resolution = "1080p",
                    Seasons = [1],
                    Episodes = [index],
                    TorrentInfo = System.Text.Json.JsonSerializer.SerializeToElement(new { title = "fixture" })
                }, _ => Task.CompletedTask, _ => { })));
            resources.ViewModel.ShowEmptyNotice = false;
            await LayoutAsync(page);
            Require(tabs.Visibility == Visibility.Visible && Math.Abs(search.ActualHeight - expanded) <= 1,
                $"{width} 搜索结束改变了固定来源栏高度：before={expanded}, after={search.ActualHeight}");
            Require(submit.ActualWidth == 64 && submit.IsTabStop, "搜索按钮未加宽或键盘无法定位");
            var inputBox = Box(Descendants(input).OfType<ScrollViewer>().Single(element => element.Name == "ContentElement"));
            var sitesBox = Box(sites);
            var submitBox = Box(submit);
            Require(inputBox.Width >= 70 && inputBox.Right <= sitesBox.Left && sitesBox.Right <= submitBox.Left,
                "搜索框、站点、搜索按钮的横向次序或窄窗可用宽度错误");
            Require(submitBox.Right <= Box(input).Right && sitesBox.Left >= Box(input).Left,
                "站点选择没有与搜索框同行或已裁切");
            var resourceBox = Box(Get<MoviePilotResourceBrowser>(resources, "Browser"));
            Require(resourceBox.Top - Box(search).Bottom <= 16, "资源结果与搜索栏之间仍有过大空白");
            await SaveProbeScreenAsync($"search-persistent-resources-{width}-screen.png");
            Require(Get<ItemsView>(Get<MoviePilotResourceBrowser>(resources, "Browser"), "ResourceList").Focus(FocusState.Programmatic),
                "资源列表无法接收恢复前的焦点");
            var field = Descendants(input).OfType<TextBox>().First();
            Require(field.Focus(FocusState.Keyboard), "搜索栏无法通过键盘重新聚焦");
            await LayoutAsync(page);
            Require(tabs.Visibility == Visibility.Visible && search.ActualHeight >= expanded - 1,
                "重新聚焦搜索栏改变了固定来源选项");
            await SaveProbeScreenAsync($"search-focused-resources-{width}-screen.png");
            foreach (var source in new[] { 0, 1, 3, 2 })
            {
                tabs.SelectedIndex = source;
                await LayoutAsync(page);
                Require(sites.Visibility == (source == 2 ? Visibility.Visible : Visibility.Collapsed),
                    "站点入口没有只随 MP 资源显示和恢复");
            }
        }
        tabs.SelectedIndex = 0;

        Rect Box(FrameworkElement element)
        {
            var origin = element.TransformToVisual(page).TransformPoint(default);
            return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
        }
    }

    /// <summary>
    /// 只数搜索请求。来源目录 <c>media/source</c> 与站点目录不是搜索：换来源照旧会拉它们，把它们算进来
    /// 就量不出「换来源不替用户搜一次」。
    /// </summary>
    private static int Searches(IReadOnlyList<string> paths) => paths.Count(path =>
        !path.Contains("/stream", StringComparison.Ordinal) && (path.Contains("media/search", StringComparison.Ordinal)
        || path.Contains("search/title", StringComparison.Ordinal)
        || path.Contains("search/media/", StringComparison.Ordinal)));

    /// <summary>
    /// 从 <paramref name="from"/> 之后新增的媒体搜索请求。一次提交在 <c>media/search</c> 后面还要发一条
    /// <c>subscribe/</c> 读订阅状态（结果拿去标注已订阅），所以一次提交的最后一条路径不是媒体搜索；
    /// 量「这次提交用没用用户选的来源」只能看这一条。
    /// </summary>
    private static List<string> NewMediaSearches(IReadOnlyList<string> paths, int from) =>
        paths.Skip(from).Where(path => path.Contains("media/search?", StringComparison.Ordinal)).ToList();

    private sealed class SearchProbeServer : HttpMessageHandler
    {
        internal bool Offline { get; set; }
        internal object[]? MediaResults { get; set; }
        internal List<string> Paths { get; } = [];
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
            if (request.RequestUri.AbsolutePath.EndsWith("/stream", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
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
            if (path.Contains("media/source", StringComparison.Ordinal)) return Task.FromResult(Sources());
            if (path.Contains("media/search?", StringComparison.Ordinal) && MediaResults is { } results) return Task.FromResult(Json(results));
            if (path.Contains("media/search?", StringComparison.Ordinal)) return Task.FromResult(Json(new[]
            { new { title = "测试电影", type = "电影", media_source = "themoviedb", media_id = "123", tmdb_id = 123 } }));
            if (path.Contains("search/title?", StringComparison.Ordinal) || path.Contains("search/media/", StringComparison.Ordinal))
                return PendingResource?.Task ?? Task.FromResult(Json(Array.Empty<object>()));
            throw new InvalidOperationException("非搜索请求");
        }
    }
}

using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Interop;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Updates;
using Windows.Storage.Streams;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SidebarAsync()
    {
        var originalAnimations = HomeMotion.PreferenceEnabled;
        var originalTheme = ThemeHost.Current;
        using var fixture = CreateFixture();
        fixture.Settings.Ui.AnimationsEnabled = false;
        fixture.Settings.Ui.ShowHomeBanner = true;
        var picture = await FixtureImageAsync();
        var favorite = false;
        var movies = Enumerable.Range(0, 20).Select(index => new EmbyItem
        {
            Id = "sidebar-movie-" + index,
            Name = "离线影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" },
            BackdropImageTags = ["backdrop"]
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            if (path.EndsWith("/Views", StringComparison.Ordinal))
            {
                var prefix = request.RequestUri.Host == "b.invalid" ? "B" : "A";
                return Task.FromResult(Json(new
                {
                    Items = new[]
                    {
                        new EmbyItem { Id = prefix + "-movies", Name = prefix + " 的电影", Type = EmbyItemType.CollectionFolder, CollectionType = "movies" },
                        new EmbyItem { Id = prefix + "-shows", Name = prefix + " 的电视剧", Type = EmbyItemType.CollectionFolder, CollectionType = "tvshows" },
                        new EmbyItem { Id = "music", Name = "音乐库", Type = EmbyItemType.CollectionFolder, CollectionType = "music" }
                    }
                }));
            }
            if (Query(request, "Filters")?.Contains("IsFavorite", StringComparison.Ordinal) == true)
                return Task.FromResult(Json(new { Items = favorite ? movies.Take(1).ToArray() : [], TotalRecordCount = favorite ? 1 : 0 }));
            if (Query(request, "IncludeItemTypes") == EmbyItemType.Audio)
                return Task.FromResult(Json(new ItemsResult { Items = [new() { Id = "song", Name = "侧栏音乐曲目", Type = EmbyItemType.Audio }], TotalRecordCount = 1 }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(movies.Take(6).ToArray()));
            if (path.EndsWith("/Items", StringComparison.Ordinal) || path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = movies, TotalRecordCount = movies.Length }));
            return Task.FromResult(FakeTransport.Standard(request));
        };

        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        fixture.Actions.TitleStripChanged = shell.SetTitleStrip;
        // Only lend the already-running probe window for title-bar geometry; no player is attached.
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var page = new SettingsPage();
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "sidebar-settings"));
        paths.EnsureCreated();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()), fixture.Capabilities, fixture.Images);
        await page.ViewModel.ReloadAsync();
        page.SelectedCategory = "界面";
        page.Visibility = Visibility.Collapsed;
        _root.Children.Add(page);

        var nav = Get<NavigationView>(shell, "Sidebar");
        var host = Get<Grid>(shell, "SidebarHost");
        var frame = shell.Pages;
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(1240, 900);
            await LayoutAsync(shell);
            var settingsIcon = Get<AnimatedIcon>(shell, "SidebarSettingsIcon");
            Require(settingsIcon.Source is null, "关动画后侧栏设置图标仍挂着动态源");
            Require(fixture.Settings.Ui.EnableImmersive && host.Visibility == Visibility.Collapsed, "默认侧栏没有关闭");
            Require(!page.ViewModel.Sections.SelectMany(section => section.Rows).Any(row => row.Label == "侧边栏模式"),
                "界面设置仍保留独立侧栏开关");
            await ToggleAsync(true);
            Require(host.Visibility == Visibility.Collapsed && Get<Button>(shell, "SettingsButton").Visibility == Visibility.Visible,
                "未登录时侧栏出现或设置入口丢失");
            await ToggleAsync(false);
            await fixture.SelectAsync();
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            await LayoutAsync(shell);
            await Task.Delay(250);
            await SaveProbeScreenAsync("sidebar-off-1240-screen.png");
            var home = (HomePage)frame.Content;
            var shelves = home.ViewModel.Shelves.ToArray();
            int DataRequests() => fixture.Transport.Requests.Count(request => !request.Contains("/Images/", StringComparison.Ordinal));
            var requests = DataRequests();
            Require(home.BannerCardRead().Ok && shell.TitleStripNow == TitleStrip.OnScrim,
                "沉浸主页没有恢复通栏轮播与标题浅墨：" + home.BannerCardRead().Detail);
            Require(ShellSelfCheck.ReadInk(shell, _window!).Ok, "沉浸轮播系统按钮墨色不对");
            AssertCarouselScrollBar();
            shell.SetTitleStrip(TitleStrip.Plain);
            Require(!ShellSelfCheck.ReadInk(shell, _window!).Ok, "标题墨色检查没有拒绝错误模式档");
            shell.SetTitleStrip(TitleStrip.OnScrim);
            await SaveProbeScreenAsync("carousel-immersive-1240-screen.png");
            await ToggleAsync(true);
            await LayoutAsync(shell);
            Require(host.Visibility == Visibility.Visible && nav.IsPaneOpen && Math.Abs(host.ActualWidth - 256) < 1,
                $"宽窗侧栏未展开：shown={host.Visibility} pane={nav.IsPaneOpen} host={host.ActualWidth:0.0}/{host.Width:0.0} shell={shell.ActualWidth:0.0} immersive={fixture.Settings.Ui.EnableImmersive} live={fixture.Session.IsSignedIn} loaded={shell.IsLoaded}");
            Require(ReferenceEquals(home, frame.Content) && shelves.SequenceEqual(home.ViewModel.Shelves)
                && DataRequests() == requests,
                $"切换侧栏重建主页货架或重新请求了数据：page={ReferenceEquals(home, frame.Content)} "
                + $"shelves={shelves.Length}/{home.ViewModel.Shelves.Count} same={shelves.SequenceEqual(home.ViewModel.Shelves)} "
                + $"requests={requests}/{DataRequests()} width={shell.ActualWidth:0} pane={nav.IsPaneOpen}");
            Require(shell.LibraryViews.Count == 3 && nav.MenuItems.OfType<NavigationViewItem>().Count(item =>
                item.Tag is string tag && tag.StartsWith("library:", StringComparison.Ordinal)) == 3, "媒体库数量或音乐入口错误");
            Require(home.BannerCardRead().Ok, "侧栏模式轮播不是四边留白的圆角大图：" + home.BannerCardRead().Detail);
            Require(shell.TitleStripNow == TitleStrip.Plain && ShellSelfCheck.ReadInk(shell, _window!).Ok,
                "商店轮播标题墨色没有恢复主题");
            AssertCarouselScrollBar();
            var homeScroller = Get<ScrollView>(home, "Scroller");
            homeScroller.ScrollTo(0, 100, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            await LayoutAsync(shell);
            AssertCarouselScrollBar();
            homeScroller.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            await LayoutAsync(shell);
            await ToggleAsync(false);
            Require(home.BannerCardRead().Ok && shell.TitleStripNow == TitleStrip.OnScrim
                && ReferenceEquals(home, frame.Content) && shelves.SequenceEqual(home.ViewModel.Shelves)
                && DataRequests() == requests, "切回沉浸没有恢复轮播或重查主页");
            Get<HomeBanner>(home, "Banner").Apply([]);
            await LayoutAsync(shell);
            Require(Get<ScrollView>(home, "Scroller").Margin.Top == 0 && shell.TitleStripNow == TitleStrip.Plain,
                "空轮播没有撤回标题栏扩展与浅墨");
            AssertCarouselScrollBar();
            Get<HomeBanner>(home, "Banner").Apply(home.ViewModel.Slides);
            await LayoutAsync(shell);
            Require(home.BannerCardRead().Ok && shell.TitleStripNow == TitleStrip.OnScrim,
                "轮播恢复后没有再次贴顶");
            AssertCarouselScrollBar();
            await ToggleAsync(true);
            Require(home.BannerCardRead().Ok, "再次切回商店轮播没有恢复圆角留白");
            AssertCarouselScrollBar();
            AssertSidebarGeometry(shell, host, true);
            AssertSidebarIconSelection(shell);
            DumpSidebarLayout(shell, "1240 expanded");
            await SaveProbeScreenAsync("sidebar-on-1240-screen.png");

            // 2026-11-14 用户令「拖动侧边栏边缘调整宽度」「只有展开时才支持调整」：拖动条只在展开档露面；
            // 直接走产品那条重排方法，核对宽度生效、内容区让位、拖动条跟着走、夹取上下限，最后还原 256。
            // 指针手势本身（按下、移动、松手）由 SidebarResizeContractTests 与下面的展开/收起显隐守卫覆盖，
            // 这里不伪造指针坐标 —— 那只能证明被伪造的那一段。
            var resizeGrip = Get<SidebarResizeGrip>(shell, "SidebarResizeGrip");
            Require(resizeGrip.Visibility == Visibility.Visible, "展开档没有露出侧栏拖动条");
            Call(shell, "ResizeSidebar", 320.0);
            await LayoutAsync(shell);
            Require(Math.Abs(nav.OpenPaneLength - 320) < 1 && Math.Abs(host.ActualWidth - 320) < 1
                && Math.Abs(Get<Grid>(shell, "ContentHost").Margin.Left - 320) < 1,
                $"拖动后侧栏宽度没有生效：pane={nav.OpenPaneLength:0.#} host={host.ActualWidth:0.#} "
                    + $"content={Get<Grid>(shell, "ContentHost").Margin.Left:0.#}");
            Require(Math.Abs(Left(resizeGrip, shell) - 320) < 1,
                $"拖动条没有贴在栏的右沿：left={Left(resizeGrip, shell):0.#}");
            Call(shell, "ResizeSidebar", 40.0);
            await LayoutAsync(shell);
            Require(Math.Abs(nav.OpenPaneLength - Momoka.Configuration.UiSettings.MinSidebarWidth) < 1,
                $"拖动宽度没有夹到下限：{nav.OpenPaneLength:0.#}");
            Call(shell, "ResizeSidebar", 4000.0);
            await LayoutAsync(shell);
            Require(Math.Abs(nav.OpenPaneLength - Momoka.Configuration.UiSettings.MaxSidebarWidth) < 1,
                $"拖动宽度没有夹到上限：{nav.OpenPaneLength:0.#}");
            Call(shell, "ResizeSidebar", 256.0);
            await LayoutAsync(shell);
            Require(resizeGrip.Visibility == Visibility.Visible && Math.Abs(host.ActualWidth - 256) < 1,
                "侧栏宽度没有还原");

            void AssertCarouselScrollBar()
            {
                var banner = Get<HomeBanner>(home, "Banner");
                var scrollBar = Get<Microsoft.UI.Xaml.Controls.Primitives.ScrollBar>(home, "_verticalScrollBar");
                var expected = banner.Visibility == Visibility.Visible
                    ? (fixture.Settings.Ui.EnableImmersive ? 0 : 20) + banner.ActualHeight : 0;
                Require(scrollBar is not null && Math.Abs(scrollBar.Margin.Top - expected) < 1,
                    $"轮播滚动条没有固定在下沿：实际 {scrollBar?.Margin.Top:0.0}、预期 {expected:0.0}");
            }

            // 2026-11-14 用户令：点标题框，框用关联动画移进内容区；搜索页开着时标题框收起，离开再出现。
            Require(nav.MenuItems.Concat(nav.FooterMenuItems).OfType<NavigationViewItem>()
                .All(item => item.Tag as string != "search"), "侧边栏里仍有搜索入口");
            var titleSearch = Get<AutoSuggestBox>(shell, "TitleSearchBox");
            Require(titleSearch.Visibility == Visibility.Visible, "宽窗侧边栏模式没有标题搜索框");
            foreach (var width in new[] { 699, 700, 1240 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(shell);
                Require(titleSearch.Visibility == (width >= 700 ? Visibility.Visible : Visibility.Collapsed),
                    $"商店搜索框没有随尺寸切换（{width}）");
                Require(Get<Button>(shell, "SearchButton").Visibility == Visibility.Collapsed,
                    "商店模式出现第二份搜索入口");
                if (width >= 700) Require(Math.Abs(titleSearch.ActualWidth - Math.Clamp(shell.ActualWidth * 0.30, 220, 420)) < 1,
                    "商店搜索框宽度没有随窗口恢复");
            }
            Require(shell.BrowseRoot.Margin.Top == HomePage.ChromeHeight && Get<Grid>(shell, "ContentHost").Padding.Top == 0,
                "商店模式没有让画布避开标题栏");
            HomeMotion.ApplyPreference(true);
            Require(settingsIcon.Source is not null, "开动画后侧栏设置图标没有恢复动态源");
            TitleSearchTransition.ResetOutcome();
            Require(titleSearch.Focus(FocusState.Programmatic), "标题搜索框不能获得焦点");
            await UntilAsync(() => shell.CurrentTag == LibraryRequest.SearchTag
                && frame.Content is LibraryPage searchPage && searchPage.ViewModel.SearchVisibility == Visibility.Visible);
            var titleSearchPage = (LibraryPage)frame.Content;
            await LayoutAsync(shell);
            await UntilAsync(() => TitleSearchTransition.LastOutcome is "started" or "skipped");
            Require(TitleSearchTransition.LastOutcome == "started",
                $"标题框移到内容区的关联动画没有启动：{TitleSearchTransition.LastOutcome}");
            Require(titleSearch.Visibility == Visibility.Collapsed, "搜索页仍留着标题搜索框");
            Require(Get<Grid>(titleSearchPage, "SearchEntry").Visibility == Visibility.Visible,
                "搜索页没有接住移下来的输入框");
            Require(titleSearchPage.SearchInputFocused, "框落下后光标不在内容区的输入框");
            // 来源栏仍须居中在搜索区（搜索页这一档两列 Auto 会把它钉在左沿）。
            var searchTabs = Get<FrameworkElement>(titleSearchPage, "SourceTabs");
            var searchSurface = Get<Border>(titleSearchPage, "SearchSurface");
            Require(Math.Abs(Left(searchTabs, titleSearchPage) + searchTabs.ActualWidth / 2
                    - (Left(searchSurface, titleSearchPage) + searchSurface.ActualWidth / 2)) <= 1,
                "搜索页来源栏没有居中在搜索区");
            var searchInput = Get<AutoSuggestBox>(titleSearchPage, "SearchInput");
            var searchField = Descendants(searchInput).OfType<TextBox>().Single();
            searchField.Text = "离线标题";
            var searchQuery = Descendants(searchInput).OfType<Button>().Single(button => button.Name == "QueryButton");
            ((IInvokeProvider)new ButtonAutomationPeer(searchQuery).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(650);
            Require(titleSearchPage.ViewModel.SearchText == searchInput.Text && searchInput.Text == "离线标题",
                "内容区输入或立即回车被延迟文本事件覆盖");
            Require(titleSearchPage.ViewModel.LoadedCount > 0, "标题查询没有留下搜索结果");
            await SaveProbeScreenAsync("sidebar-title-search-screen.png");
            foreach (var searchWidth in new[] { 620, 1240, 620, 1240 })
            {
                Require(searchInput.Focus(FocusState.Programmatic), "缩放前输入无法聚焦");
                ResizeProbeClient(searchWidth, 900);
                await LayoutAsync(shell);
                Require(Get<Grid>(titleSearchPage, "SearchEntry").Visibility == Visibility.Visible,
                    "宽窄切换收起了内容区的输入框");
                Require(titleSearch.Visibility == Visibility.Collapsed, "宽窄切换把标题框放回了标题栏");
                Require(searchInput.Text == "离线标题", "宽窄切换丢失关键词");
                Require(titleSearchPage.SearchInputFocused, "宽窄切换没有把焦点交给可见输入");
            }
            foreach (var immersive in new[] { true, false })
            {
                Require(searchInput.Focus(FocusState.Programmatic), "模式切换前输入不能聚焦");
                fixture.Settings.Ui.EnableImmersive = immersive;
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(shell);
                Require(ReferenceEquals(frame.Content, titleSearchPage) && searchInput.Text == "离线标题",
                    "显示模式切换重建搜索页或丢失关键词");
                Require(Get<Grid>(titleSearchPage, "SearchEntry").Visibility == Visibility.Visible,
                    "模式切换收起了内容区的输入框");
                Require(titleSearch.Visibility == Visibility.Collapsed, "搜索页把标题框放回了标题栏");
                Require(titleSearchPage.SearchInputFocused, "模式切换没有恢复输入焦点");
            }
            Call(shell, "GoBack");
            await UntilAsync(() => frame.Content is HomePage && shell.ActiveContentReady);
            await LayoutAsync(shell);
            Require(titleSearch.Visibility == Visibility.Visible, "离开搜索页后标题框没有回到标题栏");
            HomeMotion.ApplyPreference(false);
            Require(settingsIcon.Source is null, "再次关动画后侧栏设置图标没有收起动态源");

            var musicTag = HomeLayout.LibraryKey(shell.LibraryViews.Single(view => view.Id == "music"));
            Select(nav.MenuItems.OfType<NavigationViewItem>().Single(item => item.Tag as string == musicTag));
            await UntilAsync(() => frame.Content is MusicLibraryPage music && music.ViewModel.IsReady);
            AssertSidebarIconSelection(shell);
            Require(((MusicLibraryPage)frame.Content).ViewModel.Rows.Single().Item.Type == EmbyItemType.Audio,
                "侧栏音乐库未进入歌曲页面");
            await SaveProbeScreenAsync("sidebar-music-1240-screen.png");

            Call(shell, "GoBack");
            await UntilAsync(() => frame.Content is HomePage && shell.ActiveContentReady);
            Call(shell, "GoForward");
            await CheckMusicDestinationAsync("前进");

            var moviesTag = HomeLayout.LibraryKey(shell.LibraryViews.Single(view => view.Id == "A-movies"));
            Select(nav.MenuItems.OfType<NavigationViewItem>().Single(item => item.Tag as string == moviesTag));
            await UntilAsync(() => shell.ActiveContentReady);
            Require(shell.CurrentTag == moviesTag, "侧栏选择没有导航到媒体库");
            Call(shell, "GoBack");
            await CheckMusicDestinationAsync("返回");
            Call(shell, "GoForward");
            await UntilAsync(() => frame.Content is LibraryPage && shell.CurrentTag == moviesTag && shell.ActiveContentReady);
            shell.OpenDetail(DetailRequest.For(fixture.Services, Series()));
            await UntilAsync(() => shell.ActiveContentReady);
            Require((nav.SelectedItem as NavigationViewItem)?.Tag as string == moviesTag, "详情页丢失所属媒体库高亮");
            AssertSidebarIconSelection(shell);

            async Task CheckMusicDestinationAsync(string direction)
            {
                await UntilAsync(() => frame.Content is MusicLibraryPage music && music.IsLoaded
                    && music.ViewModel.IsReady && !music.ViewModel.Busy && music.ViewModel.Rows.Count == 1);
                var music = (MusicLibraryPage)frame.Content;
                Require(shell.CurrentTag == musicTag && music.Tag as string == musicTag
                    && (nav.SelectedItem as NavigationViewItem)?.Tag as string == musicTag,
                    direction + "音乐根页后目的地没有恢复");
                AssertSidebarIconSelection(shell);
                var backEntries = frame.BackStack.ToArray();
                var forwardEntries = frame.ForwardStack.ToArray();
                var rows = music.ViewModel.Rows.ToArray();
                var beforeRequests = DataRequests();
                shell.GoTo(musicTag);
                Require(ReferenceEquals(frame.Content, music) && backEntries.SequenceEqual(frame.BackStack)
                    && forwardEntries.SequenceEqual(frame.ForwardStack) && rows.SequenceEqual(music.ViewModel.Rows)
                    && beforeRequests == DataRequests(), direction + "音乐根页后重复选择重建了页面或历史");
            }
            Call(shell, "GoBack");
            await UntilAsync(() => shell.ActiveContentReady);
            var libraryPage = (LibraryPage)frame.Content;
            var history = frame.BackStack.Count;
            var forward = frame.ForwardStack.Count;
            ResizeProbeClient(760, 650);
            await LayoutAsync(shell);
            await LayoutAsync(libraryPage);
            // 2026-11-16 用户令「拉动窗口的时候侧边栏不要自动展开或者收起」：从 1240 拉窄到 760，
            // 栏仍是用户选的那一档（展开），开合只归标题栏那颗折叠键。
            Require(nav.IsPaneOpen && Math.Abs(host.ActualWidth - nav.OpenPaneLength) < 1,
                $"拉窄窗口把展开的侧栏自动收起了：pane={nav.IsPaneOpen} host={host.ActualWidth:0.#}/{nav.OpenPaneLength:0.#}");
            var wall = Get<ItemsView>(libraryPage, "CardWall");
            wall.ApplyTemplate();
            await UntilAsync(() =>
            {
                wall.UpdateLayout();
                return wall.IsLoaded && wall.Visibility == Visibility.Visible && wall.ScrollView is not null;
            });
            var scroll = wall.ScrollView!;
            Require(scroll.ScrollableHeight > 0,
                $"侧栏滚动夹具没有可滚内容：cards={libraryPage.ViewModel.Cards.Count}；view={libraryPage.ViewModel.View}；extent={scroll.ExtentHeight:0.0}；viewport={scroll.ViewportHeight:0.0}");
            await ScrollProbeAsync(scroll, Math.Min(120, scroll.ScrollableHeight));
            Require(scroll.VerticalOffset > 0, $"侧栏没有滚动：offset={scroll.VerticalOffset:0.0}/{scroll.ScrollableHeight:0.0}");
            var offset = scroll.VerticalOffset;
            await ToggleAsync(false);
            await ToggleAsync(true);
            await LayoutAsync(shell);
            Require(ReferenceEquals(libraryPage, frame.Content) && history == frame.BackStack.Count
                && forward == frame.ForwardStack.Count && Math.Abs(offset - scroll.VerticalOffset) < 1,
                $"开关侧栏丢失页面、返回记录或滚动位置：page={ReferenceEquals(libraryPage, frame.Content)} "
                + $"history={history}/{frame.BackStack.Count} forward={forward}/{frame.ForwardStack.Count} "
                + $"offset={offset:R}/{scroll.VerticalOffset:R} extent={scroll.ExtentHeight:R} viewport={scroll.ViewportHeight:R}");
            Require(!nav.IsPaneOpen && Math.Abs(host.ActualWidth - 56) < 1, "窄窗进商店模式没有收成图标栏");
            // 2026-11-16 用户令「拉动窗口的时候侧边栏不要自动展开或者收起」：收着的时候把窗口拉宽
            // 也不许自己展开，拉回来也不许自己变档；开合只归标题栏那颗折叠键。
            ResizeProbeClient(1240, 650);
            await LayoutAsync(shell);
            Require(!nav.IsPaneOpen && Math.Abs(host.ActualWidth - 56) < 1, "拉宽窗口把收起的侧栏自动展开了");
            ResizeProbeClient(760, 650);
            await LayoutAsync(shell);
            Require(!nav.IsPaneOpen && Math.Abs(host.ActualWidth - 56) < 1, "宽窄往返改写了侧栏开合");
            var paneToggle = Get<Button>(shell, "NavToggleButton");
            Require(paneToggle.Visibility == Visibility.Visible, "侧栏在屏上时导航开关没出现");
            ((IInvokeProvider)new ButtonAutomationPeer(paneToggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            Require(nav.IsPaneOpen && Math.Abs(host.ActualWidth - nav.OpenPaneLength) < 1, "折叠键没有把侧栏展开");
            ((IInvokeProvider)new ButtonAutomationPeer(paneToggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            Require(!nav.IsPaneOpen && Math.Abs(host.ActualWidth - 56) < 1, "折叠键没有把侧栏收回去");
            Require(resizeGrip.Visibility == Visibility.Collapsed, "收起档仍露出侧栏拖动条（只有展开时才支持调整）");
            AssertSidebarGeometry(shell, host, true);
            DumpSidebarLayout(shell, "760 compact");
            await SaveProbeScreenAsync("sidebar-compact-760-screen.png");

            await InspectLibraryPreviewAsync(shell, fixture, moviesTag, 760);

            ResizeProbeClient(600, 650);
            shell.ShowAccount("用于验证窄窗的很长很长的账号名称", "名称很长的离线服务器");
            await LayoutAsync(shell);
            // 收起那一档先读一格底色：栏 56 宽，x=28 落在它的中线上；y 取七成高，两档都落在栏的空白上
            // （条目到 236 为止，栏底那颗设置从 613 起）。
            var railSpot = (X: 28.0, Y: shell.ActualHeight * 0.7);
            var railColor = await SidebarColorAsync(shell, railSpot.X, railSpot.Y);
            // 2026-10-13 起开合由标题栏左上角那颗导航开关负责（框架自带那颗收掉了），它同时把菜单项
            // 往上顶到标题栏下沿。走的仍是 UIA 的 Invoke，和用户按下去是同一条路。
            var toggle = Get<Button>(shell, "NavToggleButton");
            Require(toggle.Visibility == Visibility.Visible, "侧栏在屏上时导航开关没出现");
            ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            Require(nav.IsPaneOpen, "侧栏展开按钮没有生效");
            AssertSidebarGeometry(shell, host, true);
            // 展开前后同一格必须同色（2026-10-14 用户：「侧边栏展开后不要变色」）。只比属性证明不了屏上
            // 画了什么 —— 这里读的是元素树自己渲染出来的那一格像素。
            var expandedColor = await SidebarColorAsync(shell, railSpot.X, railSpot.Y);
            Require(railColor == expandedColor,
                $"侧栏展开后底色变了：收起时 {railColor}、展开后 {expandedColor}");
            // 2026-11-14 按用户令「侧边栏与内容区之间对比度不够高，侧栏加深一层面色」：栏这一列铺
            // EgSidebarBrush（85% 窗口色），比内容画布的 60% 更实一档；仍是半透明，Mica Alt 透得上来。
            // 只读属性证明不了屏上的分界，但能钉住「这一层还在、并且没有变成实心或全透明」。
            Require(Get<Grid>(shell, "SidebarHost").Background is SolidColorBrush { Color.A: > 0 and < 0xFF },
                "侧边栏那层加深的面色不见或变成了实心/全透明");
            DumpSidebarLayout(shell, "600 expanded");
            var title = Get<StackPanel>(shell, "TitleActions");
            var account = Get<Button>(shell, "AccountButton");
            // 账号那一颗 2026-11-13 起回到标题栏右上角（用户令「人像图标移到最小化左边、和搜索栏对齐」）：
            // 整排必须留在标题栏里（不越出那 48），账号落在右上角、只留一颗 40 见方的人像。
            Require(Top(title, shell) + title.ActualHeight <= HomePage.ChromeHeight + 1,
                "标题栏那一排被挤出了标题栏");
            Require(Left(account, shell) > shell.ActualWidth / 2
                && Top(account, shell) + account.ActualHeight <= HomePage.ChromeHeight + 1
                && Math.Abs(account.ActualWidth - 40) <= 0.5
                && Math.Abs(account.ActualHeight - 40) <= 0.5,
                $"账号没有落在标题栏右上角：({Left(account, shell):0},{Top(account, shell):0}) "
                    + $"{account.ActualWidth:0}×{account.ActualHeight:0}");
            // 2026-11-12 晚的排法（用户令「主页不显示返回按钮…放左上角，折叠侧边栏按钮移到它右边」）：
            // 这一跑停在详情页（非主页），返回在最左、折叠键紧贴它右边；标题 2026-11-13 删掉。
            var backButton = Get<Button>(shell, "BackButton");
            Require(backButton.Visibility == Visibility.Visible
                && Left(toggle, shell) >= Left(backButton, shell) + backButton.ActualWidth
                && Top(toggle, shell) + toggle.ActualHeight <= HomePage.ChromeHeight + 1,
                "媒体库页上「返回 · 折叠」的次序不对");
            await SaveProbeScreenAsync("sidebar-expanded-600-screen.png");
            ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(shell);
            AssertSidebarGeometry(shell, host, true);
            DumpSidebarLayout(shell, "600 collapsed");
            await SaveProbeScreenAsync("sidebar-collapsed-600-screen.png");

            favorite = true;
            await shell.Favorites.RefreshAsync();
            Require(Get<NavigationViewItem>(shell, "SidebarFavorites").Visibility == Visibility.Visible, "侧栏收藏入口缺失");
            await ToggleAsync(false);
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeProbeClient(width, 900);
                await LayoutAsync(shell);
                Require(shell.ProbeTitleActions().Ok, "收藏页加入后标题栏布局不合法" + shell.ProbeTitleActions().Detail);
                // 沉浸模式恢复旧标题栏：宽窄窗口都保留左上放大镜，居中搜索框与侧栏一起收起。
                Require(Get<Button>(shell, "HomeButton").Visibility == Visibility.Visible
                    && Get<Button>(shell, "FavoritesButton").Visibility == Visibility.Visible
                    && Get<Button>(shell, "SettingsButton").Visibility == Visibility.Visible
                    && Get<Button>(shell, "SearchButton").Visibility == Visibility.Visible
                    && Get<Button>(shell, "ForwardButton").Visibility == Visibility.Visible
                    && Get<AutoSuggestBox>(shell, "TitleSearchBox").Visibility == Visibility.Collapsed
                    && Get<TextBlock>(shell, "AppTitleLabel").Visibility == Visibility.Collapsed
                    && Get<Button>(shell, "BackButton").Visibility == Visibility.Visible,
                    "媒体库页的标题栏那一排缺了按键");
                Require(shell.BrowseRoot.Margin.Top == 0 && Get<Grid>(shell, "ContentHost").Padding.Top == HomePage.ChromeHeight,
                    "沉浸模式没有恢复标题栏画布与页内让位");
                var back = Get<Button>(shell, "BackButton");
                var forwardButton = Get<Button>(shell, "ForwardButton");
                var homeButton = Get<Button>(shell, "HomeButton");
                var searchButton = Get<Button>(shell, "SearchButton");
                Require(Math.Abs(Left(homeButton, shell) - 18) <= 0.5
                    && Math.Abs(Left(back, shell) - Left(searchButton, shell) - searchButton.ActualWidth - 2) <= 0.5
                    && Math.Abs(Left(forwardButton, shell) - Left(back, shell) - back.ActualWidth - 2) <= 0.5,
                    "沉浸工具组没有恢复0.2.5顺序和间距");
                Require(title.Children.IndexOf(back) < title.Children.IndexOf(forwardButton)
                    && title.Children.IndexOf(homeButton) < title.Children.IndexOf(back),
                    "沉浸返回/前进的默认键盘顺序与视觉次序不一致");
                foreach (var button in new[] { homeButton, Get<Button>(shell, "FavoritesButton"),
                    Get<Button>(shell, "SettingsButton"), searchButton, back, forwardButton })
                    Require(button.ActualWidth == 42 && button.ActualHeight == 32
                        && button.Content is Viewbox { Width: 26, Height: 26 }, "沉浸工具组未恢复0.2.5统一尺寸");
                Require((int)typeof(ShellSelfCheck).GetMethod("HitAt", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [_window, Left(forwardButton, shell) + forwardButton.ActualWidth / 2, Top(forwardButton, shell) + forwardButton.ActualHeight / 2])!
                    == Native.HitClient, "前进按钮落在不可点击的拖动区");
                await SaveProbeScreenAsync($"toolbar-navigation-{width}-screen.png");
            }
            await ToggleAsync(true);
            Require(Get<Button>(shell, "ForwardButton").Visibility == Visibility.Collapsed,
                "商店模式保留了沉浸前进按钮及空格");
            favorite = false;
            await shell.Favorites.RefreshAsync();
            Require(Get<NavigationViewItem>(shell, "SidebarFavorites").Visibility == Visibility.Collapsed,
                "空收藏没有隐藏");

            shell.ShowPlayer(true);
            await ToggleAsync(false);
            await ToggleAsync(true);
            Require(host.Visibility == Visibility.Collapsed, "播放层中切换偏好使侧栏冒出来");
            shell.ShowPlayer(false);
            Require(host.Visibility == Visibility.Visible && ReferenceEquals(libraryPage, frame.Content), "播放返回没有恢复侧栏或重建了页面");

            SettingsReset.Restore(fixture.Settings);
            await (Task)Call(page.ViewModel, "ReapplyAndReloadAsync", "离线恢复默认")!;
            Require(host.Visibility == Visibility.Visible && !fixture.Settings.Ui.EnableImmersive,
                "恢复默认没有当场进入商店模式并开启侧栏");
            var imported = SettingsMigration.NewDefaults();
            imported.Ui.EnableImmersive = true;
            imported.Ui.AnimationsEnabled = false;
            SettingsPreferences.Apply(fixture.Settings, imported);
            await (Task)Call(page.ViewModel, "ReapplyAndReloadAsync", "离线导入")!;
            Require(host.Visibility == Visibility.Collapsed, "导入沉浸模式没有当场关闭侧栏");
            await ToggleAsync(false);
            await ToggleAsync(true);

            await fixture.SelectAsync("b.invalid");
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady && shell.LibraryViews.Count == 6);
            var switched = nav.MenuItems.OfType<NavigationViewItem>()
                .Where(item => item.Tag is string tag && tag.StartsWith("library:", StringComparison.Ordinal)).ToArray();
            Require(switched.Length == 6 && !fixture.Settings.Ui.EnableImmersive
                && switched.Select(item => (string)item.Tag).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(shell.LibraryViews.Select(HomeLayout.LibraryKey)), "第二个账号上线后没有保留所有来源的独立媒体库");
            Call(shell, "ShowSignIn", null, false, null);
            Require(host.Visibility == Visibility.Collapsed && Get<Button>(shell, "SettingsButton").Visibility == Visibility.Visible,
                "注销后侧栏或设置恢复入口错误");

            async Task ToggleAsync(bool enabled)
            {
                page.Visibility = Visibility.Visible;
                await LayoutAsync(page);
                var section = page.ViewModel.Sections.Single(item => item.Category == "界面" && item.Title == "详情页外观");
                var row = section.Rows.OfType<SettingChoiceRow>()
                    .Single(item => item.Label == "显示模式");
                var picker = Descendants(page).OfType<ComboBox>().Single(item => ReferenceEquals(item.ItemsSource, row.Choices));
                picker.StartBringIntoView();
                await LayoutAsync(picker);
                var choice = enabled ? "商店模式" : "沉浸模式";
                picker.SelectedItem = row.Choices.Single(item => item.Label == choice);
                await UntilAsync(() => row.Selected?.Label == choice);
                Require(row.Selected?.Label == choice && fixture.Settings.Ui.EnableImmersive == !enabled
                    && !fixture.SettingsService.HasUnsavedChanges, "显示模式没有保存并即时生效");
                if (enabled && !fixture.Session.IsSignedIn) await SaveProbeScreenAsync("sidebar-setting-screen.png");
                page.Visibility = Visibility.Collapsed;
                await LayoutAsync(shell);
            }
        }
        finally
        {
            page.Release();
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            Field(shell, "_window", null);
            _root.Children.Remove(page);
            _root.Children.Remove(shell);
            _window!.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(originalAnimations);
            ThemeHost.Apply(originalTheme);
            ResizeInspect(1420, 980);
        }

        static void Select(NavigationViewItem item) =>
            ((ISelectionItemProvider)new NavigationViewItemAutomationPeer(item).GetPattern(PatternInterface.SelectionItem)).Select();
    }

    /// <summary>
    /// 启动时侧边栏模式已经开着（2026-11-13 用户：「侧边栏模式怎么只有标题没有侧边栏」）。
    /// <para>
    /// 与「运行中切换显示模式」不是同一条路：那一档设置文件里存着 EnableImmersive=false，<c>Loaded</c> 那一刻外壳还没量到宽度
    /// （ActualWidth 是 0），栏的宽要等第一次 SizeChanged 才落定。这一条要证明的就是 —— 等它落定之后
    /// 屏上是「栏 + 页面」，而不是只剩页面、栏被裁到看不见。
    /// </para>
    /// </summary>
    private async Task SidebarAtStartupAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Ui.EnableImmersive = SettingsMigration.NewDefaults().Ui.EnableImmersive;
        Require(!fixture.Settings.Ui.EnableImmersive, "出厂显示模式应为商店模式");
        fixture.Settings.Ui.AnimationsEnabled = false;
        fixture.Settings.Ui.ShowHomeBanner = false;
        await fixture.SelectAsync();

        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        fixture.Actions.TitleStripChanged = shell.SetTitleStrip;
        // 只借已经跑着的那扇探针窗口量标题栏几何；没有播放器。
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        try
        {
            await (Task)Call(shell, "EnterShellAsync")!;
            await LayoutAsync(shell);
            await Task.Delay(200);

            var host = Get<Grid>(shell, "SidebarHost");
            var nav = Get<NavigationView>(shell, "Sidebar");
            Require(host.Visibility == Visibility.Visible && Math.Abs(host.ActualWidth - 256) < 1,
                $"启动时侧栏没有展开：显示 {host.Visibility}、宽 {host.ActualWidth:0}、pane={nav.IsPaneOpen}、"
                    + $"外壳宽 {shell.ActualWidth:0}");
            Require(Get<Button>(shell, "NavToggleButton").Visibility == Visibility.Visible, "启动时折叠键没有出现");
            Require(Math.Abs(shell.BrowseRoot.Margin.Left - 256) < 1,
                $"内容区没有让开侧栏：左让 {shell.BrowseRoot.Margin.Left:0}");

            // 栏里的项真的在屏上：几何上「有一块 256 宽的栏」和「栏里画得出字」是两件事，后者才是用户看到的。
            var home = Get<NavigationViewItem>(shell, "SidebarHome");
            Require(home.Visibility == Visibility.Visible && home.ActualWidth > 0 && home.ActualHeight > 0,
                "启动时栏里的入口没有实现出来");
            await SaveProbeScreenAsync("sidebar-startup-screen.png");
        }
        finally
        {
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            Field(shell, "_window", null);
            _root.Children.Remove(shell);
            ResizeInspect(1420, 980);
        }
    }

    /// <summary>
    /// 栏里某一格真的画成了什么色：把外壳渲染进自己的一块位图再读那一格（<see cref="RenderTargetBitmap"/>），
    /// 不去碰桌面 —— 取样与「别人的窗口压在上面」无关，也没有取不到屏幕 DC 那一类失败。
    /// <para>
    /// 2026-10-14 用户一句「侧边栏展开后不要变色」：栏的底在「先收起、再按 ≡ 展开」这条路上会多出一层
    /// 亚克力（见 Palette.xaml 里那两颗键的注释）。读同一格比两次 —— 收起一次、展开一次 —— 才是这句要求的
    /// 直接证据；只读控件的属性说不出屏上画了什么。
    /// </para>
    /// </summary>
    private static async Task<string> SidebarColorAsync(ShellPage shell, double x, double y)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(shell);

        var buffer = await bitmap.GetPixelsAsync();
        var pixels = new byte[buffer.Length];
        DataReader.FromBuffer(buffer).ReadBytes(pixels);

        // 位图是按岛的像素尺寸给的（BGRA8、预乘），所以逻辑坐标要乘上缩放再夹进边界。
        var scale = shell.XamlRoot.RasterizationScale;
        var column = Math.Clamp((int)Math.Round(x * scale), 0, bitmap.PixelWidth - 1);
        var row = Math.Clamp((int)Math.Round(y * scale), 0, bitmap.PixelHeight - 1);
        var at = ((row * bitmap.PixelWidth) + column) * 4;
        return $"#{pixels[at + 2]:X2}{pixels[at + 1]:X2}{pixels[at]:X2}";
    }

    /// <summary>
    /// 渲染帧是 8 位整数，半透明画刷的预乘读数会差上一级，所以逐通道给 ±tolerance。
    /// 只用于侧栏那一格底色：它现在是一支半透明角色（2026-11-12 改为 Mica 之后），不是不透明实色。
    /// </summary>
    private static bool NearColor(string actual, (byte R, byte G, byte B) want, int tolerance)
    {
        if (actual is not { Length: 7 } || actual[0] != '#') return false;

        static bool Near(string text, int offset, byte want, int tolerance) =>
            int.TryParse(text.AsSpan(offset, 2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            && Math.Abs(value - want) <= tolerance;

        return Near(actual, 1, want.R, tolerance)
            && Near(actual, 3, want.G, tolerance)
            && Near(actual, 5, want.B, tolerance);
    }

    /// <summary>读数：窗宽、栏宽、面板状态与每个入口图标的可见矩形，写进日志目录供复核（不参与断言）。</summary>
    private void DumpSidebarLayout(ShellPage shell, string label)
    {
        var nav = Get<NavigationView>(shell, "Sidebar");
        var host = Get<Grid>(shell, "SidebarHost");
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(shell.XamlRoot) as FrameworkElement;
        var lines = new List<string>
        {
            "== " + label,
            $"window {shell.ActualWidth:F1}x{shell.ActualHeight:F1} rail={host.ActualWidth:F1} " +
            $"pane={nav.PaneDisplayMode} open={nav.IsPaneOpen}",
            $"  paneClipOffset={Translate(PaneClip(shell))}",
            $"  focus={(focused is null ? "无" : string.IsNullOrEmpty(focused.Name) ? focused.GetType().Name : focused.Name)}"
                + $"/{Get<Button>(shell, "AccountButton").FocusState}"
        };
        foreach (var item in nav.MenuItems.OfType<NavigationViewItem>())
        {
            var bounds = Bounds(item, host);
            lines.Add($"  '{item.Content}' vis={item.Visibility} rect={bounds.X:F1},{bounds.Y:F1},{bounds.Width:F1},{bounds.Height:F1}");
            if (item.Icon is { } icon)
            {
                var iconBounds = Bounds(icon, host);
                lines.Add($"    icon rect={iconBounds.X:F1},{iconBounds.Y:F1},{iconBounds.Width:F1},{iconBounds.Height:F1} " +
                    $"visible={VisibleArea(icon, host):F1}/{iconBounds.Width * iconBounds.Height:F1}");
            }
        }

        // 账号那一颗在标题栏右上角（2026-11-13），只留一颗人像；栏底那颗设置是 NavigationView 的页脚项，
        // 走的是和条目同一条度量。两处都读一遍字形矩形：被内容格挤窄的时候，按钮的矩形看着照旧是对的。
        var account = Get<Button>(shell, "AccountButton");
        var accountBounds = Bounds(account, host);
        lines.Add($"  [账号] vis={account.Visibility} rect={accountBounds.X:F1},{accountBounds.Y:F1},"
            + $"{accountBounds.Width:F1},{accountBounds.Height:F1}");
        var accountGlyph = Get<FontIcon>(shell, "AccountGlyph");
        if (accountGlyph.Visibility == Visibility.Visible)
        {
            var glyphBounds = Bounds(accountGlyph, host);
            lines.Add($"    glyph rect={glyphBounds.X:F1},{glyphBounds.Y:F1},{glyphBounds.Width:F1},{glyphBounds.Height:F1} " +
                $"visible={VisibleArea(accountGlyph, host):F1}/{glyphBounds.Width * glyphBounds.Height:F1}");
        }
        var settings = Get<NavigationViewItem>(shell, "SidebarSettings");
        var settingsBounds = Bounds(settings, host);
        lines.Add($"  [设置] vis={settings.Visibility} rect={settingsBounds.X:F1},{settingsBounds.Y:F1},"
            + $"{settingsBounds.Width:F1},{settingsBounds.Height:F1}");
        if (settings.Icon is { } settingsIcon)
        {
            var iconBounds = Bounds(settingsIcon, host);
            lines.Add($"    glyph rect={iconBounds.X:F1},{iconBounds.Y:F1},{iconBounds.Width:F1},{iconBounds.Height:F1} " +
                $"visible={VisibleArea(settingsIcon, host):F1}/{iconBounds.Width * iconBounds.Height:F1}");
        }

        // 账号那几格字的矩形也读一遍：排错了只是「一行小字」，尺寸上看得出来的是行距和左沿。
        foreach (var (entry, text) in new (string, TextBlock)[]
        {
            ("名字", Get<TextBlock>(shell, "AccountUser")),
            ("服务器", Get<TextBlock>(shell, "AccountServer")),
            ("·", Get<TextBlock>(shell, "AccountDot"))
        })
        {
            if (text.Visibility != Visibility.Visible) { lines.Add($"  ({entry}) 收着"); continue; }
            var bounds = Bounds(text, host);
            lines.Add($"  ({entry}) rect={bounds.X:F1},{bounds.Y:F1},{bounds.Width:F1},{bounds.Height:F1} " +
                $"visible={VisibleArea(text, host):F1}/{bounds.Width * bounds.Height:F1}");
        }

        File.AppendAllLines(Path.Combine(_options.Paths.LogDirectory, "sidebar-layout.txt"), lines);
    }

    /// <summary>元素经祖先裁剪后仍可见的面积；祖先不可见、透明为零或裁剪到空即判零。
    /// 裁剪几何自身的位移也要算进去 —— 收起态那条把整栏推出去的偏移就写在这里。</summary>
    private static double VisibleArea(FrameworkElement element, UIElement root)
    {
        var rect = Bounds(element, root);
        for (DependencyObject? ancestor = element; ancestor is not null && !ReferenceEquals(ancestor, root);
            ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not FrameworkElement container) continue;
            if (container.Visibility != Visibility.Visible || container.Opacity <= 0) return 0;
            rect.Intersect(Bounds(container, root));
            if (container.Clip is RectangleGeometry clip)
                rect.Intersect(Bounds(clip.Transform is { } transform ? transform.TransformBounds(clip.Rect) : clip.Rect,
                    container, root));
            if (rect.IsEmpty) return 0;
        }
        return rect.Width * rect.Height;
    }

    private static string Translate(Transform? transform) =>
        transform is CompositeTransform composite ? $"{composite.TranslateX:F1},{composite.TranslateY:F1}" : transform?.GetType().Name ?? "-";

    private static Transform? PaneClip(ShellPage shell)
    {
        foreach (var split in Descendants(Get<NavigationView>(shell, "Sidebar")).OfType<SplitView>())
            foreach (var part in Descendants(split).OfType<FrameworkElement>())
                if (part.Clip is RectangleGeometry { Transform: CompositeTransform transform }) return transform;
        return null;
    }

    private static Windows.Foundation.Rect Bounds(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static Windows.Foundation.Rect Bounds(Windows.Foundation.Rect rect, UIElement element, UIElement root) =>
        element.TransformToVisual(root).TransformBounds(rect);

    private void AssertSidebarGeometry(ShellPage shell, Grid host, bool shown)
    {
        var pane = Get<NavigationView>(shell, "Sidebar");
        Require(Get<PathIcon>(shell, "NavPaneExpandedIcon").Visibility
            == (pane.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed), "展开图标未随面板更新");
        Require(Get<PathIcon>(shell, "NavPaneCollapsedIcon").Visibility
            == (pane.IsPaneOpen ? Visibility.Collapsed : Visibility.Visible), "收起图标未随面板更新");
        var content = Get<Grid>(shell, "ContentHost");
        Require(Math.Abs(Left(content, shell) - (shown ? host.ActualWidth : 0)) < 1
            && Math.Abs(content.ActualWidth + Left(content, shell) - shell.ActualWidth) < 1, "侧栏与内容区重叠或出现空白");

        // 标题栏左上那两颗（返回、折叠）的字形要和栏里那几项的图标落在同一条竖线上（用户 2026-10-13：
        // 「关闭导航没和下面对齐」，2026-11-13 又钉了一次：「返回键出现时要和下面那一列对齐」）。
        // 两档的图标中线不一样（展开 32、收成图标栏 28，见 ShellPage.Sidebar.cs 的 ApplySidebarLayout），
        // 而那一排的缩进跟着档走，所以两档都该对得上 —— 容差 3 像素，钉的是「不再差一整格」。
        if (shown && Get<NavigationViewItem>(shell, "SidebarHome").Icon is { ActualWidth: > 0 } homeIcon)
        {
            var railCentre = Left(homeIcon, shell) + homeIcon.ActualWidth / 2;
            var back = Get<Button>(shell, "BackButton");
            var toggle = Get<Button>(shell, "NavToggleButton");
            // 这一排的第一颗（非主页时是返回，主页上是折叠）和栏里那几项同一条竖线；后面那颗按托盘加间距
            // 跟在它右边 —— 「返回键出现时要和下面那一列对齐」说的是第一颗，不是每一颗。
            var first = back.Visibility == Visibility.Visible ? back : toggle;
            var entry = ReferenceEquals(first, back) ? "返回" : "折叠";
            if (first.Visibility == Visibility.Visible && first.Content is FrameworkElement glyph
                && glyph.ActualWidth > 0)
            {
                var centre = Left(glyph, shell) + glyph.ActualWidth / 2;
                Require(Math.Abs(centre - railCentre) <= 3,
                    $"{entry}的字形没和栏里那几项对齐：中线 {centre:0.#} 对 {railCentre:0.#}");
            }
            if (back.Visibility == Visibility.Visible && toggle.Visibility == Visibility.Visible)
            {
                var gap = Left(toggle, shell) - (Left(back, shell) + back.ActualWidth);
                Require(Math.Abs(gap - 2) <= 1.5, $"折叠键没有紧贴在返回右边：间距 {gap:0.#}");
            }
        }
        var title = shell.ProbeTitleActions();
        Require(title.Ok, "标题栏布局错误：" + title.Detail);
        var label = Get<TextBlock>(shell, "AppTitleLabel");
        var actions = Get<StackPanel>(shell, "TitleActions");
        Require(label.Text == "Momoka" && label.ActualWidth > 0
            && Left(label, shell) >= Left(actions, shell) + actions.ActualWidth + 7,
            "应用名未出现在导航按钮右侧");
        Require((int)typeof(ShellSelfCheck).GetMethod("HitAt", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [_window, Left(label, shell) + label.ActualWidth / 2, Top(label, shell) + label.ActualHeight / 2])!
            == Native.HitCaption, "应用名区域不再可拖动");
        var hit = (int)typeof(ShellSelfCheck).GetMethod("HitAt", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [_window, title.X + title.Width / 2, title.Y + title.Height / 2])!;
        Require(hit == Native.HitClient, "侧栏收放后标题栏仍把按钮当拖动区");

        // 栏底那颗设置（2026-11-13 回到 FooterMenuItems）：它和栏里那些条目走同一条度量，得整块留在栏内、
        // 贴着窗口下沿，尺寸也和条目一致 —— 用户令「统一侧边栏按钮的大小」要的正是这个。
        var nav = Get<NavigationView>(shell, "Sidebar");
        var settings = Get<NavigationViewItem>(shell, "SidebarSettings");
        var settingsAt = settings.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point());
        Require(settings.ActualHeight > 0 && settingsAt.Y + settings.ActualHeight <= shell.ActualHeight + 1,
            "栏底的设置入口被裁掉");
        Require(settingsAt.X >= -1 && settingsAt.X + settings.ActualWidth <= host.ActualWidth + 1,
            "栏底的设置入口跑到栏外");
        if (Get<NavigationViewItem>(shell, "SidebarHome") is { ActualHeight: > 0 } sizedHome)
        {
            Require(Math.Abs(settings.ActualHeight - sizedHome.ActualHeight) <= 0.5
                && Math.Abs(settings.ActualWidth - sizedHome.ActualWidth) <= 0.5,
                $"栏底的设置和条目不一样大：{settings.ActualWidth:0.#}×{settings.ActualHeight:0.#} 对 "
                    + $"{sizedHome.ActualWidth:0.#}×{sizedHome.ActualHeight:0.#}");
        }
        foreach (var item in nav.MenuItems.Concat(nav.FooterMenuItems).OfType<NavigationViewItem>()
            .Where(item => item.Visibility == Visibility.Visible))
        {
            var icon = item.Icon;
            Require(icon is { ActualWidth: > 0, ActualHeight: > 0 }, $"侧栏入口 {item.Content} 的图标没有尺寸");
            var bounds = Bounds(icon, host);
            Require(bounds.Left >= -1 && bounds.Right <= host.ActualWidth + 1
                && bounds.Top >= 0 && bounds.Bottom <= host.ActualHeight + 1,
                $"侧栏入口 {item.Content} 的图标位于可见栏外：{bounds}");
            // 只看 TransformToVisual 的矩形是假绿：SplitView 的紧凑栏会把整块面板用裁剪几何推出栏外，
            // 元素位置和尺寸都照旧。这里按裁剪后的可见面积判断，图标必须整块留在栏内。
            var area = bounds.Width * bounds.Height;
            Require(VisibleArea(icon, host) >= area - 0.5,
                $"侧栏入口 {item.Content} 的图标被裁到栏外：可见 {VisibleArea(icon, host):F1}/{area:F1} 平方像素");
        }

        // 账号那一颗在标题栏右上角（2026-11-13）：侧边栏模式里只留一颗人像，字形整块在格子里，
        // 竖直中线和标题栏正中那颗搜索框同一条 —— 用户令「和搜索栏对齐」。搜索框窄窗会收起，收起时只判字形。
        var account = Get<Button>(shell, "AccountButton");
        var accountGlyph = Get<FontIcon>(shell, "AccountGlyph");
        Require(account.Visibility == Visibility.Visible && AccountTextHidden(shell),
            "侧边栏模式里账号还留着文字那一格");
        Require(accountGlyph.Visibility == Visibility.Visible && accountGlyph.ActualWidth > 0,
            "侧边栏模式里账号的人像字形没有露出来");
        Require(accountGlyph.ActualWidth >= accountGlyph.FontSize - 0.5,
            $"账号的人像被内容格挤窄：{accountGlyph.ActualWidth:F1} 对字号 {accountGlyph.FontSize:F1}");
        var glyphBox = Bounds(accountGlyph, host);
        Require(VisibleArea(accountGlyph, host) >= glyphBox.Width * glyphBox.Height - 0.5,
            $"账号的人像被裁到格外交外：可见 {VisibleArea(accountGlyph, host):F1}/{glyphBox.Width * glyphBox.Height:F1} 平方像素");
        var searchBox = Get<AutoSuggestBox>(shell, "TitleSearchBox");
        if (searchBox.Visibility == Visibility.Visible && searchBox.ActualHeight > 0)
        {
            var glyphCentre = Top(accountGlyph, shell) + accountGlyph.ActualHeight / 2;
            var boxCentre = Top(searchBox, shell) + searchBox.ActualHeight / 2;
            Require(Math.Abs(glyphCentre - boxCentre) <= 1.5,
                $"账号的人像没和搜索框对齐：中线 {glyphCentre:0.#} 对 {boxCentre:0.#}");
        }
    }

    /// <summary>账号那两格文字在侧边栏模式里收着（那一档只留一颗人像）。</summary>
    private static bool AccountTextHidden(ShellPage shell) =>
        Get<Grid>(shell, "AccountText").Visibility == Visibility.Collapsed;

    private static double Left(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).X;

    private static double Top(FrameworkElement element, UIElement root) =>
        element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y;

    /// <summary>核对实际菜单项只有当前页面持有实心图标。</summary>
    private static void AssertSidebarIconSelection(ShellPage shell)
    {
        var nav = Get<NavigationView>(shell, "Sidebar");
        var icons = (Dictionary<NavigationViewItem, (IconElement Outline, IconElement Filled)>)
            typeof(ShellPage).GetField("_sidebarIcons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        foreach (var (item, pair) in icons)
            Require(ReferenceEquals(item.Icon, ReferenceEquals(item, nav.SelectedItem) ? pair.Filled : pair.Outline),
                "导航图标未随当前页面切换：" + item.Content);
    }

    /// <summary>UIA 选中一个侧栏项 —— 和用户点它是同一条路（走 <c>SelectionChanged</c>）。</summary>
    private static void SelectSidebarItem(NavigationViewItem item) =>
        ((ISelectionItemProvider)new NavigationViewItemAutomationPeer(item).GetPattern(PatternInterface.SelectionItem)).Select();

    /// <summary>
    /// 侧边栏那两条 MoviePilot 订阅入口（2026-10-13）。判据是 <c>MoviePilotService.Available</c>，所以这条
    /// 用例用假 MoviePilot 走一遍「没连上 → 连上 → 断线」：入口的出现与收起、点开落在订阅列表页、以及断线
    /// 时那一页连同它的返回历史一起退掉。
    /// </summary>
    private async Task MoviePilotSidebarAsync()
    {
        var originalAnimations = HomeMotion.PreferenceEnabled;
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        using var fixture = CreateFixture(server);
        await fixture.SelectAsync();
        fixture.Settings.Ui.AnimationsEnabled = false;
        fixture.Settings.Ui.EnableImmersive = false;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var movies = Get<NavigationViewItem>(shell, "SidebarMoviePilotMovies");
        var series = Get<NavigationViewItem>(shell, "SidebarMoviePilotSeries");
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(1240, 900);
            // 先排版一次：Loaded 那一拍才会按设置里的侧边栏开关把栏立起来。
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            await LayoutAsync(shell);
            Require(Get<Grid>(shell, "SidebarHost").Visibility == Visibility.Visible, "侧栏没有立起来");
            Require(movies.Visibility == Visibility.Collapsed && series.Visibility == Visibility.Collapsed,
                "MoviePilot 没连上时侧栏仍显示订阅入口");

            // **先把栏展开，再连 MoviePilot** —— 应用里那条路就是这个顺序（窗宽那一拍先把栏开起来、
            // MoviePilot 的可用性随后才到），而分区标题的内容可见性是 NavigationView 按面板模式在实现
            // 那一刻算的：标题在栏收着的时候翻成 Visible，之后展开也不回头补（标题占着位置、一个字不画，
            // 2026-10-13 晚实测；「媒体库」那条标题同一个脾气）。这一档顺带就是这一帧截图要的画面。
            var nav = Get<NavigationView>(shell, "Sidebar");
            var host = Get<Grid>(shell, "SidebarHost");
            nav.IsPaneOpen = true;
            await LayoutAsync(shell);
            Require(nav.IsPaneOpen && Math.Abs(host.ActualWidth - 256) < 1, "订阅那一段没有把栏展开");

            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => movies.Visibility == Visibility.Visible && series.Visibility == Visibility.Visible);
            Require(movies.Content as string == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies)
                && series.Content as string == HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries),
                "侧栏订阅入口的文案不是主页那两排的名字");
            // 两条入口 2026-10-13 晚起有了自己的一段：「订阅」那条分区标题必须和它们同进同退，而且站在
            // 「媒体库」那一段**下面**（用户：「侧边栏展开时新增订阅分区，放在媒体库下面」）。媒体库那几项是
            // 运行时插在两条标题之间的，所以只比标题的先后就够。
            var section = Get<NavigationViewItemHeader>(shell, "SidebarSubscriptionsHeader");
            Require(section.Visibility == Visibility.Visible && section.Content as string == "订阅",
                "订阅分区标题没有跟着两条入口一起出现");
            Require(nav.MenuItems.IndexOf(section) > nav.MenuItems.IndexOf(Get<NavigationViewItemHeader>(shell, "SidebarLibrariesHeader")),
                "订阅分区没有排在媒体库那一段下面");
            // 标题得真有高度 —— 「Visibility 是 Visible 可是没画出高度」在截图上只是「少了一行字」，
            // 只看 Visibility 会放过它。
            // 可用性刚从异步登录回到 UI，Visible 不代表新标题已经跑过这一帧布局。
            // 等实际标题有尺寸后仍按原高度与图标裁切守卫检查，不放宽判定。
            await LayoutAsync(section);
            Require(section.ActualHeight > 0, $"订阅分区标题没有画出高度：{section.ActualHeight:0.#}");
            // 图标是照 MoviePilot 网页端复刻的两条 16×16 路径（场记板／带底座的电视）。这里量的是
            // 「PathIcon 在栏里真占了地方、也没被栏裁掉」—— 路径画错或尺寸不对时图标会是空的或只露出一角，
            // 而截图上一眼看不出来的是「它到底画没画出来」。
            foreach (var (entry, item) in new[] { ("电影", movies), ("电视剧", series) })
            {
                Require(item.Icon is { ActualWidth: > 0, ActualHeight: > 0 },
                    $"订阅入口「{entry}」的图标没有尺寸");
                var icon = item.Icon!;
                var bounds = Bounds(icon, host);
                Require(bounds.Left >= -1 && bounds.Right <= host.ActualWidth + 1
                    && bounds.Top >= 0 && bounds.Bottom <= host.ActualHeight + 1
                    && VisibleArea(icon, host) >= bounds.Width * bounds.Height - 0.5,
                    $"订阅入口「{entry}」的图标被裁到栏外：{bounds}");
            }
            await SaveProbeScreenAsync("sidebar-moviepilot-1240-screen.png");

            // 读取中的转圈（2026-10-20）：先把列表那一趟扣在手里再点入口 —— 屏上这时该是一颗转圈加
            // 「正在读取订阅…」，而不是「暂无电影订阅。」（从前那一句在数据回来之前就写上了，几秒后卡片
            // 冒出来把它顶掉）。转圈与空态的判据在视图模型，这里量的是页面真接上了没有、也真画出了高度。
            var list = Pending<HttpResponseMessage>();
            server.PendingList = list;
            SelectSidebarItem(movies);
            await UntilAsync(() => shell.Pages.Content is MoviePilotSubscriptionsPage);
            var page = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await LayoutAsync(page);
            var loadingState = Get<StackPanel>(page, "LoadingState");
            var loadingRing = Get<ProgressRing>(page, "LoadingRing");
            Require(loadingState.Visibility == Visibility.Visible && loadingRing.IsActive,
                "订阅还在读，页面上却没有转圈");
            Require(loadingState.ActualHeight > 0, $"转圈那一块没有画出高度：{loadingState.ActualHeight:0.#}");
            Require(Get<StackPanel>(page, "EmptyState").Visibility == Visibility.Collapsed,
                "订阅还在读，空态就把「暂无电影订阅。」先写上了");
            await SaveProbeScreenAsync("sidebar-moviepilot-loading-screen.png");
            list.SetResult(Json(new { success = true, data = server.Items.Values.ToArray() }));
            server.PendingList = null;
            await UntilAsync(() => page.ViewModel.IsReady);
            await LayoutAsync(page);
            Require(loadingState.Visibility == Visibility.Collapsed && !loadingRing.IsActive,
                "订阅读回来了，转圈还留在页面上");
            Require(shell.CurrentTag == HomeLayout.MoviePilotMovies, "订阅列表页没有落在侧栏那把钥匙上");
            Require(Get<TextBlock>(page, "PageTitle").Text == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies),
                "订阅列表页的标题不对");
            Require(Get<NavigationView>(shell, "Sidebar").SelectedItem is NavigationViewItem { Tag: string tag }
                && tag == HomeLayout.MoviePilotMovies, "订阅列表页没有高亮对应的侧栏项");
            AssertSidebarIconSelection(shell);
            // 这一页只画它那一半：电影入口下电视剧那一排整块收着（数据两份都在，收的是版面）。
            // 两条货架带住在 MoviePilotSubscriptionsView 自己的名字域里，所以从那一层找。
            var view = Get<MoviePilotSubscriptionsView>(page, "Subscriptions");
            Require(Get<ShelfStrip>(view, "MovieSubscriptions").Visibility == Visibility.Visible
                && Get<ShelfStrip>(view, "SeriesSubscriptions").Parent is FrameworkElement hidden
                && hidden.Visibility == Visibility.Collapsed, "电影订阅页没有只画电影那一排");
            Require(Get<StackPanel>(page, "EmptyState").Visibility == Visibility.Collapsed, "有订阅时仍显示空态");
            Require(Get<Button>(view, "RefreshSubscriptions").Visibility == Visibility.Visible
                && Get<Button>(view, "RefreshSeriesSubscriptions").Visibility == Visibility.Visible,
                "订阅列表整页的标题旁没有刷新入口");
            await SaveProbeScreenAsync("sidebar-moviepilot-page-screen.png");

            // 订阅墙的工具栏（2026-11-12 用户令）：删掉那一排带字的药丸，换成网页端订阅页头部那三个图标
            // ——筛选、拖拽排序、批量；排序项并进筛选面板。这里量的是三颗键真立起来、面板里真是
            // 「名称＋状态＋排序」、选中一行真把墙筛掉，而不是只读 XAML 文本。
            var wall = Get<MoviePilotSubscriptionWallView>(page, "Wall");
            Require(wall.Visibility == Visibility.Visible, "分类页没有立起订阅墙");
            var wallFilter = Get<Button>(wall, "WallFilterButton");
            var wallSortMode = Get<Button>(wall, "WallSortModeButton");
            var wallBatch = Get<Button>(wall, "WallBatchButton");
            Require(wallFilter.ActualWidth > 0 && wallSortMode.ActualWidth > 0 && wallBatch.ActualWidth > 0,
                $"订阅墙三颗图标键没有尺寸：{wallFilter.ActualWidth:0.#}／{wallSortMode.ActualWidth:0.#}／{wallBatch.ActualWidth:0.#}");
            Require(Get<Button>(wall, "WallRefresh").ActualWidth > 0 && Get<Button>(wall, "WallMaintenanceButton").ActualWidth > 0,
                "订阅墙的刷新／维护入口没有画出来");
            var pills = Descendants(wall).OfType<Button>()
                .Select(button => button.Content as string)
                .Where(text => text is "筛选订阅" or "排序订阅" or "拖拽排序" or "批量操作" or "维护" or "刷新订阅")
                .ToArray();
            Require(pills.Length == 0, "订阅墙还留着带字的药丸键：" + string.Join("、", pills));

            wallFilter.Flyout.ShowAt(wallFilter);
            var filterOptions = Get<StackPanel>(wall, "WallFilterOptions");
            var sortOptions = Get<StackPanel>(wall, "WallSortOptions");
            await UntilAsync(() => filterOptions.Children.Count == 4 && sortOptions.Children.Count == 3);
            Require(filterOptions.Children.Count == 4 && sortOptions.Children.Count == 3,
                $"筛选面板的条目不对：状态 {filterOptions.Children.Count} 项、排序 {sortOptions.Children.Count} 项（电影应为 4／3）");
            Require(filterOptions.Children.Concat(sortOptions.Children).All(row => row is Button { Content: Grid }),
                "筛选面板的行不是「图标＋标签＋勾」那种行");
            await LayoutAsync(wall);
            await SaveProbeScreenAsync("mp-wall-filter-panel-screen.png");
            // 选状态：墙跟着筛、面板收起（网页端 selectFilter）。
            InvokeSubscriptionButton((Button)filterOptions.Children[3]);
            await UntilAsync(() => wall.ViewModel.WallFilter == MoviePilotSubscriptionWallFilter.Paused);
            Require(wall.ViewModel.WallFilter == MoviePilotSubscriptionWallFilter.Paused, "选中「暂停」没有改到筛选条件");
            await UntilAsync(() => !wallFilter.Flyout.IsOpen);
            Require(!wallFilter.Flyout.IsOpen, "选完状态，筛选面板没有收起");
            wall.ViewModel.SelectWallFilter(MoviePilotSubscriptionWallFilter.All);
            await LayoutAsync(wall);
            // 2026-11-16 用户令：墙上的卡片宽度要跟媒体库一样跟着窗口走（从前钉死 340／352）。
            await CheckSubscriptionWallSizingAsync(wall);
            await CheckSubscriptionWallMenuAnchorAsync(wall, "movie");

            SelectSidebarItem(series);
            await UntilAsync(() => shell.CurrentTag == HomeLayout.MoviePilotSeries);
            // 换目的地是**新的一页**（Frame 不缓存页面），旧那一页在 OnNavigatedFrom 里已经放开它的订阅数据 ——
            // 从旧实例读到的 ViewModel 已经换回它自己的备用属主，IsReady 永远是假。
            var seriesPage = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await UntilAsync(() => seriesPage.ViewModel.IsReady);
            await LayoutAsync(seriesPage);
            Require(Get<TextBlock>(seriesPage, "PageTitle").Text == HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries),
                "换到电视剧入口后标题没跟着换");
            var seriesView = Get<MoviePilotSubscriptionsView>(seriesPage, "Subscriptions");
            Require(Get<ShelfStrip>(seriesView, "SeriesSubscriptions").Visibility == Visibility.Visible
                && Get<ShelfStrip>(seriesView, "MovieSubscriptions").Parent is FrameworkElement collapsed
                && collapsed.Visibility == Visibility.Collapsed, "电视剧订阅页没有只画电视剧那一排");
            await CheckSubscriptionWallMenuAnchorAsync(Get<MoviePilotSubscriptionWallView>(seriesPage, "Wall"), "series");

            // 断线：两条入口当场收起，那一条分区标题跟着收，这一页也退掉，返回历史里不留。
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => movies.Visibility == Visibility.Collapsed && series.Visibility == Visibility.Collapsed);
            Require(section.Visibility == Visibility.Collapsed, "两条入口收起后「订阅」分区标题还留在栏里");
            await UntilAsync(() => shell.Pages.Content is not MoviePilotSubscriptionsPage);
            Require(shell.Pages.BackStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage))
                && shell.Pages.ForwardStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage)),
                "断线后返回或前进历史里还留着订阅列表页");
            // 断线只收那两条入口：账号那一颗和栏底的设置照旧在各自的位置上。
            var account = Get<Button>(shell, "AccountButton");
            Require(account.Visibility == Visibility.Visible
                && Left(account, shell) > shell.ActualWidth / 2
                && Top(account, shell) + account.ActualHeight <= HomePage.ChromeHeight + 1,
                "断线后账号那一颗离开了标题栏右上角");
            Require(Get<NavigationViewItem>(shell, "SidebarSettings").Visibility == Visibility.Visible, "断线后设置入口丢了");
        }
        finally
        {
            Call(shell, "StopMoviePilotMonitoring");
            // 探针的 shell 用完即从视觉树移除并丢弃：让当前页面放开它的加载与订阅就够了。
            // 不走产品 ReleaseContent 那一步清 Frame 返回栈 —— 断线那一拍的导航可能仍在
            // Navigating，WinUI 会以 0x800710DD 拒绝 Clear，把本已通过的检查盖成失败。
            if (shell.Pages.Content is IShellContent current) current.Release();
            shell.SignInRoot.Detach();
            Field(shell, "_window", null);
            _root.Children.Remove(shell);
            _window!.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(originalAnimations);
            ResizeInspect(1420, 980);
        }
    }

    private async Task SidebarLibraryPreviewAsync()
    {
        var motion = HomeMotion.PreferenceEnabled;
        using var fixture = CreateFixture();
        fixture.Settings.Ui.EnableImmersive = false;
        fixture.Settings.Ui.AnimationsEnabled = false;
        var picture = await FixtureImageAsync(400, 600);
        var movies = Enumerable.Range(0, 3).Select(index => new EmbyItem
        {
            Id = "preview-movie-" + index,
            Name = "预览影片 " + (index + 1),
            Type = EmbyItemType.Movie,
            ImageTags = new() { ["Primary"] = "poster" }
        }).ToArray();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            Require(request.Method == HttpMethod.Get, "预览探针拒绝服务器写入");
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            if (path.EndsWith("/Views", StringComparison.Ordinal))
                return Task.FromResult(Json(new
                {
                    Items = new[] { new EmbyItem
                {
                    Id = "preview-movies", Name = "预览电影库", Type = EmbyItemType.CollectionFolder, CollectionType = "movies"
                } },
                    TotalRecordCount = 1
                }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(movies));
            if (path.EndsWith("/Items", StringComparison.Ordinal) || path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = movies, TotalRecordCount = movies.Length }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        await fixture.SelectAsync();
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        try
        {
            HomeMotion.ApplyPreference(false);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady && shell.LibraryViews.Count == 1);
            var tag = HomeLayout.LibraryKey(shell.LibraryViews.Single());
            foreach (var width in new[] { 760, 600 })
            {
                ResizeProbeClient(width, 650);
                Get<NavigationView>(shell, "Sidebar").IsPaneOpen = false;
                await LayoutAsync(shell);
                await InspectLibraryPreviewAsync(shell, fixture, tag, width);
            }
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)),
                "预览界面验证触发播放或服务器写入");
        }
        finally
        {
            Call(shell, "HideLibraryPreview");
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            Field(shell, "_window", null);
            _root.Children.Remove(shell);
            _window!.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(motion);
            ResizeInspect(1420, 980);
        }
    }

    private async Task InspectLibraryPreviewAsync(ShellPage shell, Fixture fixture, string moviesTag, int width)
    {
        var nav = Get<NavigationView>(shell, "Sidebar");
        var host = Get<Grid>(shell, "SidebarHost");
        // 折叠侧栏的媒体库悬停预览（2026-11-13 用户令）：沿图标栏右沿展开、不压栏，库名/封面在，
        // 聚合模式下多一颗「查看更多」。展示入口验证布局/图片，静止计时与点击抑制由源码链接夹具覆盖。
        var preview = Get<Border>(shell, "LibraryPreviewPanel");
        var previewCovers = Get<Grid>(shell, "LibraryPreviewCovers");
        var previewTitle = Get<TextBlock>(shell, "LibraryPreviewTitle");
        var previewMeta = Get<TextBlock>(shell, "LibraryPreviewMeta");
        var previewMore = Get<Button>(shell, "LibraryPreviewMore");
        var previewItem = nav.MenuItems.OfType<NavigationViewItem>().Single(item => item.Tag as string == moviesTag);
        var aggregate = fixture.Settings.AggregateMode;
        try
        {
            fixture.Settings.AggregateMode = true;
            // 明确展示入口只验布局/图片；静止计时与点击抑制由源码链接夹具验证，不冒充物理指针。
            await (Task)Call(shell, "ShowLibraryPreviewAsync", previewItem, moviesTag)!;
            await UntilAsync(() => preview.Visibility == Visibility.Visible && previewCovers.Children.Count == 3);
            await LayoutAsync(preview);
            Require(Left(preview, shell) >= host.ActualWidth + 7 && Top(preview, shell) >= HomePage.ChromeHeight - 1,
                $"媒体库悬停预览压住侧栏或跑出内容区：left={Left(preview, shell):0} rail={host.ActualWidth:0} top={Top(preview, shell):0}");
            Require(previewTitle.Text == previewItem.Content as string, "媒体库悬停预览的库名不对");
            Require(previewMeta.Visibility == Visibility.Collapsed, "媒体库悬停预览仍显示服务器名副标题");
            Require(previewMore.Visibility == Visibility.Visible, "聚合模式下没有「查看更多」");
            // 一颗封面现在是一格「图 + 片名」的竖排（2026-11-14 用户令「给封面下方加上片名」），
            // 按钮在格子里，不再是预览网格的直接孩子。
            var coverButtons = Descendants(previewCovers).OfType<Button>().ToArray();
            Require(coverButtons.Length == 3, "可播放封面没有使用三颗原生按钮");
            var coverTitles = previewCovers.Children.OfType<StackPanel>()
                .Select(cell => cell.Children.OfType<TextBlock>().LastOrDefault()?.Text).ToArray();
            Require(coverTitles.Length == 3 && coverTitles.All(title => !string.IsNullOrEmpty(title)),
                "预览封面下方没有显示片名");
            foreach (var button in coverButtons)
            {
                Require(button.Tag is EmbyItem item && AutomationProperties.GetName(button) == $"播放：{item.Name}"
                    && AutomationProperties.GetAutomationId(button).StartsWith("SidebarLibraryPreviewPlay_", StringComparison.Ordinal)
                    && new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) is IInvokeProvider,
                    "封面播放按钮没有正确的可访问名称、定位句柄或原生调用语义");
            }
            await UntilAsync(() => Descendants(previewCovers).OfType<Image>().Any(image => image.Source is not null));
            await SaveProbeScreenAsync($"sidebar-library-preview-{width}-screen.png");

            Call(shell, "HideLibraryPreview");
            await UntilAsync(() => preview.Visibility == Visibility.Collapsed);

            fixture.Settings.AggregateMode = false;
            await (Task)Call(shell, "ShowLibraryPreviewAsync", previewItem, moviesTag)!;
            await UntilAsync(() => preview.Visibility == Visibility.Visible);
            await LayoutAsync(preview);
            Require(previewMore.Visibility == Visibility.Collapsed, "非聚合模式仍显示「查看更多」");
        }
        finally
        {
            fixture.Settings.AggregateMode = aggregate;
            Call(shell, "HideLibraryPreview");
        }

    }


}

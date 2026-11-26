using System.Diagnostics;
using System.Globalization;
using System.Net;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Localization;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.UI.ViewManagement;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotCalendarAsync()
    {
        var originalTheme = ThemeHost.Current;
        var originalAnimations = HomeMotion.PreferenceEnabled;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = MoviePilotCalendarLayout.Month(today);
        var first = MoviePilotCalendarLayout.Window(month).Start;
        using var moviePilot = new CalendarMoviePilotProbeServer(month, await FixtureImageAsync(96, 144));
        using var aniBt = new CalendarAniBtProbeServer(today);
        using var fixture = CreateFixture(moviePilot, aniBt);
        fixture.Settings.Ui.EnableImmersive = false;
        fixture.Settings.Ui.AnimationsEnabled = false;
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        var navigationCompleted = false;
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(1240, 760);
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            var sidebar = Get<NavigationView>(shell, "Sidebar");
            sidebar.IsPaneOpen = true;
            await LayoutAsync(shell);
            var entry = Get<NavigationViewItem>(shell, "SidebarMoviePilotCalendar");
            Require(entry.Visibility == Visibility.Visible, "匿名来源均可用时侧栏没有日历");
            SelectSidebarItem(entry);
            await UntilAsync(() => shell.Pages.Content is MoviePilotCalendarPage && moviePilot.InitialRead.Task.IsCompleted);
            navigationCompleted = true;
            var page = (MoviePilotCalendarPage)shell.Pages.Content;
            await LayoutAsync(page);
            var scroll = Get<ScrollViewer>(page, "CalendarScroll");
            var grid = Get<Grid>(page, "MonthGrid");
            var title = Get<TextBlock>(page, "MonthTitle");
            var notice = Get<InfoBar>(page, "CalendarNotice");
            var crowdedDate = month;
            Require(page.ViewModel.Busy && !notice.IsOpen && !CalendarCards(grid).Any(), "日历在首次响应前未处于独立的加载状态");
            RequireCalendarGrid(grid, scroll, first);
            await SaveProbeScreenAsync("calendar-loading-screen.png");

            var watch = Stopwatch.StartNew();
            moviePilot.InitialReply.TrySetResult(true);
            await UntilAsync(() => moviePilot.StatisticsRead.Task.IsCompleted);
            await UntilAsync(() => CalendarCards(grid).Count() == 6);
            var firstContentMs = watch.ElapsedMilliseconds;
            await UntilAsync(() => moviePilot.PosterReads == 1);
            Require(page.ViewModel.Busy && !moviePilot.StatisticsReply.Task.IsCompleted &&
                !moviePilot.PosterReply.Task.IsCompleted,
                "首屏安排没有独立于分集统计和封面发布");
            moviePilot.StatisticsReply.TrySetResult(true);
            moviePilot.PosterReply.TrySetResult(true);
            await UntilAsync(() => !page.ViewModel.Busy && !notice.IsOpen);
            await LayoutAsync(page);
            var firstDay = CalendarDay(grid, first);
            var crowdedDay = CalendarDay(grid, crowdedDate);
            Require(CalendarCards(crowdedDay).Count() == 5, "拥挤日期没有先保留五条安排");
            var establishedTitle = title.Text;
            Require(!string.IsNullOrWhiteSpace(establishedTitle) &&
                AutomationProperties.GetHeadingLevel(title) == AutomationHeadingLevel.Level1,
                "月份标题没有显示或未标为一级标题");
            var seriesCard = CalendarCards(crowdedDay).Single(card =>
                Get<TextBlock>(card, "EventTitle").Text == "01 · 匿名剧集");
            await UntilAsync(() => Get<Image>(seriesCard, "PosterImage").Source is not null);
            var peerName = FrameworkElementAutomationPeer.CreatePeerForElement(seriesCard)?.GetName();
            Require(peerName?.Contains("匿名剧集", StringComparison.Ordinal) == true,
                "日历卡片没有实际读屏名称");
            var library = Get<TextBlock>(seriesCard, "LibraryText");
            Require(library.Visibility == Visibility.Visible &&
                library.Text.StartsWith(UiText.Translate("已入库"), StringComparison.Ordinal) &&
                library.Text.Contains("1/1", StringComparison.Ordinal),
                "首屏后的分集统计没有回填实际卡片");
            Require(moviePilot.StatisticsReads == 1 && moviePilot.PosterReads == 1,
                "同订阅或同地址被重复读取");

            var moreId = "MoviePilotCalendarMore" + crowdedDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var more = Descendants(crowdedDay).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == moreId);
            var moreLabel = FrameworkElementAutomationPeer.CreatePeerForElement(more)?.GetName();
            Require(!string.IsNullOrWhiteSpace(moreLabel), "展开按钮缺少可读名称");
            Require(more.Focus(FocusState.Keyboard), "超过五条时不能使用键盘定位展开按钮");
            more.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await LayoutAsync(page);
            var offset = scroll.VerticalOffset;
            InvokeCalendarButton(more);
            await UntilAsync(() => CalendarCards(crowdedDay).Count() == 6);
            await LayoutAsync(page);
            Require(Math.Abs(scroll.VerticalOffset - offset) < 2 && ReferenceEquals(firstDay, CalendarDay(grid, first)),
                "展开日期时重建空日格或跳动滚动位置");
            Require(FrameworkElementAutomationPeer.CreatePeerForElement(more)?.GetName() != moreLabel,
                "展开后可访问名称没有变为收起");
            Require(ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), more), "原格展开后丢失键盘焦点");
            InvokeCalendarButton(more);
            await UntilAsync(() => CalendarCards(crowdedDay).Count() == 5);
            await ScrollProbeAsync(scroll, 0);

            // 同一页面从宽到最小客户区再恢复，侧栏保持打开，观察真实控件的布局而不是窗口宽度常量。
            var next = Get<Button>(page, "NextMonth");
            var sources = Get<Segmented>(page, "CalendarSources");
            var filters = Get<Segmented>(page, "MediaType");
            var seriesOption = Get<SegmentedItem>(page, "SeriesMedia");
            var aniBtOption = Get<SegmentedItem>(page, "AniBtCalendarSource");
            foreach (var width in new[] { 1240, 760, 600, 1240 })
            {
                ResizeProbeClient(width, width == 600 ? 560 : 760);
                await LayoutAsync(page);
                RequireCalendarGrid(grid, scroll, first);
                RequireCalendarControlFits(page, title);
                RequireCalendarControlFits(page, next);
                RequireCalendarControlFits(page, sources);
                RequireCalendarControlFits(page, filters);
                Require(Get<TextBlock>(seriesCard, "EventTitle").ActualWidth > 0 &&
                    ReferenceEquals(seriesCard, CalendarCards(crowdedDay).First()), "缩放没有保留实际卡片或标题被挤没");
                var compact = seriesCard.ActualWidth < MoviePilotCalendarLayout.TextOnlyCardWidth;
                var expectedVisibility = compact ? Visibility.Collapsed : Visibility.Visible;
                var posterVisibility = Get<Border>(seriesCard, "PosterFrame").Visibility;
                Require((width != 600 || compact) && posterVisibility == expectedVisibility && library.Visibility == expectedVisibility
                    && AutomationProperties.GetName(seriesCard).Contains("1/1", StringComparison.Ordinal),
                    $"{width} DIP 窗口，卡宽 {seriesCard.ActualWidth:0.0}，海报 {posterVisibility}，入库 {library.Visibility}：极窄布局或完整可访问名称错误");
                if (width != 1240) await SaveProbeScreenAsync($"calendar-{width}-screen.png");
            }

            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(page);
                RequireCalendarGrid(grid, scroll, first);
                var actualTheme = ThemeHost.Current;
                if (!new AccessibilitySettings().HighContrast)
                    Require(title.Foreground is SolidColorBrush color && color.Color == ThemeHost.ToColor(actualTheme.Colors.Text),
                        $"日历月份的文字未跟随 {actualTheme.Id} 主题");
                await SaveProbeScreenAsync($"calendar-theme-{actualTheme.Id}-screen.png");
            }
            InvokeCalendarButton(Get<Button>(page, "CurrentMonth"));
            var todayCell = CalendarDay(grid, today);
            await UntilAsync(() =>
            {
                var box = Bounds(todayCell, scroll);
                return box.Top < scroll.ViewportHeight && box.Bottom > 0;
            });
            Require(moviePilot.CalendarReads == 1 && AutomationProperties.GetName(todayCell)
                .Contains(today.ToString("D", CultureInfo.GetCultureInfo(UiText.Language)), StringComparison.Ordinal),
                "同月今天按钮重新请求了日历，或当天缺少完整日期");
            await SaveProbeScreenAsync("calendar-today-screen.png");
            await ScrollProbeAsync(scroll, 0);
            Require(FrameworkElementAutomationPeer.CreatePeerForElement(next)?.GetName() is { Length: > 0 }
                && next.Focus(FocusState.Keyboard) && ReferenceEquals(FocusManager.GetFocusedElement(page.XamlRoot), next),
                "翻月按钮没有辅助功能名称或键盘焦点");
            await SaveProbeScreenAsync("calendar-keyboard-focus-screen.png");

            SelectCalendarOption(filters, seriesOption);
            await UntilAsync(() => ReferenceEquals(filters.SelectedItem, seriesOption) && CalendarCards(grid).Count() == 1);
            Require(moviePilot.CalendarReads == 1, "电影/剧集筛选重新读取了日历");
            SelectCalendarOption(filters, Get<SegmentedItem>(page, "AllMedia"));
            await UntilAsync(() => CalendarCards(grid).Count() == 6);
            SelectCalendarOption(sources, aniBtOption);
            await UntilAsync(() => ReferenceEquals(sources.SelectedItem, aniBtOption) && !page.ViewModel.Busy && CalendarCards(grid).Count() == 1);
            Require(Get<TextBlock>(CalendarCards(grid).Single(), "EventTitle").Text == "匿名 AniBT 首播" && aniBt.Reads > 0,
                "切换 AniBT 后未展示真实来源的匿名数据");
            await SaveProbeScreenAsync("calendar-anibt-screen.png");
            var mpOption = Get<SegmentedItem>(page, "MoviePilotCalendarSource");
            SelectCalendarOption(sources, mpOption);
            await UntilAsync(() => ReferenceEquals(sources.SelectedItem, mpOption) && !page.ViewModel.Busy && CalendarCards(grid).Count() == 6);

            // 跨两个月查旧结果不能回填。旧响应在新空月布局完成之后才送回，故意不遵从取消。
            InvokeCalendarButton(next);
            await UntilAsync(() => moviePilot.StaleRead.Task.IsCompleted && page.ViewModel.Busy);
            InvokeCalendarButton(next);
            await UntilAsync(() => !page.ViewModel.Busy && page.ViewModel.Month == month.AddMonths(2));
            Require(CalendarCards(grid).Count() == 0 && !notice.IsOpen &&
                Get<TextBlock>(page, "MonthSummary").Text == UiText.Translate("本月暂无符合条件的播出安排。"),
                "空月没有清晰的独立空态");
            moviePilot.StaleReply.TrySetResult(true);
            await moviePilot.StaleReturned.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await LayoutAsync(page);
            Require(page.ViewModel.Month == month.AddMonths(2) && !notice.IsOpen && !CalendarCards(grid).Any(), "迟到结果覆盖了后选月份");
            await SaveProbeScreenAsync("calendar-empty-screen.png");

            // 403 的匿名正文有哨兵，验证页内不泄出响应内容；随后从实际 InfoBar 重试。
            moviePilot.ForbidNext();
            InvokeCalendarButton(Get<Button>(page, "PreviousMonth"));
            await UntilAsync(() => notice.IsOpen);
            var retry = Get<Button>(page, "RetryCalendar");
            Require(!page.ViewModel.Busy && notice.Severity == InfoBarSeverity.Error &&
                ReferenceEquals(notice.ActionButton, retry) && retry.Visibility == Visibility.Visible &&
                !notice.Message.Contains(CalendarMoviePilotProbeServer.SensitiveBody, StringComparison.Ordinal), "失败提示缺少恢复入口、仍在加载或包含原始响应");
            await SaveProbeScreenAsync("calendar-retry-screen.png");
            var requestsBeforeRetry = moviePilot.CalendarReads;
            InvokeCalendarButton(retry);
            await UntilAsync(() => !page.ViewModel.Busy && !notice.IsOpen && CalendarCards(grid).Any());
            Require(moviePilot.CalendarReads == requestsBeforeRetry + 1, "就地重试没有发出一趟新的读取");
            InvokeCalendarButton(Get<Button>(page, "CurrentMonth"));
            await UntilAsync(() => page.ViewModel.Month == month && !page.ViewModel.Busy && CalendarCards(grid).Count() == 6);
            Require(title.Text == establishedTitle, "返回今天未恢复原月份");
            var requestsBeforeRefresh = moviePilot.CalendarReads;
            InvokeCalendarButton(Get<Button>(page, "RefreshCalendar"));
            await UntilAsync(() => moviePilot.CalendarReads == requestsBeforeRefresh + 1 && !page.ViewModel.Busy && !notice.IsOpen);

            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "calendar-observations.txt"),
                $"匿名内存 HTTP；从释放日期响应到实际事件卡出现={firstContentMs}ms（统计及海报尚未放行）；DPI={page.XamlRoot.RasterizationScale:0.00}\n"
                + $"月份请求={moviePilot.CalendarReads}；统计请求={moviePilot.StatisticsReads}；海报请求={moviePilot.PosterReads}；AniBT 请求={aniBt.Reads}\n");
        }
        finally
        {
            moviePilot.InitialReply.TrySetResult(true);
            moviePilot.StaleReply.TrySetResult(true);
            moviePilot.StatisticsReply.TrySetResult(true);
            moviePilot.PosterReply.TrySetResult(true);
            Call(shell, "StopMoviePilotMonitoring");
            if (navigationCompleted) Call(shell, "ReleaseContent");
            else if (shell.Pages.Content is IShellContent content) content.Release();
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
            ThemeHost.Apply(originalTheme);
            HomeMotion.ApplyPreference(originalAnimations);
        }
    }

    private static void InvokeCalendarButton(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        Require(peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider && !string.IsNullOrWhiteSpace(peer.GetName()),
            "日历按钮缺少名称或 Invoke：" + button.Name);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static void SelectCalendarOption(Segmented group, SegmentedItem item)
    {
        var groupPeer = FrameworkElementAutomationPeer.CreatePeerForElement(group);
        var directPeer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
        var name = AutomationProperties.GetName(item);
        if (string.IsNullOrWhiteSpace(name)) name = directPeer?.GetName();
        var peer = groupPeer?.GetChildren()?.SingleOrDefault(child => child.GetName() == name) ?? directPeer;
        Require(peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider && !string.IsNullOrWhiteSpace(peer.GetName()),
            "日历选项缺少名称或 SelectionItem：" + item.Name);
        ((ISelectionItemProvider)peer!.GetPattern(PatternInterface.SelectionItem)).Select();
    }

    private static Border CalendarDay(Grid grid, DateOnly date)
    {
        var id = "CalendarDay" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return grid.Children.OfType<Border>().Single(day => AutomationProperties.GetAutomationId(day) == id);
    }

    private static IEnumerable<MoviePilotCalendarEventCard> CalendarCards(DependencyObject root) =>
        Descendants(root).OfType<MoviePilotCalendarEventCard>();

    private static void RequireCalendarGrid(Grid grid, ScrollViewer scroll, DateOnly first)
    {
        Require(grid.ColumnDefinitions.Count == 7 && grid.RowDefinitions.Count == 7 &&
            scroll.HorizontalScrollMode == ScrollMode.Disabled && scroll.ScrollableWidth < 1,
            "日历未按六周七列排布，或产生了横向滚动");
        for (var offset = 0; offset < 42; offset++)
        {
            var day = CalendarDay(grid, first.AddDays(offset));
            Require(Grid.GetColumn(day) == offset % 7 && Grid.GetRow(day) == 1 + offset / 7 &&
                day.ActualWidth > 0 && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(day)),
                "实际日期位置、宽度或可访问日期名称不正确");
        }
    }

    private static void RequireCalendarControlFits(FrameworkElement page, FrameworkElement control)
    {
        var box = Bounds(control, page);
        Require(box.Left >= -1 && box.Right <= page.ActualWidth + 1 && box.Width > 0 && box.Height > 0,
            $"宽度 {page.ActualWidth:0} DIP 的日历控件被裁切：{control.Name}，{box}");
    }

    private sealed class CalendarMoviePilotProbeServer(DateOnly month, byte[] poster) : HttpMessageHandler
    {
        internal const string SensitiveBody = "calendar-fixture-hidden-response";
        private int _calendarReads;
        private int _statisticsReads;
        private int _posterReads;
        private int _forbidNext;
        internal int CalendarReads => Volatile.Read(ref _calendarReads);
        internal int StatisticsReads => Volatile.Read(ref _statisticsReads);
        internal int PosterReads => Volatile.Read(ref _posterReads);
        internal TaskCompletionSource<bool> InitialRead { get; } = Pending<bool>();
        internal TaskCompletionSource<bool> InitialReply { get; } = Pending<bool>();
        internal TaskCompletionSource<bool> StatisticsRead { get; } = Pending<bool>();
        internal TaskCompletionSource<bool> StatisticsReply { get; } = Pending<bool>();
        internal TaskCompletionSource<bool> PosterReply { get; } = Pending<bool>();
        internal TaskCompletionSource<bool> StaleRead { get; } = Pending<bool>();
        internal TaskCompletionSource<bool> StaleReply { get; } = Pending<bool>();
        internal TaskCompletionSource<bool> StaleReturned { get; } = Pending<bool>();
        internal void ForbidNext() => Interlocked.Exchange(ref _forbidNext, 1);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            Require(uri is { Scheme: "https", Host: "moviepilot.invalid", Port: 3001 }, "日历探针拒绝 MoviePilot 非匿名目的地");
            var path = uri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/v1/login/access-token")
                return Json(new { access_token = "calendar-fixture-session", super_user = false });
            Require(request.Method == HttpMethod.Get, "日历探针拒绝服务器写入");
            switch (path)
            {
                case "/api/v1/dashboard/system":
                    return Json(new { version = "fixture" });
                case "/api/v1/user/current":
                    return Json(new { is_active = true, is_superuser = false });
                case "/api/v1/subscribe/":
                    return Json(Array.Empty<object>());
                case "/api/v1/subscribe/calendar":
                    return await ReadCalendarAsync(request, cancellationToken);
                case "/api/v1/subscribe/files/1":
                    Interlocked.Increment(ref _statisticsReads);
                    StatisticsRead.TrySetResult(true);
                    await StatisticsReply.Task.WaitAsync(cancellationToken);
                    return Json(new
                    {
                        subscribe = new { id = 1, name = "01 · 匿名剧集", type = "电视剧", season = 1, total_episode = 1 },
                        episodes = new Dictionary<string, object> { ["1"] = new { library = new[] { new { file_path = "/fixture/anonymous.mkv" } } } }
                    });
                case "/api/v1/system/cache/image":
                    Interlocked.Increment(ref _posterReads);
                    await PosterReply.Task.WaitAsync(cancellationToken);
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) };
                default:
                    return new(HttpStatusCode.NotFound);
            }
        }

        private async Task<HttpResponseMessage> ReadCalendarAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref _calendarReads);
            var start = DateOnly.ParseExact(Query(request, "start")!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateOnly.ParseExact(Query(request, "end")!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Require(end.DayNumber - start.DayNumber == 42 && Query(request, "page") == "1",
                "真实页面没有读取一个完整六周窗口");
            if (Interlocked.Exchange(ref _forbidNext, 0) == 1)
                return new(HttpStatusCode.Forbidden) { Content = new StringContent(SensitiveBody) };

            var (currentWindow, _) = MoviePilotCalendarLayout.Window(month);
            var (nextWindow, _) = MoviePilotCalendarLayout.Window(month.AddMonths(1));
            if (start == currentWindow && InitialRead.TrySetResult(true))
                await InitialReply.Task.WaitAsync(token);
            if (start == nextWindow && StaleRead.TrySetResult(true))
            {
                // A remote response can arrive after view cancellation; unblock it only after the next month is visible.
                await StaleReply.Task;
                StaleReturned.TrySetResult(true);
            }

            if (start == currentWindow)
            {
                var items = Enumerable.Range(1, 6).Select(id => new
                {
                    date = month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    subscribe = new
                    {
                        id,
                        name = id == 1 ? "01 · 匿名剧集" : $"{id:00} · 匿名长标题电影",
                        type = id == 1 ? "电视剧" : "电影",
                        season = 1,
                        poster = "https://calendar-fixture.invalid/shared-poster.jpg",
                        total_episode = 1
                    },
                    episodes = id == 1 ? new[] { new { episode_number = 1, name = "第一集" } } : [],
                    runtime = id == 1 ? 45 : 110
                }).Cast<object>().Append(Movie(month.AddDays(7), 7, "下周匿名电影")).ToArray();
                return Json(new { total = items.Length, items });
            }

            if (start == nextWindow)
                return Json(new { total = 1, items = new[] { Movie(month.AddMonths(1), 8, "上次请求的匿名电影") } });
            return Json(new { total = 0, items = Array.Empty<object>() });
        }

        private static object Movie(DateOnly date, int id, string name) => new
        {
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            subscribe = new { id, name, type = "电影", poster = "https://calendar-fixture.invalid/shared-poster.jpg" },
            episodes = Array.Empty<object>(),
            runtime = 102
        };
    }

    private sealed class CalendarAniBtProbeServer(DateOnly today) : HttpMessageHandler
    {
        private int _reads;
        internal int Reads => Volatile.Read(ref _reads);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoint = request.RequestUri is { Scheme: "https", Host: "anibt.net", AbsolutePath: "/api/seasons/anime" };
            Require(request.Method == HttpMethod.Get && endpoint, "日历探针拒绝 AniBT 未登记的目标或动作");
            Require(request.Headers.Authorization is null, "匿名 AniBT 日历被附加了凭据");
            Interlocked.Increment(ref _reads);
            return Task.FromResult(Json(new
            {
                ok = true,
                data = new
                {
                    scope = new { kind = "season" },
                    truncated = false,
                    byWeekday = new[] { new { animes = new[]
                    {
                        new
                        {
                            animeId = "calendar-fixture-anime", title = new { primary = "匿名 AniBT 首播" }, kind = "TV",
                            premierePrecision = "day", premiereDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        }
                    } } }
                }
            }));
        }
    }
}

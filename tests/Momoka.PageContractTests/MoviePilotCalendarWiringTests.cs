using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotCalendarWiringTests
{
    internal static void Register()
    {
        Test("MP 日历接线：侧栏真实入口与页面 Tag 指向同一目的地", () =>
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/ShellPage.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var item = xaml.Descendants().Single(element => element.Attribute(x + "Name")?.Value == "SidebarMoviePilotCalendar");
            Assert.Equal("NavigationViewItem", item.Name.LocalName);
            Assert.Equal("日历", item.Attribute("Content")?.Value);
            Assert.Equal("moviepilot:calendar", item.Attribute("Tag")?.Value);
            Assert.Equal("Collapsed", item.Attribute("Visibility")?.Value);
            var page = Read("MoviePilotCalendarPage.xaml.cs");
            Assert.Contains("Destination = \"moviepilot:calendar\"", page);
            Assert.Contains("Tag = Destination;", page);
            var sidebar = Read("ShellPage.Sidebar.cs");
            Assert.Contains("SidebarMoviePilotCalendar.Visibility = CalendarAvailable ? Visibility.Visible : Visibility.Collapsed", sidebar);
            Assert.Contains("MoviePilotCalendarPage.Destination when CalendarAvailable => SidebarMoviePilotCalendar", sidebar);
        });

        Test("MP 日历接线：直接导航再次核对可用性且不依赖媒体服务器登录", () =>
        {
            var shell = Read("ShellPage.xaml.cs");
            var start = shell.IndexOf("if (tag == MoviePilotCalendarPage.Destination)", StringComparison.Ordinal);
            var end = shell.IndexOf("return;", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            Assert.Contains("if (!CalendarAvailable) return;", shell[start..(end + 7)]);
            var block = shell[start..shell.IndexOf("// MoviePilot 的两条订阅入口", start, StringComparison.Ordinal)];
            Assert.Contains("Open(typeof(MoviePilotCalendarPage)", block);
            Assert.DoesNotContain("_session", block);
            var moviePilot = Read("ShellPage.MoviePilot.cs");
            var availability = moviePilot[moviePilot.IndexOf("private bool CalendarAvailable", StringComparison.Ordinal)..];
            availability = availability[..availability.IndexOf(';')];
            Assert.Contains("HasMoviePilot(_moviePilotService, Settings.MoviePilot)", availability);
            Assert.Contains("|| MoviePilotCalendarViewModel.HasAniBt(Settings.MoviePilot)", availability);
            Assert.Contains("GetRequiredService<Momoka.AniBt.AniBtClient>()", block);
        });

        Test("MP 日历接线：设置关闭当场退页并清理双向历史", () =>
        {
            var source = Read("ShellPage.MoviePilot.cs");
            Assert.Contains("CalendarChanged += OnMoviePilotCalendarChanged", source);
            Assert.Contains("CalendarChanged -= OnMoviePilotCalendarChanged", source);
            var start = source.IndexOf("private void OnMoviePilotCalendarChanged", StringComparison.Ordinal);
            var handler = source[start..source.IndexOf("private void OnMoviePilotAvailabilityChanged", start, StringComparison.Ordinal)];
            Assert.Contains("SyncSidebar();", handler);
            Assert.Contains("ContentFrame.Content is MoviePilotCalendarPage calendar", handler);
            Assert.Contains("if (CalendarAvailable) calendar.ViewModel.RefreshSources()", handler);
            Assert.Contains("GoTo(\"home\")", handler);
            Assert.Contains("PruneMoviePilotHistory();", handler);
            Assert.Contains("SyncChrome();", handler);
            Assert.Contains("type == typeof(MoviePilotCalendarPage)", source);
            var prune = source[source.IndexOf("private void PruneMoviePilotHistory()", StringComparison.Ordinal)..];
            Assert.Contains("IsUnavailableMoviePilotPage(ContentFrame.BackStack[index].SourcePageType)", prune);
            Assert.Contains("IsUnavailableMoviePilotPage(ContentFrame.ForwardStack[index].SourcePageType)", prune);
            Assert.Contains("_backTrails.RemoveAt(index)", prune);
            Assert.Contains("_forwardTrails.RemoveAt(index)", prune);
        });

        Test("MP 日历接线：启动后台刷新只在开关开启时跑一趟并写入缓存", () =>
        {
            var moviePilot = Read("ShellPage.MoviePilot.cs");
            var start = moviePilot.IndexOf("private async Task RefreshCalendarOnStartupAsync", StringComparison.Ordinal);
            Assert.True(start >= 0, "缺少启动后台刷新入口");
            var block = moviePilot[start..moviePilot.IndexOf("private void OnMoviePilotHistoryCapabilitiesChanged", start, StringComparison.Ordinal)];
            Assert.Contains("_moviePilotCalendarRefreshStarted", block);
            Assert.Contains("_moviePilotCalendarRefreshOnLaunch", block);
            Assert.Contains("CalendarEnabled", block);
            Assert.Contains("CalendarAsync(start, end, refresh.Token)", block);
            Assert.Contains("_moviePilotCalendarRefreshOnLaunch = Settings.MoviePilot.CalendarEnabled && Settings.MoviePilot.CalendarRefreshOnStartup", moviePilot);
            Assert.Contains("_ = RefreshCalendarOnStartupAsync(service);", moviePilot);
            Assert.Contains("_moviePilotCalendarRefresh?.Cancel();", moviePilot);
        });

        Test("MP 日历接线：Attach 与离页共用 Release，取消并退订重画", () =>
        {
            var page = Read("MoviePilotCalendarPage.xaml.cs");
            var attach = page[page.IndexOf("internal void Attach(", StringComparison.Ordinal)..];
            attach = attach[..attach.IndexOf("protected override void OnNavigatedFrom", StringComparison.Ordinal)];
            Assert.True(attach.IndexOf("Release();", StringComparison.Ordinal) < attach.IndexOf("_request = request", StringComparison.Ordinal));
            Assert.Contains("ViewModel.Changed += OnChanged", attach);
            Assert.Contains("ViewModel.Attach(request.Service, request.Settings, request.AniBt)", attach);
            var leave = page[page.IndexOf("protected override void OnNavigatedFrom", StringComparison.Ordinal)..];
            leave = leave[..leave.IndexOf("private void OnChanged", StringComparison.Ordinal)];
            Assert.Contains("Release();", leave);
            Assert.Contains("ViewModel.Changed -= OnChanged", leave);
            Assert.Contains("ViewModel.Release();", leave);
            Assert.Contains("ClearMonth();", leave);
            Assert.Contains("ReleaseImages();", leave);
            var clear = page[page.IndexOf("private void ClearMonth()", StringComparison.Ordinal)..];
            clear = clear[..clear.IndexOf("private void ReleaseImages()", StringComparison.Ordinal)];
            Assert.Contains("cell.Cards.Children.Concat(cell.ExtraCards.Children)", clear);
            Assert.Contains("card.Release();", clear);
            Assert.Contains("_days.Clear();", clear);
            Assert.Contains("MonthGrid.Children.Clear();", clear);
        });

        Test("日历月视图：月份操作、单选筛选与提示条接线，七列不横滚", () =>
        {
            var page = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarPage.xaml"));
            var filters = Named(page, "MediaType");
            Assert.Equal("SearchSourceTabs", filters.Name.LocalName);
            Assert.Equal("OnMediaTypeChanged", filters.Attribute("SelectionChanged")?.Value);
            var choices = filters.Elements().ToArray();
            Assert.Equal(3, choices.Length);
            Assert.True(choices.Select(element => element.Attribute("Content")?.Value).SequenceEqual(new[] { "全部", "电影", "电视剧" }));
            Assert.True(choices.All(element => element.Name.LocalName == "SegmentedItem"));
            var month = Named(page, "MonthTitle");
            var actions = Named(page, "MonthActions");
            Assert.Equal(month.Parent, actions.Parent);
            Assert.Equal("1", actions.Attribute("Grid.Column")?.Value);
            foreach (var (name, handler) in new[] { ("PreviousMonth", "OnPreviousMonth"), ("NextMonth", "OnNextMonth"), ("CurrentMonth", "OnToday"), ("RefreshCalendar", "OnRefresh"), ("RetryCalendar", "OnRetry") })
                Assert.Equal(handler, Named(page, name).Attribute("Click")?.Value);
            Assert.True(Named(page, "RetryCalendar").Ancestors().Any(element => element.Name.LocalName == "InfoBar.ActionButton"));
            var scroll = Named(page, "CalendarScroll");
            Assert.Equal("Disabled", scroll.Attribute("HorizontalScrollMode")?.Value);
            Assert.Equal("Disabled", scroll.Attribute("HorizontalScrollBarVisibility")?.Value);
            Assert.True(Named(page, "MonthGrid").Ancestors().Contains(scroll));
            Assert.False(page.Descendants().Any(element => element.Name.LocalName is "ComboBox" or "ComboBoxItem"));
            Assert.False(page.Descendants().Any(element => Name(element) is "SelectedDayTitle" or "DaySummary" or "DayEvents"));
        });

        Test("MP 日历日格：事件卡直接在日期内排列，超过五项只展开该天", () =>
        {
            var page = Read("MoviePilotCalendarPage.xaml.cs");
            var render = Between(page, "private void BuildMonth(", "private Border Cell(");
            Assert.DoesNotContain("MonthGrid.MinWidth", page);
            Assert.DoesNotContain("MonthGrid.Width", page);
            Assert.Contains("cell.Expanded = !cell.Expanded", render);
            Assert.Contains("SyncDay(day, cell, cell.Entries)", render);
            Assert.Contains("Grid.SetColumn(border, offset % 7)", render);
            Assert.Contains("Grid.SetRow(border, 1 + (offset / 7))", render);
            var sync = Between(page, "private void SyncDay(", "private void FocusCalendar(");
            Assert.Contains("MoviePilotCalendarLayout.VisibleCount(entries.Count, false)", sync);
            Assert.Contains("SyncCards(cell.Cards, entries, 0, preview)", sync);
            Assert.Contains("SyncCards(cell.ExtraCards, entries, preview, cell.Expanded ? entries.Count - preview : 0)", sync);
            Assert.Contains("existing.Update(entry, LoadImageAsync, token)", sync);
            Assert.DoesNotContain("ReloadAsync(", sync);
            Assert.DoesNotContain("SelectDate(", page);
        });

        Test("MP 日历事件卡：同格海报、标题、集数、入库和更新时间均有原生展示", () =>
        {
            var card = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarEventCard.xaml"));
            var names = card.Descendants().Select(Name).Where(name => name is not null).ToHashSet();
            foreach (var name in new[] { "PosterImage", "PosterPlaceholder", "EventTitle", "EpisodeText", "LibraryText", "UpdatedText", "RuntimeText" })
                Assert.True(names.Contains(name), "事件卡缺少 " + name);
            var poster = card.Descendants().Single(element => Name(element) == "PosterImage");
            Assert.Equal("UniformToFill", poster.Attribute("Stretch")?.Value);
            var posterFrame = poster.Ancestors().First(element => element.Name.LocalName == "Border");
            Assert.Equal("56", posterFrame.Attribute("Width")?.Value);
            Assert.Equal("84", posterFrame.Attribute("Height")?.Value);
            var title = card.Descendants().Single(element => Name(element) == "EventTitle");
            Assert.Equal("Wrap", title.Attribute("TextWrapping")?.Value);
            Assert.Equal("2", title.Attribute("MaxLines")?.Value);
            Assert.Equal("CharacterEllipsis", title.Attribute("TextTrimming")?.Value);

            var source = Read("MoviePilotCalendarEventCard.xaml.cs");
            Assert.Contains("MoviePilotCalendarPresentation.EpisodeLabel(entry, UiText.Translate)", source);
            Assert.Contains("MoviePilotCalendarPresentation.LibraryState(entry)", source);
            Assert.Contains("MoviePilotCalendarPresentation.LibraryStateText(entry)", source);
            Assert.Contains("MoviePilotCalendarPresentation.UpdatedText(entry, DateTimeOffset.Now, UiText.Translate)", source);
            Assert.Contains("SetDetail(UpdatedText, updated)", source);
            Assert.Contains("AutomationProperties.SetName(this, description)", source);
            Assert.Contains("ToolTipService.SetToolTip(this, description)", source);
            Assert.DoesNotContain("CompletedEpisodes", source);
            Assert.DoesNotContain("MissingEpisodes", source);
        });

        Test("日历事件卡：缩放只重排现有控件，不重新读取或销毁卡片", () =>
        {
            var source = Read("MoviePilotCalendarEventCard.xaml.cs");
            var resize = Between(source, "private void OnCardSizeChanged(", "internal bool Matches(");
            Assert.Contains("Math.Abs(width - _layoutWidth) < 0.5", resize);
            Assert.Contains("MoviePilotCalendarLayout.FitCard(width)", resize);
            Assert.Contains("PosterFrame.Height = layout.PosterWidth * 1.5", resize);
            Assert.Contains("Grid.SetRow(TextPanel, layout.Stacked && layout.PosterWidth > 0 ? 1 : 0)", resize);
            Assert.Contains("UpdateDetailVisibility()", resize);
            foreach (var forbidden in new[] { "LoadImageAsync", "Release(", "Attach(", "RenderMonth(", "DecodeAsync" })
                Assert.DoesNotContain(forbidden, resize);
            var xaml = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarEventCard.xaml"));
            foreach (var name in new[] { "EpisodeText", "LibraryText", "UpdatedText" })
                Assert.Equal("Wrap", Named(xaml, name).Attribute("TextWrapping")?.Value);
            Assert.False(xaml.Descendants().Any(element => element.Name.LocalName == "Viewbox"));
        });

        Test("MP 日历事件卡：离页取消旧海报，迟到读取不能覆盖重用的卡片", () =>
        {
            var source = Read("MoviePilotCalendarEventCard.xaml.cs");
            Assert.Contains("PosterFrame.EffectiveViewportChanged += OnPosterViewportChanged", source);
            var poster = Between(source, "private void StartVisiblePoster()", "private async Task LoadImageAsync(");
            Assert.Contains("if (!_inViewport || PosterFrame.Visibility != Visibility.Visible || _posterAttempted", poster);
            Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(_posterLifetime)", poster);
            Assert.Contains("!ReferenceEquals(_load, pending) || pending.IsCancellationRequested", source);
            Assert.Contains("_load?.Cancel();", source);
            Assert.Contains("_load?.Dispose();", source);
            Assert.Contains("PosterImage.Source = null;", source);
            Assert.Contains("PosterPlaceholder.Visibility = Visibility.Visible;", source);
        });

        Test("MP 日历海报：相同空列表短路前仍撤销旧图片归属并捕获新连接", () =>
        {
            var changed = Between(Read("MoviePilotCalendarPage.xaml.cs"), "private void OnChanged()", "private void SyncToolbar(");
            Assert.Contains("_renderedSourceRevision != ViewModel.SourceRevision", changed);
            var ownership = changed.IndexOf("if (ownershipChanged)", StringComparison.Ordinal);
            var clear = changed.IndexOf("ReleaseImages();", ownership, StringComparison.Ordinal);
            var lifetime = changed.IndexOf("_imagesLifetime = new CancellationTokenSource();", clear, StringComparison.Ordinal);
            var capture = changed.IndexOf("request.Service?.CaptureImageFetcher()", lifetime, StringComparison.Ordinal);
            var reuse = changed.IndexOf("ReferenceEquals(_renderedEntries, entries)", StringComparison.Ordinal);
            Assert.True(ownership >= 0 && clear > ownership && lifetime > clear && capture > lifetime && reuse > capture,
                "空数组可能复用同一实例，不能先短路再更新海报的连接归属");
            var release = Between(Read("MoviePilotCalendarPage.xaml.cs"), "private void ReleaseImages()", "private void BuildMonth(");
            Assert.Contains("_imagesLifetime?.Cancel();", release);
            Assert.Contains("_images.Clear();", release);
            Assert.Contains("_fetchImage = null;", release);
        });

        Test("AniBT 日历：来源控件、独立封面和播出标签接入，卡片收起订阅专有字段", () =>
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarPage.xaml"));
            var sources = Named(xaml, "CalendarSources");
            var choices = sources.Elements().ToArray();
            Assert.True(choices.Select(element => element.Attribute("Content")?.Value).SequenceEqual(new[] { "MoviePilot 订阅", "AniBT 番剧" }));
            Assert.Equal("OnSourceChanged", sources.Attribute("SelectionChanged")?.Value);
            var page = Read("MoviePilotCalendarPage.xaml.cs");
            Assert.Contains("AniBtCalendarSource.Visibility = ViewModel.AniBtAvailable", page);
            Assert.Contains("ViewModel.SelectSource((CalendarSource)CalendarSources.SelectedIndex)", page);
            Assert.Contains("request.AniBt.CalendarCoverAsync", page);
            var card = Between(Read("MoviePilotCalendarEventCard.xaml.cs"), "private void PresentAnime(", "private void PresentDetails(");
            Assert.Contains("PresentDetails(entry.AiringAt", card);
            Assert.Contains("entry.AiringAt is { } airing", card);
            Assert.Contains("entry.IsPremiere", card);
        });

        Test("MP 日历海报：只通过捕获连接读取 HTTP 图片，不让 Image 直接发请求", () =>
        {
            var page = Read("MoviePilotCalendarPage.xaml.cs");
            var load = Between(page, "private Task<BitmapImage?> LoadImageAsync(", "private static async Task<BitmapImage?> ReadImageAsync(");
            var absolute = load.IndexOf("!Uri.TryCreate(url, UriKind.Absolute, out var image)", StringComparison.Ordinal);
            var scheme = load.IndexOf("image.Scheme is not (\"http\" or \"https\")", StringComparison.Ordinal);
            var reject = load.IndexOf("return Task.FromResult<BitmapImage?>(null)", StringComparison.Ordinal);
            var fetch = load.IndexOf("ReadImageAsync(url, _fetchImage, _imagesLifetime.Token)", StringComparison.Ordinal);
            Assert.True(absolute >= 0 && scheme > absolute && reject > scheme && fetch > reject,
                "本地文件、自定义协议和相对路径必须在读取图片之前拒绝");
            var read = Between(page, "private static async Task<BitmapImage?> ReadImageAsync(", "private void OnToolbarSizeChanged(");
            Assert.Contains("await PosterSlots.WaitAsync(token)", read);
            Assert.Contains("await fetch(url, token)", read);
            Assert.Contains("PosterLoader.DecodeAsync(bytes, 56)", read);
            Assert.Contains("token.ThrowIfCancellationRequested();", read);
            Assert.Contains("CaptureImageFetcher()", page);
            Assert.DoesNotContain("UriSource", page);
            Assert.DoesNotContain("UriSource", Read("MoviePilotCalendarEventCard.xaml.cs"));
            var card = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarEventCard.xaml"));
            Assert.True(card.Descendants().Where(element => element.Name.LocalName == "Image").All(element => element.Attribute("Source") is null),
                "海报只能使用受连接所有权保护的读取结果");
        });
    }

    private static string? Name(XElement element) => element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value;

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element => Name(element) == name);

    private static string Between(string source, string begin, string end)
    {
        var start = source.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(start >= 0, "缺少生产方法 " + begin);
        var finish = source.IndexOf(end, start + begin.Length, StringComparison.Ordinal);
        Assert.True(finish > start, "缺少生产方法边界 " + end);
        return source[start..finish];
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/Views", file));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }
}

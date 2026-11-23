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
            Assert.Contains("foreach (var card in _cards) card.Release();", clear);
            Assert.Contains("_cards.Clear();", clear);
            Assert.Contains("MonthGrid.Children.Clear();", clear);
        });

        Test("日历月视图：顶部筛选、中央年月、右侧翻月，七列适应窗口且不横滚", () =>
        {
            var page = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarPage.xaml"));
            var filters = Named(page, "MediaType");
            Assert.Equal("Horizontal", filters.Attribute("Orientation")?.Value);
            var choices = filters.Elements().ToArray();
            Assert.Equal(3, choices.Length);
            Assert.True(choices.Select(element => element.Attribute("Content")?.Value).SequenceEqual(new[] { "全部", "电影", "电视剧" }));
            Assert.True(choices.Select(element => element.Attribute("Tag")?.Value).SequenceEqual(new[] { "0", "1", "2" }));
            Assert.True(choices.All(element => element.Attribute("Click")?.Value == "OnMediaTypeChanged"));
            var month = Named(page, "MonthTitle");
            Assert.Equal("1", month.Attribute("Grid.Column")?.Value);
            Assert.Equal("Center", month.Attribute("HorizontalAlignment")?.Value);
            var actions = Named(page, "MonthActions");
            Assert.Equal("2", actions.Attribute("Grid.Column")?.Value);
            Assert.Equal("Right", actions.Attribute("HorizontalAlignment")?.Value);
            foreach (var (name, handler) in new[] { ("PreviousMonth", "OnPreviousMonth"), ("NextMonth", "OnNextMonth"), ("CurrentMonth", "OnToday"), ("RefreshCalendar", "OnRefresh") })
                Assert.Equal(handler, Named(page, name).Attribute("Click")?.Value);
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
            var render = Between(page, "private void RenderMonth(", "private void ExpandDay(");
            Assert.DoesNotContain("MonthGrid.MinWidth", render);
            Assert.DoesNotContain("MonthGrid.Width", page);
            Assert.Contains("MoviePilotCalendarLayout.VisibleCount(events.Length, _expandedDays.Contains(day))", render);
            Assert.Contains("for (var index = 0; index < visible; index++) panel.Children.Add(EventCard(events[index]))", render);
            Assert.Contains("if (events.Length > visible)", render);
            Assert.Contains("more.Click += (_, _) => ExpandDay(day, panel, more, events, visible)", render);
            Assert.Contains("cell.MinHeight = 180", render);
            Assert.Contains("Grid.SetColumn(cell, offset % 7)", render);
            Assert.Contains("Grid.SetRow(cell, 1 + (offset / 7))", render);
            var expand = Between(page, "private void ExpandDay(", "private MoviePilotCalendarEventCard EventCard(");
            Assert.Contains("if (!_expandedDays.Add(day)) return", expand);
            Assert.Contains("panel.Children.Remove(more)", expand);
            Assert.Contains("for (var index = visible; index < entries.Count; index++) panel.Children.Add(EventCard(entries[index]))", expand);
            Assert.DoesNotContain("RenderMonth(", expand);
            Assert.DoesNotContain("ViewModel.", expand);
            Assert.DoesNotContain("GoTo(", expand);
            Assert.DoesNotContain("ReloadAsync(", expand);
            Assert.DoesNotContain("SelectDate(", page);
            var changed = Between(page, "private void OnChanged()", "private void ClearMonth()");
            Assert.Contains("_renderedMonth != ViewModel.Month || _renderedType != ViewModel.MediaType || _renderedSource != ViewModel.Source) _expandedDays.Clear()", changed);
        });

        Test("MP 日历事件卡：同格海报、标题、集数、入库和更新时间均有原生展示", () =>
        {
            var card = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarEventCard.xaml"));
            var names = card.Descendants().Select(Name).Where(name => name is not null).ToHashSet();
            foreach (var name in new[] { "PosterImage", "PosterPlaceholder", "EventTitle", "EpisodeText", "LibraryRow", "LibraryText", "UpdatedRow", "UpdatedText" })
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
            Assert.Contains("MoviePilotCalendarPresentation.EpisodeLabel(entry)", source);
            Assert.Contains("MoviePilotCalendarPresentation.LibraryState(entry)", source);
            Assert.Contains("MoviePilotCalendarPresentation.LibraryStateText(entry)", source);
            Assert.Contains("MoviePilotCalendarPresentation.UpdatedText(entry, DateTimeOffset.Now)", source);
            Assert.Contains("UpdatedText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed", source);
            Assert.Contains("AutomationProperties.SetName(this, description)", source);
            Assert.Contains("ToolTipService.SetToolTip(this, description)", source);
            Assert.DoesNotContain("CompletedEpisodes", source);
            Assert.DoesNotContain("MissingEpisodes", source);
        });

        Test("日历事件卡：缩放只重排现有控件，不重新读取或销毁卡片", () =>
        {
            var source = Read("MoviePilotCalendarEventCard.xaml.cs");
            var resize = Between(source, "private void OnCardSizeChanged(", "internal void Attach(");
            Assert.Contains("Math.Abs(width - _layoutWidth) < 0.5", resize);
            Assert.Contains("MoviePilotCalendarLayout.FitCard(width)", resize);
            Assert.Contains("PosterFrame.Height = layout.PosterWidth * 1.5", resize);
            Assert.Contains("Grid.SetRow(TextPanel, layout.Stacked ? 1 : 0)", resize);
            foreach (var forbidden in new[] { "LoadImageAsync", "Release(", "Attach(", "RenderMonth(", "DecodeAsync" })
                Assert.DoesNotContain(forbidden, resize);
            var xaml = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarEventCard.xaml"));
            foreach (var name in new[] { "EpisodeText", "LibraryText", "CompleteText", "PartialText", "UpdatedText" })
                Assert.Equal("Wrap", Named(xaml, name).Attribute("TextWrapping")?.Value);
            Assert.False(xaml.Descendants().Any(element => element.Name.LocalName == "Viewbox"));
        });

        Test("MP 日历事件卡：离页取消旧海报，迟到读取不能覆盖重用的卡片", () =>
        {
            var source = Read("MoviePilotCalendarEventCard.xaml.cs");
            var attach = source[source.IndexOf("internal void Attach(", StringComparison.Ordinal)..];
            attach = attach[..attach.IndexOf("private async Task LoadImageAsync", StringComparison.Ordinal)];
            Assert.True(attach.IndexOf("Release();", StringComparison.Ordinal) < attach.IndexOf("EventTitle.Text = entry.Title", StringComparison.Ordinal));
            Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource(token)", attach);
            Assert.Contains("!ReferenceEquals(_load, pending) || pending.IsCancellationRequested", source);
            Assert.Contains("_load?.Cancel();", source);
            Assert.Contains("_load?.Dispose();", source);
            Assert.Contains("PosterImage.Source = null;", source);
            Assert.Contains("PosterPlaceholder.Visibility = Visibility.Visible;", source);
        });

        Test("MP 日历海报：相同空列表短路前仍撤销旧图片归属并捕获新连接", () =>
        {
            var changed = Between(Read("MoviePilotCalendarPage.xaml.cs"), "private void OnChanged()", "private void ClearMonth()");
            var ownership = changed.IndexOf("if (ViewModel.Busy || _imagesLifetime is null)", StringComparison.Ordinal);
            var clear = changed.IndexOf("ReleaseImages();", ownership, StringComparison.Ordinal);
            var lifetime = changed.IndexOf("_imagesLifetime = new CancellationTokenSource();", clear, StringComparison.Ordinal);
            var capture = changed.IndexOf("_fetchImage = isAniBt ? request.AniBt.CalendarCoverAsync : request.Service?.CaptureImageFetcher();", lifetime, StringComparison.Ordinal);
            var reuse = changed.IndexOf("if (ReferenceEquals(_renderedEntries, entries) && _renderedMonth == ViewModel.Month && _renderedSource == ViewModel.Source) return;", StringComparison.Ordinal);
            Assert.True(ownership >= 0 && clear > ownership && lifetime > clear && capture > lifetime && reuse > capture,
                "空数组可能复用同一实例，不能先短路再更新海报的连接归属");
            var release = Between(Read("MoviePilotCalendarPage.xaml.cs"), "private void ReleaseImages()", "private void RenderMonth(");
            Assert.Contains("_imagesLifetime?.Cancel();", release);
            Assert.Contains("_images.Clear();", release);
            Assert.Contains("_fetchImage = null;", release);
        });

        Test("AniBT 日历：来源控件、独立封面和播出标签接入，卡片收起订阅专有字段", () =>
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "src/Momoka.Shell/Views/MoviePilotCalendarPage.xaml"));
            var choices = Named(xaml, "CalendarSources").Elements().ToArray();
            Assert.True(choices.Select(element => element.Attribute("Content")?.Value).SequenceEqual(new[] { "MoviePilot 订阅", "AniBT 番剧" }));
            Assert.True(choices.Select(element => element.Attribute("Click")?.Value).SequenceEqual(new[] { "OnMoviePilotSource", "OnAniBtSource" }));
            var page = Read("MoviePilotCalendarPage.xaml.cs");
            Assert.Contains("AniBtCalendarSource.Visibility = ViewModel.AniBtAvailable", page);
            Assert.Contains("ViewModel.SelectSource(CalendarSource.AniBt)", page);
            var card = Between(Read("MoviePilotCalendarEventCard.xaml.cs"), "internal void Attach(AniBtCalendarEntry", "private void SetDescription(");
            Assert.Contains("LibraryRow.Visibility = LibraryBadge.Visibility = UpdatedRow.Visibility = RuntimeText.Visibility = Visibility.Collapsed", card);
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
            var read = Between(page, "private static async Task<BitmapImage?> ReadImageAsync(", "private Border Cell(");
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

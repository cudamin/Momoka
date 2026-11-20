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
            Assert.Equal("moviepilot:calendar", item.Attribute("Tag")?.Value);
            Assert.Equal("Collapsed", item.Attribute("Visibility")?.Value);
            var page = Read("MoviePilotCalendarPage.xaml.cs");
            Assert.Contains("Destination = \"moviepilot:calendar\"", page);
            Assert.Contains("Tag = Destination;", page);
            var sidebar = Read("ShellPage.Sidebar.cs");
            Assert.Contains("SidebarMoviePilotCalendar.Visibility = MoviePilotCalendarAvailable ? Visibility.Visible : Visibility.Collapsed", sidebar);
            Assert.Contains("MoviePilotCalendarPage.Destination when MoviePilotCalendarAvailable => SidebarMoviePilotCalendar", sidebar);
        });

        Test("MP 日历接线：直接导航再次核对可用性且不依赖媒体服务器登录", () =>
        {
            var shell = Read("ShellPage.xaml.cs");
            var start = shell.IndexOf("if (tag == MoviePilotCalendarPage.Destination)", StringComparison.Ordinal);
            var end = shell.IndexOf("return;", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            Assert.Contains("if (!MoviePilotCalendarAvailable) return;", shell[start..(end + 7)]);
            var block = shell[start..shell.IndexOf("// MoviePilot 的两条订阅入口", start, StringComparison.Ordinal)];
            Assert.Contains("Open(typeof(MoviePilotCalendarPage)", block);
            Assert.DoesNotContain("_session", block);
            var moviePilot = Read("ShellPage.MoviePilot.cs");
            var availability = moviePilot[moviePilot.IndexOf("private bool MoviePilotCalendarAvailable", StringComparison.Ordinal)..];
            availability = availability[..availability.IndexOf(';')];
            Assert.Contains("Available == true", availability);
            Assert.Contains("CalendarEnabled", availability);
        });

        Test("MP 日历接线：设置关闭当场退页并清理双向历史", () =>
        {
            var source = Read("ShellPage.MoviePilot.cs");
            Assert.Contains("CalendarChanged += OnMoviePilotCalendarChanged", source);
            Assert.Contains("CalendarChanged -= OnMoviePilotCalendarChanged", source);
            var start = source.IndexOf("private void OnMoviePilotCalendarChanged", StringComparison.Ordinal);
            var handler = source[start..source.IndexOf("private void OnMoviePilotAvailabilityChanged", start, StringComparison.Ordinal)];
            Assert.Contains("SyncSidebar();", handler);
            Assert.Contains("!MoviePilotCalendarAvailable && ContentFrame.Content is MoviePilotCalendarPage", handler);
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
            Assert.Contains("ViewModel.Attach(request.Service, request.Settings)", attach);
            var leave = page[page.IndexOf("protected override void OnNavigatedFrom", StringComparison.Ordinal)..];
            leave = leave[..leave.IndexOf("private void OnChanged", StringComparison.Ordinal)];
            Assert.Contains("Release();", leave);
            Assert.Contains("ViewModel.Changed -= OnChanged", leave);
            Assert.Contains("ViewModel.Release();", leave);
            Assert.Contains("DayEvents.Children.Clear();", leave);
            Assert.Contains("MonthGrid.Children.Clear();", leave);
        });
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Root(), "src/Momoka.Shell/Views", file));

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }
}

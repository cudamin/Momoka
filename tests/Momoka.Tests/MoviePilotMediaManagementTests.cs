using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MoviePilotMediaManagementTests
{
    public static void Register()
    {
        Test("MP 媒体管理菜单：重新整理分组在前、媒体管理紧随、四个删除范围且不套品牌", () =>
        {
            foreach (var backend in new[] { "Emby", "Jellyfin", "FnNas", "Plex" })
                foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode })
                {
                    var menu = ItemMenu.For(new EmbyItem { Id = "item", Type = type }, true,
                        jellyfin: backend == "Jellyfin", fnNas: backend == "FnNas", plex: backend == "Plex",
                        moviePilotManagementAvailable: true, moviePilotSmartAvailable: true).ToList();
                    var versionsIndex = menu.FindIndex(row => row.Command == ItemCommand.MoviePilotVersions);
                    var groupIndex = menu.FindIndex(row => row.Command == ItemCommand.MoviePilotReorganizeMenu);
                    var group = menu[groupIndex];
                    var restricted = backend is "FnNas" or "Plex";
                    Assert.Equal(versionsIndex + 1, groupIndex);
                    Assert.Equal(restricted ? -1 : 0, group.Children!.ToList().FindIndex(row => row.Command == ItemCommand.MoviePilotReorganize));
                    var managementIndex = menu.FindIndex(row => row.Command == ItemCommand.MoviePilotMediaManagement);
                    Assert.Equal(groupIndex + 1, managementIndex);
                    var management = menu[managementIndex];
                    Assert.Equal("媒体管理", management.Label);
                    Assert.Equal("仅删除记录|删记录和源文件|删记录和库文件|删除记录及两端",
                        string.Join("|", management.Children!.Select(row => row.Label)));
                    Assert.Equal(type is EmbyItemType.Series or EmbyItemType.Season ? "智能批量整理" : "智能整理",
                        group.Children!.Single(row => row.Command == ItemCommand.MoviePilotSmartReorganize).Label);
                }
        });
        Test("MP 主树整合：跨服务器管理不恢复已退役手动入口，短菜单与非动漫识别保留", () =>
        {
            foreach (var backend in new[] { "Emby", "Jellyfin", "FnNas", "Plex" })
            {
                var fnNas = backend == "FnNas";
                var plex = backend == "Plex";
                var jellyfin = backend == "Jellyfin";
                var menu = ItemMenu.For(new EmbyItem { Type = EmbyItemType.Series, Genres = ["剧情"] }, true,
                    jellyfin: jellyfin, fnNas: fnNas, plex: plex,
                    moviePilotManagementAvailable: true, moviePilotSmartAvailable: true);
                var reorganize = menu.Single(row => row.Command == ItemCommand.MoviePilotReorganizeMenu).Children!;
                Assert.Equal(!fnNas && !plex, reorganize.Any(row => row.Command == ItemCommand.MoviePilotReorganize));
                Assert.Equal(!fnNas && !plex, ItemMenu.Supports(ItemCommand.MoviePilotReorganize, jellyfin, fnNas, plex));
                Assert.Equal(!fnNas && !plex, reorganize.Any(row => row.Command == ItemCommand.AniBtScrape));
                Assert.Equal(1, menu.Count(row => row.Command == ItemCommand.MoviePilotMediaManagement));
                foreach (var row in menu.Where(row => row.Command is { } command && ItemMenu.IsMoviePilot(command)))
                {
                    Assert.True(row.Label.Count(ch => ch is >= '\u4e00' and <= '\u9fff') <= 7, row.Label);
                    Assert.True(ItemMenu.Supports(row.Command!.Value, jellyfin, fnNas, plex));
                    if (row.Children is { } children)
                        foreach (var child in children)
                        {
                            Assert.True(child.Label.Count(ch => ch is >= '\u4e00' and <= '\u9fff') <= 7, child.Label);
                            Assert.True(ItemMenu.Supports(child.Command!.Value, jellyfin, fnNas, plex));
                        }
                }
            }
        });
        Test("MP 主树运行覆盖：原生菜单和确认窗逐主题截图，保护零写及状态恢复", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
            Assert.True(root is not null);
            var source = File.ReadAllText(Path.Combine(root!.FullName, "src/Momoka.Shell/ShellNavigationProbe.MediaManagement.cs"));
            var runtime = source[..source.IndexOf("private async Task InspectMediaManagementAsync", StringComparison.Ordinal)];
            Assert.Equal(2, runtime.Split("foreach (var theme in Momoka.Theming.UiThemes.All)").Length - 1);
            Assert.Contains("mp-media-menu-{theme.Id}-{width}.png", runtime);
            Assert.Contains("mp-media-reorganize-submenu-{theme.Id}-{width}.png", runtime);
            Assert.Contains("mp-media-submenu-{theme.Id}-{width}.png", runtime);
            Assert.Contains("AssertNoBrand", runtime);
            Assert.Contains("仍套用 MP 品牌模板", runtime);
            Assert.Contains("mp-media-delete-confirm-{theme.Id}.png", runtime);
            Assert.Contains("ThemeHost.Apply(originalTheme);", runtime);
            Assert.Contains("_window!.TopMost = topMost;", runtime);
            Assert.Contains("transport.Writes == 0", runtime);
            Assert.Contains("dialog.DefaultButton == ContentDialogButton.Close", runtime);
            Assert.Contains("VerticalAlignment = VerticalAlignment.Top", runtime);
            var sidebar = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/ShellNavigationProbe.Sidebar.cs"));
            var titleCheck = sidebar.IndexOf("Require(section.ActualHeight > 0", StringComparison.Ordinal);
            var measured = sidebar.LastIndexOf("await LayoutAsync(section);", titleCheck, StringComparison.Ordinal);
            Assert.True(measured >= 0 && measured < titleCheck);
            Assert.Contains("VisibleArea(icon, host) >= bounds.Width * bounds.Height - 0.5", sidebar);
        });
        Test("MP 发布件悬停探针：真实焦点、初态与动画进度均确认，原缩放边界不放宽", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
            Assert.True(root is not null);
            var source = File.ReadAllText(Path.Combine(root!.FullName, "src/Momoka.Shell/ShellNavigationProbe.Subscriptions.cs"));
            Assert.Contains("Require(scroll.Focus(FocusState.Programmatic)", source);
            Assert.Contains("FocusManager.GetFocusedElement(scroll.XamlRoot), scroll)", source);
            var reset = source.IndexOf("Call(poster, \"ResetMotion\");", StringComparison.Ordinal);
            var entered = source.IndexOf("poster.SetHovered(true);", reset, StringComparison.Ordinal);
            var progressed = source.IndexOf("await UntilAsync(() => poster.Zoom > 1);", entered, StringComparison.Ordinal);
            var guarded = source.IndexOf("poster.Zoom > 1 && poster.Zoom <= 1.045", progressed, StringComparison.Ordinal);
            Assert.True(reset >= 0 && entered > reset && progressed > entered && guarded > progressed);
            Assert.Contains("Math.Abs(poster.Zoom - 1) < 0.001 && more.Visibility == Visibility.Collapsed", source);
            Assert.False(source.Contains("poster.SetHovered(true); await Task.Delay(80);", StringComparison.Ordinal));
        });
        Test("MP 媒体管理菜单：库卡和合集不加，缺权限或关闭助手时完整隐藏", () =>
        {
            foreach (var type in new[] { EmbyItemType.CollectionFolder, EmbyItemType.BoxSet, EmbyItemType.Person })
                Assert.False(ItemMenu.For(new EmbyItem { Type = type }, true, moviePilotManagementAvailable: true,
                    moviePilotSmartAvailable: true).Any(row => row.Command is { } cmd && ItemMenu.IsMoviePilot(cmd)));
            var item = new EmbyItem { Type = EmbyItemType.Movie };
            Assert.False(ItemMenu.For(item, false, moviePilotManagementAvailable: true, moviePilotSmartAvailable: true)
                .Any(row => row.Command is { } cmd && ItemMenu.IsMoviePilot(cmd)));
            Assert.False(ItemMenu.For(item, true).Any(row => row.Command == ItemCommand.MoviePilotMediaManagement));
            var noAi = ItemMenu.For(item, true, moviePilotManagementAvailable: true);
            Assert.True(noAi.Any(row => row.Command == ItemCommand.MoviePilotMediaManagement));
            Assert.False(noAi.SelectMany(row => row.Children ?? (IReadOnlyList<ItemMenuEntry>)[])
                .Any(row => row.Command == ItemCommand.MoviePilotSmartReorganize));
        });
        Test("MP 菜单品牌：条目更多菜单不再套 MP 图标，订阅菜单保留", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md"))) root = root.Parent;
            Assert.True(root is not null, "找不到当前源码树");
            var itemMenu = File.ReadAllText(Path.Combine(root!.FullName, "src", "Momoka.Shell", "Views", "ItemCommands.cs"));
            Assert.False(itemMenu.Contains("MoviePilotMenuBrand", StringComparison.Ordinal), "条目更多菜单仍在套 MP 图标");
            foreach (var page in new[] { "MoviePilotSubscriptionMenu.cs", "MoviePilotSubscriptionPage.xaml.cs" })
                Assert.Contains("MoviePilotMenuBrand.Apply(item)",
                    File.ReadAllText(Path.Combine(root.FullName, "src", "Momoka.Shell", "Views", page)));
        });
        Test("MP 菜单品牌：与设置页共享白色三路径，未残留紫色 SVG", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md"))) root = root.Parent;
            Assert.True(root is not null, "找不到当前源码树");
            var templates = XDocument.Load(Path.Combine(root!.FullName, "src", "Momoka.Shell", "Theme", "MoviePilotMenus.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var mark = templates.Descendants().Single(element => element.Name.LocalName == "DataTemplate" &&
                (string?)element.Attribute(x + "Key") == "EgMoviePilotMarkTemplate");
            var paths = mark.Descendants().Where(element => element.Name.LocalName == "Path").ToArray();
            Assert.Equal(3, paths.Length);
            Assert.True(paths.All(element => (string?)element.Attribute("Fill") == "{ThemeResource TextFillColorPrimaryBrush}"));
            Assert.Contains("M48,191 L32,181", (string)paths[0].Attribute("Data")!);
            Assert.Contains("M96,221 L96,200", (string)paths[1].Attribute("Data")!);
            Assert.Contains("M107,165 L102,162", (string)paths[2].Attribute("Data")!);
            var settings = XDocument.Load(Path.Combine(root.FullName, "src", "Momoka.Shell", "Views", "SettingsPage.xaml"));
            Assert.True(settings.Descendants().Any(element => (string?)element.Attribute("ContentTemplate") == "{StaticResource EgMoviePilotMarkTemplate}"));
            Assert.Equal(1, templates.Descendants().Count(element => (string?)element.Attribute(x + "Name") == "MoviePilotBrandMark"));
            Assert.Equal(1, templates.Descendants().Count(element => (string?)element.Attribute("ContentTemplate") == "{StaticResource EgMoviePilotMarkTemplate}"));
            Assert.False(templates.Descendants().Any(element => (string?)element.Attribute(x + "Key") == "EgMoviePilotMenuSubItemStyle"));
            Assert.False(templates.Descendants().Any(element => element.Name.LocalName == "SvgImageSource"));
        });
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
            MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
        {
            Test("MP 媒体管理 DELETE：" + action + " 只在消费确认后发正确标志和 ID", () =>
            {
                using var f = new HistoryFixture();
                var plan = f.Prepare(action);
                Assert.Equal(0, f.Transport.Writes.Count);
                Assert.Contains("#1", plan.Details);
                Assert.Contains("/downloads/one.mkv", plan.Details);
                Assert.Contains("/library/one.mkv", plan.Details);
                var result = f.Submit(plan);
                Assert.Equal(1, f.Transport.Writes.Count);
                var write = f.Transport.Writes[0];
                Assert.Equal(HttpMethod.Delete, write.Method);
                Assert.Equal($"?deletesrc={(plan.DeleteSource ? "true" : "false")}&deletedest={(plan.DeleteDestination ? "true" : "false")}", write.Query);
                Assert.Equal("{\"id\":1}", write.Body);
                Assert.False(result.NeedsReview);
                Assert.True(result.Receipts.Single().Removed);
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(plan));
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Prepare(MoviePilotHistoryAction.DeleteAll));
                Assert.Equal(1, f.Transport.Writes.Count);
            });
        }
        Test("MP 智能整理：电影单记录 POST 无聊天正文，回执只说已提交", () =>
        {
            using var f = new HistoryFixture();
            var plan = f.Prepare(MoviePilotHistoryAction.SmartReorganize);
            Assert.Equal(0, f.Transport.Writes.Count);
            var result = f.Submit(plan);
            Assert.Equal(HttpMethod.Post, f.Transport.Writes.Single().Method);
            Assert.Equal("/api/v1/history/transfer/1/ai-redo", f.Transport.Writes.Single().Path);
            Assert.Equal("", f.Transport.Writes.Single().Body);
            Assert.Equal("fixture-progress", result.ProgressKey);
            Assert.Contains("不代表文件已整理完成", result.Summary);
        });
        Test("MP 智能批量整理：电视剧一条也用批量路由，整剧多集收齐才发送 ID 数组", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 3;
            var plan = f.Prepare(MoviePilotHistoryAction.SmartReorganize, batch: true);
            Assert.Equal(3, plan.Histories.Count);
            var result = f.Submit(plan);
            Assert.Equal("/api/v1/history/transfer/ai-redo", f.Transport.Writes.Single().Path);
            Assert.Equal("{\"history_ids\":[1,2,3]}", f.Transport.Writes.Single().Body);
            Assert.Equal(3, result.AcceptedIds.Count);
        });
        foreach (var fault in new[] { "permission", "ai-disabled", "changed", "missing", "connection", "dependency", "selected-dependency" })
        {
            Test("MP 智能整理提交前拒绝：" + fault + " 不产生写请求", () =>
            {
                using var f = new HistoryFixture();
                if (fault == "selected-dependency") f.Transport.Count = 2;
                var plan = f.Prepare(MoviePilotHistoryAction.SmartReorganize);
                f.Transport.Fault = fault;
                if (fault == "connection") f.Settings.MoviePilot.Username = "replacement";
                Assert.Throws<MoviePilotException>(() => f.Submit(plan));
                Assert.Equal(0, f.Transport.Writes.Count);
            });
        }
        Test("MP 媒体管理准备：路径歧义和媒体编号冲突不猜测记录", () =>
        {
            foreach (var fault in new[] { "ambiguous", "changed" })
            {
                using var f = new HistoryFixture();
                f.Transport.Fault = fault;
                Assert.Throws<MoviePilotException>(() => f.Prepare(MoviePilotHistoryAction.DeleteAll));
                Assert.Equal(0, f.Transport.Writes.Count);
            }
        });
        Test("MP 文件删除范围：目录和非本地存储不能伪装媒体文件", () =>
        {
            foreach (var fault in new[] { "source-dir", "target-dir", "remote-storage" })
            {
                using var f = new HistoryFixture();
                f.Transport.Fault = fault;
                Assert.Throws<MoviePilotException>(() => f.Prepare(MoviePilotHistoryAction.DeleteAll));
                Assert.Equal(0, f.Transport.Writes.Count);
                Assert.Equal(1, f.Prepare(MoviePilotHistoryAction.DeleteRecord).Histories.Count);
            }
        });
        Test("MP 媒体管理匹配：片名不同只补查精确文件，不扩大到目录", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Fault = "renamed";
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteRecord);
            Assert.Equal(1, plan.Histories.Count);
            Assert.True(f.Transport.HistoryQueries.Contains("/library/one.mkv"));
            Assert.False(f.Transport.HistoryQueries.Contains("/library"));
            Assert.Equal(0, f.Transport.Writes.Count);
        });
        Test("MP 删除部分成功：逐步骤保留，后续未发，换删除档不能重放", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 3;
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteAll, batch: true);
            f.Transport.Fault = "partial";
            var result = f.Submit(plan);
            Assert.True(result.NeedsReview);
            Assert.Equal(1, result.Receipts.Count);
            Assert.Equal("deleted", result.Receipts[0].Source);
            Assert.Equal("failed", result.Receipts[0].Destination);
            Assert.Equal(1, f.Transport.Writes.Count);
            f.Transport.Fault = "";
            Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(f.Prepare(MoviePilotHistoryAction.DeleteAll, true)));
            Assert.Equal(1, f.Transport.Writes.Count);
        });
        foreach (var fault in new[] { "lost", "write-json", "no-receipt", "401-second" })
        {
            Test("MP 批量删除回执不明：" + fault + " 不重放已发请求", () =>
            {
                using var f = new HistoryFixture();
                f.Transport.Count = 3;
                var plan = f.Prepare(MoviePilotHistoryAction.DeleteAll, true);
                f.Transport.Fault = fault;
                var result = f.Submit(plan);
                Assert.True(result.NeedsReview);
                Assert.Equal(fault == "401-second" ? 2 : 1, f.Transport.Writes.Count);
                f.Transport.Fault = "";
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(f.Prepare(MoviePilotHistoryAction.DeleteAll, true)));
                Assert.Equal(fault == "401-second" ? 2 : 1, f.Transport.Writes.Count);
            });
        }
        foreach (var fault in new[] { "no-progress", "wrong-ids", "lost", "write-json" })
        {
            Test("MP 智能批量回执不明：" + fault + " 保留防重账本", () =>
            {
                using var f = new HistoryFixture();
                f.Transport.Count = 2;
                var plan = f.Prepare(MoviePilotHistoryAction.SmartReorganize, true);
                f.Transport.Fault = fault;
                Assert.Throws<MoviePilotOperationUncertainException>(() => f.Submit(plan));
                Assert.Equal(1, f.Transport.Writes.Count);
                f.Transport.Fault = "";
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(f.Prepare(MoviePilotHistoryAction.SmartReorganize, true)));
                Assert.Equal(1, f.Transport.Writes.Count);
            });
        }
        Test("MP 删除已无历史：未执行文件删除不能报告范围完成", () =>
        {
            using var f = new HistoryFixture();
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteAll);
            f.Transport.Fault = "record-gone";
            var result = f.Submit(plan);
            Assert.True(result.NeedsReview);
            Assert.True(result.Receipts.Single().Removed);
            Assert.False(result.Receipts.Single().Complete);
        });
        Test("MP 媒体管理能力：助手关闭、权限丢失和恢复当场更新，不清偏好", () =>
        {
            using var f = new HistoryFixture();
            var changes = 0;
            f.Service.HistoryCapabilitiesChanged += (_, _) => changes++;
            f.Service.HistoryCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(f.Service.CanManageHistory && f.Service.CanSmartReorganize);
            f.Transport.Fault = "ai-disabled";
            f.Service.HistoryCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(f.Service.CanManageHistory);
            Assert.False(f.Service.CanSmartReorganize);
            f.Transport.Fault = "permission";
            f.Service.HistoryCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(f.Service.CanManageHistory);
            f.Transport.Fault = "";
            f.Service.HistoryCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(f.Service.CanSmartReorganize);
            Assert.Equal(4, changes);
            Assert.True(f.Settings.MoviePilot.Enabled);
        });
        Test("MP 历史操作取消：确认前取消没有写请求", () =>
        {
            using var f = new HistoryFixture();
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteAll);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => f.Service.SubmitHistoryActionAsync(plan, cancel.Token).GetAwaiter().GetResult());
            Assert.Equal(0, f.Transport.Writes.Count);
        });
    }
}

internal sealed class HistoryFixture : IDisposable
{
    public AppSettings Settings { get; } = new();
    public HistoryTransport Transport { get; } = new();
    private readonly MoviePilotClient _client;
    public MoviePilotService Service { get; }
    public HistoryFixture()
    {
        Settings.MoviePilot.Enabled = true;
        Settings.MoviePilot.Url = "http://192.0.2.10:3001";
        Settings.MoviePilot.Username = "anonymous-fixture";
        Settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
        _client = new MoviePilotClient(Transport);
        Service = new MoviePilotService(_client, new MoviePilotCredentials(new RejectHistorySecret()), Settings);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(Service, null)!;
        var sessionType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
        var session = Activator.CreateInstance(sessionType, flags | BindingFlags.Public, null,
            [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
        typeof(MoviePilotService).GetField("_session", flags)!.SetValue(Service, session);
        typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(Service, identity);
    }
    public MoviePilotHistoryPlan Prepare(MoviePilotHistoryAction action, bool batch = false) =>
        Service.PrepareHistoryActionAsync(new("Fixture", "电影", "1", null, "",
            Enumerable.Range(1, Transport.Count).Select(id => new MoviePilotTransferFile("fixture", $"/library/{Name(id)}.mkv")).ToArray()),
            action, batch, Service.ConnectionStamp, CancellationToken.None).GetAwaiter().GetResult();
    public MoviePilotHistoryResult Submit(MoviePilotHistoryPlan plan) => Service.SubmitHistoryActionAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
    public static string Name(int id) => id == 1 ? "one" : id == 2 ? "two" : "three";
    public void Dispose() { Service.Dispose(); _client.Dispose(); }
    private sealed class RejectHistorySecret : ISecretProtector
    {
        public string Protect(string value) => throw new AssertionException("不访问真实秘密");
        public string Unprotect(string value) => throw new AssertionException("匿名预置会话不得登录或读取秘密");
    }
}

internal sealed class HistoryTransport : HttpMessageHandler
{
    public string Fault { get; set; } = "";
    public int Count { get; set; } = 1;
    public List<(HttpMethod Method, string Path, string Query, string Body)> Writes { get; } = [];
    public List<string> HistoryQueries { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var url = request.RequestUri!;
        Assert.Equal("192.0.2.10", url.Host);
        if (url.AbsolutePath.EndsWith("user/current", StringComparison.Ordinal))
            return Json(new { is_active = true, is_superuser = Fault != "permission", permissions = new { manage = false } });
        if (url.AbsolutePath.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { AI_AGENT_ENABLE = Fault != "ai-disabled" });
        if (url.AbsolutePath == "/api/v1/history/transfer" && request.Method == HttpMethod.Get)
        {
            var query = url.Query.TrimStart('?').Split('&').Select(value => value.Split('=', 2))
                .Single(value => value[0] == "title")[1];
            HistoryQueries.Add(Uri.UnescapeDataString(query));
            var dependency = Uri.UnescapeDataString(url.Query).Contains("title=/", StringComparison.Ordinal);
            var records = Fault == "missing" || Fault == "renamed" && Uri.UnescapeDataString(query) == "Fixture" ? [] : Enumerable.Range(1, Count).Select(id => History(id)).ToList();
            if (Fault == "ambiguous") records.Add(History(99, dest: "/library/one.mkv"));
            if (Fault == "dependency" && dependency) records.Add(History(99, source: "/library/one.mkv"));
            return Json(new { list = records, total = records.Count });
        }
        if (url.AbsolutePath.StartsWith("/api/v1/history/transfer", StringComparison.Ordinal) && request.Method != HttpMethod.Get)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            Writes.Add((request.Method, url.AbsolutePath, url.Query, body));
            if (Fault == "lost") throw new HttpRequestException("匿名回执丢失");
            if (Fault == "write-json") return Reply("{");
            if (Fault == "no-receipt") return Reply("{\"success\":true,\"data\":{}}");
            if (Fault == "record-gone") return Json(new { source = new { status = "not_requested" }, destination = new { status = "not_requested" }, history = "not_found" });
            if (Fault == "401-second" && Writes.Count == 2) return Reply("expired", HttpStatusCode.Unauthorized);
            if (request.Method == HttpMethod.Post)
            {
                var ids = url.AbsolutePath.EndsWith("/transfer/ai-redo", StringComparison.Ordinal)
                    ? JsonDocument.Parse(body).RootElement.GetProperty("history_ids").EnumerateArray().Select(id => id.GetInt64()).ToArray() : [1L];
                return Json(new { progress_key = Fault == "no-progress" ? "" : "fixture-progress", history_ids = Fault == "wrong-ids" ? new[] { 999L } : ids });
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            var source = url.Query.Contains("deletesrc=true", StringComparison.Ordinal);
            var dest = url.Query.Contains("deletedest=true", StringComparison.Ordinal);
            var partial = Fault == "partial";
            return Reply(JsonSerializer.Serialize(new
            {
                success = !partial,
                data = new
                {
                    source = new { status = source ? "deleted" : "not_requested", message = "" },
                    destination = new { status = dest ? partial ? "failed" : "deleted" : "not_requested", message = partial ? "fixture failure" : "" },
                    history = partial ? "retained" : "deleted",
                    message = ""
                }
            }));
        }
        throw new AssertionException("未登记的请求：" + request.Method + " " + url.AbsolutePath);
    }
    private object History(int id, string? dest = null, string? source = null) => new
    {
        id,
        title = Fault == "renamed" ? "DifferentTitle" : "Fixture",
        type = "电影",
        media_source = "themoviedb",
        media_id = Fault == "changed" ? "2" : "1",
        status = true,
        mode = "softlink",
        src = source ?? (Fault == "selected-dependency" && id == 2 ? "/library/one.mkv" : $"/downloads/{HistoryFixture.Name(id)}.mkv"),
        dest = dest ?? $"/library/{HistoryFixture.Name(id)}.mkv",
        src_fileitem = new { storage = Fault == "remote-storage" ? "cloud" : "local", type = Fault == "source-dir" ? "dir" : "file", path = source ?? (Fault == "selected-dependency" && id == 2 ? "/library/one.mkv" : $"/downloads/{HistoryFixture.Name(id)}.mkv") },
        dest_fileitem = new { storage = "local", type = Fault == "target-dir" ? "dir" : "file", path = dest ?? $"/library/{HistoryFixture.Name(id)}.mkv" }
    };
    private static HttpResponseMessage Json(object value) => Reply(JsonSerializer.Serialize(new { success = true, data = value }));
    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

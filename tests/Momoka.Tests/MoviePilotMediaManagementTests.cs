using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
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
        Test("MP 更多菜单：能力补到时不收起且探针在运行清单里", () =>
        {
            // 2026-10-30 用户报过：「刚打开应用时更多里少几条，过几秒菜单自己关了，再开才有」。这条把
            // 「先开菜单、连接与管理能力后到」那段运行检查钉在清单里，别让探针被静默删掉。
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) root = root.Parent;
            Assert.True(root is not null);
            var cases = File.ReadAllText(Path.Combine(root!.FullName, "src/Momoka.Shell/ShellNavigationProbe.cs"));
            Assert.Contains("MoviePilotMenuFillAsync", cases);
            var sections = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/ShellNavigationProbe.Sections.cs"));
            Assert.Contains("[nameof(MoviePilotMenuFillAsync)] = ShellProbeSections.MoviePilot", sections);
            var probe = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/ShellNavigationProbe.MoviePilotAvailability.cs"));
            Assert.Contains("moviepilot-menu-filled.png", probe);
            Assert.Contains("把整张菜单收掉了", probe);
            var commands = File.ReadAllText(Path.Combine(root.FullName, "src/Momoka.Shell/Views/ItemCommands.cs"));
            Assert.Contains("before.IsSubsetOf(after)", commands);
            Assert.Contains("Apply(menu, rows);", commands);
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
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
            MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
        {
            Test("MP 媒体管理选择：" + action + " 取消个别记录后只删除原顺序中的选中 ID", () =>
            {
                using var f = new HistoryFixture();
                f.Transport.Count = 3;
                var original = f.Prepare(action, batch: true);
                long[] ids = [3, 1];
                var selected = original.SelectRecords(ids);
                ids[0] = 2;
                Assert.True(original.Claimed);
                Assert.False(selected.Claimed);
                Assert.Equal("1|3", string.Join("|", selected.Histories.Select(history => history.Id)));
                Assert.Equal(original.Title, selected.Title);
                Assert.Equal(original.Action, selected.Action);
                Assert.Equal(original.Batch, selected.Batch);
                Assert.Equal(original.ConnectionStamp, selected.ConnectionStamp);
                Assert.True(ReferenceEquals(original.Histories[0], selected.Histories[0]));
                Assert.True(ReferenceEquals(original.Histories[2], selected.Histories[1]));
                Assert.Equal(0, f.Transport.Writes.Count);
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(original));
                Assert.Equal(0, f.Transport.Writes.Count);
                var result = f.Submit(selected);
                Assert.Equal(2, result.RequestedCount);
                Assert.False(result.NeedsReview);
                Assert.Equal("{\"id\":1}|{\"id\":3}", string.Join("|", f.Transport.Writes.Select(write => write.Body)));
                Assert.True(f.Transport.Writes.All(write => write.Method == HttpMethod.Delete));
                Assert.True(f.Transport.Writes.All(write => write.Query ==
                    $"?deletesrc={(selected.DeleteSource ? "true" : "false")}&deletedest={(selected.DeleteDestination ? "true" : "false")}"));
                Assert.Throws<MoviePilotOperationBlockedException>(() => selected.SelectRecords([1]));
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(selected));
                Assert.Equal(2, f.Transport.Writes.Count);
            });
        }
        Test("MP 媒体管理选择：全选保留整个范围，原确认只能派生一次且零写", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 3;
            var original = f.Prepare(MoviePilotHistoryAction.DeleteAll, batch: true);
            var selected = original.SelectRecords([3, 2, 1]);
            Assert.Equal("1|2|3", string.Join("|", selected.Histories.Select(history => history.Id)));
            Assert.Throws<MoviePilotOperationBlockedException>(() => original.SelectRecords([2]));
            Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(original));
            Assert.Equal(0, f.Transport.Writes.Count);
        });
        Test("MP 媒体管理选择：空、重复、未知与非法 ID 不产生确认或写入，也不消费有效范围", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 3;
            var original = f.Prepare(MoviePilotHistoryAction.DeleteAll, batch: true);
            Assert.Throws<ArgumentNullException>(() => original.SelectRecords(null!));
            foreach (long[] ids in new long[][] { [], [1, 1], [1, 4], [0], [-1] })
            {
                Assert.Throws<MoviePilotOperationBlockedException>(() => original.SelectRecords(ids));
                Assert.False(original.Claimed);
                Assert.Equal(0, f.Transport.Writes.Count);
            }
            var selected = original.SelectRecords([2]);
            Assert.Equal(1, selected.Histories.Count);
            Assert.Equal(2L, selected.Histories[0].Id);
            Assert.Equal(0, f.Transport.Writes.Count);
        });
        Test("MP 媒体管理选择：并行派生只有一份确认，未选记录不能从子计划加回", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 3;
            var original = f.Prepare(MoviePilotHistoryAction.DeleteAll, batch: true);
            var accepted = new System.Collections.Concurrent.ConcurrentBag<MoviePilotHistoryPlan>();
            var rejected = 0;
            Parallel.For(0, 12, _ =>
            {
                try { accepted.Add(original.SelectRecords([3, 1])); }
                catch (MoviePilotOperationBlockedException) { Interlocked.Increment(ref rejected); }
            });
            Assert.Equal(1, accepted.Count);
            Assert.Equal(11, rejected);
            var selected = accepted.Single();
            Assert.Throws<MoviePilotOperationBlockedException>(() => selected.SelectRecords([1, 2]));
            Assert.False(selected.Claimed);
            Assert.Equal(0, f.Transport.Writes.Count);
            var reduced = selected.SelectRecords([3]);
            Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(selected));
            Assert.Equal(0, f.Transport.Writes.Count);
            var result = f.Submit(reduced);
            Assert.False(result.NeedsReview);
            Assert.Equal("{\"id\":3}", f.Transport.Writes.Single().Body);
        });
        Test("MP 媒体管理选择：选择后快照变化、连接替换或取消仍拒绝写入", () =>
        {
            foreach (var fault in new[] { "changed", "connection", "cancel" })
            {
                using var f = new HistoryFixture();
                f.Transport.Count = 3;
                var selected = f.Prepare(MoviePilotHistoryAction.DeleteAll, batch: true).SelectRecords([2]);
                using var cancellation = new CancellationTokenSource();
                if (fault == "connection") f.Settings.MoviePilot.Username = "replacement";
                else if (fault == "cancel") cancellation.Cancel();
                else f.Transport.Fault = fault;
                if (fault == "cancel")
                    Assert.Throws<OperationCanceledException>(() => f.Service.SubmitHistoryActionAsync(selected, cancellation.Token).GetAwaiter().GetResult());
                else Assert.Throws<MoviePilotException>(() => f.Submit(selected));
                Assert.Equal(0, f.Transport.Writes.Count);
            }
        });
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
        Test("MP 媒体管理准备：媒体编号冲突仍拒绝自动删除", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Fault = "changed";
            Assert.Throws<MoviePilotException>(() => f.Prepare(MoviePilotHistoryAction.DeleteAll));
            Assert.Equal(0, f.Transport.Writes.Count);
        });
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
            MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
        {
            Test("MP 删除重复目标：" + action + " 两条同文件历史可选择，已不存在的第二次文件删除也完成", () =>
            {
                using var f = new HistoryFixture();
                f.Transport.Fault = "duplicate-target";
                var original = f.Prepare(action);
                Assert.Equal("99|1", string.Join("|", original.Histories.Select(history => history.Id)));
                Assert.Equal(0, f.Transport.Writes.Count);
                var selected = original.SelectRecords([1, 99]);
                var result = f.Submit(selected);
                Assert.Equal("{\"id\":99}|{\"id\":1}", string.Join("|", f.Transport.Writes.Select(write => write.Body)));
                Assert.False(result.NeedsReview);
                Assert.Equal(2, result.Receipts.Count);
                Assert.True(result.Receipts.All(receipt => receipt.Complete));
                Assert.Equal(selected.DeleteDestination ? "already_missing" : "not_requested", result.Receipts[1].Destination);
                Assert.Equal(selected.DeleteSource ? "already_missing" : "not_requested", result.Receipts[1].Source);
            });
        }
        Test("MP 删除候选：同目标多记录可以只选一条，重新整理仍要求唯一原记录", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Fault = "duplicate-target";
            Assert.Throws<MoviePilotException>(() => f.Prepare(MoviePilotHistoryAction.SmartReorganize));
            var selected = f.Prepare(MoviePilotHistoryAction.DeleteDestination).SelectRecords([1]);
            Assert.Equal(0, f.Transport.Writes.Count);
            Assert.False(f.Submit(selected).NeedsReview);
            Assert.Equal("{\"id\":1}", f.Transport.Writes.Single().Body);
        });
        Test("MP 单集删除：成功及失败软链记录有不同源、同一目标，全部可选且仅删除唯一库文件", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Fault = "mixed-status-target";
            var context = new MoviePilotTransferContext("Fixture", "电视剧", "1", 1, "5", [new("episode", "/library/one.mkv")]);
            var plan = f.Service.PrepareHistoryActionAsync(context, MoviePilotHistoryAction.DeleteDestination, false,
                f.Service.ConnectionStamp, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal("99|1", string.Join("|", plan.Histories.Select(history => history.Id)));
            Assert.True(plan.Histories.Any(history => history.Success) && plan.Histories.Any(history => !history.Success));
            Assert.Equal(2, plan.Histories.Select(history => history.SourcePath).Distinct().Count());
            Assert.Equal("/library/one.mkv", string.Join("|", plan.LibraryPaths));
            Assert.Equal(0, f.Transport.Writes.Count);
            var result = f.Submit(plan.SelectRecords([1, 99]));
            Assert.False(result.NeedsReview);
            Assert.True(result.RemovesSubject(plan));
            Assert.Equal("/library/one.mkv", string.Join("|", result.RemovedLibraryPaths(plan)));
            Assert.Equal("already_missing", result.Receipts[1].Destination);
            Assert.True(f.Transport.Writes.All(write => write.Query == "?deletesrc=false&deletedest=true"));
        });
        Test("MP 删除匹配：精确目标优先于后续源，保留每个文件候选并按记录 ID 去重", () =>
        {
            var context = new MoviePilotTransferContext("Fixture", "电影", "1", null, "",
                [new("one", "/library/one.mkv"), new("two", "/library/two.mkv")]);
            MoviePilotTransferHistory[] histories =
            [
                new() { Id = 1, SourcePath = "/downloads/one.mkv", TargetPath = "/library/one.mkv" },
                new() { Id = 99, SourcePath = "/downloads/one.mkv", TargetPath = "/library/one.mkv" },
                new() { Id = 100, SourcePath = "/library/one.mkv", TargetPath = "/library/other.mkv" },
                new() { Id = 2, SourcePath = "/library/two.mkv", TargetPath = "/library/two.mkv" }
            ];
            var matched = MoviePilotTransferHistoryMatch.RequireDeletionMatches(context, histories);
            Assert.Equal("99|1|2", string.Join("|", matched.Select(history => history.Id)));
            Assert.Throws<MoviePilotException>(() => MoviePilotTransferHistoryMatch.RequireDeletionMatches(
                context with { Files = [new("missing", "/library/missing.mkv")] }, histories));
        });
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
        {
            Test("MP 删除库文件范围：" + action + "只匹配当前集目标，后续把它当源的第四和第六集不能删除", () =>
            {
                using var f = new HistoryFixture();
                f.Transport.Fault = "only-downstream-sources";
                var context = new MoviePilotTransferContext("Fixture", "电视剧", "1", 1, "5", [new("episode", "/library/one.mkv")]);
                Assert.Throws<MoviePilotException>(() => f.Service.PrepareHistoryActionAsync(context, action, false,
                    f.Service.ConnectionStamp, CancellationToken.None).GetAwaiter().GetResult());
                Assert.Equal(0, f.Transport.Writes.Count);
            });
        }
        foreach (var action in new[] { MoviePilotHistoryAction.DeleteSource, MoviePilotHistoryAction.DeleteAll })
        {
            Test("MP 删除共享源：" + action + " 同目标重复记录可准备，另有不同目标的软链接仍在提交前保护", () =>
            {
                using var f = new HistoryFixture();
                f.Transport.Fault = "duplicate-with-other-target";
                var plan = f.Prepare(action).SelectRecords([1, 99]);
                Assert.Equal(0, f.Transport.Writes.Count);
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(plan));
                Assert.Equal(0, f.Transport.Writes.Count);
            });
        }
        Test("MP 删除目标：其它版本共用下载源不会阻止仅删库文件，引用库文件的下游链接仍受保护", () =>
        {
            using (var f = new HistoryFixture())
            {
                f.Transport.Fault = "duplicate-with-other-target";
                var result = f.Submit(f.Prepare(MoviePilotHistoryAction.DeleteDestination).SelectRecords([1, 99]));
                Assert.False(result.NeedsReview);
                Assert.Equal(2, f.Transport.Writes.Count);
            }
            using (var f = new HistoryFixture())
            {
                f.Transport.Fault = "dependency";
                var plan = f.Prepare(MoviePilotHistoryAction.DeleteDestination);
                Assert.Equal(0, f.Transport.Writes.Count);
                Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(plan));
                Assert.Equal(0, f.Transport.Writes.Count);
            }
        });
        Test("MP 删除共享源：无法核对其它软链接目标时不能当作同目标重复记录", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Fault = "source-with-unknown-target";
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteSource);
            Assert.Equal(0, f.Transport.Writes.Count);
            Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(plan));
            Assert.Equal(0, f.Transport.Writes.Count);
        });
        Test("MP 删除共享源：其它存储里的同名目标仍是另一份软链接，不允许删除其共享源", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Fault = "source-with-other-storage";
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteSource);
            Assert.Equal(0, f.Transport.Writes.Count);
            Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(plan));
            Assert.Equal(0, f.Transport.Writes.Count);
        });
        Test("MP 删除范围：选择单版保留完整条目文件清单，只删一版不会报告整条移除", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 2;
            var original = f.Prepare(MoviePilotHistoryAction.DeleteDestination, true);
            var selected = original.SelectRecords([1]);
            Assert.Equal("/library/one.mkv|/library/two.mkv", string.Join("|", original.LibraryPaths));
            Assert.Equal("/library/one.mkv|/library/two.mkv", string.Join("|", selected.LibraryPaths));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)selected.LibraryPaths)[0] = "/library/changed.mkv");
            var result = f.Submit(selected);
            Assert.Equal("/library/one.mkv", string.Join("|", result.RemovedLibraryPaths(selected)));
            Assert.False(result.RemovesSubject(selected));
        });
        Test("MP 删除范围：所有库版本的目标步骤完成后才报告整条移除", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 2;
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteDestination, true);
            var result = f.Submit(plan);
            Assert.Equal("/library/one.mkv|/library/two.mkv", string.Join("|", result.RemovedLibraryPaths(plan)));
            Assert.True(result.RemovesSubject(plan));
        });
        Test("MP 删除范围：两条历史同一目标只记一次，任一明确目标回执即可证明唯一文件已移除", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Fault = "duplicate-target";
            var selected = f.Prepare(MoviePilotHistoryAction.DeleteDestination).SelectRecords([1, 99]);
            var result = f.Submit(selected);
            Assert.Equal("/library/one.mkv", string.Join("|", result.RemovedLibraryPaths(selected)));
            Assert.True(result.RemovesSubject(selected));
        });
        Test("MP 删除范围：目标已删除但源删除失败时保留警告，同时准确报告库文件已移除", () =>
        {
            using var f = new HistoryFixture();
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteAll);
            var result = new MoviePilotHistoryResult(false, "", [], [new(1, "failed", "deleted", "retained", "源删除失败")], 1);
            Assert.True(result.NeedsReview);
            Assert.Equal("/library/one.mkv", string.Join("|", result.RemovedLibraryPaths(plan)));
            Assert.True(result.RemovesSubject(plan));
        });
        Test("MP 删除范围：未知、失败、仅删记录与未请求目标均不能摘除条目", () =>
        {
            using var f = new HistoryFixture();
            foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
                MoviePilotHistoryAction.DeleteDestination, MoviePilotHistoryAction.DeleteAll })
            {
                var plan = f.Prepare(action);
                foreach (var destination in new[] { "unknown", "failed", "not_requested" })
                {
                    var result = new MoviePilotHistoryResult(false, "", [], [new(1, "deleted", destination, "deleted", "")], 1);
                    Assert.Equal(0, result.RemovedLibraryPaths(plan).Count);
                    Assert.False(result.RemovesSubject(plan));
                }
            }
        });
        Test("MP 删除范围：仅删记录、仅删源及智能回执不接受伪造库删除步骤", () =>
        {
            using var f = new HistoryFixture();
            foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource,
                MoviePilotHistoryAction.SmartReorganize })
            {
                var plan = f.Prepare(action);
                var result = new MoviePilotHistoryResult(false, "", [], [new(1, "deleted", "deleted", "deleted", "")], 1);
                Assert.Equal(0, result.RemovedLibraryPaths(plan).Count);
                Assert.False(result.RemovesSubject(plan));
            }
            var deletion = f.Prepare(MoviePilotHistoryAction.DeleteDestination);
            var smartResult = new MoviePilotHistoryResult(true, "progress", [1], [new(1, "deleted", "deleted", "deleted", "")], 1);
            Assert.Equal(0, smartResult.RemovedLibraryPaths(deletion).Count);
            Assert.False(smartResult.RemovesSubject(deletion));
        });
        Test("MP 删除范围：Windows 路径归一化能确认删除，远端大小写区别仍保留", () =>
        {
            var history = new MoviePilotTransferHistory { Id = 1, TargetPath = "C:\\Library\\One.mkv" };
            var plan = new MoviePilotHistoryPlan("Fixture", MoviePilotHistoryAction.DeleteDestination, false, "fixture", [history],
                ["c:/library/one.mkv"]);
            var result = new MoviePilotHistoryResult(false, "", [], [new(1, "not_requested", "already_missing", "deleted", "")], 1);
            Assert.True(result.RemovesSubject(plan));
            var remote = new MoviePilotHistoryPlan("Fixture", MoviePilotHistoryAction.DeleteDestination, false, "fixture",
                [history with { TargetPath = "/Library/One.mkv" }], ["/library/one.mkv"]);
            Assert.False(result.RemovesSubject(remote));
        });
        Test("MP 删除范围：其它记录或未发送版本的回执不能扩大已移除范围", () =>
        {
            using var f = new HistoryFixture();
            f.Transport.Count = 2;
            var plan = f.Prepare(MoviePilotHistoryAction.DeleteAll, true);
            var result = new MoviePilotHistoryResult(false, "", [],
                [new(1, "deleted", "deleted", "deleted", ""), new(999, "deleted", "deleted", "deleted", "")], 2);
            Assert.Equal("/library/one.mkv", string.Join("|", result.RemovedLibraryPaths(plan)));
            Assert.False(result.RemovesSubject(plan));
        });
        foreach (var keepDeletedHistory in new[] { false, true })
        {
            Test("MP 连续删除版本：媒体库旧清单仍有 A+B，A 历史" + (keepDeletedHistory ? "仍可见" : "已移除") + "时只准备剩余 B", () =>
            {
                using var f = new ItemHistoryFixture();
                f.History.Transport.KeepRemovedHistory = keepDeletedHistory;
                var first = f.Prepare().SelectRecords([1]);
                var result = f.Submit(first);
                Assert.False(result.RemovesSubject(first));
                Assert.Equal("/library/one.mkv", string.Join("|", result.RemovedLibraryPaths(first)));
                var next = f.Prepare(f.ScopeCopy());
                Assert.Equal("2", string.Join("|", next.Histories.Select(history => history.Id)));
                Assert.Equal("/library/two.mkv", string.Join("|", next.LibraryPaths));
                var completed = f.Submit(next);
                Assert.True(completed.RemovesSubject(next));
                Assert.Equal("{\"id\":1}|{\"id\":2}", string.Join("|", f.History.Transport.Writes.Select(write => write.Body)));
            });
        }
        foreach (var fault in new[] { "partial", "no-receipt", "record-gone", "lost" })
        {
            Test("MP 连续删除版本：" + fault + "没有明确目标删除回执时不能跳过 A 或解除防重", () =>
            {
                using var f = new ItemHistoryFixture();
                var selected = f.Prepare().SelectRecords([1]);
                f.History.Transport.Fault = fault;
                var result = f.Submit(selected);
                Assert.Equal(0, result.RemovedLibraryPaths(selected).Count);
                Assert.True(result.NeedsReview);
                f.History.Transport.Fault = "";
                Assert.Throws<MoviePilotException>(() => f.Prepare());
                Assert.Equal(1, f.History.Transport.Writes.Count);
            });
        }
        Test("MP 连续删除版本：仅删记录和仅删源不记录目标消失", () =>
        {
            foreach (var action in new[] { MoviePilotHistoryAction.DeleteRecord, MoviePilotHistoryAction.DeleteSource })
            {
                using var f = new ItemHistoryFixture();
                var first = f.Prepare(action: action).SelectRecords([1]);
                var result = f.Submit(first);
                Assert.Equal(0, result.RemovedLibraryPaths(first).Count);
                Assert.Throws<MoviePilotException>(() => f.Prepare());
                Assert.Equal(1, f.History.Transport.Writes.Count);
            }
        });
        Test("MP 连续删除版本：同账号不同条目不能继承 A 的移除记忆", () =>
        {
            using var f = new ItemHistoryFixture();
            f.Submit(f.Prepare().SelectRecords([1]));
            Assert.Throws<MoviePilotException>(() => f.Prepare(itemId: "other-item"));
            Assert.Equal(1, f.History.Transport.Writes.Count);
        });
        Test("MP 连续删除版本：另一个账号和退出后新会话不能继承同路径移除记忆", () =>
        {
            using var f = new ItemHistoryFixture();
            f.Submit(f.Prepare().SelectRecords([1]));
            var other = f.AddSource("other");
            Assert.Throws<MoviePilotException>(() => f.Prepare(other));
            var replacement = f.ReplaceSource();
            Assert.Throws<MoviePilotException>(() => f.Prepare(replacement));
            Assert.Equal(1, f.History.Transport.Writes.Count);
        });
        Test("MP 连续删除版本：MoviePilot 连接代次变化不继承已删路径", () =>
        {
            using var f = new ItemHistoryFixture();
            f.Submit(f.Prepare().SelectRecords([1]));
            var previous = f.History.Service.ConnectionStamp;
            f.History.Settings.MoviePilot.Username = "replacement-fixture";
            f.History.RefreshSession();
            Assert.False(previous == f.History.Service.ConnectionStamp);
            Assert.Throws<MoviePilotException>(() => f.Prepare());
            Assert.Equal(1, f.History.Transport.Writes.Count);
        });
        Test("MP 连续删除版本：通用历史提交没有条目归属，不能给下一次条目删除记忆", () =>
        {
            using var f = new ItemHistoryFixture();
            f.History.Submit(f.History.Prepare(MoviePilotHistoryAction.DeleteDestination, true).SelectRecords([1]));
            Assert.Throws<MoviePilotException>(() => f.Prepare());
            Assert.Equal(1, f.History.Transport.Writes.Count);
        });
        Test("MP 连续删除版本：准备的条目计划不能提交到另一个在线来源", () =>
        {
            using var f = new ItemHistoryFixture();
            var selected = f.Prepare().SelectRecords([1]);
            Assert.Throws<MoviePilotOperationBlockedException>(() => f.Submit(selected, f.AddSource("other")));
            Assert.Equal(0, f.History.Transport.Writes.Count);
        });
        Test("MP 连续删除版本：源删除失败但目标明确完成，只记已移除目标 A 并可准备剩余 B", () =>
        {
            using var f = new ItemHistoryFixture();
            var first = f.Prepare(action: MoviePilotHistoryAction.DeleteAll).SelectRecords([1]);
            f.History.Transport.Fault = "source-partial";
            var result = f.Submit(first);
            Assert.True(result.NeedsReview);
            Assert.Equal("/library/one.mkv", string.Join("|", result.RemovedLibraryPaths(first)));
            f.History.Transport.Fault = "";
            var next = f.Prepare();
            Assert.Equal("/library/two.mkv", string.Join("|", next.LibraryPaths));
            Assert.Equal(2L, next.Histories.Single().Id);
        });
        Test("MP 连续删除版本：只排除已确认的准确路径，新增版本 C 缺历史仍停止", () =>
        {
            using var f = new ItemHistoryFixture();
            f.Submit(f.Prepare().SelectRecords([1]));
            f.ChangeMediaPath("/library/new-version.mkv");
            Assert.Throws<MoviePilotException>(() => f.Prepare());
            Assert.Equal(1, f.History.Transport.Writes.Count);
        });
        Test("MP 连续删除版本：目标移除记忆不解除两个版本共用源的提交账本", () =>
        {
            using var f = new ItemHistoryFixture();
            f.History.Transport.Fault = "shared-download-source";
            f.Submit(f.Prepare().SelectRecords([1]));
            Assert.Throws<MoviePilotOperationBlockedException>(() => f.Prepare());
            Assert.Equal(1, f.History.Transport.Writes.Count);
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
        RefreshSession();
    }
    public void RefreshSession()
    {
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

internal sealed class ItemHistoryFixture : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "fixtures", "item-history", Guid.NewGuid().ToString("N"));
    private readonly List<CancellationTokenSource> _sourceLifetimes = [];
    private readonly StubTransport _media = new();
    public HistoryFixture History { get; } = new();
    public EmbySession Session { get; }
    public EmbySessionScope Scope { get; }

    public ItemHistoryFixture()
    {
        History.Transport.Count = 2;
        History.Transport.SimulateRemovedHistory = true;
        ChangeMediaPath("/library/two.mkv");
        var protector = new RejectItemHistorySecret();
        Session = new EmbySession(new AppSettings(), new SettingsStore(new AppPaths(_root), protector),
            new CredentialVault(protector), DeviceIdentity.Create("item-history", "1"), _media);
        Scope = AddSource("original");
        typeof(EmbySession).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Scope);
    }

    public void ChangeMediaPath(string otherPath)
    {
        foreach (var id in new[] { "item", "other-item" })
            _media.Answer("Items/" + id, JsonSerializer.Serialize(new
            {
                Id = id,
                Name = "Fixture",
                Type = "Movie",
                Path = "/library/one.mkv",
                ProviderIds = new { Tmdb = "1" },
                MediaSources = new[]
                {
                    new { Id = "A", Name = "A", Path = "/library/one.mkv" },
                    new { Id = "B", Name = "B", Path = otherPath }
                }
            }));
    }

    public EmbySessionScope ScopeCopy() => Session.Capture();

    public EmbySessionScope AddSource(string name)
    {
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
        var connection = new EmbyConnection(new Uri("http://192.0.2.20:8096/emby/"), "anonymous-token", name + "-user",
            "anonymous-fixture", "anonymous-media", Session.Device);
        var identity = new EmbySessionScope.Identity(new EmbyClient(http, connection),
            new ServerProfile { Id = "media", Url = "http://192.0.2.20:8096" }, new AccountProfile { Id = name });
        var source = new EmbySessionScope(Session, identity);
        _sourceLifetimes.Add(identity.Lifetime);
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
        sources.Add(source.Key, source);
        return source;
    }

    public EmbySessionScope ReplaceSource()
    {
        Scope.IdentityState.Lifetime.Cancel();
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session)!;
        sources.Remove(Scope.Key);
        return AddSource("original");
    }

    public MoviePilotHistoryPlan Prepare(EmbySessionScope? source = null, string itemId = "item",
        MoviePilotHistoryAction action = MoviePilotHistoryAction.DeleteDestination) =>
        History.Service.PrepareItemHistoryActionAsync(source ?? Scope, new EmbyItem { Id = itemId, Type = EmbyItemType.Movie }, action,
            History.Service.ConnectionStamp, CancellationToken.None).GetAwaiter().GetResult();

    public MoviePilotHistoryResult Submit(MoviePilotHistoryPlan plan, EmbySessionScope? source = null) =>
        History.Service.SubmitItemHistoryActionAsync(source ?? Scope, plan, CancellationToken.None).GetAwaiter().GetResult();

    public void Dispose()
    {
        Session.Dispose();
        foreach (var lifetime in _sourceLifetimes) lifetime.Dispose();
        History.Dispose();
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")), "匿名会话不得持久化或读取真实设置");
    }

    private sealed class RejectItemHistorySecret : ISecretProtector
    {
        public string Protect(string value) => throw new AssertionException("条目删除夹具不能写入凭据");
        public string Unprotect(string value) => throw new AssertionException("条目删除夹具不能读取凭据");
    }
}

internal sealed class HistoryTransport : HttpMessageHandler
{
    public string Fault { get; set; } = "";
    public int Count { get; set; } = 1;
    public bool SimulateRemovedHistory { get; set; }
    public bool KeepRemovedHistory { get; set; }
    private readonly HashSet<int> _removedHistories = [];
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
            if (SimulateRemovedHistory && !KeepRemovedHistory)
                records = records.Where(record => !_removedHistories.Contains(JsonSerializer.SerializeToElement(record).GetProperty("id").GetInt32())).ToList();
            if (Fault == "ambiguous" || Fault.StartsWith("duplicate-", StringComparison.Ordinal))
                records.Add(History(99, dest: "/library/one.mkv", source: "/downloads/one.mkv"));
            if (Fault == "mixed-status-target") records.Add(History(99, dest: "/library/one.mkv", source: "/downloads/failed-version.mkv"));
            if (Fault == "duplicate-with-other-target") records.Add(History(100, dest: "/library/other.mkv", source: "/downloads/one.mkv"));
            if (Fault == "source-with-unknown-target") records.Add(History(100, dest: "", source: "/downloads/one.mkv"));
            if (Fault == "source-with-other-storage" && dependency) records.Add(History(100, dest: "/library/one.mkv", source: "/downloads/one.mkv"));
            if (Fault == "only-downstream-sources")
                records = [History(4, dest: "/library/episode-four.mkv", source: "/library/one.mkv"),
                    History(6, dest: "/library/episode-six.mkv", source: "/library/one.mkv")];
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
            var sourcePartial = Fault == "source-partial";
            var alreadyMissing = (Fault.StartsWith("duplicate-", StringComparison.Ordinal) || Fault == "mixed-status-target") && Writes.Count > 1;
            if (SimulateRemovedHistory && !partial && !sourcePartial)
                _removedHistories.Add(JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32());
            return Reply(JsonSerializer.Serialize(new
            {
                success = !partial && !sourcePartial,
                data = new
                {
                    source = new { status = source ? sourcePartial ? "failed" : alreadyMissing ? "already_missing" : "deleted" : "not_requested", message = "" },
                    destination = new { status = dest ? partial ? "failed" : alreadyMissing ? "already_missing" : "deleted" : "not_requested", message = partial ? "fixture failure" : "" },
                    history = partial || sourcePartial ? "retained" : "deleted",
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
        type = Fault is "mixed-status-target" or "only-downstream-sources" ? "电视剧" : "电影",
        seasons = Fault == "only-downstream-sources" ? "S01" : "",
        episodes = Fault == "only-downstream-sources" ? "E0" + id : "",
        media_source = "themoviedb",
        media_id = Fault == "changed" ? "2" : "1",
        status = Fault != "mixed-status-target" || id != 99,
        mode = "softlink",
        src = source ?? (Fault == "shared-download-source" ? "/downloads/shared.mkv" : Fault == "selected-dependency" && id == 2 ? "/library/one.mkv" : $"/downloads/{HistoryFixture.Name(id)}.mkv"),
        dest = dest ?? $"/library/{HistoryFixture.Name(id)}.mkv",
        src_fileitem = new { storage = Fault == "remote-storage" ? "cloud" : "local", type = Fault == "source-dir" ? "dir" : "file", path = source ?? (Fault == "shared-download-source" ? "/downloads/shared.mkv" : Fault == "selected-dependency" && id == 2 ? "/library/one.mkv" : $"/downloads/{HistoryFixture.Name(id)}.mkv") },
        dest_fileitem = new { storage = Fault == "source-with-other-storage" && id == 100 ? "cloud" : "local", type = Fault == "target-dir" ? "dir" : "file", path = dest ?? $"/library/{HistoryFixture.Name(id)}.mkv" }
    };
    private static HttpResponseMessage Json(object value) => Reply(JsonSerializer.Serialize(new { success = true, data = value }));
    private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

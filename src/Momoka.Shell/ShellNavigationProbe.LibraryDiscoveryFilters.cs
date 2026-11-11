using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryDiscoveryFiltersAsync()
    {
        using var fixture = CreateFixture();
        var server = new DiscoveryFiltersServer(await FixtureImageAsync(400, 600));
        fixture.Transport.Reply = server.ReplyAsync;
        await fixture.SelectAsync("discovery-filters.invalid", kind: MediaServerKind.Plex);
        fixture.Settings.AggregateMode = false;
        var scope = fixture.Session.Capture();
        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        var evidence = new List<object>();
        try
        {
            ResizeProbeClient(1280, 900);
            frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services,
                Title = "匿名 Plex 影片库",
                ParentId = "1",
                CollectionType = "movies",
                Source = scope
            });
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.IsReady && !page.ViewModel.Busy);
            var view = page.ViewModel;
            Require(!view.NoticeOpen && view.FilterVisibility == Visibility.Visible,
                "Plex 未显示服务器声明的筛选入口");
            await PlexActionAsync(view, "TogglePlexFilterAsync");
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 1000);
                await LayoutAsync(page);
                var panel = Get<Border>(page, "PlexFilterPanel");
                var pickers = Descendants(panel).OfType<ComboBox>().Where(picker => picker.Name == "PlexFilterPicker").ToArray();
                Require(panel.Visibility == Visibility.Visible && pickers.Length == 4 && pickers.All(picker => picker.ActualHeight > 0),
                    "Plex 筛选面板未实际渲染四个服务端字段");
                Require(Bounds(panel, page).Left >= 0 && Bounds(panel, page).Right <= page.ActualWidth + 1,
                    "Plex 筛选面板在窄窗越界");
                await SaveProbeScreenAsync($"discovery-filters-{width}-screen.png");
            }
            foreach (var pair in new[] { ("类型", "动画"), ("年份", "2025"), ("已看状态", "未看"), ("分辨率", "4K") })
            {
                var picker = Descendants(Get<Border>(page, "PlexFilterPanel")).OfType<ComboBox>()
                    .Single(control => Equals(control.Header, pair.Item1));
                var chosen = picker.Items.Cast<object>().Single(value =>
                    (string?)value.GetType().GetProperty("Title")?.GetValue(value) == pair.Item2);
                var reads = server.ItemQueries.Count;
                picker.SelectedItem = chosen;
                await UntilAsync(() => server.ItemQueries.Count > reads && !view.Busy);
            }
            Require(view.FilterCount == 4 && server.ItemQueries.Last().Contains("genre=7", StringComparison.Ordinal)
                && server.ItemQueries.Last().Contains("year=2025", StringComparison.Ordinal)
                && server.ItemQueries.Last().Contains("unwatched=1", StringComparison.Ordinal)
                && server.ItemQueries.Last().Contains("resolution=4k", StringComparison.Ordinal),
                "真实选择框没有把组合条件发送到Plex");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("discovery-filters-combined-760-screen.png");
            var titleSort = view.SortEntries().Single(entry => entry.Option.Value == "titleSort");
            view.ChooseSort(titleSort.Option);
            await UntilAsync(() => !view.Busy);
            view.ClosePlexFilter();
            await LayoutAsync(page);
            page.ShowSortMenu();
            await LayoutAsync(page);
            var sortList = Get<ListView>(page, "SortList");
            Require(sortList.Items.Cast<SortEntry>().Select(entry => entry.Option.Value).SequenceEqual(new[] { "titleSort", "addedAt" }),
                "真实排序菜单没有使用Plex目录");
            await SaveProbeScreenAsync("discovery-sort-menu-760-screen.png");
            Get<Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase>(page, "SortMenu").Hide();
            await LayoutAsync(page);
            var jump = await view.JumpToAsync("B");
            Require(jump is { Index: 3, Letter: "B" } && server.AlphabetReads == 1,
                "Plex字母跳转没有使用服务端桶计数");
            Require(view.Cards[jump!.Value.Index].Item.Name.StartsWith('B'), "Plex 字母位置没有落在 B 桶");
            var alphabet = Get<AlphaBar>(page, "Alpha");
            var letter = Descendants(alphabet).OfType<Button>().Single(button => Equals(button.Content, "B"));
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(letter).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => server.AlphabetReads == 2 && Get<string?>(alphabet, "_currentLetter") == "B");
            await LayoutAsync(page);
            Require(Get<ScrollView>(page, "_scroll").VerticalOffset > 0, "实际字母按钮没有滚动到目标行");
            await SaveProbeScreenAsync("discovery-alpha-B-760-screen.png");
            page.ShowSortMenu();
            await LayoutAsync(page);
            var reverse = Descendants(sortList).OfType<Button>().Single(button => button.Tag is EmbySortOption option
                && option.Value == "titleSort" && button.Visibility == Visibility.Visible);
            ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(reverse).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => !view.Busy && server.ItemQueries.Last().Contains("titleSort%3Adesc", StringComparison.OrdinalIgnoreCase));
            Require(view.AlphaVisibility == Visibility.Collapsed, "降序仍展示不可用的正向字母跳转");
            Get<Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase>(page, "SortMenu").Hide();
            evidence.Add(new { CombinedFilters = 4, AlphaIndex = jump.Value.Index, Descending = true });
            await view.TogglePlexFilterAsync();
            await PlexActionAsync(view, "ClearPlexFiltersAsync");
            Require(view.FilterCount == 0 && !view.NoticeOpen, "清除Plex筛选失败");
            server.FailCatalog = true;
            await PlexActionAsync(view, "RetryPlexFiltersAsync");
            await LayoutAsync(page);
            var retry = Get<HyperlinkButton>(page, "PlexFilterRetry");
            Require(retry.IsLoaded && retry.ActualHeight > 0 && retry.Visibility == Visibility.Visible,
                "Plex 目录失败没有可见重试入口");
            await SaveProbeScreenAsync("discovery-filters-catalog-failure-screen.png");
            server.FailCatalog = false;
            await PlexActionAsync(view, "RetryPlexFiltersAsync");
            Require(view.FilterVisibility == Visibility.Visible && !view.NoticeOpen, "Plex目录恢复未恢复筛选");
            server.EmptyCatalog = true;
            await PlexActionAsync(view, "RetryPlexFiltersAsync");
            Require(!view.NoticeOpen && view.Cards.Count > 0 && view.FilterVisibility == Visibility.Collapsed,
                "空目录应隐藏筛选并继续显示普通库内容");
            server.EmptyCatalog = false;
            await PlexActionAsync(view, "RetryPlexFiltersAsync");
            Require(view.FilterVisibility == Visibility.Visible && !view.NoticeOpen, "空目录恢复后没有恢复入口");
            Require(fixture.Actions.Plays == 0, "Plex筛选探针触发播放");
        }
        finally { page?.Release(); frame.Content = null; _root.Children.Remove(frame); }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "discovery-filters-receipt.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Resolve the optional feature on the old build too, so the native before-check can fail on its missing affordance.
    private static Task PlexActionAsync(LibraryViewModel view, string method) =>
        view.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(view, null) as Task
        ?? throw new InvalidOperationException("缺少Plex筛选操作：" + method);

    private sealed class DiscoveryFiltersServer(byte[] poster)
    {
        public bool FailCatalog { get; set; }
        public bool EmptyCatalog { get; set; }
        public int AlphabetReads { get; private set; }
        public List<string> ItemQueries { get; } = [];
        public Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require(request.Method == HttpMethod.Get, "Plex筛选探针拒绝写入");
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal)) return Reply(new(HttpStatusCode.NotFound));
            if (path.EndsWith("/identity", StringComparison.Ordinal) || path is "/media" or "/media/")
                return Reply(Json(new { MediaContainer = new { machineIdentifier = "discovery-filters", friendlyName = "匿名 Plex", version = "1.41.2" } }));
            if (path.EndsWith("/library/sections", StringComparison.Ordinal))
                return Reply(Json(new { MediaContainer = new { Directory = new[] { new { key = "1", type = "movie", title = "影片", filters = true } } } }));
            if (path.EndsWith("/library/sections/1", StringComparison.Ordinal))
            {
                if (FailCatalog) return Reply(new(HttpStatusCode.ServiceUnavailable));
                if (EmptyCatalog) return Reply(Json(new { MediaContainer = new { Type = Array.Empty<object>() } }));
                return Reply(Json(new
                {
                    MediaContainer = new
                    {
                        Type = new[] { new
                {
                    type = "movie", key = "/library/sections/1/all?type=1", title = "电影",
                    Filter = new object[] {
                        new { filter = "genre", filterType = "string", key = "/library/sections/1/genre?type=1", title = "类型" },
                        new { filter = "year", filterType = "integer", key = "/library/sections/1/year?type=1", title = "年份" },
                        new { filter = "unwatched", filterType = "boolean", title = "已看状态" },
                        new { filter = "resolution", filterType = "string", key = "/library/sections/1/resolution?type=1", title = "分辨率" } },
                    Sort = new object[] {
                        new { key = "titleSort", descKey = "titleSort:desc", title = "标题", defaultDirection = "asc", firstCharacterKey = "firstCharacter" },
                        new { key = "addedAt", descKey = "addedAt:desc", title = "添加时间", defaultDirection = "desc" } }
                } }
                    }
                }));
            }
            foreach (var field in new[] { "genre", "year", "resolution" })
                if (path.EndsWith("/library/sections/1/" + field, StringComparison.Ordinal))
                {
                    var pairs = field switch
                    {
                        "genre" => new[] { ("7", "动画"), ("8", "剧情") },
                        "year" => new[] { ("2024", "2024"), ("2025", "2025") },
                        _ => new[] { ("1080", "1080p"), ("4k", "4K") }
                    };
                    return Reply(Json(new { MediaContainer = new { Directory = pairs.Select(pair => new { key = pair.Item1, title = pair.Item2 }).ToArray() } }));
                }
            if (path.EndsWith("/library/sections/1/firstCharacter", StringComparison.Ordinal))
            {
                AlphabetReads++;
                Require(Query(request, "type") == "1" && Query(request, "genre") == "7" && Query(request, "year") == "2025",
                    "字母索引没有保留媒体类型与当前组合筛选");
                return Reply(Json(new
                {
                    MediaContainer = new
                    {
                        Directory = new[] {
                    new { key = "A", title = "A", size = 3 }, new { key = "B", title = "B", size = 37 } }
                    }
                }));
            }
            if (path.EndsWith("/library/sections/1/all", StringComparison.Ordinal))
            {
                ItemQueries.Add(request.RequestUri.Query);
                var start = request.Headers.TryGetValues("X-Plex-Container-Start", out var starts) ? int.Parse(starts.Single()) : 0;
                var size = request.Headers.TryGetValues("X-Plex-Container-Size", out var sizes) ? int.Parse(sizes.Single()) : 20;
                var movies = Enumerable.Range(start, Math.Max(0, Math.Min(size, 40 - start))).Select(index => new
                {
                    ratingKey = (index + 500).ToString(),
                    title = (index < 3 ? "A" : "B") + " 影片 " + index,
                    type = "movie",
                    librarySectionID = 1,
                    thumb = "/library/metadata/500/thumb/1"
                }).ToArray();
                return Reply(Json(new { MediaContainer = new { totalSize = 40, Metadata = movies } }));
            }
            if (path.Contains("/thumb/", StringComparison.Ordinal))
                return Reply(new(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            throw new InvalidOperationException("Plex筛选探针出现未登记的读取：" + path);
        }
        private static Task<HttpResponseMessage> Reply(HttpResponseMessage value) => Task.FromResult(value);
    }
}

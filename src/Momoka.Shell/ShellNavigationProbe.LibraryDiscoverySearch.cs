using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Actual search controls and navigation with anonymous read-only HTTP.</summary>
    private async Task LibraryDiscoverySearchAsync()
    {
        var evidence = new List<object>();
        var poster = await FixtureImageAsync(400, 600);
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            using var fixture = CreateFixture();
            var server = new DiscoverySearchServer(kind, poster);
            fixture.Transport.Reply = server.ReplyAsync;
            await fixture.SelectAsync("discovery-search.invalid", kind: kind);
            fixture.Settings.AggregateMode = false;
            var source = fixture.Session.Capture();
            var shell = DiscoveryShell(fixture);
            fixture.Actions.CaptureOpen = shell.OpenItem;
            var frame = Get<Frame>(shell, "ContentFrame");
            _root.Children.Add(shell);
            try
            {
                foreach (var width in new[] { 1280, 760, 360 })
                {
                    ResizeProbeClient(width, 900);
                    foreach (var category in new[] { "People", "Collections", "Playlists" })
                    {
                        (frame.Content as IShellContent)?.Release();
                        Require(frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services, "星海") with
                        { Source = source }), "扩展搜索页导航失败");
                        var page = (LibraryPage)frame.Content;
                        await UntilAsync(() => page.IsReady && !page.ViewModel.Busy);
                        Require(!page.ViewModel.NoticeOpen, "热门搜索返回错误");
                        var kinds = page.ViewModel.Cards.Select(card => card.Item.Type).Distinct().ToArray();
                        Require(new[] { EmbyItemType.Person, EmbyItemType.BoxSet, EmbyItemType.Playlist }
                            .All(kinds.Contains), "热门搜索缺少人物、合集或播放列表");
                        var tabs = Get<SelectorBar>(page, "SearchCategories");
                        var selected = tabs.Items.SingleOrDefault(item => (string?)item.Tag == category);
                        Require(selected is { Visibility: Visibility.Visible }, "搜索缺少可见分类：" + category);
                        selected!.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                        tabs.SelectedItem = selected;
                        await UntilAsync(() => (page.NavigationRequest as LibraryRequest)?.SearchCategory.ToString() == category
                            && !page.ViewModel.Busy && page.ViewModel.Cards.Count > 0);
                        await LayoutAsync(page);
                        var expected = category switch
                        {
                            "People" => EmbyItemType.Person,
                            "Collections" => EmbyItemType.BoxSet,
                            _ => EmbyItemType.Playlist
                        };
                        Require(page.ViewModel.Cards.All(card => card.Item.Type == expected && !card.Item.IsPlayable),
                            "分类结果混入其他类型或暴露直接播放");
                        var viewport = Get<ScrollViewer>(page, "SearchCategoriesScroll");
                        Require(Bounds(viewport, page).Left >= -1 && Bounds(viewport, page).Right <= page.ActualWidth + 1,
                            "扩展搜索分类在窄窗越界");
                        await SaveProbeScreenAsync($"discovery-search-{kind}-{category}-{width}-screen.png");

                        var target = page.ViewModel.Cards[0];
                        var searchRequest = page.NavigationRequest;
                        page.ViewModel.Open(target);
                        if (expected == EmbyItemType.Playlist)
                        {
                            await UntilAsync(() => frame.Content is ServerListsPage listPage && listPage.ViewModel.IsReady && !listPage.ViewModel.Busy);
                            var listPage = (ServerListsPage)frame.Content;
                            Require(!listPage.ViewModel.NoticeOpen && listPage.ViewModel.Scope?.Key == source.Key
                                && listPage.ViewModel.OpenedList?.Id == target.Item.Id, "播放列表打开失去身份或成员上下文");
                            Require(listPage.ViewModel.Members.Select(entry => entry.Item.Id).SequenceEqual(new[] { "list-z", "list-a" }),
                                "播放列表成员被标题排序或未读到");
                            await LayoutAsync(listPage);
                            await SaveProbeScreenAsync($"discovery-open-{kind}-{category}-{width}-screen.png");
                            evidence.Add(new
                            {
                                Backend = kind.ToString(),
                                Width = width,
                                Category = category,
                                SourceRetained = true,
                                Items = listPage.ViewModel.Members.Select(entry => entry.Item.Id).ToArray()
                            });
                            continue;
                        }
                        if (expected == EmbyItemType.Person)
                        {
                            await UntilAsync(() => frame.Content is PersonPage person && person.ViewModel.IsReady && !person.ViewModel.Busy);
                            var person = (PersonPage)frame.Content;
                            Require(person.NavigationRequest is PersonRequest request && request.Person.Source?.Key == source.Key
                                && person.ViewModel.Subject.Id == target.Item.Id, "人物页丢失原身份或人物编号");
                            Require(person.ViewModel.MovieShelf?.Cards.Select(card => card.Item.Id).SequenceEqual(new[] { "work" }) == true,
                                "人物作品未读到");
                            await LayoutAsync(person);
                            await SaveProbeScreenAsync($"discovery-open-{kind}-{category}-{width}-screen.png");
                            evidence.Add(new { Backend = kind.ToString(), Width = width, Category = category, SourceRetained = true });
                            continue;
                        }
                        // Frame may reuse the cached LibraryPage; its request, rather than page object, identifies navigation.
                        await UntilAsync(() => frame.Content is LibraryPage next && !ReferenceEquals(next.NavigationRequest, searchRequest)
                            && next.IsReady && !next.ViewModel.Busy);
                        var opened = (LibraryPage)frame.Content;
                        Require(!opened.ViewModel.NoticeOpen, "搜索结果打开后出现错误");
                        var openedRequest = opened.NavigationRequest as LibraryRequest;
                        Require(openedRequest?.Source?.Key == source.Key, "结果打开丢失原服务器账号");
                        Require(expected == EmbyItemType.Person
                            ? openedRequest?.PersonId == target.Item.Id
                            : openedRequest?.ParentId == target.Item.Id,
                            "人物被作为容器打开，或合集/播放列表丢失父项");
                        Require(opened.ViewModel.Cards.Select(card => card.Item.Id).SequenceEqual(
                            expected == EmbyItemType.Playlist ? new[] { "list-z", "list-a" } : new[] { "work" }),
                            "结果打开为空，或播放列表成员被标题排序");
                        await LayoutAsync(opened);
                        await SaveProbeScreenAsync($"discovery-open-{kind}-{category}-{width}-screen.png");
                        evidence.Add(new
                        {
                            Backend = kind.ToString(),
                            Width = width,
                            Category = category,
                            SourceRetained = true,
                            Items = opened.ViewModel.Cards.Select(card => card.Item.Id).ToArray()
                        });
                    }
                }
                Require(fixture.Actions.Plays == 0 && server.Writes == 0, "只读搜索探针触发了播放或写操作");
                if (kind == MediaServerKind.Jellyfin)
                    Require(server.PersonReads > 0 && server.PlaylistReads > 0, "Jellyfin 未使用人物或列表成员接口");
            }
            finally
            {
                (frame.Content as IShellContent)?.Release();
                frame.Content = null;
                fixture.Actions.CaptureOpen = null;
                Get<DispatcherTimer>(shell, "_toast").Stop();
                _root.Children.Remove(shell);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "discovery-search-receipt.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Mount the real navigation shell without attaching a player, production startup or account events.
    private static ShellPage DiscoveryShell(Fixture fixture)
    {
        var shell = new ShellPage();
        Field(shell, "_services", fixture.Services);
        Field(shell, "_session", fixture.Session);
        Field(shell, "_settings", fixture.SettingsService);
        Field(shell, "_capabilities", fixture.Capabilities);
        Get<FrameworkElement>(shell, "SignIn").Visibility = Visibility.Collapsed;
        Get<FrameworkElement>(shell, "ContentHost").Visibility = Visibility.Visible;
        Get<FrameworkElement>(shell, "SidebarHost").Visibility = Visibility.Collapsed;
        return shell;
    }

    private sealed class DiscoverySearchServer(MediaServerKind kind, byte[] poster)
    {
        public int PersonReads { get; private set; }
        public int PlaylistReads { get; private set; }
        public int Writes { get; private set; }
        public Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Get) Writes++;
            Require(request.Method == HttpMethod.Get, "搜索探针拒绝服务器写入");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Reply(new(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return Reply(Json(new
                {
                    ProductName = kind.ToString(),
                    ServerName = "匿名搜索",
                    Version =
                    kind == MediaServerKind.Jellyfin ? "10.10.7" : "4.10.1.0"
                }));
            if (path.EndsWith("/Users/Me", StringComparison.Ordinal) || path.EndsWith("/Users/user", StringComparison.Ordinal))
                return Reply(Json(new { Id = "user", Name = "user", Policy = new { IsAdministrator = false } }));
            if (path.EndsWith("/Persons", StringComparison.Ordinal))
            {
                PersonReads++;
                Require(Query(request, "UserId") == "user" && Query(request, "Limit") == "0",
                    "Jellyfin 人物读取没有用户边界或旧版全匹配读取语义");
                return Reply(Page([Entry("person-1", "星海 同名人物", EmbyItemType.Person),
                    Entry("person-2", "星海 同名人物", EmbyItemType.Person)]));
            }
            if (path.EndsWith("/Playlists/playlist/Items", StringComparison.Ordinal))
            {
                PlaylistReads++;
                Require(Query(request, "UserId") == "user", "Jellyfin 列表读取缺少用户");
                return Reply(Page([Entry("list-z", "Z 顺序第一部", EmbyItemType.Movie),
                    Entry("list-a", "A 顺序第二部", EmbyItemType.Movie)], request));
            }
            if (path.EndsWith("/Playlists/playlist/Users/user", StringComparison.Ordinal))
                return Reply(Json(new { UserId = "user", CanEdit = false }));
            if (path.EndsWith("/Items/person-1", StringComparison.Ordinal)) return Reply(Json(Entry("person-1", "星海 同名人物", EmbyItemType.Person)));
            if (path.EndsWith("/Items/playlist", StringComparison.Ordinal))
                return Reply(Json(new { Id = "playlist", Type = "Playlist", Name = "星海 播放列表", MediaType = "Video", CanEditItems = false }));
            if (path.EndsWith("/Items", StringComparison.Ordinal))
            {
                if (Query(request, "ParentId") == "playlist")
                {
                    Require(kind == MediaServerKind.Emby && Query(request, "SortBy") == "ListItemOrder",
                        "播放列表使用了错误的成员路由或排序");
                    return Reply(Page([Entry("list-z", "Z 顺序第一部", EmbyItemType.Movie),
                        Entry("list-a", "A 顺序第二部", EmbyItemType.Movie)], request));
                }
                if (Query(request, "ParentId") == "collection" || Query(request, "PersonIds") == "person-1")
                    return Reply(Page([Entry("work", "星海 关联作品", EmbyItemType.Movie)], request));
                if (Query(request, "SearchTerm") is not null)
                {
                    var types = (Query(request, "IncludeItemTypes") ?? "").Split(',');
                    var items = new[] { Entry("movie", "星海 影片", EmbyItemType.Movie),
                        Entry("person-1", "星海 同名人物", EmbyItemType.Person),
                        Entry("person-2", "星海 同名人物", EmbyItemType.Person),
                        Entry("collection", "星海 合集", EmbyItemType.BoxSet),
                        Entry("playlist", "星海 播放列表", EmbyItemType.Playlist) };
                    if (kind == MediaServerKind.Jellyfin)
                        Require(!types.Contains(EmbyItemType.Person), "Jellyfin 人物混入普通 Items 搜索");
                    return Reply(Page(items.Where(item => types.Contains(item.Type)).ToArray(), request));
                }
                return Reply(Page([]));
            }
            return Reply(FakeTransport.Standard(request));
        }

        private static EmbyItem Entry(string id, string title, string type) => new()
        {
            Id = id,
            Name = title,
            Type = type,
            IsFolder = type is EmbyItemType.BoxSet or EmbyItemType.Playlist,
            ImageTags = new() { ["Primary"] = "anonymous" }
        };
        private static HttpResponseMessage Page(EmbyItem[] items, HttpRequestMessage? request = null)
        {
            var start = request is not null && int.TryParse(Query(request, "StartIndex"), out var offset) ? offset : 0;
            var limit = request is not null && int.TryParse(Query(request, "Limit"), out var count) && count > 0 ? count : items.Length;
            return Json(new { Items = items.Skip(start).Take(limit).ToArray(), TotalRecordCount = items.Length });
        }
        private static Task<HttpResponseMessage> Reply(HttpResponseMessage value) => Task.FromResult(value);
    }
}

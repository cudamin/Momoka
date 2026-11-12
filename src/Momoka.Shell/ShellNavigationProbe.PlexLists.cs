using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Services;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Real Plex list controls with an inert PMS handler; no account exchange, media stream or disk write.</summary>
    private async Task PlexServerListsAsync()
    {
        using var fixture = CreateFixture();
        var server = new PlexListsProbeServer();
        fixture.Transport.AllowPlexProfile = true;
        fixture.Transport.Reply = server.ReplyAsync;
        await fixture.SelectAsync("plex-lists.invalid", user: "7", kind: MediaServerKind.Plex);
        var source = fixture.Session.Capture();
        var services = new PlexListsProbeServices(fixture.Services);
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            foreach (var width in new[] { 1280, 760, 360 })
            {
                server.Editable = true;
                ResizeProbeClient(width, 900);
                (frame.Content as IShellContent)?.Release();
                Require(frame.Navigate(typeof(ServerListsPage), new ServerListsRequest(services, source)), "Plex 清单页导航失败");
                var page = (ServerListsPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(page.ViewModel.Lists.Count == 1 && page.ViewModel.Lists[0].Id == "80" && !page.ViewModel.NoticeOpen,
                    "Plex 视频目录未保留普通清单或混入智能／音乐清单");
                Require(page.ViewModel.Libraries.Count == 2, "Plex 清单没有独立影视库目录");
                await LayoutAsync(page);
                Require(Bounds(Get<ComboBox>(page, "ServerListAccount"), page).Right <= page.ActualWidth + 1,
                    "Plex 账号选择器超出窄窗");
                Require(Get<AppBarButton>(page, "ServerListCreate").Visibility == Visibility.Visible,
                    "Plex 有明确创建能力却未显示新建入口");
                await SaveProbeScreenAsync($"plex-lists-directory-{width}-screen.png");

                var creates = server.Writes.Count;
                Invoke(Get<AppBarButton>(page, "ServerListCreate"));
                var create = await DialogAsync();
                Find<TextBox>(create, "ServerListName").Text = "离线周末电影";
                Require(!create.IsPrimaryButtonEnabled, "Plex 没有初始媒体却允许创建");
                await SearchAsync(create, "星海");
                var createMatches = Find<ListView>(create, "ServerListSearchResults");
                Require(createMatches.Items.Count == 2, "Plex 电影搜索没有映射给媒体选择器");
                createMatches.SelectedIndex = 0;
                Require(create.IsPrimaryButtonEnabled, "Plex 已选媒体却不能创建");
                await SaveProbeScreenAsync($"plex-lists-create-{width}-screen.png");
                Invoke(Find<Button>(create, "PrimaryButton"));
                await UntilAsync(() => Dialogs().Length == 0 && !page.ViewModel.Busy && server.Writes.Count == creates + 1);
                Require(server.Writes.Last() == "POST /media/playlists", "Plex Primary 确认没有按原生创建提交一次");
                Require(page.ViewModel.NoticeSeverity == InfoBarSeverity.Success, "Plex 创建确认后没有显示保存结果");

                await page.ViewModel.OpenAsync(page.ViewModel.Lists[0]);
                var members = Get<ListView>(page, "ServerListMembers");
                members.SelectedIndex = 1;
                await LayoutAsync(page);
                Require(page.ViewModel.Members.Count == 3 && page.ViewModel.Members[0].Item.Id == page.ViewModel.Members[1].Item.Id
                    && page.ViewModel.Members[0].EntryId == "201" && page.ViewModel.Members[1].EntryId == "202",
                    "Plex 重复影片失去原生成员身份");
                Require(Get<AppBarButton>(page, "ServerListRemove").Visibility == Visibility.Visible
                    && Get<AppBarButton>(page, "ServerListMoveUp").Visibility == Visibility.Visible,
                    "Plex 普通可编辑清单未提供成员移除与上移");
                Require(Bounds(Get<CommandBar>(page, "ServerListCommands"), page).Right <= page.ActualWidth + 1,
                    "Plex 原生操作栏超出窄窗");
                await SaveProbeScreenAsync($"plex-lists-members-{width}-screen.png");
                var beforeMove = server.Writes.Count;
                await page.ViewModel.MoveAsync(-1);
                Require(server.Writes.Count == beforeMove + 1 && server.Writes.Last() == "PUT /media/playlists/80/items/202/move",
                    "Plex 上移没有按第二个成员身份发送");
                Require(page.ViewModel.Members[0].EntryId == "202" && page.ViewModel.NoticeSeverity == InfoBarSeverity.Success,
                    "Plex 排序写回读后没有保留确认顺序");
                server.ResetMembers();
                await page.ViewModel.ReloadAsync();

                Invoke(Get<AppBarButton>(page, "ServerListAdd"));
                var add = await DialogAsync();
                await SearchAsync(add, "星海");
                Find<ListView>(add, "ServerListSearchResults").SelectedIndex = 0;
                Require(add.IsPrimaryButtonEnabled && DialogDismiss.GetClosesOnOutsideClick(add), "Plex 添加表没有选择或点外取消");
                var beforeCancel = server.Writes.Count;
                add.Hide();
                await UntilAsync(() => Dialogs().Length == 0);
                Require(server.Writes.Count == beforeCancel, "Plex 取消媒体选择仍发送写入");

                server.Editable = false;
                await page.ViewModel.ReloadAsync();
                members.SelectedIndex = 0;
                await LayoutAsync(page);
                Require(Get<AppBarButton>(page, "ServerListAdd").Visibility == Visibility.Collapsed
                    && Get<AppBarButton>(page, "ServerListRemove").Visibility == Visibility.Collapsed
                    && Get<AppBarButton>(page, "ServerListMoveUp").Visibility == Visibility.Collapsed,
                    "Plex readOnly 清单仍露出写操作");
                await SaveProbeScreenAsync($"plex-lists-readonly-{width}-screen.png");
                server.Editable = true;
                await page.ViewModel.ReloadAsync();
                Require(Get<AppBarButton>(page, "ServerListAdd").Visibility == Visibility.Visible, "Plex 恢复编辑权限后没有恢复入口");

                await page.ViewModel.BackAsync();
                var kind = Get<ComboBox>(page, "ServerListKind");
                kind.SelectedIndex = 1;
                await UntilAsync(() => page.ViewModel.Kind == ServerListKind.Collection && page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(page.ViewModel.Lists.Count == 1 && page.ViewModel.Lists[0].Id == "90", "Plex 合集未按所选电影库单独读取");
                Require(server.CollectionQueries.Any(query => query == "5:1"), "Plex 合集目录没有原生电影 subtype");
                await SaveProbeScreenAsync($"plex-collections-directory-{width}-screen.png");
                await page.ViewModel.OpenAsync(page.ViewModel.Lists[0]);
                members.SelectedIndex = 0;
                await LayoutAsync(page);
                Require(page.ViewModel.Members.Count == 1 && page.ViewModel.Members[0].EntryId == "10"
                    && Get<AppBarButton>(page, "ServerListRemove").Visibility == Visibility.Visible
                    && Get<AppBarButton>(page, "ServerListMoveUp").Visibility == Visibility.Collapsed
                    && Get<AppBarButton>(page, "ServerListMoveDown").Visibility == Visibility.Collapsed,
                    "Plex 合集成员或首轮排序边界错误");
                Invoke(Get<AppBarButton>(page, "ServerListAdd"));
                var collectionAdd = await DialogAsync();
                await SearchAsync(collectionAdd, "星海");
                Require(Find<ListView>(collectionAdd, "ServerListSearchResults").Items.Count == 2
                    && Find<ListView>(collectionAdd, "ServerListSearchResults").Items.Cast<EmbyItem>().All(item => item.PlexSectionId == "5"),
                    "Plex 合集选择结果没有限制当前电影库");
                Find<ListView>(collectionAdd, "ServerListSearchResults").SelectedIndex = 0;
                await SaveProbeScreenAsync($"plex-collections-add-{width}-screen.png");
                var beforeCollectionCancel = server.Writes.Count;
                collectionAdd.Hide();
                await UntilAsync(() => Dialogs().Length == 0);
                Require(server.Writes.Count == beforeCollectionCancel, "取消合集添加仍写入媒体关系");
                await page.ViewModel.BackAsync();
                var library = Get<ComboBox>(page, "ServerListLibrary");
                library.SelectedIndex = 1;
                await UntilAsync(() => page.ViewModel.Library?.Id == "6" && page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(page.ViewModel.Lists.Count == 1 && page.ViewModel.Lists[0].Id == "91"
                    && server.CollectionQueries.Any(query => query == "6:2"), "Plex 电视剧合集未按独立库和subtype读取");
            }
            Require(fixture.Actions.Plays == 0, "Plex 清单探针触发播放");
            Require(server.StreamRequests == 0 && server.AccountRequests == 0, "Plex 清单探针越过媒体或真实账号边界");
        }
        finally
        {
            foreach (var dialog in Dialogs()) dialog.Hide();
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
        }

        ContentDialog[] Dialogs() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Distinct().ToArray();
        async Task<ContentDialog> DialogAsync()
        {
            await UntilAsync(() => Dialogs().Length == 1);
            var dialog = Dialogs().Single();
            await LayoutAsync(dialog);
            return dialog;
        }
        async Task SearchAsync(ContentDialog dialog, string term)
        {
            Find<TextBox>(dialog, "ServerListSearchTerm").Text = term;
            Invoke(Find<Button>(dialog, "ServerListSearch"));
            await UntilAsync(() => Find<ListView>(dialog, "ServerListSearchResults").Items.Count > 0);
            await LayoutAsync(dialog);
        }
        static T Find<T>(ContentDialog dialog, string name) where T : FrameworkElement =>
            Descendants(dialog).OfType<T>().Single(control => control.Name == name);
        static void Invoke(FrameworkElement button)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
            Require(peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "Plex 清单按钮没有原生 Invoke 模式");
            ((IInvokeProvider)peer!.GetPattern(PatternInterface.Invoke)).Invoke();
        }
    }

    private sealed class PlexListsProbeServices(IServiceProvider services) : IServiceProvider
    {
        private readonly ServerListsService _lists = new();
        public object? GetService(Type type) => type == typeof(ServerListsService) ? _lists : services.GetService(type);
    }

    private sealed class PlexListsProbeServer
    {
        public bool Editable { get; set; } = true;
        public List<string> Writes { get; } = [];
        public List<string> CollectionQueries { get; } = [];
        public List<string> Searches { get; } = [];
        public int StreamRequests { get; private set; }
        public int AccountRequests { get; private set; }
        private readonly List<(string Id, string Entry)> _members = [("10", "201"), ("10", "202"), ("11", "203")];
        public void ResetMembers() { _members.Clear(); _members.AddRange([("10", "201"), ("10", "202"), ("11", "203")]); }

        public Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require(request.RequestUri is not null, "Plex 清单探针请求缺少地址");
            if (request.RequestUri!.Host == "plex.tv")
            {
                Require(request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/v2/user",
                    "Plex 清单探针仅允许固定只读账号权限地址");
                Require(!request.RequestUri.Query.Contains("X-Plex-Token", StringComparison.OrdinalIgnoreCase), "Plex 账号权限令牌进入URL");
                return Task.FromResult(Json(new { id = 7, anonymous = false }));
            }
            Require(request.RequestUri.Host == "plex-lists.invalid", "Plex 清单探针拒绝其他来源");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("stream", StringComparison.Ordinal) || path.Contains("/parts/", StringComparison.Ordinal))
            { StreamRequests++; throw new InvalidOperationException("Plex 清单探针禁止媒体流"); }
            if (path.Contains("signin", StringComparison.Ordinal))
            { AccountRequests++; throw new InvalidOperationException("Plex 清单探针禁止账号交换"); }
            if (request.Method != HttpMethod.Get)
            {
                Require(path == "/media/playlists" || path.StartsWith("/media/playlists/80/items", StringComparison.Ordinal)
                    || path == "/media/library/collections" || path.StartsWith("/media/library/collections/90/items", StringComparison.Ordinal)
                    || path.StartsWith("/media/library/collections/90/children/", StringComparison.Ordinal), "Plex 探针拒绝非清单关系写入");
                Writes.Add(request.Method.Method + " " + path);
                if (path.EndsWith("/move", StringComparison.Ordinal))
                {
                    var entry = path.Split('/')[^2];
                    var member = _members.Single(item => item.Entry == entry);
                    _members.Remove(member);
                    var after = Query(request, "after");
                    var index = after is null ? 0 : _members.FindIndex(item => item.Entry == after) + 1;
                    _members.Insert(index, member);
                }
                return Reply(new { Metadata = new[] { Playlist() } });
            }
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal) || path.EndsWith("/sys/version", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path == "/media/identity" || path == "/media/")
                return Reply(new { machineIdentifier = "plex-lists-machine", friendlyName = "匿名 Plex 清单", version = "1.41.2" });
            Require(request.Headers.Contains("X-Plex-Token"), "Plex 清单请求未携带占位头部令牌");
            Require(!request.RequestUri.Query.Contains("X-Plex-Token", StringComparison.OrdinalIgnoreCase), "Plex 占位令牌进入URL");
            if (path == "/media/media/providers") return Reply(new
            {
                MediaProvider = new[] { new { identifier = "com.plexapp.plugins.library", Feature = new object[]
                { new { type = "playlist", key = "/playlists", requires = "nonAnonymousAccount" }, new { type = "collection", key = "/library/collections" }, new { type = "manage" } } } }
            });
            if (path == "/media/library/sections") return Reply(new
            {
                Directory = new[]
                { new { key = "5", type = "movie", title = "电影库" }, new { key = "6", type = "show", title = "电视剧库" } }
            });
            if (path == "/media/playlists/all") return Reply(new
            { Metadata = new object[] { Playlist(), Playlist("81", smart: 1), Playlist("82", family: "audio") }, totalSize = 3 });
            if (path == "/media/playlists/80") return Reply(new { Metadata = new[] { Playlist() } });
            if (path == "/media/playlists/80/items") return Reply(new
            { Metadata = _members.Select(member => Member(member.Id, member.Entry)).ToArray(), totalSize = _members.Count });
            if (path is "/media/library/sections/5/collections" or "/media/library/sections/6/collections")
            {
                var section = path.Split('/')[^2];
                CollectionQueries.Add(section + ":" + Query(request, "subtype"));
                return Reply(new { Metadata = new[] { Collection(section) }, totalSize = 1 });
            }
            if (path is "/media/library/collections/90" or "/media/library/collections/91")
                return Reply(new { Metadata = new[] { Collection(path.EndsWith("/91", StringComparison.Ordinal) ? "6" : "5") } });
            if (path == "/media/library/collections/90/children") return Reply(new { Metadata = new[] { Member("10", null) }, totalSize = 1 });
            if (path == "/media/library/collections/91/children") return Reply(new { Metadata = new[] { Show("12") }, totalSize = 1 });
            if (path == "/media/search" || path is "/media/library/sections/5/all" or "/media/library/sections/6/all")
            {
                var term = Query(request, path == "/media/search" ? "query" : "title") ?? "";
                var section = path == "/media/search" ? "all" : path.Split('/')[^2];
                Searches.Add(section + ":" + term);
                return Reply(new { Metadata = new[] { Member("10", null), Member("11", null) }, totalSize = 2 });
            }
            if (path.StartsWith("/media/library/metadata/", StringComparison.Ordinal))
                return Reply(new { Metadata = new[] { Member(path.Split('/')[^1], null) } });
            throw new InvalidOperationException("Unexpected Plex list fixture path: " + path);
        }

        private object Playlist(string id = "80", int smart = 0, string family = "video") => new
        {
            ratingKey = id,
            key = "/playlists/" + id + "/items",
            type = "playlist",
            playlistType = family,
            title = "Plex 周末电影 · 原生成员身份",
            readOnly = Editable ? 0 : 1,
            smart
        };
        private static object Collection(string section) => new
        {
            ratingKey = section == "6" ? "91" : "90",
            key = "/library/collections/" + (section == "6" ? "91" : "90") + "/children",
            type = "collection",
            subtype = section == "6" ? "show" : "movie",
            librarySectionID = section,
            title = section == "6" ? "电视剧合集" : "电影合集",
            smart = 0
        };
        private static object Member(string id, string? entry) => new
        {
            ratingKey = id,
            key = "/library/metadata/" + id,
            type = "movie",
            librarySectionID = "5",
            title = id == "10" ? "星海电影 · 重复加入" : "星海续篇",
            playlistItemID = entry
        };
        private static object Show(string id) => new
        { ratingKey = id, key = "/library/metadata/" + id + "/children", type = "show", librarySectionID = "6", title = "星海剧集" };
        private static Task<HttpResponseMessage> Reply(object root) => Task.FromResult(Json(new { MediaContainer = root }));
    }
}

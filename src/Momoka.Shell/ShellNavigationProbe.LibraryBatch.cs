using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>正式多选控件与匿名HTTP；取消只阻止下一项，已发请求保留自己的确证结果。</summary>
    private async Task LibraryBatchAsync()
    {
        var receipts = new List<object>();
        var poster = await FixtureImageAsync();
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.Plex })
        {
            using var fixture = CreateFixture();
            var server = new LibraryBatchServer(kind, poster);
            fixture.Transport.Reply = server.ReplyAsync;
            await fixture.SelectAsync("library-batch.invalid", kind: kind);
            var source = fixture.Session.Capture();
            var capabilities = new ServerCapabilities(fixture.Session);
            await capabilities.ProbeAsync();
            fixture.Actions.MenuPermissions = scope => capabilities.PermissionsFor(scope.Key);
            var frame = new Frame();
            LibraryPage? page = null;
            _root.Children.Add(frame);
            var topMost = _window!.TopMost;
            try
            {
                _window.TopMost = true;
                frame.Navigate(typeof(LibraryPage), new LibraryRequest
                {
                    Services = fixture.Services,
                    Title = "匿名多选 · " + kind,
                    ParentId = "1",
                    CollectionType = "movies",
                    Source = source
                });
                page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.IsReady && !page.ViewModel.Busy && page.ViewModel.Cards.Count == 3);
                Require(!page.ViewModel.NoticeOpen, "匿名多选初次浏览失败");

                foreach (var width in new[] { 1100, 760 })
                {
                    ResizeProbeClient(width, 900);
                    await LayoutAsync(page);
                    Invoke(Get<Button>(page, "LibrarySelectButton"));
                    await UntilAsync(() => Get<FrameworkElement>(page, "LibraryBatchSurface").Visibility == Visibility.Visible);
                    SelectAll();
                    await LayoutAsync(page);
                    var wall = Get<ItemsView>(page, "CardWall");
                    Require(wall.SelectedItems.Count == 3, "正式卡墙没有保留三项选择");
                    Require(Get<TextBlock>(page, "LibrarySelectionCount").Text.Contains('3'), "选择数没有更新");
                    var surface = Get<FrameworkElement>(page, "LibraryBatchSurface");
                    var bounds = surface.TransformToVisual(page).TransformBounds(new(0, 0, surface.ActualWidth, surface.ActualHeight));
                    Require(surface.ActualHeight > 0 && bounds.Left >= -1 && bounds.Right <= page.ActualWidth + 1,
                        "宽窄多选工具栏没有实际布局或超出客户区");
                    Require(Get<Button>(page, "PlayAllButton").Visibility == Visibility.Collapsed,
                        "选择期间仍显示播放全部");
                    var realized = Descendants(page).OfType<PosterCard>().Where(card => card.Card?.Item.Id is "101" or "102" or "103").ToArray();
                    Require(realized.Length == 3 && realized.All(card => card.IsLoaded && card.ActualWidth > 0),
                        "三张所选海报没有全部进入实际视觉树");
                    foreach (var card in realized)
                    {
                        foreach (var name in new[] { "PlayButton", "MoreButton", "WatchedButton", "FavoriteButton" })
                            Require(Get<Button>(card, name).Visibility == Visibility.Collapsed,
                                "选择期间仍显示单项动作：" + name);
                        var container = AncestorContainer(card);
                        Require(container.IsSelected, "正式ItemContainer没有显示已选");
                        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(container);
                        Require(peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider { IsSelected: true },
                            "正式选择容器未暴露UIA已选状态");
                        Require(!string.IsNullOrWhiteSpace(peer.GetAutomationId()) || !string.IsNullOrWhiteSpace(peer.GetName()),
                            "选择项没有可定位的名称或句柄");
                    }
                    await OpenBatchMenuAsync();
                    Require(MenuHas(ItemCommand.MarkPlayed), "所选影片没有已看多选入口");
                    if (kind == MediaServerKind.Plex)
                        Require(!MenuHas(ItemCommand.Favorite) && !MenuHas(ItemCommand.Unfavorite)
                            && !MenuHas(ItemCommand.AddToCollection), "Plex 多选露出未接入的收藏或合集写入");
                    else Require(MenuHas(ItemCommand.Favorite) && MenuHas(ItemCommand.AddToCollection),
                        "获授权Emby/Jellyfin多选缺少收藏或合集入口");
                    CloseBatchMenus();
                    await LayoutAsync(page);
                    await SaveProbeScreenAsync($"library-batch-{kind}-{width}-selection-screen.png");
                    Invoke(Get<Button>(page, "LibraryEndSelection"));
                    await UntilAsync(() => Get<FrameworkElement>(page, "LibraryBatchSurface").Visibility == Visibility.Collapsed);
                    Require(wall.SelectedItems.Count == 0 && server.Writes.Count == 0, "退出选择没有清空选择或产生写入");
                }

                BeginAndSelectAll();
                await OpenBatchMenuAsync();
                Invoke(BatchMenuItems().Single(item => item.Tag is ItemCommand.MarkPlayed));
                await UntilAsync(() => OpenBatchDialogs().Length == 1);
                var dialog = OpenBatchDialogs().Single();
                Require(DialogDismiss.GetClosesOnOutsideClick(dialog), "批处理确认没有接上点外取消");
                Require(DialogDismiss.TryDismissFromBackdrop(dialog), "批处理确认的实际遮罩无法取消");
                await UntilAsync(() => OpenBatchDialogs().Length == 0);
                await SettleAsync();
                Require(server.Writes.Count == 0, "取消批处理确认仍写入服务器");

                if (kind != MediaServerKind.Plex)
                {
                    fixture.Session.ReplaceClient(source, source.CurrentConnection with { IsAdministrator = false, CanManageCollections = false });
                    await OpenBatchMenuAsync();
                    Require(!MenuHas(ItemCommand.AddToCollection) && MenuHas(ItemCommand.Favorite),
                        "撤销合集授权没有收起入口或误收起收藏");
                    CloseBatchMenus();
                    await SaveProbeScreenAsync($"library-batch-{kind}-denied-screen.png");
                    fixture.Session.ReplaceClient(source, source.CurrentConnection with
                    {
                        IsAdministrator = kind == MediaServerKind.Emby,
                        CanManageCollections = true
                    });
                    await OpenBatchMenuAsync();
                    Require(MenuHas(ItemCommand.AddToCollection), "恢复授权后本页多选入口没有恢复");
                    CloseBatchMenus();
                }

                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    try { await BatchOperationAsync(page, ItemCommand.MarkPlayed, cancelled.Token); }
                    catch (OperationCanceledException) { }
                }
                Require(server.Writes.Count == 0, "预取消批处理产生了写入");
                if (Get<FrameworkElement>(page, "LibraryBatchSurface").Visibility != Visibility.Visible) BeginAndSelectAll();

                server.HoldSecondWrite = true;
                var run = BatchOperationAsync(page, ItemCommand.MarkPlayed);
                try
                {
                    await server.SecondWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await LayoutAsync(page);
                    var cancel = Get<Button>(page, "LibraryCancelBatch");
                    Require(cancel.Visibility == Visibility.Visible && cancel.IsEnabled, "执行中没有取消剩余入口");
                    Invoke(cancel);
                    server.SecondWriteRelease.TrySetResult();
                    await run;
                }
                finally
                {
                    server.SecondWriteRelease.TrySetResult();
                    await run;
                }
                await LayoutAsync(page);
                Require(server.Writes.Select(write => write.Id).SequenceEqual(new[] { "101", "102" }),
                    "取消剩余重发了成功项或仍发送第三项");
                Require(page.ViewModel.Cards.Single(card => card.Item.Id == "101").Item.IsWatched,
                    "第一项成功没有更新正式卡片");
                Require(!page.ViewModel.Cards.Single(card => card.Item.Id == "102").Item.IsWatched
                    && !page.ViewModel.Cards.Single(card => card.Item.Id == "103").Item.IsWatched,
                    "失败或未尝试项被当成成功");
                RequireBatchStatuses(page, "Succeeded", "Failed", "Canceled");
                var result = Get<InfoBar>(page, "LibraryBatchResult");
                Require(result.IsOpen && result.ActualHeight > 0, "部分成功没有实际可见结果反馈");
                await SaveProbeScreenAsync($"library-batch-{kind}-partial-screen.png");
                receipts.Add(new
                {
                    Product = kind.ToString(),
                    Scenario = "success-failure-cancelled-rest",
                    Writes = server.Writes.ToArray(),
                    Results = BatchStatuses(page),
                    server.MetadataReads,
                    ConfirmationCancelledWrites = 0,
                    PreCancelledWrites = 0,
                    fixture.Actions.Plays
                });

                if (kind == MediaServerKind.Plex)
                {
                    Call(page, "ProbeEndSelection");
                    BeginAndSelectAll();
                    server.HoldSecondWrite = false;
                    server.FailMetadata = true;
                    server.HoldMetadata = true;
                    var before = server.Writes.Count;
                    var refreshing = BatchOperationAsync(page, ItemCommand.MarkUnplayed);
                    try
                    {
                        await server.MetadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        Require(!page.ViewModel.Cards.Single(card => card.Item.Id == "101").Item.IsWatched,
                            "Plex 写已确认，但在飞补读仍阻塞卡片确认位");
                        server.MetadataRelease.TrySetResult();
                        await refreshing;
                    }
                    finally
                    {
                        server.MetadataRelease.TrySetResult();
                        await refreshing;
                    }
                    Require(server.Writes.Count == before + 3 && server.Writes.Skip(before).Select(write => write.Id)
                        .SequenceEqual(new[] { "101", "102", "103" }), "Plex 补读失败重复发送已确认写入");
                    Require(page.ViewModel.Cards.All(card => !card.Item.IsWatched), "Plex 补读失败撤回已确认未看位");
                    RequireBatchStatuses(page, "Succeeded", "Succeeded", "Succeeded");
                    Require(BatchItems(page).Count(item => item.GetType().GetProperty("RefreshError")?.GetValue(item) is not null) == 3,
                        "Plex 三项确认没有各自保留独立刷新失败");
                    Require(Get<InfoBar>(page, "LibraryBatchResult").IsOpen, "Plex 补读失败没有结果反馈");
                    await SaveProbeScreenAsync("library-batch-Plex-refresh-failed-screen.png");
                    receipts.Add(new
                    {
                        Product = kind.ToString(),
                        Scenario = "confirmed-refresh-failed",
                        Writes = server.Writes.Skip(before).ToArray(),
                        Results = BatchStatuses(page),
                        server.MetadataReads,
                        fixture.Actions.Plays
                    });
                }
                Require(fixture.Actions.Plays == 0 && fixture.Actions.Opened == 0, "多选探针触发了播放或详情导航");
            }
            finally
            {
                server.SecondWriteRelease.TrySetResult();
                server.MetadataRelease.TrySetResult();
                foreach (var dialog in OpenBatchDialogs()) dialog.Hide();
                CloseBatchMenus();
                page?.Release();
                frame.Content = null;
                _root.Children.Remove(frame);
                _window.TopMost = topMost;
            }

            void BeginAndSelectAll()
            {
                Call(page!, "ProbeBeginSelection");
                SelectAll();
            }
            void SelectAll()
            {
                for (var index = 0; index < 3; index++) Call(page!, "ProbeSelectBatch", index);
            }
            async Task OpenBatchMenuAsync()
            {
                CloseBatchMenus();
                await LayoutAsync(page!);
                Invoke(Get<Button>(page!, "LibraryBatchActions"));
                await UntilAsync(() => BatchMenuItems().Length > 0);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "library-batch-receipt.json"),
            JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));

        void CloseBatchMenus()
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .Where(popup => Descendants(popup.Child).OfType<MenuFlyoutPresenter>().Any()).ToArray()) popup.IsOpen = false;
        }
        MenuFlyoutItem[] BatchMenuItems() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutItem>().ToArray();
        bool MenuHas(ItemCommand command) => BatchMenuItems().Any(item => item.Tag is ItemCommand value && value == command);
        ContentDialog[] OpenBatchDialogs() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Distinct().ToArray();
        static void Invoke(FrameworkElement control)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control);
            Require(peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "多选控件没有UIA调用入口：" + control.Name);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        }
        static ItemContainer AncestorContainer(DependencyObject child)
        {
            for (var parent = VisualTreeHelper.GetParent(child); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ItemContainer container) return container;
            throw new InvalidOperationException("正式海报不在ItemContainer里");
        }
    }

    private static Task BatchOperationAsync(LibraryPage page, ItemCommand command, CancellationToken token = default) =>
        page.GetType().GetMethod("ProbeBatchOperationAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(page, [command, token]) as Task ?? throw new InvalidOperationException("缺少正式多选执行入口");

    private static object[] BatchItems(LibraryPage page)
    {
        var result = page.GetType().GetProperty("LastBatchResult", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(page) ?? throw new InvalidOperationException("多选没有逐项结果读数");
        var values = result.GetType().GetProperty("Results")?.GetValue(result) as IEnumerable
            ?? throw new InvalidOperationException("多选结果没有逐项Results");
        return values.Cast<object>().ToArray();
    }

    private static string[] BatchStatuses(LibraryPage page) =>
        BatchItems(page).Select(item => item.GetType().GetProperty("Status")?.GetValue(item)?.ToString()
            ?? throw new InvalidOperationException("多选结果没有逐项Status")).ToArray();

    private static void RequireBatchStatuses(LibraryPage page, params string[] expected) =>
        Require(BatchStatuses(page).SequenceEqual(expected), "多选成功、失败与未尝试读数不符：" + string.Join(',', BatchStatuses(page)));

    private sealed record LibraryBatchWrite(string Id, string Method, string Path);

    private sealed class LibraryBatchServer(MediaServerKind kind, byte[] poster)
    {
        private readonly Dictionary<string, EmbyItem> _items = new[] { "101", "102", "103" }.ToDictionary(id => id, id => new EmbyItem
        {
            Id = id,
            Name = "匿名多选影片 " + id,
            Type = EmbyItemType.Movie,
            UserData = new() { Played = false, IsFavorite = false },
            ImageTags = new() { ["Primary"] = "fixture" }
        });
        public List<LibraryBatchWrite> Writes { get; } = [];
        public int MetadataReads { get; private set; }
        public bool HoldSecondWrite { get; set; }
        public bool FailMetadata { get; set; }
        public bool HoldMetadata { get; set; }
        public TaskCompletionSource SecondWriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondWriteRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MetadataEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MetadataRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            var plexWrite = path.EndsWith("/:/scrobble", StringComparison.Ordinal) || path.EndsWith("/:/unscrobble", StringComparison.Ordinal);
            if (plexWrite || request.Method != HttpMethod.Get)
            {
                Require(kind == MediaServerKind.Plex ? plexWrite && request.Method == HttpMethod.Get
                    : path.Contains("/PlayedItems/", StringComparison.Ordinal) || path.Contains("/FavoriteItems/", StringComparison.Ordinal),
                    "匿名多选探针拒绝未知写入：" + path);
                var id = kind == MediaServerKind.Plex ? Query(request, "key") : path.Split('/')[^1];
                Require(id is not null && _items.ContainsKey(id), "匿名批写没有锁定本夹具条目");
                if (kind == MediaServerKind.Plex)
                    Require(Query(request, "identifier") == "com.plexapp.plugins.library"
                        && request.Headers.Contains("X-Plex-Token") && Query(request, "X-Plex-Token") is null,
                        "Plex批写没有保留单项协议与令牌头");
                Writes.Add(new(id!, request.Method.Method, request.RequestUri.PathAndQuery));
                if (HoldSecondWrite && id == "102")
                {
                    SecondWriteEntered.TrySetResult();
                    // 忽略在飞取消，检查取消剩余不丢弃当前HTTP失败。
                    await SecondWriteRelease.Task;
                    return new(HttpStatusCode.Forbidden);
                }
                if (path.Contains("/FavoriteItems/", StringComparison.Ordinal)) _items[id!].UserData!.IsFavorite = request.Method == HttpMethod.Post;
                else _items[id!].UserData!.Played = kind == MediaServerKind.Plex
                    ? path.EndsWith("/:/scrobble", StringComparison.Ordinal) : request.Method == HttpMethod.Post;
                return kind == MediaServerKind.Plex ? new(HttpStatusCode.NoContent) : Json(_items[id!].UserData!);
            }
            if (path.Contains("/Images/", StringComparison.Ordinal) || path.Contains("/thumb/", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) };
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return kind == MediaServerKind.Plex ? new(HttpStatusCode.NotFound)
                    : Json(new { ProductName = kind.ToString(), ServerName = "匿名多选", Version = kind == MediaServerKind.Emby ? "4.10.1.0" : "10.10.7" });
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/Users/Me", StringComparison.Ordinal) || path.EndsWith("/Users/user", StringComparison.Ordinal))
                return Json(new { Id = "user", Policy = new { IsAdministrator = kind == MediaServerKind.Emby, EnableCollectionManagement = true, EnableContentDeletion = false } });
            if (kind != MediaServerKind.Plex && path.EndsWith("/Items", StringComparison.Ordinal))
                return Json(new { Items = _items.Values.ToArray(), TotalRecordCount = _items.Count });
            if (kind != MediaServerKind.Plex && path.Contains("/Items/", StringComparison.Ordinal) && _items.TryGetValue(path.Split('/')[^1], out var embyItem))
                return Json(embyItem);
            if (path.EndsWith("/identity", StringComparison.Ordinal) || path is "/media" or "/media/")
                return Json(new { MediaContainer = new { machineIdentifier = "library-batch-machine", friendlyName = "匿名多选Plex", version = "1.41.2" } });
            if (path.EndsWith("/library/sections", StringComparison.Ordinal))
                return Json(new { MediaContainer = new { Directory = new[] { new { key = "1", title = "匿名影片库", type = "movie", filters = false } } } });
            if (path.EndsWith("/library/sections/1", StringComparison.Ordinal))
                return Json(new { MediaContainer = new { Type = Array.Empty<object>() } });
            if (path.EndsWith("/library/sections/1/all", StringComparison.Ordinal))
                return Json(new { MediaContainer = new { Metadata = _items.Values.Select(PlexItem).ToArray(), totalSize = _items.Count } });
            if (path.Contains("/library/metadata/", StringComparison.Ordinal) && _items.TryGetValue(path.Split('/')[^1], out var plexItem))
            {
                MetadataReads++;
                if (HoldMetadata)
                {
                    MetadataEntered.TrySetResult();
                    await MetadataRelease.Task;
                }
                return FailMetadata ? new(HttpStatusCode.ServiceUnavailable)
                    : Json(new { MediaContainer = new { Metadata = new[] { PlexItem(plexItem) } } });
            }
            return FakeTransport.Standard(request);
        }

        private static object PlexItem(EmbyItem item) => new
        {
            ratingKey = item.Id,
            title = item.Name,
            type = "movie",
            librarySectionID = 1,
            duration = 1_200_000,
            viewCount = item.IsWatched ? 1 : 0,
            viewOffset = 0,
            thumb = "/library/metadata/" + item.Id + "/thumb/1"
        };
    }
}

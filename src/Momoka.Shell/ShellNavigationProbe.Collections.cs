using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>只写匿名HTTP夹具里的成员关系；不删除媒体、不读真实账号、不播放。</summary>
    private async Task LibraryCollectionsAsync()
    {
        var receipts = new List<object>();
        var poster = await FixtureImageAsync();
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            using var fixture = CreateFixture();
            var members = new List<EmbyItem> { Movie("collection-first", "第一部"), Movie("collection-second", "第二部") };
            var media = members.ToDictionary(item => item.Id);
            var writes = new List<string>();
            var reads = 0;
            fixture.Transport.Reply = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method != HttpMethod.Get)
                {
                    Require(path.EndsWith(kind == MediaServerKind.Emby ? "/Collections/set/Items/Delete" : "/Collections/set/Items", StringComparison.Ordinal)
                        && request.Method == (kind == MediaServerKind.Emby ? HttpMethod.Post : HttpMethod.Delete),
                        "匿名合集探针拒绝成员关系以外的写入");
                    Require(Query(request, "Ids") == "collection-first", "移除请求没有锁定原成员");
                    writes.Add(request.Method.Method + " " + request.RequestUri.PathAndQuery);
                    members.RemoveAll(item => item.Id == "collection-first");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                }
                if (path.Contains("/Images/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
                if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                    return Task.FromResult(Json(new { ProductName = kind.ToString(), ServerName = "匿名合集", Version = kind == MediaServerKind.Emby ? "4.10.1.0" : "10.10.7" }));
                if (path.EndsWith("/Users/Me", StringComparison.Ordinal) || path.EndsWith("/Users/user", StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Id = "user", Policy = new { IsAdministrator = kind == MediaServerKind.Emby, EnableCollectionManagement = true, EnableContentDeletion = false } }));
                if (path.EndsWith("/Items", StringComparison.Ordinal) && Query(request, "ParentId") == "set")
                {
                    reads++;
                    return Task.FromResult(Json(new { Items = members.ToArray(), TotalRecordCount = members.Count }));
                }
                if (path.EndsWith("/Items/collection-first", StringComparison.Ordinal))
                    return Task.FromResult(Json(media["collection-first"]));
                return Task.FromResult(FakeTransport.Standard(request));
            };
            await fixture.SelectAsync(kind: kind);
            var source = fixture.Session.Capture();
            var capabilities = new ServerCapabilities(fixture.Session);
            await capabilities.ProbeAsync();
            Require(capabilities.PermissionsFor(source.Key).CanManageCollections == true
                && capabilities.PermissionsFor(source.Key).IsAdministrator == (kind == MediaServerKind.Emby), "匿名探测没有保留合集授权");
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
                    Title = "匿名合集",
                    ParentId = "set",
                    ParentType = EmbyItemType.BoxSet,
                    Source = source
                });
                page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy && page.ViewModel.Cards.Count == 2);
                foreach (var width in new[] { 1100, 760 })
                {
                    ResizeProbeClient(width, 800);
                    await LayoutAsync(page);
                    await OpenMenuAsync("collection-first");
                    Require(Has(ItemCommand.AddToCollection) && Has(ItemCommand.RemoveFromCollection), "获授权成员缺少合集操作");
                    if (kind == MediaServerKind.Jellyfin)
                        Require(!Has(ItemCommand.EditMetadata) && !Has(ItemCommand.ChangeCover) && !Has(ItemCommand.Delete),
                            "普通用户的合集授权扩大成媒体编辑或删除权限");
                    await SaveProbeScreenAsync($"collections-{kind}-{width}-menu-screen.png");
                    CloseMenus();
                    await SettleAsync();
                }
                var original = page.ViewModel.Cards.Single(card => card.Item.Id == "collection-first").Item;
                var oldReads = reads;
                await OpenMenuAsync("collection-first");
                var remove = MenuItems().Single(row => row.Tag is ItemCommand.RemoveFromCollection);
                Require(remove.Text == "从合集中移除", "移除关系的菜单文案不明确");
                new MenuFlyoutItemAutomationPeer(remove).Invoke();
                await UntilAsync(() => writes.Count == 1 && reads > oldReads && !page.ViewModel.Busy && page.ViewModel.Cards.Count == 1);
                Require(page.ViewModel.Cards[0].Item.Id == "collection-second", "成功后没有只刷新当前合集成员");
                Require(Get<TextBlock>(page, "LibraryItemCount").Text == "共 1 项", "移除后工具栏总数未更新");
                var full = await source.ExecuteAsync((client, token) => client.GetItemAsync(original.Id, token), CancellationToken.None);
                Require(full.Id == original.Id && full.UserData?.IsFavorite == true && full.UserData.PlaybackPositionTicks == 1234,
                    "移除关系破坏媒体或观看状态");
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"collections-{kind}-removed-screen.png");

                fixture.Session.ReplaceClient(source, source.CurrentConnection with { IsAdministrator = false, CanManageCollections = false });
                await OpenMenuAsync("collection-second");
                Require(!Has(ItemCommand.AddToCollection) && !Has(ItemCommand.RemoveFromCollection), "明确拒绝没有隐藏合集操作");
                await SaveProbeScreenAsync($"collections-{kind}-denied-screen.png");
                CloseMenus();
                fixture.Session.ReplaceClient(source, source.CurrentConnection with
                {
                    IsAdministrator = kind == MediaServerKind.Emby,
                    CanManageCollections = true
                });
                await OpenMenuAsync("collection-second");
                Require(Has(ItemCommand.AddToCollection) && Has(ItemCommand.RemoveFromCollection), "授权恢复后当前页入口没有恢复");
                var membership = page.ViewModel.Cards[0].CollectionMembership!;
                CloseMenus();
                page.ViewModel.Cancel();
                Require(!membership.Matches(page.ViewModel.Cards[0].Item) && source.IsAvailable, "页面取消未终止合集关系或误注销账号");
                Require(writes.Count == 1 && fixture.Actions.Plays == 0 && media.Count == 2, "匿名验证删除了媒体或启动播放");
                receipts.Add(new { Product = kind.ToString(), Writes = writes.ToArray(), Reads = reads, RemainingMembers = members.Select(item => item.Id).ToArray(), MediaCount = media.Count, Plays = fixture.Actions.Plays });
            }
            finally
            {
                CloseMenus();
                page?.Release();
                frame.Content = null;
                _root.Children.Remove(frame);
                _window.TopMost = topMost;
            }

            async Task OpenMenuAsync(string id)
            {
                CloseMenus();
                await LayoutAsync(page!);
                await UntilAsync(() => Descendants(page!).OfType<PosterCard>().Any(card => card.Card?.Item.Id == id));
                var card = Descendants(page!).OfType<PosterCard>().First(value => value.Card?.Item.Id == id);
                new ButtonAutomationPeer(Get<Button>(card, "MoreButton")).Invoke();
                await UntilAsync(() => MenuItems().Length > 0);
                Require(card.Card!.CollectionMembership?.Matches(card.Card.Item) == true, "原生卡片菜单未绑定当前合集关系");
            }
            MenuFlyoutItem[] MenuItems() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutItem>().ToArray();
            bool Has(ItemCommand command) => MenuItems().Any(row => row.Tag is ItemCommand value && value == command);
            void CloseMenus()
            {
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                    .Where(popup => Descendants(popup.Child).OfType<MenuFlyoutPresenter>().Any()).ToArray()) popup.IsOpen = false;
            }
        }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "collections-receipt.json"),
            JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));

        static EmbyItem Movie(string id, string name)
        {
            var item = new EmbyItem { Id = id, Name = name, Type = EmbyItemType.Movie, UserData = new() { IsFavorite = true, PlaybackPositionTicks = 1234 } };
            item.ImageTags["Primary"] = "fixture";
            return item;
        }
    }
}

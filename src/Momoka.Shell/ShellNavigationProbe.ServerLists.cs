using System.Net;
using System.Text.Json;
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
    /// <summary>Real native page/list/command bar and modal search against anonymous local handlers.</summary>
    private async Task ServerListsAsync()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin }) await ServerListsForAsync(kind);
    }

    private async Task ServerListsForAsync(MediaServerKind kind)
    {
        using var fixture = CreateFixture();
        var server = new ServerListsProbeServer(kind);
        fixture.Transport.Reply = server.ReplyAsync;
        await fixture.SelectAsync("server-lists.invalid", kind: kind);
        var source = fixture.Session.Capture();
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            foreach (var width in new[] { 1280, 760, 360 })
            {
                ResizeProbeClient(width, 900);
                (frame.Content as IShellContent)?.Release();
                Require(frame.Navigate(typeof(ServerListsPage), new ServerListsRequest(fixture.Services, source)), "服务器清单页导航失败");
                var page = (ServerListsPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
                Require(page.Tag as string == ServerListsRequest.Tag, "服务器清单目录没有根目的地标记");
                Require(page.ViewModel.Lists.Count == 1 && !page.ViewModel.NoticeOpen, "清单目录没有读到普通视频清单");
                await LayoutAsync(page);
                Require(Bounds(Get<ComboBox>(page, "ServerListAccount"), page).Right <= page.ActualWidth + 1, "账号选择器超出窄窗");
                await SaveProbeScreenAsync($"server-lists-{kind}-directory-{width}-screen.png");
                await page.ViewModel.OpenAsync(page.ViewModel.Lists[0]);
                await LayoutAsync(page);
                Require(page.ViewModel.Members.Count == 3 && page.ViewModel.Members[0].Item.Id == page.ViewModel.Members[1].Item.Id
                    && page.ViewModel.Members[0].EntryId != page.ViewModel.Members[1].EntryId, "重复媒体失去独立成员身份");
                var list = Get<ListView>(page, "ServerListMembers");
                list.SelectedIndex = 1;
                await LayoutAsync(page);
                Require(Get<AppBarButton>(page, "ServerListRemove").Visibility == Visibility.Visible, "编辑清单不显示成员操作");
                Require(Bounds(Get<CommandBar>(page, "ServerListCommands"), page).Right <= page.ActualWidth + 1, "原生命令栏超出窄窗");
                await SaveProbeScreenAsync($"server-lists-{kind}-members-{width}-screen.png");
                await page.ViewModel.MoveAsync(-1);
                Require(server.Writes.Last().Contains("entry-b/Move/0", StringComparison.Ordinal), "排序没有按选中成员提交");
                Require(page.ViewModel.NoticeSeverity == InfoBarSeverity.Warning, "服务器未达到目标顺序时仍声称排序完成");

                InvokeAppBar(Get<AppBarButton>(page, "ServerListAdd"));
                await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 1);
                var dialog = DialogsFor(_root.XamlRoot)[0];
                Require(DialogDismiss.GetClosesOnOutsideClick(dialog), "清单编辑表未接点外取消");
                // 对话框对象先出现，模板与内容随后一拍才实例化；Machine 慢或窗口刚缩放时直接 Single 会读到空树。
                await LayoutAsync(dialog);
                await UntilAsync(() => Descendants(dialog).OfType<TextBox>().Any(item => item.Name == "ServerListSearchTerm"));
                var search = Descendants(dialog).OfType<TextBox>().Single(item => item.Name == "ServerListSearchTerm");
                search.Text = "星海";
                new ButtonAutomationPeer(Descendants(dialog).OfType<Button>().Single(item => item.Name == "ServerListSearch")).Invoke();
                await UntilAsync(() => Descendants(dialog).OfType<ListView>().Single(item => item.Name == "ServerListSearchResults").Items.Count > 0);
                var matches = Descendants(dialog).OfType<ListView>().Single(item => item.Name == "ServerListSearchResults");
                matches.SelectedIndex = 0;
                await LayoutAsync(dialog);
                Require(dialog.IsPrimaryButtonEnabled, "搜索选中媒体后不能添加");
                await SaveProbeScreenAsync($"server-lists-{kind}-add-{width}-screen.png");
                var before = server.Writes.Count;
                dialog.Hide();
                await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 0);
                await UntilAsync(() => !Get<bool>(page, "_dialogOpen"));
                Require(server.Writes.Count == before, "取消媒体选择仍写入清单");
                if (width == 1280)
                {
                    InvokeAppBar(Get<AppBarButton>(page, "ServerListAdd"));
                    await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 1);
                    var save = DialogsFor(_root.XamlRoot)[0];
                    Descendants(save).OfType<TextBox>().Single(item => item.Name == "ServerListSearchTerm").Text = "星海";
                    new ButtonAutomationPeer(Descendants(save).OfType<Button>().Single(item => item.Name == "ServerListSearch")).Invoke();
                    await UntilAsync(() => Descendants(save).OfType<ListView>().Single(item => item.Name == "ServerListSearchResults").Items.Count > 0);
                    Descendants(save).OfType<ListView>().Single(item => item.Name == "ServerListSearchResults").SelectedIndex = 0;
                    new ButtonAutomationPeer(Descendants(save).OfType<Button>().Single(item => item.Name == "PrimaryButton")).Invoke();
                    await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 0 && !page.ViewModel.Busy && server.Writes.Count == before + 1);
                    Require(page.ViewModel.NoticeSeverity == InfoBarSeverity.Success, "原生确认没有保存选择快照");
                    var card = new CardItem(source.Bind(new EmbyItem { Id = "single", Name = "单项加入", Type = EmbyItemType.Movie }), fixture.Images, 160);
                    try
                    {
                        await UntilAsync(() => !Get<bool>(page, "_dialogOpen"));
                        ItemCommands.Run(ItemCommand.AddToPlaylist, fixture.Session, fixture.Actions, page, card);
                        await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 1);
                        var single = DialogsFor(_root.XamlRoot)[0];
                        await LayoutAsync(single);
                        await UntilAsync(() => Descendants(single).OfType<ComboBox>().Any(element => element.Name == "BatchPlaylist"));
                        Descendants(single).OfType<ComboBox>().Single(element => element.Name == "BatchPlaylist").SelectedIndex = 0;
                        Require(single.IsPrimaryButtonEnabled, "单项列表选择后不能提交");
                        var previousWrites = server.Writes.Count;
                        new ButtonAutomationPeer(Descendants(single).OfType<Button>().Single(element => element.Name == "PrimaryButton")).Invoke();
                        await UntilAsync(() => DialogsFor(_root.XamlRoot).Length == 0 && server.Writes.Count == previousWrites + 1);
                    }
                    finally { card.AbandonPoster(); }
                }
                server.Editable = false;
                await page.ViewModel.ReloadAsync();
                list.SelectedIndex = 0;
                await LayoutAsync(page);
                Require(Get<AppBarButton>(page, "ServerListAdd").Visibility == Visibility.Collapsed
                    && Get<AppBarButton>(page, "ServerListRemove").Visibility == Visibility.Collapsed, "只读清单仍暴露编辑命令");
                await SaveProbeScreenAsync($"server-lists-{kind}-readonly-{width}-screen.png");
                server.Editable = true;
                await page.ViewModel.ReloadAsync();
                Require(Get<AppBarButton>(page, "ServerListAdd").Visibility == Visibility.Visible, "恢复编辑权限后入口未恢复");
                if (width == 1280)
                {
                    var item = page.ViewModel.OpenedList!.Item;
                    Require(item.Source?.Key == source.Key && ReferenceEquals(item.Source.UserStateOwner, source.UserStateOwner),
                        "清单下钻请求缺少读取时绑定的来源身份");
                    var request = new ServerListsRequest(fixture.Services, source, item);
                    Require(frame.Navigate(typeof(ServerListsPage), request), "服务器清单下钻导航失败");
                    await UntilAsync(() => frame.Content is ServerListsPage detail && detail.ViewModel.IsReady
                        && !detail.ViewModel.Busy && detail.ViewModel.OpenedList is not null);
                    Require((frame.Content as FrameworkElement)?.Tag as string == string.Empty,
                        "服务器清单下钻冒充目录目的地");
                    Require(frame.Content is ServerListsPage entered && !entered.ViewModel.NoticeOpen
                        && entered.ViewModel.Members.Count == 3 && entered.ViewModel.OpenedList?.Id == item.Id
                        && entered.ViewModel.Scope?.Key == source.Key, "清单下钻实际读取失败或身份不符");
                    frame.GoBack();
                    await UntilAsync(() => frame.Content is ServerListsPage directory && directory.ViewModel.IsReady && !directory.ViewModel.Busy);
                    Require((frame.Content as FrameworkElement)?.Tag as string == ServerListsRequest.Tag,
                        "返回服务器清单目录后根目的地未恢复");
                    Require(frame.Content is ServerListsPage returned && !returned.ViewModel.NoticeOpen
                        && returned.ViewModel.OpenedList is null && returned.ViewModel.Lists.Count == 1,
                        "返回清单目录实际读取失败或仍保留下钻状态");
                    frame.GoForward();
                    await UntilAsync(() => frame.Content is ServerListsPage detail && detail.ViewModel.IsReady
                        && !detail.ViewModel.Busy && detail.ViewModel.OpenedList is not null);
                    Require((frame.Content as FrameworkElement)?.Tag as string == string.Empty,
                        "前进服务器清单下钻后冒充目录目的地");
                    Require(frame.Content is ServerListsPage forwarded && !forwarded.ViewModel.NoticeOpen
                        && forwarded.ViewModel.Members.Count == 3 && forwarded.ViewModel.OpenedList?.Id == item.Id,
                        "前进清单下钻实际读取失败");
                }
            }
            Require(fixture.Actions.Plays == 0, "清单探针触发真实播放");
        }
        finally { (frame.Content as IShellContent)?.Release(); _root.Children.Remove(frame); }
    }

    private static ContentDialog[] DialogsFor(XamlRoot root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root)
        .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Distinct().ToArray();
    private static void InvokeAppBar(AppBarButton button) =>
        ((IInvokeProvider)new AppBarButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private sealed class ServerListsProbeServer(MediaServerKind kind)
    {
        public bool Editable { get; set; } = true;
        public List<string> Writes { get; } = [];
        public Task<HttpResponseMessage> ReplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Require(request.RequestUri!.Host.EndsWith(".invalid", StringComparison.Ordinal), "清单探针只允许匿名假服务器");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return Reply(new
            { ProductName = kind.ToString(), ServerName = "匿名清单服务器", Version = kind == MediaServerKind.Jellyfin ? "10.10.7" : "4.10.1.0" });
            if (path.EndsWith("/Playlists/playlist/Users/user", StringComparison.Ordinal)) return Reply(new { UserId = "user", CanEdit = Editable });
            if (path.EndsWith("/Users/Me", StringComparison.Ordinal) || path.EndsWith("/Users/user", StringComparison.Ordinal))
                return Reply(new { Id = "user", Name = "user", Policy = new { IsAdministrator = false } });
            if (path.EndsWith("/Views", StringComparison.Ordinal)) return Reply(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 });
            if (request.Method != HttpMethod.Get)
            {
                Require(path.Contains("/Playlists/playlist/Items", StringComparison.Ordinal), "清单探针拒绝非关系写入");
                Writes.Add(path);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (path.EndsWith("/Items/playlist", StringComparison.Ordinal)) return Reply(ListItem());
            if (path.EndsWith("/Items", StringComparison.Ordinal) || path.EndsWith("/Playlists/playlist/Items", StringComparison.Ordinal))
            {
                if (Query(request, "ParentId") == "playlist" || path.EndsWith("/Playlists/playlist/Items", StringComparison.Ordinal))
                    return Reply(new { Items = new[] { Member("same", "entry-a"), Member("same", "entry-b"), Member("other", "entry-c") }, TotalRecordCount = 3 });
                if (Query(request, "SearchTerm") is { Length: > 0 }) return Reply(new { Items = new[] { Member("match", "unused") }, TotalRecordCount = 1 });
                return Reply(new { Items = new[] { ListItem() }, TotalRecordCount = 1 });
            }
            return Task.FromResult(FakeTransport.Standard(request));
        }
        private object ListItem() => new { Id = "playlist", Name = "周末电影清单 · 重复成员与原生排序", Type = "Playlist", MediaType = "Video", CanEditItems = Editable };
        private static object Member(string id, string entry) => new { Id = id, Name = id == "same" ? "星海电影 · 两次加入" : "下一部电影", Type = "Movie", MediaType = "Video", PlaylistItemId = entry };
        private static Task<HttpResponseMessage> Reply(object value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value)) });
    }
}

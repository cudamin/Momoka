using System.Net;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task FavoritesNavigationAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var favorite = new EmbyItem
        {
            Id = "favorite-season",
            Name = "收藏的第一季",
            Type = EmbyItemType.Season,
            UserData = new EmbyUserData()
        };
        fixture.Transport.Reply = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/FavoriteItems/", StringComparison.Ordinal))
            {
                favorite.UserData.IsFavorite = request.Method == HttpMethod.Post;
                return Task.FromResult(Json(favorite.UserData));
            }
            if (Query(request, "Filters")?.Contains("IsFavorite", StringComparison.Ordinal) == true)
                return Task.FromResult(Json(new
                {
                    Items = favorite.UserData.IsFavorite ? new[] { favorite } : [],
                    TotalRecordCount = favorite.UserData.IsFavorite ? 1 : 0
                }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        fixture.Actions.FavoritesChanged = shell.RefreshFavorites;
        _root.Children.Add(shell);
        try
        {
            await (Task)Call(shell, "EnterShellAsync")!;
            await LayoutAsync(shell);
            await shell.Favorites.RefreshAsync();
            var button = Get<Button>(shell, "FavoritesButton");
            Require(button.Visibility == Visibility.Collapsed, "空收藏仍显示标题栏入口");
            ItemCommands.ToggleFavorite(fixture.Session, shell, new CardItem(favorite, fixture.Images, 170));
            await UntilAsync(() => button.Visibility == Visibility.Visible);
            Require(AutomationProperties.GetName(button) == "收藏", "收藏按钮缺少自动化名称");
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => shell.CurrentTag == LibraryRequest.FavoritesTag
                && shell.Pages.Content is LibraryPage { ViewModel.IsReady: true });
            var page = (LibraryPage)shell.Pages.Content;
            Require(page.ViewModel.Cards.Count == 1 && page.ViewModel.Cards[0].Item.Type == EmbyItemType.Season,
                "收藏按钮未打开包含季的收藏页面");
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeInspect(width, 800);
                await LayoutAsync(shell);
                var home = Get<Button>(shell, "HomeButton");
                var homeRight = home.TransformToVisual(shell).TransformPoint(new(0, 0)).X + home.ActualWidth;
                var favoriteLeft = button.TransformToVisual(shell).TransformPoint(new(0, 0)).X;
                Require(favoriteLeft >= homeRight && favoriteLeft - homeRight <= 8, "收藏入口不在主页右侧");
                Require(shell.ProbeTitleActions().Ok, "含收藏入口的标题栏布局不合法");
                await SaveReturnFrameAsync(shell, $"favorites-{width}.png");
                await SaveProbeScreenAsync($"favorites-screen-{width}.png");
            }
            ItemCommands.ToggleFavorite(fixture.Session, shell, page.ViewModel.Cards[0]);
            await UntilAsync(() => button.Visibility == Visibility.Collapsed && page.ViewModel.Cards.Count == 0);
            Require(page.ViewModel.ShowEmptyNotice && shell.CurrentTag == LibraryRequest.FavoritesTag,
                "取消最后一项收藏没有停留在空收藏页");
            await SaveReturnFrameAsync(shell, "favorites-empty.png");
            Call(shell, "ShowSignIn", new object?[] { null, false, null });
            Require(!shell.Favorites.HasItems, "退出登录未清除收藏入口");
        }
        finally
        {
            shell.Favorites.Dispose();
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
            ResizeInspect(1420, 980);
        }
    }

    private async Task FavoritesRaceAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = new FavoritesViewModel();
        vm.Attach(fixture.Session.Capture());
        var old = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (_, _) => old.Task;
        var first = vm.RefreshAsync();
        fixture.Transport.Reply = (_, _) => Task.FromResult(Items(0, 0));
        await vm.RefreshAsync();
        old.SetResult(Items(1, 1));
        await first;
        Require(!vm.HasItems, "旧收藏刷新覆盖最新空结果");

        var previous = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (_, _) => previous.Task;
        var switching = vm.RefreshAsync();
        fixture.Transport.Reply = (request, _) => Task.FromResult(FakeTransport.Standard(request));
        await fixture.SelectAsync("b.invalid");
        vm.Attach(fixture.Session.Capture());
        fixture.Transport.Reply = (_, _) => Task.FromResult(Items(0, 0));
        await vm.RefreshAsync();
        previous.SetResult(Items(1, 1));
        await switching;
        Require(!vm.HasItems, "旧账号收藏回填新账号");

        var signingOut = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (_, _) => signingOut.Task;
        var clearing = vm.RefreshAsync();
        vm.Clear();
        signingOut.SetResult(Items(1, 1));
        await clearing;
        Require(!vm.HasItems, "登录界面被迟到响应重新显示收藏按钮");
    }

    private async Task DeletePreservesListAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var items = Enumerable.Range(0, 120).Select(index => new EmbyItem
        { Id = "delete-" + index, Name = $"影片 {index:000}", Type = EmbyItemType.Movie }).ToList();
        var reads = 0;
        var writes = 0;
        var failDelete = false;
        fixture.Transport.Reply = (request, _) =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                if (failDelete) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                writes++;
                items.RemoveAll(item => request.RequestUri!.AbsolutePath.EndsWith("/" + item.Id, StringComparison.Ordinal));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (Query(request, "StartIndex") is { } offset)
            {
                reads++;
                return Task.FromResult(Json(new { Items = items.Skip(int.Parse(offset)).Take(20).ToArray(), TotalRecordCount = items.Count }));
            }
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        try
        {
            ResizeInspect(1040, 800);
            frame.Navigate(typeof(LibraryPage), new LibraryRequest { Services = fixture.Services, Title = "删除位置回归", ParentId = "library" });
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            await page.ViewModel.LoadUntilAsync(79);
            await LayoutAsync(page);
            var scroll = Get<ItemsView>(page, "CardWall").ScrollView!;
            scroll.ScrollTo(0, 1200, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            await UntilAsync(() => Math.Abs(scroll.VerticalOffset - 1200) <= 1);
            var offset = scroll.VerticalOffset;
            var before = page.ViewModel.Cards.ToArray();
            var count = reads;
            var removed = before[25];
            await SaveReturnFrameAsync(page, "delete-before.png");

            async Task ConfirmDeleteAsync(CardItem card, bool confirm)
            {
                ItemCommands.Run(ItemCommand.Delete, fixture.Session, fixture.Actions, page, card,
                    changed: () => throw new InvalidOperationException("删除不应重载第一页"), removed: page.RemovalCallback());
                ContentDialog? dialog = null;
                await UntilAsync(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
                    .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().FirstOrDefault()) is not null);
                await LayoutAsync(dialog!);
                Require(dialog!.DefaultButton == ContentDialogButton.Close, "删除确认默认按钮改变");
                var button = Descendants(dialog).OfType<Button>().Single(control => control.Name == (confirm ? "PrimaryButton" : "CloseButton"));
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Task.Delay(200);
            }

            await ConfirmDeleteAsync(removed, false);
            Require(writes == 0 && page.ViewModel.Cards.SequenceEqual(before), "取消删除改变了列表");
            failDelete = true;
            await ConfirmDeleteAsync(removed, true);
            Require(writes == 0 && page.ViewModel.Cards.SequenceEqual(before), "失败删除改变了列表");
            failDelete = false;
            await ConfirmDeleteAsync(removed, true);
            await UntilAsync(() => page.ViewModel.Cards.Count == before.Length - 1);
            await LayoutAsync(page);
            Require(writes == 1 && reads == count && page.ViewModel.Cards.SequenceEqual(before.Where(card => card != removed)),
                "删除重读分页或重建了未删除卡片");
            Require(Math.Abs(scroll.VerticalOffset - offset) <= 1, $"删除跳动：{offset} → {scroll.VerticalOffset}");
            await SaveReturnFrameAsync(page, "delete-after.png");
            await SaveProbeScreenAsync("delete-after-screen.png");
            await page.ViewModel.LoadUntilAsync(99);
            Require(page.ViewModel.Cards.Select(card => card.Item.Id).Distinct().Count() == page.ViewModel.Cards.Count,
                "删除后继续分页重复条目");
            Require(page.ViewModel.Cards.Select(card => card.Item.Id).SequenceEqual(items.Take(page.ViewModel.Cards.Count).Select(item => item.Id)),
                "删除后分页漏项");
        }
        finally
        {
            page?.Release();
            page?.ViewModel.Dispose();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }

    private async Task DeletePagingRaceAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Library();
        fixture.Transport.Reply = (_, _) => Task.FromResult(Items(20, 60));
        await vm.ReloadAsync();
        var before = vm.Cards.ToArray();
        var pending = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (_, _) => pending.Task;
        vm.LoadMore();
        Require(vm.Busy, "未发出在途分页");
        Require(await vm.RemoveAsync(before[5], vm.QueryToken), "删除未移除当前卡片");
        pending.SetResult(Items(20, 60, "stale"));
        await SettleAsync();
        Require(!vm.Busy && vm.Cards.SequenceEqual(before.Where(card => card != before[5])), "旧分页回填删除后的列表");
        var oldToken = vm.QueryToken;
        fixture.Transport.Reply = (_, _) => Task.FromResult(Items(20, 60, "new"));
        await vm.SearchAsync("new");
        Require(!await vm.RemoveAsync(before[0], oldToken) && vm.Cards.Count == 20, "旧菜单删除了新查询的卡片");
    }

    private async Task DeleteContainerAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = new LibraryViewModel();
        vm.Attach(LibraryRequest.Favorites(fixture.Services), fixture.Actions, fixture.SettingsService,
            fixture.Session, fixture.Images, fixture.Capabilities);
        var series = Series();
        var season = Season("a");
        var episode = Episode("a");
        var kept = new EmbyItem { Id = "kept", Name = "保留的影片", Type = EmbyItemType.Movie };
        fixture.Transport.Reply = (_, _) => Task.FromResult(Json(new { Items = new[] { series, season, episode, kept }, TotalRecordCount = 4 }));
        await vm.ReloadAsync();
        var original = vm.Cards.Last();
        fixture.Transport.Reply = (_, _) => Task.FromResult(Json(new { Items = new[] { kept }, TotalRecordCount = 1 }));
        Require(await vm.RemoveAsync(vm.Cards[0], vm.QueryToken) && vm.Cards.Count == 1
            && ReferenceEquals(vm.Cards[0], original) && !vm.HasMore, "整剧删除残留后代或重建了保留卡片");

        var waiting = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (_, _) => waiting.Task;
        var reload = vm.ReloadAsync();
        Require(vm.Busy && vm.Cards.Count == 1, "未进入保留旧卡片的新查询");
        fixture.Transport.Reply = (_, _) => Task.FromResult(Items(2, 2, "new-order"));
        Require(await vm.RemoveAsync(original, vm.QueryToken), "查询切换中的删除未重新读取新查询");
        waiting.SetResult(Items(1, 1, "stale"));
        await reload;
        Require(vm.Cards.Count == 2 && vm.Cards.All(card => card.Item.Id.StartsWith("new-order", StringComparison.Ordinal)),
            "查询切换中删除混入旧排序");

        fixture.Transport.Reply = (_, _) => Task.FromResult(Json(new { Items = new[] { series, season, episode, kept }, TotalRecordCount = 4 }));
        await vm.ReloadAsync();
        var delayed = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (_, _) => delayed.Task;
        var removingSeries = vm.RemoveAsync(vm.Cards[0], vm.QueryToken);
        fixture.Transport.Reply = (_, _) => Task.FromResult(Items(0, 0));
        Require(await vm.RemoveAsync(vm.Cards.Last(), vm.QueryToken), "第二次删除未重读在途容器变更");
        delayed.SetResult(Json(new { Items = new[] { kept }, TotalRecordCount = 1 }));
        await removingSeries;
        Require(vm.Cards.Count == 0 && !vm.HasMore, "交错删除取消重读后留下已删除后代");
    }

    private async Task DeleteDetailCardAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var vm = fixture.Detail();
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Similar", StringComparison.Ordinal)
            ? Task.FromResult(Items(20, 20, "similar")) : Task.FromResult(FakeTransport.Standard(request));
        await vm.ReloadAsync();
        var seasons = vm.SeasonShelf!.Cards.ToArray();
        var similar = vm.SimilarShelf!.Cards.ToArray();
        var episodes = vm.EpisodeShelf!.Cards.ToArray();
        await vm.RemoveAsync(similar[10]);
        Require(vm.SimilarShelf.Cards.SequenceEqual(similar.Where(card => card != similar[10]))
            && vm.SeasonShelf.Cards.SequenceEqual(seasons) && vm.EpisodeShelf.Cards.SequenceEqual(episodes),
            "详情页删除更多类似重建其他货架");
        await vm.RemoveAsync(episodes[0]);
        Require(vm.Episodes.Count == 0 && vm.EpisodeShelf.Cards.Count == 0 && vm.PlayTarget is null,
            "删除当前播放目标后保留了无效单集");
        Require(vm.SeasonShelf.Cards.SequenceEqual(seasons), "删除单集重建全部剧季");

        var next = Episode("a");
        next.Id = "ep-next";
        using var episodePage = new DetailViewModel();
        episodePage.Attach(DetailRequest.For(fixture.Services, Episode("a")), fixture.Actions, fixture.SettingsService,
            fixture.Session, fixture.Images);
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Episodes", StringComparison.Ordinal)
            ? Task.FromResult(Json(new { Items = new[] { Episode("a"), next } })) : Task.FromResult(FakeTransport.Standard(request));
        await episodePage.ReloadAsync();
        await episodePage.RemoveAsync(episodePage.EpisodeShelf!.Cards[0]);
        Require(episodePage.PlayTarget is null && episodePage.CurrentItem?.Id == "ep-a", "已删除单集的详情页跳播另一集");

        using var targetPage = fixture.Detail();
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Episodes", StringComparison.Ordinal)
            ? Task.FromResult(Json(new { Items = new[] { Episode("a"), next } }))
            : request.RequestUri.AbsolutePath.EndsWith("/Similar", StringComparison.Ordinal) ? Task.FromResult(Items(3, 3))
            : Task.FromResult(FakeTransport.Standard(request));
        await targetPage.ReloadAsync();
        var targetReply = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Items/ep-next", StringComparison.Ordinal)
            ? targetReply.Task : Task.FromResult(FakeTransport.Standard(request));
        var selecting = targetPage.RemoveAsync(targetPage.EpisodeShelf!.Cards[0]);
        await targetPage.RemoveAsync(targetPage.SimilarShelf!.Cards[0]);
        targetReply.SetResult(Json(next));
        await selecting;
        Require(targetPage.PlayTarget?.Id == next.Id, "删除推荐撤销了正在补选的播放目标");

        using var loadingPage = fixture.Detail();
        var seasonsReply = Pending<HttpResponseMessage>();
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Seasons", StringComparison.Ordinal)
            ? seasonsReply.Task : request.RequestUri.AbsolutePath.EndsWith("/Similar", StringComparison.Ordinal)
                ? Task.FromResult(Items(3, 3)) : Task.FromResult(FakeTransport.Standard(request));
        var loading = loadingPage.ReloadAsync();
        await UntilAsync(() => loadingPage.SimilarShelf!.Cards.Count > 0);
        await loadingPage.RemoveAsync(loadingPage.SimilarShelf!.Cards[0]);
        Require(loadingPage.Busy, "删除推荐提前结束首屏加载");
        seasonsReply.SetResult(Json(new { Items = new[] { Season("a") } }));
        await loading;
        Require(loadingPage.Episodes.Count > 0 && loadingPage.PlayTarget is not null, "删除推荐丢失首屏剧季及播放目标");
    }
}

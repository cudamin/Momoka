using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // Real SearchInput, source control and Frame lifecycle; all responses stay in FakeTransport.
    private async Task FoundationSearchReturnAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var all = Enumerable.Range(0, 40).Select(index => new EmbyItem
        { Id = "foundation-new-" + index, Name = "当前搜索 " + index, Type = EmbyItemType.Movie }).ToArray();
        var stale = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        fixture.Transport.Reply = (message, _) =>
        {
            if (Query(message, "SearchTerm") is not { } term) return Task.FromResult(FakeTransport.Standard(message));
            if (term == "旧查询")
            {
                entered.TrySetResult(true);
                return stale.Task;
            }
            var offset = int.Parse(Query(message, "StartIndex") ?? "0");
            return Task.FromResult(Json(new { Items = all.Skip(offset).Take(20).ToArray(), TotalRecordCount = all.Length }));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        var old = Task.CompletedTask;
        try
        {
            ResizeProbeClient(1100, 760);
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            var input = Get<AutoSuggestBox>(page, "SearchInput");
            input.Text = "旧查询";
            old = page.SubmitSearchAsync(input.Text);
            await entered.Task;
            var tabs = Get<SearchSourceTabs>(page, "SourceTabs");
            tabs.SelectedIndex = 1;
            input.Text = "当前查询";
            await page.SearchTypingAsync(input.Text);
            var before = fixture.Transport.Requests.Count;
            tabs.SelectedIndex = 0;
            await SettleAsync();
            Require(fixture.Transport.Requests.Count == before, "切回媒体库来源不应自动提交搜索");
            var request = (LibraryRequest)page.NavigationRequest!;
            Require(request.SearchTerm == input.Text && page.ViewModel.Cards.Count == 0,
                "切回媒体库未对齐当前输入或仍留下旧卡片");
            await page.SubmitSearchAsync(input.Text);
            stale.SetResult(Items(2, 2, "foundation-old-"));
            await old;
            await UntilAsync(() => !page.ViewModel.Busy);
            await page.ViewModel.LoadUntilAsync(39);
            FoundationProbeReadings.Search("当前查询", input.Text, request.SearchTerm!, tabs.SelectedIndex,
                page.ViewModel.QueryCurrent(page.ViewModel.QueryToken), page.ViewModel.Cards.Select(card => card.Item.Id), all.Select(item => item.Id));
            await LayoutAsync(page);
            await SaveProbeScreenAsync("foundation-search-current-screen.png");

            frame.Navigate(typeof(LibraryPage), new LibraryRequest { Services = fixture.Services, Title = "离开搜索", ParentId = "library" });
            await UntilAsync(() => frame.Content is LibraryPage other && other.IsReady && !other.ViewModel.Busy);
            frame.GoBack();
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.IsReady && !page.ViewModel.Busy);
            await page.ViewModel.LoadUntilAsync(39);
            input = Get<AutoSuggestBox>(page, "SearchInput");
            request = (LibraryRequest)page.NavigationRequest!;
            FoundationProbeReadings.Search("当前查询", input.Text, request.SearchTerm!, Get<SearchSourceTabs>(page, "SourceTabs").SelectedIndex,
                page.ViewModel.QueryCurrent(page.ViewModel.QueryToken), page.ViewModel.Cards.Select(card => card.Item.Id), all.Select(item => item.Id));
            await LayoutAsync(page);
            await SaveProbeScreenAsync("foundation-search-return-screen.png");
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                "搜索返回匿名探针产生写入或播放");
        }
        finally
        {
            stale.TrySetResult(Items(0, 0));
            await old;
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
            ResizeProbeClient(1420, 980);
        }
    }

    private async Task FoundationHomeDirectoryReturnAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Session.TakeRestoredViews();
        var views = Pending<HttpResponseMessage>();
        var entered = Pending<bool>();
        // Several real library shelves create vertical content; 24 cards in one horizontal row do not.
        var libraries = Enumerable.Range(0, 4).Select(index => new EmbyItem
        {
            Id = "foundation-library-" + index,
            Name = "迟到媒体库 " + index,
            Type = EmbyItemType.CollectionFolder,
            CollectionType = "movies"
        }).ToArray();
        fixture.Transport.Reply = (message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal))
            {
                entered.TrySetResult(true);
                return views.Task;
            }
            if (message.RequestUri.AbsolutePath.EndsWith("/Items/Latest", StringComparison.Ordinal))
                return Task.FromResult(Json(Enumerable.Range(0, 24).Select(index => new EmbyItem
                { Id = "foundation-latest-" + (Query(message, "ParentId") ?? "none") + "-" + index, Name = "最近添加 " + index, Type = EmbyItemType.Movie }).ToArray()));
            return Task.FromResult(FakeTransport.Standard(message));
        };
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        _root.Children.Add(shell);
        var entering = Task.CompletedTask;
        try
        {
            ResizeProbeClient(1100, 760);
            await LayoutAsync(shell);
            entering = (Task)Call(shell, "EnterShellAsync")!;
            await entered.Task;
            shell.GoTo("home");
            await UntilAsync(() => shell.ActiveContentReady);
            var early = (HomePage)shell.Pages.Content;
            var request = (HomeRequest)early.NavigationRequest!;
            Require(request.LibraryViews.Count == 0, "目录挂起夹具未经过空目录历史请求");
            shell.OpenChild(new LibraryRequest { Services = fixture.Services, Title = "目录到达时仍在其他页", ParentId = "library" });
            await UntilAsync(() => shell.ActiveContentReady);
            Require(shell.Pages.BackStack.Last().NavigationTransitionInfo is SuppressNavigationTransitionInfo,
                "正式 Shell 浏览导航仍把平台离场动画写进历史");
            var other = shell.Pages.Content;
            views.SetResult(Json(new { Items = libraries }));
            await entering;
            Require(ReferenceEquals(other, shell.Pages.Content), "目录迟到抢回了主页");
            Call(shell, "GoBack");
            await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage home && !home.ViewModel.Busy
                && home.ViewModel.LibraryShelf?.Cards.Count == libraries.Length);
            var returned = (HomePage)shell.Pages.Content;
            Require(Get<bool>(returned, "_shelfEntranceSuppressed"), "正式 Shell 返回主页没有抑制货架入场");
            var shelf = returned.ViewModel.LibraryShelf!;
            await LayoutAsync(returned);
            var repeater = Get<ItemsRepeater>(returned, "ShelfRepeater");
            var row = (FrameworkElement)repeater.GetOrCreateElement(returned.HomeRows.IndexOf(shelf));
            await LayoutAsync(row);
            var head = HomeFoldMotion.Find<ShelfHead>(row)!;
            FoundationProbeReadings.Home(shell.LibraryViews.Select(item => item.IdentityKey), shelf.Cards.Select(card => card.Item.IdentityKey),
                shelf.CanOpen, head.Offer, Get<Button>(head, "Scope").Visibility == Visibility.Visible);
            Require(head.DoorState.Glyph == Visibility.Collapsed && !Get<Button>(head, "Scope").Focus(FocusState.Programmatic),
                "固定媒体库标题仍有箭头或可聚焦导航按钮");
            var libraryKeys = shell.LibraryViews.Select(HomeLayout.LibraryKey).ToArray();
            var recentShelves = returned.HomeRows.OfType<CardShelf>()
                .Count(value => libraryKeys.Contains(value.HomeKey, StringComparer.Ordinal) && value.Cards.Count == 24);
            Require(recentShelves == libraries.Length, "返回主页有目录卡但没有每库最近添加货架");
            await SaveProbeScreenAsync("foundation-home-directory-return-screen.png");
            var scroll = Get<ScrollView>(returned, "Scroller");
            FoundationProbeReadings.Directory(libraries.Length, recentShelves, scroll.ScrollableHeight);
            var offset = Math.Min(180, scroll.ScrollableHeight);
            scroll.ScrollTo(0, offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
            await UntilAsync(() => Math.Abs(scroll.VerticalOffset - offset) <= 1);
            shell.OpenChild(new LibraryRequest { Services = fixture.Services, Title = "再次离页", ParentId = "library" });
            await UntilAsync(() => shell.ActiveContentReady);
            Call(shell, "GoBack");
            await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage home
                && Math.Abs(Get<ScrollView>(home, "Scroller").VerticalOffset - offset) <= 1);
            returned = (HomePage)shell.Pages.Content;
            Require(((HomeRequest)returned.NavigationRequest!).ScrollOffset == offset, "返回主页导航请求丢失滚动位置");
            Require(shell.Pages.ForwardStack.Last().NavigationTransitionInfo is SuppressNavigationTransitionInfo,
                "正式 Shell 返回后的前进记录丢失立即交接策略");
            Call(shell, "GoForward");
            await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is LibraryPage);
            Call(shell, "GoBack");
            await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage home
                && Math.Abs(Get<ScrollView>(home, "Scroller").VerticalOffset - offset) <= 1);
            returned = (HomePage)shell.Pages.Content;
            Require(Get<bool>(returned, "_shelfEntranceSuppressed"), "前进后再次返回主页重新播放了货架入场");
            await UntilAsync(() => returned.IsLoaded);
            await LayoutAsync(returned);
            AssertShelvesSettled(Get<ItemsRepeater>(returned, "ShelfRepeater"));
            await SaveProbeScreenAsync("foundation-home-scroll-return-screen.png");
            var retainedHome = shell.Pages.Content;
            Require(shell.Pages.CacheSize == 2, "浏览缓存必须有明确的两页容量上限");
            Call(shell, "ReleaseContent");
            Require(shell.Pages.Content is null && shell.Pages.CacheSize == 2
                && shell.Pages.BackStack.Count == 0 && shell.Pages.ForwardStack.Count == 0,
                "释放身份后没有清理浏览页与历史，或没有恢复缓存容量");
            shell.GoTo("home");
            await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage);
            Require(!ReferenceEquals(retainedHome, shell.Pages.Content), "身份释放后复用了旧浏览页实例");
            Require(fixture.Actions.Plays == 0 && fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                "目录返回匿名探针产生写入或播放");
        }
        finally
        {
            views.TrySetResult(Json(new { Items = Array.Empty<EmbyItem>() }));
            await entering;
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
            ResizeProbeClient(1420, 980);
        }
    }
}

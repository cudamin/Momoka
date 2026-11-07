using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        var library = new EmbyItem { Id = "foundation-library", Name = "迟到媒体库", Type = EmbyItemType.CollectionFolder, CollectionType = "movies" };
        fixture.Transport.Reply = (message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal))
            {
                entered.TrySetResult(true);
                return views.Task;
            }
            if (message.RequestUri.AbsolutePath.EndsWith("/Items/Latest", StringComparison.Ordinal))
                return Task.FromResult(Json(Enumerable.Range(0, 24).Select(index => new EmbyItem
                { Id = "foundation-latest-" + index, Name = "最近添加 " + index, Type = EmbyItemType.Movie }).ToArray()));
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
            var other = shell.Pages.Content;
            views.SetResult(Json(new { Items = new[] { library } }));
            await entering;
            Require(ReferenceEquals(other, shell.Pages.Content), "目录迟到抢回了主页");
            Call(shell, "GoBack");
            await UntilAsync(() => shell.ActiveContentReady && shell.Pages.Content is HomePage home && !home.ViewModel.Busy
                && home.ViewModel.LibraryShelf?.Cards.Count == 1);
            var returned = (HomePage)shell.Pages.Content;
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
            Require(returned.HomeRows.OfType<CardShelf>().Any(value => value.HomeKey == HomeLayout.LibraryKey(shell.LibraryViews.Single()) && value.Cards.Count == 24),
                "返回主页有目录卡但没有本库最近添加货架");
            await SaveProbeScreenAsync("foundation-home-directory-return-screen.png");
            var scroll = Get<ScrollView>(returned, "Scroller");
            Require(scroll.ScrollableHeight > 0, "主页滚动恢复夹具缺少可滚动内容");
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
            await SaveProbeScreenAsync("foundation-home-scroll-return-screen.png");
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

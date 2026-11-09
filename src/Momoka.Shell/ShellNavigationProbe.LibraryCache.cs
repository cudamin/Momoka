using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task PerformanceLibraryCacheAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Settings.Ui.PageSize = 100;
        var held = Pending<bool>();
        var holdNewLibrary = false;
        var reads = 0;
        fixture.Transport.Reply = async (message, token) =>
        {
            if (!message.RequestUri!.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal))
                return FakeTransport.Standard(message);
            reads++;
            var other = Query(message, "ParentId") == "other-library";
            if (other && holdNewLibrary) await held.Task.WaitAsync(token);
            var start = int.Parse(Query(message, "StartIndex") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var limit = int.Parse(Query(message, "Limit") ?? "100", System.Globalization.CultureInfo.InvariantCulture);
            return Json(new ItemsResult
            {
                TotalRecordCount = 400,
                Items = Enumerable.Range(start, Math.Max(0, Math.Min(limit, 400 - start)))
                    .Select(index => new EmbyItem
                    {
                        Id = (other ? "other-" : "cache-") + index,
                        Name = "匿名电影 " + index,
                        Type = EmbyItemType.Movie
                    }).ToList()
            });
        };
        var frame = new Frame { CacheSize = 2 };
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 800);
            var request = new LibraryRequest
            {
                Services = fixture.Services, Title = "匿名缓存库", Source = fixture.Session.Capture(),
                ParentId = "cache-library", CollectionType = "movies"
            };
            Require(frame.Navigate(typeof(LibraryPage), request, new SuppressNavigationTransitionInfo()), "缓存库首次导航失败");
            var page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.IsReady && page.ViewModel.Cards.Count == 100);
            await LayoutAsync(page);
            var wall = Get<ItemsView>(page, "CardWall");
            await ScrollProbeAsync(wall.ScrollView!, 0);
            await LayoutAsync(page);
            var cards = page.ViewModel.Cards.ToArray();
            var collection = page.ViewModel.Cards;
            var times = new List<double>();
            for (var visit = 0; visit < 10; visit++)
            {
                Require(frame.Navigate(typeof(Page), null, new SuppressNavigationTransitionInfo()), "缓存库中间页面导航失败");
                // 用户的返回输入发生在下一帧；先完成中间页的真实卸载/加载生命周期，
                // 避免同一次回调把尚未卸载的 ItemsView 移出又塞回。
                await LayoutAsync((Page)frame.Content);
                await UntilAsync(() => !page.IsLoaded);
                var oldQuery = page.ViewModel.QueryToken;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                frame.GoBack(new SuppressNavigationTransitionInfo());
                Require(ReferenceEquals(frame.Content, page), "普通根返回重新构造了页面");
                Require(page.ViewModel.RestoredFromCache && ReferenceEquals(collection, page.ViewModel.Cards)
                    && cards.SequenceEqual(page.ViewModel.Cards), "普通根返回重新构造了卡片");
                Require(!page.ViewModel.QueryCurrent(oldQuery), "普通根返回沿用了离页查询令牌");
                await LayoutAsync(page);
                await UntilAsync(() => page.IsLoaded && !Get<bool>(page, "_restoringAnchor")
                    && Get<object?>(page, "_pendingAnchor") is null);
                await UntilAsync(() => wall.ScrollView is { VerticalOffset: < 1, State: ScrollingInteractionState.Idle });
                await UntilAsync(() => IsRealized(page, page.ViewModel.Cards[0].Item.IdentityKey));
                times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            var anchor = page.ViewModel.Cards[0].Item.IdentityKey;
            Require(IsRealized(page, anchor) && wall.ScrollView?.VerticalOffset < 1, "顶部复用没有保持第一项");
            await SaveProbeScreenAsync("performance-library-cache-return-1280-screen.png");
            ResizeProbeClient(760, 800);
            Require(frame.Navigate(typeof(Page), null, new SuppressNavigationTransitionInfo()), "窄窗缓存中间导航失败");
            await LayoutAsync((Page)frame.Content);
            await UntilAsync(() => !page.IsLoaded);
            frame.GoBack(new SuppressNavigationTransitionInfo());
            await LayoutAsync(page);
            await UntilAsync(() => IsRealized(page, anchor) && wall.ScrollView?.VerticalOffset < 1);
            await SaveProbeScreenAsync("performance-library-cache-return-760-screen.png");

            holdNewLibrary = true;
            Call(page, "OnSettingsClicked", page, new RoutedEventArgs());
            Require(Get<Grid>(page, "SettingsOverlay").Visibility == Visibility.Visible, "换库设置浮层前置未打开");
            var other = request with { Title = "匿名另一库", ParentId = "other-library", ScrollOffset = 0,
                AnchorQueryKey = null, AnchorIndex = -1, AnchorIdentityKey = null, LoadedCount = 0 };
            Require(frame.Navigate(typeof(LibraryPage), other, new SuppressNavigationTransitionInfo()), "同类型另一库导航失败");
            Require(ReferenceEquals(frame.Content, page) && page.ViewModel.Cards.Count == 0 && !page.IsReady,
                "另一库主体未返回时仍展示旧库内容");
            Require(Get<Grid>(page, "SettingsOverlay").Visibility == Visibility.Collapsed, "另一库继承了旧库设置浮层");
            held.TrySetResult(true);
            await UntilAsync(() => page.IsReady && page.ViewModel.Cards.Count == 100);
            await LayoutAsync(page);
            Require(page.ViewModel.Cards.All(card => card.Item.Id.StartsWith("other-", StringComparison.Ordinal)), "另一库混入旧卡片");
            Require(Get<ItemsView>(page, "CardWall").ScrollView?.VerticalOffset < 1, "新请求零偏移继承了旧滚动");
            await SaveProbeScreenAsync("performance-library-cache-new-library-760-screen.png");
            await ScrollProbeAsync(wall.ScrollView!, 400);
            Require(wall.ScrollView!.VerticalOffset > 1, "深滚动淘汰前置未离开顶部");
            Require(frame.Navigate(typeof(Page), null, new SuppressNavigationTransitionInfo()), "深滚动库离页失败");
            Require(page.NavigationCacheMode == NavigationCacheMode.Disabled, "深滚动页面必须交给快照锚点恢复");
            await LayoutAsync((Page)frame.Content);
            frame.GoBack(new SuppressNavigationTransitionInfo());
            Require(!ReferenceEquals(frame.Content, page), "深滚动页面仍复用不稳定控件");
            await UntilAsync(() => frame.Content is LibraryPage restored && restored.IsReady);
            Require(frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services), new SuppressNavigationTransitionInfo()), "缓存搜索导航失败");
            var search = (LibraryPage)frame.Content;
            Require(search.ViewModel.Cards.Count == 0, "搜索页继承旧库卡片");
            Require(frame.Navigate(typeof(Page), null, new SuppressNavigationTransitionInfo()), "缓存搜索离页失败");
            Require(search.NavigationCacheMode == NavigationCacheMode.Disabled, "搜索页面没有退出类型缓存");
            frame.GoBack(new SuppressNavigationTransitionInfo());
            Require(!ReferenceEquals(frame.Content, search), "搜索页仍被类型缓存持有");
            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "performance-library-cache.json"),
                JsonSerializer.Serialize(new { visits = times.Count, layoutMilliseconds = times, reads,
                    samePage = true, sameCards = true, newRequestCleared = true, searchEvicted = true,
                    realNetwork = false, mediaLoaded = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            held.TrySetResult(true);
            if (frame.Content is IShellContent content) content.Release();
            frame.Content = null;
            frame.CacheSize = 0;
            frame.BackStack.Clear();
            frame.ForwardStack.Clear();
            _root.Children.Remove(frame);
        }

        static bool IsRealized(LibraryPage page, string identity, bool identityMatch = true)
        {
            var wall = Get<ItemsView>(page, "CardWall");
            var scroll = wall.ScrollView;
            return scroll is not null && Descendants(wall).OfType<PosterCard>().Any(poster =>
                poster.Card?.Item.IdentityKey == identity && poster.ActualHeight > 0
                && poster.TransformToVisual(scroll).TransformPoint(new Point(0, 0)).Y < scroll.ViewportHeight
                && poster.TransformToVisual(scroll).TransformPoint(new Point(0, 0)).Y + poster.ActualHeight > 0);
        }

    }
}

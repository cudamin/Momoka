using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task HomeMoviePilotLibraryAsync()
    {
        var animations = HomeMotion.PreferenceEnabled;
        using var server = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        using var fixture = CreateFixture(server);
        var artwork = await FixtureImageAsync(1280, 720);
        var movie = new EmbyItem { Id = "home-movie", Name = "离线影片", Type = EmbyItemType.Movie, BackdropImageTags = ["backdrop"] };
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/Views", StringComparison.Ordinal)) return Task.FromResult(Json(new
            {
                Items = new[] { new EmbyItem { Id = "library", Name = "电影", Type = EmbyItemType.CollectionFolder, CollectionType = "movies", ImageTags = new() { ["Primary"] = "poster" } } }
            }));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal) || path.EndsWith("/Shows/NextUp", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>() }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal)) return Task.FromResult(Json(new[] { movie }));
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(artwork) });
            return Task.FromResult(FakeTransport.Standard(request));
        };
        // Login caches the directory; install the reply before either account is restored.
        await fixture.SelectAsync();
        await fixture.SelectAsync("b.invalid");
        fixture.Settings.Ui.AnimationsEnabled = false;
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        Field(shell, "_window", _window);
        _root.Children.Add(shell);
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(1280, 900);
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            var home = (HomePage)shell.Pages.Content;
            Require(home.ViewModel.LibraryShelf?.Cards.All(card => !card.IsNavigation) != false, "未连接时显示了 MP 库卡");
            await ConnectMoviePilotAsync(fixture);
            // 2026-11-11 起主页的 MP 订阅库是两张分类导航卡（订阅·电影／订阅·电视剧），
            // 不再是旧的那一张「MP 订阅库」汇总卡；点开落在对应分类页，那一页立订阅墙。
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.Count(card => card.IsNavigation) == 2);
            var ordinary = home.ViewModel.LibraryShelf!.Cards.Where(card => !card.IsNavigation).ToArray();
            Require(ordinary.Length == 2, "两来源媒体库夹具没有生效");
            var navigation = home.ViewModel.LibraryShelf.Cards.Where(card => card.IsNavigation).ToArray();
            Require(navigation.Length == 2
                && navigation.Any(card => card.Item.Id == HomeLayout.MoviePilotMovies
                    && card.Title == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies))
                && navigation.Any(card => card.Item.Id == HomeLayout.MoviePilotSeries
                    && card.Title == HomeLayout.FixedTitle(HomeLayout.MoviePilotSeries))
                && navigation.All(card => card.Scope is null && card.Item.Source is null
                    && card.PlayVisibility == Visibility.Collapsed && card.MoreVisibility == Visibility.Collapsed),
                "主页订阅分类卡不是两张纯导航卡");
            var card = navigation.Single(item => item.Item.Id == HomeLayout.MoviePilotMovies);
            var cardRemovals = 0;
            home.ViewModel.LibraryShelf.Cards.CollectionChanged += (_, args) =>
            {
                if (args.OldItems is null) return;
                foreach (var old in args.OldItems)
                    if (navigation.Any(item => ReferenceEquals(item, old))) cardRemovals++;
            };
            await home.ViewModel.BackgroundRefreshAsync();
            Require(cardRemovals == 0, "刷新拆掉了 MP 库卡");
            foreach (var placement in new[] { HomeLibraryPlacement.BelowCarousel, HomeLibraryPlacement.OnCarousel })
                foreach (var width in new[] { 1280, 760 })
                {
                    fixture.Settings.Ui.HomeLibraryPlacement = placement;
                    ResizeProbeClient(width, 900);
                    Call(home, "UpdateLibraryOverlay");
                    await LayoutAsync(home);
                    var holder = placement == HomeLibraryPlacement.OnCarousel ? (FrameworkElement)Get<Border>(home, "LibraryOverlay") : home;
                    var strip = Descendants(holder).OfType<ShelfStrip>().First(strip => ReferenceEquals(strip.ItemsSource, home.ViewModel.LibraryShelf.Cards));
                    strip.FocusOn(home.ViewModel.LibraryShelf.Cards.Count - 1);
                    await LayoutAsync(strip);
                    var poster = Descendants(strip).OfType<PosterCard>().Single(poster => ReferenceEquals(poster.Card, card));
                    Require(poster.ActualWidth > 0 && Bounds(poster, strip).Left >= -1
                        && Bounds(poster, strip).Right <= strip.ActualWidth + 1, "MP 库卡被截断");
                    await SaveProbeScreenAsync($"mp-library-{placement}-{width}-screen.png");
                }
            var overlay = Get<Border>(home, "LibraryOverlay");
            var button = Descendants(overlay).OfType<Button>().Single(button => button.Content is PosterCard poster && ReferenceEquals(poster.Card, card));
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => shell.Pages.Content is MoviePilotSubscriptionsPage);
            var page = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await LayoutAsync(page);
            Require(shell.CurrentTag == HomeLayout.MoviePilotMovies
                && Get<TextBlock>(page, "PageTitle").Text == HomeLayout.FixedTitle(HomeLayout.MoviePilotMovies),
                "首页电影订阅卡没有落到电影分类页");
            // 分类页立墙、收横排；汇总库保留横排是另一条入口的形态，不再由主页卡进入。
            Require(Get<MoviePilotSubscriptionWallView>(page, "Wall").Visibility == Visibility.Visible
                && Get<MoviePilotSubscriptionsView>(page, "Subscriptions").Visibility == Visibility.Collapsed,
                "电影分类页没有立墙、收横排");
            await SaveProbeScreenAsync("mp-library-all-screen.png");
            Call(shell, "GoBack");
            await UntilAsync(() => shell.Pages.Content is HomePage returned && returned.IsReady);
            home = (HomePage)shell.Pages.Content;
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.Count(card => card.IsNavigation) == 2);
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.All(card => !card.IsNavigation) != false);
            await LayoutAsync(home);
            await SaveProbeScreenAsync("mp-library-hidden-screen.png");
            Require((home.ViewModel.LibraryShelf?.Cards.Count ?? 0) == ordinary.Length, "关闭 MP 改变普通媒体库");
            home.ViewModel.Open(card);
            Require(ReferenceEquals(shell.Pages.Content, home), "失效卡仍打开 MP 页");
            server.Items.Clear();
            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => home.ViewModel.LibraryShelf?.Cards.Count(card => card.IsNavigation) == 2);
            await LayoutAsync(home);
            await SaveProbeScreenAsync("mp-library-restored-screen.png");
            home.ViewModel.Open(home.ViewModel.LibraryShelf!.Cards.First(card => card.IsNavigation));
            await UntilAsync(() => shell.Pages.Content is MoviePilotSubscriptionsPage);
            page = (MoviePilotSubscriptionsPage)shell.Pages.Content;
            await LayoutAsync(page);
            // 分类页把页面级空态收起，零订阅的标题与说明由订阅墙自己那块画。
            Require(Get<StackPanel>(page, "EmptyState").Visibility == Visibility.Collapsed
                && Get<MoviePilotSubscriptionWallView>(page, "Wall").Visibility == Visibility.Visible,
                "零订阅时分类页没有把空态交给订阅墙");
            await SaveProbeScreenAsync("mp-library-empty-screen.png");
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => shell.Pages.Content is HomePage ready && ready.IsReady);
            await LayoutAsync((HomePage)shell.Pages.Content);
            Require(shell.Pages.BackStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage))
                && shell.Pages.ForwardStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionsPage)), "断线仍保留 MP 历史");
            Require(server.Writes.Count == 0, "导航产生了 MP 写入");
        }
        finally
        {
            Call(shell, "StopMoviePilotMonitoring");
            // 同侧栏探针：用完即弃的 shell 只需放开页面，清 Frame 返回栈会在导航未落地时
            // 被 WinUI 拒绝（0x800710DD），不该让清理异常盖掉本项的检查结果。
            if (shell.Pages.Content is IShellContent current) current.Release();
            shell.SignInRoot.Detach();
            Field(shell, "_window", null);
            _root.Children.Remove(shell);
            _window!.PlaybackTitleBar = false;
            _window.SetTitleBarHoles();
            HomeMotion.ApplyPreference(animations);
            ResizeInspect(1420, 980);
        }
    }
}

using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MultiSourceSignInAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var server = fixture.Session.Server!;
        var original = fixture.Session.Capture();
        var offline = new AccountProfile { Username = "待登录的另一个账号", UserId = "offline-user" };
        server.Accounts.Add(offline);
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        _root.Children.Add(shell);
        try
        {
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            await shell.SwitchProfileAsync(server, offline);
            Require(shell.SignInVisible && shell.SignInRoot.ViewModel.Username == offline.Username
                && shell.SignInRoot.ViewModel.Password.Length == 0,
                "登录指定离线账号时错误回填了默认账号或其保存密码");
            Require(original.IsAvailable && fixture.Session.Sources.Count == 1,
                "打开另一个账号的登录表单不应退出已在线账号");

            shell.OpenSignIn(server, newAccount: true);
            Require(shell.SignInRoot.ViewModel.Username.Length == 0 && shell.SignInRoot.ViewModel.Password.Length == 0,
                "添加账号仍预填已有账号的用户名或密码");
            Require(Get<Button>(shell, "HomeButton").IsEnabled, "添加账号时缺少返回主页入口");
            Call(shell, "OnHomeClicked", shell, new RoutedEventArgs());
            await UntilAsync(() => !shell.SignInVisible && shell.CurrentTag == "home" && shell.ActiveContentReady);
            Require(original.IsAvailable && fixture.Session.Sources.Count == 1,
                "取消添加账号时改变了已有来源");
        }
        finally
        {
            shell.Favorites.Dispose();
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
        }
    }

    private async Task SourceCaptionTransitionsAsync()
    {
        using var fixture = CreateFixture();
        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1280, 720);
        fixture.Settings.Ui.ShowHomeBanner = true;
        fixture.Settings.Ui.CarouselCount = HomeCarousel.MinSlots;
        fixture.Settings.Ui.CarouselSeconds = 60;
        fixture.Settings.Ui.HomeLibraryPlacement = HomeLibraryPlacement.BelowCarousel;
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var host = request.RequestUri.Host;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(path.Contains("/Images/Primary", StringComparison.Ordinal) ? poster : backdrop)
                });
            if (path.EndsWith("/Views", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = new[] { AggregateLibrary() }, TotalRecordCount = 1 }));
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
                return Task.FromResult(Json(new[] { AggregateMovie(0, host), AggregateMovie(1, host) }));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(AggregatePage(host, "", 0, 2, 2));
            if (path.EndsWith("/Shows/NextUp", StringComparison.Ordinal)) return Task.FromResult(Items(0, 0));
            if (path.EndsWith("/Items/shared-0", StringComparison.Ordinal))
                return Task.FromResult(Json(AggregateMovie(0, host)));
            if (path.EndsWith("/Items", StringComparison.Ordinal))
                return Task.FromResult(AggregatePage(host, Query(request, "SearchTerm") ?? "共享影片", 0, 2, 2));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        await fixture.SelectAsync("a.invalid", "账号甲");
        var server = fixture.Session.Server!;
        server.Name = "家庭库";
        var first = fixture.Session.Capture();
        var secondAccount = new AccountProfile { Username = "账号乙", UserId = "账号乙" };
        server.Accounts.Add(secondAccount);
        Require(fixture.Session.Sources.Count == 1, "未登录的保存账号被算成在线来源");
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        _root.Children.Add(shell);
        try
        {
            ResizeProbeClient(1280, 1000);
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            var single = await CurrentHomeAsync(1);
            await CheckHomeAsync(single, "single");
            await CheckBrowsingAsync();
            shell.GoTo("home");
            single = await CurrentHomeAsync(1);

            secondAccount.ProtectedAccessToken = PassthroughSecretProtector.Instance.Protect("offline-fixture-token");
            Require(await fixture.Session.TryRestoreAsync(server, secondAccount, CancellationToken.None), "第二个假账号恢复失败");
            await UntilAsync(() => !ReferenceEquals(shell.Pages.Content, single));
            var multiple = await CurrentHomeAsync(2);
            Require(first.IsAvailable && fixture.Session.Sources.Select(source => source.ServerProfileId).Distinct().Count() == 1,
                "同服务器第二账号登录退出了第一账号，或没有形成两个独立来源");
            await CheckHomeAsync(multiple, "same-server-two");
            await CheckBrowsingAsync();
            shell.GoTo("home");
            multiple = await CurrentHomeAsync(2);

            fixture.Session.SignOut(server, secondAccount);
            await UntilAsync(() => !ReferenceEquals(shell.Pages.Content, multiple));
            var remaining = await CurrentHomeAsync(1);
            Require(first.IsAvailable && secondAccount.AutoConnect == false, "退出第二账号改变了原账号或自动恢复偏好");
            await CheckHomeAsync(remaining, "single-restored");
            await CheckBrowsingAsync();
            var remoteServer = new ServerProfile { Name = "外网库", Url = "https://b.invalid/media" };
            var remoteAccount = new AccountProfile
            {
                Username = "账号丙",
                UserId = "账号丙",
                ProtectedAccessToken = PassthroughSecretProtector.Instance.Protect("offline-fixture-token")
            };
            remoteServer.Accounts.Add(remoteAccount);
            fixture.Settings.Servers.Add(remoteServer);
            Require(await fixture.Session.TryRestoreAsync(remoteServer, remoteAccount, CancellationToken.None), "跨服务器假账号恢复失败");
            await UntilAsync(() => !ReferenceEquals(shell.Pages.Content, remaining));
            var crossServer = await CurrentHomeAsync(2);
            Require(MediaVersionDiscovery.HasMultipleServers(fixture.Session.Sources), "跨服务器夹具没有连接两个服务器");
            await CheckHomeAsync(crossServer, "two-servers");
            await CheckBrowsingAsync();
            Require(fixture.Actions.Plays == 0, "来源文字回归意外请求播放");
        }
        finally
        {
            Get<LoadGeneration>(shell, "_identityLoads").Cancel();
            Call(shell, "StopMoviePilotMonitoring");
            Field(shell, "_session", null);
            fixture.Session.Dispose();
            await SettleAsync();
            shell.Favorites.Dispose();
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
            ResizeInspect(1420, 980);
        }

        async Task<HomePage> CurrentHomeAsync(int accounts)
        {
            try
            {
                await UntilAsync(() => !shell.SignInVisible && shell.CurrentTag == "home"
                    && shell.Pages.Content is HomePage { IsReady: true }
                    && shell.LibraryViews.Count == accounts
                    && Get<string>(shell, "_displayedSources") == (string)Call(shell, "SourcesStamp")!);
            }
            catch (TimeoutException error)
            {
                var home = shell.Pages.Content as HomePage;
                throw new InvalidOperationException($"等待 {accounts} 账号主页失败：page={shell.Pages.Content?.GetType().Name}, "
                    + $"tag={shell.CurrentTag}, ready={home?.IsReady}, signedOut={shell.SignInVisible}, "
                    + $"libraries={shell.LibraryViews.Count}, cards={home?.ViewModel.LibraryShelf?.Cards.Count}, "
                    + $"slides={home?.ViewModel.Slides.Count}, sources={fixture.Session.Sources.Count}, "
                    + $"stamp={Get<string>(shell, "_displayedSources")}, current={(string)Call(shell, "SourcesStamp")!}", error);
            }
            var ready = (HomePage)shell.Pages.Content;
            Require(ready.ViewModel.LibraryShelf?.Cards.Count == accounts,
                $"主页来源卡片数量错误：expected={accounts}, actual={ready.ViewModel.LibraryShelf?.Cards.Count}");
            Require(ready.ViewModel.Slides.Count == Math.Min(accounts * 2, HomeCarousel.MinSlots),
                $"轮播数量与合法张数不符：accounts={accounts}, actual={ready.ViewModel.Slides.Count}");
            return ready;
        }

        async Task CheckHomeAsync(HomePage home, string stage)
        {
            var sources = fixture.Session.Sources.ToArray();
            var libraries = Get<Dictionary<string, LibraryRequest>>(shell, "_libraries").Values.ToArray();
            Require(libraries.Length == sources.Length && libraries.All(library => library.Source is not null
                && library.Title == "电影"), "媒体库导航标题不应显示服务器名称或用户名");
            foreach (var width in new[] { 1280, 760 })
            {
                ResizeProbeClient(width, 1000);
                await LayoutAsync(home);
                await Task.WhenAll(home.ViewModel.LibraryShelf!.Cards.Select(card => card.EnsurePosterAsync()));
                var banner = Get<HomeBanner>(home, "Banner");
                await UntilAsync(() => banner.Current?.Picture is not null);
                await Task.Delay(1000);
                Require(Get<FrameworkElement>(banner, "TitleRow").Opacity > 0.99
                    && Get<FrameworkElement>(banner, "CaptionRow").Opacity > 0.99,
                    "首页截图不能在轮播文字仍然淡入时拍摄");
                AssertAggregateLabels(home, sources);
                await SaveProbeScreenAsync($"aggregate-home-{stage}-{width}-screen.png");

                // 多账号时再往下滚一屏：媒体库卡片名称和「最近添加」货架标题的服务器名前缀要留下实屏证据。
                if (sources.Length > 1 && width == 1280)
                {
                    var scroller = Get<ScrollView>(home, "Scroller");
                    scroller.ScrollTo(0, 620, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                    await LayoutAsync(home);
                    await Task.Delay(200);
                    await SaveProbeScreenAsync($"aggregate-home-{stage}-{width}-labels-screen.png");
                    scroller.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled));
                    await LayoutAsync(home);
                }
            }
        }

        async Task CheckBrowsingAsync()
        {
            shell.OpenSearch();
            await UntilAsync(() => shell.Pages.Content is LibraryPage);
            var search = (LibraryPage)shell.Pages.Content;
            await search.ViewModel.SearchAsync("共享影片");
            await LayoutAsync(search);
            var sources = fixture.Session.Sources.ToArray();
            AssertAggregateLabels(search, sources);
            foreach (var source in sources)
                search.ViewModel.RememberSearch(search.ViewModel.Cards.First(card => card.Scope?.Key == source.Key).Item);
            await search.ViewModel.SearchAsync("");
            Require(search.ViewModel.RecentSearches.Count == sources.Length
                && search.ViewModel.RecentSearches.All(row => row.TypeLabel == "电影"),
                "最近搜索不应追加服务器名称或用户名");
            shell.GoTo("home");
            await CurrentHomeAsync(sources.Length);
            foreach (var source in sources)
            {
                Require(shell.TryOpenLibrary("shared-library", source), "媒体库导航没有保留来源");
                await UntilAsync(() => shell.Pages.Content is LibraryPage { IsReady: true });
                var library = (LibraryPage)shell.Pages.Content;
                await LayoutAsync(library);
                Require(library.ViewModel.Cards.All(card => card.Scope?.Key == source.Key), "媒体库内容路由到了另一账号");
                AssertAggregateLabels(library, source);
                var selected = library.ViewModel.Cards[0].Item;
                shell.OpenDetail(DetailRequest.For(fixture.Services, selected));
                await UntilAsync(() => shell.Pages.Content is DetailPage { IsReady: true });
                var detail = (DetailPage)shell.Pages.Content;
                await UntilAsync(() => detail.ViewModel.Sources.Count == sources.Length * 2);
                var versions = Get<IReadOnlyList<MediaVersion>>(detail.ViewModel, "_versions");
                var showServer = MediaVersionDiscovery.HasMultipleServers(sources);
                Require(detail.ViewModel.Sources.All(row =>
                {
                    var owner = versions.Single(version => ReferenceEquals(version.Source, row.Source)).Item.Source!;
                    var expected = ItemDetail.SourceLabel(row.Source) + (showServer ? $" · {owner.ServerName}" : "");
                    return row.Text == expected && !row.Text.Contains(owner.Connection.UserName, StringComparison.Ordinal);
                }), "版本选择只应在多服务器时追加末尾服务器名，不能显示用户名");
                if (source.Key == sources[0].Key)
                {
                    var picker = Get<EgPicker>(detail, detail.ViewModel.ColumnPickersVisibility == Visibility.Visible
                        ? "ColumnSourcePicker" : "SourcePicker");
                    await LayoutAsync(detail);
                    Require(picker.ItemsSource == detail.ViewModel.Sources && picker.DisplayMemberPath == "Text",
                        "版本选择没有绑定带来源规则的文本");
                    picker.IsDropDownOpen = true;
                    await UntilAsync(() => picker.PopupPart?.IsOpen == true);
                    await Task.Delay(250);
                    await SaveProbeScreenAsync($"aggregate-version-{(showServer ? "multi-server" : sources.Length > 1 ? "same-server" : "single")}-screen.png");
                    picker.IsDropDownOpen = false;
                }
                Require(new[] { detail.ViewModel.EpisodeShelf, detail.ViewModel.SeasonShelf, detail.ViewModel.SimilarShelf }
                    .All(shelf => shelf is not null && shelf.Cards.All(card => card.Subtitle == card.Item.CardSubtitle)),
                    "详情货架不应追加来源文字");
                shell.GoTo("home");
                await CurrentHomeAsync(sources.Length);
            }
        }
    }

    private async Task MultiSourceDefaultExpiryAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var originalServer = fixture.Session.Server!;
        var originalAccount = fixture.Session.Account!;
        var original = fixture.Session.Capture();
        await fixture.SelectAsync("b.invalid");
        var remaining = fixture.Session.Capture();
        fixture.Session.SelectSource(original);
        foreach (var source in fixture.Session.Sources) fixture.Session.TakeRestoredViews(source);
        fixture.Transport.Reply = (request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal))
                return Task.FromResult(FakeTransport.Standard(request));
            return Task.FromResult(request.RequestUri.Host == "a.invalid"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(new
                {
                    Items = new[] { new EmbyItem { Id = "remaining-library", Name = "仍在线的媒体库",
                    Type = EmbyItemType.CollectionFolder, CollectionType = "movies" } }
                }));
        };
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        _root.Children.Add(shell);
        try
        {
            await LayoutAsync(shell);
            await shell.SwitchProfileAsync(originalServer, originalAccount);
            await UntilAsync(() => !shell.SignInVisible && shell.CurrentTag == "home" && shell.ActiveContentReady);
            Require(!original.IsAvailable && remaining.IsAvailable && remaining.IsCurrent,
                "默认账号失效时未保留另一账号作为默认来源");
            Require(shell.LibraryViews.Count == 1 && shell.LibraryViews[0].Id == "remaining-library"
                && shell.LibraryViews[0].Source?.Key == remaining.Key,
                "切换处理中丢掉来源变更通知，未恢复到仍在线账号的主页");
        }
        finally
        {
            shell.Favorites.Dispose();
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
        }
    }
}

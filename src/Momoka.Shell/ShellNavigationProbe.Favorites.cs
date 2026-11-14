using System.Collections.Concurrent;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task FavoritesNavigationAsync()
    {
        static void Invoke(Button button)
        {
            var provider = FrameworkElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke)
                as IInvokeProvider ?? throw new InvalidOperationException("标题栏按钮没有 UIA 调用入口");
            provider.Invoke();
        }

        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Settings.Ui.ShowHomeBanner = false;
        var bannerImage = await FixtureImageAsync(1280, 720);
        var bannerItem = new EmbyItem
        {
            Id = "home-navigation-banner",
            Name = "首页轮播",
            Type = EmbyItemType.Movie,
            BackdropImageTags = ["backdrop"],
            Overview = "标题栏左上统一留主页、收藏、设置、搜索、返回、前进，轮播图保持贴顶。"
        };
        fixture.Session.Capture().Bind(bannerItem);
        var favorite = new EmbyItem
        {
            Id = "favorite-season",
            Name = "收藏的第一季",
            Type = EmbyItemType.Season,
            UserData = new EmbyUserData()
        };
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bannerImage) });
            if (path.EndsWith("/Items/Latest", StringComparison.Ordinal))
                return Task.FromResult(Json(new[] { bannerItem }));
            if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<EmbyItem>(), TotalRecordCount = 0 }));
            if (path.Contains("/FavoriteItems/", StringComparison.Ordinal))
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
        fixture.Actions.TitleStripChanged = shell.SetTitleStrip;
        _root.Children.Add(shell);
        try
        {
            await (Task)Call(shell, "EnterShellAsync")!;
            await LayoutAsync(shell);
            await shell.Favorites.RefreshAsync();
            var homeButton = Get<Button>(shell, "HomeButton");
            var favoritesButton = Get<Button>(shell, "FavoritesButton");
            var settingsButton = Get<Button>(shell, "SettingsButton");
            var searchButton = Get<Button>(shell, "SearchButton");
            var backButton = Get<Button>(shell, "BackButton");
            await LayoutAsync(shell);

            // 首页、空收藏（2026-11-12 晚的规矩）：收藏没有内容可去、收起；主页走不动、暗着；**返回在主页上整个
            // 收起**（用户令「主页不显示返回按钮」）；前进键退役；沉浸模式恢复左上放大镜，居中框收起。
            var box = Get<Button>(shell, "TitleSearchBox");
            Require(favoritesButton.Visibility == Visibility.Collapsed, "没有收藏时收藏按钮没有收起");
            Require(homeButton.Visibility == Visibility.Visible && backButton.Visibility == Visibility.Collapsed,
                "主页上主页键不在、或者返回没有收起");
            Require(settingsButton.Visibility == Visibility.Visible, "设置没在左上这一排");
            Require(box.Visibility == Visibility.Collapsed && searchButton.Visibility == Visibility.Visible,
                $"沉浸搜索入口不对：搜索框 {box.Visibility}、放大镜 {searchButton.Visibility}");
            Require(!homeButton.IsEnabled && !backButton.IsEnabled, "首页上主页或返回没有变暗");
            Require(Left(homeButton, shell) < Left(settingsButton, shell),
                "标题栏这一排的次序不是 主页 · 设置");
            Require(shell.Pages.Margin.Top == 0, "标题栏又给页面留了旧的顶部占位");
            Require(shell.ProbeTitleActions().Ok, "首页标题栏布局不合法" + shell.ProbeTitleActions().Detail);
            Require(Left(homeButton, shell) <= 24 && Top(homeButton, shell) + homeButton.ActualHeight <= HomePage.ChromeHeight,
                "标题栏那一排没有从左上角起");
            await SaveProbeScreenAsync("favorites-star-home-empty-screen.png");

            // 有收藏之后爱心露面，并且站在主页与设置之间。
            ItemCommands.ToggleFavorite(fixture.Session, shell, new CardItem(favorite, fixture.Images, 170));
            await UntilAsync(() => shell.Favorites.HasItems);
            await LayoutAsync(shell);
            Require(favoritesButton.Visibility == Visibility.Visible, "有收藏后收藏按钮没有出现");
            Require(Left(homeButton, shell) < Left(favoritesButton, shell)
                && Left(favoritesButton, shell) < Left(settingsButton, shell), "收藏没有排在主页与设置之间");
            Require(shell.ProbeTitleActions().Ok, "有收藏时标题栏布局不合法" + shell.ProbeTitleActions().Detail);
            await SaveProbeScreenAsync("favorites-star-home-screen.png");

            // 爱心进收藏页：聚合在线账号的收藏，季也在里面。
            Invoke(favoritesButton);
            await UntilAsync(() => shell.CurrentTag == LibraryRequest.FavoritesTag
                && shell.Pages.Content is LibraryPage { ViewModel.IsReady: true });
            var page = (LibraryPage)shell.Pages.Content;
            Require(page.ViewModel.Cards.Count == 1 && page.ViewModel.Cards[0].Item.Type == EmbyItemType.Season,
                "收藏按钮没有打开包含季的收藏页面");
            Require(page.ViewModel.FavoriteAccountFilterVisibility == Visibility.Collapsed
                && Get<Button>(page, "FavoriteAccountButton").Visibility == Visibility.Collapsed,
                "单账号收藏页仍显示账号筛选");
            Require(backButton.Visibility == Visibility.Visible && backButton.IsEnabled,
                "收藏页的返回没有露出来或按不动");
            Require(shell.ProbeTitleActions().Ok, "收藏页标题栏布局不合法" + shell.ProbeTitleActions().Detail);
            foreach (var width in new[] { 1240, 760 })
            {
                ResizeInspect(width, 800);
                await LayoutAsync(shell);
                var origin = favoritesButton.TransformToVisual(shell).TransformPoint(new(0, 0));
                Require(origin.Y >= 0 && origin.Y + favoritesButton.ActualHeight <= HomePage.ChromeHeight,
                    "收藏页的收藏按钮没有落在标题栏");
                Require(shell.ProbeTitleActions().Ok, "收藏页标题栏自检失败" + shell.ProbeTitleActions().Detail);
                await SaveReturnFrameAsync(shell, $"favorites-{width}.png");
                await SaveProbeScreenAsync($"favorites-screen-{width}.png");
            }

            // 取消最后一项：留在空收藏页（旧规矩不变），爱心随之收起。
            ItemCommands.ToggleFavorite(fixture.Session, shell, page.ViewModel.Cards[0]);
            await UntilAsync(() => !shell.Favorites.HasItems && page.ViewModel.Cards.Count == 0);
            await LayoutAsync(shell);
            Require(favoritesButton.Visibility == Visibility.Collapsed, "取消最后一项收藏后爱心没有收起");
            Require(page.ViewModel.ShowEmptyNotice && shell.CurrentTag == LibraryRequest.FavoritesTag,
                "取消最后一项收藏没有停留在空收藏页");
            Require(shell.ProbeTitleActions().Ok, "空收藏页标题栏布局不合法" + shell.ProbeTitleActions().Detail);
            await SaveReturnFrameAsync(shell, "favorites-empty.png");
            await SaveProbeScreenAsync("favorites-empty-screen.png");

            // 空态没有爱心可点，用返回离开；主页照旧不给页面留旧导航空间。
            Invoke(backButton);
            await UntilAsync(() => shell.CurrentTag == "home");
            await LayoutAsync(shell);
            Require(shell.Pages.Margin.Top == 0, "无轮播主页缓存返回残留旧导航空间");
            var home = (HomePage)shell.Pages.Content;
            var banner = Get<HomeBanner>(home, "Banner");
            fixture.Settings.Ui.ShowHomeBanner = true;
            await home.ViewModel.ApplyLayoutAsync();
            await UntilAsync(() => banner.FrontPicture is not null);
            ItemCommands.ToggleFavorite(fixture.Session, shell, new CardItem(favorite, fixture.Images, 170));
            await UntilAsync(() => shell.Favorites.HasItems);
            foreach (var width in new[] { 1280, 760, 600 })
            {
                ResizeProbeClient(width, 820);
                await LayoutAsync(shell);
                var center = banner.TransformToVisual(shell).TransformPoint(new(0, 0));
                // 2026-11-12 晚：轮播是四边留白的圆角大图（外圈光晕 2026-11-13 已删），上沿落在标题栏底下 20 处
                // （BannerZone 的 36,20,13,0）。
                Require(shell.Pages.Margin.Top == 0 && Math.Abs(center.Y - (HomePage.ChromeHeight + 20)) < 1,
                    $"轮播没有落在标题栏底下 20 处（实测 {center.Y:0}）");
                Require(favoritesButton.Visibility == Visibility.Visible && shell.ProbeTitleActions().Ok,
                    "轮播页的标题栏那一排没有正确排列");
                await SaveProbeScreenAsync($"home-favorites-banner-{width}-screen.png");
            }
            fixture.Settings.Ui.ShowHomeBanner = false;
            await home.ViewModel.ApplyLayoutAsync();
            await LayoutAsync(shell);
            Require(shell.Pages.Margin.Top == 0, "关闭轮播后残留旧导航留白");
            await SaveProbeScreenAsync("home-favorites-no-banner-screen.png");

            // 历史：收藏页 → 返回回首页（返回在收藏页是亮的）；爱心全程都在。
            Invoke(favoritesButton);
            await UntilAsync(() => shell.CurrentTag == LibraryRequest.FavoritesTag);
            await LayoutAsync(shell);
            Invoke(backButton);
            await UntilAsync(() => shell.CurrentTag == "home");
            await LayoutAsync(shell);
            Require(favoritesButton.Visibility == Visibility.Visible, "历史返回首页后爱心丢了（收藏还在）");
            Require(!backButton.IsEnabled, "回到首页后返回按钮仍按得动");
            Require(backButton.Visibility == Visibility.Collapsed, "回到首页后返回按钮没有收起");
            // 前进键 2026-11-12 晚退役（用户选「前进键退役」）：那一步改由 Alt+→ / 鼠标侧键走同一条 GoForward。
            Call(shell, "GoForward");
            await UntilAsync(() => shell.CurrentTag == LibraryRequest.FavoritesTag);

            // 登录卡：收藏清空、爱心收起，外壳那一排照旧留着；返回在登录卡上收起（没有可退的地方）。
            Call(shell, "ShowSignIn", new object?[] { null, false, null });
            Require(favoritesButton.Visibility == Visibility.Collapsed, "退出登录没有收起爱心");
            Require(homeButton.Visibility == Visibility.Visible
                && settingsButton.Visibility == Visibility.Visible
                && backButton.Visibility == Visibility.Collapsed, "登录卡上标题栏那一排不对");
            fixture.Session.SignOut();
            fixture.Transport.Reply = new PlexStateProbeServer(false).ReplyAsync;
            await fixture.SelectAsync("home-navigation-plex.invalid", kind: MediaServerKind.Plex);
            await (Task)Call(shell, "EnterShellAsync")!;
            await LayoutAsync(shell);
            Require(favoritesButton.Visibility == Visibility.Collapsed
                && shell.Pages.Margin.Top == 0, "纯 Plex 仍显示收藏爱心或残留占位");
            await SaveProbeScreenAsync("home-favorites-plex-screen.png");
            fixture.Session.SignOut();
            fixture.Transport.Reply = (request, _) => Query(request, "Filters")?.Contains("IsFavorite", StringComparison.Ordinal) == true
                ? Task.FromResult(Json(new { Items = new[] { favorite }, TotalRecordCount = 1 }))
                : Task.FromResult(FakeTransport.Standard(request));
            await fixture.SelectAsync("home-navigation-restored.invalid");
            await (Task)Call(shell, "EnterShellAsync")!;
            await shell.Favorites.RefreshAsync();
            await LayoutAsync(shell);
            Require(favoritesButton.Visibility == Visibility.Visible, "恢复有收藏的来源后没有恢复爱心");
            await SaveProbeScreenAsync("home-favorites-restored-screen.png");
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

    /// <summary>两个在线账号的收藏直接聚在一页；分账号筛选只问选中账号，迟到响应不覆盖新筛选。</summary>
    private async Task FavoriteAccountsAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync("a.invalid", "账号甲");
        var first = fixture.Session.Capture();
        await fixture.SelectAsync("b.invalid", "账号乙");
        var second = fixture.Session.Capture();
        var firstItem = new EmbyItem
        {
            Id = "favorite-a",
            Name = "甲账号的收藏",
            Type = EmbyItemType.Movie,
            UserData = new EmbyUserData { IsFavorite = true }
        };
        var secondItem = new EmbyItem
        {
            Id = "favorite-b",
            Name = "乙账号的收藏",
            Type = EmbyItemType.Movie,
            UserData = new EmbyUserData { IsFavorite = true }
        };
        var reads = new ConcurrentQueue<(string Host, int Offset)>();
        var secondReply = Pending<HttpResponseMessage>();
        var holdSecond = false;
        fixture.Transport.Reply = (request, _) =>
        {
            if (Query(request, "Filters")?.Contains("IsFavorite", StringComparison.Ordinal) == true)
            {
                var host = request.RequestUri!.Host;
                reads.Enqueue((host, int.Parse(Query(request, "StartIndex") ?? "0")));
                if (host == "b.invalid" && holdSecond) return secondReply.Task;
                var item = host == "a.invalid" ? firstItem : secondItem;
                return Task.FromResult(Json(new { Items = new[] { item }, TotalRecordCount = 1 }));
            }
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var frame = new Frame();
        _root.Children.Add(frame);
        LibraryPage? page = null;
        try
        {
            ResizeProbeClient(1280, 900);
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Favorites(fixture.Services));
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
            Require(page.ViewModel.Cards.Count == 2
                && page.ViewModel.Cards.Select(card => card.Item.Id).OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(new[] { "favorite-a", "favorite-b" })
                && page.ViewModel.Cards.Select(card => card.Scope?.Key).Distinct().Count() == 2,
                "收藏页没有直接聚合两个在线账号的收藏");
            Require(page.ViewModel.FavoriteAccountFilterVisibility == Visibility.Visible, "多账号收藏页没有显示账号筛选");
            Require(page.ViewModel.FavoriteAccountLabel == "全部账号", "账号筛选默认不是全部账号");
            await LayoutAsync(page);
            var button = Get<Button>(page, "FavoriteAccountButton");
            Require(button.Visibility == Visibility.Visible, "账号筛选按钮没有显示");
            var entries = page.BuildFavoriteAccountMenu();
            Require(string.Join(',', entries.Select(entry => entry.Label)) == "全部账号,a.invalid · 账号甲,b.invalid · 账号乙",
                "账号筛选菜单顺序或文字不对");
            Require(entries.Count(entry => entry.IsChecked) == 1 && entries[0].IsChecked, "账号筛选默认勾错了");
            page.ShowFavoriteAccountMenu();
            await LayoutAsync(page);
            await SaveProbeScreenAsync("favorites-accounts-menu-screen.png");
            ((Flyout)button.Flyout).Hide();

            var before = reads.Count;
            await page.ViewModel.ChooseFavoriteAccountAsync(second.Key);
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy && page.ViewModel.Cards.Count == 1);
            Require(page.ViewModel.Cards[0].Item.Id == "favorite-b" && page.ViewModel.Cards[0].Scope?.Key == second.Key,
                "按账号筛选后仍混入其他账号");
            Require(page.ViewModel.FavoriteAccountLabel == "b.invalid · 账号乙", "账号筛选按钮没有跟随选中账号");
            Require(page.BuildFavoriteAccountMenu().Single(entry => entry.IsChecked).Key == second.Key,
                "账号筛选菜单没有勾中选中账号");
            Require(reads.Skip(before).All(read => read.Host == "b.invalid"), "按账号筛选后仍向其他来源发请求");
            await LayoutAsync(page);
            await SaveProbeScreenAsync("favorites-accounts-filtered-screen.png");

            await page.ViewModel.ChooseFavoriteAccountAsync("missing");
            Require(page.ViewModel.Cards.Count == 1 && page.ViewModel.FavoriteAccountLabel == "b.invalid · 账号乙",
                "未知账号改变了收藏筛选");

            reads.Clear();
            holdSecond = true;
            var switching = page.ViewModel.ChooseFavoriteAccountAsync(null);
            await UntilAsync(() => reads.Any(read => read.Host == "b.invalid"));
            var narrowed = page.ViewModel.ChooseFavoriteAccountAsync(first.Key);
            holdSecond = false;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy && page.ViewModel.Cards.Count == 1
                && page.ViewModel.Cards[0].Item.Id == "favorite-a");
            secondReply.SetResult(Json(new { Items = new[] { secondItem }, TotalRecordCount = 1 }));
            await switching;
            await narrowed;
            Require(page.ViewModel.Cards.Count == 1 && page.ViewModel.Cards[0].Item.Id == "favorite-a"
                && page.ViewModel.FavoriteAccountLabel == "a.invalid · 账号甲", "迟到的另一账号收藏覆盖了筛选结果");

            await page.ViewModel.ChooseFavoriteAccountAsync(null);
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy && page.ViewModel.Cards.Count == 2);
            Require(page.ViewModel.FavoriteAccountLabel == "全部账号", "回到全部账号后按钮文字没有复原");
            Require(first.IsAvailable && second.IsAvailable, "收藏筛选改变了在线来源");
            Require(fixture.Transport.Requests.All(value => value.StartsWith("GET ", StringComparison.Ordinal)),
                "收藏聚合验证产生了写操作");
        }
        finally
        {
            secondReply.TrySetResult(Json(new { Items = new[] { secondItem }, TotalRecordCount = 1 }));
            page?.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
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
                $"删除重读分页或重建了未删除卡片：writes={writes}，reads={count}→{reads}，rows={before.Length}→{page.ViewModel.Cards.Count}，retained={page.ViewModel.Cards.SequenceEqual(before.Where(card => card != removed))}，offset={offset:0}→{scroll.VerticalOffset:0}，extent={scroll.ExtentHeight:0}，viewport={scroll.ViewportHeight:0}");
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
        var items = Enumerable.Range(0, 60).Select(index => new EmbyItem
        { Id = "race-" + index, Name = $"影片 {index:000}", Type = EmbyItemType.Movie }).ToList();
        var pending = Pending<HttpResponseMessage>();
        var delayNextPage = false;
        var delayedReads = 0;
        fixture.Transport.Reply = (request, _) =>
        {
            var offset = int.Parse(Query(request, "StartIndex") ?? "0");
            if (delayNextPage && offset == 20)
            {
                delayNextPage = false;
                delayedReads++;
                return pending.Task;
            }
            return Task.FromResult(Json(new { Items = items.Skip(offset).Take(20).ToArray(), TotalRecordCount = items.Count }));
        };
        await vm.ReloadAsync();
        var before = vm.Cards.ToArray();
        var stalePage = items.Skip(20).Take(20).ToArray();
        delayNextPage = true;
        vm.LoadMore();
        try
        {
            await UntilAsync(() => delayedReads == 1);
            Require(vm.Busy, "未发出在途分页");
            items.RemoveAt(5);
            // A search page refreshes from zero after deletion. Only the old append request waits;
            // its replacement must read the current catalog and refill the first twenty results.
            Require(await vm.RemoveAsync(before[5], vm.QueryToken), "删除未移除当前卡片");
            pending.SetResult(Json(new { Items = stalePage, TotalRecordCount = 60 }));
            await SettleAsync();
            Require(!vm.Busy && vm.Cards.Count == 20
                && vm.Cards.Take(19).SequenceEqual(before.Where(card => card != before[5]))
                && vm.Cards.Select(card => card.Item.Id).SequenceEqual(items.Take(20).Select(item => item.Id))
                && Get<int>(vm, "_total") == 59, "旧分页回填、删除后补位错误或重建了保留卡片");
        }
        finally { pending.TrySetCanceled(); }
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
        Require(episodePage.PlayTarget is null && episodePage.CurrentItem is null && episodePage.TitleLink is null
            && episodePage.EpisodeShelf!.Cards.Count == 0 && episodePage.NoticeMessage?.Contains("已从服务器移除", StringComparison.Ordinal) == true,
            "删除当前单集仍保留主体、播放目标、标题操作或旧货架");

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

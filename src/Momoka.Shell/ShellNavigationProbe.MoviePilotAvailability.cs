using CommunityToolkit.WinUI.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private static async Task ConnectMoviePilotAsync(Fixture fixture)
    {
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.Url = "https://moviepilot.invalid:3001";
        fixture.Settings.MoviePilot.Username = "fixture";
        new MoviePilotCredentials(PassthroughSecretProtector.Instance).SetPassword(fixture.Settings.MoviePilot, "fixture");
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        Require(await service.RefreshAvailabilityAsync(), "假 MoviePilot 连接失败");
    }

    private async Task MoviePilotAvailabilityAsync()
    {
        var mp = new SearchProbeServer();
        using var fixture = CreateFixture(mp);
        await fixture.SelectAsync();
        fixture.Settings.MoviePilot.Enabled = true;
        fixture.Settings.MoviePilot.LastConnected = DateTimeOffset.Now;
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services));
            var page = (LibraryPage)frame.Content;
            await LayoutAsync(page);
            var tabs = Get<Segmented>(page, "SourceTabs");
            Require(tabs.Visibility == Visibility.Collapsed, "只有开关或历史成功时仍显示入口");
            await SaveProbeScreenAsync("moviepilot-unverified.png");
            await ConnectMoviePilotAsync(fixture);
            await UntilAsync(() => tabs.Visibility == Visibility.Visible);
            tabs.SelectedIndex = 1;
            await UntilAsync(() => Get<MoviePilotSearchPanel>(page, "MoviePilotPanel").Visibility == Visibility.Visible);
            mp.Offline = true;
            Require(!await service.RefreshAvailabilityAsync(), "断线仍被视为可用");
            await UntilAsync(() => tabs.Visibility == Visibility.Collapsed && tabs.SelectedIndex == 0);
            Require(Get<MoviePilotSearchPanel>(page, "MoviePilotPanel").Visibility == Visibility.Collapsed
                && Get<Grid>(page, "EmbyContent").Visibility == Visibility.Visible, "断线后未恢复媒体库内容");
            await SaveProbeScreenAsync("moviepilot-offline.png");
            mp.Offline = false;
            Require(await service.RefreshAvailabilityAsync(), "恢复后仍不可用");
            await UntilAsync(() => tabs.Visibility == Visibility.Visible);
            await SaveProbeScreenAsync("moviepilot-connected.png");
            tabs.SelectedIndex = 2;
            var versions = Get<MoviePilotVersionsView>(page, "MoviePilotResources");
            var picker = Get<MoviePilotSitePicker>(versions, "SearchSitePicker");
            var chooseSites = Get<DropDownButton>(picker, "ChooseSites");
            chooseSites.Flyout.ShowAt(chooseSites);
            await UntilAsync(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).Count > 0);
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => tabs.Visibility == Visibility.Collapsed && tabs.SelectedIndex == 0);
            Require(Get<MoviePilotVersionsView>(page, "MoviePilotResources").Visibility == Visibility.Collapsed,
                "关闭设置后资源面板没有收起");
            await UntilAsync(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).Count == 0);
            fixture.Settings.MoviePilot.Enabled = true;
            await LayoutAsync(page);
            Require(tabs.Visibility == Visibility.Collapsed, "重新开启但未验证时复用了旧连接");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            _root.Children.Remove(frame);
        }
    }

    private async Task MoviePilotMenusAsync()
    {
        using var fixture = CreateFixture(new SubscriptionProbeServer());
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        var anchor = new Button { Content = "离线菜单验证", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _root.Children.Add(shell);
        _root.Children.Add(anchor);
        var subscriptions = new MoviePilotSubscriptionsViewModel();
        MenuFlyout? subscriptionMenu = null;
        try
        {
            await LayoutAsync(anchor);
            var card = new CardItem(Series(), fixture.Images, 170);
            ItemCommands.Show(fixture.Session, shell, anchor, card, null);
            await UntilAsync(() => MenuItems().Count(item => item.Tag is ItemCommand.MoviePilotVersions or ItemCommand.MoviePilotReorganize) == 2);
            await SaveProbeScreenAsync("moviepilot-menu-connected.png");
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => MenuItems().Length == 0);
            Require(ItemCommands.Build(fixture.Session, shell, anchor, card).Items.OfType<MenuFlyoutItem>()
                .All(item => item.Tag is not (ItemCommand.MoviePilotVersions or ItemCommand.MoviePilotReorganize)), "关闭后菜单仍有 MP 入口");

            fixture.Settings.MoviePilot.Enabled = true;
            Require(await service.RefreshAvailabilityAsync(), "菜单恢复前连接失败");
            var person = new CardItem(new EmbyItem { Id = "person", Name = "离线演员", Type = EmbyItemType.Person }, fixture.Images, 170);
            ItemCommands.Show(fixture.Session, shell, anchor, person, null);
            await UntilAsync(() => MenuItems().Length > 0);
            fixture.Settings.MoviePilot.Enabled = false;
            await SettleAsync();
            Require(MenuItems().Length > 0, "MP 关闭误关普通 Emby 菜单");
            CloseMenus();

            fixture.Settings.MoviePilot.Enabled = true;
            Require(await service.RefreshAvailabilityAsync(), "订阅菜单恢复前连接失败");
            subscriptions.Attach(service);
            await subscriptions.ReloadAsync();
            subscriptionMenu = MoviePilotSubscriptionMenu.Create(anchor, subscriptions, subscriptions.Movies[0], () => { }, service);
            subscriptionMenu.ShowAt(anchor);
            await UntilAsync(() => MenuItems().Length > 0);
            fixture.Settings.MoviePilot.Enabled = false;
            await UntilAsync(() => MenuItems().Length == 0);
            await SaveProbeScreenAsync("moviepilot-menus-dismissed.png");
        }
        finally
        {
            subscriptionMenu?.Hide();
            CloseMenus();
            subscriptions.Cancel();
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(anchor);
            _root.Children.Remove(shell);
        }

        MenuFlyoutItem[] MenuItems() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutItem>().ToArray();

        void CloseMenus()
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .Where(popup => Descendants(popup.Child).OfType<MenuFlyoutPresenter>().Any()).ToArray()) popup.IsOpen = false;
        }
    }

    private async Task MoviePilotUnavailableNavigationAsync()
    {
        var mp = new SubscriptionProbeServer { Poster = await FixtureImageAsync() };
        using var fixture = CreateFixture(mp);
        await fixture.SelectAsync();
        await ConnectMoviePilotAsync(fixture);
        var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
        var subscription = (await service.SubscriptionsAsync(CancellationToken.None)).First();
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        _root.Children.Add(shell);
        try
        {
            await (Task)Call(shell, "EnterShellAsync")!;
            await LayoutAsync(shell);
            shell.GoTo("home");
            await WaitAsync("主页加载", () => shell.ActiveContentReady);
            Call(shell, "OpenSubscription", subscription);
            shell.GoTo("home");
            fixture.Settings.MoviePilot.Enabled = false;
            await WaitAsync("清理返回历史", () => shell.Pages.BackStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionPage)));
            Call(shell, "GoBack");
            Require(shell.Pages.Content is not MoviePilotSubscriptionPage, "返回复活了不可用订阅页");

            fixture.Settings.MoviePilot.Enabled = true;
            await service.RefreshAvailabilityAsync();
            Call(shell, "OpenSubscription", subscription);
            Call(shell, "GoBack");
            fixture.Settings.MoviePilot.Enabled = false;
            await WaitAsync("清理前进历史", () => shell.Pages.ForwardStack.All(entry => entry.SourcePageType != typeof(MoviePilotSubscriptionPage)));
            Call(shell, "GoForward");
            Require(shell.Pages.Content is not MoviePilotSubscriptionPage, "前进复活了不可用订阅页");

            fixture.Settings.MoviePilot.Enabled = true;
            await service.RefreshAvailabilityAsync();
            Call(shell, "OpenSubscription", subscription);
            var page = (MoviePilotSubscriptionPage)shell.Pages.Content;
            await WaitAsync("订阅详情加载", () => page.ViewModel.IsReady);
            await LayoutAsync(page);
            var asking = ConfirmDialog.For(page, dialog => MoviePilotDialog.ShowAsync(dialog, service))("暂停订阅", "离线测试", "暂停");
            await WaitAsync("操作弹窗打开", DialogOpen);
            fixture.Settings.MoviePilot.Enabled = false;
            Require(!await asking, "关闭设置后确认框仍接受操作");
            await WaitAsync("退出订阅详情", () => shell.Pages.Content is not MoviePilotSubscriptionPage);
            await WaitAsync("操作弹窗关闭", () => !DialogOpen());

            fixture.Settings.MoviePilot.Enabled = true;
            await service.RefreshAvailabilityAsync();
            Call(shell, "OpenSubscription", subscription);
            page = (MoviePilotSubscriptionPage)shell.Pages.Content;
            await WaitAsync("订阅详情加载", () => page.ViewModel.IsReady);
            await LayoutAsync(page);
            var editing = MoviePilotSubscriptionEditor.ShowAsync(page, subscription, service);
            await WaitAsync("操作弹窗打开", DialogOpen);
            fixture.Settings.MoviePilot.Enabled = false;
            Require(await editing is null, "关闭设置后编辑框仍返回改动");
            Require(mp.Writes.Count == 0, "关闭弹窗时提交了写请求");
            await WaitAsync("操作弹窗关闭", () => !DialogOpen());
            await SaveProbeScreenAsync("moviepilot-dialogs-dismissed.png");
        }
        finally
        {
            // 断言失败也必须关掉弹窗，避免下一条用例被 WinUI 的单弹窗限制连带阻塞。
            foreach (var dialog in OpenDialogs()) dialog.Hide();
            fixture.Settings.MoviePilot.Enabled = false;
            Call(shell, "StopMoviePilotMonitoring");
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
            _root.Children.Remove(shell);
        }

        async Task WaitAsync(string stage, Func<bool> ready)
        {
            try { await UntilAsync(ready); }
            catch (TimeoutException error)
            {
                throw new InvalidOperationException($"{stage}超时；连接可用={service.Available}，页面={shell.Pages.Content?.GetType().Name}", error);
            }
        }

        IEnumerable<ContentDialog> OpenDialogs() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Distinct().ToArray();

        bool DialogOpen() => OpenDialogs().Any();
    }
}

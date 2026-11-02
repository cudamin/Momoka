using Momoka.Emby;
using Momoka.Infrastructure;
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
    private async Task AccountAggregateMenuAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync("a.invalid", "账号甲");
        var firstServer = fixture.Session.Server!;
        var firstAccount = fixture.Session.Account!;
        await fixture.SelectAsync("b.invalid", "账号乙");
        var secondServer = fixture.Session.Server!;
        var secondAccount = fixture.Session.Account!;
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.Contains("/Items", StringComparison.Ordinal)
            || request.RequestUri.AbsolutePath.EndsWith("/Shows/NextUp", StringComparison.Ordinal)
            ? Task.FromResult(Items(0, 0)) : Task.FromResult(FakeTransport.Standard(request));
        fixture.Settings.AggregateMode = false;
        fixture.Session.ApplyAggregateMode();
        var shell = new ShellPage();
        shell.Attach(fixture.Services);
        _root.Children.Add(shell);
        var menu = Get<MenuFlyout>(shell, "AccountMenu");
        var topMost = _window!.TopMost;
        _window.TopMost = true;
        try
        {
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            await SourcesReadyAsync(1);
            var toggle = Get<ToggleMenuFlyoutItem>(shell, "AggregateModeItem");
            var offline = Get<MenuFlyoutSubItem>(shell, "OfflineMenu");
            var online = Get<MenuFlyoutItem>(shell, "AccountOnlineItem");
            Require(!toggle.IsChecked && offline.Visibility == Visibility.Collapsed && online.Visibility == Visibility.Collapsed,
                "非聚合模式仍显示下线或在线数量占位");
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeProbeClient(width, 850);
                await LayoutAsync(shell);
                var normalWidth = await PhotographAsync("off");
                await ToggleModeAsync();
                await SourcesReadyAsync(2);
                Require(toggle.IsChecked && offline.Visibility == Visibility.Visible && offline.Items.Count == 2,
                    "聚合开关未恢复账号或下线入口未立即显示");
                Require(online.Text == "2 个账号在线" && online.Visibility == Visibility.Visible,
                    "在线数量未独立显示");
                var aggregateWidth = await PhotographAsync("on");
                Require(aggregateWidth <= normalWidth + 1, $"聚合菜单多出空白：关 {normalWidth:F1}，开 {aggregateWidth:F1}");
                var stale = offline.Items.OfType<MenuFlyoutItem>().Single(item => ((EmbySessionScope)item.Tag).ServerProfileId == firstServer.Id);
                await ToggleModeAsync();
                await SourcesReadyAsync(1);
                Call(shell, "OnTakeAccountOffline", stale, new RoutedEventArgs());
                Require(fixture.Session.IsSignedIn && offline.Visibility == Visibility.Collapsed, "关闭聚合后旧下线菜单仍可执行");
            }
            await (Task)Call(shell, "SetAccountAggregateModeAsync", true)!;
            await SourcesReadyAsync(2);
            await UntilAsync(() => !menu.IsOpen);
            menu.ShowAt(Get<Button>(shell, "AccountButton"));
            await LayoutAsync(shell);
            var target = offline.Items.OfType<MenuFlyoutItem>().Single(item => ((EmbySessionScope)item.Tag).ServerProfileId == firstServer.Id);
            var targetSource = (EmbySessionScope)target.Tag;
            Require(AutomationProperties.GetAutomationId(target).StartsWith("AccountOffline:", StringComparison.Ordinal), "下线账号无法稳定定位");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(offline);
            ((IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)).Expand();
            await LayoutAsync(shell);
            await SaveProbeScreenAsync("account-menu-offline-list-screen.png");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(target).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => fixture.Session.Sources.Count == 1 && shell.ActiveContentReady);
            Require(!targetSource.IsAvailable && firstAccount.HasSavedToken && firstAccount.AutoConnect != false,
                "下线删除了保存凭据或未撤销来源");
            Call(shell, "OnTakeAccountOffline", target, new RoutedEventArgs());
            Require(fixture.Session.Sources.Count == 1, "失效的下线目标误伤其他账号");
            var last = offline.Items.OfType<MenuFlyoutItem>().Single();
            var late = Pending<System.Net.Http.HttpResponseMessage>();
            var waiting = false;
            fixture.Transport.Reply = (request, cancellation) => request.RequestUri!.Host == "a.invalid"
                && request.RequestUri.AbsolutePath.EndsWith("/Views", StringComparison.Ordinal)
                ? DelayRestoreAsync(cancellation)
                : request.RequestUri.AbsolutePath.Contains("/Items", StringComparison.Ordinal)
                    || request.RequestUri.AbsolutePath.EndsWith("/Shows/NextUp", StringComparison.Ordinal)
                    ? Task.FromResult(Items(0, 0)) : Task.FromResult(FakeTransport.Standard(request));
            var restore = (Task)Call(shell, "SetAccountAggregateModeAsync", true)!;
            await UntilAsync(() => waiting);
            Call(shell, "OnTakeAccountOffline", last, new RoutedEventArgs());
            await UntilAsync(() => shell.SignInVisible && !fixture.Session.IsSignedIn);
            late.TrySetResult(Items(0, 0));
            await restore;
            Require(shell.SignInVisible && !fixture.Session.IsSignedIn, "最后账号下线后迟到自动连接复活了来源");
            Require(secondAccount.HasSavedToken && secondAccount.AutoConnect != false && fixture.Actions.Plays == 0,
                "最后账号下线未保留凭据或意外请求播放");

            Task<System.Net.Http.HttpResponseMessage> DelayRestoreAsync(CancellationToken cancellation)
            {
                waiting = true;
                return late.Task.WaitAsync(cancellation);
            }

            async Task ToggleModeAsync()
            {
                await UntilAsync(() => !menu.IsOpen);
                menu.ShowAt(Get<Button>(shell, "AccountButton"));
                await LayoutAsync(shell);
                ((IToggleProvider)new ToggleMenuFlyoutItemAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
                menu.Hide();
                await UntilAsync(() => !menu.IsOpen);
            }

            Task SourcesReadyAsync(int count) => UntilAsync(() => fixture.Session.Sources.Count == count
                && Get<int>(shell, "_sourceOperations") == 0 && shell.ActiveContentReady
                && Get<string>(shell, "_displayedSources") == (string)Call(shell, "SourcesStamp")!);

            async Task<double> PhotographAsync(string state)
            {
                await SettleAsync();
                await UntilAsync(() => !menu.IsOpen);
                menu.ShowAt(Get<Button>(shell, "AccountButton"));
                await LayoutAsync(shell);
                var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot)
                    .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().Single();
                var actual = presenter.ActualWidth;
                Require(actual > 0 && actual <= 240, $"账号菜单未保持紧凑：{actual:F1}");
                await SaveProbeScreenAsync($"account-menu-{state}-{_root.ActualWidth:F0}-screen.png");
                Require(VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot).Count > 0, "截图时账号菜单已意外关闭");
                menu.Hide();
                await UntilAsync(() => !menu.IsOpen);
                return actual;
            }
        }
        finally
        {
            menu.Hide();
            _window.TopMost = topMost;
            _root.Children.Remove(shell);
            shell.Favorites.Dispose();
            Call(shell, "ReleaseContent");
            shell.SignInRoot.Detach();
        }
    }
}

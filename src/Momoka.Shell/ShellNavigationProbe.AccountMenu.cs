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
        var menu = Get<Flyout>(shell, "AccountMenu");
        var topMost = _window!.TopMost;
        _window.TopMost = true;
        try
        {
            await LayoutAsync(shell);
            await (Task)Call(shell, "EnterShellAsync")!;
            await UntilAsync(() => shell.ActiveContentReady);
            await SourcesReadyAsync(1);
            var toggle = Get<ToggleSwitch>(shell, "AggregateModeItem");
            var profiles = Get<ListView>(shell, "AccountProfileList");
            var online = Get<TextBlock>(shell, "AccountOnlineItem");
            var selectionIds = profiles.Items.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                .Select(AutomationProperties.GetAutomationId).Where(id => id.StartsWith("AccountSelect:", StringComparison.Ordinal)).Order().ToArray();
            var beforeSeed = fixture.Transport.Requests.Count;
            shell.RefreshAccountMenus();
            Require(fixture.Transport.Requests.Count == beforeSeed, "刷新开关状态意外重新登录");
            Require(!toggle.IsOn && OfflineButtons().All(button => button.Visibility == Visibility.Collapsed) && online.Visibility == Visibility.Collapsed,
                "非聚合模式仍显示下线或在线数量占位");
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeProbeClient(width, 850);
                await LayoutAsync(shell);
                var normalWidth = await PhotographAsync("off");
                await ToggleModeAsync();
                await SourcesReadyAsync(2);
                Require(toggle.IsOn && OfflineButtons().Count(button => button.Visibility == Visibility.Visible) == 2,
                    "聚合开关未恢复账号或下线入口未立即显示");
                Require(selectionIds.SequenceEqual(profiles.Items.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                    .Select(AutomationProperties.GetAutomationId).Where(id => id.StartsWith("AccountSelect:", StringComparison.Ordinal)).Order()),
                    "账号上下线改变了同一行的自动化定位");
                Require(online.Text == "2 个账号在线" && online.Visibility == Visibility.Visible,
                    "在线数量未独立显示");
                var aggregateWidth = await PhotographAsync("on");
                Require(aggregateWidth <= normalWidth + 1, $"聚合菜单多出空白：关 {normalWidth:F1}，开 {aggregateWidth:F1}");
                var stale = OfflineButtons().Single(button => button.Tag is EmbySessionScope source && source.ServerProfileId == firstServer.Id);
                await ToggleModeAsync();
                await SourcesReadyAsync(1);
                Call(shell, "OnTakeAccountOffline", stale, new RoutedEventArgs());
                Require(fixture.Session.IsSignedIn && OfflineButtons().All(button => button.Visibility == Visibility.Collapsed), "关闭聚合后旧下线菜单仍可执行");
            }
            await (Task)Call(shell, "SetAccountAggregateModeAsync", true)!;
            await SourcesReadyAsync(2);
            foreach (var theme in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(shell);
                await PhotographAsync(theme.Id);
            }
            ThemeHost.Apply(_options.Theme ?? "midnight");
            var firstSelect = profiles.Items.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                .Single(button => AutomationProperties.GetName(button) == "a.invalid · 账号甲");
            menu.ShowAt(Get<Button>(shell, "AccountButton"));
            await LayoutAsync(shell);
            firstSelect = profiles.Items.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                .Single(button => AutomationProperties.GetName(button) == "a.invalid · 账号甲");
            ((IInvokeProvider)new ButtonAutomationPeer(firstSelect).GetPattern(PatternInterface.Invoke)).Invoke();
            await SourcesReadyAsync(2);
            Require(!menu.IsOpen, "选择账号后浮层没有关闭");
            Require(fixture.Session.Server?.Id == firstServer.Id && fixture.Session.Account?.Id == firstAccount.Id,
                "点击身份行没有切换到对应服务器账号");
            await UntilAsync(() => !menu.IsOpen);
            menu.ShowAt(Get<Button>(shell, "AccountButton"));
            await LayoutAsync(shell);
            var target = OfflineButtons().Single(button => button.Tag is EmbySessionScope source && source.ServerProfileId == firstServer.Id);
            var targetSource = (EmbySessionScope)target.Tag;
            Require(AutomationProperties.GetAutomationId(target).StartsWith("AccountOffline:", StringComparison.Ordinal), "下线账号无法稳定定位");
            var targetRow = (Grid)target.Parent;
            var select = targetRow.Children.OfType<Button>().Single(button => !ReferenceEquals(button, target));
            Require(select.Content is TextBlock text && text.Text.StartsWith("a.invalid · 账号甲", StringComparison.Ordinal),
                "服务器与账号没有合并为同一行");
            var selectBounds = select.TransformToVisual(targetRow).TransformBounds(new Windows.Foundation.Rect(0, 0, select.ActualWidth, select.ActualHeight));
            var offlineBounds = target.TransformToVisual(targetRow).TransformBounds(new Windows.Foundation.Rect(0, 0, target.ActualWidth, target.ActualHeight));
            Require(offlineBounds.X >= selectBounds.Right && Math.Abs(offlineBounds.Y - selectBounds.Y) < 1,
                "下线按钮没有放在账号行右侧");
            await SaveProbeScreenAsync("account-menu-offline-list-screen.png");
            ((IInvokeProvider)new ButtonAutomationPeer(target).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => fixture.Session.Sources.Count == 1 && shell.ActiveContentReady);
            Require(!targetSource.IsAvailable && firstAccount.HasSavedToken && firstAccount.AutoConnect != false,
                "下线删除了保存凭据或未撤销来源");
            Call(shell, "OnTakeAccountOffline", target, new RoutedEventArgs());
            Require(fixture.Session.Sources.Count == 1, "失效的下线目标误伤其他账号");
            var last = OfflineButtons().Single(button => button.Visibility == Visibility.Visible);
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

            Button[] OfflineButtons() => profiles.Items.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("AccountOffline:", StringComparison.Ordinal)).ToArray();

            async Task ToggleModeAsync()
            {
                await UntilAsync(() => !menu.IsOpen);
                menu.ShowAt(Get<Button>(shell, "AccountButton"));
                await LayoutAsync(shell);
                ((IToggleProvider)new ToggleSwitchAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
                menu.Hide();
                await UntilAsync(() => !menu.IsOpen);
            }

            Task SourcesReadyAsync(int count) => UntilAsync(() => fixture.Session.Sources.Count == count
                && Get<int>(shell, "_sourceOperations") == 0 && shell.ActiveContentReady
                && Get<string>(shell, "_displayedSources") == (string)Call(shell, "SourcesStamp")!);

            async Task<double> PhotographAsync(string state)
            {
                await Task.Delay(250);
                await UntilAsync(() => !menu.IsOpen);
                menu.ShowAt(Get<Button>(shell, "AccountButton"));
                await LayoutAsync(shell);
                var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot)
                    .SelectMany(popup => Descendants(popup.Child)).OfType<FlyoutPresenter>().Single();
                var actual = presenter.ActualWidth;
                Require(actual > 0 && actual <= 360, $"账号菜单未保持紧凑：{actual:F1}");
                var knob = Descendants(toggle).OfType<Grid>().Single(element => element.Name == "SwitchKnob");
                var knobOffset = ((TranslateTransform)knob.RenderTransform).X;
                Require(toggle.IsOn ? knobOffset > 10 : Math.Abs(knobOffset) < 1,
                    $"滑动开关文字与滑块位置不一致：{toggle.IsOn}/{knobOffset:F1}");
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

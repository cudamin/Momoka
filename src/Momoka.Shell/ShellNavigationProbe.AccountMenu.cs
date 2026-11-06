using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Emby;
using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Shell.Views;

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
        fixture.Transport.Reply = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/Items/Latest", StringComparison.Ordinal)
            ? Task.FromResult(Json(Array.Empty<EmbyItem>()))
            : request.RequestUri.AbsolutePath.Contains("/Items", StringComparison.Ordinal)
            || request.RequestUri.AbsolutePath.EndsWith("/Shows/NextUp", StringComparison.Ordinal)
            ? Task.FromResult(Items(0, 0)) : Task.FromResult(FakeTransport.Standard(request));
        fixture.Settings.AggregateMode = false;
        fixture.Session.ApplyAggregateMode();
        await VerifyServersPageVisibilityAsync();
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
            var identity = Get<TextBlock>(shell, "AccountIdentityItem");
            var aggregateRow = Get<Grid>(shell, "AggregateModeRow");
            var management = new ViewModels.ServersViewModel();
            management.Attach(fixture.Actions, fixture.SettingsService, fixture.Session);
            var beforeVisibility = fixture.Transport.Requests.Count;
            var originalServers = fixture.Settings.Servers.ToArray();
            firstServer.Accounts.Add(new Momoka.Configuration.AccountProfile { Id = "offline-extra", Username = "未登录账号" });
            try
            {
                foreach (var count in new[] { 0, 1, 2, 1, 2 })
                {
                    fixture.Settings.Servers.Clear();
                    fixture.Settings.Servers.AddRange(originalServers.Take(count));
                    fixture.SettingsService.Save();
                    await LayoutAsync(shell);
                    Require(aggregateRow.Visibility == (count > 1 ? Visibility.Visible : Visibility.Collapsed),
                        "保存服务器变化后聚合选项未即时更新");
                    shell.RefreshAccountMenus();
                    management.Refresh();
                    await UntilAsync(() => !menu.IsOpen);
                    menu.ShowAt(Get<Button>(shell, "AccountButton"));
                    await LayoutAsync(shell);
                    var expected = count > 1 ? Visibility.Visible : Visibility.Collapsed;
                    Require(aggregateRow.Visibility == expected && management.AggregateOptionsVisibility == expected
                        && management.NoteVisibility == expected, $"{count} 台保存服务器的聚合选项或说明显隐错误");
                    Require(!fixture.Settings.AggregateMode && fixture.Session.Sources.Count == 1,
                        "隐藏聚合选项修改了偏好或在线会话");
                    Require(count > 1 || aggregateRow.ActualHeight == 0, "隐藏聚合选项留下行高");
                    if (count == 1) await SaveProbeScreenAsync($"account-menu-single-server-{_root.ActualWidth:F0}-screen.png");
                    menu.Hide();
                }
                Require(fixture.Transport.Requests.Count == beforeVisibility, "仅改变聚合选项显隐发起了恢复请求");
            }
            finally
            {
                fixture.Settings.Servers.Clear();
                fixture.Settings.Servers.AddRange(originalServers);
                firstServer.Accounts.RemoveAt(firstServer.Accounts.Count - 1);
                shell.RefreshAccountMenus();
                management.Cancel();
            }
            var selectionIds = profiles.Items.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                .Select(AutomationProperties.GetAutomationId).Where(id => id.StartsWith("AccountSelect:", StringComparison.Ordinal)).Order().ToArray();
            var beforeSeed = fixture.Transport.Requests.Count;
            shell.RefreshAccountMenus();
            Require(fixture.Transport.Requests.Count == beforeSeed, "刷新开关状态意外重新登录");
            Require(!toggle.IsOn && OfflineButtons().All(button => button.Visibility == Visibility.Collapsed) && online.Visibility == Visibility.Collapsed,
                "非聚合模式仍显示下线或在线数量占位");
            menu.ShowAt(Get<Button>(shell, "AccountButton"));
            await UntilAsync(() => toggle.IsLoaded);
            var off = Get<VisualState>(shell, "_accountOffState");
            var reset = Get<DoubleAnimation>(shell, "_accountOffAnimation");
            Require(off.Storyboard.Children.Contains(reset) && reset.To == 0 && reset.Duration == TimeSpan.Zero
                && Storyboard.GetTargetProperty(reset) == "X", "账号开关关态未安装明确终值");
            var animationCount = off.Storyboard.Children.Count;
            var requestCount = fixture.Transport.Requests.Count;
            var changes = 0;
            void OnToggled(object sender, RoutedEventArgs args) => changes++;
            toggle.Toggled += OnToggled;
            try
            {
                Call(shell, "EnsureAccountToggleOffState");
                Call(shell, "EnsureAccountToggleOffState");
                Require(ReferenceEquals(off, Get<VisualState>(shell, "_accountOffState"))
                    && ReferenceEquals(reset, Get<DoubleAnimation>(shell, "_accountOffAnimation"))
                    && off.Storyboard.Children.Count == animationCount && changes == 0 && !toggle.IsOn
                    && !fixture.Settings.AggregateMode && fixture.Transport.Requests.Count == requestCount,
                    "账号开关重复加载增加动画、改变偏好或触发请求");
            }
            finally { toggle.Toggled -= OnToggled; menu.Hide(); }
            foreach (var width in new[] { 1100, 760, 1100, 760 })
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
                Require(online.Text == "2 个账号在线" && online.Visibility == Visibility.Visible && identity.Visibility == Visibility.Collapsed,
                    "聚合菜单顶部未仅显示在线数量");
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
            fixture.Settings.Servers.Remove(secondServer);
            shell.RefreshAccountMenus();
            Require(aggregateRow.Visibility == Visibility.Collapsed && toggle.IsOn && fixture.Settings.AggregateMode
                && fixture.Session.Sources.Count == 2 && OfflineButtons().Count(button => button.Visibility == Visibility.Visible) == 1,
                "隐藏已开启的聚合选项清空了偏好或下线操作");
            fixture.Settings.Servers.Add(secondServer);
            shell.RefreshAccountMenus();
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
            await UntilAsync(() => fixture.Session.Server?.Id == firstServer.Id && fixture.Session.Account?.Id == firstAccount.Id);
            await SourcesReadyAsync(2);
            Require(!menu.IsOpen, "选择账号后浮层没有关闭");
            Require(fixture.Session.Server?.Id == firstServer.Id && fixture.Session.Account?.Id == firstAccount.Id,
                "点击身份行没有切换到对应服务器账号");
            await UntilAsync(() => !menu.IsOpen);
            menu.ShowAt(Get<Button>(shell, "AccountButton"));
            await LayoutAsync(shell);
            // ListView 在浮层打开后才实现行；根页面完成布局不代表新一批账号按钮已经挂树。
            await UntilAsync(() =>
            {
                profiles.ApplyTemplate();
                profiles.UpdateLayout();
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(shell.XamlRoot))
                    if (popup.Child is FrameworkElement content) content.UpdateLayout();
                return menu.IsOpen && profiles.IsLoaded && OfflineButtons().Any(button => button.Tag is EmbySessionScope source
                    && source.ServerProfileId == firstServer.Id && button.IsLoaded && button.Parent is Grid && button.ActualWidth > 0);
            });
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
                Require(toggle.IsOn ? online.Visibility == Visibility.Visible && identity.Visibility == Visibility.Collapsed
                    : online.Visibility == Visibility.Collapsed && identity.Visibility == Visibility.Visible,
                    "账号菜单顶部身份与在线数量没有按模式切换");
                Require(actual > 0 && actual <= 300, $"账号菜单未保持紧凑：{actual:F1}");
                Require(presenter.Background is AcrylicBrush { AlwaysUseFallback: false }, "账号菜单背景不是亚克力");
                Require(profiles.Items.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                    .All(button => button.FontSize == 12 && button.ActualHeight <= 33), "账号行未使用较小字号或紧凑高度");
                var knob = Descendants(toggle).OfType<Grid>().Single(element => element.Name == "SwitchKnob");
                var expected = toggle.IsOn;
                var readings = new List<string>();
                var stable = 0;
                var waited = System.Diagnostics.Stopwatch.StartNew();
                while (waited.ElapsedMilliseconds < 2000 && stable < 3)
                {
                    var offset = ((TranslateTransform)knob.RenderTransform).X;
                    if (readings.Count < 12)
                        readings.Add($"{waited.ElapsedMilliseconds}:{offset:0.0}");
                    stable = menu.IsOpen && toggle.IsLoaded && knob.IsLoaded && toggle.IsOn == expected
                        && (expected ? offset > 10 : Math.Abs(offset) < 1) ? stable + 1 : 0;
                    await Task.Delay(25);
                }
                var knobOffset = ((TranslateTransform)knob.RenderTransform).X;
                Log.Info("外壳探针", $"账号开关：{state} width={_root.ActualWidth:0} isOn={toggle.IsOn} "
                    + $"offset={knobOffset:0.0} waited={waited.ElapsedMilliseconds}ms "
                    + $"base={((TranslateTransform)knob.RenderTransform).GetAnimationBaseValue(TranslateTransform.XProperty)}");
                await SaveProbeScreenAsync($"account-menu-{state}-{_root.ActualWidth:F0}-screen.png");
                knobOffset = ((TranslateTransform)knob.RenderTransform).X;
                Require(stable == 3 && toggle.IsOn == expected && (expected ? knobOffset > 10 : Math.Abs(knobOffset) < 1),
                    $"滑动开关文字与滑块位置不一致：{toggle.IsOn}/{knobOffset:F1} loaded={toggle.IsLoaded}/{knob.IsLoaded} "
                        + $"base={((TranslateTransform)knob.RenderTransform).GetAnimationBaseValue(TranslateTransform.XProperty)} "
                        + $"knobs={Descendants(toggle).OfType<Grid>().Count(element => element.Name == "SwitchKnob")} "
                        + $"{ProbeHostState(toggle)} waited={waited.ElapsedMilliseconds}ms samples={string.Join(",", readings)}");
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

        async Task VerifyServersPageVisibilityAsync()
        {
            var host = new Frame();
            _root.Children.Add(host);
            var originalServers = fixture.Settings.Servers.ToArray();
            ServersPage? page = null;
            try
            {
                Require(host.Navigate(typeof(ServersPage), new ServerRequest(fixture.Services)), "服务器页未导航成功");
                page = (ServersPage)host.Content;
                await UntilAsync(() => page.IsReady);
                var toggle = Get<ToggleSwitch>(page, "AggregateToggle");
                foreach (var count in new[] { 1, 2, 0, 2 })
                {
                    fixture.Settings.Servers.Clear();
                    fixture.Settings.Servers.AddRange(originalServers.Take(count));
                    fixture.SettingsService.Save();
                    await UntilAsync(() => page.Summary.Servers == count);
                    await LayoutAsync(page);
                    Require(((FrameworkElement)toggle.Parent).Visibility == (count > 1 ? Visibility.Visible : Visibility.Collapsed),
                        "服务器页保存台数变化后开关区域未同步更新");
                }
                page.Release();
                fixture.Settings.Servers.RemoveAt(1);
                fixture.SettingsService.Save();
                await LayoutAsync(page);
                Require(page.Summary.Servers == 2, "服务器页释放后保存通知仍修改旧页面");
            }
            finally
            {
                page?.Release();
                _root.Children.Remove(host);
                fixture.Settings.Servers.Clear();
                fixture.Settings.Servers.AddRange(originalServers);
            }
        }
    }
}

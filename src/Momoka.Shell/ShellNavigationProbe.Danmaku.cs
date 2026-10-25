using System.Collections.ObjectModel;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task SettingsDanmakuAsync(int width)
    {
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, $"danmaku-settings-{width}"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        _root.Children.Add(page);
        try
        {
            ResizeProbeClient(width, 900);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "弹幕";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var card = page.ViewModel.Sections.Single(section => section.Category == "弹幕");
            Require(card.Rows.Count == 4 && !fixture.Settings.Danmaku.Enabled, "初始应只显示启用与连接配置");
            await SaveProbeScreenAsync($"settings-danmaku-off-{width}-screen.png");
            var enabled = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "启用弹弹play");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(enabled).GetPattern(PatternInterface.Toggle)).Toggle();
            await LayoutAsync(page);
            Require(card.Rows.Count == 4, "未配凭据时显示了无效的外观设置");

            var app = card.Rows.OfType<SettingTextRow>().Single(row => row.Label == "AppId");
            app.Text = "offline-fixture-app";
            await LayoutAsync(page);
            var password = Descendants(page).OfType<PasswordBox>().Single();
            password.Password = "offline-fixture-secret";
            await SettleAsync();
            var secret = card.Rows.OfType<SettingSecretRow>().Single();
            Require(secret.Draft == "offline-fixture-secret", "PasswordBox 未绑定密钥草稿");
            var save = Descendants(page).OfType<Button>().Single(control => AutomationProperties.GetName(control) == "保存弹弹play密钥");
            ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(page);
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(password.Password.Length == 0 && secret.Draft.Length == 0, "成功保存后仍在界面保留明文");
            Require(card.Rows.OfType<SettingSliderRow>().Count() == 5, "配置后没有显示五个外观滑块");
            Require(ReferenceEquals(card, page.ViewModel.Sections.Single(section => section.Category == "弹幕")), "配置变更替换了整张卡");
            var opacity = Descendants(page).OfType<Slider>().Single(control => control.DataContext is SettingSliderRow row && row.Label == "不透明度（%）");
            opacity.Value = 55;
            await LayoutAsync(page);
            Require(fixture.Settings.Danmaku.Opacity == 55, "真实滑块未写回偏好");
            await SaveProbeScreenAsync($"settings-danmaku-on-{width}-screen.png");

            var inputToggle = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "显示弹幕输入栏");
            Require(inputToggle.IsOn, "输入栏没有默认开启");
            inputToggle.IsOn = false;
            await LayoutAsync(page);
            Require(!fixture.Settings.Danmaku.ShowInput && fixture.Settings.Danmaku.Show, "关闭输入栏影响了弹幕显示或未保存偏好");
            var show = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "显示弹幕");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(show).GetPattern(PatternInterface.Toggle)).Toggle();
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(!card.Rows.OfType<SettingSliderRow>().Any() && fixture.Settings.Danmaku.Opacity == 55, "关闭显示未完整隐藏或丢失偏好");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(show).GetPattern(PatternInterface.Toggle)).Toggle();
            await UntilAsync(() => page.IsContentMotionSettled);
            await LayoutAsync(page);
            Require(!fixture.Settings.Danmaku.ShowInput && card.Rows.OfType<SettingToggleRow>().Any(row => row.Label == "显示弹幕输入栏"), "恢复弹幕未保留输入栏偏好或未恢复开关");
            Require(card.Rows.OfType<SettingSliderRow>().Single(row => row.Label == "不透明度（%）").Value == 55, "恢复显示时滑块不是原值");
            enabled.IsOn = false;
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(card.Rows.Count == 4 && fixture.Settings.Danmaku.Opacity == 55, "关闭服务未隐藏从属区域或清空偏好");
            enabled.IsOn = true;
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(card.Rows.OfType<SettingSliderRow>().Any(), "重新启用没有恢复外观设置");
            var block = Descendants(page).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == "屏蔽词");
            block.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.9 });
            await LayoutAsync(block);
            await SaveProbeScreenAsync($"settings-danmaku-bottom-{width}-screen.png");

            page.SelectedCategory = "播放器";
            await LayoutAsync(page);
            var backend = page.ViewModel.Sections.Single(section => section.Title == "播放器").Rows.OfType<SettingChoiceRow>().Single();
            var picker = Descendants(page).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == "播放后端");
            picker.SelectedItem = backend.Choices.Single(choice => choice.Label.StartsWith("外部", StringComparison.Ordinal));
            await LayoutAsync(page);
            Require(!page.ViewModel.Categories.Any(entry => entry.Label == "弹幕") && !page.ViewModel.Sections.Any(section => section.Category == "弹幕"), "外部后端没有隐藏弹幕入口及卡片");
            Require(fixture.Settings.Danmaku.Opacity == 55 && fixture.Settings.Danmaku.ProtectedAppSecret.Length > 0, "外部后端清空了偏好或密钥");
            picker.SelectedItem = backend.Choices.Single(choice => choice.Label.StartsWith("内置", StringComparison.Ordinal));
            await LayoutAsync(page);
            Require(page.ViewModel.Categories.Any(entry => entry.Label == "弹幕") && page.ViewModel.Sections.Any(section => section.Category == "弹幕"), "内置后端没有恢复弹幕入口及卡片");

            page.SelectedCategory = "快捷键";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var scroll = Descendants(page).OfType<ScrollView>().Single();
            await ScrollProbeAsync(scroll, 0);
            await LayoutAsync(page);
            var shortcuts = page.ViewModel.Sections.Single(section => section.Category == "快捷键");
            Require(shortcuts.Rows.OfType<SettingShortcutRow>().Count() == ShortcutCatalog.Actions.Count, "启用弹幕后快捷键目录不完整");
            await SaveProbeScreenAsync($"settings-shortcuts-{width}-screen.png");
            var last = Descendants(page).OfType<ShortcutCaptureBox>().Last();
            last.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.9 });
            await LayoutAsync(last);
            await SaveProbeScreenAsync($"settings-shortcuts-new-{width}-screen.png");
            page.SelectedCategory = "关于";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            await ScrollProbeAsync(scroll, 0);
            await LayoutAsync(page);
            Require(page.ViewModel.Sections.Single(section => section.Category == "关于").Rows.OfType<SettingFactRow>()
                .Any(row => row.Label == AboutFacts.StarLabel), "关于没有点星行");
            var star = Descendants(page).OfType<TextBlock>().Single(text => text.Text == AboutFacts.StarLabel);
            Require(star.ActualWidth > 0 && star.ActualHeight > 0, "点星行没有实际布局到界面");
            // 系统 ItemContainerTransitions 的呈现可能晚于 XAML 属性归位；不关闭动画，等真实屏幕落定。
            await Task.Delay(1500);
            await SaveProbeScreenAsync($"settings-about-star-{width}-screen.png");
            Require(StarPixels() > 30, "点星文字在实际截图中没有足够的亮像素，不能把空白卡片判为通过");
            star.Opacity = 0;
            try
            {
                await Task.Delay(250);
                Require(StarPixels() == 0, "文字像素检查未能识别故意隐藏的点星文字");
            }
            finally { star.Opacity = 1; }

            int StarPixels()
            {
                var bounds = star.TransformToVisual(_root).TransformBounds(new Windows.Foundation.Rect(0, 0, star.ActualWidth, star.ActualHeight));
                var scale = _root.XamlRoot.RasterizationScale;
                return CompositionPlaybackProbe.BrightPixels(_window!, new Windows.Foundation.Rect(bounds.X * scale, bounds.Y * scale,
                    bounds.Width * scale, bounds.Height * scale));
            }
        }
        finally { _root.Children.Remove(page); }
    }

    private async Task DanmakuPanelAsync(int width)
    {
        using var fixture = CreateFixture();
        fixture.Settings.Danmaku.Enabled = true;
        fixture.Settings.Danmaku.AppId = "offline-fixture-app";
        fixture.Settings.Danmaku.ProtectedAppSecret = "offline-fixture-secret";
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, $"danmaku-panel-{width}"));
        paths.EnsureCreated();
        using var client = new DandanPlayClient(fixture.Settings, new(PassthroughSecretProtector.Instance), Path.Combine(paths.Root, "cache"), new RejectNetwork());
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings, () => throw new InvalidOperationException("离线面板不创建播放后端"),
            new PlaybackPlanner(fixture.Settings, shaders, paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images, shaders, fixture.Dispatcher, client);
        vm.Connect();
        var panel = new DanmakuPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24), Width = width < 500 ? 300 : 440 };
        panel.Attach(vm);
        var results = Get<ObservableCollection<DandanPlayEpisode>>(panel, "_results");
        results.Add(new(230001, "测试作品：用于核对长标题与自动换行", "第 1 话 · 这只是离线夹具，不访问任何服务器"));
        Get<ListView>(panel, "Results").Visibility = Visibility.Visible;
        vm.DanmakuStatus = "来源：弹弹play · 离线界面夹具；未开始播放、未发送网络请求。";
        _root.Children.Add(panel);
        try
        {
            ResizeProbeClient(width, 900);
            await LayoutAsync(panel);
            var opacity = Get<Slider>(panel, "OpacitySlider");
            opacity.Value = 45;
            Require(fixture.Settings.Danmaku.Opacity == 45, "面板滑块未写回与设置页同一偏好");
            var show = Get<ToggleSwitch>(panel, "ShowToggle");
            show.IsOn = false;
            await LayoutAsync(panel);
            Require(Get<StackPanel>(panel, "Appearance").Visibility == Visibility.Collapsed && fixture.Settings.Danmaku.Opacity == 45, "面板关闭显示未隐藏外观或丢失偏好");
            show.IsOn = true;
            await LayoutAsync(panel);
            Require(Get<StackPanel>(panel, "Appearance").Visibility == Visibility.Visible && opacity.Value == 45, "面板恢复显示未恢复偏好");
            await SaveProbeScreenAsync($"player-danmaku-panel-{width}-screen.png");
            var block = Get<TextBox>(panel, "BlockBox");
            block.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 1 });
            await LayoutAsync(block);
            await SaveProbeScreenAsync($"player-danmaku-panel-bottom-{width}-screen.png");
            Field(vm, "_generation", 1);
            opacity.Value = 50;
            Require(fixture.Settings.Danmaku.Opacity == 45, "过期面板修改了新一场播放的设置");
            Require(Get<TextBlock>(panel, "SessionMessage").Visibility == Visibility.Visible, "过期面板没有显示恢复提示");

            // 不创建播放后端；直接重放真实 Closed 回调的迟到顺序，检查旧面板不会取消新搜索。
            var holder = new PlayerPage();
            var oldFlyout = new Flyout { Content = panel };
            var nextPanel = new DanmakuPanel();
            nextPanel.Attach(vm);
            var nextFlyout = new Flyout { Content = nextPanel };
            try
            {
                Field(holder, "_danmakuFlyout", oldFlyout);
                Field(holder, "_danmakuPanel", panel);
                Call(holder, "CloseDanmakuPanel");
                var request = Get<int>(vm, "_danmakuSearchRequest");
                panel.Close();
                Require(Get<int>(vm, "_danmakuSearchRequest") == request, "重复关闭旧面板取消了后续搜索");
                Field(holder, "_danmakuFlyout", nextFlyout);
                Field(holder, "_danmakuPanel", nextPanel);
                Call(holder, "OnDanmakuFlyoutClosed", oldFlyout, panel);
                Require(ReferenceEquals(Get<DanmakuPanel>(holder, "_danmakuPanel"), nextPanel)
                    && !Get<bool>(nextPanel, "_closed") && Get<int>(vm, "_danmakuSearchRequest") == request,
                    "旧浮层的迟到关闭影响了新面板或新搜索");
                Call(holder, "OnDanmakuFlyoutClosed", nextFlyout, nextPanel);
                Require(Get<bool>(nextPanel, "_closed") && Get<Flyout?>(holder, "_danmakuFlyout") is null,
                    "当前浮层关闭后未释放面板");
            }
            finally { nextPanel.Close(); holder.ReleaseVideoSurface(); }
        }
        finally { panel.Close(); _root.Children.Remove(panel); await vm.ShutdownAsync(); }
    }
}

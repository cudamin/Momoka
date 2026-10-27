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
using Microsoft.UI.Xaml.Media;

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
            BeginSettingsFixtureVisit(page);
            ResizeProbeClient(width, 900);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "弹幕";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            // 2026-10-27 起「弹幕」分类是四张卡：弹幕（开关与匹配）、显示与外观、类型与屏蔽、应用凭据。
            var cards = DanmakuCards(page);
            Require(cards.Length == 4, "弹幕分类应分成四张卡");
            var first = cards[0];
            Require(DanmakuRows(page).Length == 2 && !fixture.Settings.Danmaku.Enabled, "初始应只显示启用与凭据来源");
            Require(cards.Count(section => section.SectionVisibility == Visibility.Visible) == 2, "服务关着时应只显示两张有内容的卡");
            await SaveProbeScreenAsync($"settings-danmaku-off-{width}-screen.png");
            var enabled = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "启用弹弹play");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(enabled).GetPattern(PatternInterface.Toggle)).Toggle();
            await LayoutAsync(page);
            Require(DanmakuRows(page).OfType<SettingSliderRow>().Count() == 5, "内置凭据开启后没有显示外观设置");
            Require(!DanmakuRows(page).OfType<SettingTextRow>().Any(row => row.Label == "AppId"), "内置凭据下仍显示开发者配置");

            var custom = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "使用自己的应用凭据");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(custom).GetPattern(PatternInterface.Toggle)).Toggle();
            await LayoutAsync(page);
            Require(fixture.Settings.Danmaku.CredentialSource == DanmakuCredentialSource.Custom
                && DanmakuRows(page).Length == 5 && !DanmakuRows(page).OfType<SettingSliderRow>().Any(),
                "切到自定义凭据后没有收起外观或显示三行配置");
            Require(cards[3].Rows.Count == 4 && cards[3].Title == "应用凭据", "凭据三行应连同上级开关一起落在最下面那张卡");

            var app = DanmakuRows(page).OfType<SettingTextRow>().Single(row => row.Label == "AppId");
            app.Text = "offline-fixture-app";
            await LayoutAsync(page);
            var password = Descendants(page).OfType<PasswordBox>().Single();
            password.Password = "offline-fixture-secret";
            await SettleAsync();
            var secret = DanmakuRows(page).OfType<SettingSecretRow>().Single();
            Require(secret.Draft == "offline-fixture-secret", "PasswordBox 未绑定密钥草稿");
            var save = Descendants(page).OfType<Button>().Single(control => AutomationProperties.GetName(control) == secret.SaveAutomationName);
            ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)).Invoke();
            await LayoutAsync(page);
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(password.Password.Length == 0 && secret.Draft.Length == 0, "成功保存后仍在界面保留明文");
            Require(DanmakuRows(page).OfType<SettingSliderRow>().Count() == 5, "配置后没有显示五个外观滑块");
            Require(ReferenceEquals(first, DanmakuCards(page)[0]), "配置变更替换了卡片");
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
            Require(!DanmakuRows(page).OfType<SettingSliderRow>().Any() && fixture.Settings.Danmaku.Opacity == 55, "关闭显示未完整隐藏或丢失偏好");
            Require(cards[1].Rows.Count == 1 && cards[2].Rows.Count == 0, "关闭显示后「类型与屏蔽」应整张收起");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(show).GetPattern(PatternInterface.Toggle)).Toggle();
            await UntilAsync(() => page.IsContentMotionSettled);
            await LayoutAsync(page);
            Require(!fixture.Settings.Danmaku.ShowInput && DanmakuRows(page).OfType<SettingToggleRow>().Any(row => row.Label == "显示弹幕输入栏"), "恢复弹幕未保留输入栏偏好或未恢复开关");
            Require(DanmakuRows(page).OfType<SettingSliderRow>().Single(row => row.Label == "不透明度（%）").Value == 55, "恢复显示时滑块不是原值");
            enabled.IsOn = false;
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(DanmakuRows(page).Length == 5 && fixture.Settings.Danmaku.Opacity == 55, "关闭服务未隐藏从属区域或清空偏好");
            enabled.IsOn = true;
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(DanmakuRows(page).OfType<SettingSliderRow>().Any(), "重新启用没有恢复外观设置");
            var block = Descendants(page).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == "屏蔽词");
            block.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.9 });
            await LayoutAsync(block);
            await SaveProbeScreenAsync($"settings-danmaku-bottom-{width}-screen.png");

            // 最下面那张「应用凭据」卡：上级开关和三行凭据都在这一张里（用户 2026-10-27 指定）。
            // 这一行的按钮没有 AutomationId／Name，按它显示的字找（Fact 行的按钮内容就是动作名）。
            var developer = Descendants(page).OfType<Button>().Single(control => control.Content as string == "打开开发者中心");
            developer.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.9 });
            await LayoutAsync(developer);
            await SaveProbeScreenAsync($"settings-danmaku-credentials-{width}-screen.png");

            // 2026-10-27「开启隐藏下方说明之后用线条分割每个功能」：说明收起后，同一张卡里除首行外每行顶上
            // 一条 1px 细线；说明放回来就撤掉。拨的是「界面」卡上那颗真开关，量的是行容器模板根上的
            // BorderThickness —— 自检的 ProbeCompactMode 量的是同一条规则的模型那一侧。
            page.SelectedCategory = "界面";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var compact = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "隐藏功能下方说明");
            Require(!compact.IsOn, "「隐藏功能下方说明」默认应关闭");
            compact.IsOn = true;
            await UntilAsync(() => page.IsContentMotionSettled);
            await LayoutAsync(page);
            // 「界面」卡里是色板那一种行（StackPanel 模板根），跟弹幕那几种形状不是同一个模板：精简模式下
            // 它也必须有分隔线，所以在这里另拍一张。
            await SaveProbeScreenAsync($"settings-interface-compact-{width}-screen.png");
            page.SelectedCategory = "弹幕";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var compactCard = DanmakuCards(page).First(section => section.Rows.Count > 1);
            var rowsList = Descendants(page).OfType<ItemsControl>()
                .Single(control => control.Name == "SettingsRowsList" && ReferenceEquals(control.ItemsSource, compactCard.Rows));
            Require((await RowBorderAsync(rowsList, compactCard.Rows[0])).Top == 0, "精简模式下第一行仍画了分隔线");
            Require((await RowBorderAsync(rowsList, compactCard.Rows[1])).Top == 1, "精简模式下其余行没有分隔线");
            await SaveProbeScreenAsync($"settings-danmaku-compact-{width}-screen.png");
            compact.IsOn = false;
            await UntilAsync(() => page.IsContentMotionSettled);
            await LayoutAsync(page);
            Require((await RowBorderAsync(rowsList, compactCard.Rows[1])).Top == 0, "关掉精简模式后分隔线没有撤掉");

            // 2026-10-27 拆卡的另外四个分类各拍一张：主页、播放行为、音频输出、字幕。自检走查量的是行容器
            // 数量，看不出「卡分得对不对」；这几张是给人看的证据，宽窄两档都拍。
            foreach (var (category, name) in new[] { ("主页", "home"), ("播放行为", "playback"), ("音频输出", "audio"), ("字幕", "subtitle") })
            {
                page.SelectedCategory = category;
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                await SaveProbeScreenAsync($"settings-split-{name}-{width}-screen.png");
            }

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
            var scroll = Descendants(page).OfType<ScrollViewer>().Single(view => view.Name == "SettingsScroll");
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
            // 关于 2026-10-27 拆成四张卡，点星行在第一张。
            Require(page.ViewModel.Sections.Where(section => section.Category == "关于")
                .SelectMany(section => section.Rows).OfType<SettingFactRow>()
                .Any(row => row.Label == AboutFacts.StarLabel), "关于没有点星行");
            var star = Descendants(page).OfType<TextBlock>().Single(text => text.Text == AboutFacts.StarLabel);
            Require(star.ActualWidth > 0 && star.ActualHeight > 0, "点星行没有实际布局到界面");
            // 截图和随后的亮像素正/负采样必须共用可见窗口。SaveProbeScreenAsync 会还原置顶状态，
            // 单独拍完就取消置顶时，其他窗口可以在两次采样之间盖上来；遮挡守卫仍保持原样。
            var wasTopMost = _window!.TopMost;
            try
            {
                _window.TopMost = true;
                // 树有尺寸不代表合成器已绘制当前分类；先渲染当前页并等两帧，再做正/负实屏采样。
                await SaveReturnFrameAsync(page, $"settings-about-star-{width}-render.png");
                await WaitForDiagnosticsFrameAsync();
                await SaveProbeScreenAsync($"settings-about-star-{width}-screen.png");
                Require(StarPixels() > 30, "点星文字在实际截图中没有足够的亮像素，不能把空白卡片判为通过");
                star.Opacity = 0;
                try
                {
                    await SaveReturnFrameAsync(page, $"settings-about-star-{width}-hidden-render.png");
                    await WaitForDiagnosticsFrameAsync();
                    await SaveProbeScreenAsync($"settings-about-star-{width}-hidden-screen.png");
                    Require(StarPixels() == 0, "文字像素检查未能识别故意隐藏的点星文字");
                }
                finally { star.Opacity = 1; }
            }
            finally { _window.TopMost = wasTopMost; }

            int StarPixels()
            {
                var bounds = star.TransformToVisual(_root).TransformBounds(new Windows.Foundation.Rect(0, 0, star.ActualWidth, star.ActualHeight));
                var scale = _root.XamlRoot.RasterizationScale;
                return CompositionPlaybackProbe.BrightPixels(_window!, new Windows.Foundation.Rect(bounds.X * scale, bounds.Y * scale,
                    bounds.Width * scale, bounds.Height * scale));
            }
        }
        finally { page.Release(); _root.Children.Remove(page); }
    }

    private async Task DanmakuPanelAsync(int width)
    {
        using var fixture = CreateFixture();
        fixture.Settings.Danmaku.Enabled = true;
        fixture.Settings.Danmaku.AppId = "offline-fixture-app";
        fixture.Settings.Danmaku.ProtectedAppSecret = "offline-fixture-secret";
        fixture.Settings.Danmaku.CredentialSource = DanmakuCredentialSource.Custom;
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

    /// <summary>
    /// 「弹幕」分类下的四张卡，按屏上次序。拆卡之后不能再拿 <c>Single(Category == "弹幕")</c> 找它 —— 那是
    /// 2026-10-27 之前的形状。
    /// </summary>
    private static SettingSection[] DanmakuCards(SettingsPage page) =>
        [.. page.ViewModel.Sections.Where(section => section.Category == "弹幕")];

    private static SettingRow[] DanmakuRows(SettingsPage page) =>
        [.. DanmakuCards(page).SelectMany(section => section.Rows)];

    /// <summary>
    /// 一行容器模板根上的边框粗细。设置行的分隔线是行模板根上的 1px 上边框（见 SettingRow.DividerThickness），
    /// 容器要等模板落定才拿得到 —— 所以等它，超时就是失败，不返回一个「看起来像 0」的默认值。
    /// </summary>
    private static async Task<Thickness> RowBorderAsync(ItemsControl list, object row)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (list.ContainerFromItem(row) is FrameworkElement container
                && VisualTreeHelper.GetChildrenCount(container) > 0
                && VisualTreeHelper.GetChild(container, 0) is Grid root) return root.BorderThickness;
            list.UpdateLayout();
            await Task.Delay(16);
        }
        throw new InvalidOperationException("设置行容器没有落到模板根上");
    }
}

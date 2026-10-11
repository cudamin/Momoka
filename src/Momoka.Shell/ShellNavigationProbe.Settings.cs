using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Services;
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
    private async Task SettingsEntranceAsync()
    {
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-motion"));
        paths.EnsureCreated();
        // 本例测动效，不扫描机器上的数千字体；给现有缓存放入空目录，字体扫描另有专项探针。
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var evidence = new List<string> { $"系统动画：{HomeMotion.AnimationsEnabled}" };
        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings), new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            fixture.Capabilities);
        await page.ViewModel.ReloadAsync();
        var sections = page.ViewModel.Sections.ToArray();
        _root.Children.Add(page);
        try
        {
            await LayoutAsync(page);
            await SettingsAnimationToggleAsync(page, fixture);
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                await LayoutAsync(page);
                var content = Get<Grid>(page, "SettingsContent");
                var began = false;
                var startReading = "未观察到动画开始";
                // 布局可能占满 UI 线程直到动画结束。紧盯父层从隐藏恢复的那一刻，
                // 不把定时器恢复得晚误判成没有动画；原来的淡入、位移断言保持不变。
                var watch = content.RegisterPropertyChangedCallback(UIElement.OpacityProperty, (_, _) =>
                {
                    if (content.Opacity != 1) return;
                    var starting = Get<List<FrameworkElement>>(page, "_enteringSections");
                    if (starting.Count == 0) return;
                    began |= starting.Any(section => section.Opacity < 1 && section.RenderTransform is TranslateTransform { Y: > 0 });
                    startReading = $"开始时分组 {starting.Count}，透明度 "
                        + string.Join(", ", starting.Select(section => section.Opacity.ToString("0.000")));
                });
                try
                {
                    page.SelectedCategory = "关于";
                    page.SelectedCategory = "字幕";
                    page.SelectedCategory = "视频输出";
                    await UntilAsync(() => content.Opacity == 1);
                }
                finally { content.UnregisterPropertyChangedCallback(UIElement.OpacityProperty, watch); }
                var entering = Get<List<FrameworkElement>>(page, "_enteringSections").ToArray();
                evidence.Add($"宽度 {width}：{startReading}；采样时分组 {entering.Length}");
                if (HomeMotion.AnimationsEnabled)
                {
                    Require(entering.Length > 0 && began, "设置分组没有实际淡入和上浮：" + startReading);
                    Require(entering.All(section => section.RenderTransform is TranslateTransform), "入场没有使用模板根位移");
                    Require(Get<Grid>(page, "SettingsLayout").Opacity == 1
                        && Get<Grid>(page, "SettingsContent").RenderTransform is not TranslateTransform,
                        "父层仍有叠加动画");
                }
                evidence.Add($"宽度 {width}：入场分组 {entering.Length}，透明度 "
                    + string.Join(", ", entering.Select(section => section.Opacity.ToString("0.000"))));
                await Task.Delay(430);
                AssertSettingsSettled(page);
                Require(page.SelectedCategory == "视频输出" && page.ViewModel.Sections.SequenceEqual(sections),
                    "快速切换回到了旧分类或重建了设置行");
                await SaveReturnFrameAsync(page, $"settings-motion-{width}.png");

                // 长页滚到底后切短页，不能留下空内容；只检查视口真正画到的分组。
                var scroll = Descendants(page).OfType<ScrollView>().Single();
                scroll.ScrollTo(0, scroll.ScrollableHeight,
                    new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                await Task.Delay(100);
                page.SelectedCategory = "关于";
                await Task.Delay(430);
                AssertSettingsSettled(page);
                var visible = Descendants(page).OfType<FrameworkElement>()
                    .Where(element => element.Name == "SettingsSection" && element.Visibility == Visibility.Visible).ToArray();
                Require(visible.Length == 1 && visible[0].ActualHeight > 0, "长页切短页没有实现目标分组");
                Require(scroll.VerticalOffset <= scroll.ScrollableHeight + 1, "切短页后滚动位置超出内容");

                // 窗口隐藏调用的释放入口，在排队或播放中都必须作废回调；之后仍能重新入场。
                page.SelectedCategory = "播放器";
                page.ReleaseHosted();
                await Task.Delay(80);
                AssertSettingsSettled(page);
                Require(Get<List<FrameworkElement>>(page, "_enteringSections").Count == 0, "隐藏后迟到回调重新播放动画");
                Call(page, "QueueSettingsEntrance");
                await UntilAsync(() => Get<Grid>(page, "SettingsContent").Opacity == 1);
                page.ReleaseHosted();
                AssertSettingsSettled(page);
            }

            // 真实托管 Frame 的父层不参与动画；无需构造播放服务或访问真实服务器。
            Field(page, "_request", new SettingsRequest(fixture.Services));
            page.SelectedCategory = "服务器";
            await LayoutAsync(page);
            Require(page.CurrentPage is ServersPage && Get<Grid>(page, "SettingsContent").Opacity == 1
                && Get<List<FrameworkElement>>(page, "_enteringSections").Count == 0, "托管页叠加了父层动画");
            page.ReleaseHosted();
            if (page.CurrentPage is Page { Content: FrameworkElement hosted })
                Require(hosted.Opacity == 1, "隐藏后托管页仍半透明");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "settings-motion.txt"), evidence);
        }
    }

    /// <summary>
    /// 「非管理员账号自动隐藏设置里的 EmbyServer 那一栏」：把假服务器报成普通账号，左栏那一组整个不出现，
    /// 紧凑窗口的下拉里也不列那五个分类；报回管理员时整组装回来，顺序照旧。
    /// <para>
    /// 值得一条探针，因为三件坏事在屏上都看不出来：两份名单（分组、平铺）只摘一份就是「宽窗口藏了、窄窗口还
    /// 列着」；收走之后右半边还停在被收走的那一页上（那页只会说「需要服务器管理员权限」，而左栏里已经没有
    /// 它）；以及装回来时顺序乱了。三样都得读名单和落点才认得出。
    /// </para>
    /// </summary>
    private async Task SettingsAdministrationGateAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-gate"));
        paths.EnsureCreated();
        // 同 SettingsEntranceAsync：本例不扫机器上的数千字体，给现有缓存放入空目录。
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));

        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Capabilities);
        await page.ViewModel.ReloadAsync();
        _root.Children.Add(page);
        try
        {
            await LayoutAsync(page);
            var rail = Get<ListView>(page, "CategoryNavigation");
            var picker = Get<ComboBox>(page, "CompactCategoryNavigation");

            Require(page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(new[] { "Momoka", "EmbyServer" }),
                "管理员账号下左栏没有「EmbyServer」那一组");

            // 先把落点摆在被收走的那一页上，好看出收走之后它有没有被收回来。
            page.SelectedCategory = SettingsViewModel.ServerDashboardCategory;
            await LayoutAsync(page);

            fixture.Capabilities.IsAdministrator = false;
            // 走页面那一条（OnNavigatedTo 里的那一句），不是直接喊视图模型：分组换表之后把
            // CollectionViewSource 重指一次这件事只在页面那头，漏了它屏上就留着一个空的组标题。
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);

            Require(page.ViewModel.ServerAdministrationHidden
                && page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(new[] { "Momoka" }),
                "普通账号下左栏仍留着「EmbyServer」那一组");
            Require(!Descendants(rail).OfType<TextBlock>().Any(text => text.Text == "EmbyServer"),
                "普通账号下左栏里「EmbyServer」那个组标题还留在屏上");
            Require(!page.Categories.Any(category =>
                    SettingsViewModel.ServerAdministrationCategories.Contains(category, StringComparer.Ordinal)),
                "普通账号下平铺名单仍列着服务器管理分类");
            Require(rail.Items.Cast<string>().SequenceEqual(page.Categories), "左栏分组与平铺名单对不上");
            Require(picker.ItemsSource is IEnumerable<string> entries && entries.SequenceEqual(page.Categories),
                "紧凑窗口的下拉没有跟着同一份名单走");
            Require(page.SelectedCategory == SettingsViewModel.FirstCardCategory,
                "收走那一组之后右半边还停在被收走的那一页上");
            await SaveReturnFrameAsync(page, "settings-server-admin-hidden.png");

            fixture.Capabilities.IsAdministrator = true;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);

            Require(!page.ViewModel.ServerAdministrationHidden
                && page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(new[] { "Momoka", "EmbyServer" }),
                "管理员身份回来之后那一组没有装回去");
            Require(Descendants(rail).OfType<TextBlock>().Any(text => text.Text == "EmbyServer"),
                "装回来之后左栏里没有「EmbyServer」那个组标题");
            Require(page.Categories.TakeLast(SettingsViewModel.ServerAdministrationCategories.Count)
                    .SequenceEqual(SettingsViewModel.ServerAdministrationCategories)
                && rail.Items.Cast<string>().SequenceEqual(page.Categories),
                "装回来的服务器管理分类次序不对");
            await SaveReturnFrameAsync(page, "settings-server-admin-shown.png");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
        }
    }

    private async Task SettingsAnimationToggleAsync(SettingsPage page, Fixture fixture)
    {
        var original = HomeMotion.PreferenceEnabled;
        var systemAnimations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        var player = new PlayerPage { IsHitTestVisible = false };
        try
        {
            page.SelectedCategory = "界面";
            await LayoutAsync(page);
            var toggle = Descendants(page).OfType<ToggleSwitch>()
                .Single(control => AutomationProperties.GetName(control) == "UI 动画");
            var peer = new ToggleSwitchAutomationPeer(toggle);
            Require(peer.GetName() == "UI 动画" && peer.GetPattern(PatternInterface.Toggle) is IToggleProvider,
                "UI 动画开关没有可访问名称或切换模式");
            var provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
            Require(toggle.IsOn, "新配置的 UI 动画没有默认开启");
            provider.Toggle();
            Require(!toggle.IsOn && !fixture.Settings.Ui.AnimationsEnabled && !HomeMotion.AnimationsEnabled
                && !fixture.SettingsService.HasUnsavedChanges, "关闭开关没有同步到配置、动画入口或磁盘");

            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                page.SelectedCategory = "视频输出";
                page.SelectedCategory = "界面";
                await LayoutAsync(page);
                AssertSettingsSettled(page);
                Require(Get<List<FrameworkElement>>(page, "_enteringSections").Count == 0,
                    "关闭动画后仍启动设置分组入场");
                await SaveReturnFrameAsync(page, $"settings-animation-off-{width}.png");
            }

            // 假宿主不附加播放服务，只验证 UI；不加载媒体，也不下发播放或倍速命令。
            player.Visibility = Visibility.Visible;
            _root.Children.Add(player);
            await LayoutAsync(player);
            Call(player, "Pulse", true);
            Require(Get<Border>(player, "PulseBadge").Opacity == 1
                && Get<ScaleTransform>(player, "PulseScale").ScaleX == 1,
                "关闭动画后暂停提示没有静态显示");
            await UntilAsync(() => Get<Border>(player, "PulseBadge").Visibility == Visibility.Collapsed);
            Call(player, "AnimateWheelTo", 3);
            Require(Get<double>(player, "_wheelPosition") == 3 && !Get<bool>(player, "_wheelAnimating"),
                "关闭动画后倍速刻度没有立即落定");
            Call(player, "Render");
            Require(Get<Grid>(player, "TitleStrip").OpacityTransition.Duration == TimeSpan.Zero
                && Get<Grid>(player, "TransportRow").OpacityTransition.Duration == TimeSpan.Zero
                && Get<Border>(player, "Rail").OpacityTransition.Duration == TimeSpan.Zero,
                "关闭动画后播放器仍有隐式淡入");

            provider.Toggle();
            Require(toggle.IsOn && fixture.Settings.Ui.AnimationsEnabled
                && HomeMotion.AnimationsEnabled == systemAnimations, "重新开启没有遵循系统动画设置");
            Call(player, "Render");
            Require(Get<Grid>(player, "TitleStrip").OpacityTransition.Duration ==
                (systemAnimations ? TimeSpan.FromMilliseconds(220) : TimeSpan.Zero), "重新开启没有恢复过渡时长");
            _root.Children.Remove(player);
            await Task.Delay(250); // 原生 ToggleSwitch 的滑块先完成状态过渡，再留截图。
            await SaveReturnFrameAsync(page, "settings-animation-on-760.png");
        }
        finally
        {
            Call(player, "HidePulse");
            _root.Children.Remove(player);
            player.ReleaseVideoSurface();
            HomeMotion.ApplyPreference(original);
        }
    }

    private static void AssertSettingsSettled(SettingsPage page)
    {
        Require(Get<Grid>(page, "SettingsContent").Opacity == 1, "设置内容停留在透明状态");
        foreach (var section in Descendants(page).OfType<FrameworkElement>().Where(element => element.Name == "SettingsSection"))
            Require(section.Opacity == 1 && (section.RenderTransform is not TranslateTransform drift || drift.Y == 0),
                "设置分组未归位");
    }
}

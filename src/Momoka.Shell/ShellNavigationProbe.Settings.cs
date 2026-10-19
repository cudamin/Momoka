using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Platform;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Theming;
using Momoka.Updates;
using Microsoft.Extensions.DependencyInjection;
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
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings), new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()),
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
                await WaitForSettingsSettledAsync(page);
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
                await WaitForSettingsSettledAsync(page);
                AssertSettingsSettled(page);
                // ScrollView 在合成提交后才按新 extent 钳制偏移；分组动画结束不代表滚动操作已结束。
                await UntilAsync(() => scroll.VerticalOffset <= scroll.ScrollableHeight + 1);
                var visible = Descendants(page).OfType<FrameworkElement>()
                    .Where(element => element.Name == "SettingsSection" && element.Visibility == Visibility.Visible).ToArray();
                Require(visible.Length == 1 && visible[0].ActualHeight > 0, "长页切短页没有实现目标分组");
                Require(scroll.VerticalOffset <= scroll.ScrollableHeight + 1, "切短页后滚动位置超出内容");

                // 「设置 → 关于」新增的两行（「启动时自动检查新版本」那一格与那颗手动「检查新版本」按钮，
                // 2026-10-14）拍下来：这一页是另一扇窗口，而设置窗口的原生 Title 是空的，tools/shot.ps1 够不着它
                // （见 docs/开发与验证.md），所以宽窄两张只能由探针自己拍。摆在切到「关于」之后、回「播放器」之前。
                scroll.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"settings-about-{width}-screen.png");

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

            // 渲染管线行住进基础输出卡之后，切管线重建的就是这一行自己住的卡 —— 祖先容器压在
            // SelectionChanged 的传播路上被换掉，从前没有这条路径（此前的重建都发生在别的卡上）。
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                await SettingsPipelineRowAsync(page, fixture, width);
            }

            // 播放器分类 2026-10-11 起一或两张分区卡（播放器 ＋ 随独占模式进出的自定义 mpv.conf；外置两行
            // 同日并回播放器卡、仅外部后端在场）；同日用户令把截图分区挪进播放行为，那一类三张分区卡
            // （播放行为／资源筛选／截图）。宽窄两档各留同屏帧：顶帧在第一张卡，另两张把对应分区滚进视口。
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                page.SelectedCategory = "播放器";
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page); // 分组入场动画先落定，别处同款等待。
                AssertSettingsSettled(page);
                await SaveReturnFrameAsync(page, $"settings-player-sections-{width}.png");
                page.SelectedCategory = "播放行为";
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                AssertSettingsSettled(page);
                await SaveReturnFrameAsync(page, $"settings-playback-sections-{width}.png");
                foreach (var (title, file) in new[] { ("资源筛选", $"settings-playback-resource-{width}.png"), ("截图", $"settings-playback-screenshot-{width}.png") })
                {
                    // 滚动条不在行数据里，把目标分区整个带进视口再拍。VerticalAlignmentRatio=0 让分区顶边
                    // 对到视口顶 —— 默认的最小滚动只把标题滚到视口底缘，拍到的还是上一张卡的尾巴。
                    var section = Descendants(page).OfType<FrameworkElement>()
                        .Where(element => element.Name == "SettingsSection" && element.Visibility == Visibility.Visible)
                        .First(element => Descendants(element).OfType<TextBlock>().Any(text => text.Text == title));
                    section.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                    await LayoutAsync(page);
                    AssertSettingsSettled(page);
                    await SaveReturnFrameAsync(page, file);
                }
            }
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
    /// 设置 → 主页 → 「主页媒体库位置」（用户 2026-10-12 令、2026-10-16 改名并挪进主页卡）：三档下拉，装机默认是
    /// 「随窗口高度变换」。
    /// <para>
    /// 值得一条探针，因为这一行坏掉的样子在屏上都很安静：下拉里少一项读起来像「这功能就这样」；控件没有落在
    /// 存档里那一档上（<see cref="SettingChoiceRow"/> 的候选表认不出存档里的值时会给一个「设置文件中的值」的
    /// 兜底项），用户下次打开设置看到的就不是自己选的那一档；而选一下没有写回设置，主页那一头读到的还是旧值 ——
    /// 三样都得读存档和真实控件才认得出。主页卡另留宽窄两帧截图。
    /// </para>
    /// </summary>
    private async Task SettingsHomeLibraryPlacementAsync()
    {
        const string label = "主页媒体库位置";
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-library-placement"));
        paths.EnsureCreated();
        // 同 SettingsEntranceAsync：本例不扫机器上的数千字体，给现有缓存放入空目录。
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));

        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()), fixture.Capabilities);
        await page.ViewModel.ReloadAsync();
        _root.Children.Add(page);
        try
        {
            page.SelectedCategory = "主页";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            AssertSettingsSettled(page);

            var row = page.ViewModel.Sections.Where(section => section.Category == "主页")
                .SelectMany(section => section.Rows)
                .OfType<SettingChoiceRow>().Single(candidate => candidate.Label == label);
            var combo = Descendants(page).OfType<ComboBox>()
                .Single(control => AutomationProperties.GetName(control) == label);

            Require(row.Choices.Count == 3 && combo.Items.Count == 3,
                $"「{label}」不是三档：数据行 {row.Choices.Count} 项、控件 {combo.Items.Count} 项");
            Require(row.Choices.Select(choice => choice.Label)
                    .SequenceEqual(["随窗口高度变换", "固定在轮播图中", "固定在轮播图下方"]),
                "「主页媒体库位置」三档的名字或次序变了：" + string.Join("、", row.Choices.Select(choice => choice.Label)));

            // 装机默认那一档：存档里是 Auto，控件显示的也是它（不是那个「设置文件中的值」的兜底项）。
            Require(fixture.Settings.Ui.HomeLibraryPlacement == HomeLibraryPlacement.Auto, "主页卡的默认档不是「随窗口高度变换」");
            Require(ReferenceEquals(combo.SelectedItem, row.Selected), "控件没有落在存档里那一档上");

            // 逐档拨过去，存档跟着走 —— 主页那一头读的就是这一个字段（ShellPrefs 那一趟不在本例里验，
            // 它由主页用例真拨一次设置来覆盖）。
            foreach (var (text, value) in new[]
            {
                ("固定在轮播图中", HomeLibraryPlacement.OnCarousel),
                ("固定在轮播图下方", HomeLibraryPlacement.BelowCarousel),
                ("随窗口高度变换", HomeLibraryPlacement.Auto)
            })
            {
                combo.SelectedItem = row.Choices.Single(choice => choice.Label == text);
                await Task.Delay(80);
                Require(fixture.Settings.Ui.HomeLibraryPlacement == value, $"选「{text}」没有写回设置");
            }

            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                AssertSettingsSettled(page);
                combo.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"settings-home-placement-{width}-screen.png");
            }
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
    }

    /// <summary>
    /// 设置 → 界面 → 「开机自启」：这一行和这一页别的行都不一样 —— 它写进去的不是设置文件，而是机器上的启动项
    /// （HKCU 的 Run），所以它坏掉的样子几乎全在屏外：开关拨了没写下去（屏上照样亮着、机器上什么都没变）、写不
    /// 下去时开关不肯拨回去（用户以为改成了）、初值不是问机器而是写死一个「关」（机器上开着，屏上却是关的），以及
    /// 打包安装那一档下这一行本该整个不显示却还在。
    /// <para>
    /// 驱动的是真控件、真那一行，接的却是假实现：探针绝不能真写 HKCU 里那条启动项 —— 那会在跑探针这台机器上
    /// 留下一条真的开机自启，而那是用户机器上的登记，不是探针该动的东西。
    /// </para>
    /// </summary>
    private async Task SettingsStartupRegistrationAsync()
    {
        const string label = "开机自启";
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-startup"));
        paths.EnsureCreated();
        // 同 SettingsEntranceAsync：本例不扫机器上的数千字体，给现有缓存放入空目录。
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));

        // 先把它摆成「机器上开着」：这一行的初值只能来自那台机器的事实，写死一个「关」在这一步就露馅。
        Require(fixture.Startup.Apply(true) && fixture.Startup.IsEnabled, "假启动项没有摆成开着");

        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()), fixture.Capabilities);
        await page.ViewModel.ReloadAsync();
        _root.Children.Add(page);
        try
        {
            page.SelectedCategory = "界面";
            await SettleAsync();

            ToggleSwitch Control() => Descendants(page).OfType<ToggleSwitch>()
                .Single(control => AutomationProperties.GetName(control) == label);

            SettingToggleRow Row() => page.ViewModel.Sections
                .Where(section => section.Category == "界面")
                .SelectMany(section => section.Rows)
                .OfType<SettingToggleRow>()
                .Single(candidate => candidate.Label == label);

            Require(Row().IsOn && Control().IsOn, "机器上开着，这一行却没有读成开");
            Require(Row().Note.Contains("启动项", StringComparison.Ordinal)
                && Row().Note.Contains("设置文件", StringComparison.Ordinal),
                "这一行的说明没写明它改的是机器上的启动项、不进设置文件：" + Row().Note);

            // 关掉：机器上那一条要真被撤掉，屏上两处跟着走。
            fixture.Startup.Requests.Clear();
            Control().IsOn = false;
            await SettleAsync();
            Require(fixture.Startup.Requests.SequenceEqual([false]) && !fixture.Startup.IsEnabled,
                $"拨到关没有写下去（收到 {string.Join('、', fixture.Startup.Requests)}）");
            Require(!Row().IsOn && !Control().IsOn, "拨到关之后屏上没有跟着走");

            // 再打开。
            fixture.Startup.Requests.Clear();
            Control().IsOn = true;
            await SettleAsync();
            Require(fixture.Startup.Requests.SequenceEqual([true]) && fixture.Startup.IsEnabled, "拨到开没有写下去");
            Require(Row().IsOn && Control().IsOn, "拨到开之后屏上没有跟着走");

            // 写不下去（组策略挡住、注册表只读、安全软件拦下）：开关必须拨回去并说一句，
            // 而不是停在用户拨的那一档 —— 那正是这一页最怕的「界面在骗人」。
            fixture.Startup.Refuse = true;
            Control().IsOn = false;
            await SettleAsync();
            Require(fixture.Startup.IsEnabled, "假启动项在被拒绝时仍改了状态（这一条用例本身就假了）");
            Require(Row().IsOn && Control().IsOn, "写不下去时开关没有拨回真正生效的那一档");
            Require(page.ViewModel.NoticeOpen && page.ViewModel.NoticeSeverity == InfoBarSeverity.Error
                && (page.ViewModel.NoticeMessage ?? "").Contains("没能关掉", StringComparison.Ordinal),
                $"写不下去时没有说一句：开={page.ViewModel.NoticeOpen}、话=「{page.ViewModel.NoticeMessage}」");
            fixture.Startup.Refuse = false;

            // 打包安装那一档：这一条办不到，整行不显示（按项目规则整个收起，不是置灰）。
            fixture.Startup.Available = false;
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "界面";
            await SettleAsync();
            Require(!Rows().Any(row => row.Label == label), "打包安装那一档下这一行的数据行还在");
            Require(!Descendants(page).OfType<Control>().Any(control => AutomationProperties.GetName(control) == label)
                && !Descendants(page).OfType<TextBlock>().Any(text => text.Text == label),
                "打包安装那一档下这一行的控件或标签还在屏上");

            // 装回来，并留一张宽窄两档的实屏。
            fixture.Startup.Available = true;
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "界面";
            await SettleAsync();
            Require(Rows().Any(row => row.Label == label), "办得到的时候这一行没有装回来");

            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                await SettleAsync();
                Control().StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 });
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"settings-interface-startup-{width}-screen.png");
            }

            IReadOnlyList<SettingRow> Rows() => [.. page.ViewModel.Sections
                .Where(section => section.Category == "界面")
                .SelectMany(section => section.Rows)];

            async Task SettleAsync()
            {
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                AssertSettingsSettled(page);
            }
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
    }

    /// <summary>
    /// 管理员入口与三段服务器目录：Emby 管理员左栏是「Momoka」「Emby Server」「设备」「高级」，目录里有
    /// 固定条目与服务器插件页，且不再单列「网页控制台」；非管理员整份收起；Jellyfin 仍只有「网页控制台」，
    /// 飞牛影视没有管理入口；切产品或退出连接时落点收回第一张卡。分组与平铺两份名单必须同步。
    /// </summary>
    private async Task SettingsAdministrationGateAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        fixture.Capabilities.AdminPages =
        [
            new() { Name = "cinemaintros", DisplayName = "Cinema Intros", Href = "configurationpage?name=cinemamode", MenuSection = "server", MenuIcon = "movie", EnableInMainMenu = true },
            new() { Name = "customcssjs", DisplayName = "CustomCssJS Provider", Href = "configurationpage?name=customcssjs", MenuSection = "server", MenuIcon = "code", EnableInMainMenu = true },
            new() { Name = "dlna", DisplayName = "DLNA", Href = "configurationpage?name=dlna", MenuSection = "devices", MenuIcon = "devices", EnableInMainMenu = true },
            new() { Name = "backup", DisplayName = "Backup & Restore", Href = "configurationpage?name=backup", MenuSection = null, EnableInMainMenu = true }
        ];
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-gate"));
        paths.EnsureCreated();
        // 同 SettingsEntranceAsync：本例不扫机器上的数千字体，给现有缓存放入空目录。
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));

        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()), fixture.Capabilities);
        await page.ViewModel.ReloadAsync();
        // 探针直接挂页面，不经过 SettingsPage.OnNavigatedTo；这里走页面那一层刷新，
        // 它除了 ViewModel 还会把分组用的 CollectionViewSource 换到新表。
        await (Task)Call(page, "RefreshServerAdministrationAsync")!;
        _root.Children.Add(page);
        try
        {
            await LayoutAsync(page);
            var rail = Get<ListView>(page, "CategoryNavigation");
            var picker = Get<ComboBox>(page, "CompactCategoryNavigation");
            var embyGroups = new[] { "Momoka", "Emby Server", "设备", "高级" };

            // Emby 管理员：Momoka 自己的设置在最上面，服务器三段目录接在下面，「网页控制台」不再单列。
            Require(page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(embyGroups),
                "Emby 管理员左栏不是 Momoka 在前、三段服务器目录在后");
            Require(!page.Categories.Contains(SettingsViewModel.DashboardCategory),
                "Emby 仍单列「网页控制台」");
            Require(page.Categories.Contains("控制台") && page.Categories.Contains("通用")
                && page.Categories.Contains("Cinema Intros") && page.Categories.Contains("DLNA")
                && page.Categories.Contains("Backup & Restore"),
                "服务器目录或插件页没有进左栏");
            Require(!page.Categories.Any(category => category.Contains("CustomCssJS", StringComparison.OrdinalIgnoreCase)),
                "CustomCssJS 插件页仍在左栏");
            Require(SitsBeforeLastCard(
                    page.ViewModel.CategoryGroups.Single(group => group.Name == "Momoka"),
                    new[] { SettingsViewModel.NotificationsCategory }),
                "「通知」没有排在 诊断 与 关于 中间");
            Require(rail.Items.Cast<string>().SequenceEqual(page.Categories), "左栏分组与平铺名单对不上");
            Require(page.ViewModel.ConsoleCategory == "控制台"
                && page.ViewModel.ServerRoute("控制台") == "dashboard"
                && page.ViewModel.ServerRoute("通用") == "dashboard/settings",
                "服务器页没有映射到网页路由");

            // 选中一个服务器页，权限收走后必须退回第一张卡。
            page.SelectedCategory = "控制台";
            await LayoutAsync(page);

            fixture.Capabilities.IsAdministrator = false;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);
            Require(page.ViewModel.ServerAdministrationHidden
                && page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(new[] { "Momoka" }),
                "普通账号下左栏仍留着服务器目录");
            Require(!page.Categories.Any(category => category == SettingsViewModel.NotificationsCategory
                    || category == "控制台" || category == "Cinema Intros" || category == SettingsViewModel.DashboardCategory),
                "普通账号下平铺名单仍列着管理页");
            Require(rail.Items.Cast<string>().SequenceEqual(page.Categories), "左栏分组与平铺名单对不上");
            Require(picker.ItemsSource is IEnumerable<string> hiddenEntries && hiddenEntries.SequenceEqual(page.Categories),
                "紧凑窗口的下拉没有跟着同一份名单走");
            Require(page.SelectedCategory == SettingsViewModel.FirstCardCategory,
                "收走管理页之后右半边还停在被收走的页上");
            await SaveReturnFrameAsync(page, "settings-server-admin-hidden.png");

            fixture.Capabilities.IsAdministrator = true;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);
            Require(!page.ViewModel.ServerAdministrationHidden
                && page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(embyGroups)
                && page.Categories.Contains("控制台"),
                "管理员身份回来之后三段目录没有按原次序装回去");
            Require(rail.Items.Cast<string>().SequenceEqual(page.Categories), "左栏分组与平铺名单对不上");
            await SaveReturnFrameAsync(page, "settings-server-admin-shown.png");

            // Jellyfin：换成它自己的五段目录，服务器页与 Emby 的分区、标签、插件页不混用。
            page.SelectedCategory = "控制台";
            fixture.Capabilities.ServerKind = MediaServerKind.Jellyfin;
            fixture.Capabilities.InterfaceFlavor = ServerInterfaceFlavor.Jellyfin;
            fixture.Capabilities.AdminPages =
            [
                new() { Name = "customcssjs", DisplayName = "CustomCssJS Provider", Href = "configurationpage?name=customcssjs", EnableInMainMenu = true },
                new() { Name = "jellyfinplugin", DisplayName = "Jellyfin 插件页", Href = "configurationpage?name=jellyfinplugin", EnableInMainMenu = true }
            ];
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);
            var jellyfinGroups = new[] { "Momoka", "服务器", "设备", "电视直播", "插件", "高级" };
            Require(page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(jellyfinGroups),
                "连接 Jellyfin 后左栏不是五段 Jellyfin 目录");
            Require(!page.Categories.Contains(SettingsViewModel.DashboardCategory)
                && !page.Categories.Contains(SettingsViewModel.NotificationsCategory),
                "Jellyfin 仍列着 Emby 或旧版「网页控制台」入口");
            Require(page.Categories.Contains("控制台") && page.Categories.Contains("常规")
                && page.Categories.Contains("用户") && page.Categories.Contains("媒体库")
                && page.Categories.Contains("转码") && page.Categories.Contains("数字录像机")
                && page.Categories.Contains("我的插件") && page.Categories.Contains("目录")
                && page.Categories.Contains("计划任务") && page.Categories.Contains("媒体资料管理器")
                && page.Categories.Contains("Jellyfin 插件页"),
                "Jellyfin 固定条目、插件页或媒体资料管理器没有进左栏");
            Require(!page.Categories.Any(category => category.Contains("CustomCssJS", StringComparison.OrdinalIgnoreCase)),
                "Jellyfin 仍列出 CustomCssJS 插件页");
            Require(page.ViewModel.ConsoleCategory == "控制台"
                && page.ViewModel.ServerRoute("控制台") == "dashboard"
                && page.ViewModel.ServerRoute("常规") == "dashboard/settings"
                && page.ViewModel.ServerRoute("媒体资料管理器") == "metadata"
                && page.ViewModel.ServerRoute("数字录像机") == "dashboard/recordings",
                "Jellyfin 服务器页没有映射到它自己的网页路由");
            Require(page.SelectedCategory == "控制台"
                && page.ViewModel.ServerRoute("控制台") == "dashboard",
                "换成 Jellyfin 后「控制台」没有留在原位并指向 Jellyfin 路由");
            Require(rail.Items.Cast<string>().SequenceEqual(page.Categories), "Jellyfin 左栏分组与平铺名单对不上");
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                await LayoutAsync(page);
                Require(rail.Items.Cast<string>().SequenceEqual(page.Categories)
                    && picker.ItemsSource is IEnumerable<string> jellyfinEntries && jellyfinEntries.SequenceEqual(page.Categories),
                    "Jellyfin 宽窄导航分类不一致");
                await SaveProbeScreenAsync($"settings-jellyfin-admin-{width}-screen.png");
            }

            foreach (bool? administrator in new bool?[] { false, null })
            {
                fixture.Capabilities.IsAdministrator = administrator;
                await (Task)Call(page, "RefreshServerAdministrationAsync")!;
                await LayoutAsync(page);
                Require(page.ViewModel.ServerAdministrationHidden
                    && !page.Categories.Any(category => category == SettingsViewModel.DashboardCategory
                        || category == SettingsViewModel.NotificationsCategory
                        || category == "控制台")
                    && page.ViewModel.ConsoleCategory is null,
                    "Jellyfin 非管理员或权限未知时仍暴露管理入口");
            }
            await SaveProbeScreenAsync("settings-jellyfin-user-screen.png");

            // 飞牛影视没有管理入口。
            fixture.Capabilities.ServerKind = MediaServerKind.FnNas;
            fixture.Capabilities.InterfaceFlavor = ServerInterfaceFlavor.Generic;
            fixture.Capabilities.IsAdministrator = true;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);
            Require(page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(new[] { "Momoka" })
                && !page.Categories.Any(category => category is "控制台" or "用户" or "媒体库" or "设置" or "任务计划")
                && !page.Categories.Contains(SettingsViewModel.DashboardCategory),
                "飞牛影视仍列出管理入口");
            Require(page.ViewModel.ConsoleCategory is null
                && !page.Categories.Contains(SettingsViewModel.NotificationsCategory),
                "飞牛影视仍暴露 Emby 管理入口");
            foreach (var width in new[] { 1100, 760 })
            {
                ResizeInspect(width, 900);
                await LayoutAsync(page);
                Require(rail.Items.Cast<string>().SequenceEqual(page.Categories)
                    && picker.ItemsSource is IEnumerable<string> fnNasEntries && fnNasEntries.SequenceEqual(page.Categories),
                    "飞牛影视宽窄导航分类不一致");
                await SaveProbeScreenAsync($"settings-fnnas-{width}-screen.png");
            }

            // 回 Emby：三段目录与「控制台」回来。
            fixture.Capabilities.ServerKind = MediaServerKind.Emby;
            fixture.Capabilities.InterfaceFlavor = ServerInterfaceFlavor.Emby;
            fixture.Capabilities.AdminPages =
            [
                new() { Name = "cinemaintros", DisplayName = "Cinema Intros", Href = "configurationpage?name=cinemamode", MenuSection = "server", MenuIcon = "movie", EnableInMainMenu = true }
            ];
            fixture.Capabilities.IsAdministrator = true;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);
            Require(page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(embyGroups)
                && page.Categories.Contains("控制台")
                && SitsBeforeLastCard(
                    page.ViewModel.CategoryGroups.Single(group => group.Name == "Momoka"),
                    new[] { SettingsViewModel.NotificationsCategory }),
                "返回 Emby 后三段目录没有按原次序恢复");

            fixture.Capabilities.IsConnected = false;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            Require(page.ViewModel.ServerAdministrationHidden, "退出连接后仍暴露管理入口");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
        }
    }

    /// <summary>
    /// 服务器页的连接模式：两台服务器在线时是通用界面，切到独占后只留一台并回到那一台的界面。
    /// 切走的那台令牌不能在模式收口时被清掉 —— 用户只是切走了，账号以后还要能直接登录。
    /// </summary>
    private async Task ServerAccessModesAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        await fixture.SelectAsync("b.invalid", "b");

        // 探针窗口里页面入场动画不跑完时，实屏截图会拍到上一页的残影；与服务器控制台那条一样关掉动画再等落定。
        var originalAnimations = HomeMotion.PreferenceEnabled;
        HomeMotion.ApplyPreference(false);
        var page = new ServersPage();
        _root.Children.Add(page);
        try
        {
            ResizeInspect(1100, 900);
            page.ViewModel.Attach(fixture.Actions, fixture.SettingsService, fixture.Session, fixture.Images);
            await page.ViewModel.ReloadAsync();
            await LayoutAsync(page);
            await UntilAsync(() => page.Opacity == 1 && Get<Grid>(page, "PageLayout").Opacity == 1);
            await Task.Delay(160);
            Require(_root.Children.Count == 1 && ReferenceEquals(_root.Children[0], page),
                $"服务器页探针的根里还留着别的页（{_root.Children.Count} 个直接孩子）");

            Require(fixture.Session.Sources.Count == 2, "两台服务器没有同时上线，模式探针起点不对");
            Require(page.ViewModel.AggregateMode && !page.ViewModel.ExclusiveMode, "默认不是聚合模式");
            Require(Get<RadioButton>(page, "AggregateModeRadio").IsChecked == true, "聚合模式单选没有选中");
            Require(page.ViewModel.InterfaceTitle == "通用界面",
                $"两台服务器在线时界面应退回通用，实际是「{page.ViewModel.InterfaceTitle}」");
            // 假身份页面里实屏截图会拍到上一张控制台页的残影（2026-10-19 多次复现，已记在 PROGRESS），
            // 服务器页的证据用页面渲染图；它是渲染结果，不冒充实屏。
            await SaveRenderAsync(page, "servers-access-modes-aggregate-render.png");

            var prunedAccount = fixture.Settings.Servers
                .SelectMany(server => server.Accounts)
                .First(account => !ReferenceEquals(account, fixture.Session.Account));
            page.ViewModel.UseExclusiveCommand.Execute(null);
            await UntilAsync(() => fixture.Session.Sources.Count == 1);
            await LayoutAsync(page);
            await Task.Delay(160);

            Require(fixture.Settings.ServerAccess.Mode == ServerConnectionMode.Exclusive, "独占模式没有写进设置");
            Require(page.ViewModel.ExclusiveMode && !page.ViewModel.AggregateMode, "拨完独占模式后单选状态不对");
            Require(Get<RadioButton>(page, "ExclusiveModeRadio").IsChecked == true, "独占模式单选没有选中");
            Require(page.ViewModel.InterfaceTitle == "Emby 界面",
                $"只剩一台服务器时界面应回到 Emby，实际是「{page.ViewModel.InterfaceTitle}」");
            Require(fixture.Session.Sources.Select(source => source.ServerProfileId).Distinct().Count() == 1,
                "独占模式仍有一台以上服务器在线");
            Require(prunedAccount.HasSavedToken,
                "模式收口把另一台服务器的已保存令牌清掉了");
            await SaveRenderAsync(page, "servers-access-modes-exclusive-render.png");

            ResizeInspect(760, 900);
            await LayoutAsync(page);
            await LayoutAsync(page);
            await SaveRenderAsync(page, "servers-access-modes-exclusive-760-render.png");
            ResizeInspect(1100, 900);
            await LayoutAsync(page);

            page.ViewModel.UseAggregateCommand.Execute(null);
            Require(fixture.Settings.ServerAccess.Mode == ServerConnectionMode.Aggregate, "切回聚合模式没有写进设置");
            Require(fixture.Session.Sources.Count == 1, "切回聚合不应自动重连被收口的来源");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            HomeMotion.ApplyPreference(originalAnimations);
            ResizeInspect(1420, 980);
        }
    }

    /// <summary>
    /// Jellyfin 控制台不再画第二条左栏：设置页外层给出路由，这一页直接落到 Jellyfin Web 的
    /// <c>#/dashboard/…</c> 地址。不启动 WebView2，只看落点和网页地址。
    /// </summary>
    private async Task JellyfinConsoleNavigationAsync()
    {
        using var fixture = CreateFixture();
        fixture.Transport.Reply = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return Task.FromResult(Json(new
                {
                    Id = "jf-server",
                    ServerName = "离线 Jellyfin",
                    ProductName = "Jellyfin",
                    Version = "12.2.0"
                }));
            if (path.EndsWith("/Users/Me", StringComparison.Ordinal))
                return Task.FromResult(Json(new
                {
                    Id = "jf",
                    Name = "jf",
                    Policy = new { IsAdministrator = true }
                }));
            if (path.EndsWith("/System/Info", StringComparison.Ordinal))
                return Task.FromResult(Json(new { ServerName = "离线 Jellyfin", Version = "12.2.0" }));
            // Jellyfin 12 的媒体库列表走顶层 UserViews，不是 Emby 的 Users/{id}/Views。
            if (path.EndsWith("/UserViews", StringComparison.Ordinal))
                return Task.FromResult(Json(new { Items = Array.Empty<object>() }));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        await fixture.SelectAsync("jellyfin.invalid", "jf", MediaServerKind.Jellyfin);

        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "jf-console-nav-" + Guid.NewGuid().ToString("N")));
        paths.EnsureCreated();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(fixture.Session)
            .AddSingleton(fixture.Launcher)
            .AddSingleton(paths)
            .BuildServiceProvider();

        var page = new DashboardPage();
        _root.Children.Add(page);
        try
        {
            page.Attach(new DashboardRequest(services, "dashboard/libraries", LoadContent: false));
            await UntilAsync(() => page.IsReady);
            Require(page.CurrentRoute == "dashboard/libraries",
                $"Jellyfin 落点不是外层给的路由：{page.CurrentRoute}");
            Require(page.CurrentNavigationAddress.Contains("/web/index.html#/dashboard/libraries", StringComparison.Ordinal),
                $"Jellyfin 路由没有拼成 Jellyfin Web 地址（{page.CurrentNavigationAddress}）");
            Require(page.FindName("ServerSettingsNavList") is null
                && page.FindName("CompactNavPicker") is null,
                "Jellyfin 控制台仍画着自己那条内层左栏");
            await SaveRenderAsync(page, "settings-console-jellyfin-route-render.png");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
    }

    /// <summary>
    /// 把这一页（不是整个窗口）渲染成 PNG。控制台页的探针不启动 WebView2，屏幕截图在这种假窗口里
    /// 会拍到前一张设置页的残影（2026-10-19 实测）；这条渲染图只作左栏布局的辅助证据，不冒充实屏。
    /// </summary>
    private async Task SaveRenderAsync(FrameworkElement element, string name)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var buffer = await bitmap.GetPixelsAsync();
        var pixels = new byte[buffer.Length];
        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);

        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
            Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var output = File.Create(Path.Combine(_options.Paths.LogDirectory, name));
        await stream.AsStreamForRead().CopyToAsync(output);
    }

    /// <summary>
    /// 管理员入口是不是正好落在最后一张卡（关于）前面、且次序与 <paramref name="expected"/> 一致。
    /// 用户令 2026-10-16 那句「通知和网页控制台放到诊断和关于之间」量的就是这件事。
    /// </summary>
    private static bool SitsBeforeLastCard(IReadOnlyList<string> categories, IReadOnlyList<string> expected) =>
        categories.Count > expected.Count + 1
        && categories[^1] == SettingsViewModel.LastCardCategory
        && categories.Skip(categories.Count - 1 - expected.Count).Take(expected.Count).SequenceEqual(expected);

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

    private static Task WaitForSettingsSettledAsync(SettingsPage page) => UntilAsync(() =>
        Get<Grid>(page, "SettingsContent").Opacity == 1
        && Descendants(page).OfType<FrameworkElement>().Where(element => element.Name == "SettingsSection")
            .All(section => section.Opacity == 1 && (section.RenderTransform is not TranslateTransform drift || drift.Y == 0)));

    private static void AssertSettingsSettled(SettingsPage page)
    {
        Require(Get<Grid>(page, "SettingsContent").Opacity == 1, "设置内容停留在透明状态");
        foreach (var section in Descendants(page).OfType<FrameworkElement>().Where(element => element.Name == "SettingsSection"))
            Require(section.Opacity == 1 && (section.RenderTransform is not TranslateTransform drift || drift.Y == 0),
                "设置分组未归位");
    }

    // 切管线必须当场重排基础输出卡（集成锁定 D3D11，独占恢复接口选择），而重建的恰好是渲染管线
    // 那一行自己住的卡。这条探针在真实控件上走一遍事件链：对着真下拉写入另一档 SelectedItem，触发与用户
    // 点选同一条 SelectionChanged → 双向绑定 → after 重建（这次重建发生在祖先容器上）的路，断在半路当场报红。
    // 边界照实说：机器注不进鼠标事件，弹层开不了，「从弹层里点条目」那一段没有覆盖；其余各步与用户路径
    // 共用同一根线（同上头 UI 动画开关那条的道理）。管线存值落在探针自己的隔离目录，不碰真实设置。
    private async Task SettingsPipelineRowAsync(SettingsPage page, Fixture fixture, int width)
    {
        var original = fixture.Settings.Mpv.Pipeline;
        var originalApi = fixture.Settings.Video.GpuApi;
        try
        {
            Require(fixture.Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv, "管线探针假定内置后端");
            page.SelectedCategory = "视频输出";
            await LayoutAsync(page);

            // 出发前的两态要与存值对得上，否则探针照到的是别的坏处。
            var integrated = fixture.Settings.Mpv.Pipeline == VideoPipelineKind.Integrated;
            AssertGraphicsApi(page, fixture);

            // 档位次序是 Pipelines 表的次序（集成在前、独占在后），按下标挑另一档。
            var (row, combo) = PipelineRow(page);
            combo.SelectedItem = row.Choices[integrated ? 1 : 0];
            // 数据集合先变，XAML 容器在后续布局才替换；旧树也可能恰好处于“不在动画”的状态。
            await UntilAsync(() => !Descendants(page).Any(element => ReferenceEquals(element, combo)));
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page); // 重建可能带分组入场动画，与别处一样等它落定再读。
            Require(fixture.Settings.Mpv.Pipeline == (integrated ? VideoPipelineKind.Standalone : VideoPipelineKind.Integrated),
                "切管线没有保存新选择");
            AssertGraphicsApi(page, fixture);
            Require(fixture.Settings.Video.GpuApi == originalApi, "集成接口覆盖了独占接口偏好");
            AssertSettingsSettled(page);
            await SaveReturnFrameAsync(page, $"settings-pipeline-switched-{width}.png");
            await SaveProbeScreenAsync($"settings-pipeline-switched-{width}-screen.png");

            // 重建把整张卡的行和下拉换成了新对象，切回去要重新找；顺带证明确实换了新树。
            var (restoredRow, restoredCombo) = PipelineRow(page);
            Require(!ReferenceEquals(restoredCombo, combo), "重建没有换出新的下拉");
            restoredCombo.SelectedItem = restoredRow.Choices[integrated ? 0 : 1];
            await UntilAsync(() => !Descendants(page).Any(element => ReferenceEquals(element, restoredCombo)));
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            Require(fixture.Settings.Mpv.Pipeline == original, "切回原管线没有保存选择");
            AssertGraphicsApi(page, fixture);
            Require(fixture.Settings.Video.GpuApi == originalApi, "切回后丢失了原先的图形接口");
            AssertSettingsSettled(page);
            await SaveReturnFrameAsync(page, $"settings-pipeline-restored-{width}.png");
            await SaveProbeScreenAsync($"settings-pipeline-restored-{width}-screen.png");
        }
        finally
        {
            // 探针中断时的保险：存值归位，别把半截状态留给同目录后面的探针步骤。
            fixture.Settings.Mpv.Pipeline = original;
        }
    }

    private async Task SettingsVideoAvailabilityAsync(int width, bool customConfigOnly = false)
    {
        using var fixture = CreateFixture();
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, $"video-settings-{width}"));
        paths.EnsureCreated();
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance),
            new UpdateClient(new RejectNetwork()), fixture.Capabilities);
        _root.Children.Add(page);
        try
        {
            ResizeInspect(width, 900);
            if (customConfigOnly) await SettingsCustomConfigAsync(page, fixture, width);
            else await SettingsVideoDependenciesAsync(page, fixture, width);
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            ResizeInspect(1420, 980);
        }
    }

    private async Task SettingsVideoDependenciesAsync(SettingsPage page, Fixture fixture, int width)
    {
        var video = fixture.Settings.Video;
        var mpv = fixture.Settings.Mpv;
        var original = (video.VideoSync, video.Interpolation, video.Tscale, video.HighFrameRateAudioSync,
            video.HighRefreshRateLimitHz, video.Dither, video.DitherDepth, video.Deband, video.DebandStrength,
            video.QualityPreset, video.GpuApi, video.Renderer, mpv.Backend, mpv.Pipeline,
            mpv.ExecutablePath, mpv.EnableIpc);
        const string fallback = "高帧率或高刷新率时使用音频同步";
        const string threshold = "音频同步回退阈值（Hz）";
        try
        {
            video.VideoSync = "display-resample";
            video.Interpolation = true;
            video.Tscale = "bicubic";
            video.HighFrameRateAudioSync = true;
            video.HighRefreshRateLimitHz = 144;
            video.Dither = "fruit";
            video.DitherDepth = "10";
            video.Deband = "yes";
            video.DebandStrength = "high";
            video.QualityPreset = "default";
            video.GpuApi = "opengl";
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "视频输出";
            await SettleAsync();
            AssertGraphicsApi(page, fixture);
            AssertInterpolation("bicubic");
            AssertRow(threshold, true);
            AssertRow("抖动目标位深", true);
            AssertRow("去色带强度", true);

            foreach (var kernel in new[] { "oversample", "mitchell", "bicubic" })
            {
                await PickAsync("插值", MpvOutputOptions.InterpolationKernels, kernel);
                AssertInterpolation(kernel);
                Require(video.Interpolation && video.Tscale == kernel, "插值算法选择没有保存");
            }
            // 选择后重建基础卡，截图和断言必须重新取得当前控件。
            var interpolationCombo = Descendants(page).OfType<ComboBox>()
                .Single(control => AutomationProperties.GetName(control) == "插值");
            var topMost = _window!.TopMost;
            try
            {
                // Raise the host before opening the popup; raising it afterwards can cover the dropdown.
                _window.TopMost = true;
                interpolationCombo.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 });
                await SettleAsync();
                interpolationCombo.IsDropDownOpen = true;
                await SettleAsync();
                Require(interpolationCombo.IsDropDownOpen, "插值下拉没有展开");
                await CompositionPlaybackProbe.SaveScreenAsync(_window,
                    Path.Combine(_options.Paths.LogDirectory, $"settings-video-interpolation-{width}-screen.png"));
            }
            finally
            {
                interpolationCombo.IsDropDownOpen = false;
                _window.TopMost = topMost;
            }

            await PickAsync("视频同步", MpvOutputOptions.VideoSync, "audio");
            AssertInterpolation("no", readOnly: true);
            AssertRow(fallback, false);
            AssertRow(threshold, false);
            Require(video.Interpolation && video.Tscale == "bicubic" && video.HighRefreshRateLimitHz == 144,
                "音频同步清空了插值或回退偏好");
            await CaptureAsync("audio");

            await PickAsync("视频同步", MpvOutputOptions.VideoSync, "display-resample");
            AssertInterpolation("bicubic");
            AssertRow(threshold, true);
            await PickLabelAsync("插值", "关闭");
            AssertInterpolation("no");
            Require(!video.Interpolation && video.Tscale == "bicubic", "关闭插值没有停用开关或清空了算法偏好");
            Require(Rows().Single(row => row.Label == "视频同步").Note.Contains("插值关闭"), "关闭插值后同步说明没有更新");
            AssertRow(fallback, true); // 显示同步本身也需要回退开关。
            await ToggleAsync(fallback);
            AssertRow(threshold, false);
            await ToggleAsync(fallback);
            AssertRow(threshold, true);
            await PickAsync("视频同步", MpvOutputOptions.VideoSync, "");
            AssertInterpolation("no");
            AssertRow(fallback, false); // 自动 + 无插值实际使用音频同步。
            await CaptureAsync("interpolation-off");
            await PickAsync("视频同步", MpvOutputOptions.VideoSync, "audio");
            AssertInterpolation("no", readOnly: true);
            await PickAsync("视频同步", MpvOutputOptions.VideoSync, "");
            AssertInterpolation("no");
            Require(!video.Interpolation && video.Tscale == "bicubic", "切换同步模式意外开启插值或清空算法偏好");
            await PickAsync("插值", MpvOutputOptions.InterpolationKernels, "bicubic");
            AssertInterpolation("bicubic");
            AssertRow(fallback, true);
            AssertRow(threshold, true);
            Require(video.Tscale == "bicubic" && video.HighRefreshRateLimitHz == 144, "恢复后插值参数丢失");

            var number = Descendants(page).OfType<NumberBox>()
                .Single(control => AutomationProperties.GetName(control) == threshold);
            number.Value = 165;
            Require(video.HighRefreshRateLimitHz == 165 && Rows().Single(row => row.Label == "视频同步").Note.Contains("165 Hz"),
                "回退阈值没有保存或同步说明没有更新");

            await PickAsync("抖动", MpvOutputOptions.Dithers, "no");
            await PickAsync("去色带", MpvOutputOptions.DebandModes, "no");
            AssertRow("抖动目标位深", false);
            AssertRow("去色带强度", false);
            await CaptureAsync("effects-hidden");
            await PickAsync("去色带", MpvOutputOptions.DebandModes, "");
            AssertRow("去色带强度", false);
            await PickAsync("抖动", MpvOutputOptions.Dithers, "fruit");
            await PickAsync("去色带", MpvOutputOptions.DebandModes, "auto");
            AssertRow("抖动目标位深", true);
            AssertRow("去色带强度", true);
            Require(video.DitherDepth == "10" && video.DebandStrength == "high", "恢复后抖动或去色带偏好丢失");
            await CaptureAsync("restored");

            await PickAsync("抖动", MpvOutputOptions.Dithers, "");
            await PickAsync("画质预设", MpvOutputOptions.QualityPresets, "fast");
            AssertRow("抖动目标位深", false);
            await PickAsync("画质预设", MpvOutputOptions.QualityPresets, "default");
            AssertRow("抖动目标位深", true);

            page.SelectedCategory = "播放器";
            await SettleAsync();
            await PickLabelAsync("播放后端", "外部 mpv.exe（独立窗口）");
            // 外置两行并回播放器卡后随值进出：外部后端当场在场，路径与 IPC 偏好不因重建而动。
            AssertExternalRows(true);
            await SaveReturnFrameAsync(page, $"settings-player-external-{width}.png");
            page.SelectedCategory = "视频输出";
            await SettleAsync();
            AssertRow("渲染管线", false);
            AssertRow("视频渲染", true);
            AssertGraphicsApi(page, fixture);
            await PickAsync("视频渲染", MpvOutputOptions.Renderers, "gpu");
            foreach (var label in new[] { "视频同步", "硬件解码", "反交错", "色彩范围", "插值", "抖动", "去色带" })
                AssertRow(label, true);
            await CaptureAsync("external");
            page.SelectedCategory = "播放器";
            await SettleAsync();
            await PickLabelAsync("播放后端", "内置播放器（libmpv）");
            AssertExternalRows(false);
            page.SelectedCategory = "视频输出";
            await SettleAsync();
            AssertRow("渲染管线", true);
            AssertRow("视频渲染", false);
            Require(video.GpuApi == "opengl" && mpv.Pipeline == original.Pipeline, "切后端丢失了接口或管线偏好");
            AssertGraphicsApi(page, fixture);

            // 导入/恢复默认共用 ReloadAsync；重新装载后仍按保存的条件构造行。
            await PickAsync("视频同步", MpvOutputOptions.VideoSync, "audio");
            await page.ViewModel.ReloadAsync();
            await SettleAsync();
            AssertInterpolation("no", readOnly: true);
            AssertRow(fallback, false);
            await PickAsync("视频同步", MpvOutputOptions.VideoSync, "display-vdrop");
            AssertInterpolation("bicubic");
            Require(video.Interpolation && video.Tscale == "bicubic", "重新加载后插值偏好丢失");
        }
        finally
        {
            (video.VideoSync, video.Interpolation, video.Tscale, video.HighFrameRateAudioSync,
                video.HighRefreshRateLimitHz, video.Dither, video.DitherDepth, video.Deband, video.DebandStrength,
                video.QualityPreset, video.GpuApi, video.Renderer, mpv.Backend, mpv.Pipeline,
                mpv.ExecutablePath, mpv.EnableIpc) = original;
            await page.ViewModel.ReloadAsync();
            await SettleAsync();
        }

        IEnumerable<SettingRow> Rows() => page.ViewModel.Sections.Where(section => section.Category == "视频输出")
            .SelectMany(section => section.Rows);
        void AssertInterpolation(string value, bool readOnly = false)
        {
            AssertRow("插值", true);
            AssertRow("启用插值", false);
            AssertRow("插值算法", false);
            var row = Rows().Single(candidate => candidate.Label == "插值");
            var combo = Descendants(page).OfType<ComboBox>()
                .SingleOrDefault(control => AutomationProperties.GetName(control) == "插值");
            if (readOnly)
            {
                Require(combo is null && row is SettingFactRow { Value: "关闭", ActionVisibility: Visibility.Collapsed }
                    && Descendants(page).OfType<FrameworkElement>().Any(element => ReferenceEquals(element.DataContext, row)
                        && Descendants(element).OfType<TextBlock>().Any(text => text.Text == "关闭")),
                    "音频同步下必须保留插值只读关闭文字，不能保留下拉框");
            }
            else
            {
                var expected = new[] { "关闭" }.Concat(MpvOutputOptions.InterpolationKernels.Select(kernel => kernel.Label));
                Require(row is SettingChoiceRow choice && choice.Choices.Select(item => item.Label).SequenceEqual(expected)
                    && combo is { IsEnabled: true } && combo.Items.Count == 4
                    && ReferenceEquals(combo.SelectedItem, choice.Selected)
                    && choice.Selected?.Label == (value == "no" ? "关闭" : MpvOutputOptions.Describe(MpvOutputOptions.InterpolationKernels, value)),
                    "插值必须显示关闭及三种算法，并选中当前值");
            }
        }
        // 外置两行（mpv.exe 路径 / 启用 IPC 进度通道）并回播放器卡、随播放后端进出：数据行、真实行和
        // 可聚焦控件三头都要对上；收起时不得动路径与 IPC 偏好（隐藏不清空偏好）。
        void AssertExternalRows(bool present)
        {
            var rows = page.ViewModel.Sections.Where(section => section.Category == "播放器")
                .SelectMany(section => section.Rows).ToList();
            Require(page.ViewModel.Sections.Count(section => section.Title == "播放器") == 1
                && page.ViewModel.Sections.All(section => section.Title != "外置播放器"),
                "外置播放器分区没有并回唯一的播放器卡");
            Require(rows.Any(row => row.Label == "mpv.exe 路径") == present
                && rows.Any(row => row.Label == "启用 IPC 进度通道") == present,
                "外置两行的数据行显隐错误");
            Require(Descendants(page).OfType<TextBlock>().Any(text => text.Text == "mpv.exe 路径") == present
                && Descendants(page).OfType<TextBlock>().Any(text => text.Text == "启用 IPC 进度通道") == present,
                "外置两行的真实行显隐错误");
            if (!present)
            {
                Require(!Descendants(page).OfType<Control>().Any(control =>
                    AutomationProperties.GetName(control) is "mpv.exe 路径" or "启用 IPC 进度通道"),
                    "外置两行隐藏后仍留有可聚焦控件");
                Require(mpv.ExecutablePath == original.ExecutablePath && mpv.EnableIpc == original.EnableIpc,
                    "隐藏外置两行动了路径或 IPC 偏好");
            }
        }
        void AssertRow(string label, bool present)
        {
            Require(Rows().Any(row => row.Label == label) == present, $"{label} 的数据行显隐错误");
            Require(Descendants(page).OfType<TextBlock>().Any(text => text.Text == label) == present,
                $"{label} 的真实行显隐错误");
            if (!present)
                Require(!Descendants(page).OfType<Control>().Any(control => AutomationProperties.GetName(control) == label),
                    $"{label} 隐藏后仍留有可聚焦控件");
        }
        async Task SettleAsync()
        {
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            AssertSettingsSettled(page);
        }
        async Task PickLabelAsync(string label, string choice)
        {
            var row = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingChoiceRow>()
                .Single(candidate => candidate.Label == label);
            var combo = Descendants(page).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == label);
            combo.SelectedItem = row.Choices.Single(item => item.Label == choice);
            await SettleAsync();
        }
        Task PickAsync(string label, IReadOnlyList<MpvChoice> choices, string value) =>
            PickLabelAsync(label, MpvOutputOptions.Describe(choices, value));
        async Task ToggleAsync(string label)
        {
            var toggle = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == label);
            var peer = new ToggleSwitchAutomationPeer(toggle);
            ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
            await SettleAsync();
        }
        async Task CaptureAsync(string state)
        {
            var scroll = Descendants(page).OfType<ScrollView>().Single();
            foreach (var offset in new[] { 0d, 650d })
            {
                scroll.ScrollTo(0, offset, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                await LayoutAsync(page);
                await SaveProbeScreenAsync($"settings-video-{state}-{width}-{offset:0}-screen.png");
            }
            scroll.ScrollTo(0, 0, new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
        }
    }

    private static (SettingChoiceRow Row, ComboBox Combo) PipelineRow(SettingsPage page)
    {
        var row = page.ViewModel.Sections.First(section => section.Title == "基础输出").Rows
            .OfType<SettingChoiceRow>().Single(candidate => candidate.Label == "渲染管线");
        var combo = Descendants(page).OfType<ComboBox>()
            .Single(control => AutomationProperties.GetName(control) == "渲染管线");
        return (row, combo);
    }

    private static void AssertGraphicsApi(SettingsPage page, Fixture fixture)
    {
        var integrated = fixture.Settings.Mpv.Backend == MpvBackendKind.BuiltInLibMpv
            && fixture.Settings.Mpv.Pipeline == VideoPipelineKind.Integrated;
        var row = page.ViewModel.Sections.First(section => section.Title == "基础输出").Rows
            .Single(candidate => candidate.Label == "图形接口");
        var combo = Descendants(page).OfType<ComboBox>()
            .SingleOrDefault(control => AutomationProperties.GetName(control) == "图形接口");
        if (integrated)
            Require(combo is null && row is SettingFactRow { Value: "Direct3D 11（D3D11）", ActionVisibility: Visibility.Collapsed }
                && Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Direct3D 11（D3D11）"),
                "集成模式必须显示 D3D11 只读文字，不能保留下拉框");
        else
            Require(row is SettingChoiceRow choice && combo is { IsEnabled: true }
                && ReferenceEquals(combo.SelectedItem, choice.Selected)
                && choice.Selected?.Label == MpvOutputOptions.Describe(MpvOutputOptions.GpuApis, fixture.Settings.Video.GpuApi),
                "独占模式没有恢复保存的图形接口");
    }
}

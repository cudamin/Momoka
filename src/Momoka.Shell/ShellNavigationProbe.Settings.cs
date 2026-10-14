using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
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
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
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
    /// 设置 → 界面 → 「首页媒体库位置」（用户令 2026-10-12）：三档下拉，装机默认是「随窗口高度变换」。
    /// <para>
    /// 值得一条探针，因为这一行坏掉的样子在屏上都很安静：下拉里少一项读起来像「这功能就这样」；控件没有落在
    /// 存档里那一档上（<see cref="SettingChoiceRow"/> 的候选表认不出存档里的值时会给一个「设置文件中的值」的
    /// 兜底项），用户下次打开设置看到的就不是自己选的那一档；而选一下没有写回设置，主页那一头读到的还是旧值 ——
    /// 三样都得读存档和真实控件才认得出。界面卡另留宽窄两帧截图。
    /// </para>
    /// </summary>
    private async Task SettingsHomeLibraryPlacementAsync()
    {
        const string label = "首页媒体库位置";
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
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Capabilities);
        await page.ViewModel.ReloadAsync();
        _root.Children.Add(page);
        try
        {
            page.SelectedCategory = "界面";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            AssertSettingsSettled(page);

            var row = page.ViewModel.Sections.Where(section => section.Category == "界面")
                .SelectMany(section => section.Rows)
                .OfType<SettingChoiceRow>().Single(candidate => candidate.Label == label);
            var combo = Descendants(page).OfType<ComboBox>()
                .Single(control => AutomationProperties.GetName(control) == label);

            Require(row.Choices.Count == 3 && combo.Items.Count == 3,
                $"「{label}」不是三档：数据行 {row.Choices.Count} 项、控件 {combo.Items.Count} 项");
            Require(row.Choices.Select(choice => choice.Label)
                    .SequenceEqual(["随窗口高度变换", "固定在轮播图中", "固定在轮播图下方"]),
                "「首页媒体库位置」三档的名字或次序变了：" + string.Join("、", row.Choices.Select(choice => choice.Label)));

            // 装机默认那一档：存档里是 Auto，控件显示的也是它（不是那个「设置文件中的值」的兜底项）。
            Require(fixture.Settings.Ui.HomeLibraryPlacement == HomeLibraryPlacement.Auto, "界面卡的默认档不是「随窗口高度变换」");
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
                await SaveProbeScreenAsync($"settings-interface-placement-{width}-screen.png");
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
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Capabilities);
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
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
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

            // 产品变化时权限仍为 true；不能只在权限位变化时重建组名和分类。
            page.SelectedCategory = SettingsViewModel.NotificationsCategory;
            fixture.Capabilities.ServerKind = MediaServerKind.Jellyfin;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);
            Require(page.ViewModel.CategoryGroups.Select(group => group.Name).SequenceEqual(new[] { "Momoka", "JellyfinServer" }),
                "连接 Jellyfin 后管理组没有自动改名");
            Require(!page.Categories.Contains(SettingsViewModel.NotificationsCategory)
                && !page.ViewModel.CanOpenServerCategory(SettingsViewModel.NotificationsCategory)
                && page.SelectedCategory == SettingsViewModel.FirstCardCategory,
                "Jellyfin 仍允许打开通知或留下已失效的通知页面");
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
                    && !page.Categories.Any(SettingsViewModel.ServerAdministrationCategories.Contains)
                    && !page.ViewModel.CanOpenServerCategory(SettingsViewModel.ServerUsersCategory),
                    "Jellyfin 非管理员或权限未知时仍暴露管理入口");
            }
            await SaveProbeScreenAsync("settings-jellyfin-user-screen.png");

            fixture.Capabilities.ServerKind = MediaServerKind.Emby;
            fixture.Capabilities.IsAdministrator = true;
            await (Task)Call(page, "RefreshServerAdministrationAsync")!;
            await LayoutAsync(page);
            Require(page.Categories.TakeLast(SettingsViewModel.ServerAdministrationCategories.Count)
                    .SequenceEqual(SettingsViewModel.ServerAdministrationCategories)
                && page.ViewModel.CategoryGroups.Last().Name == "EmbyServer",
                "返回 Emby 后五个管理入口没有按原次序恢复");

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
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Capabilities);
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

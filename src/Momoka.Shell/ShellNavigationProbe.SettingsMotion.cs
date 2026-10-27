using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Updates;
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
    /// <summary>真实模板与按钮、两阶段拖拽集合更新、弹簧帧、保存和关闭动画；数据全部来自隔离夹具。</summary>
    private async Task SettingsPriorityFiltersAsync(int width)
    {
        using var fixture = CreateFixture();
        fixture.Settings.Playback.SubtitleTitleRules = [new("双语", TitlePreference.Prefer), new("SDH", TitlePreference.Prefer)];
        fixture.Settings.Playback.AudioFormatRules = [new("Atmos", TitlePreference.Prefer), new("FLAC", TitlePreference.Prefer)];
        fixture.Settings.Playback.VideoFileRules = [new("REMUX", TitlePreference.Prefer), new("HDR", TitlePreference.Prefer)];
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, $"priority-settings-{width}"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        var preference = HomeMotion.PreferenceEnabled;
        HomeMotion.ApplyPreference(true);
        _root.Children.Add(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            ResizeProbeClient(width, 900);
            await page.ViewModel.ReloadAsync();
            Require(SettingTitleRulesRow.Probe().Ok, "关键词表增删／排序／两阶段提交的离线探针失败");
            foreach (var (category, label) in new[] { ("字幕", "标题筛选"), ("播放行为", "音轨格式筛选"), ("播放行为", "视频文件名筛选") })
            {
                page.SelectedCategory = category;
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                var row = page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingTitleRulesRow>()
                    .Single(item => item.Label == label);
                var list = Descendants(page).OfType<ListView>()
                    .Single(control => ReferenceEquals(control.ItemsSource, row.Rules));
                Require(list.CanDragItems && list.CanReorderItems && list.AllowDrop && list.Tag == row,
                    $"{label} 没有接入原生拖拽或模板行引用");
                list.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.35 });
                await LayoutAsync(list);
                var first = row.Rules[0].Term;
                var second = row.Rules[1].Term;
                var down = Descendants(list).OfType<Button>().Single(button => AutomationProperties.GetName(button) == $"下移 {first}");
                ((IInvokeProvider)new ButtonAutomationPeer(down).GetPattern(PatternInterface.Invoke)).Invoke();
                if (HomeMotion.AnimationsEnabled)
                {
                    await UntilAsync(() => page.HasPrioritySpring(list));
                    await SaveProbeScreenAsync($"settings-priority-{label}-{width}-spring-screen.png");
                }
                await UntilAsync(() => !page.HasPrioritySpring(list));
                Require(row.Rules[0].Term == second && row.Rules[1].Term == first && Stored()[0].Term == second,
                    $"{label} 上下按钮没有保存排序");
                Require(row.Rules[0].Position == 1 && row.Rules[1].Position == 2,
                    $"{label} 优先级序号没有跟随集合移动");
                Require(row.Rules.All(item => item.State == TitlePreference.Prefer), $"{label} 换位改变了规则态度");
                // 与 WinUI 原生拖拽一致的 Remove + Add，通过正式动画处理器完成提交。
                var motions = Get<Dictionary<ListView, SettingsPriorityMotion>>(page, "_priorityMotions");
                var motion = motions[list];
                motion.BeginDrag();
                var dragged = row.Rules[0];
                row.Rules.RemoveAt(0);
                Require(Stored().Count == 2, $"{label} 拖拽中间缺少一项的集合被写盘");
                row.Rules.Add(dragged);
                motion.EndDrag();
                if (HomeMotion.AnimationsEnabled) await UntilAsync(() => page.HasPrioritySpring(list));
                await UntilAsync(() => !page.HasPrioritySpring(list));
                Require(Stored().Select(item => item.Term).SequenceEqual(row.Rules.Select(item => item.Term)),
                    $"{label} 拖拽完成后存档与屏上次序不一致");
                Require(KeywordFilter.Rank(new[] { second, first }, Stored(), text => text).Single() == first,
                    $"{label} 排到上方的规则没有实际优先");
                var input = Descendants(page).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == row.NewTermName);
                input.Text = "自定义优先词";
                await UntilAsync(() => row.NewTerm == "自定义优先词");
                var add = Descendants(page).OfType<Button>().Single(control => AutomationProperties.GetName(control) == row.AddName);
                ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)).Invoke();
                await LayoutAsync(list);
                Require(Stored().Count == 3 && row.NewTerm.Length == 0 && row.Rules[^1].State == TitlePreference.Neutral,
                    $"{label} 添加入口／默认态度／清空输入框失败：存档 {Stored().Count}、表 {row.Rules.Count}、"
                        + $"新词「{row.NewTerm}」、输入「{input.Text}」、末项 {row.Rules[^1].Term}:{row.Rules[^1].State}");
                var newChoice = row.Rules[^1];
                var state = Descendants(list.ContainerFromItem(newChoice)).OfType<ComboBox>().Single();
                Require((string?)state.SelectedItem == "默认", $"{label} 新增项屏上的态度不是默认");
                state.SelectedItem = "候补";
                await UntilAsync(() => newChoice.State == TitlePreference.Exclude);
                Require(Stored()[^1].State == TitlePreference.Exclude, $"{label} 态度下拉没有写回设置");
                state.SelectedItem = "默认";
                await UntilAsync(() => newChoice.State == TitlePreference.Neutral);
                input.Text = " 自定义优先词 ";
                await UntilAsync(() => row.NewTerm == " 自定义优先词 ");
                ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => row.NewTerm.Length == 0);
                Require(Stored().Count == 3, $"{label} 重复关键词被添加");
                HomeMotion.ApplyPreference(false);
                row.Rules.Move(0, 1);
                await LayoutAsync(list);
                Require(!page.HasPrioritySpring(list) && page.IsContentMotionSettled, $"{label} 关闭动画后仍在回弹");
                Require(Descendants(list).OfType<FrameworkElement>().Where(element => element.Name == "PriorityRuleRoot")
                    .All(element => element.RenderTransform is TranslateTransform { Y: 0 }), $"{label} 关闭动画留下位移");
                HomeMotion.ApplyPreference(true);
                await SaveProbeScreenAsync($"settings-priority-{label}-{width}-screen.png");
                var content = Descendants(list).OfType<Grid>().First(element => element.Name == "PriorityRuleRoot");
                Require(content.ActualWidth <= list.ActualWidth + 1 && content.ActualWidth > 0, $"{label} 行超出列表宽度");
                var after = Stored().Select(item => item.Term).ToArray();
                await page.ViewModel.ReloadAsync();
                Require(page.ViewModel.Sections.SelectMany(section => section.Rows).OfType<SettingTitleRulesRow>()
                    .Single(item => item.Label == label).Rules.Select(item => item.Term).SequenceEqual(after),
                    $"{label} 页面重入丢失顺序");

                IReadOnlyList<KeywordRule> Stored() => label switch
                {
                    "标题筛选" => fixture.Settings.Playback.SubtitleTitleRules,
                    "音轨格式筛选" => fixture.Settings.Playback.AudioFormatRules,
                    _ => fixture.Settings.Playback.VideoFileRules
                };
            }
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            HomeMotion.ApplyPreference(preference);
            ResizeInspect(1420, 980);
        }
    }

    /// <summary>真实开关、关联行与卡片补位，覆盖无损／格式／自定义配置、快速切换与偏好保留。</summary>
    private async Task SettingsConditionalRowsAsync(int width)
    {
        using var fixture = CreateFixture();
        var mpv = fixture.Settings.Mpv;
        mpv.ScreenshotFormat = "webp";
        mpv.ScreenshotWebpLossless = false;
        mpv.ScreenshotWebpQuality = 73;
        mpv.ScreenshotWebpCompression = 2;
        mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        mpv.Pipeline = VideoPipelineKind.Standalone;
        mpv.CustomConfigEnabled = false;
        mpv.CustomConfigPath = @"C:\offline\remembered-mpv.conf";
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, $"conditional-settings-{width}"));
        paths.EnsureCreated();
        var page = CreateMotionSettingsPage(fixture, moviePilot, paths);
        var preference = HomeMotion.PreferenceEnabled;
        HomeMotion.ApplyPreference(true);
        _root.Children.Add(page);
        try
        {
            BeginSettingsFixtureVisit(page);
            ResizeProbeClient(width, 900);
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "播放行为";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var section = page.ViewModel.Sections.Single(item => item.Title == "截图");
            var rows = Descendants(page).OfType<ItemsControl>().Single(control => ReferenceEquals(control.ItemsSource, section.Rows));
            await WaitForSettingsRowsLoadedAsync(page, rows); // 有尺寸/IsLoaded 不证明 Loaded 中的真实行接线已完成。
            var lossless = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "WebP 无损");
            var format = Descendants(page).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == "截图格式");
            lossless.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.35 });
            await LayoutAsync(lossless);
            Require(page.HasConditionalTransitions(rows) == HomeMotion.AnimationsEnabled, "从属行没有遵循 UI 动画设置");
            await SaveProbeScreenAsync($"settings-conditional-webp-{width}-visible-screen.png");
            Toggle(lossless);
            if (HomeMotion.AnimationsEnabled)
            {
                await UntilAsync(() => page.HasConditionalMotion(rows));
                await SaveReturnFrameAsync(page, $"settings-conditional-webp-{width}-closing.png");
            }
            await UntilAsync(() => page.IsContentMotionSettled);
            await LayoutAsync(page);
            Require(!HasRow("WebP 质量") && !HasRow("WebP 压缩"), "开启无损没有收起完整从属行");
            Require(ReferenceEquals(section, page.ViewModel.Sections.Single(item => item.Title == "截图"))
                && Descendants(page).Any(element => ReferenceEquals(element, lossless))
                && Descendants(page).Any(element => ReferenceEquals(element, format)), "显隐替换了上级控件或整张卡");
            Require(mpv.ScreenshotWebpQuality == 73 && mpv.ScreenshotWebpCompression == 2, "隐藏清空了画质偏好");
            await SaveProbeScreenAsync($"settings-conditional-webp-{width}-hidden-screen.png");
            Toggle(lossless);
            Require(!HomeMotion.AnimationsEnabled || !page.IsContentMotionSettled, "恢复行排队时过早报告动画落定");
            await UntilAsync(() => page.IsContentMotionSettled);
            await LayoutAsync(page);
            rows.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 1 });
            await LayoutAsync(rows);
            Require(HasRow("WebP 质量") && HasRow("WebP 压缩") && mpv.ScreenshotWebpQuality == 73 && mpv.ScreenshotWebpCompression == 2,
                "关闭无损没有恢复从属行及原值");
            await SaveProbeScreenAsync($"settings-conditional-webp-{width}-restored-screen.png");
            var formatRow = section.Rows.OfType<SettingChoiceRow>().Single(item => item.Label == "截图格式");
            format.SelectedItem = formatRow.Choices.Single(choice => choice.Label == MpvOutputOptions.Describe(MpvBaseline.ScreenshotFormats, "png"));
            await LayoutAsync(page);
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(!HasRow("WebP 无损") && !HasRow("WebP 质量") && !HasRow("WebP 压缩"), "切成 PNG 未收起全部 WebP 行");
            format.SelectedItem = formatRow.Choices.Single(choice => choice.Label == "WebP");
            await LayoutAsync(page);
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(HasRow("WebP 无损") && HasRow("WebP 质量"), "恢复 WebP 未恢复原偏好对应的行");
            lossless = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "WebP 无损");
            Toggle(lossless);
            Toggle(lossless);
            Toggle(lossless);
            Toggle(lossless);
            HomeMotion.ApplyPreference(false);
            await LayoutAsync(page);
            Require(page.IsContentMotionSettled && !page.HasConditionalTransitions(rows), "快速切换或关闭动画没有清理动画");
            Require(VisualTreeHelper.GetParent(rows) is Border surface && double.IsNaN(surface.Height) && surface.Clip is null, "卡片没有恢复自适应高度或裁剪残留");
            Require(HasRow("WebP 质量") && mpv.ScreenshotWebpQuality == 73, "快速切换落在错误状态或丢失原值");
            HomeMotion.ApplyPreference(true);
            page.SelectedCategory = "播放器";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            var custom = Descendants(page).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "启用自定义 mpv.conf");
            Require(!HasRow("mpv.conf 路径"), "自定义配置关闭时仍有路径行");
            Toggle(custom);
            Require(!HomeMotion.AnimationsEnabled || !page.IsContentMotionSettled, "启用配置排队时过早报告动画落定");
            await LayoutAsync(page);
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(HasRow("mpv.conf 路径") && mpv.CustomConfigPath.EndsWith("remembered-mpv.conf", StringComparison.Ordinal), "启用没有恢复路径偏好");
            await SaveProbeScreenAsync($"settings-conditional-config-{width}-visible-screen.png");
            Toggle(custom);
            await LayoutAsync(page);
            await UntilAsync(() => page.IsContentMotionSettled);
            Require(!HasRow("mpv.conf 路径"), "关闭自定义配置未隐藏路径");
            await SaveProbeScreenAsync($"settings-conditional-config-{width}-hidden-screen.png");
            Toggle(custom);
            page.SelectedCategory = "字幕";
            page.SelectedCategory = "播放器";
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            Require(HasRow("mpv.conf 路径") && page.IsContentMotionSettled, "分类快速切换导致恢复行不见或高度残留");
            await SaveProbeScreenAsync($"settings-conditional-config-{width}-restored-screen.png");
            await page.ViewModel.ReloadAsync();
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            Require(HasRow("mpv.conf 路径") && mpv.ScreenshotWebpQuality == 73, "重入没有保留开关和从属值");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            HomeMotion.ApplyPreference(preference);
            ResizeInspect(1420, 980);
        }

        bool HasRow(string label) => Descendants(page).OfType<TextBlock>().Any(text => text.Text == label);
        static void Toggle(ToggleSwitch control) =>
            ((IToggleProvider)new ToggleSwitchAutomationPeer(control).GetPattern(PatternInterface.Toggle)).Toggle();
    }
    private static SettingsPage CreateMotionSettingsPage(Fixture fixture, MoviePilotClient moviePilot, AppPaths paths)
    {
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        var page = new SettingsPage();
        page.ViewModel.Attach(fixture.SettingsService, new ShaderStaging(fixture.Settings), fonts, paths,
            fixture.Launcher, fixture.Startup, new AudioDeviceCatalogue(() => null), () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask,
            new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings),
            new MoviePilotCredentials(PassthroughSecretProtector.Instance), new UpdateClient(new RejectNetwork()),
            fixture.Capabilities, fixture.Images);
        return page;
    }

}

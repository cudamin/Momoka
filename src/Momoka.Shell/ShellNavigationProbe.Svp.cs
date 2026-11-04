using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Real settings controls in an isolated fixture; no SVP process, DLL load or media.</summary>
    private async Task SettingsSvpAsync(SettingsPage page, Fixture fixture, int width)
    {
        var settings = fixture.Settings;
        settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        settings.Mpv.Pipeline = VideoPipelineKind.Standalone;
        settings.Mpv.SvpEnabled = false;
        settings.Mpv.SvpVapourSynthPath = Path.Combine(_options.Paths.Root, "svp-not-installed", "VSScript.dll");
        settings.Video.FrameInterpolationMode = FrameInterpolationMode.MotionCompensated;
        settings.Video.FrameInterpolationFps = 72;
        settings.Video.FrameInterpolationQuality = FrameInterpolationQuality.High;
        settings.Video.Interpolation = true;
        settings.Video.Tscale = "bicubic";
        settings.Video.VideoSync = "display-resample";
        await ReloadAsync();
        var card = Card();
        var rows = card.Rows;
        AssertRows(false);
        Require(!Switch().IsOn, "SVP 新配置没有默认关闭");
        await CaptureAsync("off");
        await ToggleAsync();
        AssertRows(true);
        Require(settings.Mpv.SvpEnabled && Switch().IsOn, "SVP 开关没有写回与同步显示");
        Require(ReferenceEquals(card, Card()) && ReferenceEquals(rows, Card().Rows), "SVP 更新替换了卡片或集合");
        var state = Card().Rows.OfType<SettingFactRow>().Single(row => row.Label == "SVP 接入状态");
        Require(state.Value.Contains("缺少", StringComparison.Ordinal), "缺少组件被误报为可用");
        Require(page.ViewModel.Sections.Single(section => section.Title == "基础输出").Rows
            .Any(row => row.Label == "视频同步" && row is SettingFactRow), "SVP 没有接管基础卡同步");
        var box = Descendants(page).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == "VapourSynth 组件");
        var textPeer = new TextBoxAutomationPeer(box);
        Require(box.IsEnabled && !box.IsReadOnly && textPeer.GetName() == "VapourSynth 组件",
            "SVP 组件路径没有可访问编辑入口");
        var unavailable = Path.Combine(_options.Paths.Root, "svp-unavailable", "VSScript.dll");
        var pathRow = Card().Rows.OfType<SettingTextRow>().Single(row => row.Label == "VapourSynth 组件");
        void Trace(string stage) => File.AppendAllText(Path.Combine(_options.Paths.LogDirectory, "svp-focus-trace.txt"),
            $"{DateTimeOffset.Now:O} width={width} stage={stage} boxFocused={ReferenceEquals(FocusManager.GetFocusedElement(box.XamlRoot), box)} box={box.Text} row={pathRow.Text} path={settings.Mpv.SvpVapourSynthPath} unsaved={fixture.SettingsService.HasUnsavedChanges}\n");
        // Match the product's TextBox contract: x:Bind commits Text on lost focus.
        // TextBoxAutomationPeer.GetPattern(Value) is not exposed as IValueProvider
        // by this WinUI projection, so drive the real edit/focus path instead.
        await CommitSvpPathAsync(box, Switch(), "\"" + unavailable + "\"", async () =>
        {
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
            Trace("settled");
            Require(pathRow.Text == unavailable, "SVP 路径编辑没有提交到当前行模型");
            Require(settings.Mpv.SvpVapourSynthPath == unavailable && !fixture.SettingsService.HasUnsavedChanges,
                "SVP 路径没有保存或刷新状态");
        }, Trace);
        state.RunCommand.Execute(null);
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        Require(Card().Rows.OfType<SettingFactRow>().Single(row => row.Label == "SVP 接入状态").Value.Contains("缺少", StringComparison.Ordinal),
            "重新检测缺失组件没有保留错误状态");
        await CaptureAsync("on-missing");

        await ToggleAsync();
        AssertRows(false);
        Require(settings.Video.FrameInterpolationMode == FrameInterpolationMode.MotionCompensated
            && settings.Video.FrameInterpolationFps == 72 && settings.Video.FrameInterpolationQuality == FrameInterpolationQuality.High,
            "关闭 SVP 丢失 CPU 补帧偏好");
        Require(settings.Video.Interpolation && settings.Video.Tscale == "bicubic" && settings.Video.VideoSync == "display-resample",
            "关闭 SVP 改写基础输出偏好");
        Require(settings.Mpv.SvpVapourSynthPath == unavailable, "关闭 SVP 清空组件路径");
        await CaptureAsync("restored");

        await ToggleAsync();
        // Toggle the actual custom-config row, so this checks immediate cross-card wiring.
        var custom = page.ViewModel.Sections.Single(section => section.Title == "自定义 mpv.conf")
            .Rows.OfType<SettingToggleRow>().Single(row => row.Label == "启用自定义 mpv.conf");
        custom.IsOn = true;
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        Require(Card().Rows.Any(row => row.Label == "接入 SVP") && !Card().Rows.Any(row => row.Label == "VapourSynth 组件"),
            "自定义配置启用时没有隐藏 SVP 从属项／保留恢复入口");
        Require(Card().Rows.OfType<SettingFactRow>().Single(row => row.Label == "SVP 接入状态").Value.Contains("自定义", StringComparison.Ordinal),
            "自定义配置冲突未解释有效状态");
        await CaptureAsync("custom");
        settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
        await ReloadAsync();
        Require(!Card().Rows.Any(row => row.Label.Contains("SVP", StringComparison.Ordinal) || row.Label == "VapourSynth 组件"),
            "外部后端仍显示无法使用的 SVP 入口");
        settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        await ReloadAsync();
        AssertRows(true);
        Require(settings.Mpv.SvpEnabled && settings.Mpv.SvpVapourSynthPath == unavailable, "恢复内置管线丢失 SVP 偏好");
        await CaptureAsync("integrated");

        SettingSection Card() => page.ViewModel.Sections.Single(section => section.Title == "补帧");
        ToggleSwitch Switch() => Descendants(page).OfType<ToggleSwitch>()
            .Single(control => AutomationProperties.GetName(control) == "接入 SVP");
        void AssertRows(bool svp)
        {
            foreach (var label in new[] { "模式", "目标帧率", "计算质量" })
                Require(Card().Rows.Any(row => row.Label == label) == !svp, label + " 显隐错误");
            foreach (var label in new[] { "VapourSynth 组件", "SVP 接入状态", "SVP 设置" })
                Require(Card().Rows.Any(row => row.Label == label) == svp, label + " 显隐错误");
            Require(Descendants(page).OfType<ComboBox>().Any(control => AutomationProperties.GetName(control) == "模式") == !svp,
                "SVP 切换后模式真实控件显隐错误");
        }
        async Task ReloadAsync()
        {
            await page.ViewModel.ReloadAsync();
            page.SelectedCategory = "视频输出";
            var videoCards = page.ViewModel.Sections.Where(section => section.Category == "视频输出").Select(section => section.Title);
            Require(string.Join("|", videoCards) == "基础输出|画质与着色器|HDR 与杜比视界|补帧", "视频输出卡片没有遵循用户指定顺序");
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
        }
        async Task ToggleAsync()
        {
            var toggle = Switch();
            var peer = new ToggleSwitchAutomationPeer(toggle);
            Require(peer.GetName() == "接入 SVP" && peer.GetPattern(PatternInterface.Toggle) is IToggleProvider,
                "SVP 开关缺少稳定的可访问名称／切换模式");
            ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
            await LayoutAsync(page);
            await WaitForSettingsSettledAsync(page);
        }
        async Task CaptureAsync(string stateName)
        {
            var scroll = Descendants(page).OfType<ScrollViewer>().Single(view => view.Name == "SettingsScroll");
            var section = Descendants(page).OfType<FrameworkElement>()
                .First(element => element.Name == "SettingsSection" && ReferenceEquals(element.DataContext, Card()));
            var point = section.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());
            await ScrollProbeAsync(scroll, Math.Max(0, scroll.VerticalOffset + point.Y - 20));
            await LayoutAsync(page);
            await SaveReturnFrameAsync(page, $"settings-svp-{stateName}-{width}.png");
            await SaveProbeScreenAsync($"settings-svp-{stateName}-{width}-screen.png");
        }
    }
    // The real edit/commit path is kept in one small boundary for focus-order regressions.
    private static async Task CommitSvpPathAsync(TextBox editor, ToggleSwitch next, string typed,
        Func<Task> verifyCommit, Action<string> record)
    {
        Require(editor.Focus(FocusState.Programmatic), "SVP 路径输入框无法取得焦点");
        record("focus-request");
        await UntilAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(editor.XamlRoot), editor));
        record("focus-acquired");
        var lost = false;
        void OnLostFocus(object sender, RoutedEventArgs args) { lost = true; record("lost-focus"); }
        editor.LostFocus += OnLostFocus;
        try
        {
            editor.Text = typed;
            record("typed");
            Require(next.Focus(FocusState.Programmatic), "SVP 路径编辑后无法转移焦点");
            record("transfer-request");
            await UntilAsync(() => lost && ReferenceEquals(FocusManager.GetFocusedElement(next.XamlRoot), next));
            record("focus-transferred");
            await verifyCommit();
            record("commit-verified");
        }
        finally { editor.LostFocus -= OnLostFocus; }
    }
}

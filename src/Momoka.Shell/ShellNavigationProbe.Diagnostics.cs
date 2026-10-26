using Momoka.Diagnostics;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.Platform;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实控件与假身份：布局、日志筛选、实时跟随及详情。禁止创建播放后端或访问真实账号。</summary>
    private async Task DiagnosticsLayoutAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "diagnostics-layout"));
        paths.EnsureCreated();
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("运行状态探针禁止创建播放后端"),
            new PlaybackPlanner(fixture.Settings, shaders, paths.ShaderCacheDirectory, paths.ScreenshotDirectory),
            allowPlayback: false);
        var sink = new RingBufferLogSink(80);
        for (var index = 0; index < 24; index++)
            sink.Write(new LogEntry(DateTimeOffset.Now.AddSeconds(-index),
                index % 3 == 0 ? LogLevel.Warn : LogLevel.Info, index % 2 == 0 ? "网络" : "更新",
                $"离线日志 {index + 1}：验证日志区能同时显示多行", index == 0 ? "假传输返回的诊断信息，无真实网络请求。" : null));
        var clipboard = new DiagnosticsProbeClipboard();
        var originalAnimations = HomeMotion.PreferenceEnabled;
        HomeMotion.ApplyPreference(false);
        var page = new DiagnosticsPage();
        _root.Children.Add(page);
        try
        {
            page.ViewModel.Attach(fixture.Actions, fixture.SettingsService, fixture.Session, fixture.Capabilities,
                playback, paths, fixture.Dispatcher, clipboard, fixture.Launcher, sink);
            await page.ViewModel.ReloadAsync();
            foreach (var width in new[] { 1100, 760, 560 })
            {
                ResizeProbeClient(width, 680);
                _window!.SynchronizeContentLayout();
                await LayoutAsync(page);
                Require(page.Realised > 0, "运行状态没有实现出日志模板");
                var status = Get<Grid>(page, "StatusCards");
                var log = Get<Border>(page, "LogPanel");
                var statusBounds = Bounds(status, page);
                var logBounds = Bounds(log, page);
                Require(logBounds.Top >= statusBounds.Bottom + 10 && logBounds.Bottom <= page.ActualHeight + 1,
                    "状态摘要和日志区重叠或超出窗口");
                Require(statusBounds.Height <= 180 && log.ActualHeight >= 300,
                    $"状态区占用过多空间，日志视口不足（状态 {statusBounds.Height:F0}，日志 {log.ActualHeight:F0}）");
                Require(!Descendants(status).OfType<ScrollView>().Any(), "状态摘要仍在独立滚动区里");
                var filter = Get<Grid>(page, "DiagnosticsFilterBar");
                var category = ById<ComboBox>(page, "DiagnosticsCategoryPicker");
                var level = ById<ComboBox>(page, "DiagnosticsLevelPicker");
                var clear = ById<Button>(page, "DiagnosticsClearFilterButton");
                var follow = Get<ToggleSwitch>(page, "DiagnosticsFollowSwitch");
                var filterItems = new FrameworkElement[] { category, level, clear, follow };
                for (var index = 0; index < filterItems.Length; index++)
                {
                    var bounds = Bounds(filterItems[index], filter);
                    Require(bounds.Left >= -1 && bounds.Right <= filter.ActualWidth + 1,
                        $"筛选工具超出内容宽度：{AutomationProperties.GetAutomationId(filterItems[index])}");
                    for (var other = index + 1; other < filterItems.Length; other++)
                    {
                        var otherBounds = Bounds(filterItems[other], filter);
                        Require(bounds.Right <= otherBounds.Left + 1 || otherBounds.Right <= bounds.Left + 1
                            || bounds.Bottom <= otherBounds.Top + 1 || otherBounds.Bottom <= bounds.Top + 1,
                            "日志筛选工具互相重叠");
                    }
                }
                if (width >= 760) Require(Grid.GetRow(follow) == 0, "宽度足够时实时跟随仍独占一行");
                else Require(Grid.GetRow(follow) == 1, "窄窗没有让实时跟随换到第二行");
                await SaveRenderAsync(page, $"diagnostics-compact-{width}-render.png");
                await WaitForDiagnosticsFrameAsync();
                await SaveProbeScreenAsync($"diagnostics-compact-{width}-screen.png");
            }

            ResizeProbeClient(760, 680);
            _window!.SynchronizeContentLayout();
            await LayoutAsync(page);
            var categoryPicker = ById<ComboBox>(page, "DiagnosticsCategoryPicker");
            var levelPicker = ById<ComboBox>(page, "DiagnosticsLevelPicker");
            categoryPicker.SelectedItem = "网络";
            levelPicker.SelectedIndex = 2;
            Require(page.ViewModel.SelectedCategory == "网络" && page.ViewModel.SelectedLevel == 2
                && page.ViewModel.Rows.All(row => row.Entry.Category == "网络" && row.Entry.Level >= LogLevel.Warn),
                "筛选控件没有回写或筛选结果不正确");
            var clearButton = ById<Button>(page, "DiagnosticsClearFilterButton");
            Invoke(clearButton);
            Require(page.ViewModel.SelectedCategory == "全部" && page.ViewModel.SelectedLevel == 0
                && page.ViewModel.Rows.Count == 24, "清除筛选按钮没有恢复全部日志");
            var followSwitch = Get<ToggleSwitch>(page, "DiagnosticsFollowSwitch");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(followSwitch).GetPattern(PatternInterface.Toggle)).Toggle();
            Require(!page.ViewModel.Following, "实时跟随开关没有回写暂停状态");
            sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "网络", "暂停时到达的日志", null));
            Require(page.ViewModel.Rows.Count == 24, "暂停时日志仍然更新");
            page.ViewModel.RefreshCommand.Execute(null);
            Require(page.ViewModel.Rows.Count == 25, "手动刷新没有补齐暂停时的日志");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(followSwitch).GetPattern(PatternInterface.Toggle)).Toggle();
            sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "网络", "恢复实时跟随", null));
            Require(page.ViewModel.Following && page.ViewModel.Rows.Count == 26, "实时跟随恢复后没有追加日志");
            page.ViewModel.CopyCommand.Execute(null);
            Require(clipboard.Text?.Contains("恢复实时跟随", StringComparison.Ordinal) == true,
                "复制日志没有通过假剪贴板保留当前内容");

            var details = Get<Button>(page, "DiagnosticsDetailsButton");
            var flyout = (Flyout)details.Flyout;
            Invoke(details);
            await UntilAsync(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).Count > 0);
            var content = (ScrollView)flyout.Content;
            await LayoutAsync(content);
            Require(Descendants(content).OfType<TextBlock>().Any(text => text.Text == page.ViewModel.Server),
                "详情没有保留完整服务器地址");
            var launch = ById<TextBox>(content, "DiagnosticsLaunchOptionsBox");
            Require(((StackPanel)launch.Parent).Visibility == Visibility.Collapsed,
                "尚未播放时启动参数组仍然占位");
            page.ViewModel.LaunchHeading = "离线启动参数展示";
            page.ViewModel.LaunchOptions = "vo = gpu-next\nao = wasapi";
            await LayoutAsync(content);
            Require(((StackPanel)launch.Parent).Visibility == Visibility.Visible
                && SameLines(launch.Text, page.ViewModel.LaunchOptions),
                "运行详情的启动参数没有更新");
            await WaitForDiagnosticsFrameAsync();
            await SaveProbeScreenAsync("diagnostics-compact-details-screen.png");
            await SaveRenderAsync(content, "diagnostics-compact-details-render.png");
            flyout.Hide();
            Require(fixture.Actions.Plays == 0 && !playback.IsPlaying, "运行状态探针触发了播放");
        }
        finally
        {
            ((Flyout)Get<Button>(page, "DiagnosticsDetailsButton").Flyout).Hide();
            page.Release();
            _root.Children.Remove(page);
            HomeMotion.ApplyPreference(originalAnimations);
            ResizeInspect(1420, 980);
        }

        // WinUI TextBox normalizes newlines to CR. Compare the displayed lines, not platform line endings.
        static bool SameLines(string actual, string? expected) =>
            actual.ReplaceLineEndings("\n") == expected?.ReplaceLineEndings("\n");

        static T ById<T>(DependencyObject root, string id) where T : FrameworkElement =>
            Descendants(root).OfType<T>().Single(element => AutomationProperties.GetAutomationId(element) == id);

        static void Invoke(Button button) =>
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static async Task WaitForDiagnosticsFrameAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = 0;
        void OnRendering(object? sender, object args)
        {
            if (++frames >= 2) ready.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        finally { CompositionTarget.Rendering -= OnRendering; }
    }

    private sealed class DiagnosticsProbeClipboard : IClipboard
    {
        public string? Text { get; private set; }
        public void SetText(string text) => Text = text;
    }
}

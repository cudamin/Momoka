using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Interop;
using Momoka.Shell.Platform;
using Momoka.Shell.Views;
using Momoka.Updates;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private readonly List<SettingsWindow> _openingProbeWindows = [];

    private Task SettingsOpeningAsync() => SettingsOpeningAsync(inspect: false);

    private async Task SettingsOpeningAsync(bool inspect)
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync("a.invalid", "a");
        await fixture.SelectAsync("b.invalid", "b");
        fixture.Capabilities.InterfaceFlavor = ServerInterfaceFlavor.Generic;
        fixture.Capabilities.Sources = fixture.Session.Sources.Select(scope =>
            new ServerSettingsSource(scope, MediaServerKind.Emby, true)).ToArray();
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "settings-opening"));
        paths.EnsureCreated();
        var fonts = new FontLibrary();
        Field(fonts, "_scan", Task.FromResult(FontCatalogue.Empty));
        using var moviePilot = new MoviePilotClient(new RejectNetwork());
        using var moviePilotService = new MoviePilotService(moviePilot, new MoviePilotCredentials(PassthroughSecretProtector.Instance), fixture.Settings);
        using var updates = new UpdateClient(new RejectNetwork());
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("设置开窗检查禁止播放"),
            new PlaybackPlanner(fixture.Settings, new ShaderGroupResolver(fixture.Settings.Shaders),
                paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        using var services = new ServiceCollection()
            .AddSingleton(fixture.SettingsService).AddSingleton(paths)
            .AddSingleton(fixture.Session).AddSingleton(fixture.Images)
            .AddSingleton(fixture.Services.GetRequiredService<Media.DetailHeroCache>())
            .AddSingleton<IServerCapabilities>(fixture.Capabilities)
            .AddSingleton<ISecretProtector>(PassthroughSecretProtector.Instance)
            .AddSingleton<ISystemLauncher>(fixture.Launcher).AddSingleton<IStartupRegistration>(fixture.Startup)
            .AddSingleton(new ShaderStaging(fixture.Settings)).AddSingleton(fonts)
            .AddSingleton(new AudioDeviceCatalogue(() => null)).AddSingleton(playback)
            .AddSingleton(moviePilotService)
            .AddSingleton(new MoviePilotCredentials(PassthroughSecretProtector.Instance))
            .AddSingleton(updates).BuildServiceProvider();
        var timing = new List<string>();
        var theme = ThemeHost.Current.Id;
        var watch = Stopwatch.StartNew();
        var window = SettingsWindow.TryCreate(_window) ?? throw new InvalidOperationException("正式设置窗口创建失败");
        _openingProbeWindows.Add(window);
        timing.Add($"创建 {watch.ElapsedMilliseconds} ms；显示={Native.IsWindowVisible(window.Handle)}");
        window.Show(new SettingsRequest(services, "界面"));
        timing.Add($"首次导航和显示 {watch.ElapsedMilliseconds} ms");
        var page = window.Page ?? throw new InvalidOperationException("正式设置页未导航");
        Require(Get<Action?>(page.ViewModel, "_clearHeroCache") is not null, "初次设置页未绑定详情背景清理");
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        await WaitForDiagnosticsFrameAsync();
        timing.Add($"首次正文稳定 {watch.ElapsedMilliseconds} ms；行={page.Realised}");

        // 2026-11-14 用户报「点完设置会自动跑到主窗口后面，再点一次设置也不会跑到前面」。这一步走用户
        // 真正走的那条路：先把主窗口提到前面（盖住设置窗口），再按一次设置。Window.Activate 对已经
        // 看得见的窗口只 SetActiveWindow、不改 z 序，所以 Show 必须自己 MoveInZOrderAtTop。
        // 放在 presenter.IsAlwaysOnTop 之前：那个开关是探针为了拍图自己加的置顶，会把这里要验的东西
        // 整个盖过去。
        _window!.Activate();
        window.Show(new SettingsRequest(services, "界面"));
        await LayoutAsync(page);
        await WaitForDiagnosticsFrameAsync();
        Require(SettingsWindowIsInFront(window), "主窗口在前时再按设置，设置窗口没有回到主窗口前面");
        timing.Add($"重按设置回到最前 {watch.ElapsedMilliseconds} ms");

        // 2026-11-15 用户再报「点完设置自动跑到主窗口后面」：Show 跑在主窗口那颗设置按钮的 Click 里，
        // 事件返回后主窗口的 XAML 岛把前台与键盘焦点收回去，刚提上来的设置窗口又被压到后面。这里按真实
        // 顺序摆出「Show 之后同一拍主窗口抢回前台」，Show 排在下一拍的那一次「激活 + 提到最前」必须把
        // 它重新拿回来。放到 HWND_BOTTOM 而不是把主窗口提到 HWND_TOP：后者的成败取决于当下谁在前台，
        // 换一台机器就可能没摆成；「设置窗口在最底下」是确定的，这一条回归才有前提。
        window.Show(new SettingsRequest(services, "界面"));
        _window.Activate();
        Native.SetWindowPos(window.Handle, Native.HwndBottom, 0, 0, 0, 0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);
        Require(!SettingsWindowIsInFront(window), "设置窗口没能摆到主窗口后面，这条回归没有可量的前提");
        await LayoutAsync(page);
        await WaitForDiagnosticsFrameAsync();
        Require(SettingsWindowIsInFront(window), "主窗口在设置窗显现后抢回前台，下一拍没有把设置窗口重新提到前面");
        timing.Add($"主窗口抢回前台后设置窗回到最前 {watch.ElapsedMilliseconds} ms");

        var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(window.Handle));
        var presenter = (OverlappedPresenter)appWindow.Presenter;
        var wasTopMost = presenter.IsAlwaysOnTop;
        try
        {
            presenter.IsAlwaysOnTop = true;
            var groups = page.ViewModel.CategoryGroups;
            var sections = page.ViewModel.Sections;
            for (var visit = 0; visit < 4; visit++)
            {
                await ResizeSettingsProbeWindowAsync(Get<Window>(window, "_window"), visit % 2 == 0 ? 1100 : 760, 760);
                await WaitForDiagnosticsFrameAsync();
                await CompositionPlaybackProbe.SaveScreenAsync(window.Handle,
                    Path.Combine(_options.Paths.LogDirectory, $"settings-opening-{visit}-screen.png"));
                if (visit % 2 == 0) Native.SendMessage(window.Handle, Native.WmClose, IntPtr.Zero, IntPtr.Zero);
                else window.Hide();
                Require(!window.IsOpen && !Native.IsWindowVisible(window.Handle), "隐藏未完成");
                watch.Restart();
                window.Show(new SettingsRequest(services));
                await LayoutAsync(page);
                await WaitForSettingsSettledAsync(page);
                await WaitForDiagnosticsFrameAsync();
                timing.Add($"重开 {visit + 1}：{watch.ElapsedMilliseconds} ms；行={page.Realised}");
                await CompositionPlaybackProbe.SaveScreenAsync(window.Handle,
                    Path.Combine(_options.Paths.LogDirectory, $"settings-reopened-{visit}-screen.png"));
                Require(ReferenceEquals(page, window.Page) && ReferenceEquals(sections, page.ViewModel.Sections),
                    "重开丢失缓存页面或草稿集合");
                Require(ReferenceEquals(groups, page.ViewModel.CategoryGroups), "插件不变仍重新构建聚合目录");
            }
            foreach (var current in Momoka.Theming.UiThemes.All)
            {
                ThemeHost.Apply(current.Id);
                await UntilAsync(() => ThemeHost.Current.Id == current.Id
                    && Get<Microsoft.UI.Xaml.Media.SolidColorBrush>(window, "_canvas").Color
                        == SettingsWindow.SelectCanvasColor(
                            (SettingsWindow.SettingsSystemAppearance)Call(window, "ReadSystemAppearance")!,
                            ThemeHost.ToColor(current.Colors.Window), window.BackdropState.Mica));
                await WaitForDiagnosticsFrameAsync();
                await CompositionPlaybackProbe.SaveScreenAsync(window.Handle,
                    Path.Combine(_options.Paths.LogDirectory, $"settings-opening-{current.Id}-screen.png"));
            }

            if (inspect)
            {
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var controls = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 12, Margin = new Thickness(24, 60, 24, 24) };
                var reopen = new Microsoft.UI.Xaml.Controls.Button { Content = "重新打开聚合设置" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reopen, "SettingsOpeningReopen");
                reopen.Click += (_, _) => window.Show(new SettingsRequest(services));
                var done = new Microsoft.UI.Xaml.Controls.Button { Content = "完成开窗检查" };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(done, "SettingsOpeningDone");
                done.Click += (_, _) => finished.TrySetResult();
                controls.Children.Add(reopen);
                controls.Children.Add(done);
                _root.Children.Add(controls);
                try { await finished.Task.WaitAsync(TimeSpan.FromMinutes(3)); }
                finally { _root.Children.Remove(controls); }
            }
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "settings-opening-times.txt"), timing);
        }
        finally
        {
            window.Hide();
            presenter.IsAlwaysOnTop = wasTopMost;
            Get<Window>(window, "_window").Content = null;
            await playback.ShutdownAsync();
            ThemeHost.Apply(theme);
        }
    }

    /// <summary>
    /// 设置窗口此刻是不是压在主窗口前面：取窗口正中间那一点，看命中的是不是它自己（含它的子窗口）。
    /// 这是屏幕上量出来的层级，不是 API 有没有被调用 —— Activate 照旧调了，窗口却仍在主窗口底下，
    /// 正是 2026-11-14 用户报的那个现象。
    /// </summary>
    private static bool SettingsWindowIsInFront(SettingsWindow window)
    {
        if (!Native.GetWindowRect(window.Handle, out var frame)) return false;

        var hit = Native.WindowFromPoint(new NativePoint
        {
            X = (frame.Left + frame.Right) / 2,
            Y = (frame.Top + frame.Bottom) / 2
        });
        return hit != IntPtr.Zero && Native.GetAncestor(hit, Native.GaRoot) == window.Handle;
    }
}

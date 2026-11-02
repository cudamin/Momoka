using System.Diagnostics;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

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
            .AddSingleton<IServerCapabilities>(fixture.Capabilities)
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
        await LayoutAsync(page);
        await WaitForSettingsSettledAsync(page);
        await WaitForDiagnosticsFrameAsync();
        timing.Add($"首次正文稳定 {watch.ElapsedMilliseconds} ms；行={page.Realised}");
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
}

using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.UI.ViewManagement;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MaterialLifecycleAsync()
    {
        var savedTheme = ThemeHost.Current;
        var savedBackdrop = _window!.UseBackdrop;
        var savedBackground = _root.Background;
        var savedTopMost = _window.TopMost;
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var frame = new Frame();
        var caption = new TextBlock { Text = "材质生命周期（匿名数据）", Margin = new Thickness(24) };
        var holder = (Grid)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource EgCanvasBrush}' />");
        holder.Children.Add(frame);
        holder.Children.Add(caption);
        _root.Children.Add(holder);
        var receipts = new List<object>();
        try
        {
            _window.TopMost = true;
            _window.Activate();
            ResizeProbeClient(1100, 800);
            ThemeHost.Register(holder);
            await LayoutAsync(holder);
            _window.UseBackdrop = true;
            Require(!MicaController.IsSupported() || _window.BackdropConnected,
                "主岛支持 Mica 却没有成功连接：" + _window.BackdropState);
            for (var cycle = 0; cycle < 4; cycle++)
            {
                _window.VideoVisible = true;
                Require(_window.VideoVisible && !_window.UseBackdrop && !_window.BackdropConnected,
                    "请求摘除浏览材质后仍有目标：" + _window.BackdropState);
                _window.VideoVisible = false;
                Require(!_window.VideoVisible && _window.UseBackdrop
                    && (!MicaController.IsSupported() || _window.BackdropConnected),
                    "返回浏览没有重接目标：" + _window.BackdropState);
            }
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                await LayoutAsync(holder);
                await WaitForDiagnosticsFrameAsync();
                Require(ThemeHost.Current.Id == theme.Id && holder.RequestedTheme == ElementTheme.Dark,
                    "主题代次与根不同步");
                receipts.Add(new { stage = "island", theme = theme.Id, state = _window.BackdropState,
                    contrast = new AccessibilitySettings().HighContrast, effects = new UISettings().AdvancedEffectsEnabled });
                await SaveProbeScreenAsync($"p2-island-{theme.Id}-screen.png");
            }

            var item = new EmbyItem { Id = "material-menu", Type = EmbyItemType.Movie, Name = "材质生命周期",
                ProviderIds = new() { ["Tmdb"] = "12345" }, MediaType = "Video" };
            fixture.Transport.Reply = (request, _) => Task.FromResult(
                request.RequestUri!.AbsolutePath.EndsWith("/Items/material-menu", StringComparison.Ordinal)
                    ? Json(item) : FakeTransport.Standard(request));
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
            var page = (DetailPage)frame.Content;
            await UntilAsync(() => page.IsReady);
            await LayoutAsync(page);
            fixture.Actions.MoviePilotEnabled = true;
            var materials = new HashSet<DetailMenuBackdrop>();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var anchor = Get<Button>(page, "ColumnMoreButton");
                ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(anchor)
                    .GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => Presenters().Length == 1);
                var main = Presenters()[0];
                await LayoutAsync(main);
                var submenu = main.Items.OfType<MenuFlyoutSubItem>().First();
                ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(submenu)
                    .GetPattern(PatternInterface.ExpandCollapse)).Expand();
                await UntilAsync(() => Presenters().Length == 2);
                var presenters = Presenters();
                foreach (var presenter in presenters) materials.Add((DetailMenuBackdrop)presenter.SystemBackdrop);
                foreach (var theme in UiThemes.All)
                {
                    ThemeHost.Apply(theme);
                    await LayoutAsync(page);
                    await WaitForDiagnosticsFrameAsync();
                    Require(Presenters().Length == 2 && presenters.All(presenter => Presenters().Contains(presenter)),
                        "切主题重建或关闭了菜单目标");
                    foreach (var presenter in presenters)
                    {
                        var material = (DetailMenuBackdrop)presenter.SystemBackdrop;
                        Require(material.TargetCount > 0 && material.UsesCardColor, "菜单目标未同步颜色：" + material.MaterialState);
                        var solidColor = new AccessibilitySettings().HighContrast
                            ? new UISettings().GetColorValue(UIColorType.Background)
                            : ThemeHost.ToColor(theme.Colors.SurfaceAlt.WithAlpha(0xFF));
                        Require(presenter.Background is SolidColorBrush brush
                            && (material.NeedsSolidSurface ? brush.Color == solidColor : brush.Color.A == 0),
                            "菜单透明/实色层没有跟随连接结果");
                        receipts.Add(new { stage = "menu", cycle, theme = theme.Id,
                            targets = material.TargetCount, attached = material.ConnectedTargetCount,
                            solid = material.SolidTargetCount, state = material.MaterialState,
                            host = _window.BackdropState });
                    }
                    // 此截图允许该主菜单和子菜单自己的Popup；宿主归属守卫仍检查主窗客户区。
                    if (cycle == 0) await SaveProbeScreenAsync($"p2-menu-{theme.Id}-screen.png");
                }
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).ToArray()) popup.IsOpen = false;
                await UntilAsync(() => Presenters().Length == 0 && materials.All(material => material.TargetCount == 0));
            }
            Require(materials.All(material => material.TargetCount == 0), "关闭菜单后仍有材质目标");
        }
        finally
        {
            if (frame.Content is IShellContent page) page.Release();
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).ToArray()) popup.IsOpen = false;
            _root.Children.Remove(holder);
            _root.Background = savedBackground;
            ThemeHost.Apply(savedTheme);
            _window.UseBackdrop = savedBackdrop;
            _window.TopMost = savedTopMost;
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "p2-material-lifecycle.json"),
                System.Text.Json.JsonSerializer.Serialize(receipts, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        MenuFlyoutPresenter[] Presenters() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
            .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().ToArray();
    }

    private async Task SettingsSystemAppearanceAsync()
    {
        var frame = new Frame();
        var canvas = new SolidColorBrush();
        var native = SettingsWindow.CreateCaptionedWindow(frame, canvas);
        var window = (SettingsWindow)Activator.CreateInstance(typeof(SettingsWindow),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            [native, frame, canvas, MicaController.IsSupported()], null)!;
        _openingProbeWindows.Add(window);
        // 真正 settings root/caption 及同一个 canvas；不启动VM或任何服务读取。
        var panel = new Grid();
        panel.Children.Add(new TextBlock { Text = "系统外观匿名检查", Margin = new Thickness(32) });
        frame.Content = panel;
        Call(window, "ApplyBackdrop");
        Call(window, "PaintCaption");
        native.Activate();
        Field(window, "_open", true);
        Call(window, "ResumeSystemAppearance");
        native.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));
        native.AppWindow.Move(new Windows.Graphics.PointInt32(1450, 80));
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controls = new StackPanel { Margin = new Thickness(32, 80, 32, 32), Spacing = 16 };
        panel.Children.Add(controls);
        var readings = new List<object>();
        var sample = new Button { Name = "P2SampleSystemAppearance", Content = "记录当前系统外观" };
        sample.Click += async (_, _) =>
        {
            try
            {
                var state = (SettingsWindow.SettingsSystemAppearance)Call(window, "ReadSystemAppearance")!;
                var expected = SettingsWindow.SelectCanvasColor(state, ThemeHost.ToColor(ThemeHost.Current.Colors.Window), window.BackdropState.Mica);
                Require(canvas.Color == expected, "系统事件后的画布未跟随当前状态");
                Require(SettingsBackdropCheck.Matches(MicaController.IsSupported(), window.BackdropState.Mica,
                    window.BackdropState.CanvasAlpha, state.HighContrast, state.AdvancedEffects), "设置窗口自检判据没有接受正确系统回退");
                var bar = native.AppWindow.TitleBar;
                Require(!state.HighContrast || bar.ButtonForegroundColor == state.Foreground, "高对比caption未使用系统前景");
                Require(!state.HighContrast || bar.ButtonHoverBackgroundColor == state.Highlight
                    && bar.ButtonPressedBackgroundColor == state.Highlight
                    && bar.ButtonHoverForegroundColor == state.HighlightText
                    && bar.ButtonPressedForegroundColor == state.HighlightText,
                    "高对比caption悬停/按下没有使用系统Highlight/HighlightText");
                await WaitForDiagnosticsFrameAsync();
                readings.Add(new { theme = ThemeHost.Current.Id, state.HighContrast, state.AdvancedEffects, canvas = canvas.Color.ToString(),
                    foreground = bar.ButtonForegroundColor?.ToString(),
                    hoverBackground = bar.ButtonHoverBackgroundColor?.ToString(), pressedBackground = bar.ButtonPressedBackgroundColor?.ToString(),
                    hoverForeground = bar.ButtonHoverForegroundColor?.ToString(), pressedForeground = bar.ButtonPressedForegroundColor?.ToString() });
                await CompositionPlaybackProbe.SaveWindowScreenAsync(window.Handle,
                    Path.Combine(_options.Paths.LogDirectory, $"p2-system-{readings.Count}-screen.png"));
                sample.Content = $"已记录 {readings.Count} 次系统外观";
            }
            catch (Exception error) { stop.TrySetException(error); }
        };
        controls.Children.Add(sample);
        var done = new Button { Name = "P2SystemAppearanceDone", Content = "结束系统外观检查" };
        done.Click += (_, _) => stop.TrySetResult();
        controls.Children.Add(done);
        try { await stop.Task.WaitAsync(TimeSpan.FromMinutes(8)); Require(readings.Count >= 3, "缺少系统状态切换采样"); }
        finally
        {
            File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "p2-system-appearance.json"),
                System.Text.Json.JsonSerializer.Serialize(readings));
            window.Hide();
            native.Content = null;
        }
    }
}

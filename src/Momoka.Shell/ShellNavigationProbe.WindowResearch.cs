using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;
using Momoka.Shell.Interop;
using Momoka.Shell.Windowing;
using Windows.Graphics;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>无服务、播放或注册表的 Window/AppWindow 与现有 HostWindow 兼容性研究。</summary>
    private async Task WindowHostResearchAsync(bool injectFailure = false)
    {
        // 主探针是 XAML 岛。隐藏 anchor 延长 framework Window 应用寿命，真正关闭研究窗不会提前结束整轮。
        var anchor = new Window { Content = new Grid(), Title = "Momoka P7 research anchor" };
        _shortcutProbeWindows.Add(anchor);
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        var input = new TextBox { Name = "WindowResearchDraft", Header = "匿名草稿", Text = "P7 draft" };
        var focus = new Button { Name = "WindowResearchFocus", Content = "焦点验证" };
        panel.Children.Add(input);
        panel.Children.Add(focus);
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(ThemeHost.ToColor(ThemeHost.Current.Colors.Window)) };
        root.Children.Add(panel);
        var window = new Window { Content = root, Title = "Momoka P7 Window/AppWindow research" };
        var research = new FrameworkWindowResearch(window);
        var app = window.AppWindow;
        var handle = Microsoft.UI.Win32Interop.GetWindowFromWindowId(app.Id);
        _shortcutProbeWindows.Add(window); // 创建期失败也由整轮终结者负责关闭，不留未登记的framework窗口。
        var baselineClient = _window!.ClientSize;
        var hostScale = _root.XamlRoot.RasterizationScale;
        var hostApp = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_window.Handle));
        var presenter = (OverlappedPresenter)app.Presenter;
        var readings = new List<object>();
        var hidden = 0;
        var changed = 0;
        var closed = 0;
        var failureObserved = false;
        research.Hidden += () => hidden++;
        void OnChanged(AppWindow sender, AppWindowChangedEventArgs args) => changed++;
        void OnClosed(object sender, WindowEventArgs args) => closed++;
        app.Changed += OnChanged;
        window.Closed += OnClosed;
        var workArea = DisplayArea.GetFromWindowId(hostApp.Id, DisplayAreaFallback.Nearest).WorkArea;
        try
        {
            presenter.IsAlwaysOnTop = true;
            app.Move(new PointInt32(workArea.X + 32, workArea.Y + 32));
            research.Show();
            await LayoutAsync(root);
            var scale = root.XamlRoot.RasterizationScale;
            foreach (var width in new[] { 600, 760, 1100 })
            {
                ResizeProbeClient(width, 600);
                await LayoutAsync(_root);
                Require(Math.Abs(_root.ActualWidth - width) < 2 && Math.Abs(_root.ActualHeight - 600) < 2,
                    "现有HostWindow客户区DIP未匹配实际client/nonclient尺寸请求");
                var physicalHost = _window.ClientSize;
                readings.Add(new { Host = "HostWindow", WidthDip = _root.ActualWidth, HeightDip = _root.ActualHeight,
                    Physical = new { physicalHost.Width, physicalHost.Height }, Scale = hostScale, AppWindow = hostApp.Id.Value });

                app.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(600 * scale)));
                await LayoutAsync(root);
                Require(Math.Abs(root.ActualWidth - width) < 2 && Math.Abs(root.ActualHeight - 600) < 2,
                    "framework Window客户区DIP未匹配AppWindow请求");
                Require(input.Focus(FocusState.Keyboard) && ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), input),
                    "framework Window文本框不能接受键盘焦点");
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(input)
                    ?? throw new InvalidOperationException("framework Window文本框没有原生UIA peer");
                Require(peer.GetClassName() == "TextBox" && peer.GetName() == "匿名草稿",
                    "framework Window的原生UIA文本框角色或名称缺失");
                Require(focus.Focus(FocusState.Keyboard) && ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), focus),
                    "framework Window按钮不能接受键盘焦点");
                Native.GetClientRect(handle, out var client);
                readings.Add(new { Host = "Window", WidthDip = root.ActualWidth, HeightDip = root.ActualHeight,
                    Physical = new { client.Width, client.Height }, Scale = scale, Peer = peer.GetClassName(), Name = peer.GetName() });
                if (!injectFailure && width == 760)
                    await CompositionPlaybackProbe.SaveScreenAsync(handle, Path.Combine(_options.Paths.LogDirectory, "window-host-research-760-screen.png"));
            }

            input.Text = "P7 retained draft";
            research.Hide();
            Require(!research.IsOpen && !Native.IsWindowVisible(handle) && hidden == 1, "研究窗隐藏状态或通知不正确");
            research.Hide();
            Require(hidden == 1, "重复隐藏重复通知");
            research.Show();
            await LayoutAsync(root);
            Require(input.Text == "P7 retained draft" && ReferenceEquals(window.Content, root), "研究窗重开替换草稿或原页面");
            Native.SendMessage(handle, Native.WmClose, IntPtr.Zero, IntPtr.Zero);
            await UntilAsync(() => !research.IsOpen && !Native.IsWindowVisible(handle));
            Require(!research.IsClosed && closed == 0 && hidden == 2, "研究窗用户关闭未取消并隐藏");
            research.Show();
            await LayoutAsync(root);
            Require(input.Text == "P7 retained draft", "用户关闭后重开丢失草稿");
            if (injectFailure) throw new WindowResearchFailure();
            Require(changed > 0, "AppWindow变化事件没有发生");
        }
        catch (WindowResearchFailure)
        {
            failureObserved = true;
        }
        finally
        {
            try
            {
                research.Dispose();
                await UntilAsync(() => research.IsClosed && !Native.IsWindowVisible(handle));
            }
            finally
            {
                app.Changed -= OnChanged;
                window.Closed -= OnClosed;
                hostApp.ResizeClient(new SizeInt32(baselineClient.Width, baselineClient.Height));
                _window.Activate();
            }
        }
        Require(research.IsClosed && closed == 1 && window.Content is null, "研究收尾未关闭或释放内容");
        Require(injectFailure == failureObserved, "故障路径未执行预定异常");
        var file = injectFailure ? "window-host-research-failure.json" : "window-host-research.json";
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, file), JsonSerializer.Serialize(new
        {
            Readings = readings, Hidden = hidden, Closed = closed, AppWindowChanges = changed, InjectedFailure = failureObserved,
            CleanupPassed = true, VideoLoaded = false, RealNetwork = false, MainHostMigrated = false,
            Unverified = new[] { "mpv composition", "HDR", "fullscreen video", "cross-monitor DPI", "Narrator", "desktop activation" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class WindowResearchFailure : Exception;
}

using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Real command surface held for keyboard input; every server request stays in the anonymous handler.</summary>
    private async Task BatchCommandBarKeyboardAsync()
    {
        using var fixture = CreateFixture();
        var server = new LibraryBatchServer(MediaServerKind.Emby, await FixtureImageAsync());
        fixture.Transport.Reply = server.ReplyAsync;
        await fixture.SelectAsync("batch-commandbar.invalid");
        var capabilities = new ServerCapabilities(fixture.Session);
        await capabilities.ProbeAsync();
        fixture.Actions.MenuPermissions = scope => capabilities.PermissionsFor(scope.Key);
        var frame = new Frame();
        var panel = new Grid { Margin = new Thickness(0, 40, 0, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var stop = new Button { Content = "结束键盘验证", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(stop, "BatchKeyboardDone");
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stop.Click += (_, _) => finished.TrySetResult();
        panel.Children.Add(stop);
        Grid.SetRow(frame, 1);
        panel.Children.Add(frame);
        _root.Children.Add(panel);
        LibraryPage? page = null;
        var readings = new List<object>();
        var topMost = _window!.TopMost;
        try
        {
            _window.TopMost = true;
            ResizeProbeClient(600, 760);
            frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services, Title = "匿名多选键盘验证", ParentId = "1",
                CollectionType = "movies", Source = fixture.Session.Capture()
            });
            page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.IsReady && !page.ViewModel.Busy && page.ViewModel.Cards.Count == 3);
            Call(page, "ProbeBeginSelection");
            var wall = Get<ItemsView>(page, "CardWall");
            for (var index = 0; index < 3; index++) wall.Select(index);
            await LayoutAsync(page);
            var bar = Get<CommandBar>(page, "LibraryBatchCommandBar");
            var actions = Get<AppBarButton>(page, "LibraryBatchActions");
            var clear = Get<AppBarButton>(page, "LibraryClearSelection");
            var menu = Get<MenuFlyout>(page, "LibraryBatchMenu");
            foreach (var width in new[] { 1100, 760, 600 })
            {
                ResizeProbeClient(width, 760);
                await LayoutAsync(page);
                await UntilAsync(() => Math.Abs(page.ActualWidth - width) < 1);
                Read("layout-" + width);
                Require(bar.ActualWidth > 0 && bar.ActualWidth <= page.ActualWidth + 1, "CommandBar没有约束在页面宽度内");
            }
            // Constrain the production toolbar in the fixture to exercise its real overflow route
            // independently of this machine's fonts. The product has no fixed toolbar width.
            bar.Width = 200;
            await LayoutAsync(page);
            await UntilAsync(() => Math.Abs(bar.ActualWidth - 200) < 1 && actions.IsInOverflow);
            Read("constrained-overflow");
            Require(actions.IsInOverflow, "匿名窄命令栏没有真正把批量按钮移入溢出");
            var more = Descendants(bar).OfType<Button>().Single(button => button.Name == "MoreButton");
            Invoke(more);
            await UntilAsync(() => bar.IsOpen && clear.IsLoaded);
            Require(clear.Focus(FocusState.Keyboard), "原生溢出操作不能接受键盘焦点");
            Read("overflow-ready");
            WriteStage("overflow-ready");
            await UntilStageAsync("overflow-closed", () => !bar.IsOpen && Popups() == 0);
            Require(wall.SelectedItems.Count == 3 && Get<FrameworkElement>(page, "LibraryBatchSurface").Visibility == Visibility.Visible,
                "首个Escape关闭溢出时结束了选择");
            Read("overflow-closed");
            Require(ReferenceEquals(FocusManager.GetFocusedElement(_root.XamlRoot), more), "关闭溢出后原生焦点没有回到更多按钮");
            WriteStage("selection-ready");
            await UntilStageAsync("selection-ended", () => Get<FrameworkElement>(page, "LibraryBatchSurface").Visibility == Visibility.Collapsed);
            Require(wall.SelectedItems.Count == 0 && wall.SelectionMode == ItemsViewSelectionMode.None, "第二个Escape未结束多选");
            Read("selection-ended");

            Call(page, "ProbeBeginSelection");
            for (var index = 0; index < 3; index++) wall.Select(index);
            await LayoutAsync(page);
            Invoke(more);
            await UntilAsync(() => bar.IsOpen);
            Require(actions.Focus(FocusState.Keyboard), "溢出中的批量按钮不能接受键盘焦点");
            Invoke(actions);
            await UntilAsync(() => menu.IsOpen);
            var mark = VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).SelectMany(popup => Descendants(popup.Child))
                .OfType<MenuFlyoutItem>().Single(item => item.Tag is ItemCommand.MarkPlayed);
            Require(mark.Focus(FocusState.Keyboard), "批量菜单项不能接受键盘焦点");
            Read("submenu-ready");
            WriteStage("submenu-ready");
            await UntilStageAsync("submenu-closed", () => !menu.IsOpen && ReferenceEquals(FocusManager.GetFocusedElement(_root.XamlRoot), actions));
            Require(wall.SelectedItems.Count == 3 && bar.IsOpen, "Escape关闭批量子菜单时没有保留父层和选择");
            Read("submenu-closed");
            WriteStage("parent-ready");
            await UntilStageAsync("parent-closed", () => !bar.IsOpen && Popups() == 0);
            Require(wall.SelectedItems.Count == 3, "Escape关闭父层时结束了选择");
            Read("parent-closed");
            Require(ReferenceEquals(FocusManager.GetFocusedElement(_root.XamlRoot), more), "关闭父溢出后原生焦点没有回到更多按钮");
            Require(server.Writes.Count == 0 && fixture.Actions.Plays == 0, "键盘打开或取消菜单产生了操作");
            await SaveProbeScreenAsync("batch-commandbar-keyboard-600-screen.png");
            WriteStage("complete");
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));

            int Popups() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).Count;

            void Read(string stage)
            {
                var focus = FocusManager.GetFocusedElement(_root.XamlRoot) as FrameworkElement;
                readings.Add(new
                {
                    Stage = stage, PageWidth = page.ActualWidth, BarWidth = bar.ActualWidth,
                    Overflow = bar.PrimaryCommands.OfType<AppBarButton>().Where(button => button.IsInOverflow).Select(button => button.Name).ToArray(),
                    bar.IsOpen, SubmenuOpen = menu.IsOpen, Selection = wall.SelectedItems.Count,
                    FocusName = focus?.Name, FocusType = focus?.GetType().Name, FocusId = focus is null ? null : AutomationProperties.GetAutomationId(focus),
                    Popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot).Count, Writes = server.Writes.Count
                });
                File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "batch-commandbar-keyboard.json"),
                    JsonSerializer.Serialize(readings, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally
        {
            page?.Release();
            frame.Content = null;
            _root.Children.Remove(panel);
            _window.TopMost = topMost;
        }

        void WriteStage(string stage) => File.WriteAllText(Path.Combine(_options.Paths.LogDirectory, "batch-commandbar-keyboard-stage.txt"), stage);
        async Task UntilStageAsync(string stage, Func<bool> ready)
        {
            await Task.Delay(200);
            for (var attempt = 0; attempt < 1200; attempt++)
            {
                if (ready()) { WriteStage(stage); return; }
                await Task.Delay(50);
            }
            throw new TimeoutException("未收到真实键盘阶段：" + stage);
        }
        static void Invoke(FrameworkElement control)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control);
            Require(peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "缺少正式UIA调用：" + control.Name);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        }
    }
}

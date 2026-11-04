using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotResourceSortMenuAsync(MoviePilotResourceBrowser view,
        MoviePilotResourceBrowserViewModel browser, MoviePilotResourceRow[] rows, int width)
    {
        browser.SetResults(rows);
        await LayoutAsync(view);
        var button = Get<DropDownButton>(view, "SortOrder");
        var menu = Get<MenuFlyout>(view, "SortMenu");
        var label = Get<TextBlock>(view, "SortLabel");
        var ascendingIcon = Get<PathIcon>(view, "SortAscendingIcon");
        var descendingIcon = Get<PathIcon>(view, "SortDescendingIcon");
        var directions = new[] { Get<RadioMenuFlyoutItem>(view, "SortAscending"), Get<RadioMenuFlyoutItem>(view, "SortDescending") };
        var fields = new[] { Get<RadioMenuFlyoutItem>(view, "SortDefault"), Get<RadioMenuFlyoutItem>(view, "SortSite"),
            Get<RadioMenuFlyoutItem>(view, "SortSize"), Get<RadioMenuFlyoutItem>(view, "SortSeeders"), Get<RadioMenuFlyoutItem>(view, "SortPublishedAt") };
        var originalRows = browser.Rows;
        var topMost = _window!.TopMost;
        try
        {
            // 展开菜单前置顶，避免截图时抬高宿主把它自己的 Popup 压到后面。
            _window.TopMost = true;
            Require(button.Focus(FocusState.Keyboard) && Math.Abs(button.ActualHeight - 32) < 1 && button.ActualWidth < 132
                && AutomationProperties.GetName(button).Contains("资源排序", StringComparison.Ordinal), "网页排序入口高度、内容宽度或键盘/UIA名称不对");
            Require(directions.All(item => item.GroupName == directions[0].GroupName) && directions[0].GroupName.Length > 0
                && fields.All(item => item.GroupName == fields[0].GroupName) && fields[0].GroupName != directions[0].GroupName,
                "排序方向与字段不是两组独立的单选");
            var other = new MoviePilotResourceBrowser();
            other.Attach(new MoviePilotResourceBrowserViewModel());
            Require(Get<RadioMenuFlyoutItem>(other, "SortAscending").GroupName != directions[0].GroupName
                && Get<RadioMenuFlyoutItem>(other, "SortDefault").GroupName != fields[0].GroupName,
                "两个资源浏览器共享了排序单选组");
            Require(VisibleIcon(descendingIcon), "排序方向图标没有实际绘出");
            descendingIcon.Visibility = Visibility.Collapsed;
            await LayoutAsync(view);
            Require(!VisibleIcon(descendingIcon), "图标检测没有识别故意隐藏的方向图标");
            descendingIcon.Visibility = Visibility.Visible;
            await LayoutAsync(view);

            await OpenAsync();
            CheckSelected();
            await CloseAsync(() => new MenuFlyoutItemAutomationPeer(directions[0]).Invoke());
            Require(VisibleIcon(ascendingIcon) && !VisibleIcon(descendingIcon), "选升序没有改变实际排序图标");
            await OpenAsync();
            CheckSelected();
            await CloseAsync(() => new MenuFlyoutItemAutomationPeer(fields[2]).Invoke());
            Require(label.Text == "体积" && !browser.SortDescending && ReferenceEquals(browser.Rows[0], rows[0]),
                "按体积升序没有真正排序，或选择字段改变了方向");
            await OpenAsync();
            CheckSelected();
            await CloseAsync(() => new MenuFlyoutItemAutomationPeer(fields[3]).Invoke());
            Require(label.Text == "做种数" && !browser.SortDescending && ReferenceEquals(browser.Rows[0], rows[^1]),
                "做种数升序不是从少到多，或入口名称没更新");
            await OpenAsync();
            CheckSelected();
            await CloseAsync(() => new MenuFlyoutItemAutomationPeer(directions[1]).Invoke());
            Require(ReferenceEquals(browser.Rows[0], rows[0]) && VisibleIcon(descendingIcon) && !VisibleIcon(ascendingIcon),
                "选降序没有改变真实顺序或方向图标");
            Require(ReferenceEquals(browser.Rows, originalRows) && rows[0].State == MoviePilotDownloadState.Uncertain,
                "切排序替换了集合或丢失下载反馈");
            await OpenAsync();
            CheckSelected();
            await CloseAsync(() => new MenuFlyoutItemAutomationPeer(fields[3]).Invoke());
            await OpenAsync();
            CheckSelected();
            await SaveProbeScreenAsync($"mp-resource-sort-menu-{width}-screen.png");
            await CloseAsync(view.ClosePopups);
            await SaveProbeScreenAsync($"mp-resource-sort-selected-{width}-screen.png");

        }
        finally
        {
            view.ClosePopups();
            _window.TopMost = topMost;
        }

        static bool VisibleIcon(PathIcon icon) => icon.Visibility == Visibility.Visible && icon.ActualWidth > 0 && icon.ActualHeight > 0;
        void CheckSelected()
        {
            Require(directions.Count(item => item.IsChecked) == 1 && directions[1].IsChecked == browser.SortDescending,
                $"排序方向菜单没有保持唯一选中：{string.Join(',', directions.Select(item => item.Text + "=" + item.IsChecked))}；VM降序={browser.SortDescending}");
            Require(fields.Count(item => item.IsChecked) == 1 && fields[browser.SortIndex].IsChecked,
                $"排序字段菜单没有保持唯一选中：{string.Join(',', fields.Select(item => item.Text + "=" + item.IsChecked))}；VM字段={browser.SortIndex}");
            foreach (var item in directions.Concat(fields))
            {
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
                Require(peer is not null && peer.GetAutomationId() == item.Name && peer.GetName() == item.Text,
                    $"排序菜单 {item.Name} 缺少实际UIA名称或稳定句柄");
            }
        }
        async Task CloseAsync(Action request)
        {
            var closed = Pending<bool>();
            void OnClosed(object? sender, object args) => closed.TrySetResult(true);
            menu.Closed += OnClosed;
            try
            {
                request();
                // IsOpen 在退场动画开始时已变成 false；Closed 才证明能够再次打开。
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await UntilAsync(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(view.XamlRoot)
                    .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().Any());
                await LayoutAsync(view);
            }
            finally { menu.Closed -= OnClosed; }
        }
        async Task OpenAsync()
        {
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => menu.IsOpen);
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(view.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().Single();
            await LayoutAsync(presenter);
            var bounds = presenter.TransformToVisual(_root).TransformBounds(new Windows.Foundation.Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
            Require(bounds.X >= -1 && bounds.Y >= -1 && bounds.Right <= _root.ActualWidth + 1 && bounds.Bottom <= _root.ActualHeight + 1,
                $"{width}宽排序菜单超出窗口：{bounds} / {_root.ActualWidth:0.#}×{_root.ActualHeight:0.#}");
        }
    }
}

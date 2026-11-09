using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.MoviePilot;
using Momoka.Shell.Interop;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>实际输入框/菜单/分组的匿名回归；不移动指针、不授权任何整理写入。</summary>
    private async Task MoviePilotSeriesResourceBrowserAsync(MoviePilotSeriesReorganizeDialog picker, SeriesReorganizeServer server)
    {
        var vm = picker.ViewModel;
        var list = Get<ListView>(picker, "SeriesResources");
        var search = Get<AutoSuggestBox>(picker, "ResourceSearch");
        ResizeInspect(1120, 960);
        await LayoutAsync(picker);
        RequireSearchRadius(search);
        var original = vm.Rows.ToArray();
        var historyReads = server.Queries.Count;
        var topMost = _window!.TopMost;
        try
        {
            _window.TopMost = true;
            var input = Descendants(search).OfType<TextBox>().Single();
            var searchPeer = FrameworkElementAutomationPeer.CreatePeerForElement(search);
            Require(searchPeer is AutoSuggestBoxAutomationPeer && searchPeer.GetAutomationId() == "ResourceSearch",
                "新搜索栏没有实际UIA句柄或原生查询能力");
            // WinUI 文本的 ValuePattern 由原生适配器提供，不是 WinRT IValueProvider。
            // 在真实搜索控件填词，核对内部 TextBox 的文字事件，再通过原生查询 peer 提交；不直接改 VM。
            async Task EnterAsync(string text)
            {
                search.Text = text;
                await LayoutAsync(picker);
                Require(input.Text == text && vm.SearchText == text,
                    $"搜索文字事件未接通：内部文字={input.Text}，查询文字={search.Text}，状态={vm.SearchText}");
                ((AutoSuggestBoxAutomationPeer)searchPeer).Invoke();
            }
            InvokeSeriesButton(picker, "SelectAllResources");
            await EnterAsync("*S01E0?*.mkv E02");
            await UntilAsync(() => vm.Rows.Count == 1 && vm.Rows[0].History.Id == 2);
            Require(vm.SelectedCount == 1 && original.Where(row => row.History.Id != 2).All(row => !row.Selected),
                "搜索没有保留当前匹配选择，或筛掉的资源仍授权整理");
            await SaveProbeScreenAsync("mp-series-search-filtered-screen.png");
            await EnterAsync("");
            await UntilAsync(() => vm.Rows.Count == 6);
            Require(vm.SelectedCount == 1 && ReferenceEquals(original[1], vm.Rows[1]),
                "清除搜索重选隐藏记录或丢失原行快照");

            await ChooseAsync("StatusFilterButton", "StatusFilterMenu", "StatusSuccess");
            Require(vm.StatusFilter == MoviePilotTransferStatusFilter.Success && vm.Rows.Count == 6, "成功筛选没有采用原记录状态");
            await ChooseAsync("StatusFilterButton", "StatusFilterMenu", "StatusFailed");
            Require(vm.Rows.Count == 0 && vm.EmptyVisibility == Visibility.Visible && vm.SelectedCount == 0 && !picker.IsPrimaryButtonEnabled,
                "失败筛选零结果没有空态或仍能整理成功记录");
            await SaveProbeScreenAsync("mp-series-search-empty-screen.png");
            await ChooseAsync("StatusFilterButton", "StatusFilterMenu", "StatusAll");
            Require(vm.Rows.Count == 6 && vm.SelectedCount == 0, "全部状态没有恢复未选择资源");
            vm.SetSelected([vm.Rows[1]], true);
            await ChooseAsync("ResourceSortButton", "ResourceSortMenu", "SortTitle");
            Require(vm.Sort == MoviePilotTransferSort.Title && !vm.Descending && Get<MenuFlyoutItem>(picker, "SortDirection").Visibility == Visibility.Visible,
                "标题排序没有提供有效方向");
            await ChooseAsync("ResourceSortButton", "ResourceSortMenu", "SortDirection");
            Require(vm.Descending && vm.Rows.Select(row => row.History.Id).SequenceEqual(new long[] { 6, 5, 4, 3, 2, 1 }),
                "标题降序没有真正改变显示顺序");
            Require(vm.SelectedCount == 1 && vm.Rows.Single(row => row.History.Id == 2).Selected,
                "排序取消了原选择或授权了其它记录");
            await ChooseAsync("ResourceSortButton", "ResourceSortMenu", "SortSize");
            Require(vm.Descending && vm.Rows[0].History.Id == 6, "更换排序字段没有保留方向或未按实际文件大小排序");
            foreach (var field in new[] { "SortPath", "SortMode", "SortTime", "SortStatus" })
                await ChooseAsync("ResourceSortButton", "ResourceSortMenu", field);
            await ChooseAsync("ResourceSortButton", "ResourceSortMenu", "SortDefault");
            Require(!vm.Descending && vm.Rows.SequenceEqual(original) && Get<MenuFlyoutItem>(picker, "SortDirection").Visibility == Visibility.Collapsed,
                "默认顺序未还原接口顺序，或留下无效方向入口");
            await ChooseAsync("ResourceViewButton", "ResourceViewMenu", "ViewGrouped");
            Require(vm.Grouped && vm.Groups.Count == 1 && vm.Groups[0].Members.Count == 6, "分组模式不是实际标题分组");
            await LayoutAsync(picker);
            var groupToggle = Descendants(list).OfType<Button>().Single(button => button.Name == "ToggleSeriesResourceGroup");
            Require(FrameworkElementAutomationPeer.CreatePeerForElement(groupToggle)?.GetAutomationId() == "ToggleSeriesResourceGroup",
                "模板中的分组按钮没有运行时UIA句柄");
            InvokeGroup(groupToggle);
            await LayoutAsync(picker);
            Require(vm.Groups[0].Count == 0 && vm.SelectedCount == 1 && picker.IsPrimaryButtonEnabled,
                "折叠分组丢失原勾选，或折叠时不能继续已明确选择的范围");
            InvokeSeriesButton(picker, "ClearResourceSelection");
            Require(vm.SelectedCount == 0 && !picker.IsPrimaryButtonEnabled, "折叠分组后清空没有撤销选择");
            var groupSelect = Descendants(list).OfType<Button>().Single(button => button.Name == "SelectSeriesResourceGroup");
            InvokeGroup(groupSelect);
            Require(vm.SelectedCount == 6 && vm.Rows.All(row => row.Selected), "折叠状态的全选本组没有选择实际成员");
            await SaveProbeScreenAsync("mp-series-group-collapsed-screen.png");
            InvokeGroup(groupToggle);
            await LayoutAsync(picker);
            Require(vm.SelectedCount == 6 && Descendants(list).OfType<CheckBox>().Where(box => box.Name == "SelectSeriesResource")
                .All(box => box.IsChecked == true), "展开分组没有恢复原生行选择或复选框状态");
            await SaveProbeScreenAsync("mp-series-group-expanded-screen.png");
            await ChooseAsync("ResourceViewButton", "ResourceViewMenu", "ViewList");
            Require(!vm.Grouped && vm.SelectedCount == 6 && ReferenceEquals(list.ItemsSource, vm.Rows), "返回列表模式丢失当前选择或仍显示分组头");
            InvokeSeriesButton(picker, "ClearResourceSelection");
            var originalTheme = ThemeHost.Current.Id;
            try
            {
                foreach (var theme in Momoka.Theming.UiThemes.All)
                {
                    ThemeHost.Apply(theme.Id);
                    await LayoutAsync(picker);
                    await OpenAsync("ResourceSortButton", "ResourceSortMenu");
                    await SaveProbeScreenAsync($"mp-series-menu-{theme.Id}-screen.png");
                    await CloseAsync(Get<MenuFlyout>(picker, "ResourceSortMenu"), () => Get<MenuFlyout>(picker, "ResourceSortMenu").Hide());
                }
            }
            finally { ThemeHost.Apply(originalTheme); }
            Require(server.Queries.Count == historyReads && server.Writes == 0 && server.PreviewIds.Length == 0,
                "搜索、筛选、排序、分组或选择发起了额外历史读取或整理预览/写入");
            foreach (var width in new[] { 1120, 760, 420 })
            {
                ResizeInspect(width, 960);
                await LayoutAsync(picker);
                var panel = Get<Grid>(picker, "ResourcePanel");
                Require(search.ActualWidth > 120 && list.ActualWidth > 180 && list.ActualHeight > 100, "窄窗搜索或清单被工具栏挤没");
                var bounds = search.TransformToVisual(panel).TransformBounds(new Windows.Foundation.Rect(0, 0, search.ActualWidth, search.ActualHeight));
                Require(bounds.X >= -1 && bounds.Right <= panel.ActualWidth + 1, "搜索栏超出窄窗内容边界");
                foreach (var (button, menu) in new[]
                {
                    ("StatusFilterButton", "StatusFilterMenu"), ("ResourceSortButton", "ResourceSortMenu"), ("ResourceViewButton", "ResourceViewMenu")
                })
                {
                    await OpenAsync(button, menu);
                    await SaveProbeScreenAsync($"mp-series-{menu}-{width}-screen.png");
                    await CloseAsync(Get<MenuFlyout>(picker, menu), () => Get<MenuFlyout>(picker, menu).Hide());
                }
            }
        }
        finally
        {
            foreach (var name in new[] { "StatusFilterMenu", "ResourceSortMenu", "ResourceViewMenu" }) Get<MenuFlyout>(picker, name).Hide();
            _window.TopMost = topMost;
            ResizeInspect(1120, 960);
        }

        static void InvokeGroup(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        async Task ChooseAsync(string buttonName, string menuName, string itemName)
        {
            await OpenAsync(buttonName, menuName);
            var item = Get<MenuFlyoutItem>(picker, itemName);
            await CloseAsync(Get<MenuFlyout>(picker, menuName), () => new MenuFlyoutItemAutomationPeer(item).Invoke());
        }
        async Task OpenAsync(string buttonName, string menuName)
        {
            _window!.Activate();
            var handle = _window.Handle;
            Require(handle != nint.Zero, "整理清单探针窗口没有有效句柄");
            await UntilAsync(() => Native.GetForegroundWindow() == handle);
            var button = Get<Button>(picker, buttonName);
            Require(button.IsLoaded && button.ActualWidth > 0 && button.ActualHeight > 0, "菜单按钮未完成布局");
            InvokeSeriesButton(picker, buttonName);
            var menu = Get<MenuFlyout>(picker, menuName);
            await UntilAsync(() => menu.IsOpen);
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().Single();
            await LayoutAsync(presenter);
            Require(BrushColor(presenter.Background).A == 255,
                "整理清单菜单仍为透明背景，长路径会与选项文字重叠");
            var bounds = presenter.TransformToVisual(_root).TransformBounds(new Windows.Foundation.Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
            Require(bounds.X >= -1 && bounds.Y >= -1 && bounds.Right <= _root.ActualWidth + 1 && bounds.Bottom <= _root.ActualHeight + 1,
                "整理清单菜单超出窗口可用边界：" + bounds);
            var choices = menu.Items.OfType<RadioMenuFlyoutItem>().ToArray();
            Require(choices.Count(item => item.IsChecked) == 1 && choices.All(item => item.GroupName == choices[0].GroupName),
                "整理清单菜单没有唯一单选标记或共享本组标识");
            foreach (var item in menu.Items.OfType<MenuFlyoutItem>().Where(item => item.Visibility == Visibility.Visible))
            {
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
                Require(peer is not null && peer.GetAutomationId() == item.Name && peer.GetName() == item.Text,
                    "菜单缺少运行时UIA句柄或名称：" + item.Name);
            }
        }
        async Task CloseAsync(MenuFlyout menu, Action request)
        {
            // 截图前后系统可能已轻关闭菜单；订阅一个不会再到来的 Closed 会误报超时。
            request();
            await UntilAsync(() => !menu.IsOpen && !VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutPresenter>().Any());
            await LayoutAsync(picker);
        }
    }
}

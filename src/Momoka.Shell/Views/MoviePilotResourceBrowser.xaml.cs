using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotResourceBrowser : UserControl
{
    private readonly string _sortGroup = Guid.NewGuid().ToString("N");
    private string SortDirectionGroup => $"mp-resource-direction-{_sortGroup}";
    private string SortFieldGroup => $"mp-resource-field-{_sortGroup}";
    public MoviePilotResourceBrowserViewModel ViewModel { get; private set; } = new();

    public MoviePilotResourceBrowser() => InitializeComponent();

    internal void Attach(MoviePilotResourceBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    private static Visibility AscendingIconVisibility(bool descending) => descending ? Visibility.Collapsed : Visibility.Visible;
    private static Visibility DescendingIconVisibility(bool descending) => descending ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 点卡片（指针单击或键盘回车）：打开下载选择栏。卡片上的详情方钮自己处理指针，不会走到这里。
    /// 身份待核对、已加入下载或结果不明的行按钮不可执行，这一次点按什么也不做 —— 卡片说明里已经写清原因。
    /// </summary>
    private void OnResourceInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is MoviePilotResourceRow row) OpenDownload(row);
    }

    /// <summary>卡片的下载入口；探针绕开真实指针时也走这一支。</summary>
    internal bool OpenDownload(MoviePilotResourceRow row)
    {
        if (!row.DownloadCommand.CanExecute(null)) return false;
        row.DownloadCommand.Execute(null);
        return true;
    }

    private void OnSortMenuOpening(object sender, object e)
    {
        // 原生单选在模板初始化/组名落地时会重算；每次打开从当前 VM 读回，不与编译绑定抢选中状态。
        SortAscending.IsChecked = !ViewModel.SortDescending;
        SortDescending.IsChecked = ViewModel.SortDescending;
        foreach (var item in SortMenu.Items.OfType<RadioMenuFlyoutItem>().Where(item => item.Tag is string))
            item.IsChecked = item.Tag is string value && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index == ViewModel.SortIndex;
    }

    private void OnSortDirectionClick(object sender, RoutedEventArgs e) => ViewModel.SortDescending = ReferenceEquals(sender, SortDescending);

    private void OnSortFieldClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string value } && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index >= 0 && index < MoviePilotResourceBrowserViewModel.SortLabels.Count)
            ViewModel.SortIndex = index;
    }

    internal void ClosePopups()
    {
        foreach (var facet in new[] { SiteFilter, SeasonFilter, PromotionFilter, VideoFilter, EditionFilter, ResolutionFilter, GroupFilter })
            facet.ClosePopup();
        AdvancedFilterDialog?.Hide();
        SortMenu.Hide();
    }

    /// <summary>当前打开的综合筛选弹窗；检查台用它取真实控件，关窗或换页时一并收掉。</summary>
    internal MoviePilotResourceFilterDialog? AdvancedFilterDialog { get; private set; }

    private void OnAdvancedFilters(object sender, RoutedEventArgs e) => OpenAdvancedFilters();

    /// <summary>
    /// 打开综合筛选弹窗（网页端「全部筛选」的对应物）。弹窗与结果共用同一份视图模型，
    /// 勾选即生效；点外面或关闭都只是收起，不改已经选中的条件。
    /// </summary>
    internal MoviePilotResourceFilterDialog OpenAdvancedFilters()
    {
        if (AdvancedFilterDialog is { } opened) return opened;
        var dialog = new MoviePilotResourceFilterDialog(ViewModel)
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme
        };
        AdvancedFilterDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(AdvancedFilterDialog, dialog)) AdvancedFilterDialog = null;
        };
        _ = dialog.ShowAsync();
        return dialog;
    }
}

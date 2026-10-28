using System.Globalization;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        AdvancedFilters.Hide();
        SortMenu.Hide();
        foreach (var filter in new[] { LabelFilter, DateFilter, EpisodeFilter })
            filter.IsDropDownOpen = false;
    }
}

using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>剧资源清单只读取和选择；关闭清单后才打开整理预览与提交表单。</summary>
public sealed partial class MoviePilotSeriesReorganizeDialog : ContentDialog
{
    public MoviePilotSeriesReorganizeViewModel ViewModel { get; } = new();
    internal IReadOnlyList<MoviePilotTransferHistory> SelectedHistories { get; private set; } = [];
    private XamlRoot? _layoutRoot;

    internal MoviePilotSeriesReorganizeDialog(MoviePilotService service, MoviePilotTransferContext context)
    {
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        ViewModel.Attach(service, context);
        ViewModel.PresentationChanged += OnPresentationChanged;
        SeriesGroupsSource.Source = ViewModel.Groups;
        SeriesResources.ItemsSource = ViewModel.Rows;
        var groupKey = Guid.NewGuid().ToString("N");
        foreach (var item in StatusFilterMenu.Items.OfType<RadioMenuFlyoutItem>()) item.GroupName = groupKey + "-status";
        foreach (var item in ResourceSortMenu.Items.OfType<RadioMenuFlyoutItem>()) item.GroupName = groupKey + "-sort";
        foreach (var item in ResourceViewMenu.Items.OfType<RadioMenuFlyoutItem>()) item.GroupName = groupKey + "-view";
        SyncMenus();
        Opened += OnOpened;
        Closed += (_, _) =>
        {
            if (_layoutRoot is not null) _layoutRoot.Changed -= OnRootChanged;
            _layoutRoot = null;
            ViewModel.PresentationChanged -= OnPresentationChanged;
            ViewModel.Dispose();
        };
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _layoutRoot = XamlRoot;
        if (_layoutRoot is not null) _layoutRoot.Changed += OnRootChanged;
        FitPanel();
        await ViewModel.ReloadAsync().ConfigureAwait(true);
    }
    private async void OnReload(object sender, RoutedEventArgs args) => await ViewModel.ReloadAsync().ConfigureAwait(true);
    private void OnSelectAll(object sender, RoutedEventArgs args) => ViewModel.SelectAll();
    private void OnClearSelection(object sender, RoutedEventArgs args) => ViewModel.ClearSelection();
    private void OnResourceClicked(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is MoviePilotSeriesTransferRow row) ViewModel.SetSelected([row], !row.Selected);
    }
    private void OnPresentationChanged()
    {
        // 单一选择来源是资源行，不是会在筛选、重排、分组时自行清空的原生 SelectedItems。
        var source = ViewModel.Grouped ? (object)SeriesGroupsSource.View : ViewModel.Rows;
        if (!ReferenceEquals(SeriesResources.ItemsSource, source)) SeriesResources.ItemsSource = source;
        SyncMenus();
    }
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ViewModel.SearchText = sender.Text;
    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => ViewModel.SearchText = args.QueryText;
    private void OnMenuOpening(object sender, object args) => SyncMenus();
    private void OnStatusFilter(object sender, RoutedEventArgs args)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag } && Enum.TryParse<MoviePilotTransferStatusFilter>(tag, out var status))
            ViewModel.StatusFilter = status;
        SyncMenus();
    }
    private void OnSort(object sender, RoutedEventArgs args)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag } && Enum.TryParse<MoviePilotTransferSort>(tag, out var sort))
            ViewModel.Sort = sort;
        SyncMenus();
    }
    private void OnSortDirection(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Sort != MoviePilotTransferSort.Default) ViewModel.Descending = !ViewModel.Descending;
    }
    private void OnViewMode(object sender, RoutedEventArgs args)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag }) ViewModel.Grouped = tag == "True";
        SyncMenus();
    }
    private void OnGroupToggle(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: MoviePilotSeriesTransferGroup group }) ViewModel.ToggleGroup(group);
    }
    private void OnSelectGroup(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: MoviePilotSeriesTransferGroup group } && ViewModel.Groups.Contains(group))
            ViewModel.SetSelected(group.Members, true);
    }
    private void SyncMenus()
    {
        foreach (var item in StatusFilterMenu.Items.OfType<RadioMenuFlyoutItem>())
            item.IsChecked = (string)item.Tag == ViewModel.StatusFilter.ToString();
        foreach (var item in ResourceSortMenu.Items.OfType<RadioMenuFlyoutItem>())
            item.IsChecked = (string)item.Tag == ViewModel.Sort.ToString();
        ViewList.IsChecked = !ViewModel.Grouped;
        ViewGrouped.IsChecked = ViewModel.Grouped;
    }
    private void OnSelectionTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args) => args.Handled = true;
    private void OnContinue(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try { SelectedHistories = ViewModel.Select(); }
        catch (MoviePilotException error)
        {
            args.Cancel = true;
            ViewModel.SelectionProblem(error);
        }
    }
    private void OnResourceLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: MoviePilotSeriesTransferRow row })
            _ = ViewModel.LoadArtworkAsync(row);
    }
    private void OnResourceContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is MoviePilotSeriesTransferRow row) _ = ViewModel.LoadArtworkAsync(row);
    }
    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs args) => FitPanel();
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => FitPanel();
    private void FitPanel()
    {
        if (XamlRoot is not { } root) return;
        ResourcePanel.Width = Math.Clamp(root.Size.Width - 96, 240, 940);
        ResourcePanel.MaxHeight = Math.Clamp(root.Size.Height - 220, 120, 660);
    }
    public static bool CanReload(bool busy) => !busy;
}

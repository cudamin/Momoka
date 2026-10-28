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
    private bool _syncingSelection;

    internal MoviePilotSeriesReorganizeDialog(MoviePilotService service, MoviePilotTransferContext context)
    {
        InitializeComponent();
        RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        ViewModel.Attach(service, context);
        Opened += OnOpened;
        Closed += (_, _) =>
        {
            if (_layoutRoot is not null) _layoutRoot.Changed -= OnRootChanged;
            _layoutRoot = null;
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
    private void OnSelectAll(object sender, RoutedEventArgs args) => SeriesResources.SelectAll();
    private void OnClearSelection(object sender, RoutedEventArgs args) => SeriesResources.SelectedItems.Clear();
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        _syncingSelection = true;
        try
        {
            foreach (var row in args.RemovedItems.OfType<MoviePilotSeriesTransferRow>()) row.Selected = false;
            foreach (var row in args.AddedItems.OfType<MoviePilotSeriesTransferRow>()) row.Selected = true;
        }
        finally { _syncingSelection = false; }
        ViewModel.SelectedCount = SeriesResources.SelectedItems.Count;
    }
    private void OnResourceCheckChanged(object sender, RoutedEventArgs args)
    {
        if (_syncingSelection || sender is not CheckBox { DataContext: MoviePilotSeriesTransferRow row } box ||
            !ViewModel.Rows.Contains(row)) return;
        if (box.IsChecked == true && !SeriesResources.SelectedItems.Contains(row)) SeriesResources.SelectedItems.Add(row);
        else if (box.IsChecked != true) SeriesResources.SelectedItems.Remove(row);
    }
    private void OnSelectionTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args) => args.Handled = true;
    private void OnContinue(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try { SelectedHistories = ViewModel.Select(SeriesResources.SelectedItems.OfType<MoviePilotSeriesTransferRow>()); }
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

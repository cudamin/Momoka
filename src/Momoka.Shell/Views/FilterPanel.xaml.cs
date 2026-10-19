using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 筛选面板：网页端 <c>modules/filtermenu</c> 那一块。All of it is <see cref="FilterPanelViewModel"/>;
/// what is left here is the contract the library page holds it by.
/// <para>
/// Nothing in this file draws anything. The rows are built by the view model and drawn by the templates
/// in the markup, which is what the 332 lines this replaced were doing by hand — every <c>CheckBox</c>,
/// <c>Grid</c> and <c>Expander</c> constructed in code, pushed into a named <c>StackPanel</c>, and its
/// handlers attached and detached around every programmatic change.
/// </para>
/// </summary>
public sealed partial class FilterPanel : UserControl
{
    public FilterPanel()
    {
        // Constructed with the panel rather than handed over afterwards, so x:Bind never has a null root;
        // the same reason SignInPage gives.
        ViewModel = new FilterPanelViewModel();
        InitializeComponent();

        // Forwarded rather than re-exposed, so the page keeps subscribing to the panel it can see in its
        // own markup and gets the panel as the sender.
        ViewModel.Changed += (_, e) => Changed?.Invoke(this, e);
    }

    /// <summary>
    /// Something was ticked. The page reloads its grid and writes the selection down.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The ✕ was pressed: the page takes the panel off the page.</summary>
    public event EventHandler? CloseRequested;

    internal FilterPanelViewModel ViewModel { get; }

    /// <inheritdoc cref="FilterPanelViewModel.Labels"/>
    internal IReadOnlyList<string> Labels => ViewModel.Labels;

    /// <inheritdoc cref="FilterPanelViewModel.RowLabels"/>
    internal IReadOnlyList<string> RowLabels => ViewModel.RowLabels;

    /// <summary>
    /// Whether every shape the view model built has a template to be drawn in, for the self-check. This is
    /// the one thing about this panel that no amount of correct view-model state would reveal: a
    /// <see cref="DataTemplateSelector"/> that returns null is legal and silent — see
    /// <see cref="FilterSectionTemplates"/>, which is a selector this codebase has already been caught by
    /// once — so the panel would come up empty with nothing anywhere saying why.
    /// </summary>
    internal bool EveryTemplateResolves =>
        Resources["EgFilterRows"] is FilterSectionTemplates templates
        && ViewModel.Rows.Count > 0
        && ViewModel.Rows.All(row => templates.SelectTemplate(row, this) is not null);

    /// <inheritdoc cref="FilterPanelViewModel.Open"/>
    internal void Open(
        ItemFilters selection,
        string? preset,
        Version? serverVersion,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> lookup,
        MediaServerKind serverKind = MediaServerKind.Emby,
        IReadOnlyList<EmbyFilterField>? fields = null)
    {
        FilterScroll.ChangeView(null, 0, null, disableAnimation: true);
        ViewModel.Open(selection, preset, serverVersion, lookup, serverKind, fields);
    }

    /// <inheritdoc cref="FilterPanelViewModel.Dismiss"/>
    internal void Dismiss() => ViewModel.Dismiss();

    private void OnCloseClicked(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The checkbox list under a 类型/家长评分/工作室/年份 row was opened. The row asks the server the first
    /// time this happens rather than when the panel opens: five lists nobody looked at is five requests
    /// nobody needed.
    /// </summary>
    private void OnValuesClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FilterValuesRow row }) row.IsOpen = true;
    }

    private void OnValuesClosed(object? sender, object e)
    {
        if ((sender as Flyout)?.Target is FrameworkElement { DataContext: FilterValuesRow row })
            row.IsOpen = false;
    }
}

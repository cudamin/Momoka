using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotResourceBrowser : UserControl
{
    public MoviePilotResourceBrowserViewModel ViewModel { get; private set; } = new();

    public MoviePilotResourceBrowser() => InitializeComponent();

    internal void Attach(MoviePilotResourceBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    internal void ClosePopups()
    {
        foreach (var facet in new[] { SiteFilter, SeasonFilter, PromotionFilter, VideoFilter, EditionFilter, ResolutionFilter, GroupFilter })
            facet.ClosePopup();
        AdvancedFilters.Hide();
        foreach (var filter in new[] { LabelFilter, DateFilter, SortOrder, EpisodeFilter })
            filter.IsDropDownOpen = false;
    }
}

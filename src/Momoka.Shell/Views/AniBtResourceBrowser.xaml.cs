using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class AniBtResourceBrowser : UserControl
{
    public AniBtBrowserViewModel ViewModel { get; private set; } = new();

    public AniBtResourceBrowser() => InitializeComponent();

    internal void Attach(AniBtBrowserViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    internal void ClosePopups()
    {
        foreach (var picker in new[] { EpisodeFilter, GroupFilter, ResolutionFilter, SortOrder })
            picker.IsDropDownOpen = false;
    }
}

using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSitePicker : UserControl
{
    public MoviePilotSitePicker() => InitializeComponent();
    public MoviePilotSitePickerViewModel ViewModel { get; private set; } = new();

    internal void Attach(MoviePilotSitePickerViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    private void OnOpening(object? sender, object args) => _ = ViewModel.EnsureLoadedAsync();

    internal void ClosePopup() => SitesFlyout.Hide();
}

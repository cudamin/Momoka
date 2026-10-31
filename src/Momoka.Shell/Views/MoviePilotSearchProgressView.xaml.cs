using Microsoft.UI.Xaml.Controls;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSearchProgressView : UserControl
{
    public MoviePilotSearchProgressView() => InitializeComponent();

    internal MoviePilotSearchProgressViewModel ViewModel { get; private set; } = new();

    internal void Attach(MoviePilotSearchProgressViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }
}

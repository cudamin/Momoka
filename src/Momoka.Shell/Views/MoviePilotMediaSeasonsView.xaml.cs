using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotMediaSeasonsView : UserControl
{
    internal MoviePilotMediaViewModel ViewModel { get; private set; } = new();
    public MoviePilotMediaSeasonsView() => InitializeComponent();
    internal void Attach(MoviePilotMediaViewModel viewModel) { ViewModel = viewModel; Bindings.Update(); }
    private void OnEpisodeSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Grid grid) return;
        var compact = args.NewSize.Width < 440;
        var picture = (Border)grid.Children[0];
        var content = (StackPanel)grid.Children[1];
        Grid.SetColumn(content, compact ? 0 : 1);
        Grid.SetRow(content, compact ? 1 : 0);
        Grid.SetColumnSpan(content, compact ? 2 : 1);
        picture.Width = compact ? Math.Min(240, Math.Max(0, args.NewSize.Width)) : 180;
        picture.Height = picture.Width * 9 / 16;
    }
}

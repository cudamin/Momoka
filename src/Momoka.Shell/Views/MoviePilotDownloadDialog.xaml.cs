using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotDownloadDialog : ContentDialog
{
    public MoviePilotDownloadViewModel ViewModel { get; } = new();
    public MoviePilotDownloadDialog() => InitializeComponent();

    private void OnDismiss(object sender, RoutedEventArgs args) => Hide();

    private async void OnMediaSelected(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is MoviePilotMedia media) await ViewModel.SelectMatchAsync(media);
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 360;
        DownloadTitle.Width = args.NewSize.Width;
        Place(DownloadPath, DestinationGrid);
        Place(DownloadMediaSource, IdentityGrid);
        void Place(FrameworkElement field, Grid grid)
        {
            grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(field, narrow ? 0 : 1);
            Grid.SetRow(field, narrow ? 1 : 0);
        }
    }

    internal static async Task<MoviePilotDownloadOptions?> ShowAsync(FrameworkElement owner, MoviePilotService service,
        MoviePilotResource resource, CancellationToken token)
    {
        if (owner.XamlRoot is not { } root || token.IsCancellationRequested) return null;
        var dialog = new MoviePilotDownloadDialog
        {
            XamlRoot = root,
            RequestedTheme = ThemeHost.Current.IsDark ? ElementTheme.Dark : ElementTheme.Light
        };
        dialog.ViewModel.Attach(service, resource);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = !dialog.ViewModel.CanSubmit;
            if (!args.Cancel) dialog.ViewModel.SavePath = dialog.DownloadPath.Text;
        };
        using var registration = token.Register(() => owner.DispatcherQueue.TryEnqueue(() =>
        {
            dialog.ViewModel.Cancel();
            dialog.Hide();
        }));
        try
        {
            _ = dialog.ViewModel.ReloadAsync();
            var result = await MoviePilotDialog.ShowAsync(dialog, service);
            return result == ContentDialogResult.Primary && !token.IsCancellationRequested ? dialog.ViewModel.Options : null;
        }
        catch (Exception error)
        {
            Log.Warn("moviepilot", "无法显示下载选项", error);
            return null;
        }
        finally { dialog.ViewModel.Cancel(); }
    }
}

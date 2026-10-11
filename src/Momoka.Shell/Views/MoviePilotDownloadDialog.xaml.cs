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
        dialog.PrimaryButtonClick += (_, _) => dialog.ViewModel.SavePath = dialog.DownloadPath.Text;
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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotSeasonPicker : ContentDialog
{
    public MoviePilotMediaViewModel ViewModel { get; } = new();
    public MoviePilotSeasonPicker() => InitializeComponent();

    internal static async Task ShowAsync(FrameworkElement owner, MoviePilotService service, MoviePilotMedia media, string group, int? season,
        CancellationToken token = default)
    {
        if (owner.XamlRoot is null || token.IsCancellationRequested) return;
        var dialog = new MoviePilotSeasonPicker { XamlRoot = owner.XamlRoot, RequestedTheme = owner.ActualTheme };
        dialog.ViewModel.Attach(service, media, group, picker: true, preselect: season, lifetimeToken: token);
        using var cancelled = token.Register(() => owner.DispatcherQueue.TryEnqueue(() =>
        {
            dialog.ViewModel.Cancel();
            dialog.Hide();
        }));
        try
        {
            _ = dialog.ViewModel.ReloadAsync();
            await MoviePilotDialog.ShowAsync(dialog, service);
        }
        finally { dialog.ViewModel.Cancel(); }
    }

    private async void OnSubmit(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var deferral = args.GetDeferral();
        try { if (await ViewModel.SubmitAsync()) { args.Cancel = false; } }
        finally { deferral.Complete(); }
    }
    private async void OnReview(object sender, RoutedEventArgs args) => await ViewModel.ReviewAsync();
}

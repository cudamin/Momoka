using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool UsesMicaBackground => ContentFrame.Content switch
    {
        HomePage or LibraryPage or LocalLibraryPage or MusicLibraryPage or SettingsPage => true,
        DetailPage detail => detail.UsesMicaBackground,
        MoviePilotSubscriptionPage subscription => subscription.UsesMicaBackground,
        _ => false
    };

    private void SyncMicaSurfaces()
    {
        // A transparent navigation host exposes the existing island's single Mica Alt.
        // Restore the original roles on other pages, including cached back/forward navigation.
        ContentHost.Background = ContentFrame.Content is SettingsPage
            ? Microsoft.UI.Xaml.Application.Current.Resources["EgMicaCommandBrush"] as Brush
            : UsesMicaBackground ? null
            : Microsoft.UI.Xaml.Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush;
        SidebarHost.Background = UsesMicaBackground
            ? Microsoft.UI.Xaml.Application.Current.Resources["EgMicaCommandBrush"] as Brush
            : Microsoft.UI.Xaml.Application.Current.Resources["EgSurfaceBrush"] as Brush;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Momoka.Shell.Views;

public sealed partial class MusicLibraryPage
{
    // Gallery MainWindow.xaml.cs, CtrlF_Invoked, v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7.
    // Hidden server-specific searches and detached pages must not consume the accelerator.
    private void CtrlF_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_released || _request is null || !MusicSearch.IsLoaded
            || MusicSearch.Visibility != Visibility.Visible || !MusicSearch.IsEnabled) return;
        args.Handled = MusicSearch.Focus(FocusState.Programmatic);
    }
}

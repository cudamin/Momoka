using Momoka.Shell.ViewModels;

namespace Microsoft.UI.Xaml
{
    public sealed class RoutedEventArgs : EventArgs;
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom);
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class ItemsView
    {
        public Microsoft.UI.Xaml.Thickness Padding { get; set; }
    }

    public sealed class ListView
    {
        public Microsoft.UI.Xaml.Thickness Padding { get; set; }
    }

    public sealed class Button
    {
        public Microsoft.UI.Xaml.Thickness Margin { get; set; }
    }
}

namespace Momoka.Shell.Views
{
    // Methods come from MusicLibraryPage.xaml.cs at build time; no paging decision is copied here.
    public sealed partial class MusicLibraryPage(MusicLibraryViewModel viewModel)
    {
        private bool _released;
        private LibraryRequest? _request;
        internal Microsoft.UI.Xaml.Controls.AutoSuggestBox MusicSearch { get; } = new();
        public MusicLibraryViewModel ViewModel { get; } = viewModel;
        internal Microsoft.UI.Xaml.Controls.ItemsView MusicWall { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ListView MusicRows { get; } = new();
        internal Microsoft.UI.Xaml.Controls.Button WallMoreButton { get; } = new();
        internal void InvokeMore() => OnMore(this, new Microsoft.UI.Xaml.RoutedEventArgs());
        internal void AttachSearch(LibraryRequest request) { _request = request; _released = false; }
        internal void ReleaseSearch() => _released = true;
        internal bool InvokeFind()
        {
            var args = new Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs();
            CtrlF_Invoked(new(), args);
            return args.Handled;
        }
    }
}

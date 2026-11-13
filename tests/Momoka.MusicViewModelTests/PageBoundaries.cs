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
    internal sealed partial class MusicLibraryPage(MusicLibraryViewModel viewModel)
    {
        public MusicLibraryViewModel ViewModel { get; } = viewModel;
        internal Microsoft.UI.Xaml.Controls.ItemsView MusicWall { get; } = new();
        internal Microsoft.UI.Xaml.Controls.ListView MusicRows { get; } = new();
        internal Microsoft.UI.Xaml.Controls.Button WallMoreButton { get; } = new();
        internal void InvokeMore() => OnMore(this, new Microsoft.UI.Xaml.RoutedEventArgs());
    }
}

using System.Collections.ObjectModel;
using Momoka.Shell.ViewModels;

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public sealed class SizeChangedEventArgs : EventArgs { public (double Width, double Height) NewSize { get; set; } }
    public class FrameworkElement { public object? Tag { get; set; } public double Width { get; set; } public double Height { get; set; } public void StartBringIntoView() { } }
    public sealed class Application { public static Application? Current => null; public Dictionary<string, object> Resources { get; } = []; }
}
namespace Microsoft.UI.Xaml.Media { public class Brush; }
namespace Microsoft.UI.Xaml.Media.Imaging { public class BitmapImage; }
namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public class UserControl : Microsoft.UI.Xaml.FrameworkElement;
    public class Grid : Microsoft.UI.Xaml.FrameworkElement { public List<object> Children { get; } = []; }
    public class Border : Microsoft.UI.Xaml.FrameworkElement;
    public class ComboBox : Microsoft.UI.Xaml.FrameworkElement { public int SelectedIndex { get; set; } }
    public class SelectionChangedEventArgs : EventArgs;
    public class ItemsRepeater { public object? ItemsSource { get; set; } public object GetOrCreateElement(int index) => new Microsoft.UI.Xaml.FrameworkElement(); }
    public class ItemsRepeaterElementPreparedEventArgs : EventArgs { public int Index { get; set; } }
}
namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> DecodeAsync(byte[] bytes, double width) => throw new InvalidOperationException("No image decoding in fixture");
    }
}
namespace Momoka.Shell.ViewModels
{
    // The media metadata owner is inert: the actual subscription VM and actual view own all tested control flow.
    public sealed partial class MoviePilotMediaViewModel
    {
        public ObservableCollection<MoviePilotMediaSeasonRow> Seasons { get; } = [];
        public string GroupId { get; set; } = "";
        private Momoka.MoviePilot.MoviePilotMedia? _media;
        private readonly CancellationTokenSource _groupLoad = new();
        internal bool OldGroupCancelled => _groupLoad.IsCancellationRequested;
        internal bool Live { get; set; } = true;
        internal int Reloads { get; private set; }
        internal void Seed(Momoka.MoviePilot.MoviePilotSubscription subscription) { _media = subscription.Media; GroupId = subscription.EpisodeGroup; }
        public Task ReloadAsync() { Reloads++; return Task.CompletedTask; }
    }
}
namespace Momoka.Shell.Views
{
    public sealed class InertBindings { public void Update() { } }
    public sealed partial class MoviePilotMediaSeasonsView
    {
        private InertBindings Bindings { get; } = new();
        private Microsoft.UI.Xaml.Controls.ItemsRepeater MediaSeasonList { get; } = new();
        private void InitializeComponent() { }
    }
}

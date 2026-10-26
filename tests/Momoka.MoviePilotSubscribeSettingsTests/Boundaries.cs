// Presentation and bitmap decode boundaries only. Actual Core service/client, complete VMs,
// Toolkit generators and dialog code execute; no real window, credentials IO or socket exists.
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public bool TryEnqueue(Action action) { action(); return true; }
    }
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum ElementTheme { Default, Light, Dark }
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom);
    public sealed class XamlRoot;
    public class RoutedEventArgs : EventArgs;
    public class FrameworkElement
    {
        public XamlRoot? XamlRoot { get; set; } = new();
        public ElementTheme ActualTheme { get; set; }
        public ElementTheme RequestedTheme { get; set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
    }
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage
    {
        public BitmapImage() { }
        public BitmapImage(Uri uri) { Uri = uri; }
        public Uri? Uri { get; }
        public int DecodePixelWidth { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum InfoBarSeverity { Informational, Success, Warning, Error }
    public enum ContentDialogResult { None, Primary, Secondary }
    public class ContentDialog : Microsoft.UI.Xaml.FrameworkElement
    {
        public static Func<ContentDialog, Task<ContentDialogResult>>? ShowEndpoint { get; set; }
        public static int Created { get; private set; }
        public static void Reset() { ShowEndpoint = null; Created = 0; }
        public ContentDialog() { Created++; }
        public bool Hidden { get; private set; }
        public Task<ContentDialogResult> ShowAsync() => ShowEndpoint?.Invoke(this) ?? Task.FromResult(ContentDialogResult.None);
        public void Hide() { Hidden = true; }
    }
    public sealed class ContentDialogButtonClickEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
        public Deferral GetDeferral() => new();
    }
    public sealed class Deferral { public void Complete() { } }
}

namespace Momoka.Shell.Media
{
    internal static class PosterLoader
    {
        internal static Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> DecodeAsync(byte[] bytes, int width) =>
            Task.FromResult<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?>(bytes.Length == 0 ? null : new() { DecodePixelWidth = width });
    }
}

namespace Momoka.Shell.ViewModels
{
    // The media VM stores this existing detail-page host type but picker mode never uses it.
    public sealed class MoviePilotSubscriptionsViewModel;
    public sealed class MoviePilotSubscriptionEpisodeRow;
}

namespace Momoka.Shell.Views
{
    public sealed partial class MoviePilotSeasonPicker
    {
        private void InitializeComponent() { }
    }
}

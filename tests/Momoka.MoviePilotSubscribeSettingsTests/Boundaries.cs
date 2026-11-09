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
    public readonly record struct Thickness(double Left, double Top, double Right, double Bottom)
    {
        public Thickness(double uniform) : this(uniform, uniform, uniform, uniform) { }
    }
    public sealed class XamlRoot;
    public sealed class ResourceDictionary : System.Collections.Generic.Dictionary<object, object>;
    public sealed class Application
    {
        public static Application? Current { get; set; }
        public ResourceDictionary Resources { get; } = new();
    }
    public class RoutedEventArgs : EventArgs;
    public sealed class ExceptionRoutedEventArgs : RoutedEventArgs;
    public delegate void ExceptionRoutedEventHandler(object sender, ExceptionRoutedEventArgs args);
    public class FrameworkElement
    {
        public object? Tag { get; set; }
        public XamlRoot? XamlRoot { get; set; } = new();
        public ElementTheme ActualTheme { get; set; }
        public ElementTheme RequestedTheme { get; set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush;
}

namespace Microsoft.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage
    {
        public BitmapImage() { }
        public BitmapImage(Uri uri) { Uri = uri; }
        public event Microsoft.UI.Xaml.ExceptionRoutedEventHandler? ImageFailed { add { } remove { } }
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
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Deferral GetDeferral() => new(() => Completion.TrySetResult());
    }
    public sealed class Deferral(Action complete) { public void Complete() => complete(); }
}

namespace Momoka.Shell.Media
{
    // Picker contracts do not decode backdrop pixels; fail closed if that boundary is reached.
    internal static class HeroPictureLoader
    {
        public sealed record HeroPixels(byte[] Pixels, int Width, int Height);
        public static Task<HeroPixels?> DecodePixelsAsync(byte[] bytes, int maxWidth, CancellationToken token) =>
            throw new InvalidOperationException("Subscription picker fixture must not decode backdrop pixels");
    }

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

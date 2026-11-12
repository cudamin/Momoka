// Event and attached-property boundaries only. Production CardArtwork, CardItem and Core execute;
// this windowless test does not establish native virtualization, WinRT decoding or screen pixels.
namespace Microsoft.UI.Xaml
{
    public sealed class PropertyMetadata(object? defaultValue, Action<DependencyObject, DependencyPropertyChangedEventArgs>? changed = null)
    {
        internal object? DefaultValue => defaultValue;
        internal Action<DependencyObject, DependencyPropertyChangedEventArgs>? Changed => changed;
    }
    public sealed class DependencyProperty(PropertyMetadata metadata)
    {
        internal PropertyMetadata Metadata => metadata;
        public static DependencyProperty RegisterAttached(string name, Type propertyType, Type owner, PropertyMetadata metadata) => new(metadata);
    }
    public sealed class DependencyPropertyChangedEventArgs(object? oldValue, object? newValue)
    {
        public object? OldValue => oldValue;
        public object? NewValue => newValue;
    }
    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object?> _values = [];
        public object? GetValue(DependencyProperty property) => _values.TryGetValue(property, out var value) ? value : property.Metadata.DefaultValue;
        public void SetValue(DependencyProperty property, object? value)
        {
            var old = GetValue(property);
            _values[property] = value;
            if (!ReferenceEquals(old, value)) property.Metadata.Changed?.Invoke(this, new(old, value));
        }
        public void ClearValue(DependencyProperty property) => _values.Remove(property);
    }
    public sealed class RoutedEventArgs : EventArgs;
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class Image : DependencyObject
    {
        public object? XamlRoot { get; set; }
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        internal int LoadedHandlers => Loaded?.GetInvocationList().Length ?? 0;
        internal int UnloadedHandlers => Unloaded?.GetInvocationList().Length ?? 0;
        internal void Load() { XamlRoot = new(); Loaded?.Invoke(this, new()); }
        internal void Unload(bool phantom = false)
        {
            if (!phantom) XamlRoot = null;
            Unloaded?.Invoke(this, new());
        }
    }
}

namespace Momoka.Shell.Views
{
    internal static class BrowsePresentation
    {
        internal static Task Ready { get; set; } = Task.CompletedTask;
        internal static int ExplicitWaits { get; set; }
        internal static Task WaitForImagesAsync() => Ready;
        internal static Task WaitForImagesAsync(Microsoft.UI.Xaml.Controls.Image image)
        {
            ExplicitWaits++;
            return Ready;
        }
    }
    internal sealed class ImageArrivalFade(Microsoft.UI.Xaml.Controls.Image image) : IDisposable
    {
        internal static int Living { get; private set; }
        private readonly Microsoft.UI.Xaml.Controls.Image _image = Track(image);
        private static Microsoft.UI.Xaml.Controls.Image Track(Microsoft.UI.Xaml.Controls.Image image) { Living++; return image; }
        internal void Stop() { }
        public void Dispose() { Living--; GC.KeepAlive(_image); }
    }
}

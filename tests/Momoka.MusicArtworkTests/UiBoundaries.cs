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
        public List<DependencyObject> Children { get; } = [];
        public object? XamlRoot { get; set; }
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
    public class UIElement : DependencyObject;
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class Image : UIElement
    {
        public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? Source { get; set; }
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
    public sealed class ItemsView : UIElement;
    public sealed class ItemsRepeater : UIElement
    {
        private readonly Dictionary<UIElement, int> _realized = [];
        public event Action<ItemsRepeater, ItemsRepeaterElementPreparedEventArgs>? ElementPrepared;
        public event Action<ItemsRepeater, ItemsRepeaterElementClearingEventArgs>? ElementClearing;
        public int GetElementIndex(UIElement element) => _realized.GetValueOrDefault(element, -1);
        internal int PreparedHandlers => ElementPrepared?.GetInvocationList().Length ?? 0;
        internal int ClearingHandlers => ElementClearing?.GetInvocationList().Length ?? 0;
        internal void Prepare(UIElement element, int index)
        {
            if (!Children.Contains(element)) Children.Add(element);
            _realized[element] = index;
            ElementPrepared?.Invoke(this, new(element, index));
        }
        internal void Clear(UIElement element)
        {
            _realized.Remove(element);
            // 同属主回收池故意保留Tree/XamlRoot与x:Bind项，只结束layout持有。
            ElementClearing?.Invoke(this, new(element));
        }
    }
    public sealed class ItemsRepeaterElementPreparedEventArgs(UIElement element, int index)
    {
        public UIElement Element => element;
        public int Index => index;
    }
    public sealed class ItemsRepeaterElementClearingEventArgs(UIElement element)
    {
        public UIElement Element => element;
    }
    public class ListViewBase : UIElement;
    public sealed class ContainerContentChangingEventArgs(UIElement itemContainer, bool inRecycleQueue, object? item = null)
    {
        public UIElement ItemContainer => itemContainer;
        public bool InRecycleQueue => inRecycleQueue;
        public object? Item => item;
        private Action<ListViewBase, ContainerContentChangingEventArgs>? _callback;
        public void RegisterUpdateCallback(Action<ListViewBase, ContainerContentChangingEventArgs> callback) => _callback = callback;
        internal Action<ListViewBase, ContainerContentChangingEventArgs>? Callback => _callback;
        internal void RunCallback()
        {
            var callback = _callback;
            _callback = null;
            callback?.Invoke(new(), this);
        }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public static class VisualTreeHelper
    {
        public static int GetChildrenCount(Microsoft.UI.Xaml.DependencyObject element) => element.Children.Count;
        public static Microsoft.UI.Xaml.DependencyObject GetChild(Microsoft.UI.Xaml.DependencyObject element, int index) => element.Children[index];
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
    public sealed partial class MusicLibraryPage
    {
        private bool _released;
        internal Microsoft.UI.Xaml.Controls.ItemsView MusicWall { get; } = new();
        internal void LoadArtwork() => OnWallLoaded(this, new());
        internal void UnloadArtwork() => OnWallUnloaded(this, new());
        internal void ReleaseArtwork() => DetachWallArtwork();
        internal void SetReleased(bool value) => _released = value;
        internal static void RecycleTrack(Microsoft.UI.Xaml.UIElement container, bool recycle)
        {
            var item = ArtworkTree(container).OfType<Microsoft.UI.Xaml.Controls.Image>().Select(CardArtwork.GetItem).FirstOrDefault();
            var args = new Microsoft.UI.Xaml.Controls.ContainerContentChangingEventArgs(container, recycle, item);
            new MusicLibraryPage().OnTrackContentChanging(new Microsoft.UI.Xaml.Controls.ListViewBase(), args);
            if (!recycle) args.RunCallback();
        }
        internal Microsoft.UI.Xaml.Controls.ContainerContentChangingEventArgs PrepareTrack(Microsoft.UI.Xaml.UIElement container, CardItem item)
        {
            var args = new Microsoft.UI.Xaml.Controls.ContainerContentChangingEventArgs(container, false, item);
            OnTrackContentChanging(new Microsoft.UI.Xaml.Controls.ListViewBase(), args);
            return args;
        }
        internal void ClearTrack(Microsoft.UI.Xaml.UIElement container)
        {
            OnTrackContentChanging(new Microsoft.UI.Xaml.Controls.ListViewBase(), new(container, true));
        }
    }
}

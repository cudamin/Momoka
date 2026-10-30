// Inert WinUI boundaries, not an implementation of the production dismissal behavior.
namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
    public readonly record struct Rect(double X, double Y, double Width, double Height)
    {
        public bool Contains(Point p) => p.X >= X && p.Y >= Y && p.X <= X + Width && p.Y <= Y + Height;
    }
}

namespace Microsoft.UI.Xaml
{
    public sealed class RoutedEvent;
    public class RoutedEventArgs : EventArgs;
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public sealed class DependencyPropertyChangedEventArgs(object? value) : EventArgs
    {
        public object? NewValue { get; } = value;
    }
    public sealed class PropertyMetadata(object? value, Action<DependencyObject, DependencyPropertyChangedEventArgs> changed)
    {
        internal object? Default { get; } = value;
        internal Action<DependencyObject, DependencyPropertyChangedEventArgs> Changed { get; } = changed;
    }
    public sealed class DependencyProperty(PropertyMetadata metadata)
    {
        internal PropertyMetadata Metadata { get; } = metadata;
        public static DependencyProperty RegisterAttached(string name, Type type, Type owner, PropertyMetadata metadata) => new(metadata);
    }
    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object?> _values = [];
        internal List<DependencyObject> Children { get; } = [];
        public object GetValue(DependencyProperty property) => (_values.GetValueOrDefault(property, property.Metadata.Default))!;
        public void SetValue(DependencyProperty property, object? value)
        {
            if (Equals(GetValue(property), value)) return;
            _values[property] = value;
            property.Metadata.Changed(this, new(value));
        }
    }
    public class UIElement : DependencyObject
    {
        private readonly List<(Input.PointerEventHandler Handler, bool HandledToo)> _pressed = [];
        public static RoutedEvent PointerPressedEvent { get; } = new();
        public int HandlerCount => _pressed.Count;
        public void AddHandler(RoutedEvent routedEvent, object handler, bool handledEventsToo) =>
            _pressed.Add(((Input.PointerEventHandler)handler, handledEventsToo));
        public void RemoveHandler(RoutedEvent routedEvent, object handler) =>
            _pressed.RemoveAll(entry => entry.Handler == (Input.PointerEventHandler)handler);
        public void Press(Input.PointerRoutedEventArgs args)
        {
            foreach (var entry in _pressed.ToArray())
                if (!args.Handled || entry.HandledToo) entry.Handler(this, args);
        }
    }
    public class FrameworkElement : UIElement
    {
        public string Name { get; set; } = "";
        public double ActualWidth { get; set; }
        public double ActualHeight { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsLoaded { get; set; }
        public bool IsHitTestVisible { get; set; } = true;
        public XamlRoot? XamlRoot { get; set; }
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public void Load() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void Unload() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
        public Media.GeneralTransform TransformToVisual(UIElement target) => new(X, Y);
    }
    public sealed class XamlRoot
    {
        public List<Controls.Primitives.Popup> Popups { get; } = [];
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class Border : FrameworkElement;
    public enum ContentDialogResult { None, Primary, Secondary }
    public sealed class ContentDialogOpenedEventArgs : EventArgs;
    public sealed class ContentDialogClosedEventArgs : EventArgs;
    public sealed class ContentDialogClosingEventArgs : EventArgs { public bool Cancel { get; set; } }
    public delegate void OpenedHandler(ContentDialog sender, ContentDialogOpenedEventArgs args);
    public delegate void ClosedHandler(ContentDialog sender, ContentDialogClosedEventArgs args);
    public delegate void ClosingHandler(ContentDialog sender, ContentDialogClosingEventArgs args);
    public class ContentDialog : FrameworkElement
    {
        public event OpenedHandler? Opened;
        public event ClosedHandler? Closed;
        public event ClosingHandler? Closing;
        public bool IsOpen { get; private set; }
        public int HideCalls { get; private set; }
        public ContentDialogResult? Result { get; private set; }
        public void Open() { IsOpen = true; Result = null; Opened?.Invoke(this, new()); }
        public void Hide()
        {
            HideCalls++;
            if (!IsOpen) return;
            var args = new ContentDialogClosingEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel) return;
            IsOpen = false;
            Result = ContentDialogResult.None;
            Closed?.Invoke(this, new());
        }
    }
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public sealed class Popup { public UIElement? Child { get; set; } }
}

namespace Microsoft.UI.Xaml.Input
{
    public delegate void PointerEventHandler(object sender, PointerRoutedEventArgs args);
    public sealed class PointerRoutedEventArgs(Windows.Foundation.Point position) : EventArgs
    {
        public bool Handled { get; set; }
        public int CoordinateReads { get; private set; }
        public PointerPoint GetCurrentPoint(UIElement target) { CoordinateReads++; return new(position); }
    }
    public sealed record PointerPoint(Windows.Foundation.Point Position);
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class GeneralTransform(double x, double y)
    {
        public Windows.Foundation.Rect TransformBounds(Windows.Foundation.Rect rect) => new(rect.X + x, rect.Y + y, rect.Width, rect.Height);
    }
    public static class VisualTreeHelper
    {
        public static int GetChildrenCount(DependencyObject root) => root.Children.Count;
        public static DependencyObject GetChild(DependencyObject root, int index) => root.Children[index];
        public static IReadOnlyList<Controls.Primitives.Popup> GetOpenPopupsForXamlRoot(XamlRoot root) => root.Popups;
    }
}

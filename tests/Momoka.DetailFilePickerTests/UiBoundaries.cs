// Only WinUI event, dependency-property, rendering and system-notification endpoints are replaced.
// Tests compile and execute the production DetailFilePicker.cs selection/lifecycle implementation.
using System.Collections;
using System.Xml.Linq;

namespace Windows.System
{
    public enum VirtualKey { Escape, Enter, Space, Down, Up }
}

namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    }
}

namespace Windows.UI.ViewManagement
{
    public enum UIElementType { Highlight, Window }
    public sealed class AccessibilitySettings
    {
        public static bool SystemHighContrast { get; set; }
        public bool HighContrast => SystemHighContrast;
        public static List<AccessibilitySettings> Instances { get; } = [];
        public AccessibilitySettings() => Instances.Add(this);
        private Action<AccessibilitySettings, object>? _highContrastChanged;
        public event Action<AccessibilitySettings, object>? HighContrastChanged
        {
            add => throw new global::System.Runtime.InteropServices.COMException("Unsupported event", unchecked((int)0x80070490));
            remove => _highContrastChanged -= value;
        }
        public int Subscribers => _highContrastChanged?.GetInvocationList().Length ?? 0;
        public static void NotifyHighContrast(bool highContrast)
        {
            SystemHighContrast = highContrast;
            foreach (var settings in Instances.ToArray()) settings._highContrastChanged?.Invoke(settings, new());
        }
    }
    public sealed class UISettings
    {
        public static List<UISettings> Instances { get; } = [];
        public static Windows.UI.Color Highlight { get; set; } = new(255, 2, 67, 121);
        public static Windows.UI.Color Window { get; set; } = new(255, 1, 2, 3);
        public UISettings() => Instances.Add(this);
        private Action<UISettings, object>? _colorValuesChanged;
        public event Action<UISettings, object>? ColorValuesChanged
        {
            add => throw new global::System.Runtime.InteropServices.COMException("Unsupported event", unchecked((int)0x80070490));
            remove => _colorValuesChanged -= value;
        }
        public int Subscribers => _colorValuesChanged?.GetInvocationList().Length ?? 0;
        public Windows.UI.Color UIElementColor(UIElementType type) => type == UIElementType.Highlight ? Highlight : Window;
        public static void NotifyColors()
        {
            foreach (var settings in Instances.ToArray()) settings._colorValuesChanged?.Invoke(settings, new());
        }
    }
}

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = [];
        public bool Accept { get; set; } = true;
        public int Pending => _pending.Count;
        public bool TryEnqueue(Action callback)
        {
            if (!Accept) return false;
            _pending.Enqueue(callback);
            return true;
        }
        public void Drain() { while (_pending.TryDequeue(out var callback)) callback(); }
    }
}

namespace Microsoft.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public enum HorizontalAlignment { Left, Center, Right, Stretch }
    public enum FocusState { Programmatic, Keyboard, Pointer }
    public readonly record struct Size(double Width, double Height);
    public sealed class XamlRoot { public Size Size { get; set; } = new(1280, 720); }
    public sealed record PropertyPath(string Path);
    public delegate void PropertyChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs args);
    public delegate void DependencyPropertyChangedCallback(DependencyObject sender, DependencyProperty property);
    public sealed class PropertyMetadata(object? value, PropertyChangedCallback? callback = null)
    {
        public object? DefaultValue { get; } = value;
        public PropertyChangedCallback? Callback { get; } = callback;
    }
    public sealed record DependencyProperty(string Name, Type Type, PropertyMetadata Metadata)
    {
        public static DependencyProperty Register(string name, Type type, Type owner, PropertyMetadata metadata) => new(name, type, metadata);
    }
    public sealed class DependencyPropertyChangedEventArgs(object? oldValue, object? newValue)
    {
        public object? OldValue { get; } = oldValue;
        public object? NewValue { get; } = newValue;
    }
    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object?> _values = [];
        private readonly Dictionary<long, (DependencyProperty Property, DependencyPropertyChangedCallback Callback)> _callbacks = [];
        private long _nextToken;
        public DependencyObject? Parent { get; set; }
        public Dictionary<DependencyProperty, Data.Binding> Bindings { get; } = [];
        public int PropertyCallbacks => _callbacks.Count;
        public object? GetValue(DependencyProperty property) => _values.GetValueOrDefault(property, property.Metadata.DefaultValue);
        public void SetValue(DependencyProperty property, object? value)
        {
            var previous = GetValue(property);
            if (property.Type == typeof(object) ? ReferenceEquals(previous, value) : Equals(previous, value)) return;
            _values[property] = value;
            property.Metadata.Callback?.Invoke(this, new(previous, value));
            foreach (var (_, (registered, callback)) in _callbacks.ToArray())
                if (ReferenceEquals(registered, property)) callback(this, property);
        }
        public long RegisterPropertyChangedCallback(DependencyProperty property, DependencyPropertyChangedCallback callback)
        {
            var token = ++_nextToken;
            _callbacks.Add(token, (property, callback));
            return token;
        }
        public void UnregisterPropertyChangedCallback(DependencyProperty property, long token) => _callbacks.Remove(token);
        public void SetBinding(DependencyProperty property, Data.Binding binding) => Bindings[property] = binding;
    }
    public sealed class RoutedEventArgs : EventArgs;
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public class UIElement : DependencyObject
    {
        public static readonly DependencyProperty VisibilityProperty = DependencyProperty.Register("Visibility", typeof(Visibility), typeof(FrameworkElement), new(Visibility.Visible));
        public static readonly object KeyDownEvent = new();
        public Visibility Visibility { get => (Visibility)GetValue(VisibilityProperty)!; set => SetValue(VisibilityProperty, value); }
    }
    public class FrameworkElement : UIElement
    {
        private readonly Dictionary<string, FrameworkElement> _names = [];
        private Input.KeyEventHandler? _keyDown;
        public object? DataContext { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsLoaded { get; private set; }
        public double ActualWidth { get; set; } = 240;
        public double MaxWidth { get; set; } = double.PositiveInfinity;
        public double MinWidth { get; set; }
        public double MaxHeight { get; set; } = double.PositiveInfinity;
        public XamlRoot? XamlRoot { get; set; } = new();
        public HorizontalAlignment HorizontalAlignment { get; set; }
        public Dictionary<string, object> Resources { get; } = [];
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public void Load() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void NotifyLoaded() => Loaded?.Invoke(this, new());
        public void Unload() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
        public void DelayedUnload() => Unloaded?.Invoke(this, new());
        public void AddHandler(object routedEvent, object handler, bool handledEventsToo) => _keyDown += (Input.KeyEventHandler)handler;
        public Input.KeyRoutedEventArgs RaiseKey(Windows.System.VirtualKey key)
        {
            var args = new Input.KeyRoutedEventArgs(key);
            _keyDown?.Invoke(this, args);
            return args;
        }
        public void UpdateLayout() { }
        public void RegisterName(string name, FrameworkElement element) => _names[name] = element;
        public object FindName(string name) => _names[name];
        public bool Focus(FocusState state) { Input.FocusManager.Current = this; return true; }
    }
    public sealed class DataTemplate(string markup) { public string Markup { get; } = markup; }
    public sealed class ItemsPanelTemplate(string markup) { public string Markup { get; } = markup; }
    public sealed class Style(string markup) { public string Markup { get; } = markup; }
}

namespace Microsoft.UI.Xaml.Input
{
    public sealed class KeyRoutedEventArgs(Windows.System.VirtualKey key) : EventArgs
    {
        public Windows.System.VirtualKey Key { get; } = key;
        public bool Handled { get; set; }
    }
    public delegate void KeyEventHandler(object sender, KeyRoutedEventArgs args);
    public static class FocusManager
    {
        public static object? Current { get; set; }
        public static object? GetFocusedElement(Microsoft.UI.Xaml.XamlRoot root) => Current;
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class SolidColorBrush(Windows.UI.Color color)
    {
        public Windows.UI.Color Color { get; set; } = color;
    }
    public static class VisualTreeHelper
    {
        public static Microsoft.UI.Xaml.DependencyObject? GetParent(Microsoft.UI.Xaml.DependencyObject element) => element.Parent;
    }
}

namespace Microsoft.UI.Xaml.Data
{
    public sealed class Binding
    {
        public object? Source { get; set; }
        public Microsoft.UI.Xaml.PropertyPath? Path { get; set; }
    }
    public sealed class CollectionViewSource
    {
        private object? _source;
        public bool IsSourceGrouped { get; set; }
        public object? Source
        {
            get => _source;
            set
            {
                _source = value;
                View = value is IEnumerable groups ? groups.Cast<IEnumerable>().SelectMany(group => group.Cast<object>()).ToList() : [];
            }
        }
        public object View { get; private set; } = new List<object>();
    }
}

namespace Microsoft.UI.Xaml.Automation.Peers
{
    public enum AccessibilityView { Raw, Content, Control }
}

namespace Microsoft.UI.Xaml.Automation
{
    public static class AutomationProperties
    {
        private static readonly Dictionary<object, string> Names = [];
        public static string GetName(object target) => Names.GetValueOrDefault(target, string.Empty);
        public static void SetName(object target, string name) => Names[target] = name;
        public static void SetAccessibilityView(object target, Peers.AccessibilityView view) { }
    }
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public enum FlyoutPlacementMode { LeftEdgeAlignedTop }
    public sealed class FlyoutBaseClosingEventArgs : EventArgs;
    public class FlyoutBase
    {
        public FlyoutPlacementMode Placement { get; set; }
        public bool ShouldConstrainToRootBounds { get; set; }
        public bool IsOpen { get; private set; }
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public bool DelayOpened { get; set; }
        public bool DelayClosed { get; set; }
        private bool _metadataClosing;
        private bool _opening;
        private bool _openingCanceled;
        public event Action<object?, object>? Opening;
        public event Action<object?, object>? Opened;
        public event Action<FlyoutBase, FlyoutBaseClosingEventArgs>? Closing;
        public event Action<object?, object>? Closed;
        public void ShowAt(Microsoft.UI.Xaml.FrameworkElement element)
        {
            ShowCount++;
            if (_metadataClosing) return;
            _opening = true;
            _openingCanceled = false;
            Opening?.Invoke(this, new());
            _opening = false;
            if (_openingCanceled) return;
            IsOpen = true;
            if (!DelayOpened) NotifyOpened();
        }
        public void NotifyOpened() => Opened?.Invoke(this, new());
        public void Hide()
        {
            if (!IsOpen && !_opening) return;
            HideCount++;
            Closing?.Invoke(this, new());
            if (_opening)
            {
                _openingCanceled = true;
                return;
            }
            IsOpen = false;
            _metadataClosing = DelayClosed;
            if (!DelayClosed) NotifyClosed();
        }
        public void NotifyClosed()
        {
            Closed?.Invoke(this, new());
            _metadataClosing = false;
        }
        public void LightDismiss()
        {
            Closing?.Invoke(this, new());
            IsOpen = false;
            _metadataClosing = DelayClosed;
            if (!DelayClosed) NotifyClosed();
        }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Control : Microsoft.UI.Xaml.FrameworkElement
    {
        public object? DefaultStyleKey { get; set; }
        public static readonly Microsoft.UI.Xaml.DependencyProperty IsEnabledProperty = Microsoft.UI.Xaml.DependencyProperty.Register("IsEnabled", typeof(bool), typeof(Control), new(true));
        public static readonly Microsoft.UI.Xaml.DependencyProperty ForegroundProperty = Microsoft.UI.Xaml.DependencyProperty.Register("Foreground", typeof(object), typeof(Control), new(null));
        public static readonly Microsoft.UI.Xaml.DependencyProperty FontSizeProperty = Microsoft.UI.Xaml.DependencyProperty.Register("FontSize", typeof(double), typeof(Control), new(12d));
        public bool IsEnabled { get => (bool)GetValue(IsEnabledProperty)!; set => SetValue(IsEnabledProperty, value); }
        public object? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        public double FontSize { get => (double)GetValue(FontSizeProperty)!; set => SetValue(FontSizeProperty, value); }
    }
    public class DropDownButton : Control
    {
        public object? Content { get; set; }
        public Primitives.FlyoutBase? Flyout { get; set; }
        public Microsoft.UI.Xaml.HorizontalAlignment HorizontalContentAlignment { get; set; }
        public event Microsoft.UI.Xaml.RoutedEventHandler? Click;
        public void Invoke()
        {
            Click?.Invoke(this, new());
            Flyout?.ShowAt(this);
        }
    }
    public sealed class Grid : Microsoft.UI.Xaml.FrameworkElement;
    public sealed class Border : Microsoft.UI.Xaml.FrameworkElement
    {
        private Microsoft.UI.Xaml.FrameworkElement? _child;
        public object? Background { get; set; }
        public Microsoft.UI.Xaml.FrameworkElement? Child
        {
            get => _child;
            set { _child = value; if (value is not null) value.Parent = this; }
        }
    }
    public sealed class TextBlock : Microsoft.UI.Xaml.FrameworkElement
    {
        public static readonly Microsoft.UI.Xaml.DependencyProperty ForegroundProperty = Control.ForegroundProperty;
        public static readonly Microsoft.UI.Xaml.DependencyProperty FontSizeProperty = Control.FontSizeProperty;
        public string Text { get; set; } = string.Empty;
    }
    public enum ListViewSelectionMode { Single }
    public sealed class GroupStyle
    {
        public bool HidesIfEmpty { get; set; }
        public Microsoft.UI.Xaml.DataTemplate? HeaderTemplate { get; set; }
    }
    public class ListViewItem : Control { public object? Content { get; set; } }
    public sealed class SelectionChangedEventArgs(IList<object> added) : EventArgs
    {
        public IList<object> AddedItems { get; } = added;
    }
    public sealed class ItemClickEventArgs(object item) : EventArgs { public object ClickedItem { get; } = item; }
    public class ListView : Control
    {
        private object? _source;
        private object? _selected;
        private readonly Dictionary<object, ListViewItem> _containers = new(ReferenceEqualityComparer.Instance);
        public ListViewSelectionMode SelectionMode { get; set; }
        public bool SingleSelectionFollowsFocus { get; set; } = true;
        public bool IsItemClickEnabled { get; set; }
        public Microsoft.UI.Xaml.ItemsPanelTemplate? ItemsPanel { get; set; }
        public Microsoft.UI.Xaml.DataTemplate? ItemTemplate { get; set; }
        public List<GroupStyle> GroupStyle { get; } = [];
        public List<object> Items { get; private set; } = [];
        public event Action<object, SelectionChangedEventArgs>? SelectionChanged;
        public event Action<object, ItemClickEventArgs>? ItemClick;
        public object? ItemsSource
        {
            get => _source;
            set
            {
                _source = value;
                _containers.Clear();
                Items = value is IEnumerable values ? values.Cast<object>().ToList() : [];
                foreach (var item in Items)
                {
                    var container = new ListViewItem { Content = item, Parent = this, XamlRoot = XamlRoot };
                    PrepareContainerForItemOverride(container, item);
                    _containers[item] = container;
                }
                SelectedItem = null;
            }
        }
        public object? SelectedItem
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value)) return;
                _selected = value;
                SelectionChanged?.Invoke(this, new(value is null ? [] : [value]));
            }
        }
        public object? ContainerFromItem(object item) => _containers.GetValueOrDefault(item);
        public object? ItemFromContainer(Microsoft.UI.Xaml.DependencyObject element) => (element as ListViewItem)?.Content;
        protected virtual void PrepareContainerForItemOverride(Microsoft.UI.Xaml.DependencyObject element, object item) { }
        public void Click(object item, bool selectFirst = true)
        {
            if (selectFirst) SelectedItem = item;
            ItemClick?.Invoke(this, new(item));
            if (!selectFirst) SelectedItem = item;
        }
        public void RaiseLateClick(object item) => ItemClick?.Invoke(this, new(item));
        public void RaiseLateSelection(object item) => SelectionChanged?.Invoke(this, new([item]));
        public void MoveFocus(object item)
        {
            ((ListViewItem)ContainerFromItem(item)!).Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            if (SingleSelectionFollowsFocus) SelectedItem = item;
        }
        public void AutomationSelect(object item) => SelectedItem = item;
    }
    public sealed class Flyout : Primitives.FlyoutBase
    {
        public object? Content { get; set; }
        public Microsoft.UI.Xaml.Style? FlyoutPresenterStyle { get; set; }
    }
    public static class ToolTipService
    {
        private static readonly Dictionary<object, object?> Values = [];
        public static void SetToolTip(object target, object? value) => Values[target] = value;
        public static object? GetToolTip(object target) => Values.GetValueOrDefault(target);
    }
}

namespace Microsoft.UI.Xaml.Markup
{
    public static class XamlReader
    {
        public static object Load(string markup)
        {
            var root = XElement.Parse(markup);
            if (root.Name.LocalName == "DataTemplate") return new Microsoft.UI.Xaml.DataTemplate(markup);
            if (root.Name.LocalName == "ItemsPanelTemplate") return new Microsoft.UI.Xaml.ItemsPanelTemplate(markup);
            if (root.Name.LocalName == "Style") return new Microsoft.UI.Xaml.Style(markup);
            return CreateElement(root);
        }
        private static Microsoft.UI.Xaml.FrameworkElement CreateElement(XElement node)
        {
            Microsoft.UI.Xaml.FrameworkElement result = node.Name.LocalName switch
            {
                "Grid" => new Microsoft.UI.Xaml.Controls.Grid(),
                "TextBlock" => new Microsoft.UI.Xaml.Controls.TextBlock(),
                _ => new Microsoft.UI.Xaml.Controls.Border()
            };
            result.Name = node.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value ?? string.Empty;
            foreach (var descendant in node.Descendants().Where(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name")))
            {
                var child = CreateElement(descendant);
                child.Parent = result;
                result.RegisterName(child.Name, child);
            }
            return result;
        }
    }
}

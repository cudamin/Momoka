// UI/dispatcher/template endpoints only. Every picker lifecycle decision executes EgPicker.cs.
namespace Windows.Foundation
{
    public readonly record struct Point(double X, double Y);
    public readonly record struct Size(double Width, double Height);
}

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        private readonly Queue<Action> _pending = [];
        public bool Accept { get; set; } = true;
        public int Pending => _pending.Count;
        public bool TryEnqueue(Action action)
        {
            if (!Accept) return false;
            _pending.Enqueue(action);
            return true;
        }
        public void DrainOne() { if (_pending.TryDequeue(out var action)) action(); }
        public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }
}

namespace Microsoft.UI.Xaml
{
    public sealed record DependencyProperty(string Name, object? DefaultValue)
    {
        public static DependencyProperty Register(string name, Type type, Type owner, PropertyMetadata metadata) => new(name, metadata.DefaultValue);
    }
    public sealed class PropertyMetadata(object? defaultValue, PropertyChangedCallback? changed = null)
    {
        public object? DefaultValue { get; } = defaultValue;
        public PropertyChangedCallback? Changed { get; } = changed;
    }
    public delegate void PropertyChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs args);
    public sealed class DependencyPropertyChangedEventArgs(object? oldValue, object? newValue)
    {
        public object? OldValue { get; } = oldValue;
        public object? NewValue { get; } = newValue;
    }
    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object?> _values = [];
        public int WidthWrites { get; private set; }
        public int MaxWidthWrites { get; private set; }
        public object GetValue(DependencyProperty property) =>
            _values.TryGetValue(property, out var value) && value is not null ? value : property.DefaultValue!;
        public void SetValue(DependencyProperty property, object? value)
        {
            _values[property] = value;
            Count(property);
        }
        public void ClearValue(DependencyProperty property)
        {
            _values.Remove(property);
            Count(property);
        }
        private void Count(DependencyProperty property)
        {
            if (property.Name == "Width") WidthWrites++;
            if (property.Name == "MaxWidth") MaxWidthWrites++;
        }
    }
    public sealed class RoutedEventArgs : EventArgs;
    public sealed class SelectionChangedEventArgs : EventArgs;
    public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs args);
    public sealed class SizeChangedEventArgs(Windows.Foundation.Size size) : EventArgs
    {
        public Windows.Foundation.Size NewSize { get; } = size;
    }
    public delegate void RoutedEventHandler(object sender, RoutedEventArgs args);
    public delegate void SizeChangedEventHandler(object sender, SizeChangedEventArgs args);
    public sealed class GeneralTransform(double x)
    {
        public Windows.Foundation.Point TransformPoint(Windows.Foundation.Point value) => new(value.X + x, value.Y);
    }
    public class FrameworkElement : DependencyObject
    {
        public static readonly DependencyProperty WidthProperty = new("Width", double.NaN);
        public static readonly DependencyProperty MaxWidthProperty = new("MaxWidth", double.PositiveInfinity);
        private SizeChangedEventHandler? _sizeChanged;
        public double Width { get => (double)GetValue(WidthProperty); set => SetValue(WidthProperty, value); }
        public double MaxWidth { get => (double)GetValue(MaxWidthProperty); set => SetValue(MaxWidthProperty, value); }
        public double ActualWidth { get; private set; }
        public bool IsLoaded { get; private set; }
        public object? Parent { get; set; }
        public double ParentOffsetX { get; set; }
        public Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
        public int SizeSubscribers => _sizeChanged?.GetInvocationList().Length ?? 0;
        public event RoutedEventHandler? Loaded;
        public event RoutedEventHandler? Unloaded;
        public event SizeChangedEventHandler SizeChanged
        {
            add => _sizeChanged += value;
            remove => _sizeChanged -= value;
        }
        public void Arrange(double width)
        {
            ActualWidth = width;
            _sizeChanged?.Invoke(this, new(new(width, 40)));
        }
        public void Load() { IsLoaded = true; Loaded?.Invoke(this, new()); }
        public void Unload() { IsLoaded = false; Unloaded?.Invoke(this, new()); }
        public void DelayedUnload() => Unloaded?.Invoke(this, new());
        public GeneralTransform TransformToVisual(FrameworkElement parent) => new(ParentOffsetX);
    }
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public sealed class Popup : Microsoft.UI.Xaml.FrameworkElement
    {
        public bool IsOpen { get; set; }
        public bool ShouldConstrainToRootBounds { get; set; }
        public object? Child { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public sealed class ComboBoxItem : Microsoft.UI.Xaml.FrameworkElement
    {
        public bool IsEnabled { get; set; } = true;
    }
    public sealed class ContentPresenter : Microsoft.UI.Xaml.FrameworkElement
    {
        public static readonly Microsoft.UI.Xaml.DependencyProperty ContentProperty = new("Content", null);
        public static readonly Microsoft.UI.Xaml.DependencyProperty ContentTemplateProperty = new("ContentTemplate", null);
        public static readonly Microsoft.UI.Xaml.DependencyProperty ContentTemplateSelectorProperty = new("ContentTemplateSelector", null);
        public object? Content
        {
            get => GetValue(ContentProperty);
            set => SetValue(ContentProperty, value!);
        }
        public object? ContentTemplate
        {
            get => GetValue(ContentTemplateProperty);
            set => SetValue(ContentTemplateProperty, value!);
        }
        public object? ContentTemplateSelector
        {
            get => GetValue(ContentTemplateSelectorProperty);
            set => SetValue(ContentTemplateSelectorProperty, value!);
        }
    }
    public class ComboBox : Microsoft.UI.Xaml.FrameworkElement
    {
        private bool _open;
        private Primitives.Popup? _templatePopup;
        public event Microsoft.UI.Xaml.RoutedEventHandler? DropDownOpened;
        public event Microsoft.UI.Xaml.RoutedEventHandler? DropDownClosed;
        public event Microsoft.UI.Xaml.SelectionChangedEventHandler? SelectionChanged;
        public object? SelectedItem { get; set; }
        public string? DisplayMemberPath { get; set; }
        public bool IsDropDownOpen
        {
            get => _open;
            set
            {
                if (_open == value) return;
                _open = value;
                if (_templatePopup is not null) _templatePopup.IsOpen = value;
                if (value) DropDownOpened?.Invoke(this, new());
                else DropDownClosed?.Invoke(this, new());
            }
        }
        public void ReapplyTemplate(Primitives.Popup? popup)
        {
            _templatePopup = popup;
            OnApplyTemplate();
        }
        public void NotifyOpened() => DropDownOpened?.Invoke(this, new());
        public void NotifySelectionChanged() => SelectionChanged?.Invoke(this, new());
        public void Prepare(Microsoft.UI.Xaml.DependencyObject container, object item) => PrepareContainerForItemOverride(container, item);
        protected virtual void OnApplyTemplate()
        {
            // Current framework source closes the picker while replacing its template.
            IsDropDownOpen = false;
            if (_templatePopup is not null) _templatePopup.ShouldConstrainToRootBounds = false;
        }
        protected object? GetTemplateChild(string name) => name == "Popup" ? _templatePopup : null;
        protected virtual void PrepareContainerForItemOverride(Microsoft.UI.Xaml.DependencyObject element, object item) { }
    }
}

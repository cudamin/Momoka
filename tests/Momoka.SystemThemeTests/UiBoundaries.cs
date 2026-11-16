// Narrow platform substitutes; ThemeHost and SystemThemeWatcher execute their full production source.
namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B)
    {
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    }
}

namespace Microsoft.UI.Xaml
{
    public enum ElementTheme { Default, Light, Dark }
    public sealed class DependencyProperty
    {
        internal PropertyMetadata Metadata { get; init; } = null!;
        public static DependencyProperty RegisterAttached(string name, Type type, Type owner, PropertyMetadata metadata) =>
            new() { Metadata = metadata };
    }
    public sealed class PropertyMetadata(object value, Action<DependencyObject, DependencyPropertyChangedEventArgs> changed)
    {
        internal object Value { get; } = value;
        internal Action<DependencyObject, DependencyPropertyChangedEventArgs> Changed { get; } = changed;
    }
    public sealed class DependencyPropertyChangedEventArgs(object value) { public object NewValue { get; } = value; }
    public class DependencyObject
    {
        private readonly Dictionary<DependencyProperty, object> _values = [];
        public object GetValue(DependencyProperty property) => _values.GetValueOrDefault(property, property.Metadata.Value);
        public void SetValue(DependencyProperty property, object value)
        {
            _values[property] = value;
            property.Metadata.Changed(this, new DependencyPropertyChangedEventArgs(value));
        }
    }
    public class FrameworkElement : DependencyObject
    {
        public ElementTheme RequestedTheme { get; set; }
        public ResourceDictionary Resources { get; } = new();
    }
    public sealed class ResourceDictionary : Dictionary<string, object>
    {
        public Uri? Source { get; set; }
        public Dictionary<string, object> ThemeDictionaries { get; } = [];
        public List<ResourceDictionary> MergedDictionaries { get; } = [];
    }
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public ResourceDictionary Resources { get; } = new();
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public sealed class SolidColorBrush(Windows.UI.Color color) { public Windows.UI.Color Color { get; set; } = color; }
    public sealed class AcrylicBrush
    {
        public Windows.UI.Color TintColor { get; set; }
        public Windows.UI.Color FallbackColor { get; set; }
        public bool AlwaysUseFallback { get; set; }
    }
}

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public static DispatcherQueue Current { get; } = new();
        public static DispatcherQueue GetForCurrentThread() => Current;
        private readonly Queue<Action> _pending = [];
        public bool Reject { get; set; }
        public bool Throw { get; set; }
        public int Pending => _pending.Count;
        public bool TryEnqueue(Action action)
        {
            if (Throw) throw new InvalidOperationException("queue fixture failure");
            if (Reject) return false;
            _pending.Enqueue(action);
            return true;
        }
        public void Drain() { while (_pending.TryDequeue(out var action)) action(); }
    }
}

namespace Windows.UI.ViewManagement
{
    public enum UIColorType { Foreground, Accent }
    public sealed class UISettings
    {
        public static readonly List<UISettings> Instances = [];
        public static Windows.UI.Color Foreground { get; set; } = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        public Windows.UI.Color Accent { get; set; } = Windows.UI.Color.FromArgb(255, 0x00, 0x78, 0xD4);
        public static bool FailRead { get; set; }
        public static bool FailSubscribe { get; set; }
        public static int Reads { get; set; }
        private event Action<UISettings, object>? _changed;
        public UISettings() => Instances.Add(this);
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public event Action<UISettings, object> ColorValuesChanged
        {
            add
            {
                if (FailSubscribe) throw new InvalidOperationException("subscribe fixture failure");
                _changed += value;
            }
            remove => _changed -= value;
        }
        public Windows.UI.Color GetColorValue(UIColorType type)
        {
            Reads++;
            if (FailRead) throw new InvalidOperationException("read fixture failure");
            return type == UIColorType.Accent ? Accent : Foreground;
        }
        public static void Notify() { foreach (var instance in Instances.ToArray()) instance._changed?.Invoke(instance, new object()); }
    }
}

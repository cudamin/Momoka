// Only WinUI object boundaries are replaced; the brush ownership and notification logic is source-linked.
namespace Microsoft.UI.Xaml
{
    public sealed class ResourceDictionary : Dictionary<object, object>
    {
        private readonly Dictionary<object, Func<object>> _deferred = [];

        public List<ResourceDictionary> MergedDictionaries { get; } = [];
        public Dictionary<object, object> ThemeDictionaries { get; } = [];

        public bool RejectEnumeration { get; set; }

        public void Defer(object key, Func<object> resolve) => _deferred.Add(key, resolve);

        public new object this[object key]
        {
            get
            {
                Materialize();
                return base[key];
            }
            set
            {
                // WinUI Insert removes by object identity after undeferring the requested alias.
                // If two keys resolve to the same brush, that removal can remove the first key.
                if (_deferred.ContainsKey(key)) Materialize();
                if (TryGetValue(key, out var previous))
                {
                    var first = this.First(entry => ReferenceEquals(entry.Value, previous)).Key;
                    Remove(first);
                    if (ContainsKey(key))
                        throw new System.Runtime.InteropServices.COMException("E_DO_RESOURCE_KEYCONFLICT", unchecked((int)0x800F0902));
                }
                Add(key, value);
            }
        }

        public new Dictionary<object, object>.Enumerator GetEnumerator()
        {
            if (RejectEnumeration) throw new InvalidOperationException("不能枚举或重建控件资源");
            Materialize();
            return base.GetEnumerator();
        }

        private void Materialize()
        {
            foreach (var (key, resolve) in _deferred.ToArray())
            {
                _deferred.Remove(key);
                base[key] = resolve();
            }
        }
    }

    public sealed class Application
    {
        public static Application Current { get; } = new();
        public ResourceDictionary Resources { get; } = new();
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public class Brush
    {
        public double Opacity { get; set; } = 1;
    }

    public sealed class SolidColorBrush : Brush
    {
        public Windows.UI.Color Color { get; set; }
    }

    public sealed class AcrylicBrush : Brush
    {
        public Windows.UI.Color TintColor { get; set; }
        public Windows.UI.Color FallbackColor { get; set; }
        public double TintOpacity { get; set; }
        public double? TintLuminosityOpacity { get; set; }
        public bool AlwaysUseFallback { get; set; }
        public TimeSpan TintTransitionDuration { get; set; }
    }
}

namespace Windows.UI
{
    public record struct Color(byte A, byte R, byte G, byte B)
    {
        public static implicit operator Color(uint value) =>
            new((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public class Control
    {
        public Microsoft.UI.Xaml.ResourceDictionary Resources { get; set; } = new();
        public Microsoft.UI.Xaml.Media.Brush? Background { get; set; }
        public double Opacity { get; set; } = 1;
    }

    public sealed class ComboBox : Control;
    public sealed class Button : Control;
}

namespace Microsoft.UI.Xaml.Controls.Primitives
{
    public sealed class ToggleButton : Microsoft.UI.Xaml.Controls.Control;
}

namespace Momoka.Shell.Views
{
    public static class HomeMotion
    {
        public static bool Enabled { get; private set; }
        public static void ApplyPreference(bool enabled) => Enabled = enabled;
    }
}

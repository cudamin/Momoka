namespace Microsoft.UI.Xaml
{
    public sealed class ResourceDictionary : Dictionary<object, object>
    {
        public Uri? Source { get; set; }
        public List<ResourceDictionary> MergedDictionaries { get; } = [];
        public Dictionary<object, object> ThemeDictionaries { get; } = [];
    }
    public sealed class Application
    {
        public static Application Current { get; } = new();
        public ResourceDictionary Resources { get; } = new();
    }
    public enum ElementTheme { Default, Light, Dark }
    public sealed class FrameworkElement
    {
        public ElementTheme RequestedTheme { get; set; }
    }
}
namespace Microsoft.UI.Xaml.Media
{
    public abstract class Brush { public double Opacity { get; set; } = 1; }
    public sealed class SolidColorBrush : Brush { public Windows.UI.Color Color { get; set; } }
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
        public static Color FromArgb(byte alpha, byte red, byte green, byte blue) => new(alpha, red, green, blue);
    }
}
namespace Momoka.Shell.Views
{
    public static class HomeMotion { public static void ApplyPreference(bool enabled) { } }
}

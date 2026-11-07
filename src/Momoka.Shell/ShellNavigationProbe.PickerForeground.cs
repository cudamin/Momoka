using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.UI;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // Use the page's production foreground rule with the actual painted fill. The probe does not
    // assume white text: a light image or a fully opaque light surface legitimately needs dark ink.
    internal static bool PickerForegroundMatches(Brush? observed, ThemeColor background, AcrylicBrush fill,
        bool materialFallback, Color? forced = null)
    {
        var reference = new ComboBox();
        var backgrounds = ControlBackgroundBrushes.ForDetail(reference, lightSurface: true);
        foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
        {
            var sample = (AcrylicBrush)reference.Resources["ComboBoxBackground" + suffix];
            sample.FallbackColor = fill.FallbackColor;
            sample.Opacity = fill.Opacity;
            sample.TintOpacity = fill.TintOpacity;
            sample.AlwaysUseFallback = fill.AlwaysUseFallback;
        }
        backgrounds.ApplyForeground(background, materialFallback, forced);
        return observed is SolidColorBrush actual && reference.Foreground is SolidColorBrush expected
            && actual.Color == expected.Color;
    }

    // Raw diagnostics only. Missing or unexpected brushes remain explicit rather than being
    // converted into a passing color, and no control/resource is mutated while formatting.
    internal static string DescribePickerForeground(string state, Brush? picker, Brush? glyph, Brush? fallbackSource,
        Color?[] keys, bool[] references, ThemeColor background, AcrylicBrush fill)
    {
        Color? Key(int index) => index < keys.Length ? keys[index] : null;
        return $"state={state}, picker={Ink(picker)}, glyph={Ink(glyph)}, fallbackSource={Ink(fallbackSource)}, "
            + $"normal={ColorText(Key(0))}, focused={ColorText(Key(1))}, focusedPressed={ColorText(Key(2))}, "
            + $"glyphReferences={string.Join('/', references)}, background={background.ToHex()}, "
            + $"fillFallback={ColorText(fill.FallbackColor)}, opacity={fill.Opacity:0.###}, tint={fill.TintOpacity:0.###}, fallback={fill.AlwaysUseFallback}";

        static string Ink(Brush? brush) => brush is SolidColorBrush solid ? ColorText(solid.Color)
            : brush?.GetType().Name ?? "null";
        static string ColorText(Color? value) => value is { } color
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}" : "null";
    }
}

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
}

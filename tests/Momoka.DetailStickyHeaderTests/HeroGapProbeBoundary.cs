using Microsoft.UI.Xaml;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    internal static void ValidateHeroGap(DetailPage page) =>
        RequireHeroGaps(page, page.HeroStack, page.ColumnActions);

    private static T Get<T>(DetailPage page, string name) where T : class =>
        (T)(object)(name switch
        {
            "HeroStack" => page.HeroStack,
            "ColumnActions" => page.ColumnActions,
            _ => throw new InvalidOperationException("Unknown hero-gap boundary element: " + name)
        });

    private static Rect Bounds(FrameworkElement element, FrameworkElement root) =>
        element.TransformToVisual(root).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

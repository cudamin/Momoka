using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    // General shell fixtures deliberately hide the home banner. Input-theme checks need its
    // actual dependent controls, so establish their enabled owner before constructing the page.
    internal static void PrepareSettingsInputFixture(UiSettings settings)
    {
        settings.ShowHomeBanner = true;
        settings.CarouselMedia = CarouselMediaType.Series;
        settings.CarouselCount = 7;
    }

    internal static bool SettingsRecoveryTargetsVisible(double viewportWidth, double viewportHeight,
        IReadOnlyList<(double X, double Y, double Width, double Height, bool Visible)> targets) =>
        viewportWidth > 0 && viewportHeight > 0 && targets.Count > 0 && targets.All(target =>
            target.Visible && target.Width > 0 && target.Height > 0 && target.X >= -1 && target.Y >= -1
            && target.X + target.Width <= viewportWidth + 1 && target.Y + target.Height <= viewportHeight + 1);
}

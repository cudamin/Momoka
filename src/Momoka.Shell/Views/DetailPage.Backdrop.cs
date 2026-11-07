using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Windows.UI;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    // 阅读保护固定在图片层，滚动内容和标题栏不再重复叠暗。沉浸背景也必须压住任意位置的亮部。
    private bool UsesBackdropFade => ViewModel.BackdropShown
        && (ViewModel.Showcase || ViewModel.ItemType is EmbyItemType.Movie or EmbyItemType.Series or EmbyItemType.Season);

    private Color ReadingShadeColor => Color.FromArgb(
        AppearanceOpacity.ReadingShadeAlpha(ViewModel.BackdropStrength, ViewModel.GaussianBackdropApplied),
        ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings).R,
        ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings).G,
        ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings).B);

    private void PaintBackdropFade()
    {
        // smoothstep 两端斜率归零；同一主题RGB只改变alpha，避开透明黑造成的脏边。
        var color = _actionAccessibility.HighContrast
            ? _actionUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : ThemeHost.ToColor(ThemeHost.Current.Colors.Window);
        BackdropEdgeBrush.GradientStops.Clear();
        BackdropEdgeBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(0, color.R, color.G, color.B) });
        for (var step = 0; step <= 32; step++)
        {
            var t = step / 32d;
            var alpha = (byte)Math.Round(255 * t * t * (3 - 2 * t));
            BackdropEdgeBrush.GradientStops.Add(new GradientStop
            {
                Offset = 0.8 + 0.2 * t,
                Color = Color.FromArgb(alpha, color.R, color.G, color.B)
            });
        }
        BackdropEdgeFade.Visibility = ViewModel.Showcase ? Visibility.Collapsed : Visibility.Visible;
        BackdropFade.Background = _actionAccessibility.HighContrast ? new SolidColorBrush(color) : ViewModel.UseAcrylicBackdrop
            ? new AcrylicBrush
            {
                TintColor = ThemeHost.ToColor(ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings)),
                FallbackColor = ThemeHost.ToColor(ShellPrefs.AppearanceColor(true, ViewModel.AppearanceSettings)),
                TintOpacity = ViewModel.BackdropStrength,
                TintLuminosityOpacity = ViewModel.BackdropStrength,
                Opacity = ViewModel.BackdropStrength == 0 ? 0 : 1,
                AlwaysUseFallback = ViewModel.BackdropStrength == 1,
                TintTransitionDuration = TimeSpan.Zero
            }
            : new SolidColorBrush(ReadingShadeColor);
        BackdropFade.Visibility = UsesBackdropFade ? Visibility.Visible : Visibility.Collapsed;
    }
}

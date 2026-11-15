using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Momoka.Shell.Media;
using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>商店模式的阅读渐变；复用主题窗口色，不读取沉浸模式的亚克力参数。</summary>
internal static class StoreBackdropStyle
{
    internal sealed class Tint
    {
        private HeroPictureLoader.HeroPixels? _pixels;
        private ThemeColor _color;
        private ThemeColor _fallback;
        internal void Clear() => _pixels = null;
        internal Color Resolve(HeroPictureLoader.HeroPixels? pixels, bool hasImage, ThemeColor fallback, bool dark)
        {
            if (!hasImage || pixels is null) { _pixels = null; return ThemeHost.ToColor(fallback); }
            // 窄窗的浅色亚克力用主题纸面，不能让旧的暗色取样盖在浅色正文下面。
            if (!dark) { _pixels = null; return ThemeHost.ToColor(fallback); }
            if (!ReferenceEquals(_pixels, pixels) || _fallback != fallback)
            {
                _pixels = pixels;
                _fallback = fallback;
                _color = ArtworkShade.Sample(pixels.Pixels, pixels.Width, pixels.Height, fallback);
            }
            return ThemeHost.ToColor(_color);
        }
    }

    // Gallery Samples/Acrylic/AcrylicPage.xaml/.xaml.cs,
    // v2.9.3 / 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7：原生页面内亚克力，平台负责回退。
    internal static void ApplyAcrylic(Border surface, Border bottom, Color color, Color pageColor)
    {
        bottom.Background = Gradient(pageColor, new Point(0.5, 0), new Point(0.5, 1),
            [(0, 0), (0.35, 0.2), (0.7, 0.7), (1, 1)]);
        if (surface.Background is AcrylicBrush existing && existing.TintColor == color) return;
        surface.Background = new AcrylicBrush
        {
            TintColor = color,
            FallbackColor = color,
            TintOpacity = 0.5,
            TintLuminosityOpacity = 0.65,
            TintTransitionDuration = TimeSpan.Zero
        };
    }

    private static LinearGradientBrush Gradient(Color color, Point start, Point end,
        (double Offset, double Opacity)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (offset, opacity) in stops)
            brush.GradientStops.Add(new GradientStop
            {
                Offset = offset,
                Color = Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B)
            });
        return brush;
    }
}

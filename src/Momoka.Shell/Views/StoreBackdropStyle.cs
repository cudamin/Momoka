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
        internal Color Resolve(HeroPictureLoader.HeroPixels? pixels, bool hasImage, ThemeColor fallback)
        {
            if (!hasImage || pixels is null) { _pixels = null; return ThemeHost.ToColor(fallback); }
            if (!ReferenceEquals(_pixels, pixels))
            {
                _pixels = pixels;
                _color = ArtworkShade.Sample(pixels.Pixels, pixels.Width, pixels.Height, fallback);
            }
            return ThemeHost.ToColor(_color);
        }
    }

    internal static void Apply(Border sides, Border bottom, Color color)
    {
        if (sides.Background is LinearGradientBrush { GradientStops.Count: 5 } existing
            && bottom.Background is LinearGradientBrush { GradientStops.Count: 5 }
            && existing.GradientStops[0].Color is var first
            && first.R == color.R && first.G == color.G && first.B == color.B)
            return;
        sides.Background = Gradient(color, new Point(0, 0.5), new Point(1, 0.5),
            [(0, 0.8), (0.16, 0.3), (0.45, 0.08), (0.85, 0.08), (1, 0.7)]);
        bottom.Background = Gradient(color, new Point(0.5, 0), new Point(0.5, 1),
            [(0, 0.08), (0.3, 0.08), (0.55, 0.2), (0.78, 0.6), (1, 1)]);
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

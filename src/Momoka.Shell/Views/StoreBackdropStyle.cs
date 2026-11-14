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

    /// <summary>
    /// 剧照后面那块环境底：2026-11-14 用户令「渐变参考图二改」之后，它不再往剧照上压一层染色，
    /// 而是一块纯色铺在剧照下面，由 <see cref="StoreBackdropFade"/> 的 Ambient 遮罩把它的左端、右端与底部
    /// 收到透明。剧照自己在同一块底上渐隐，屏幕上是「画面融进一块跟着剧照换色的暗底」，
    /// 与微软商店条目页的顶图同一种做法（微软商店闭源，截图只作视觉参考）。
    /// 2026-11-15 用户又令「下方和左右要加渐变」「最右边要留一点空隙」：右端一并收边，画面不再贴着滚动条。
    /// </summary>
    internal static void Apply(Border ambient, Color color)
    {
        if (ambient.Background is SolidColorBrush existing && existing.Color == color) return;
        ambient.Background = new SolidColorBrush(color);
    }

    internal static void ApplyVideo(Border sides, Border bottom, Color color)
    {
        // 外部视频表面上实际绘制XAML渐变，端点与页面窗口色完全一致，文字区域保持可读。
        sides.Background = Gradient(color, new Point(0, 0.5), new Point(1, 0.5),
            [(0, 1), (0.18, 0.68), (0.38, 0.2), (0.65, 0.08), (0.9, 0.3), (1, 1)]);
        bottom.Background = Gradient(color, new Point(0.5, 0), new Point(0.5, 1),
            [(0, 0.08), (0.35, 0.08), (0.6, 0.22), (0.82, 0.72), (1, 1)]);
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

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

    /// <summary>
    /// 商店艺术图那一档的纯色底（2026-11-17 用户令「要纯色，铺满左上角和右上角」）：从封面取<em>一个</em>
    /// 颜色、纵向淡到透明，左右不再收边，两个上角都是一整片同色。
    /// <para>
    /// 颜色仍走 <see cref="ArtworkShade.Sample"/>（深色主题压到阅读上限）；浅色主题再把它混进窗口色，
    /// 深墨字压上去仍然读得清。像素、窗口色或深浅翻了才重搭画笔，拖窗口不重建。
    /// </para>
    /// </summary>
    internal sealed class Wash
    {
        private static readonly SolidColorBrush Empty = new(Microsoft.UI.Colors.Transparent);
        private HeroPictureLoader.HeroPixels? _pixels;
        private ThemeColor _window;
        private bool _dark;
        private Brush? _brush;

        internal void Clear()
        {
            _pixels = null;
            _brush = null;
        }

        internal Brush Resolve(HeroPictureLoader.HeroPixels? pixels, UiTheme theme)
        {
            if (pixels is null)
            {
                Clear();
                return Empty;
            }
            if (ReferenceEquals(_pixels, pixels) && _window == theme.Colors.Window
                && _dark == theme.IsDark && _brush is not null) return _brush;

            var color = ArtworkShade.Sample(pixels.Pixels, pixels.Width, pixels.Height, theme.Colors.Window);
            if (!theme.IsDark) color = theme.Colors.Window.Mix(color, 0.35);
            var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            brush.GradientStops.Add(new GradientStop { Offset = 0, Color = ThemeHost.ToColor(color) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.6, Color = ThemeHost.ToColor(color.WithAlpha(90)) });
            brush.GradientStops.Add(new GradientStop { Offset = 1, Color = ThemeHost.ToColor(color.WithAlpha(0)) });
            _pixels = pixels;
            _window = theme.Colors.Window;
            _dark = theme.IsDark;
            return _brush = brush;
        }
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

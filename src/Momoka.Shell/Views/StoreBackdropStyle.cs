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
            // 2026-11-17 用户令「过度要平滑啊，上面从浓到淡」：从顶部的实色一路匀到透明，中间用几档
            // 递减的 alpha 把曲线拉平，不再是一大段实色之后突然收掉。
            brush.GradientStops.Add(new GradientStop { Offset = 0, Color = ThemeHost.ToColor(color) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.25, Color = ThemeHost.ToColor(color.WithAlpha(215)) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.5, Color = ThemeHost.ToColor(color.WithAlpha(155)) });
            brush.GradientStops.Add(new GradientStop { Offset = 0.75, Color = ThemeHost.ToColor(color.WithAlpha(75)) });
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

    /// <summary>
    /// 淡墨画刷：色标换算成固定 #0C0E11 的透明度档，横向（左 → 右）或竖向（上 → 下）。方向必须跟色标
    /// 那一道一致 —— 横向那组色标搭成竖向起止点，画出来就是另一道渐变。
    /// </summary>
    private static LinearGradientBrush ShadeBrush((double Position, double Alpha)[] stops, bool vertical)
    {
        var ink = StoreBackdropBlend.ShadeInk;
        var brush = new LinearGradientBrush
        {
            StartPoint = vertical ? new Point(0.5, 0) : new Point(0, 0.5),
            EndPoint = vertical ? new Point(0.5, 1) : new Point(1, 0.5)
        };
        foreach (var (position, alpha) in stops)
            brush.GradientStops.Add(new GradientStop
            {
                Offset = position,
                Color = ThemeHost.ToColor(ink.WithAlpha((byte)Math.Round(255 * alpha)))
            });
        return brush;
    }

    /// <summary>
    /// 左上角淡墨（<c>StoreShade</c>）不随几何变的那两支画刷，色标只在 <see cref="StoreBackdropBlend"/> 一处：
    /// 浅色主题的横向那道与两档同一支的竖向那道。都是静态的 —— 颜色与曲线不随页面、像素或色板变，
    /// 两页共用；XAML 里那两支只是上树第一帧的初值，与这里浅色那份逐档同值（测试核对）。
    /// 深色主题的横向那道随剧照位置变，见 <see cref="ShadeDark"/>。
    /// </summary>
    internal static class Shade
    {
        private static Brush? _across, _down;

        /// <summary>浅色主题横向那道（2026-11-27 之前两档共用的那道淡墨，浅色照旧）。</summary>
        internal static Brush Across => _across ??= ShadeBrush(StoreBackdropBlend.ShadeAcross, vertical: false);

        /// <summary>竖向那道（压顶上，两档同一支）。</summary>
        internal static Brush Down => _down ??= ShadeBrush(StoreBackdropBlend.ShadeDown, vertical: true);
    }

    /// <summary>
    /// 深色主题横向那道（2026-11-27 用户令「把详情页背景图左侧改成深色的，浅色主题时候保持浅色」）：
    /// 色标按左列宽与剧照渐隐带宽换算成整页的比例停点（<see cref="StoreBackdropBlend.ShadeDarkStops"/>），
    /// 几何变了才重搭，拖窗口时同宽不重建；页宽没量到时不画。
    /// </summary>
    internal sealed class ShadeDark
    {
        private static readonly SolidColorBrush Empty = new(Microsoft.UI.Colors.Transparent);
        private (double Page, double Left, double Fade) _geometry;
        private Brush? _brush;

        internal void Clear() => _brush = null;

        internal Brush Resolve(double pageWidth, double leftColumn, double fadeWidth)
        {
            var geometry = (pageWidth, leftColumn, fadeWidth);
            if (_brush is not null && _geometry == geometry) return _brush;
            var stops = StoreBackdropBlend.ShadeDarkStops(pageWidth, leftColumn, fadeWidth);
            _geometry = geometry;
            if (stops.Length == 0) return _brush = Empty;
            return _brush = ShadeBrush(stops, vertical: false);
        }
    }
}

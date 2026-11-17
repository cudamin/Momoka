using Momoka.Theming;

namespace Momoka.Shell.Views;

/// <summary>静图透明度与外部视频叠图共用同一条曲线；视频不经过VisualSurface。</summary>
internal static class StoreBackdropBlend
{
    internal const int Side = 128;
    internal const int FoundationWidth = 512;
    internal const int FoundationHeight = 256;
    internal const double FoundationTail = 120;
    private static readonly (double Position, double Alpha)[] PictureAcross =
        [(0, 0), (0.18, 0.38), (0.42, 0.96), (0.7, 1), (1, 0)];
    // 2026-11-15 用户令「加高右边封面图下方的渐变」：下方渐隐的起点从 0.42 提到 0.30，过渡带更长更缓。
    private static readonly (double Position, double Alpha)[] PictureDown =
        [(0, 1), (0.3, 1), (0.62, 0.7), (1, 0)];
    // 2026-11-15 用户令「详情页/订阅详情页这个左边别留这个黑边」：环境底左端不再起坡，从内容区左沿
    // （侧栏右沿）就是满色，右端照旧收在画布上。左端上坡的那一版在侧栏旁边留下一条从窗口底色升到
    // 环境色的暗带，屏上读成一道黑边。
    private static readonly (double Position, double Alpha)[] AmbientAcross =
        [(0, 1), (0.72, 1), (1, 0)];
    /// <summary>
    /// 2026-11-17 用户令「左上角的那个渐变要以左上为起点扩散，从浓变淡，过度平滑」：环境底左上角那团
    /// 暗角以左上角为圆心径向展开 —— 角上最浓（窗口底色），沿椭圆弧向外平滑变淡，到画布宽的
    /// <see cref="AmbientFanX"/>、高的 <see cref="AmbientFanY"/> 处完全交给环境底。两轴按画布比例给，
    /// 窗口拉宽拉高时这团暗角等比铺开；左沿不再另加竖直的坡（用户令「左边还是有一条90度的黑色」），
    /// 右沿另有一道收边。2026-11-18 用户令「右边少扩散一点，下面多扩散」，随后又令「扩散的面积大一点，
    /// 然后弄淡 更透」：横向 0.22、纵向铺满整个高；角上也不再压到全黑，底子留 <see cref="AmbientFanFloor"/>。
    /// </summary>
    internal const double AmbientFanX = 0.22;
    internal const double AmbientFanY = 1.00;

    /// <summary>
    /// 角上那一点的透明度下限（0–1）：不给 0，左上角就不会是一块实心黑，环境色从第一像素起就透出来，
    /// 整团暗角读起来更淡更透（2026-11-18 用户令「弄淡 更透」）。
    /// </summary>
    internal const double AmbientFanFloor = 0.35;

    /// <summary>径向那团的坡：角上下限见 <see cref="AmbientFanFloor"/>，1 = 完全交给环境底（smoothstep 由 Interpolate 给）。</summary>
    private static readonly (double Position, double Alpha)[] AmbientFan =
        [(0, AmbientFanFloor), (1, 1)];

    /// <summary>对称环境底的右坡：照旧在画布右侧收边。</summary>
    private static readonly (double Position, double Alpha)[] AmbientRight =
        [(0, 1), (0.72, 1), (1, 0)];

    private static readonly (double Position, double Alpha)[] AmbientDown =
        [(0, 1), (0.42, 1), (0.7, 0.68), (1, 0)];

    internal static double Alpha(double x, double y, bool ambient) => ambient
        ? Interpolate(x, AmbientAcross) * Interpolate(y, AmbientDown)
        : Interpolate(x, PictureAcross) * Interpolate(y, PictureDown);

    internal static byte[] Mask(bool ambient, bool symmetric = false)
    {
        var across = ambient ? AmbientAcross : PictureAcross;
        var down = ambient ? AmbientDown : PictureDown;
        var pixels = new byte[Side * Side * 4];
        for (var y = 0; y < Side; y++)
        {
            var ny = y / (double)(Side - 1);
            var vertical = Interpolate(ny, down);
            for (var x = 0; x < Side; x++)
            {
                var nx = x / (double)(Side - 1);
                // 左上角那团暗角：以角为圆心的椭圆半径，越往外越淡（见 AmbientFanX/Y）。
                var fan = Math.Sqrt(nx * nx / (AmbientFanX * AmbientFanX)
                    + ny * ny / (AmbientFanY * AmbientFanY));
                // 2026-11-17 用户令「左边还是有一条90度的黑色」：左沿不再另加竖直的窄坡 ——
                // 暗角只由上面那团径向给，左边沿离开角就交给满色环境底，不会有一条竖着的黑带。
                var horizontal = symmetric
                    ? Interpolate(Math.Min(1, fan), AmbientFan) * Interpolate(nx, AmbientRight)
                    : Interpolate(nx, across);
                var alpha = Channel(255 * horizontal * vertical);
                var at = (y * Side + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = alpha;
            }
        }
        return pixels;
    }

    // 普通内容色层位于环境底与视频之下，不创建或替换系统Backdrop。
    // 视频范围内完全不透明，外侧收到透明，叠图端点因此与实际底色一致。
    internal static double FoundationAlpha(double x, double y, double pageWidth, double videoWidth,
        double videoHeight, double rightGap)
    {
        var left = Math.Max(0, pageWidth - rightGap - videoWidth);
        // 留两个采样像素的实色余量，双线性缩放不会在视频最外沿提前漏出Mica。
        var pad = Math.Max(2, pageWidth / FoundationWidth * 2);
        var across = Ramp(x, 0, Math.Max(1, left - pad))
            * (1 - Ramp(x, pageWidth - rightGap + pad, pageWidth));
        return across * (1 - Ramp(y, videoHeight + pad, videoHeight + FoundationTail));
    }

    internal static byte[] Foundation(ThemeColor window, double pageWidth, double videoWidth,
        double videoHeight, double rightGap)
    {
        var pixels = new byte[FoundationWidth * FoundationHeight * 4];
        if (!double.IsFinite(pageWidth) || pageWidth <= 0 || !double.IsFinite(videoWidth) || videoWidth <= 0
            || !double.IsFinite(videoHeight) || videoHeight <= 0 || !double.IsFinite(rightGap) || rightGap < 0) return pixels;
        for (var y = 0; y < FoundationHeight; y++)
            for (var x = 0; x < FoundationWidth; x++)
            {
                var alpha = FoundationAlpha(x * pageWidth / (FoundationWidth - 1),
                    y * (videoHeight + FoundationTail) / (FoundationHeight - 1),
                    pageWidth, videoWidth, videoHeight, rightGap);
                var at = (y * FoundationWidth + x) * 4;
                pixels[at] = Channel(window.B * alpha);
                pixels[at + 1] = Channel(window.G * alpha);
                pixels[at + 2] = Channel(window.R * alpha);
                pixels[at + 3] = Channel(255 * alpha);
            }
        return pixels;
    }

    /// <summary>叠图RGB预乘透明度；其端点等于视频背后的环境底与窗口合成色，避免实色矩形。</summary>
    internal static byte[] Video(byte[]? ambient, ThemeColor window, double pageWidth, double ambientHeight,
        double videoWidth, double videoHeight, double rightGap)
    {
        var pixels = new byte[Side * Side * 4];
        var valid = ambient?.Length == ArtworkAmbient.Width * ArtworkAmbient.Height * 4
            && double.IsFinite(pageWidth) && pageWidth > 0 && double.IsFinite(ambientHeight) && ambientHeight > 0;
        for (var y = 0; y < Side; y++)
            for (var x = 0; x < Side; x++)
            {
                var nx = x / (double)(Side - 1);
                var ny = y / (double)(Side - 1);
                var amount = 1 - Alpha(nx, ny, ambient: false);
                var color = window;
                if (valid)
                {
                    var ax = Math.Clamp((pageWidth - rightGap - videoWidth + nx * videoWidth) / pageWidth, 0, 1);
                    var ay = Math.Clamp(ny * videoHeight / ambientHeight, 0, 1);
                    var at = ((int)Math.Round(ay * (ArtworkAmbient.Height - 1)) * ArtworkAmbient.Width
                        + (int)Math.Round(ax * (ArtworkAmbient.Width - 1))) * 4;
                    color = window.Mix(ThemeColor.Rgb(ambient![at + 2], ambient[at + 1], ambient[at]), Alpha(ax, ay, ambient: true));
                }
                var offset = (y * Side + x) * 4;
                pixels[offset] = Channel(color.B * amount);
                pixels[offset + 1] = Channel(color.G * amount);
                pixels[offset + 2] = Channel(color.R * amount);
                pixels[offset + 3] = Channel(255 * amount);
            }
        return pixels;
    }

    private static double Interpolate(double position, (double Position, double Alpha)[] stops)
    {
        position = Math.Clamp(position, 0, 1);
        for (var i = 1; i < stops.Length; i++)
        {
            if (position > stops[i].Position) continue;
            var previous = stops[i - 1];
            var next = stops[i];
            var t = (position - previous.Position) / (next.Position - previous.Position);
            return previous.Alpha + (next.Alpha - previous.Alpha) * t * t * (3 - 2 * t);
        }
        return stops[^1].Alpha;
    }

    private static double Ramp(double value, double start, double end)
    {
        var t = Math.Clamp((value - start) / Math.Max(1, end - start), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}

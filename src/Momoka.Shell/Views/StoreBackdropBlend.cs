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
    private static readonly (double Position, double Alpha)[] AmbientAcross =
        [(0, 0), (0.025, 0.7), (0.1, 1), (0.72, 1), (1, 0)];
    private static readonly (double Position, double Alpha)[] AmbientDown =
        [(0, 1), (0.42, 1), (0.7, 0.68), (1, 0)];

    internal static double Alpha(double x, double y, bool ambient) => ambient
        ? Interpolate(x, AmbientAcross) * Interpolate(y, AmbientDown)
        : Interpolate(x, PictureAcross) * Interpolate(y, PictureDown);

    internal static byte[] Mask(bool ambient)
    {
        var pixels = new byte[Side * Side * 4];
        for (var y = 0; y < Side; y++)
            for (var x = 0; x < Side; x++)
            {
                var alpha = Channel(255 * Alpha(x / (double)(Side - 1), y / (double)(Side - 1), ambient));
                var at = (y * Side + x) * 4;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = alpha;
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

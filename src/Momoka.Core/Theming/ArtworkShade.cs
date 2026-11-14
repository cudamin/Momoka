namespace Momoka.Theming;

/// <summary>
/// 从已解码的BGRA剧照取整体氛围色，压暗后保留色相，供商店背景的环境底使用。
/// <para>
/// 2026-11-14 用户令「渐变不能根据背景图颜色自动切换吗」：这张表就是「自动换色」的那一半，每次换条目都按新剧照重取。
/// 只乘一个系数会把饱和度一起压掉 —— 浅色剧照取出来的底发灰，深色剧照又几乎看不出是哪张图的颜色，所以先朝灰度
/// 反方向补一档，再把最亮通道收到阅读底的上限：亮剧照不会亮到压不住字，暗剧照保持原样。
/// </para>
/// </summary>
public static class ArtworkShade
{
    /// <summary>取出来的底最亮那一通道的上限。白字压在这档上仍有足够对比度，主题五套深色都适用。</summary>
    private const double Ceiling = 68;

    public static ThemeColor Sample(ReadOnlySpan<byte> pixels, int width, int height, ThemeColor fallback)
    {
        if (width <= 0 || height <= 0 || (long)width * height * 4 > pixels.Length) return fallback;
        var step = Math.Max(1, (int)Math.Sqrt((double)width * height / 4096));
        double red = 0, green = 0, blue = 0, weight = 0;
        for (var y = 0; y < height; y += step)
            for (var x = 0; x < width; x += step)
            {
                var at = (y * width + x) * 4;
                var r = pixels[at + 2];
                var g = pixels[at + 1];
                var b = pixels[at];
                var max = Math.Max(r, Math.Max(g, b));
                if (max < 24) continue;
                var min = Math.Min(r, Math.Min(g, b));
                var sampleWeight = 0.2 + (max - min) / (double)max;
                red += r * sampleWeight;
                green += g * sampleWeight;
                blue += b * sampleWeight;
                weight += sampleWeight;
            }
        if (weight == 0) return fallback;

        var r0 = red / weight;
        var g0 = green / weight;
        var b0 = blue / weight;
        var gray = 0.299 * r0 + 0.587 * g0 + 0.114 * b0;
        const double Vivid = 1.35;
        var r1 = gray + (r0 - gray) * Vivid;
        var g1 = gray + (g0 - gray) * Vivid;
        var b1 = gray + (b0 - gray) * Vivid;
        var brightest = Math.Max(r1, Math.Max(g1, b1));
        if (brightest <= 0) return fallback;
        var scale = Math.Min(1d, Ceiling / brightest);
        return ThemeColor.Rgb(Channel(r1 * scale), Channel(g1 * scale), Channel(b1 * scale));

        // 补饱和度会把最弱那一路推到负数（纯红的蓝通道），不能直接转 byte。
        static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
    }
}

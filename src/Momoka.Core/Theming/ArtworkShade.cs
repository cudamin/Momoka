namespace Momoka.Theming;

/// <summary>从已解码的BGRA剧照取整体氛围色，压暗后保留色相，供图片阅读遮罩使用。</summary>
public static class ArtworkShade
{
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
        return ThemeColor.Rgb((byte)Math.Round(red / weight * 0.42),
            (byte)Math.Round(green / weight * 0.42), (byte)Math.Round(blue / weight * 0.42));
    }
}

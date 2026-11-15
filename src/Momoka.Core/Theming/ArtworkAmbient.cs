namespace Momoka.Theming;

/// <summary>将剧照的局部颜色扩散成低分辨率环境底，保留左右色区，避免整图均值变成一块灰色。</summary>
public static class ArtworkAmbient
{
    public const int Width = 96;
    public const int Height = 64;

    /// <summary>只读原 BGRA，输出不透明 BGRA；尺寸固定，窗口缩放不重新取色。</summary>
    public static byte[] Render(ReadOnlySpan<byte> pixels, int width, int height, ThemeColor window, bool dark)
    {
        const int columns = 4;
        const int rows = 3;
        var colors = new ThemeColor[columns * rows];
        Array.Fill(colors, window);
        if (width > 0 && height > 0 && (long)width * height <= pixels.Length / 4)
        {
            var step = Math.Max(1, (int)Math.Sqrt((double)width * height / 4096));
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                {
                    double red = 0, green = 0, blue = 0, weight = 0;
                    for (var y = height * row / rows; y < Math.Max(height * row / rows + 1, height * (row + 1) / rows); y += step)
                        for (var x = width * column / columns; x < Math.Max(width * column / columns + 1, width * (column + 1) / columns); x += step)
                        {
                            var at = (y * width + x) * 4;
                            var r = pixels[at + 2];
                            var g = pixels[at + 1];
                            var b = pixels[at];
                            var max = Math.Max(r, Math.Max(g, b));
                            if (max < 24 || pixels[at + 3] == 0) continue;
                            var min = Math.Min(r, Math.Min(g, b));
                            var sampleWeight = pixels[at + 3] / 255d * (0.35 + (max - min) / (double)max);
                            red += r * sampleWeight;
                            green += g * sampleWeight;
                            blue += b * sampleWeight;
                            weight += sampleWeight;
                        }
                    if (weight == 0) continue;
                    var gray = (0.299 * red + 0.587 * green + 0.114 * blue) / weight;
                    var vivid = ThemeColor.Rgb(Channel(gray + (red / weight - gray) * 1.25),
                        Channel(gray + (green / weight - gray) * 1.25),
                        Channel(gray + (blue / weight - gray) * 1.25));
                    if (dark)
                    {
                        var peak = Math.Max(vivid.R, Math.Max(vivid.G, vivid.B));
                        var scale = Math.Min(1, 82d / Math.Max(1, (int)peak));
                        colors[row * columns + column] = ThemeColor.Rgb(Channel(vivid.R * scale),
                            Channel(vivid.G * scale), Channel(vivid.B * scale)).Mix(window, 0.12);
                    }
                    else colors[row * columns + column] = window.Mix(vivid, 0.34);
                }
        }

        // 用重叠的宽色区融合，而不是把几块颜色拼成格子；保留上下分布但不留文字、边缘等图像细节。
        var output = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                double red = 0, green = 0, blue = 0, weight = 0;
                for (var row = 0; row < rows; row++)
                    for (var column = 0; column < columns; column++)
                    {
                        var dx = (x / (double)(Width - 1) - (column + 0.5) / columns) / 0.24;
                        var dy = (y / (double)(Height - 1) - (row + 0.5) / rows) / 0.38;
                        var amount = Math.Exp(-0.5 * (dx * dx + dy * dy));
                        var color = colors[row * columns + column];
                        red += color.R * amount;
                        green += color.G * amount;
                        blue += color.B * amount;
                        weight += amount;
                    }
                var at = (y * Width + x) * 4;
                output[at] = Channel(blue / weight);
                output[at + 1] = Channel(green / weight);
                output[at + 2] = Channel(red / weight);
                output[at + 3] = 255;
            }
        return output;
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}

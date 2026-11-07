namespace Momoka.Theming;

/// <summary>
/// 图片上的阅读底色：按元素覆盖的区域采样，再供页面叠遮罩和控件底色、选择前景。
/// 主页与详情共用像素规则；实际元素坐标由页面提供，不引入UI或额外图片解码。
/// </summary>
public static class BackdropReading
{
    private static readonly ThemeColor Dark = ThemeColor.Rgb(22, 22, 22);
    private static readonly ThemeColor Light = ThemeColor.Rgb(255, 255, 255);

    /// <summary>
    /// 从紧密排列的BGRA8图像取8×5个区域采样点，忽略alpha。图片按UniformToFill水平居中，
    /// 纵向由<paramref name="alignTop"/>选择顶对齐或居中。未布局或损坏输入回到给定底色。
    /// 区域均色用于选择前景，不代表区域内每个像素都达到相同的对比度。
    /// </summary>
    public static ThemeColor Sample(byte[] pixels, int imageWidth, int imageHeight, double boxWidth, double boxHeight,
        double left, double top, double width, double height, bool alignTop, ThemeColor fallback)
    {
        if (pixels is null || imageWidth <= 0 || imageHeight <= 0 || pixels.Length % 4 != 0
            || (long)imageWidth * imageHeight != pixels.Length / 4
            || !double.IsFinite(boxWidth) || !double.IsFinite(boxHeight)
            || !double.IsFinite(left) || !double.IsFinite(top)
            || !double.IsFinite(width) || !double.IsFinite(height)
            || boxWidth <= 0 || boxHeight <= 0 || width <= 0 || height <= 0) return fallback;

        var scale = Math.Max(boxWidth / imageWidth, boxHeight / imageHeight);
        var drawnWidth = imageWidth * scale;
        var drawnHeight = imageHeight * scale;
        if (!double.IsFinite(scale) || scale <= 0
            || !double.IsFinite(drawnWidth) || !double.IsFinite(drawnHeight)) return fallback;

        var offsetX = (boxWidth - drawnWidth) / 2;
        var offsetY = alignTop ? 0 : (boxHeight - drawnHeight) / 2;
        var red = 0;
        var green = 0;
        var blue = 0;
        for (var y = 0; y < 5; y++)
            for (var x = 0; x < 8; x++)
            {
                var sourceX = (left + width * ((x + 0.5) / 8) - offsetX) / scale;
                var sourceY = (top + height * ((y + 0.5) / 5) - offsetY) / scale;
                if (!double.IsFinite(sourceX) || !double.IsFinite(sourceY)) return fallback;

                var sampleX = (int)Math.Round(Math.Clamp(sourceX, 0, imageWidth - 1), MidpointRounding.AwayFromZero);
                var sampleY = (int)Math.Round(Math.Clamp(sourceY, 0, imageHeight - 1), MidpointRounding.AwayFromZero);
                var at = (sampleY * imageWidth + sampleX) * 4;
                blue += pixels[at];
                green += pixels[at + 1];
                red += pixels[at + 2];
            }

        return ThemeColor.Rgb((byte)Math.Round(red / 40d), (byte)Math.Round(green / 40d), (byte)Math.Round(blue / 40d));
    }

    /// <summary>根据已合成的实色底，选择对比度较高的近黑或白色前景。</summary>
    public static ThemeColor Foreground(ThemeColor background) =>
        ThemeColor.Contrast(Dark, background) > ThemeColor.Contrast(Light, background) ? Dark : Light;
}

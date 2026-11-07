using Momoka.Theming;

internal static class BackdropReadingTests
{
    private static readonly ThemeColor Fallback = ThemeColor.Rgb(12, 34, 56);

    internal static void Run(Action<string, Action> check)
    {
        check("背景采样读取BGRA并忽略图片alpha", () =>
            Equal(ThemeColor.Rgb(30, 20, 10), Sample([10, 20, 30, 0], 1, 1, 200, 100, 0, 0, 200, 100)));

        check("区域采样按每个控件位置区分明暗", () =>
        {
            var pixels = Image(8, 5, (x, _) => x < 4 ? ThemeColor.Rgb(0, 0, 0) : ThemeColor.Rgb(255, 255, 255));
            Equal(ThemeColor.Rgb(0, 0, 0), Sample(pixels, 8, 5, 80, 50, 0, 0, 30, 50));
            Equal(ThemeColor.Rgb(255, 255, 255), Sample(pixels, 8, 5, 80, 50, 40, 0, 40, 50));
        });

        check("UniformToFill横向裁切居中而不拉伸", () =>
        {
            var pixels = Image(8, 4, (x, _) => x is >= 2 and <= 5 ? ThemeColor.Rgb(0, 255, 0) : ThemeColor.Rgb(255, 0, 0));
            Equal(ThemeColor.Rgb(0, 255, 0), Sample(pixels, 8, 4, 40, 40, 0, 0, 30, 30));
        });

        check("详情顶对齐与主页垂直居中各采可见区域", () =>
        {
            var pixels = Image(4, 8, (_, y) => y <= 2 ? ThemeColor.Rgb(255, 0, 0) : ThemeColor.Rgb(0, 0, 255));
            Equal(ThemeColor.Rgb(255, 0, 0), Sample(pixels, 4, 8, 40, 40, 0, 0, 20, 20, true));
            Equal(ThemeColor.Rgb(51, 0, 204), Sample(pixels, 4, 8, 40, 40, 0, 0, 20, 20));
        });

        check("固定8乘5采样覆盖区域而非只取中心", () =>
        {
            var pixels = Image(16, 10, (x, y) => ThemeColor.Rgb((byte)x, (byte)y, 80));
            Equal(ThemeColor.Rgb(8, 5, 80), Sample(pixels, 16, 10, 160, 100, 0, 0, 160, 100));
        });

        check("超出图片边界的采样钳制到边缘且不修改原图", () =>
        {
            var pixels = Image(2, 2, (x, y) => ThemeColor.Rgb((byte)(x * 200), (byte)(y * 200), 40));
            var saved = pixels.ToArray();
            Equal(ThemeColor.Rgb(0, 0, 40), Sample(pixels, 2, 2, 20, 20, -100, -100, 10, 10));
            Equal(ThemeColor.Rgb(200, 200, 40), Sample(pixels, 2, 2, 20, 20, 100, 100, 10, 10));
            if (!pixels.SequenceEqual(saved)) throw new InvalidOperationException("采样改写原图");
        });

        check("空图和损坏BGRA尺寸安全回退", () =>
        {
            foreach (var pixels in new[] { Array.Empty<byte>(), new byte[3], new byte[5] })
                Equal(Fallback, Sample(pixels, 1, 1, 10, 10, 0, 0, 10, 10));
            Equal(Fallback, Sample([0, 0, 0, 255], 0, 1, 10, 10, 0, 0, 10, 10));
            Equal(Fallback, Sample([0, 0, 0, 255], 1, -1, 10, 10, 0, 0, 10, 10));
            Equal(Fallback, Sample([0, 0, 0, 255], int.MaxValue, int.MaxValue, 10, 10, 0, 0, 10, 10));
        });

        check("无效布局和非有限坐标安全回退", () =>
        {
            byte[] pixels = [0, 0, 0, 255];
            foreach (var invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                Equal(Fallback, Sample(pixels, 1, 1, invalid, 10, 0, 0, 10, 10));
                Equal(Fallback, Sample(pixels, 1, 1, 10, invalid, 0, 0, 10, 10));
                Equal(Fallback, Sample(pixels, 1, 1, 10, 10, invalid, 0, 10, 10));
                Equal(Fallback, Sample(pixels, 1, 1, 10, 10, 0, invalid, 10, 10));
                Equal(Fallback, Sample(pixels, 1, 1, 10, 10, 0, 0, invalid, 10));
                Equal(Fallback, Sample(pixels, 1, 1, 10, 10, 0, 0, 10, invalid));
            }
            Equal(Fallback, Sample(pixels, 1, 1, 0, 10, 0, 0, 10, 10));
            Equal(Fallback, Sample(pixels, 1, 1, 10, -1, 0, 0, 10, 10));
            Equal(Fallback, Sample(pixels, 1, 1, 10, 10, 0, 0, 0, 10));
            Equal(Fallback, Sample(pixels, 1, 1, 10, 10, 0, 0, 10, -1));
        });

        check("自适应前景在极亮极暗和高饱和底色上选择较高对比", () =>
        {
            Equal(ThemeColor.Rgb(22, 22, 22), Foreground(ThemeColor.Rgb(255, 255, 255)));
            Equal(ThemeColor.Rgb(255, 255, 255), Foreground(ThemeColor.Rgb(0, 0, 0)));
            Equal(ThemeColor.Rgb(22, 22, 22), Foreground(ThemeColor.Rgb(255, 255, 0)));
            Equal(ThemeColor.Rgb(255, 255, 255), Foreground(ThemeColor.Rgb(0, 0, 255)));
            Equal(ThemeColor.Rgb(255, 255, 255), Foreground(ThemeColor.Rgb(120, 120, 120)));
            Equal(ThemeColor.Rgb(22, 22, 22), Foreground(ThemeColor.Rgb(128, 128, 128)));
        });

        check("浅中性罩与自选控件底色合成后仍按最终底色选字", () =>
        {
            var white = ThemeColor.Rgb(255, 255, 255);
            var background = white.Mix(ThemeColor.Rgb(128, 128, 128), 13 / 255d);
            Equal(ThemeColor.Rgb(22, 22, 22), Foreground(background));
            Equal(white, Foreground(background.Mix(ThemeColor.Rgb(0, 0, 0), 1)));
            Equal(ThemeColor.Rgb(22, 22, 22), Foreground(background.Mix(white, 1)));
        });
    }

    private static ThemeColor Sample(byte[] pixels, int imageWidth, int imageHeight, double boxWidth, double boxHeight,
        double left, double top, double width, double height, bool alignTop = false) =>
        BackdropReading.Sample(pixels, imageWidth, imageHeight, boxWidth, boxHeight, left, top, width, height, alignTop, Fallback);

    private static ThemeColor Foreground(ThemeColor background) => BackdropReading.Foreground(background);

    private static byte[] Image(int width, int height, Func<int, int, ThemeColor> color)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var ink = color(x, y);
                var at = (y * width + x) * 4;
                pixels[at] = ink.B;
                pixels[at + 1] = ink.G;
                pixels[at + 2] = ink.R;
                pixels[at + 3] = ink.A;
            }
        return pixels;
    }

    private static void Equal(ThemeColor expected, ThemeColor actual)
    {
        if (expected != actual) throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
    }
}

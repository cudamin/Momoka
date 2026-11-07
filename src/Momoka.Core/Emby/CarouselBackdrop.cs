namespace Momoka.Emby;

/// <summary>轮播背景图：左侧采色延长15%，完整扩边高斯通过原图左侧8%平滑接回清晰图。</summary>
public static class CarouselBackdrop
{
    public const double LeftExtensionShare = 0.15;

    /// <summary>延长量按原图宽度计算；像素取整最多相差半个像素。</summary>
    public static int ExtensionWidth(int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        return Math.Max(1, (int)Math.Round(width * LeftExtensionShare));
    }

    /// <summary>过渡留在原图内；至少一列，且不会超过原图宽度。</summary>
    public static int TransitionWidth(int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        return Math.Max(1, (int)Math.Round(width * 0.08));
    }

    /// <summary>
    /// 输入为顶行在前的 BGRA 像素。新增部分使用左侧 2% 的颜色、沿高度大窗口柔化（上下四分之一高度混在一起，
    /// 「边缘的色彩上下要混合，别只是简单的拉长」）后向原图左边缘过渡。高斯覆盖完整延长区，
    /// 在原图左侧8%平滑衰减，过渡之后逐字节保持原图，不改变任何alpha。
    /// </summary>
    public static (byte[] Pixels, int Width) ExtendLeft(ReadOnlySpan<byte> source, int width, int height, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var stride = checked(width * 4);
        if (source.Length != checked(stride * height))
            throw new ArgumentException("背景图像素数与尺寸不一致。", nameof(source));
        token.ThrowIfCancellationRequested();

        var extension = ExtensionWidth(width);
        var extendedWidth = checked(width + extension);
        var extendedStride = checked(extendedWidth * 4);
        var pixels = new byte[checked(extendedStride * height)];
        var sampleWidth = Math.Max(1, (int)Math.Round(width * 0.02));

        // 竖向柔化的窗口开到高的四分之一：「边缘的色彩上下要混合，别只是简单的拉长」（2026-09-12）——
        // 窗口小（4%）的时候每一行还带着自己那一行的颜色，延长区看起来就是把左边一条横向拉出来的；
        // 窗口开大之后每个行的颜色是上下各四分之一高度的混合，延长区成一条缓变的色带，不再复刻原图
        // 边缘的细节。
        var radius = Math.Max(1, (int)Math.Round(height * 0.25));

        // 每行先取左侧颜色，再用前缀和柔化竖向变化。扩展只使用边缘颜色，不复制场景或人物。
        var sums = new double[checked((height + 1) * 3)];
        for (var y = 0; y < height; y++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                double value = 0;
                for (var x = 0; x < sampleWidth; x++) value += source[y * stride + x * 4 + channel];
                sums[(y + 1) * 3 + channel] = sums[y * 3 + channel] + value / sampleWidth;
            }
        }

        for (var y = 0; y < height; y++)
        {
            source.Slice(y * stride, stride).CopyTo(pixels.AsSpan(y * extendedStride + extension * 4, stride));
            var top = Math.Max(0, y - radius);
            var bottom = Math.Min(height, y + radius + 1);

            for (var x = 0; x < extension; x++)
            {
                var position = extension == 1 ? 1 : (double)x / (extension - 1);
                var blend = position * position * (3 - 2 * position);
                var target = y * extendedStride + x * 4;

                for (var channel = 0; channel < 3; channel++)
                {
                    var sampled = (sums[bottom * 3 + channel] - sums[top * 3 + channel]) / (bottom - top);
                    var edge = source[y * stride + channel];
                    pixels[target + channel] = (byte)Math.Clamp(Math.Round(sampled + (edge - sampled) * blend), 0, 255);
                }

                pixels[target + 3] = source[y * stride + 3];
            }
        }

        // 延长区始终全高斯；淡回清晰的部分移进原图，避免扩边边界直接撞上清晰细节。
        // 过渡右端再取3σ作为卷积采样，写回范围严格止于过渡末尾。
        var transition = TransitionWidth(width);
        var blurWidth = extension + transition;
        var sampleBlurWidth = Math.Min(extendedWidth, blurWidth + GaussianBackdrop.Sigma * 3);
        var blurStride = checked(sampleBlurWidth * 4);
        var blurred = new byte[checked(blurStride * height)];
        for (var y = 0; y < height; y++)
            pixels.AsSpan(y * extendedStride, blurStride).CopyTo(blurred.AsSpan(y * blurStride));
        GaussianBackdrop.Apply(blurred, sampleBlurWidth, height, GaussianBackdrop.Sigma, token);
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < blurWidth; x++)
            {
                var position = x < extension ? 0 : (double)(x - extension) / transition;
                // 两端一、二阶导数为零，让过渡自然接回清晰图，避免线性羽化留下新接缝。
                var clearShare = position * position * position * (position * (position * 6 - 15) + 10);
                for (var channel = 0; channel < 3; channel++)
                {
                    var target = y * extendedStride + x * 4 + channel;
                    var value = blurred[y * blurStride + x * 4 + channel];
                    pixels[target] = (byte)Math.Clamp(Math.Round(value + (pixels[target] - value) * clearShare), 0, 255);
                }
            }
        }
        return (pixels, extendedWidth);
    }
}

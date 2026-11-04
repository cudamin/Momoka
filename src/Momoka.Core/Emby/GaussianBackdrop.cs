namespace Momoka.Emby;

/// <summary>真正的分离式高斯卷积，不用盒式近似。背景是不透明BGRA，保留输入alpha。</summary>
public static class GaussianBackdrop
{
    // 1280宽原料上sigma12足以柔化文字背后的细节，同时保留构图；不随窗口裁切重算。
    public const int Sigma = 12;
    public const double FadeHeight = 40;

    /// <summary>按归一化 exp(-x²/(2σ²)) 核先横再纵卷积，3σ外截断，边缘夹取防止黑圈。</summary>
    public static void Apply(byte[] pixels, int width, int height, double sigma, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4)) throw new ArgumentException("像素数与尺寸不一致。", nameof(pixels));
        token.ThrowIfCancellationRequested();
        if (sigma <= 0) return;
        if (!double.IsFinite(sigma) || sigma > 128) throw new ArgumentOutOfRangeException(nameof(sigma));

        // 与SciPy v1.16.3 ndimage/_filters.py 的_gaussian_kernel1d(order=0)同一核定义，
        // 但本背景专用实现取3σ截断和nearest边缘，不改变图片尺寸。
        var radius = (int)Math.Ceiling(sigma * 3);
        var kernel = new double[radius * 2 + 1];
        double total = 0;
        for (var i = -radius; i <= radius; i++)
        {
            var weight = Math.Exp(-0.5 * i * i / (sigma * sigma));
            kernel[i + radius] = weight;
            total += weight;
        }
        for (var i = 0; i < kernel.Length; i++) kernel[i] /= total;
        var temp = new float[checked(width * height * 3)];
        var result = pixels.ToArray();
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 3; channel++)
                {
                    double sum = 0;
                    for (var k = -radius; k <= radius; k++)
                        sum += pixels[(y * width + Math.Clamp(x + k, 0, width - 1)) * 4 + channel] * kernel[k + radius];
                    temp[(y * width + x) * 3 + channel] = (float)sum;
                }
        }
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 3; channel++)
                {
                    double sum = 0;
                    for (var k = -radius; k <= radius; k++)
                        sum += temp[(Math.Clamp(y + k, 0, height - 1) * width + x) * 3 + channel] * kernel[k + radius];
                    result[(y * width + x) * 4 + channel] = (byte)Math.Clamp(Math.Round(sum), 0, 255);
                }
        }
        token.ThrowIfCancellationRequested();
        result.CopyTo(pixels, 0);
    }
}

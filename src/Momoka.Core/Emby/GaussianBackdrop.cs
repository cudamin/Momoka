namespace Momoka.Emby;

/// <summary>真正的分离式高斯卷积，不用盒式近似。背景是不透明BGRA，保留输入alpha。</summary>
public static class GaussianBackdrop
{
    // 1280宽原料上sigma12足以柔化文字背后的细节，同时保留构图；不随窗口裁切重算。
    // 主页轮播（BannerPictureLoader）读这一档，详情页那一档改成跟设置里的强度走（见 SigmaFor）。
    public const int Sigma = 12;

    /// <summary>用户在效果demo中选定的背景下沿模糊范围：仅5%，不画黑色渐变。</summary>
    public const double BottomShare = 0.05;

    /// <summary>在图片与主题界面之间生成独立高斯背景条，接缝位于条中央，输入原图不变。</summary>
    public static byte[] Seam(byte[] pixels, int width, int height, int boundary, int rows,
        byte[] paper, byte shade, CancellationToken token = default, int imageEnd = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        if (pixels.Length != checked(width * height * 4) || paper.Length != 3)
            throw new ArgumentException("接缝像素与尺寸不一致。", nameof(pixels));
        token.ThrowIfCancellationRequested();
        rows = Math.Max(2, rows);
        var half = rows / 2;
        var margin = Math.Max(1, (int)Math.Ceiling(rows / 2d));
        var workingRows = checked(rows + margin * 2);
        var stride = checked(width * 4);
        var working = new byte[checked(stride * workingRows)];
        for (var y = 0; y < workingRows; y++)
        {
            var sourceY = boundary - half + y - margin;
            for (var x = 0; x < width; x++)
            {
                var at = y * stride + x * 4;
                var fromImage = sourceY < boundary && sourceY < imageEnd;
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = fromImage ? pixels[(Math.Clamp(sourceY, 0, height - 1) * width + x) * 4 + channel] : paper[channel];
                    var ink = channel switch { 0 => DetailHero.ScrimInk.B, 1 => DetailHero.ScrimInk.G, _ => DetailHero.ScrimInk.R };
                    working[at + channel] = fromImage
                        ? (byte)Math.Round(value * (1 - shade / 255d) + ink * shade / 255d) : value;
                }
                working[at + 3] = 255;
            }
        }
        var original = working.ToArray();
        // 输出半带是3σ，核的额外采样不扩大屏上的5%范围。
        Apply(working, width, workingRows, rows / 6d, token);
        token.ThrowIfCancellationRequested();
        var result = working.AsSpan(margin * stride, rows * stride).ToArray();
        // 模糊强度在两端平滑归零；只替换首末一行会让高频图片在第二行形成新的横线。
        for (var y = 0; y < rows; y++)
        {
            var distance = Math.Min(y, rows - 1 - y) / Math.Max(1d, (rows - 1) / 2d);
            var weight = distance * distance * (3 - 2 * distance);
            for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 3; channel++)
                {
                    var at = y * stride + x * 4 + channel;
                    var under = original[(margin + y) * stride + x * 4 + channel];
                    result[at] = (byte)Math.Round(under + (result[at] - under) * weight);
                }
        }
        return result;
    }

    /// <summary>
    /// 详情页背景图模糊拉满时的 sigma（设置里那一行拖到 100）。
    /// <para>
    /// 24 是 12 的两倍：12 还留得住「这是哪部片子」的构图，24 连人和景都糊成色块 —— 用户要的「拉到最模糊」
    /// 就是这一档。再往上（40 出头）只剩一团匀色，屏幕上已经看不出还模糊过一次，只是多等一次卷积。
    /// </para>
    /// </summary>
    public const int MaxSigma = 24;

    /// <summary>
    /// 设置里那一档强度（0..1）换算成 sigma：0 是「不加模糊」（返回 0，<see cref="Apply"/> 直接返回原图），
    /// 1 是 <see cref="MaxSigma"/>，中间线性。非有限值按 0 算 —— 与另外两条滑块的兜底一致。
    /// </summary>
    public static int SigmaFor(double strength)
    {
        var share = double.IsFinite(strength) ? Math.Clamp(strength, 0, 1) : 0;
        return (int)Math.Round(MaxSigma * share);
    }

    /// <summary>只提交背景下沿5%的高斯结果，起点平滑混合；end 是图片实际可见下沿。</summary>
    public static void ApplyBottom(byte[] pixels, int width, int height, double sigma,
        CancellationToken token = default, double end = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4)) throw new ArgumentException("像素数与尺寸不一致。", nameof(pixels));
        token.ThrowIfCancellationRequested();
        if (sigma <= 0 || end <= 0) return;
        if (!double.IsFinite(sigma) || sigma > 128) throw new ArgumentOutOfRangeException(nameof(sigma));
        if (!double.IsFinite(end) || end > 1) throw new ArgumentOutOfRangeException(nameof(end));
        var start = end * (1 - BottomShare);
        var first = Math.Clamp((int)Math.Floor(start * height), 0, height - 1);
        // 核的上方采样仍取原图，不能把渐变起点当成卷积边界。
        var from = Math.Max(0, first - (int)Math.Ceiling(sigma * 3));
        var count = Math.Min(height, (int)Math.Ceiling(end * height) + (int)Math.Ceiling(sigma * 3)) - from;
        var stride = checked(width * 4);
        var blurred = pixels.AsSpan(from * stride, count * stride).ToArray();
        Apply(blurred, width, count, sigma, token);
        token.ThrowIfCancellationRequested();
        for (var y = first; y < Math.Min(height, (int)Math.Ceiling(end * height)); y++)
        {
            var t = Math.Clamp(((y + 0.5) / height - start) / (end - start), 0, 1);
            var weight = t * t * (3 - 2 * t);
            for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 3; channel++)
                {
                    var at = y * stride + x * 4 + channel;
                    var softened = blurred[(y - from) * stride + x * 4 + channel];
                    pixels[at] = (byte)Math.Round(pixels[at] + (softened - pixels[at]) * weight);
                }
        }
    }

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

        // 大图按行并行：每一行只读写自己那一段 temp/result，行与行互不重叠；小图（测试里那种十几像素的）
        // 走单线程，省一次调度。σ=24 在 1280×720 上单线程要 4.7 秒，拖动滑块根本等不起 —— 并行之后
        // 大致能压到一秒上下，配上调用方的取消，中途换档不排队。
        var parallel = width * height >= 64 * 64;
        void Horizontal(int y)
        {
            for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 3; channel++)
                {
                    double sum = 0;
                    for (var k = -radius; k <= radius; k++)
                        sum += pixels[(y * width + Math.Clamp(x + k, 0, width - 1)) * 4 + channel] * kernel[k + radius];
                    temp[(y * width + x) * 3 + channel] = (float)sum;
                }
        }
        void Vertical(int y)
        {
            for (var x = 0; x < width; x++)
                for (var channel = 0; channel < 3; channel++)
                {
                    double sum = 0;
                    for (var k = -radius; k <= radius; k++)
                        sum += temp[(Math.Clamp(y + k, 0, height - 1) * width + x) * 3 + channel] * kernel[k + radius];
                    result[(y * width + x) * 4 + channel] = (byte)Math.Clamp(Math.Round(sum), 0, 255);
                }
        }
        if (parallel)
        {
            var options = new ParallelOptions { CancellationToken = token };
            Parallel.For(0, height, options, Horizontal);
            Parallel.For(0, height, options, Vertical);
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                Horizontal(y);
            }
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                Vertical(y);
            }
        }
        token.ThrowIfCancellationRequested();
        result.CopyTo(pixels, 0);
    }
}

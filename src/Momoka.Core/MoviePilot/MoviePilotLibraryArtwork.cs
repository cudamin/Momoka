namespace Momoka.MoviePilot;

/// <summary>首页订阅分类封面：从已有订阅选择海报，再合成带淡出倒影的横向书架。</summary>
public static class MoviePilotLibraryArtwork
{
    public const int Width = 576;
    public const int Height = 324;
    private const int ArtworkHeight = 272;
    private const int Gap = 2;
    private const int MaxPosters = 4;
    private const int MaxSourcePixels = 1280 * 720;

    /// <summary>合成订阅入口封面最多用四张海报（<see cref="Compose"/> 的上限）。</summary>
    public const int CoverPosters = MaxPosters;

    /// <summary>主页「九宫格海报墙」预设最多要九张不同海报；不足时由绘制端循环补足。</summary>
    public const int WallPosters = 9;

    /// <summary>顶行在前、非预乘 alpha 的 BGRA 像素；合成结果始终不透明。</summary>
    public sealed record ArtworkPixels(byte[] Pixels, int Width, int Height);

    /// <summary>
    /// 每条订阅优先取海报，缺海报才取背景图；去重后按顺序取前 <paramref name="limit"/> 张。
    /// 合成封面用 <see cref="CoverPosters"/>，九宫格预设用 <see cref="WallPosters"/>，
    /// 两张订阅入口卡因此能拿到各自最多九张不同的海报，而不是把同一张合成图复制九份。
    /// </summary>
    public static IReadOnlyList<string> Select(IEnumerable<MoviePilotSubscription> subscriptions,
        int limit = CoverPosters)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var urls = new List<string>(Math.Min(limit, 16));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subscription in subscriptions)
        {
            var url = ArtworkUrl(subscription.Media.PosterUrl) ?? ArtworkUrl(subscription.Media.BackdropUrl);
            if (url is null || !seen.Add(url)) continue;
            urls.Add(url);
            if (urls.Count == limit) break;
        }
        return urls.ToArray();
    }

    /// <summary>
    /// 使用实际成功解码的图片数等分封面；主图居中等比裁满，下方倒影保留主图的裁切并渐淡到深底。
    /// 输入尺寸受限，像素处理可在后台执行；输出固定大小，交给现有卡片解码和回收路径。
    /// </summary>
    public static ArtworkPixels Compose(IReadOnlyList<ArtworkPixels> sources, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count > MaxPosters)
            throw new ArgumentOutOfRangeException(nameof(sources), "订阅分类封面最多使用四张图片。");
        token.ThrowIfCancellationRequested();
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.Pixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.Width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(source.Height);
            var size = (long)source.Width * source.Height;
            if (size > MaxSourcePixels)
                throw new ArgumentOutOfRangeException(nameof(sources), "封面原图必须在解码时限制像素尺寸。");
            if (source.Pixels.Length != size * 4)
                throw new ArgumentException("封面像素数与尺寸不一致。", nameof(sources));
            token.ThrowIfCancellationRequested();
        }

        var pixels = new byte[Width * Height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = Background(0);
            pixels[offset + 1] = Background(1);
            pixels[offset + 2] = Background(2);
            pixels[offset + 3] = 255;
        }
        var availableWidth = Width - Gap * Math.Max(0, sources.Count - 1);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var left = index * availableWidth / sources.Count + index * Gap;
            var tileWidth = (index + 1) * availableWidth / sources.Count - index * availableWidth / sources.Count;
            var scale = Math.Max(tileWidth / (double)source.Width, ArtworkHeight / (double)source.Height);
            var cropX = (source.Width - tileWidth / scale) / 2;
            var cropY = (source.Height - ArtworkHeight / scale) / 2;
            for (var y = 0; y < ArtworkHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < tileWidth; x++)
                    PaintPixel(source, pixels, (y * Width + left + x) * 4,
                        (x + 0.5) / scale + cropX - 0.5, (y + 0.5) / scale + cropY - 0.5);
            }
            var reflectionHeight = Height - ArtworkHeight;
            for (var y = 0; y < reflectionHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                var remaining = 1 - y / (double)(reflectionHeight - 1);
                var opacity = 0.42 * remaining * remaining;
                for (var x = 0; x < tileWidth; x++)
                {
                    var origin = ((ArtworkHeight - 1 - y) * Width + left + x) * 4;
                    var target = ((ArtworkHeight + y) * Width + left + x) * 4;
                    for (var channel = 0; channel < 3; channel++)
                        pixels[target + channel] = (byte)Math.Round(Background(channel)
                            + (pixels[origin + channel] - Background(channel)) * opacity);
                }
            }
        }
        token.ThrowIfCancellationRequested();
        return new ArtworkPixels(pixels, Width, Height);
    }

    private static string? ArtworkUrl(string? value)
    {
        var url = value?.Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? url : null;
    }

    private static byte Background(int channel) => channel switch { 0 => 17, 1 => 14, _ => 12 };

    private static void PaintPixel(ArtworkPixels source, byte[] target, int targetOffset, double x, double y)
    {
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var left = (int)x;
        var top = (int)y;
        var right = Math.Min(left + 1, source.Width - 1);
        var bottom = Math.Min(top + 1, source.Height - 1);
        var across = x - left;
        var down = y - top;
        var first = (top * source.Width + left) * 4;
        var second = (top * source.Width + right) * 4;
        var third = (bottom * source.Width + left) * 4;
        var fourth = (bottom * source.Width + right) * 4;
        // Interpolate premultiplied colors so invisible RGB values do not bleed into visible edges.
        var a = (1 - across) * (1 - down) * source.Pixels[first + 3] / 255;
        var b = across * (1 - down) * source.Pixels[second + 3] / 255;
        var c = (1 - across) * down * source.Pixels[third + 3] / 255;
        var d = across * down * source.Pixels[fourth + 3] / 255;
        var backgroundShare = 1 - a - b - c - d;
        for (var channel = 0; channel < 3; channel++)
            target[targetOffset + channel] = (byte)Math.Clamp(Math.Round(
                source.Pixels[first + channel] * a + source.Pixels[second + channel] * b
                + source.Pixels[third + channel] * c + source.Pixels[fourth + channel] * d
                + Background(channel) * backgroundShare), 0, 255);
    }
}

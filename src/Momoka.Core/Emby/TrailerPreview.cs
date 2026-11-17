namespace Momoka.Emby;

public static class TrailerPreview
{
    /// <summary>商店右侧背景图／预告视口的宽上限；16:9 时高 517.5。</summary>
    public const double BackgroundMaxWidth = 920;

    /// <summary>
    /// 视口开始跟着窗口收窄的那条线。2026-11-17 用户令「窗口更窄的时候才开始缩放」：从前按页宽 60% 给
    /// （约 1533 以下就开始缩），现在宽到 1240 DIP 之前一律 <see cref="BackgroundMaxWidth"/>，再窄才按
    /// 「页宽 − 320」收。320 = 1240 − 920 就是这个差额：收窄时视口左沿停在 304（1240 − 16 空隙 − 920），
    /// 正好在片名栏左沿（96 留白 + 180 封面 + 22 栏距 = 298）右边一点，不再越过封面后的第一栏。
    /// </summary>
    public const double BackgroundScaleFrom = 1240;

    /// <summary>预告只装饰顶部右侧，保持16:9，不铺到左栏或底部正文。</summary>
    public static double BackgroundWidth(double width)
    {
        // 尺寸还没量到或坏数按 1280 兜底 —— 那一档已经是宽版式，给到的就是最大视口。
        var page = double.IsFinite(width) && width > 0 ? width : 1280;
        return Math.Clamp(page - (BackgroundScaleFrom - BackgroundMaxWidth), 0, BackgroundMaxWidth);
    }

    public static double BackgroundHeight(double width) => BackgroundWidth(width) * 9 / 16;

    public static string? YouTubeId(IEnumerable<MediaTrailerUrl> trailers)
    {
        foreach (var trailer in trailers)
        {
            if (!Uri.TryCreate(trailer.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort) continue;
            string? id = null;
            if (uri.Host == "youtu.be" && uri.AbsolutePath.Count(character => character == '/') == 1)
                id = uri.AbsolutePath[1..];
            else if (uri.Host is "youtube.com" or "www.youtube.com" or "m.youtube.com" && uri.AbsolutePath == "/watch")
            {
                var values = uri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("v=", StringComparison.Ordinal)).ToArray();
                if (values.Length == 1) id = values[0][2..];
            }
            if (id is { Length: 11 } && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return id;
        }
        return null;
    }
}

using Momoka.Theming;

namespace Momoka.Emby;

/// <summary>详情背景的单层遮罩：底部六分之一接主题，不把两道暗色重复合成。</summary>
public static class DetailBackdrop
{
    public const double FadeStart = 5d / 6;

    public static ThemeColor Shade(ThemeColor reading, ThemeColor paper, double along)
    {
        var fade = double.IsFinite(along) ? Math.Clamp(along * 6 - 5, 0, 1) : 0;
        var alpha = Math.Max(reading.A / 255d, fade);
        return reading.Mix(paper, fade) with { A = (byte)Math.Round(alpha * 255) };
    }
}

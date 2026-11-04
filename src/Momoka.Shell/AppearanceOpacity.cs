using Momoka.Emby;

namespace Momoka.Shell;

/// <summary>零档保留原始外观，滑块向上只增加不透明度，100 档完全不透明。</summary>
internal static class AppearanceOpacity
{
    public static double ControlBackground(double percent) => Percent(percent) / 100;

    public static double Backdrop(double percent, bool showcase)
    {
        var baseline = showcase ? DetailHero.ShowcaseBackdropOpacity : DetailHero.BackdropOpacity;
        return baseline + (1 - baseline) * Percent(percent) / 100;
    }

    // 主页图片原本已经完全不透明，增强不透明度无需改变它。
    public static double HomeBackdrop(double percent) => 1;

    private static double Percent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}

using Momoka.Emby;

namespace Momoka.Shell;

/// <summary>
/// 两条外观滑块的读数就是它们自己的名字：0 档什么都不画，100 档画满，中间线性。
/// <para>
/// 2026-11-06 定稿（同一天第二次）：背景图那一行管的不是图片自己的透明度，而是<b>压在图上的那层处理</b> ——
/// 暗色遮罩有多浓、高斯模糊有多糊。用户原话「百分之一百，遮罩完全不透明，高斯模糊拉到最模糊；百分之 0
/// 关闭遮罩，关闭高斯模糊」。当天上午的第一版把它读成图片的不透明度（0 档整张图收走），方向是反的。
/// </para>
/// </summary>
internal static class AppearanceOpacity
{
    public static double ControlBackground(double percent) => Percent(percent) / 100;

    /// <summary>背景图那一档的强度：0 不遮不糊，1 遮罩完全不透明／模糊拉满。</summary>
    public static double Backdrop(double percent) => Percent(percent) / 100;

    /// <summary>
    /// 遮罩表上某一档在给定强度下画出来的 alpha。表（<see cref="DetailHero.ScrimStops"/>）的最浓档是
    /// <see cref="DetailHero.ScrimCeiling"/> = 0xB8，也就是老版写死的观感：强度 72 时正好还原成 0xB8；
    /// 100 抬到 255（遮罩完全不透明，图看不见了），0 归零（一点都不遮）。
    /// </summary>
    public static byte ScrimAlpha(byte raw, double strength)
    {
        var share = double.IsFinite(strength) ? Math.Clamp(strength, 0, 1) : 0;
        if (raw == 0 || share == 0) return 0;
        var normalized = raw / (double)DetailHero.ScrimCeiling;
        return (byte)Math.Clamp(Math.Round(normalized * byte.MaxValue * share), 0, 255);
    }

    // 主页轮播那张图原本就是满不透明的一层，滑块不归它管（设置里那行说明也只写详情页）。
    public static double HomeBackdrop(double percent) => 1;

    private static double Percent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}

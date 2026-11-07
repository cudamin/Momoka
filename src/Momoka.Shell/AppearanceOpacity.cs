using Momoka.Configuration;
using Momoka.Emby;

namespace Momoka.Shell;

/// <summary>
/// 两条外观滑块的读数就是它们自己的名字：0 档什么都不画，100 档画满，中间线性。
/// <para>
/// 2026-11-07 起亚克力那边不再有「整体浓度」：用户令「背景图和按钮新的亚克力改为三项可调，去掉高斯模糊和
/// 颜色遮罩」，能调的只剩 <see cref="Tint"/>（染色参数百分比 → 0..1）、TintColor 和两项浓度本身。读文字
/// 底色时用 <see cref="Coverage"/> 估这层材料挡住了多少画面 —— 这是估算，不是像素级复刻平台合成。
/// </para>
/// </summary>
internal static class AppearanceOpacity
{
    public static double ControlBackground(double percent) => Percent(percent) / 100;

    /// <summary>
    /// 染色参数的百分比读数 → 0..1。0 与 100 都是有效档位（全透 / 全实），只有认不出来的值才回到出厂 55%。
    /// </summary>
    public static double Tint(double percent) => double.IsFinite(percent)
        ? Math.Clamp(percent, 0, 100) / 100 : UiSettings.DefaultAcrylicTintOpacity / 100;

    /// <summary>
    /// 这层亚克力挡在画面前面的份额：0 全透明，1 完全不透。
    /// <para>
    /// 按项目里那块玻璃记下来的经验（见 <c>Palette.xaml</c> 的 <c>EgFrostBrush</c>）：透上来的份量约等于
    /// (1 − 明暗浓度) × (1 − 染色浓度)，所以挡住的就是它的补数。平台真正的合成比这复杂（还要看底下像素的
    /// 明暗），这里只拿它算「字压在这层材料上该用白还是黑」。
    /// </para>
    /// </summary>
    public static double Coverage(double tintOpacity, double luminosityOpacity) =>
        1 - (1 - Clamp01(tintOpacity)) * (1 - Clamp01(luminosityOpacity));

    /// <summary>
    /// 没有画亚克力那一层时的阅读保护浓度（0..1）。窄窗与集页的卡面版式不上那层玻璃，压在图上的仍是这道
    /// 实色罩子；40% 是 2026-11-06 那版「背景图不透明度」的出厂值，观感与它一致。
    /// </summary>
    public const double ReadingVeil = 0.4;

    /// <summary>
    /// 遮罩表上某一档在给定强度下画出来的 alpha。表（<see cref="DetailHero.ScrimStops"/>）的最浓档是
    /// <see cref="DetailHero.ScrimCeiling"/> = 0xB8，也就是老版写死的观感：40% 时回到那道罩子的样子。
    /// </summary>
    public static byte ScrimAlpha(byte raw, double strength)
    {
        var share = double.IsFinite(strength) ? Math.Clamp(strength, 0, 1) : 0;
        if (raw == 0 || share == 0) return 0;
        var normalized = raw / (double)DetailHero.ScrimCeiling;
        return (byte)Math.Clamp(Math.Round(normalized * byte.MaxValue * share), 0, 255);
    }

    private static double Percent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

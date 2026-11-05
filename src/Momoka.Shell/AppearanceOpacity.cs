namespace Momoka.Shell;

/// <summary>
/// 两条外观滑块的读数就是它们自己的名字：0 档完全透明（什么都不画），100 档完全不透明，中间线性。
/// <para>
/// 2026-11-06 用户令「0 改为完全透明，100 改为完全不透明」之前的版本是「在现有底色／现有背景浓度上再加多少」，
/// 0 档仍然看得见一层薄白和一整张近乎不透明的背景图。判据只剩一个百分比，所以两种版式（普通与沉浸）读同一档，
/// 不再各有一条写死的基线。
/// </para>
/// </summary>
internal static class AppearanceOpacity
{
    public static double ControlBackground(double percent) => Percent(percent) / 100;

    public static double Backdrop(double percent) => Percent(percent) / 100;

    // 主页轮播那张图原本就是满不透明的一层，滑块不归它管（设置里那行说明也只写详情页）。
    public static double HomeBackdrop(double percent) => 1;

    private static double Percent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}

namespace Momoka.Playback;

/// <summary>
/// 播放页浮出菜单的高度上限：菜单底边贴着锚点往上开，所以「还能长多高」是锚点上沿到窗口顶边那一段，
/// 再留 <see cref="Gap"/> 的缝。纯算术，理由是 2026-11-03 用户令「集列表菜单不要跑到窗口外面，要始终
/// 保持在窗口内」。
/// <para>
/// **为什么需要算，而不是交给框架**：播放页的六个下拉与右键菜单都是 <c>MenuFlyout</c>，而框架在这一版
/// （WinUI 2.3.9）把它们托在<b>窗口化的弹层</b>里 —— 那是框架给菜单挂 <c>DesktopAcrylicBackdrop</c>
/// 的前提（亚克力要一个自己的 HWND 去采桌面）。窗口化弹层不受 <c>XamlRoot</c> 边界约束，而
/// <c>MenuFlyoutPresenter</c> 默认不限高：二十几集的列表在 720 高的窗口里必然从顶边顶出去。
/// 收紧 presenter 的 <c>MaxHeight</c> 之后，超出的部分交给它自己模板里的 <c>ScrollViewer</c> 滚动，
/// 亚克力与键盘导航都还在。
/// </para>
/// <para>
/// 窗口高度或锚点位置读不出来时退回「整窗高减两道缝」，而不是返回 0：一个读不出几何的窗口里，长一点的
/// 菜单好过一条挤成两行的菜单。两者都读不出来才返回 <see cref="double.PositiveInfinity"/>，也就是框架
/// 默认的不限高。
/// </para>
/// </summary>
public static class PlayerMenuFit
{
    /// <summary>菜单与窗口上沿、下沿之间留的缝（逻辑像素）。</summary>
    public const double Gap = 8;

    /// <summary>
    /// <paramref name="anchorTop"/>（锚点上沿，逻辑像素，窗口坐标）之上还能放进菜单的高度。
    /// </summary>
    public static double MaxHeight(double anchorTop, double windowHeight, double gap = Gap)
    {
        var window = double.IsFinite(windowHeight) && windowHeight > 0
            ? Math.Max(0, windowHeight - gap * 2)
            : double.PositiveInfinity;

        var room = double.IsFinite(anchorTop) && anchorTop > 0
            ? Math.Max(0, anchorTop - gap)
            : window;

        return Math.Min(room, window);
    }
}

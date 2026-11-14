using Momoka.Shell.Views;

namespace Momoka.Shell;

// Anonymous runtime readings, not a second implementation of the product's scroll state machine.
internal sealed record HomeHeaderChromeReading(
    bool HeaderVisible, bool AccountVisible, bool BackgroundVisible, bool BackgroundHitTest,
    double BackgroundHeight, double BackgroundWidth, double ShellWidth,
    bool BrowseHidden, double NativeHeight, double NativeRightInset,
    int Holes, int PassthroughRegions, bool WholeCaption,
    bool MinimizeRegion, bool MaximizeRegion, bool CloseRegion);

internal static class HomeHeaderChromeProbeReadings
{
    internal static void State(HomeHeaderMode expected, HomeHeaderChromeReading actual)
    {
        var hidden = expected == HomeHeaderMode.Hidden;
        Check(actual.HeaderVisible == !hidden && actual.AccountVisible == !hidden,
            "主页顶部操作与账号没有一起收放");
        Check(actual.BackgroundVisible == (expected == HomeHeaderMode.Revealed) && !actual.BackgroundHitTest,
            "主页亚克力背景显隐错误或拦截输入");
        if (expected == HomeHeaderMode.Revealed)
            Check(Finite(actual.BackgroundHeight, actual.BackgroundWidth, actual.ShellWidth)
                && Math.Abs(actual.BackgroundHeight - 48) <= 0.5
                && actual.ShellWidth > 0 && Math.Abs(actual.BackgroundWidth - actual.ShellWidth) <= 1,
                "主页上滚背景不是全宽 48 DIP");
        Check(actual.BrowseHidden == hidden && Finite(actual.NativeHeight, actual.NativeRightInset),
            "浏览标题栏状态未跟随当前主页或原生读数无效");
        if (hidden)
        {
            Check(actual.NativeHeight == 0 && actual.NativeRightInset == 0,
                "主页隐藏时系统三键仍保留原生高度或右侧占位");
            Check(actual.Holes == 0 && actual.PassthroughRegions == 0 && actual.WholeCaption,
                "主页隐藏时仍有不可见控件穿透区域或缺少整条拖动区");
            Check(!actual.MinimizeRegion && !actual.MaximizeRegion && !actual.CloseRegion,
                "主页隐藏时系统三键命中区仍在");
        }
        else
        {
            Check(actual.NativeHeight > 0 && actual.NativeRightInset > 0,
                "主页恢复后系统三键没有恢复原生高度与占位");
            Check(actual.Holes > 0 && actual.PassthroughRegions > 0,
                "主页恢复后操作控件没有恢复穿透区域");
            Check(actual.MinimizeRegion && actual.MaximizeRegion && actual.CloseRegion,
                "主页恢复后系统三键命中区未全部恢复");
        }
    }

    internal static void FocusTransfer(double expectedOffset, double actualOffset, bool focusInHeader)
    {
        Check(Finite(expectedOffset, actualOffset) && expectedOffset > 12
            && Math.Abs(expectedOffset - actualOffset) <= 1,
            "隐藏顶部焦点时页面被拉回顶部或丢失滚动位置");
        Check(!focusInHeader, "隐藏后焦点仍留在不可见顶部控件");
    }

    private static bool Finite(params double[] values) => values.All(double.IsFinite);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}

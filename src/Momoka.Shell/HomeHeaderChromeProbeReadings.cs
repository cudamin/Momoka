using Momoka.Shell.Views;

namespace Momoka.Shell;

// Anonymous runtime readings, not a second implementation of the product's scroll state machine.
internal sealed record HomeHeaderChromeReading(
    bool HeaderVisible, bool AccountVisible, bool BackgroundVisible, bool BackgroundHitTest,
    double BackgroundHeight, double BackgroundWidth, double ShellWidth,
    bool BrowseHidden, double NativeHeight, double NativeRightInset,
    int Holes, int PassthroughRegions, bool WholeCaption,
    bool MinimizeRegion, bool MaximizeRegion, bool CloseRegion);

internal sealed record HomeHeaderChromeLayoutReading(
    double RequestedWidth, bool ClientRectRead, int ClientWidthPixels,
    uint WindowDpi, double RasterizationScale, double ShellWidth, double? HomeWidth);

internal sealed record HomeHeaderChromeReturnReading(
    bool CurrentPage, bool ContentReady, bool HomeLoaded, bool ScrollLoaded, bool PresenterLoaded,
    bool HasXamlRoot, bool RestorePending, bool Restoring,
    double RequestedWidth, double HomeWidth, double HomeHeight, double ViewportHeight,
    double ExtentHeight, double ExpectedOffset, double Offset);

internal static class HomeHeaderChromeProbeReadings
{
    internal static bool ReturnReady(HomeHeaderChromeReturnReading actual) =>
        actual.CurrentPage && actual.ContentReady && actual.HomeLoaded && actual.ScrollLoaded
        && actual.PresenterLoaded && actual.HasXamlRoot && !actual.RestorePending && !actual.Restoring
        && Finite(actual.RequestedWidth, actual.HomeWidth, actual.HomeHeight, actual.ViewportHeight,
            actual.ExtentHeight, actual.ExpectedOffset, actual.Offset)
        && actual.RequestedWidth > 0 && Math.Abs(actual.HomeWidth - actual.RequestedWidth) <= 1.5
        && actual.HomeHeight > 0 && actual.ViewportHeight > 0 && actual.ExtentHeight >= actual.ViewportHeight
        && actual.ExpectedOffset > 12 && Math.Abs(actual.Offset - actual.ExpectedOffset) <= 1;

    internal static async Task WaitForReturnAsync(Func<HomeHeaderChromeReturnReading> read,
        Action<HomeHeaderChromeReturnReading> observe, Func<Task> next)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var actual = read();
            observe(actual);
            if (ReturnReady(actual)) return;
            await next();
        }
        throw new TimeoutException("正式返回主页未完成当前页加载、布局或滚动恢复");
    }

    internal static void Layout(HomeHeaderChromeLayoutReading actual, bool fullHome)
    {
        Check(Finite(actual.RequestedWidth, actual.RasterizationScale, actual.ShellWidth)
            && actual.RequestedWidth > 0 && actual.ClientRectRead && actual.ClientWidthPixels > 0
            && actual.WindowDpi > 0 && actual.RasterizationScale > 0
            && Math.Abs(actual.RasterizationScale - actual.WindowDpi / 96.0) <= 0.01,
            "主页顶栏目标宽度缺少有效客户区或 DPI/缩放读数");
        Check(Math.Abs(actual.ClientWidthPixels / actual.RasterizationScale - actual.RequestedWidth) <= 1.5
            && Math.Abs(actual.ShellWidth - actual.RequestedWidth) <= 1.5
            && (!fullHome || actual.HomeWidth is { } homeWidth && double.IsFinite(homeWidth)
                && Math.Abs(homeWidth - actual.RequestedWidth) <= 1.5),
            $"主页顶栏未到达目标宽度：target={actual.RequestedWidth} clientPx={actual.ClientWidthPixels} "
            + $"scale={actual.RasterizationScale} shell={actual.ShellWidth} home={actual.HomeWidth}");
    }

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

internal static class HomeHeaderChromeProbeCleanup
{
    internal static void Run(Action[] detach, Action[] restore, Exception? probeFailure = null)
    {
        var failures = new List<Exception>();
        try { RunSteps(detach); }
        finally { RunSteps(restore); }
        if (failures.Count == 0) return;
        if (probeFailure is not null) failures.Insert(0, probeFailure);
        throw new AggregateException("主页顶栏探针清理失败", failures);

        void RunSteps(Action[] steps)
        {
            foreach (var step in steps)
            {
                try { step(); }
                catch (Exception error) { failures.Add(error); }
            }
        }
    }
}

using Momoka.Shell;
using Momoka.Shell.Interop;

namespace Momoka.Tests;

// Inputs and independently stated expectations only. The diagnostic predicate is extracted unchanged
// from ShellSelfCheck.Fullscreen.cs, never reimplemented here or moved into Core.
internal static class FullscreenContractTests
{
    private static readonly NativeRect Monitor = new() { Left = -1920, Top = -200, Right = 0, Bottom = 880 };
    private const long StyleWithCaption = unchecked((long)0x912345678BCF1357UL);
    private const long StyleWithoutCaption = unchecked((long)0x912345678B0F1357UL);

    internal static void Register()
    {
        TestHarness.Test("诊断契约：演示器收起边框/标题栏，原生 caption 仍在，frame/client 全覆盖", PresenterValid);
        TestHarness.Test("诊断契约：回退模式忽略演示器与样式，只说几何", FallbackValid);
        foreach (var edge in new[] { "left", "top", "right", "bottom" })
            foreach (var delta in new[] { -1, 1 })
                TestHarness.Test($"诊断契约：frame.{edge} 偏移 {delta}px 必须失败", () => FrameOffsetFails(edge, delta));
        foreach (var edge in new[] { "left", "top", "right", "bottom" })
            foreach (var delta in new[] { -1, 1 })
                TestHarness.Test($"诊断契约：client.{edge} 偏移 {delta}px 必须失败", () => ClientOffsetFails(edge, delta));
        TestHarness.Test("诊断契约：客户区尺寸相等但整体偏移（ClientToScreen 原点错误）必须失败", OffsetClient);
        TestHarness.Test("诊断契约：frame 全覆盖但 client 少 7px 必须失败", IncompleteClient);
        foreach (var value in new[] { (0, 10), (10, 0), (-1, 10), (10, -1) })
            TestHarness.Test($"诊断契约：显示器非正尺寸 {value} 必须失败", () => InvalidMonitor(value.Item1, value.Item2));
        foreach (var presenter in new[] { true, false })
            foreach (var value in new[] { (1, 0), (32, 0), (0, 1), (0, 138), (-1, 0), (0, -1) })
                TestHarness.Test($"诊断契约：{(presenter ? "演示器" : "回退")}非零 Height/RightInset {value} 必须失败",
                    () => MetricsFail(presenter, value.Item1, value.Item2));
        foreach (var chrome in new[] { (true, false), (false, true), (true, true) })
            TestHarness.Test($"诊断契约：演示器仍报边框={chrome.Item1}/标题栏={chrome.Item2} 必须失败",
                () => PresenterChromeFails(chrome.Item1, chrome.Item2));
        TestHarness.Test("诊断契约：演示器模式下 WsCaption 掩码为 0 必须失败", PresenterStyleFails);
        foreach (var caption in new[] { 0x00400000L, 0x00800000L })
            TestHarness.Test($"诊断契约：演示器模式下 caption 掩码 0x{caption:X8} 非零即接受", () => PresenterPartialCaption(caption));
        TestHarness.Test("诊断契约：回退模式忽略演示器状态但几何仍必须全覆盖", FallbackStillNeedsGeometry);
        TestHarness.Test("真实 HostWindow custom/fallback 控制流输出满足新诊断契约（client 用监视器夹具，不冒充实测）",
            ControlFlowMatches);
    }

    private static bool Matches(NativeRect frame, NativeRect client, NativeRect monitor,
        bool presenterChrome = true, bool border = false, bool titleBar = false,
        long fullStyle = StyleWithCaption, int height = 0, int inset = 0) =>
        ShellSelfCheck.FullscreenFrameMatches(frame, client, monitor, presenterChrome,
            (border, titleBar), fullStyle, height, inset);

    private static void PresenterValid() => Assert.True(Matches(Monitor, Monitor, Monitor));

    private static void FallbackValid() => Assert.True(Matches(Monitor, Monitor, Monitor,
        presenterChrome: false, border: true, titleBar: true, fullStyle: StyleWithoutCaption),
        "回退模式不核演示器与样式");

    private static void FrameOffsetFails(string edge, int delta)
    {
        var frame = Monitor;
        switch (edge)
        {
            case "left": frame.Left += delta; break;
            case "top": frame.Top += delta; break;
            case "right": frame.Right += delta; break;
            case "bottom": frame.Bottom += delta; break;
        }
        Assert.False(Matches(frame, Monitor, Monitor));
    }

    private static void ClientOffsetFails(string edge, int delta)
    {
        var client = Monitor;
        switch (edge)
        {
            case "left": client.Left += delta; break;
            case "top": client.Top += delta; break;
            case "right": client.Right += delta; break;
            case "bottom": client.Bottom += delta; break;
        }
        Assert.False(Matches(Monitor, client, Monitor));
    }

    private static void OffsetClient()
    {
        var client = Monitor;
        client.Top += 7;
        client.Bottom += 7;
        Assert.Equal(Monitor.Height, client.Height);
        Assert.False(Matches(Monitor, client, Monitor));
    }

    private static void IncompleteClient()
    {
        var client = Monitor;
        client.Bottom -= 7;
        Assert.Equal(Monitor.Height - 7, client.Height);
        Assert.False(Matches(Monitor, client, Monitor));
    }

    private static void InvalidMonitor(int width, int height)
    {
        var monitor = new NativeRect { Left = 20, Top = 30, Right = 20 + width, Bottom = 30 + height };
        Assert.False(Matches(monitor, monitor, monitor));
    }

    private static void MetricsFail(bool presenter, int height, int inset) => Assert.False(
        Matches(Monitor, Monitor, Monitor, presenterChrome: presenter,
            border: !presenter, titleBar: !presenter,
            fullStyle: presenter ? StyleWithCaption : StyleWithoutCaption, height: height, inset: inset));

    private static void PresenterChromeFails(bool border, bool titleBar) =>
        Assert.False(Matches(Monitor, Monitor, Monitor, border: border, titleBar: titleBar));

    private static void PresenterStyleFails() =>
        Assert.False(Matches(Monitor, Monitor, Monitor, fullStyle: StyleWithoutCaption));

    private static void PresenterPartialCaption(long caption)
    {
        Assert.True(Matches(Monitor, Monitor, Monitor,
            fullStyle: (StyleWithoutCaption & ~(long)Native.WsCaption) | caption));
    }

    private static void FallbackStillNeedsGeometry()
    {
        Assert.True(Matches(Monitor, Monitor, Monitor,
            presenterChrome: false, border: true, titleBar: true, fullStyle: 0));
        var frame = Monitor;
        frame.Right -= 1;
        Assert.False(Matches(frame, Monitor, Monitor,
            presenterChrome: false, border: true, titleBar: true, fullStyle: 0));
    }

    private static void ControlFlowMatches()
    {
        foreach (var boundary in Enum.GetValues<TitleBarBoundary>())
        {
            using var f = new FullscreenFixture(boundary);
            f.Window.Fullscreen = true;
            var metrics = f.Window.TitleBarMetrics;
            var presenterChrome = f.Window.UsesPresenterChrome;
            var chrome = f.Window.PresenterChrome;
            var custom = boundary == TitleBarBoundary.Custom;
            Assert.Equal(custom, presenterChrome, "只有自定义标题栏边界由演示器收起边框");
            Assert.Equal(custom ? (false, false) : (true, true), chrome);
            Assert.True(Matches(f.State.Bounds, f.State.Monitor, f.State.Monitor,
                presenterChrome, chrome.Border, chrome.TitleBar, f.State.Style,
                (int)metrics.Height, (int)metrics.RightInset),
                "只将 style/presenter/metrics 输出接到 predicate；client 输入是独立的监视器期望值");
        }
    }
}

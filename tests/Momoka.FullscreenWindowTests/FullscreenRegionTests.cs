using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Momoka.Shell.Interop;
using Momoka.Shell.Windowing;
using Windows.Graphics;

namespace Momoka.Tests;

internal static class FullscreenRegionTests
{
    internal static void Register()
    {
        TestHarness.Test("全屏浏览意图 false：五类区域每次直写，caption/三按钮仅 [Empty]", FullscreenBrowseRegions);
        TestHarness.Test("全屏播放切回 false：仍 Collapsed 且 caption/三按钮仅 [Empty]", FullscreenFalseIntent);
        foreach (var full in new[] { false, true })
            TestHarness.Test($"播放意图区域（Fullscreen={full}）不受原浏览洞影响", () => PlaybackRegions(full));
        foreach (var width in new[] { 0, -10 })
            TestHarness.Test($"全屏零/负客户宽 {width}：五类空声明仍 [Empty] 非 []", () => EmptyFullscreenRegions(width));
        foreach (var gate in new[] { "zeroHandle", "noNonClient" })
            TestHarness.Test($"区域早退 {gate}：零 client/dpi 读取，零区域写", () => RegionEarlyReturn(gate));
        TestHarness.Test("浏览 DPI=0 用 96：caption 和三系统按钮明确分区", ZeroDpi);
        TestHarness.Test("浏览高 DPI 和标题栏 inset 向上取整：按钮最后一列吃余数", LargeInset);
        TestHarness.Test("真实 HolePixels 分数缩放向外取整并裁剪上下左右边", FractionalHoles);
        TestHarness.Test("真实 CaptionAround 无洞保留整带，排序洞后输出上下片与间隙", CaptionPieces);
        foreach (var right in new[] { 0, -1 })
            TestHarness.Test($"caption dragRight={right} 返回 [Empty]，不撤销区域", () => EmptyCaption(right, 32));
        foreach (var height in new[] { 0, -1 })
            TestHarness.Test($"caption height={height} 返回 [Empty]，不撤销区域", () => EmptyCaption(100, height));
        foreach (var value in new[] { (0, 32), (-1, 32), (2, 32), (139, 0), (139, -1) })
            TestHarness.Test($"系统按钮无有效列 {value} 三颗都 Empty", () => EmptyButtons(value.Item1, value.Item2));
        TestHarness.Test("真实系统按钮 139px 分成 46/46/47，最后一列不丢像素", ButtonRemainder);
        TestHarness.Test("标题栏 intent 来回切换直接覆盖区域，不先清空再恢复", NoWithdrawOnTransition);
        TestHarness.Test("浏览无洞 Passthrough=[] 的现存契约保留；caption/三按钮非 []", BrowsingEmptyPassthrough);
    }

    private static void FullscreenBrowseRegions()
    {
        using var f = new FullscreenFixture();
        f.Window.SetTitleBarHoles((0, 0, 120, 32), (1000, 0, 60, 32));
        f.State.ClearTrace();
        f.Window.Fullscreen = true;
        Assert.False(f.Window.PlaybackTitleBar);
        Assert.Equal(TitleBarHeightOption.Collapsed, f.Bar!.PreferredHeightOption);
        f.ExpectContentRegions(new(0, 0, 1280, 32));
        Assert.True(f.State.Calls.Where(call => call.Name.StartsWith("SetRegionRects:", StringComparison.Ordinal))
            .All(call => call.Fullscreen), "full predicate 必须在区域写前生效");
    }

    private static void FullscreenFalseIntent()
    {
        using var f = new FullscreenFixture();
        f.Window.PlaybackTitleBar = true;
        f.Window.Fullscreen = true;
        var style = f.State.Style;
        f.State.ClearTrace();
        f.Window.PlaybackTitleBar = false;
        Assert.False(f.Window.PlaybackTitleBar);
        Assert.True(f.Window.Fullscreen);
        Assert.Equal(TitleBarHeightOption.Collapsed, f.Bar!.PreferredHeightOption);
        Assert.Equal(style, f.State.Style, "只改最新意图，不重新剥样式");
        f.ExpectContentRegions(new(0, 0, 1280, 32));
        f.State.ClearTrace();
        f.Window.RefreshRegions();
        f.ExpectContentRegions(new(0, 0, 1280, 32));
    }

    private static void PlaybackRegions(bool fullscreen)
    {
        using var f = new FullscreenFixture();
        f.Window.SetTitleBarHoles((0, 0, 1142, 32));
        f.Window.PlaybackTitleBar = true;
        if (fullscreen) f.Window.Fullscreen = true;
        f.State.ClearTrace();
        f.Window.RefreshRegions();
        f.ExpectContentRegions(new(0, 0, 1280, 32));
        Assert.Equal(1, f.Window.HoleCount, "被隐藏的洞意图不被清空");
    }

    private static void EmptyFullscreenRegions(int width)
    {
        using var f = new FullscreenFixture();
        f.State.Client = new NativeRect { Left = 0, Top = 0, Right = width, Bottom = 720 };
        f.State.ClearTrace();
        f.Window.Fullscreen = true;
        f.ExpectContentRegions(new(0, 0, 0, 0));
    }

    private static void RegionEarlyReturn(string gate)
    {
        using var f = new FullscreenFixture();
        f.Window.PlaybackTitleBar = true;
        if (gate == "zeroHandle") f.Window.SetCreated(false);
        else f.Window.SetNonClient(null);
        f.State.ClearTrace();
        f.Window.RefreshRegions();
        Assert.Equal(0, f.State.Calls.Count);
        Assert.Equal(0, f.State.Reads);
        Assert.Equal(0, f.State.RegionWrites.Count);
        f.Window.PlaybackTitleBar = false;
        Assert.Equal(0, f.State.RegionWrites.Count, "intent setter 可以写 bar，但不写区域");
        Assert.False(f.State.Calls.Any(call => call.Name is "GetClientRect" or "GetDpiForWindow"));
    }

    private static void ZeroDpi()
    {
        using var f = new FullscreenFixture();
        f.State.Dpi = 0;
        f.State.Client = new NativeRect { Left = 0, Top = 0, Right = 500, Bottom = 400 };
        f.Window.RefreshRegions();
        f.ExpectRegion(NonClientRegionKind.Caption, new RectInt32(0, 0, 362, 32));
        f.ExpectRegion(NonClientRegionKind.Minimize, new RectInt32(362, 0, 46, 32));
        f.ExpectRegion(NonClientRegionKind.Maximize, new RectInt32(408, 0, 46, 32));
        f.ExpectRegion(NonClientRegionKind.Close, new RectInt32(454, 0, 46, 32));
        Assert.Equal(5, f.State.RegionWrites.Count);
    }

    private static void LargeInset()
    {
        using var f = new FullscreenFixture();
        f.State.Dpi = 144;
        f.State.Client = new NativeRect { Left = 0, Top = 0, Right = 1000, Bottom = 600 };
        f.Bar!.StandardInset = 250.2;
        f.Window.RefreshRegions();
        f.ExpectRegion(NonClientRegionKind.Caption, new RectInt32(0, 0, 749, 48));
        f.ExpectRegion(NonClientRegionKind.Minimize, new RectInt32(749, 0, 83, 48));
        f.ExpectRegion(NonClientRegionKind.Maximize, new RectInt32(832, 0, 83, 48));
        f.ExpectRegion(NonClientRegionKind.Close, new RectInt32(915, 0, 85, 48));
    }

    private static void FractionalHoles()
    {
        using var f = new FullscreenFixture();
        f.Window.SetTitleBarHoles((1.1, 2.2, 10.1, 5.1), (-10, -10, 20, 20),
            (70, 20, 20, 20), (200, 200, 10, 10), (2, 3, -1, 4), null);
        Assert.Equal(4, f.Window.HoleCount, "SetTitleBarHoles 的真实正面积过滤保留");
        var holes = f.Window.PixelHoles(120, 100, 32);
        Assert.True(holes.SequenceEqual(new RectInt32[] { new(1, 2, 13, 8), new(0, 0, 13, 13), new(87, 25, 13, 7) }),
            "以手算坐标检验实际 HolePixels，不在测试重写 clipping");
        Assert.Equal(0, f.Window.PixelHoles(120, 0, 32).Length);
        Assert.Equal(0, f.Window.PixelHoles(120, 100, 0).Length);
    }

    private static void CaptionPieces()
    {
        var whole = HostWindow.Caption([], 100, 32);
        Assert.True(whole.SequenceEqual(new RectInt32[] { new(0, 0, 100, 32) }));
        var pieces = HostWindow.Caption([new(60, 0, 20, 32), new(10, 5, 20, 10)], 100, 32);
        Assert.True(pieces.SequenceEqual(new RectInt32[] { new(0, 0, 10, 32), new(10, 0, 20, 5),
            new(10, 15, 20, 17), new(30, 0, 30, 32), new(80, 0, 20, 32) }),
            "乱序洞的真实分片应按左沿排序");
    }

    private static void EmptyCaption(int right, int height)
    {
        var result = HostWindow.Caption([], right, height);
        Assert.Equal(1, result.Length, "不能用 [] 清撤 caption 声明");
        Assert.Equal(new RectInt32(0, 0, 0, 0), result[0]);
    }

    private static void EmptyButtons(int reserved, int height)
    {
        var (minimize, maximize, close) = HostWindow.Buttons(100, reserved, height);
        var empty = new RectInt32(0, 0, 0, 0);
        Assert.Equal((empty, empty, empty), (minimize, maximize, close));
    }

    private static void ButtonRemainder()
    {
        var result = HostWindow.Buttons(17, 139, 32);
        Assert.Equal((new RectInt32(17, 0, 46, 32), new RectInt32(63, 0, 46, 32), new RectInt32(109, 0, 47, 32)), result);
    }

    private static void NoWithdrawOnTransition()
    {
        using var f = new FullscreenFixture();
        f.Window.SetTitleBarHoles((0, 0, 40, 32));
        f.State.ClearTrace();
        f.Window.PlaybackTitleBar = true;
        f.Window.PlaybackTitleBar = false;
        Assert.Equal(10, f.State.RegionWrites.Count);
        for (var start = 0; start < f.State.RegionWrites.Count; start += 5)
        {
            Assert.True(f.State.RegionWrites.Skip(start).Take(5).Select(write => write.Kind)
                .SequenceEqual(new[] { NonClientRegionKind.Passthrough, NonClientRegionKind.Caption,
                    NonClientRegionKind.Minimize, NonClientRegionKind.Maximize, NonClientRegionKind.Close }));
        }
        Assert.True(f.State.RegionWrites.All(write => write.Rects.Length > 0),
            "有实际洞/隐藏声明时每类都直接写有效数组，不先撤销再构建");
        f.ExpectRegion(NonClientRegionKind.Passthrough, new RectInt32(0, 0, 40, 32));
        f.ExpectRegion(NonClientRegionKind.Caption, new RectInt32(40, 0, 1102, 32));
    }

    private static void BrowsingEmptyPassthrough()
    {
        using var f = new FullscreenFixture();
        f.Window.RefreshRegions();
        f.ExpectRegion(NonClientRegionKind.Passthrough);
        Assert.True(f.State.RegionWrites.Where(write => write.Kind != NonClientRegionKind.Passthrough)
            .All(write => write.Rects.Length == 1), "不把真实无洞 passthrough 改成测试自造 [Empty]");
    }
}

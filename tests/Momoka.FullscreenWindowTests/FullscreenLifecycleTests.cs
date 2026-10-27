using Microsoft.UI.Windowing;
using Momoka.Playback;
using Momoka.Shell.Interop;
using Windows.Graphics;

namespace Momoka.Tests;

internal static class FullscreenLifecycleTests
{
    private static readonly TitleBarBoundary[] FallbackBoundaries =
    [
        TitleBarBoundary.NoAppWindow, TitleBarBoundary.NoTitleBar,
        TitleBarBoundary.NotExtended, TitleBarBoundary.NoNonClient
    ];

    internal static void Register()
    {
        foreach (var playback in new[] { false, true })
        {
            TestHarness.Test($"自定义标题栏{Mode(playback)}进出全屏：先收标题栏再收演示器边框，退出按演示器→样式→模式还原",
                () => CustomRoundTrip(playback));
            TestHarness.Test($"自定义标题栏{Mode(playback)}进入：先保存全屏、Collapsed 和输入区，再 SetBorderAndTitleBar",
                () => EnterOrder(playback));
            TestHarness.Test($"全屏覆盖{Mode(playback)}意图但不改意图与标题栏洞", () => IntentUntouched(playback));
            TestHarness.Test($"退出全屏按最新{Mode(playback)}意图更新标题栏", () => LatestIntent(playback));
        }
        foreach (var boundary in FallbackBoundaries)
            foreach (var playback in new[] { false, true })
                TestHarness.Test($"回退 {boundary}/{Mode(playback)}：样式移除 caption 与 thickframe，其他样式和几何还原",
                    () => FallbackRoundTrip(boundary, playback));
        TestHarness.Test("回退路径不调用 SetBorderAndTitleBar：UsesPresenterChrome=false 的四个边界",
            FallbackNeverTouchesPresenter);
        TestHarness.Test("回退路径：caption 的两位与 thickframe 一并移除并完整还原", FallbackFrameBits);
        TestHarness.Test("全屏重复进出多轮后 PresenterChrome 每轮回到 (true,true) 且样式/几何不漂移",
            PresenterChromeReturnsEveryRound);
        TestHarness.Test("演示器桩拒绝无边框但有标题栏的非法组合", PresenterRejectsInvalidCombination);
        TestHarness.Test("最大化：先 Restore 再保存正常框，退出先还框/任务栏再 Maximize", MaximizedOrder);
        TestHarness.Test("最大化且恢复浏览：使用浏览矩形，不执行 Maximize 或 Fit", BrowseRestoreFromMaximized);
        TestHarness.Test("恢复浏览：四条边采用保存的浏览框，不用全屏前播放框", BrowseRestoreGeometry);
        TestHarness.Test("恢复浏览无有效几何：退回保存的全屏前框", EmptyBrowseRestore);
        foreach (var topMost in new[] { false, true })
            foreach (var aside in new[] { false, true })
                TestHarness.Test($"退出保留当前 z-order 意图：topMost={topMost}, aside={aside}",
                    () => ZOrderIntent(topMost, aside));
        TestHarness.Test("Fullscreen 重复 true/false 幂等且不覆盖 restore", Idempotence);
        TestHarness.Test("零句柄 Fullscreen true/false：零读取、零写入、零转换", ZeroHandle);
        foreach (var freeSizing in new[] { false, true })
        {
            TestHarness.Test($"EnterFullscreen 显示器读取失败零修改（FreeSizing={freeSizing}）", () => MonitorFailure(freeSizing));
            TestHarness.Test($"Fullscreen setter 显示器失败只经过转换观察括号，生命周期零写（FreeSizing={freeSizing}）",
                () => SetterMonitorFailure(freeSizing));
            foreach (var point in new[] { "ShowRestore", "GetWindowLongPtr", "GetWindowRect", "PreferredHeightOption",
                         "SetRegionRects:Caption", "SetBorderAndTitleBar", "SetWindowPos" })
                TestHarness.Test($"异常 finally 恢复 FreeSizing={freeSizing}，故障点 {point}",
                    () => RestoreFreeSizingOnFailure(point, freeSizing));
        }
        TestHarness.Test("冷播放意图：句柄/标题栏创建后 Configure 仍 Collapsed", ColdPlaybackIntent);
        TestHarness.Test("冷全屏意图：之后 Configure 按 Fullscreen Collapsed，不改浏览意图", ColdFullscreenIntent);
        TestHarness.Test("Configure 标题栏失败：保留播放意图并退回无自定义边界", ConfigureFailure);
        TestHarness.Test("重复 PlaybackTitleBar 意图不写标题栏或区域", SameIntent);
    }

    private static string Mode(bool playback) => playback ? "播放" : "浏览";

    private static void CustomRoundTrip(bool playback)
    {
        using var f = new FullscreenFixture();
        f.Window.PlaybackTitleBar = playback;
        var bounds = f.State.Bounds;
        var style = f.State.Style;
        var metrics = f.Window.TitleBarMetrics;
        f.State.ClearTrace();
        f.Window.Fullscreen = true;
        Assert.True(f.Window.Fullscreen);
        Assert.True(f.Window.UsesPresenterChrome);
        Assert.Equal((false, false), f.Window.PresenterChrome, "进入全屏后演示器报告无边框无标题栏");
        Assert.Equal((false, false), f.State.PresenterWrites.Single());
        Assert.Equal(0, f.State.StyleWrites.Count, "演示器路径不得直接写 GWL_STYLE");
        Assert.Equal(-1, f.Index("SetWindowLongPtr"), "进入全屏不得直接写 GWL_STYLE");
        Assert.Equal(style, f.State.Style, "经典样式位原样不动，WS_CAPTION 留给原生标题栏对象");
        Assert.Equal(TitleBarHeightOption.Collapsed, f.Bar!.PreferredHeightOption);
        Assert.Equal((0.0, 0.0), f.Window.TitleBarMetrics);
        Assert.Equal(playback, f.Window.PlaybackTitleBar);
        Assert.NotNull(f.Window.Saved);
        var saved = f.Window.Saved!.Value;
        FullscreenFixture.EqualRect(bounds, saved.Bounds);
        Assert.Equal(style, saved.Style.ToInt64());
        Assert.False(saved.Maximized);
        FullscreenFixture.EqualRect(f.State.Monitor, f.State.Bounds, "覆盖整个 Monitor 而非 Work");
        Assert.Equal(1, f.State.PositionWrites.Count);
        Assert.Equal(Native.HwndTopMost, f.State.PositionWrites[0].InsertAfter);
        Assert.Equal(Native.SwpFrameChanged | Native.SwpNoActivate | Native.SwpNoCopyBits, f.State.PositionWrites[0].Flags);
        Assert.True(f.State.TrayHidden && f.State.MarkedFullscreen && f.State.TrayWatching && f.State.BandTimerRunning);
        Assert.True(f.Window.Band);

        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.False(f.Window.Fullscreen);
        Assert.Null(f.Window.Saved);
        Assert.Equal((true, true), f.Window.PresenterChrome, "退出后演示器把边框与标题栏还回来");
        Assert.Equal((true, true), f.State.PresenterWrites.Single());
        Assert.Equal(1, f.State.StyleWrites.Count, "退出只写一次 saved style");
        Assert.Equal(style, f.State.StyleWrites.Single());
        Assert.Equal(style, f.State.Style, "完整恢复初始 style");
        FullscreenFixture.EqualRect(bounds, f.State.Bounds, "完整恢复 Left/Top/Right/Bottom");
        Assert.Equal(metrics, f.Window.TitleBarMetrics, "原模式 metrics 原样恢复");
        Assert.Equal(playback ? TitleBarHeightOption.Collapsed : TitleBarHeightOption.Standard, f.Bar.PreferredHeightOption);
        Assert.False(f.State.TrayHidden || f.State.MarkedFullscreen || f.State.TrayWatching || f.State.BandTimerRunning);
        Assert.False(f.Window.Band);
        Assert.Equal(IntPtr.Zero, f.Window.Judged);
        Assert.Equal(1, f.State.PositionWrites.Count);
        f.Before("UnmarkFullscreen", "SetBorderAndTitleBar");
        f.Before("SetBorderAndTitleBar", "SetWindowLongPtr");
        f.Before("SetWindowLongPtr", "PreferredHeightOption");
        f.Before("PreferredHeightOption", "SetWindowPos");
        f.Before("SetWindowPos", "ApplyWindowsChrome");
        f.Before("ApplyWindowsChrome", "RestoreTray");
        f.Before("RestoreTray", "FitToPicture");
        Assert.True(f.State.Calls.Where(call => call.Name is "KillTimer" or "PreferredHeightOption" or "ApplyWindowsChrome")
            .All(call => !call.Fullscreen), "leave 必须先清 _restore，再恢复标准显示/圆角");
    }

    private static void EnterOrder(bool playback)
    {
        using var f = new FullscreenFixture();
        f.Window.PlaybackTitleBar = playback;
        f.Window.TrayAhead = true;
        f.Window.Judged = new IntPtr(789);
        f.State.ClearTrace();
        f.Window.Fullscreen = true;
        Assert.False(f.Window.TrayAhead);
        Assert.Equal(IntPtr.Zero, f.Window.Judged);
        f.Before("GetWindowLongPtr", "PreferredHeightOption");
        f.Before("GetWindowRect", "PreferredHeightOption");
        f.Before("PreferredHeightOption", "SetBorderAndTitleBar");
        f.Before("SetRegionRects:Close", "SetBorderAndTitleBar");
        f.Before("SetBorderAndTitleBar", "HideTrayForFullscreen");
        f.Before("HideTrayForFullscreen", "SetWindowPos");
        f.Before("SetWindowPos", "ApplyWindowsChrome");
        f.Before("ApplyWindowsChrome", "MarkFullscreen");
        f.Before("MarkFullscreen", "SetTimer");
        f.Before("SetTimer", "WatchTrayOrder");
        Assert.True(f.Call("PreferredHeightOption").Fullscreen, "先建立 _restore 后才进入标题栏 setter");
        Assert.Equal(playback ? TitleBarHeightOption.Collapsed : TitleBarHeightOption.Standard,
            f.Call("PreferredHeightOption").HeightOption, "调用轨迹记录的是写入前的显示高度");
        Assert.True(f.Call("SetBorderAndTitleBar").Fullscreen);
        Assert.Equal(TitleBarHeightOption.Collapsed, f.Call("SetBorderAndTitleBar").HeightOption,
            "演示器收起边框时 ApplyTitleBarMode 已把标题栏收成 Collapsed");
        Assert.True(f.Call("SetBorderAndTitleBar").FreeSizing);
        Assert.False(f.Call("SetWindowPos").FreeSizing, "finally 恢复后才进行铺屏");
        Assert.Equal(0, f.State.StyleWrites.Count);
        Assert.Equal(-1, f.Index("SetWindowLongPtr"));
        Assert.True(f.Call("ApplyWindowsChrome").Fullscreen);
        Assert.Equal(0, f.Window.TransitionDepth);
        Assert.True(f.State.FrameTransitionsEnabled);
        Assert.Equal(1, f.State.PublishedBefore.Count);
        Assert.Equal(1, f.State.PublishedAfter.Count);
    }

    private static void FallbackRoundTrip(TitleBarBoundary boundary, bool playback)
    {
        using var f = new FullscreenFixture(boundary);
        f.Window.PlaybackTitleBar = playback;
        var original = f.State.Style;
        var bounds = f.State.Bounds;
        f.State.ClearTrace();
        Assert.False(f.Window.UsesPresenterChrome);
        f.Window.Fullscreen = true;
        Assert.Equal(0L, f.State.Style & Native.WsCaption);
        Assert.Equal(0L, f.State.Style & Native.WsThickFrame);
        Assert.Equal(original & ~((long)Native.WsCaption | Native.WsThickFrame), f.State.Style);
        Assert.Equal(0, f.State.PresenterWrites.Count, "回退路径不碰演示器");
        Assert.Equal((true, true), f.Window.PresenterChrome, "没有演示器时按有边框有标题栏算");
        FullscreenFixture.EqualRect(f.State.Monitor, f.State.Bounds);
        Assert.Equal(playback, f.Window.PlaybackTitleBar);
        if (boundary == TitleBarBoundary.NoNonClient) Assert.Equal(0, f.State.RegionWrites.Count);
        var stripped = f.State.Style;
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.Equal(original, f.State.Style);
        Assert.Equal(1, f.State.StyleWrites.Count);
        Assert.Equal(stripped, f.Call("SetWindowLongPtr").Style);
        Assert.Equal(0, f.State.PresenterWrites.Count, "退出也不碰演示器");
        FullscreenFixture.EqualRect(bounds, f.State.Bounds);
        Assert.Equal(playback, f.Window.PlaybackTitleBar);
    }

    private static void FallbackNeverTouchesPresenter()
    {
        foreach (var boundary in FallbackBoundaries)
        {
            using var f = new FullscreenFixture(boundary);
            Assert.False(f.Window.UsesPresenterChrome, boundary + " 必须是回退路径");
            f.State.ClearTrace();
            f.Window.Fullscreen = true;
            Assert.Equal(0, f.State.PresenterWrites.Count);
            Assert.Equal(-1, f.Index("SetBorderAndTitleBar"));
            f.State.ClearTrace();
            f.Window.Fullscreen = false;
            Assert.Equal(0, f.State.PresenterWrites.Count);
            Assert.Equal(-1, f.Index("SetBorderAndTitleBar"));
        }
    }

    private static void FallbackFrameBits()
    {
        var baseStyle = unchecked((long)0xD123456789001357UL);
        foreach (var caption in new[] { 0L, 0x00400000L, 0x00800000L, 0x00C00000L })
        {
            using var f = new FullscreenFixture(TitleBarBoundary.NoAppWindow);
            var original = baseStyle | caption | Native.WsThickFrame;
            f.SetStyle(original);
            f.Window.Fullscreen = true;
            Assert.Equal(baseStyle, f.State.Style, $"caption 0x{caption:X8} 与 thickframe 必须一并移除，其他位原样");
            f.Window.Fullscreen = false;
            Assert.Equal(original, f.State.Style);
        }
    }

    private static void PresenterChromeReturnsEveryRound()
    {
        using var f = new FullscreenFixture();
        var bounds = f.State.Bounds;
        var style = f.State.Style;
        for (var round = 0; round < 3; round++)
        {
            f.State.ClearTrace();
            f.Window.Fullscreen = true;
            Assert.Equal((false, false), f.Window.PresenterChrome, $"第 {round} 轮全屏中");
            Assert.Equal((false, false), f.State.PresenterWrites.Single(), $"第 {round} 轮 enter");
            Assert.Equal(style, f.State.Style);

            f.State.ClearTrace();
            f.Window.Fullscreen = false;
            Assert.Equal((true, true), f.Window.PresenterChrome, $"第 {round} 轮退出后");
            Assert.Equal((true, true), f.State.PresenterWrites.Single(), $"第 {round} 轮 exit");
            Assert.Equal(style, f.State.Style);
            FullscreenFixture.EqualRect(bounds, f.State.Bounds);
        }
    }

    private static void PresenterRejectsInvalidCombination()
    {
        var state = new NativeState();
        var presenter = new OverlappedPresenter(state);
        Assert.Equal(true, presenter.HasBorder);
        Assert.Equal(true, presenter.HasTitleBar);
        Assert.Throws<InvalidOperationException>(() => presenter.SetBorderAndTitleBar(false, true));
        Assert.Equal(0, state.PresenterWrites.Count);
        presenter.SetBorderAndTitleBar(false, false);
        presenter.SetBorderAndTitleBar(true, true);
        Assert.Equal((true, true), (presenter.HasBorder, presenter.HasTitleBar));
        Assert.Equal(2, state.PresenterWrites.Count);
    }

    private static void IntentUntouched(bool playback)
    {
        using var f = new FullscreenFixture();
        f.Window.SetTitleBarHoles((2.2, 3.3, 40.4, 20.5), (900, 0, 40, 32));
        f.Window.PlaybackTitleBar = playback;
        var holes = f.Window.StoredHoles;
        f.State.ClearTrace();
        f.Window.Fullscreen = true;
        Assert.Equal(playback, f.Window.PlaybackTitleBar);
        Assert.True(holes.SequenceEqual(f.Window.StoredHoles), "隐藏不能清空洞/偏好");
        f.Window.Fullscreen = false;
        Assert.Equal(playback, f.Window.PlaybackTitleBar);
        Assert.True(holes.SequenceEqual(f.Window.StoredHoles));
    }

    private static void LatestIntent(bool latestPlayback)
    {
        using var f = new FullscreenFixture();
        f.Window.PlaybackTitleBar = !latestPlayback;
        f.Window.Fullscreen = true;
        f.State.ClearTrace();
        f.Window.PlaybackTitleBar = latestPlayback;
        Assert.True(f.Window.Fullscreen);
        Assert.Equal(latestPlayback, f.Window.PlaybackTitleBar);
        Assert.Equal(TitleBarHeightOption.Collapsed, f.Bar!.PreferredHeightOption);
        Assert.Equal((0.0, 0.0), f.Window.TitleBarMetrics);
        f.ExpectContentRegions(new(0, 0, 1280, 32));
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.Equal(latestPlayback, f.Window.PlaybackTitleBar);
        Assert.Equal(latestPlayback ? TitleBarHeightOption.Collapsed : TitleBarHeightOption.Standard, f.Bar.PreferredHeightOption);
        Assert.Equal(latestPlayback ? (0.0, 0.0) : (32.0, 138.0), f.Window.TitleBarMetrics);
        if (latestPlayback) f.ExpectContentRegions(new(0, 0, 1280, 32));
        else
        {
            f.ExpectRegion(Microsoft.UI.Input.NonClientRegionKind.Caption, new RectInt32(0, 0, 1142, 32));
            f.ExpectRegion(Microsoft.UI.Input.NonClientRegionKind.Minimize, new RectInt32(1142, 0, 46, 32));
            f.ExpectRegion(Microsoft.UI.Input.NonClientRegionKind.Maximize, new RectInt32(1188, 0, 46, 32));
            f.ExpectRegion(Microsoft.UI.Input.NonClientRegionKind.Close, new RectInt32(1234, 0, 46, 32));
        }
    }

    private static void MaximizedOrder()
    {
        using var f = new FullscreenFixture();
        f.State.Maximized = true;
        f.State.Bounds = f.State.MaximizedBounds;
        f.State.Style = f.State.MaximizedStyle;
        var restored = f.State.RestoredBounds;
        var normalStyle = f.State.RestoredStyle;
        f.Window.Fullscreen = true;
        f.Before("ShowRestore", "GetWindowLongPtr");
        f.Before("ShowRestore", "GetWindowRect");
        Assert.True(f.Call("ShowRestore").FreeSizing);
        Assert.False(f.Call("ShowRestore").Fullscreen);
        var saved = f.Window.Saved!.Value;
        Assert.True(saved.Maximized);
        FullscreenFixture.EqualRect(restored, saved.Bounds, "保存 restore 后正常矩形，不是最大化矩形");
        Assert.Equal(normalStyle, saved.Style.ToInt64());
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        f.Before("SetBorderAndTitleBar", "SetWindowLongPtr");
        f.Before("SetWindowLongPtr", "SetWindowPos");
        f.Before("SetWindowPos", "RestoreTray");
        f.Before("RestoreTray", "ShowMaximize");
        Assert.Equal(-1, f.Index("FitToPicture"));
        Assert.False(f.Call("ShowMaximize").Fullscreen);
        Assert.Equal(normalStyle, f.Call("ShowMaximize").Style);
        FullscreenFixture.EqualRect(restored, f.Call("ShowMaximize").Bounds);
        FullscreenFixture.EqualRect(f.State.MaximizedBounds, f.State.Bounds);
        Assert.True(f.State.Maximized);
    }

    private static void BrowseRestoreGeometry()
    {
        using var f = new FullscreenFixture();
        f.Window.Fullscreen = true;
        var browse = new WindowBounds(-1800, -90, -710, 651);
        f.Window.SetBrowseRestore(browse, restoring: true);
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.Equal((browse.Left, browse.Top, browse.Right, browse.Bottom),
            (f.State.Bounds.Left, f.State.Bounds.Top, f.State.Bounds.Right, f.State.Bounds.Bottom));
        Assert.Equal(-1, f.Index("FitToPicture"));
        Assert.Equal(-1, f.Index("ShowMaximize"));
    }

    private static void BrowseRestoreFromMaximized()
    {
        using var f = new FullscreenFixture();
        f.State.Maximized = true;
        f.Window.Fullscreen = true;
        var browse = new WindowBounds(1900, 20, 3120, 760);
        f.Window.SetBrowseRestore(browse, restoring: true);
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.Equal((browse.Left, browse.Top, browse.Right, browse.Bottom),
            (f.State.Bounds.Left, f.State.Bounds.Top, f.State.Bounds.Right, f.State.Bounds.Bottom));
        Assert.Equal(-1, f.Index("ShowMaximize"));
        Assert.Equal(-1, f.Index("FitToPicture"));
        Assert.False(f.State.Maximized);
    }

    private static void EmptyBrowseRestore()
    {
        using var f = new FullscreenFixture();
        var before = f.State.Bounds;
        f.Window.Fullscreen = true;
        f.Window.SetBrowseRestore(new WindowBounds(2, 3, 2, 3), restoring: true);
        f.Window.Fullscreen = false;
        FullscreenFixture.EqualRect(before, f.State.Bounds);
    }

    private static void ZOrderIntent(bool topMost, bool aside)
    {
        using var f = new FullscreenFixture();
        f.Window.Fullscreen = true;
        f.Window.SetZOrderIntent(topMost, aside);
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.Equal(topMost && !aside ? Native.HwndTopMost : Native.HwndNoTopMost,
            f.State.PositionWrites.Single().InsertAfter);
    }

    private static void Idempotence()
    {
        using var f = new FullscreenFixture();
        f.Window.Fullscreen = false;
        Assert.Equal(0, f.State.Calls.Count);
        f.Window.Fullscreen = true;
        var saved = f.Window.Saved;
        f.State.ClearTrace();
        f.Window.Fullscreen = true;
        Assert.Equal(0, f.State.Calls.Count);
        Assert.Equal(saved, f.Window.Saved);
        f.Window.Fullscreen = false;
        Assert.Equal(1, f.State.StyleWrites.Count);
        Assert.Equal(1, f.State.PresenterWrites.Count);
        Assert.Equal(1, f.State.PositionWrites.Count);
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.Equal(0, f.State.Calls.Count);
        Assert.Null(f.Window.Saved);
    }

    private static void ZeroHandle()
    {
        using var f = new FullscreenFixture(created: false);
        f.Window.Fullscreen = true;
        f.Window.Fullscreen = false;
        Assert.False(f.Window.Fullscreen);
        Assert.Null(f.Window.Saved);
        Assert.Equal(0, f.State.Reads);
        Assert.Equal(0, f.State.Writes);
        Assert.Equal(0, f.State.Calls.Count);
        Assert.Equal(0, f.State.PresenterWrites.Count);
    }

    private static void MonitorFailure(bool freeSizing)
    {
        using var f = new FullscreenFixture();
        f.Window.FreeSizing = freeSizing;
        f.Window.TrayAhead = true;
        f.Window.Judged = new IntPtr(789);
        f.State.MonitorAvailable = false;
        f.State.Maximized = true;
        var before = f.State.Bounds;
        var style = f.State.Style;
        f.Window.EnterDirect();
        Assert.False(f.Window.Fullscreen);
        Assert.Null(f.Window.Saved);
        Assert.Equal(freeSizing, f.Window.FreeSizing);
        Assert.Equal(style, f.State.Style);
        FullscreenFixture.EqualRect(before, f.State.Bounds);
        Assert.Equal(TitleBarHeightOption.Standard, f.Bar!.PreferredHeightOption);
        Assert.True(f.State.Maximized);
        Assert.True(f.Window.TrayAhead);
        Assert.Equal(new IntPtr(789), f.Window.Judged);
        Assert.Equal(0, f.State.Writes, "EnterFullscreen 在读取显示器失败时不修改 native/titlebar/region/tray/转换括号");
        Assert.Equal(0, f.State.RegionWrites.Count);
        Assert.Equal(0, f.State.PresenterWrites.Count);
        Assert.Equal(-1, f.Index("IsZoomed"));
        Assert.Equal(-1, f.Index("ShowRestore"));
        Assert.Equal(0, f.Window.TransitionDepth);
        Assert.True(f.State.FrameTransitionsEnabled);
        Assert.Equal(-1, f.Index("DisableFrameTransitions"));
        Assert.Equal(0, f.State.PublishedBefore.Count);
    }

    private static void SetterMonitorFailure(bool freeSizing)
    {
        using var f = new FullscreenFixture();
        f.Window.FreeSizing = freeSizing;
        f.State.MonitorAvailable = false;
        var before = f.State.Bounds;
        var style = f.State.Style;
        f.Window.Fullscreen = true;
        Assert.False(f.Window.Fullscreen);
        Assert.Null(f.Window.Saved);
        Assert.Equal(style, f.State.Style);
        FullscreenFixture.EqualRect(before, f.State.Bounds);
        Assert.Equal(0, f.State.Writes, "不发生 lifecycle 端点写入；转换框架端点仍按原 ChangeClientRect 经过");
        Assert.Equal(0, f.State.PresenterWrites.Count);
        Assert.Equal(freeSizing, f.Window.FreeSizing);
        Assert.Equal(0, f.Window.TransitionDepth);
        f.Before("DisableFrameTransitions", "GetMonitorInfo");
        f.Before("GetMonitorInfo", "PublishClientRect");
        f.Before("PublishClientRect", "EnableFrameTransitions");
        Assert.True(f.State.FrameTransitionsEnabled);
        Assert.Equal(1, f.State.PublishedBefore.Count);
        FullscreenFixture.EqualRect(before, f.State.PublishedAfter.Single());
    }

    private static void RestoreFreeSizingOnFailure(string point, bool freeSizing)
    {
        using var f = new FullscreenFixture();
        f.Window.FreeSizing = freeSizing;
        f.State.Maximized = point == "ShowRestore";
        f.State.FailAt = point;
        var error = Assert.Catch<BoundaryFailure>(() => f.Window.Fullscreen = true);
        Assert.Equal(point, error.Message);
        Assert.Equal(freeSizing, f.Window.FreeSizing, "异常不能泄漏临时 FreeSizing=true");
        Assert.Equal(0, f.Window.TransitionDepth);
        Assert.True(f.State.FrameTransitionsEnabled);
        Assert.Equal(1, f.State.PublishedBefore.Count);
        Assert.Equal(1, f.State.PublishedAfter.Count);
        if (point != "SetWindowPos") Assert.True(f.Call(point).FreeSizing);
        else Assert.Equal(freeSizing, f.Call(point).FreeSizing);
        // No rollback claim: production may already hold _restore when an external write throws.
    }

    private static void ColdPlaybackIntent()
    {
        using var f = new FullscreenFixture(TitleBarBoundary.NoAppWindow, created: false);
        f.Window.PlaybackTitleBar = true;
        Assert.True(f.Window.PlaybackTitleBar);
        Assert.Equal(0, f.State.RegionWrites.Count);
        var bar = new AppWindowTitleBar(f.State);
        f.State.ObservedTitleBar = bar;
        f.State.ConfiguredAppWindow = new AppWindow(bar, new OverlappedPresenter(f.State));
        f.Window.SetCreated(true);
        f.State.ClearTrace();
        f.Window.Configure();
        Assert.True(f.Window.PlaybackTitleBar);
        Assert.True(bar.ExtendsContentIntoTitleBar);
        Assert.Equal(TitleBarHeightOption.Collapsed, bar.PreferredHeightOption);
        f.ExpectContentRegions(new(0, 0, 1280, 32));
        Assert.Equal(0, f.State.PresenterWrites.Count);
    }

    private static void ColdFullscreenIntent()
    {
        using var f = new FullscreenFixture(TitleBarBoundary.NoAppWindow);
        f.Window.SetNonClient(null);
        f.Window.Fullscreen = true;
        Assert.False(f.Window.PlaybackTitleBar);
        var bar = new AppWindowTitleBar(f.State);
        f.State.ObservedTitleBar = bar;
        f.State.ConfiguredAppWindow = new AppWindow(bar, new OverlappedPresenter(f.State));
        f.State.ClearTrace();
        f.Window.Configure();
        Assert.True(f.Window.Fullscreen);
        Assert.True(f.Window.UsesPresenterChrome);
        Assert.False(f.Window.PlaybackTitleBar);
        Assert.Equal(TitleBarHeightOption.Collapsed, bar.PreferredHeightOption);
        f.ExpectContentRegions(new(0, 0, 1280, 32));
        f.State.ClearTrace();
        f.Window.Fullscreen = false;
        Assert.Equal(TitleBarHeightOption.Standard, bar.PreferredHeightOption);
        Assert.Equal((true, true), f.Window.PresenterChrome);
        Assert.Equal((true, true), f.State.PresenterWrites.Single());
    }

    private static void ConfigureFailure()
    {
        using var f = new FullscreenFixture();
        f.Window.PlaybackTitleBar = true;
        f.State.ClearTrace();
        f.State.FailAt = "GetForWindowId";
        f.Window.Configure();
        f.State.FailAt = null;
        Assert.True(f.Window.PlaybackTitleBar);
        f.State.ClearTrace();
        f.Window.RefreshRegions();
        Assert.Equal(0, f.State.Calls.Count, "失败后 _nonClient 清空，不写区域");
        f.Window.Fullscreen = true;
        Assert.False(f.Window.UsesPresenterChrome);
        Assert.Equal(0, f.State.PresenterWrites.Count);
        Assert.Equal(0L, f.State.Style & (Native.WsCaption | Native.WsThickFrame));
    }

    private static void SameIntent()
    {
        using var f = new FullscreenFixture();
        f.Window.PlaybackTitleBar = false;
        Assert.Equal(0, f.State.Calls.Count);
        f.Window.PlaybackTitleBar = true;
        f.State.ClearTrace();
        f.Window.PlaybackTitleBar = true;
        Assert.Equal(0, f.State.Calls.Count);
        f.Window.Fullscreen = true;
        f.State.ClearTrace();
        f.Window.PlaybackTitleBar = true;
        Assert.Equal(0, f.State.Calls.Count);
        Assert.Equal(0, f.State.PresenterWrites.Count);
    }
}

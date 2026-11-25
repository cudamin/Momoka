using Momoka.Shell;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomeHeaderChromeProbeTests
{
    private static readonly HomeHeaderChromeReading Visible = new(
        true, true, false, false, 0, 0, 1240, false, 48, 138, 2, 2, false, true, true, true);
    private static readonly HomeHeaderChromeReading Revealed = Visible with
    { BackgroundVisible = true, BackgroundHeight = 48, BackgroundWidth = 1240 };
    private static readonly HomeHeaderChromeReading Hidden = new(
        false, false, false, false, 0, 0, 1240, true, 0, 0, 0, 0, true, false, false, false);
    private static readonly HomeHeaderChromeReturnReading Returned = new(
        true, true, true, true, true, true, false, false, 1240, 1240, 760, 760, 2212, 300, 300);

    internal static void Register()
    {
        Test("主页滚动条读数：原生位置通过，缺件、轮播偏移和非法值拒绝", () =>
        {
            HomeHeaderChromeProbeReadings.ScrollBar(true, 0, 0);
            foreach (var (found, top, right) in new[]
            {
                (false, 0d, 0d), (true, 240d, 0d), (true, 0d, 20d),
                (true, double.NaN, 0d), (true, 0d, double.PositiveInfinity)
            })
                Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.ScrollBar(found, top, right));
            var caller = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.Sidebar.cs"));
            Assert.Contains("HomeHeaderChromeProbeReadings.ScrollBar(scrollBar is not null,", caller);
            Assert.Contains("OwnedScrollBarParts(scroller)", caller);
            var owners = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "ShellNavigationProbe.cs"));
            Assert.Contains("if (child is ScrollView or ScrollViewer) continue;", owners);
        });
        Test("顶部返回等待：缓存就绪和旧偏移不得绕过本次Loaded、布局与恢复", () =>
        {
            var states = new[]
            {
                Returned with { HomeLoaded = false, HomeWidth = 0, RestorePending = true },
                Returned with { HomeLoaded = true, HomeWidth = 0, RestorePending = true },
                Returned with { HomeWidth = 1240, ScrollLoaded = false, RestorePending = true },
                Returned with { RestorePending = false, Restoring = true, Offset = 118 },
                Returned with { Restoring = false, Offset = 228 },
                Returned
            };
            var phase = 0;
            var observations = new List<HomeHeaderChromeReturnReading>();
            var waiting = HomeHeaderChromeProbeReadings.WaitForReturnAsync(() => states[phase], observations.Add,
                () => { phase++; return Task.CompletedTask; });
            Assert.True(waiting.IsCompletedSuccessfully, waiting.Exception?.ToString() ?? "返回序列未完成");
            Assert.Equal(states.Length - 1, phase);
            Assert.Equal(states.Length, observations.Count);
            Assert.Equal(Returned, observations[^1]);
        });
        Test("顶部返回等待：布局已到位但Loaded恢复仍待消费时保持异步等待", () =>
        {
            var current = Returned with { RestorePending = true };
            var next = new TaskCompletionSource();
            var observations = new List<HomeHeaderChromeReturnReading>();
            var waiting = HomeHeaderChromeProbeReadings.WaitForReturnAsync(() => current,
                observations.Add, () => next.Task);
            Assert.False(waiting.IsCompleted);
            current = Returned;
            next.SetResult();
            Assert.True(waiting.IsCompletedSuccessfully, waiting.Exception?.ToString() ?? "完成信号后仍在等待");
            Assert.Equal(2, observations.Count);
            Assert.True(HomeHeaderChromeProbeReadings.ReturnReady(observations[^1]));
        });
        Test("顶部返回等待：错误页、未加载模板、无视口和坏尺寸持续拒绝", () =>
        {
            foreach (var bad in new[]
            {
                Returned with { CurrentPage = false }, Returned with { ContentReady = false },
                Returned with { HomeLoaded = false }, Returned with { ScrollLoaded = false },
                Returned with { PresenterLoaded = false }, Returned with { HasXamlRoot = false },
                Returned with { RestorePending = true }, Returned with { Restoring = true },
                Returned with { HomeWidth = 0 }, Returned with { HomeWidth = 984 },
                Returned with { RequestedWidth = double.NaN }, Returned with { RequestedWidth = 0 },
                Returned with { HomeHeight = 0 }, Returned with { ViewportHeight = 0 },
                Returned with { ExtentHeight = 759 }, Returned with { Offset = double.NaN },
                Returned with { ExpectedOffset = 0 }, Returned with { Offset = 0 }
            })
                Assert.False(HomeHeaderChromeProbeReadings.ReturnReady(bad));
            var delays = 0;
            var readings = new List<HomeHeaderChromeReturnReading>();
            var timedOut = HomeHeaderChromeProbeReadings.WaitForReturnAsync(
                () => Returned with { HomeWidth = 0 }, readings.Add,
                () => { delays++; return Task.CompletedTask; });
            Assert.True(timedOut.IsFaulted);
            Assert.Equal(1, timedOut.Exception!.InnerExceptions.Count);
            Assert.True(timedOut.Exception.InnerExceptions[0] is TimeoutException);
            Assert.Equal(80, delays);
            Assert.Equal(80, readings.Count);
            Assert.Equal(0.0, readings[^1].HomeWidth);
            HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Revealed, Revealed);
            Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Revealed, Hidden));
        });
        Test("顶部运行宽度：客户区、缩放与实际主页均到达目标且容差最多1.5 DIP", () =>
        {
            HomeHeaderChromeProbeReadings.Layout(new(1240, true, 1240, 96, 1, 1240, 1240), true);
            HomeHeaderChromeProbeReadings.Layout(new(760, true, 950, 120, 1.25, 760, 760), true);
            HomeHeaderChromeProbeReadings.Layout(new(760, true, 1520, 192, 2, 761.5, 758.5), true);
            HomeHeaderChromeProbeReadings.Layout(new(760, true, 1523, 192, 2, 760, 760), true);
            HomeHeaderChromeProbeReadings.Layout(new(760, true, 1520, 192, 2, 760, 600), false);
        });
        Test("顶部运行宽度：badwidth、错误缩放、同宽旧布局及仅主页旧宽都拒绝", () =>
        {
            var valid = new HomeHeaderChromeLayoutReading(760, true, 1520, 192, 2, 760, 760);
            foreach (var bad in new[]
            {
                valid with { ClientRectRead = false }, valid with { ClientWidthPixels = 1524 },
                valid with { RequestedWidth = 0 }, valid with { RequestedWidth = double.NaN },
                valid with { RasterizationScale = 1 }, valid with { RasterizationScale = 0 },
                valid with { RasterizationScale = double.NaN }, valid with { WindowDpi = 96 },
                valid with { ShellWidth = 761.51 }, valid with { HomeWidth = 758.49 },
                valid with { ShellWidth = 1000, HomeWidth = 1000 },
                valid with { ClientWidthPixels = 2000, ShellWidth = 1000, HomeWidth = 1000 },
                valid with { HomeWidth = null }, valid with { HomeWidth = double.NaN }
            })
                Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.Layout(bad, true));
        });
        Test("顶部探针清理：首项失败仍解绑所有属主并恢复窗口和主题，保留原失败", () =>
        {
            var steps = new List<string>();
            var first = new InvalidOperationException("release failed");
            var original = new InvalidOperationException("probe failed");
            var error = Assert.Catch<AggregateException>(() => HomeHeaderChromeProbeCleanup.Run(
            [
                () => { steps.Add("release"); throw first; },
                () => steps.Add("unbind"),
                () => steps.Add("remove-root")
            ],
            [
                () => steps.Add("window"),
                () => steps.Add("theme"),
                () => steps.Add("resize")
            ], original));
            Assert.Equal("release,unbind,remove-root,window,theme,resize", string.Join(",", steps));
            Assert.Equal(2, error.InnerExceptions.Count);
            Assert.True(ReferenceEquals(original, error.InnerExceptions[0]));
            Assert.True(ReferenceEquals(first, error.InnerExceptions[1]));
        });
        Test("顶部探针清理：恢复步骤异常不阻止后项，成功不额外制造失败", () =>
        {
            var steps = new List<string>();
            var error = Assert.Catch<AggregateException>(() => HomeHeaderChromeProbeCleanup.Run(
            [
                () => { steps.Add("release"); throw new InvalidOperationException("release failed"); },
                () => steps.Add("unbind")
            ],
            [
                () => { steps.Add("window"); throw new InvalidOperationException("window failed"); },
                () => steps.Add("theme"),
                () => steps.Add("resize")
            ]));
            Assert.Equal("release,unbind,window,theme,resize", string.Join(",", steps));
            Assert.Equal(2, error.InnerExceptions.Count);
            HomeHeaderChromeProbeCleanup.Run([() => steps.Add("detach-ok")], [() => steps.Add("restore-ok")]);
            Assert.Equal("detach-ok,restore-ok", string.Join(",", steps.TakeLast(2)));
        });
        Test("顶部运行读数：顶端、隐藏、上滚三个真实结果通过", () =>
        {
            HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Top, Visible);
            HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Hidden, Hidden);
            HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Revealed, Revealed);
            HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Revealed, Revealed with { BackgroundWidth = 760, ShellWidth = 760 });
        });
        Test("顶部运行读数：隐藏仍残留按钮、原生三键或穿透洞均失败", () =>
        {
            foreach (var bad in new[]
            {
                Hidden with { HeaderVisible = true }, Hidden with { AccountVisible = true },
                Hidden with { BackgroundVisible = true }, Hidden with { BrowseHidden = false },
                Hidden with { NativeHeight = 48 }, Hidden with { NativeRightInset = 138 },
                Hidden with { Holes = 1 }, Hidden with { PassthroughRegions = 1 },
                Hidden with { WholeCaption = false }, Hidden with { MinimizeRegion = true },
                Hidden with { MaximizeRegion = true }, Hidden with { CloseRegion = true }
            })
                Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Hidden, bad));
        });
        Test("顶部运行读数：恢复少一颗系统键、洞或原生高度均失败", () =>
        {
            foreach (var bad in new[]
            {
                Visible with { HeaderVisible = false }, Visible with { AccountVisible = false },
                Visible with { NativeHeight = 0 }, Visible with { NativeRightInset = 0 },
                Visible with { NativeHeight = double.NaN }, Visible with { Holes = 0 },
                Visible with { PassthroughRegions = 0 }, Visible with { MinimizeRegion = false },
                Visible with { MaximizeRegion = false }, Visible with { CloseRegion = false }
            })
                Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Top, bad));
        });
        Test("顶部运行读数：亚克力错误高度、非全宽、拦截输入或非法尺寸失败", () =>
        {
            foreach (var bad in new[]
            {
                Revealed with { BackgroundVisible = false }, Revealed with { BackgroundHitTest = true },
                Revealed with { BackgroundHeight = 40 }, Revealed with { BackgroundWidth = 1000 },
                Revealed with { BackgroundWidth = double.NaN }, Revealed with { ShellWidth = 0 }
            })
                Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.State(HomeHeaderMode.Revealed, bad));
        });
        Test("顶部运行读数：焦点转移须仍在原来的深处", () =>
        {
            HomeHeaderChromeProbeReadings.FocusTransfer(300, 300, false);
            HomeHeaderChromeProbeReadings.FocusTransfer(300, 300.5, false);
            Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.FocusTransfer(300, 0, false));
            Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.FocusTransfer(300, 300, true));
            Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.FocusTransfer(double.NaN, 300, false));
            Assert.Throws<InvalidOperationException>(() => HomeHeaderChromeProbeReadings.FocusTransfer(0, 0, false));
        });
        Test("顶部运行入口：真实滚动、原生区域、匿名动作和回执判据仍接线", () =>
        {
            var root = FindRoot();
            string Source(string path) => File.ReadAllText(Path.Combine(root, "src/Momoka.Shell", path));
            var registration = Source("ShellNavigationProbe.cs");
            Assert.Contains("HomeHeaderChromeAsync, 90, section: ShellProbeSections.Shell", registration);
            Assert.Contains("public void RefreshHomeHeader() => HomeHeaderChanged?.Invoke()", registration);
            Assert.Contains("InspectHomeHeaderChrome", Source("ShellNavigationProbe.Inspect.cs"));
            var caller = Source("ShellNavigationProbe.HomeHeaderChrome.cs");
            Assert.Contains("fixture.Actions.HomeHeaderChanged = shell.RefreshHomeHeader", caller);
            Assert.Contains("await ScrollProbeAsync(scroll, 240)", caller);
            Assert.Contains("await ScrollProbeAsync(scroll, 236)", caller);
            Assert.Contains("await ScrollProbeAsync(scroll, 228)", caller);
            Assert.Contains("Call(shell, \"GoBack\")", caller);
            Assert.Contains("HomeHeaderChromeProbeReadings.WaitForReturnAsync", caller);
            Assert.Contains("Get<double?>(home, \"_restoredScroll\") is not null", caller);
            Assert.Contains("scroll.ScrollPresenter?.IsLoaded == true", caller);
            Assert.Contains("Read(\"history-return\", HomeHeaderMode.Revealed)", caller);
            Assert.True(caller.IndexOf("HomeHeaderChromeProbeReadings.WaitForReturnAsync", StringComparison.Ordinal)
                < caller.IndexOf("Read(\"history-return\", HomeHeaderMode.Revealed)", StringComparison.Ordinal));
            Assert.Contains("new { snapshots, layouts, returns,", caller);
            Assert.Contains("ShellPrefs.ApplyAppearance(fixture.Settings.Ui)", caller);
            Assert.Contains("source.GetRegionRects(kind)", caller);
            Assert.Contains("HomeHeaderChromeProbeReadings.State(expected, reading)", caller);
            Assert.Contains("HomeHeaderChromeProbeReadings.FocusTransfer(240, scroll.VerticalOffset, FocusInHeader())", caller);
            Assert.Contains("fixture.Actions.Plays == 0", caller);
            Assert.Contains("request.Method == HttpMethod.Get", caller);
            Assert.Contains("ReadLayout(width, \"home-layout\", true)", caller);
            Assert.Contains("ReadLayout(width, stage, fixture.Settings.Ui.EnableImmersive && current is not null)", caller);
            Assert.Contains("Interop.Native.GetDpiForWindow(window.Handle)", caller);
            Assert.Contains("HomeHeaderChromeProbeReadings.Layout(reading, fullHome)", caller);
            Assert.True(caller.IndexOf("layouts.Add(new", StringComparison.Ordinal)
                < caller.IndexOf("HomeHeaderChromeProbeReadings.Layout(reading, fullHome)", StringComparison.Ordinal));
            Assert.Contains("HomeHeaderChromeProbeCleanup.Run", caller);
            Assert.Contains("], probeFailure)", caller);
        });
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("本树源码缺失");
    }
}

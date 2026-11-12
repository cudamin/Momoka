using Momoka.Shell;
using Momoka.Shell.Views;
using Windows.UI;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Case("真实画布规则：普通Mica/effects开取99alpha，实底不取桌面效果", () =>
        {
            var state = State(); var input = Color.FromArgb(40, 80, 90, 100);
            var result = SettingsWindow.SelectCanvasColor(state, input, true);
            Assert.Equal(Color.FromArgb(0x99, 80, 90, 100), result);
            Assert.Equal((byte)40, input.A, "值类型入参没有被修改");
            Assert.Equal(Color.FromArgb(255, 80, 90, 100), SettingsWindow.SelectCanvasColor(state, input, false));
        });
        Case("真实画布规则：effects关闭时强制实底，高对比使用系统背景", () =>
        {
            var ordinary = Color.FromArgb(255, 40, 50, 60);
            Assert.Equal((byte)255, SettingsWindow.SelectCanvasColor(State(effects: false), ordinary, true).A);
            var high = State(contrast: true, effects: true);
            Assert.Equal(high.Background, SettingsWindow.SelectCanvasColor(high, ordinary, true));
        });
        Case("真实观察：Start四类订阅各一次，Stop全部清掉", () =>
        {
            using var window = Window(); Assert.Equal(4, AppearanceFaults.Subscribers);
            Assert.Equal(1, window.PaintCalls); window.Dispose(); window.Dispose();
            Assert.Equal(0, AppearanceFaults.Subscribers);
        });
        Case("真实队列：offUI通知只post，不先读状态，UI drain才刷新", () =>
        {
            using var window = Window(); var paints = window.PaintCalls; var reads = AppearanceFaults.Reads;
            AppearanceFaults.HighContrast = true; AppearanceFaults.ReadOffUi = true;
            try { AppearanceFaults.FireContrast(); }
            finally { AppearanceFaults.ReadOffUi = false; }
            Assert.Equal(1, window.Queue.Pending); Assert.Equal(reads, AppearanceFaults.Reads); Assert.Equal(paints, window.PaintCalls);
            window.Queue.Drain(); Assert.Equal(paints + 1, window.PaintCalls);
            Assert.Equal(AppearanceFaults.Background, window.CanvasForTest);
        });
        Case("真实队列：三种突发事件合并一次并读取最新状态", () =>
        {
            using var window = Window(); var paints = window.PaintCalls;
            AppearanceFaults.FireContrast(); AppearanceFaults.FireColor(); AppearanceFaults.FireEffects();
            Assert.Equal(1, window.Queue.Pending); Assert.Equal(1, window.Queue.EnqueueCalls);
            AppearanceFaults.Effects = false; AppearanceFaults.HighContrast = true;
            window.Queue.Drain(); Assert.Equal(paints + 1, window.PaintCalls);
            Assert.True(window.SnapshotForTest!.Value.HighContrast); Assert.False(window.SnapshotForTest!.Value.AdvancedEffects);
        });
        Case("真实观察：同样状态不重复Paint，同HC换背景仍刷新", () =>
        {
            AppearanceFaults.HighContrast = true;
            using var window = Window(); var paints = window.PaintCalls;
            AppearanceFaults.FireContrast(); window.Queue.Drain(); Assert.Equal(paints, window.PaintCalls);
            AppearanceFaults.Background = Color.FromArgb(255, 61, 72, 83);
            AppearanceFaults.FireColor(); window.Queue.Drain();
            Assert.Equal(paints + 1, window.PaintCalls); Assert.Equal(AppearanceFaults.Background, window.CanvasForTest);
        });
        Case("真实观察：effects关/开还原画布alpha且不改变Mica赋值标记", () =>
        {
            using var window = Window(); Assert.Equal((byte)0x99, window.CanvasForTest.A);
            AppearanceFaults.Effects = false; AppearanceFaults.FireEffects(); window.Queue.Drain(); Assert.Equal((byte)255, window.CanvasForTest.A);
            AppearanceFaults.Effects = true; AppearanceFaults.FireEffects(); window.Queue.Drain(); Assert.Equal((byte)0x99, window.CanvasForTest.A);
        });
        Case("真实观察：Foreground/Highlight变化也更新快照", () =>
        {
            using var window = Window(); var paints = window.PaintCalls;
            AppearanceFaults.Foreground = Color.FromArgb(255, 200, 160, 120);
            AppearanceFaults.HighlightText = Color.FromArgb(255, 8, 9, 10);
            AppearanceFaults.FireColor(); window.Queue.Drain(); Assert.Equal(paints + 1, window.PaintCalls);
            Assert.Equal(AppearanceFaults.Foreground, window.SnapshotForTest!.Value.Foreground);
            Assert.Equal(AppearanceFaults.HighlightText, window.SnapshotForTest.Value.HighlightText);
        });
        Case("真实停止：关闭后已排队通知不读状态不Paint，订阅归零", () =>
        {
            var window = Window(); AppearanceFaults.HighContrast = true; AppearanceFaults.FireContrast();
            var paints = window.PaintCalls; var reads = AppearanceFaults.Reads;
            window.Dispose(); Assert.Equal(0, AppearanceFaults.Subscribers); window.Queue.Drain();
            Assert.Equal(reads, AppearanceFaults.Reads); Assert.Equal(paints, window.PaintCalls);
        });
        foreach (var throws in new[] { false, true })
            Case("真实队列：TryEnqueue" + (throws ? "抛异常" : "返回false") + "后下次可重新派发", () =>
            {
                using var window = Window(); var paints = window.PaintCalls;
                AppearanceFaults.HighContrast = true;
                window.Queue.Throw = throws; window.Queue.Accept = false; AppearanceFaults.FireContrast();
                Assert.Equal(0, window.Queue.Pending); Assert.Equal(paints, window.PaintCalls);
                window.Queue.Throw = false; window.Queue.Accept = true; AppearanceFaults.FireContrast();
                Assert.Equal(1, window.Queue.Pending); window.Queue.Drain(); Assert.Equal(paints + 1, window.PaintCalls);
            });
        Case("真实观察：系统event缺失时只在显示期使用timer，Hide停止", () =>
        {
            AppearanceFaults.EventPresent = false;
            using var window = Window(); Assert.Equal(3, AppearanceFaults.Subscribers); Assert.Equal(0, window.Queue.Timers.Count);
            window.ShowForTest(); var timer = window.Queue.Timers.Single();
            Assert.True(timer.IsRunning); Assert.Equal(TimeSpan.FromSeconds(1), timer.Interval); Assert.Equal(1, timer.Subscribers);
            AppearanceFaults.Effects = false; timer.Fire(); Assert.Equal((byte)255, window.CanvasForTest.A);
            window.HideForTest(); Assert.False(timer.IsRunning); var paints = window.PaintCalls;
            AppearanceFaults.Effects = true; timer.Fire(); Assert.Equal(paints, window.PaintCalls);
            window.ShowForTest(); Assert.Equal((byte)0x99, window.CanvasForTest.A);
            Assert.Equal(1, window.Queue.Timers.Count); Assert.Equal(1, timer.Subscribers);
            window.Dispose(); Assert.False(timer.IsRunning); Assert.Equal(0, timer.Subscribers);
        });
        Case("真实观察：所有events可用时缓存Show/Hide不重Start也不造timer", () =>
        {
            using var window = Window(); var initialSubscribers = AppearanceFaults.Subscribers;
            for (var i = 0; i < 5; i++) { window.ShowForTest(); window.HideForTest(); }
            Assert.Equal(initialSubscribers, AppearanceFaults.Subscribers); Assert.Equal(0, window.Queue.Timers.Count);
            Assert.Equal(1, AppearanceFaults.Accessibility.Count); Assert.Equal(1, AppearanceFaults.Settings.Count);
        });
        foreach (var point in new[] { "add.Contrast", "add.Color", "api.IsEventPresent", "add.Effects", "add.Activated" })
            Case("真实观察：部分订阅失败后仍可显示/轮询，关闭清已成功订阅/" + point, () =>
            {
                AppearanceFaults.FailOnce(point); using var window = Window();
                Assert.Equal(1, window.PaintCalls);
                window.ShowForTest();
                // Activation-only failure does not remove HC/color/effects subscriptions; polling is only for missing system events.
                if (point != "add.Activated") Assert.True(window.Queue.Timers.Single().IsRunning);
                window.Dispose(); Assert.Equal(0, AppearanceFaults.Subscribers);
                Assert.True(window.Queue.Timers.All(timer => !timer.IsRunning && timer.Subscribers == 0));
            });
        foreach (var point in new[] { "read.HighContrast", "read.Effects", "read.Background", "read.Foreground", "read.Highlight", "read.HighlightText" })
            Case("真实观察：首次状态读取失败不崩，后续activation可恢复/" + point, () =>
            {
                AppearanceFaults.FailOnce(point); using var window = Window();
                Assert.Equal(0, window.PaintCalls);
                window.ActivateForTest(); Assert.Equal(1, window.PaintCalls);
                Assert.Equal((byte)0x99, window.CanvasForTest.A);
            });
        Case("真实观察：缺event且隐藏timer意外tick会主动Stop", () =>
        {
            AppearanceFaults.EventPresent = false;
            using var window = Window(); window.ShowForTest(); var timer = window.Queue.Timers.Single();
            window.HideForTest(); timer.Start(); var paints = window.PaintCalls; timer.Fire();
            Assert.False(timer.IsRunning); Assert.Equal(paints, window.PaintCalls);
        });
        Case("真实观察：显示期间activation及时检查但不重建timer", () =>
        {
            AppearanceFaults.EventPresent = false;
            using var window = Window(); window.ShowForTest(); var timer = window.Queue.Timers.Single();
            AppearanceFaults.Background = Color.FromArgb(255, 89, 88, 87); AppearanceFaults.HighContrast = true;
            window.ActivateForTest(); Assert.Equal(AppearanceFaults.Background, window.CanvasForTest);
            Assert.Equal(1, window.Queue.Timers.Count); Assert.True(timer.IsRunning);
        });
        Case("真实观察：Paint失败不提交快照，相同状态activation再次尝试", () =>
        {
            AppearanceFaults.FailOnce("caption.Paint");
            using var window = Window(); Assert.Equal(0, window.PaintCalls);
            window.ActivateForTest(); Assert.Equal(1, window.PaintCalls);
            Assert.Equal((byte)0x99, window.CanvasForTest.A);
        });
        foreach (var point in new[] { "timer.Create", "timer.Start", "timer.Stop" })
            Case("降级timer故障不阻断显示隐藏和后续清理/" + point, () =>
            {
                AppearanceFaults.EventPresent = false;
                using var window = Window();
                if (point != "timer.Stop") AppearanceFaults.FailOnce(point);
                window.ShowForTest();
                if (point == "timer.Stop") AppearanceFaults.FailOnce(point);
                window.HideForTest();
                window.Dispose();
            });
        Case("已成功后切主题Paint失败，相同系统状态激活仍可恢复", () =>
        {
            using var window = Window();
            var original = window.PaintCalls;
            ThemeHost.Current = new Theme(Color.FromArgb(255, 54, 65, 76));
            AppearanceFaults.FailOnce("caption.Paint"); window.ThemeForTest();
            Assert.Equal(original, window.PaintCalls);
            window.ActivateForTest(); Assert.Equal(original + 1, window.PaintCalls);
            Assert.Equal(Color.FromArgb(0x99, 54, 65, 76), window.CanvasForTest);
        });
        Case("真实自检判据：支持、回退与错误alpha逐项区分", () =>
        {
            foreach (var supported in new[] { false, true })
                foreach (var contrast in new[] { false, true })
                    foreach (var effects in new[] { false, true })
                    {
                        var alpha = (byte)(supported && !contrast && effects ? 0x99 : 0xFF);
                        Assert.True(SettingsBackdropCheck.Matches(supported, supported, alpha, contrast, effects));
                        Assert.False(SettingsBackdropCheck.Matches(supported, !supported, alpha, contrast, effects));
                        Assert.False(SettingsBackdropCheck.Matches(supported, supported, (byte)(alpha == 0x99 ? 0xFF : 0x99), contrast, effects));
                    }
        });
        Case("材质赋值失败且首次重画读取失败后，同一系统状态仍可恢复", () =>
        {
            AppearanceFaults.HighContrast = true;
            using var window = Window();
            Assert.Equal(AppearanceFaults.Background, window.CanvasForTest);
            AppearanceFaults.FailOnce("read.HighContrast");
            window.FailBackdropForTest();
            Assert.Equal(ThemeHost.Current.Window, window.CanvasForTest);
            window.ActivateForTest();
            Assert.Equal(AppearanceFaults.Background, window.CanvasForTest);
        });
        Case("Paint返回false不提交快照，相同状态激活重新尝试", () =>
        {
            AppearanceFaults.PaintReturnsFalse = true;
            using var window = Window();
            var paints = window.PaintCalls;
            AppearanceFaults.PaintReturnsFalse = false;
            window.ActivateForTest();
            Assert.Equal(paints + 1, window.PaintCalls);
            window.ActivateForTest();
            Assert.Equal(paints + 1, window.PaintCalls);
        });
        foreach (var point in new[] { "remove.Contrast", "remove.Color", "remove.Effects", "remove.Activated", "timer.Remove" })
            Case("单项退订失败仍清理其它观察，迟到回调不读XAML/" + point, () =>
            {
                if (point == "timer.Remove") AppearanceFaults.EventPresent = false;
                using var window = Window();
                window.ShowForTest();
                AppearanceFaults.FailOnce(point);
                window.Dispose();
                Assert.Equal(point == "timer.Remove" ? 0 : 1, AppearanceFaults.Subscribers);
                Assert.Equal(point == "timer.Remove" ? 1 : 0, window.Queue.Timers.Sum(timer => timer.Subscribers));
                Assert.True(window.Queue.Timers.All(timer => !timer.IsRunning));
                var paints = window.PaintCalls; var reads = AppearanceFaults.Reads;
                AppearanceFaults.FireContrast(); AppearanceFaults.FireColor(); AppearanceFaults.FireEffects();
                window.ActivateForTest(); window.Queue.Drain();
                Assert.Equal(reads, AppearanceFaults.Reads); Assert.Equal(paints, window.PaintCalls);
                // A failed native removal proves only that cleanup was attempted. Release the
                // intentionally retained substitute explicitly, after checking all other owners.
                foreach (var item in AppearanceFaults.Accessibility) item.ReleaseFixtureSubscribers();
                foreach (var item in AppearanceFaults.Settings) item.ReleaseFixtureSubscribers();
                foreach (var item in AppearanceFaults.Windows) item.ReleaseFixtureSubscribers();
                foreach (var timer in window.Queue.Timers) timer.ReleaseFixtureSubscribers();
            });
        SettingsSelfCheckReadTests.Register();
        return TestHarness.Run();
    }
    private static SettingsWindow Window(bool mica = true)
    {
        var window = new SettingsWindow(); window.InitializeForTest(mica); return window;
    }
    private static SettingsWindow.SettingsSystemAppearance State(bool contrast = false, bool effects = true) =>
        new(contrast, effects, Color.FromArgb(255, 11, 12, 13), Colors.White, Color.FromArgb(255, 22, 33, 44), Colors.White);
    private static void Case(string name, Action action) => Test(name, () =>
    {
        AppearanceFaults.Reset(); action();
        Assert.Equal(0, AppearanceFaults.Subscribers, "本例结束后事件仍订阅，不能让下一例Reset洗掉");
        Assert.True(AppearanceFaults.Windows.SelectMany(window => window.DispatcherQueue.Timers)
            .All(timer => !timer.IsRunning && timer.Subscribers == 0), "本例结束后timer仍运行/订阅");
    });
}

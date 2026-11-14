using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PosterHoverDwellTests
{
    internal static void Register()
    {
        Test("封面静止：499毫秒不触发，500毫秒触发且只触发一次", () =>
        {
            var dwell = new PosterHoverDwell();
            Assert.True(dwell.Observe(20, 30, 100));
            Assert.False(dwell.TryComplete(599));
            Assert.Equal(1L, dwell.RemainingMilliseconds(599));
            Assert.False(dwell.IsReady);
            Assert.True(dwell.TryComplete(600));
            Assert.True(dwell.IsReady);
            Assert.False(dwell.IsPending);
            Assert.False(dwell.TryComplete(1100));
        });

        Test("封面静止：横向或纵向移动都重计半秒，旧计时回调不能提前触发", () =>
        {
            foreach (var (x, y) in new[] { (21, 30), (20, 31) })
            {
                var dwell = new PosterHoverDwell();
                dwell.Observe(20, 30, 0);
                Assert.True(dwell.Observe(x, y, 499));
                Assert.False(dwell.TryComplete(500));
                Assert.False(dwell.TryComplete(998));
                Assert.True(dwell.TryComplete(999));
            }
        });

        Test("封面静止：连续移动两秒不触发，最后一次移动后才开始半秒", () =>
        {
            var dwell = new PosterHoverDwell();
            for (var now = 0; now <= 2000; now += 100)
            {
                Assert.True(dwell.Observe(now, 0, now));
                Assert.False(dwell.TryComplete(now + 99));
            }
            Assert.False(dwell.TryComplete(2499));
            Assert.True(dwell.TryComplete(2500));
        });

        Test("封面静止：同坐标重复事件不延后触发", () =>
        {
            var dwell = new PosterHoverDwell();
            dwell.Observe(20, 30, 0);
            Assert.False(dwell.Observe(20, 30, 499));
            Assert.True(dwell.TryComplete(500));
        });

        Test("封面静止：移出或按下取消，迟到回调无效，原位置重入仍等足半秒", () =>
        {
            var dwell = new PosterHoverDwell();
            Assert.False(dwell.TryComplete(1000));
            dwell.Observe(20, 30, 0);
            dwell.Cancel();
            Assert.False(dwell.IsPending);
            Assert.False(dwell.TryComplete(500));
            Assert.True(dwell.Observe(20, 30, 600));
            Assert.False(dwell.TryComplete(1099));
            Assert.True(dwell.TryComplete(1100));
            dwell.Cancel();
            dwell.Cancel();
            Assert.False(dwell.IsReady);
            Assert.False(dwell.TryComplete(2000));
        });

        Test("封面静止：已放大后移动不反复缩回，换卡取消后重新计时", () =>
        {
            var dwell = new PosterHoverDwell();
            dwell.Observe(20, 30, 0);
            Assert.True(dwell.TryComplete(500));
            Assert.False(dwell.Observe(40, 50, 501));
            Assert.True(dwell.IsReady);
            dwell.Cancel();
            Assert.True(dwell.Observe(40, 50, 502));
            Assert.False(dwell.IsReady);
            Assert.False(dwell.TryComplete(1001));
            Assert.True(dwell.TryComplete(1002));
        });

        Test("封面静止：长系统运行时间不溢出，晚到回调正常触发", () =>
        {
            var dwell = new PosterHoverDwell();
            var now = (long)int.MaxValue + 1000;
            dwell.Observe(20, 30, now);
            Assert.False(dwell.TryComplete(now + 499));
            Assert.True(dwell.TryComplete(now + 1200));
            Assert.Equal(0L, dwell.RemainingMilliseconds(now + 1200));
        });

        Test("封面静止：点击取消旧计时，移动和迟到回调都不重新触发", () =>
        {
            var dwell = new PosterHoverDwell();
            dwell.Observe(20, 30, 0);
            dwell.Suppress();
            Assert.True(dwell.IsSuppressed);
            Assert.False(dwell.IsReady);
            Assert.False(dwell.IsPending);
            Assert.False(dwell.TryComplete(500));
            for (var now = 600; now <= 2000; now += 100)
                Assert.False(dwell.Observe(now, 30, now));
            Assert.False(dwell.TryComplete(3000));
        });

        Test("封面静止：点击后取消事件不解除抑制，真正移出重入才重新等半秒", () =>
        {
            var dwell = new PosterHoverDwell();
            dwell.Observe(20, 30, 0);
            Assert.True(dwell.TryComplete(500));
            dwell.Suppress();
            dwell.Cancel();
            Assert.False(dwell.IsReady);
            Assert.True(dwell.IsSuppressed);
            Assert.False(dwell.Observe(21, 30, 600));
            dwell.Reset();
            Assert.False(dwell.IsSuppressed);
            Assert.True(dwell.Observe(21, 30, 1000));
            Assert.False(dwell.TryComplete(1499));
            Assert.True(dwell.TryComplete(1500));
        });
    }
}

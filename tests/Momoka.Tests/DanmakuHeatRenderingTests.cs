using Momoka.Danmaku;
using Momoka.Shell;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DanmakuHeatRenderingTests
{
    internal static void Register()
    {
        Test("弹幕热力渲染：正式方法按当前轨宽绘制、底对齐并使用共享画刷", () =>
        {
            var page = Page();
            page.PaintHeatForFixture();
            Assert.True(Matches(page));
            Assert.True(page.DanmakuHeat.Children.Count > 1);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, page.DanmakuHeat.Visibility);
        });
        Test("弹幕热力渲染：相同快照与尺寸复用柱子，改宽高重新定位而不分配", () =>
        {
            var page = Page();
            page.PaintHeatForFixture();
            var first = page.DanmakuHeat.Children[0];
            var allocated = page.Allocated;
            page.PaintHeatForFixture();
            Assert.Equal(allocated, page.Allocated);
            Assert.True(ReferenceEquals(first, page.DanmakuHeat.Children[0]));
            page.SeekTrack.ActualWidth = 360;
            page.SeekTrack.ActualHeight = 40;
            page.PaintHeatForFixture();
            Assert.True(Matches(page));
            Assert.Equal(allocated, page.Allocated);
        });
        Test("弹幕热力渲染：关闭清除子元素、恢复同一快照后重新绘制", () =>
        {
            var page = Page();
            page.PaintHeatForFixture();
            page.ViewModel.DanmakuHeatVisible = false;
            page.PaintHeatForFixture();
            Assert.Equal(0, page.DanmakuHeat.Children.Count);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.DanmakuHeat.Visibility);
            page.ViewModel.DanmakuHeatVisible = true;
            page.PaintHeatForFixture();
            Assert.True(Matches(page));
        });
        Test("弹幕热力渲染：空数据、零高与过窄轨道均清掉旧柱子", () =>
        {
            foreach (var boundary in new[] { "empty", "height", "width" })
            {
                var page = Page();
                page.PaintHeatForFixture();
                if (boundary == "empty") page.ViewModel.DanmakuHeatmap = DanmakuHeat.Empty;
                if (boundary == "height") page.SeekTrack.ActualHeight = 0;
                if (boundary == "width") page.SeekTrack.ActualWidth = 1;
                page.PaintHeatForFixture();
                Assert.Equal(0, page.DanmakuHeat.Children.Count);
                Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, page.DanmakuHeat.Visibility);
            }
        });
        Test("弹幕热力渲染：更少评论裁掉多余柱子，未附加页面拒绝绘制", () =>
        {
            var page = Page();
            page.PaintHeatForFixture();
            page.ViewModel.DanmakuHeatmap = DanmakuHeat.Build([new DanmakuComment(10, 1, 1, "one")], 1200);
            page.PaintHeatForFixture();
            Assert.Equal(1, page.DanmakuHeat.Children.Count);
            Assert.True(Matches(page));
            var detached = Page();
            detached.Attached = false;
            detached.PaintHeatForFixture();
            Assert.Equal(0, detached.Allocated);
        });
        Test("弹幕热力原生读数：错位置、错高、错宽、非共享画刷和缺柱子均拒绝", () =>
        {
            var page = Page();
            page.PaintHeatForFixture();
            var readings = Readings(page);
            Assert.True(DanmakuHeatProbeEvidence.Matches(page.ViewModel.DanmakuHeatmap, 600, 31, readings));
            foreach (var broken in new[]
            {
                readings[0] with { Left = readings[0].Left + 1 },
                readings[0] with { Top = -1 },
                readings[0] with { Height = 31 },
                readings[0] with { Width = 601 },
                readings[0] with { SharedBrush = false },
                readings[0] with { Left = double.NaN }
            })
            {
                var wrong = readings.ToArray();
                wrong[0] = broken;
                Assert.False(DanmakuHeatProbeEvidence.Matches(page.ViewModel.DanmakuHeatmap, 600, 31, wrong));
            }
            Assert.False(DanmakuHeatProbeEvidence.Matches(page.ViewModel.DanmakuHeatmap, 600, 31, readings.Skip(1).ToArray()));
            Assert.False(DanmakuHeatProbeEvidence.Matches(page.ViewModel.DanmakuHeatmap, double.NaN, 31, readings));
            Assert.False(DanmakuHeatProbeEvidence.Matches(DanmakuHeat.Empty, 600, 31, readings));
        });
        Test("弹幕热力实屏：相同帧与区域外改变不算可见热力，区域内颜色改变被读取", () =>
        {
            var before = new byte[4 * 4 * 4];
            var after = before.ToArray();
            Assert.Equal(0, DanmakuHeatProbeEvidence.ChangedPixels(before, after, 4, 4, 1, 1, 3, 3));
            after[0] = 255;
            Assert.Equal(0, DanmakuHeatProbeEvidence.ChangedPixels(before, after, 4, 4, 1, 1, 3, 3));
            after[(1 * 4 + 1) * 4 + 1] = 100;
            Assert.Equal(1, DanmakuHeatProbeEvidence.ChangedPixels(before, after, 4, 4, 1, 1, 3, 3));
            Assert.Throws<ArgumentException>(() => DanmakuHeatProbeEvidence.ChangedPixels(before, after, 5, 4, 1, 1, 3, 3));
            Assert.Throws<ArgumentException>(() => DanmakuHeatProbeEvidence.ChangedPixels(before, after, 4, 4, -1, 1, 3, 3));
        });
    }

    private static PlayerPage Page()
    {
        var page = new PlayerPage();
        page.ViewModel.DanmakuHeatmap = DanmakuHeat.Build(
            Enumerable.Range(0, 60).Select(index => new DanmakuComment(index * 20 + 2, 1, 1, "anonymous")), 1200);
        return page;
    }

    private static bool Matches(PlayerPage page) => DanmakuHeatProbeEvidence.Matches(page.ViewModel.DanmakuHeatmap,
        page.SeekTrack.ActualWidth, page.SeekTrack.ActualHeight, Readings(page));

    private static DanmakuHeatBarReading[] Readings(PlayerPage page) => page.DanmakuHeat.Children.Select(bar =>
        new DanmakuHeatBarReading(bar.Left, bar.Top, bar.Width, bar.Height,
            ReferenceEquals(bar.Background, page.Resources["PlayerDanmakuHeatBrush"]))).ToArray();
}

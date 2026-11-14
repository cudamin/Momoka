using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class StoreDetailProbeEvidenceTests
{
    internal static void Register()
    {
        Test("商店订阅卡片：右资料列与窄窗堆叠仍核两侧边界", () =>
        {
            StoreDetailProbeEvidence.RequireSubscriptionColumns(1240, 96, 96, 96, 832, 856, 1144, 24, true);
            StoreDetailProbeEvidence.RequireSubscriptionColumns(760, 56, 56, 56, 704, 56, 576, 0, false);
            Reject(() => StoreDetailProbeEvidence.RequireSubscriptionColumns(1240, 96, 96, 96, 1144, 856, 1144, 24, true));
            Reject(() => StoreDetailProbeEvidence.RequireSubscriptionColumns(1240, 96, 96, 96, 832, 856, 1200, 24, true));
            Reject(() => StoreDetailProbeEvidence.RequireSubscriptionColumns(760, 56, 56, 56, 680, 56, 576, 0, false));
            Reject(() => StoreDetailProbeEvidence.RequireSubscriptionColumns(760, 56, 56, 56, 704, 56, 1000, 0, false));
        });
        Test("交付动效检查台：五项动效及设置宽窄并集接线", () =>
        {
            var probe = Read("ShellNavigationProbe.Inspect.cs");
            Assert.Contains("\"InspectPersonTransition\"", probe);
            Assert.Contains("\"InspectMotionPageTransitions\"", probe);
            Assert.Contains("\"InspectDeliveryMotion\"", probe);
            Assert.Contains("await FoundationHomeDirectoryReturnAsync();", probe);
            Assert.Contains("\"InspectDeliveryMotionSettings\"", probe);
            foreach (var call in new[] { "await SettingsEntranceAsync();", "await SettingsPriorityFiltersAsync(width);",
                "await SettingsConditionalRowsAsync(width);", "await SettingsDanmakuAsync(width);",
                "await MusicAsync();", "await FilterValuesAsync();" }) Assert.Contains(call, probe);
            Assert.Contains("foreach (var width in new[] { 1100, 760 })", probe);
            var danmaku = Read("ShellNavigationProbe.Danmaku.cs");
            Assert.Contains("关闭弹幕未隐藏热力行或清掉了关闭偏好", danmaku);
            Assert.Contains("恢复弹幕后热力行未保留关闭偏好", danmaku);
            Assert.Contains("page.Visibility = Visibility.Visible;", Read("ShellNavigationProbe.DanmakuHeat.cs"));
            var episode = Read("ShellNavigationProbe.EpisodeBand.cs");
            Assert.Contains("compact || view.Showcase ? 8d : 12d", episode);
            Assert.Contains("view.Showcase ? DetailHero.ShowcaseSheetSpacing", episode);
            Assert.Contains("Get<Grid>(page, \"HeroContent\").VerticalAlignment == VerticalAlignment.Top", episode);
            Assert.Contains("RequireHeroColumn(page);", episode);
        });
        Test("商店诊断收尾：释放失败仍还原状态并保留失败", () =>
        {
            var restored = 0;
            var failure = new InvalidOperationException("释放反例");
            try
            {
                StoreDetailProbeEvidence.Cleanup(() => throw failure, () => restored++,
                    () => throw new ArgumentException("还原反例"), () => restored++);
                throw new AssertionException("收尾失败被吞掉");
            }
            catch (AggregateException error)
            {
                Assert.Equal(2, restored);
                Assert.Equal(2, error.InnerExceptions.Count);
                Assert.True(ReferenceEquals(failure, error.InnerExceptions[0]));
            }
        });
        Test("商店运行判据：实际视口与三处右沿空隙必须一致", () =>
        {
            StoreDetailProbeEvidence.RequireViewport(1240, 1240, 744, 744, 16, 16, 16, 16);
            StoreDetailProbeEvidence.RequireViewport(360, 360, 360, 360, 16, 16, 16, 16);
            Reject(() => StoreDetailProbeEvidence.RequireViewport(1240, 1200, 744, 744, 16, 16, 16, 16));
            Reject(() => StoreDetailProbeEvidence.RequireViewport(1240, 1240, 744, 744, 0, 16, 16, 16));
            Reject(() => StoreDetailProbeEvidence.RequireViewport(1240, 1240, 744, 700, 16, 16, 16, 16));
            Reject(() => StoreDetailProbeEvidence.RequireViewport(1240, 1240, 744, 744, 16, 16, double.NaN, 16));
        });
        Test("商店运行判据：首卡不得覆盖封面、剧照或预告控制", () =>
        {
            StoreDetailProbeEvidence.RequireClearance(270, 286, 350, 418, 0, 434, 16);
            StoreDetailProbeEvidence.RequireClearance(270, 286, 350, 418, 490, 506, 16);
            Reject(() => StoreDetailProbeEvidence.RequireClearance(270, 278, 350, 418, 0, 434, 16));
            Reject(() => StoreDetailProbeEvidence.RequireClearance(270, 286, 350, 418, 0, 417, 16));
            Reject(() => StoreDetailProbeEvidence.RequireClearance(270, 286, 350, 418, 490, 434, 16));
        });
        Test("商店运行判据：保留三向渐隐，缺右侧与误加顶部必须失败", () =>
        {
            var valid = new StoreFadeRead(128, 128, 0, 0, 0, 240, 120, 100, 240);
            StoreDetailProbeEvidence.RequireMask(valid);
            Reject(() => StoreDetailProbeEvidence.RequireMask(valid with { Right = 255 }));
            Reject(() => StoreDetailProbeEvidence.RequireMask(valid with { NearRight = 240 }));
            Reject(() => StoreDetailProbeEvidence.RequireMask(valid with { Top = 0 }));
            var bytes = new byte[4 * 4 * 4];
            bytes[(1 * 4 + 0) * 4 + 3] = 11;
            bytes[(1 * 4 + 3) * 4 + 3] = 22;
            bytes[(3 * 4 + 2) * 4 + 3] = 33;
            var read = StoreDetailProbeEvidence.ReadMask(bytes, 4, 4);
            Assert.Equal(11, read.Left);
            Assert.Equal(22, read.Right);
            Assert.Equal(33, read.Bottom);
            Reject(() => StoreDetailProbeEvidence.ReadMask(bytes[..^1], 4, 4));
        });
        Test("商店运行判据：长简介必须按实测行高折叠并撤掉裁切圆角", () =>
        {
            StoreDetailProbeEvidence.RequireOverview(3, 3, true, true);
            Reject(() => StoreDetailProbeEvidence.RequireOverview(2, 3, true, true));
            Reject(() => StoreDetailProbeEvidence.RequireOverview(3, 3, false, true));
            Reject(() => StoreDetailProbeEvidence.RequireOverview(3, 3, true, false));
        });
        Test("商店首字实屏：匹配完整笔画、拒绝左上缺角和空帧", () =>
        {
            const int side = 12;
            var reference = new byte[side * side * 4];
            var screen = new byte[side * side * 4];
            for (var y = 1; y < side - 1; y++)
                for (var x = 1; x < side - 1; x++)
                {
                    var index = (y * side + x) * 4;
                    reference[index] = reference[index + 1] = reference[index + 2] = 240;
                    reference[index + 3] = 255;
                    screen[index] = screen[index + 1] = screen[index + 2] = 240;
                    screen[index + 3] = 255;
                }
            var valid = StoreDetailProbeEvidence.ReadGlyph(reference, side, side, screen, side, side, 0, 0, side, side);
            StoreDetailProbeEvidence.RequireGlyph(valid);
            for (var y = 1; y < 5; y++)
                for (var x = 1; x < 5; x++)
                    Array.Clear(screen, (y * side + x) * 4, 4);
            var clipped = StoreDetailProbeEvidence.ReadGlyph(reference, side, side, screen, side, side, 0, 0, side, side);
            Reject(() => StoreDetailProbeEvidence.RequireGlyph(clipped));
            Array.Clear(screen);
            Reject(() => StoreDetailProbeEvidence.RequireGlyph(
                StoreDetailProbeEvidence.ReadGlyph(reference, side, side, screen, side, side, 0, 0, side, side)));
            Reject(() => StoreDetailProbeEvidence.ReadGlyph(reference, side, side, screen, side, side, -1, 0, side, side));
        });
        Test("商店顶部探针：唯一分区、整套与检查台接线，实屏同帧且无播放后端", () =>
        {
            var source = Read("ShellNavigationProbe.cs");
            Assert.Equal(2, source.Split("StoreDetailBackdropAsync, 90, section: ShellProbeSections.DetailCards", StringSplitOptions.None).Length - 1);
            Assert.Contains("\"InspectStoreDetailBackdrop\"", Read("ShellNavigationProbe.Inspect.cs"));
            var probe = Read("ShellNavigationProbe.StoreDetailBackdrop.cs");
            Assert.Contains("page.SetStorePreviewActive(false);", probe);
            Assert.Contains("series.MediaSources = episode.MediaSources;", probe);
            Assert.Contains("fixture.Actions.Plays == 0", probe);
            Assert.Contains("StoreDetailProbeEvidence.RequireClearance", probe);
            Assert.Contains("StoreDetailProbeEvidence.RequireGlyph", probe);
            Assert.Contains("await screen.SaveAsync", probe);
            Assert.Contains("finally { _window.TopMost = topMost; }", probe);
            Assert.False(probe.Contains("StartStoreMedia", StringComparison.Ordinal));
            Assert.False(probe.Contains("StartStoreTrailerAsync", StringComparison.Ordinal));
        });
    }

    private static void Reject(Action action)
    {
        var rejected = false;
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { rejected = true; }
        Assert.True(rejected, "错误读数被商店运行判据接受");
    }

    private static string Read(string path)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Momoka.sln")))
                return File.ReadAllText(Path.Combine(folder.FullName, "src", "Momoka.Shell", path));
        throw new AssertionException("找不到当前测试工作树");
    }
}

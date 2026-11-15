using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MotionProbeEvidenceTests
{
    public static void Register()
    {
        MotionScreenSamplerTests.Register();
        MotionScreenCaptureTests.Register();
        Test("动效裁切判据：与同帧整客户区读数一致，含屏幕负坐标和区域外噪声", () =>
        {
            var full = new byte[10 * 8 * 4];
            var crop = new byte[5 * 5 * 4];
            var bounds = MotionCaptureBounds.Create(-1200, -100, 10, 8, new(3, 2, 7, 6));
            full[1] = 255;
            for (var y = 3; y <= 4; y++)
                for (var x = 4; x <= 6; x++)
                {
                    full[(y * 10 + x) * 4 + 1] = 240;
                    crop[((y - 2) * 5 + x - 3) * 4 + 1] = 240;
                }
            var whole = MotionProbeEvidence.Read(full, 10, 8, true, 30, true, bounds.Area);
            var cropped = MotionProbeEvidence.ReadCrop(crop, 5, 5, true, 30, true, bounds);
            Assert.Equal(whole, cropped);
            Assert.Equal(-1197, bounds.ScreenLeft);
            Assert.Equal(-98, bounds.ScreenTop);
            Assert.Equal(6, cropped.Pixels);
            Assert.False(MotionProbeEvidence.SamePresentation(cropped with { Top = cropped.Top + 2 }, cropped));
            Assert.True(MotionProbeEvidence.SamePresentation(cropped with { Top = cropped.Top + 1 }, cropped));
        });
        Test("动效裁切判据：端点为实际单像素，空帧保留全客户区哨兵，错裁切尺寸拒绝", () =>
        {
            var bounds = MotionCaptureBounds.Create(0, 0, 10, 8, new(9, 7, 9, 7));
            var pixels = new byte[] { 0, 255, 0, 255 };
            var read = MotionProbeEvidence.ReadCrop(pixels, 1, 1, true, 10, false, bounds);
            Assert.Equal(9, read.Left);
            Assert.Equal(7, read.Top);
            Assert.Equal(9, read.Right);
            Assert.Equal(7, read.Bottom);
            pixels[1] = 0;
            var empty = MotionProbeEvidence.ReadCrop(pixels, 1, 1, true, 11, false, bounds);
            Assert.False(empty.Visible);
            Assert.Equal(10, empty.Left);
            Assert.Equal(8, empty.Top);
            Assert.Equal(-1, empty.Right);
            Assert.Throws<ArgumentException>(() => MotionProbeEvidence.ReadCrop(pixels, 2, 1, true, 10, false, bounds));
            Assert.Throws<ArgumentException>(() => MotionProbeEvidence.ReadCrop(pixels, 1, 2, true, 10, false, bounds));
        });
        Test("动效候选路由：全部成功按原顺序执行且通过", () =>
        {
            var attempted = new List<string>();
            MotionProbeEvidence.RunRoutesAsync(["video-library", "music-library"], route =>
            {
                attempted.Add(route);
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            Assert.Equal("video-library,music-library", string.Join(",", attempted));
        });
        Test("动效候选路由：首路失败保留原因、执行音乐并整体拒绝", () =>
        {
            var attempted = new List<string>();
            var preserved = new List<string>();
            var error = Assert.Catch<InvalidOperationException>(() => MotionProbeEvidence.RunRoutesAsync(
                ["video-library", "music-library"], route =>
                {
                    attempted.Add(route);
                    try
                    {
                        if (route == "video-library") throw new InvalidOperationException("history-transition-mismatch");
                        return Task.CompletedTask;
                    }
                    finally { preserved.Add(route); }
                }).GetAwaiter().GetResult());
            Assert.Equal("video-library,music-library", string.Join(",", attempted));
            Assert.Equal("video-library,music-library", string.Join(",", preserved));
            Assert.Contains("video-library：InvalidOperationException：history-transition-mismatch", error.Message);
        });
        Test("动效候选路由：两路异步失败均保留类型、原因和路由", () =>
        {
            var attempted = new List<string>();
            var error = Assert.Catch<InvalidOperationException>(() => MotionProbeEvidence.RunRoutesAsync(
                ["video-library", "music-library"], route =>
                {
                    attempted.Add(route);
                    return Task.FromException(route == "video-library"
                        ? new TimeoutException("no-rendered-frame") : new IOException("evidence-write-failed"));
                }).GetAwaiter().GetResult());
            Assert.Equal("video-library,music-library", string.Join(",", attempted));
            Assert.Contains("video-library：TimeoutException：no-rendered-frame", error.Message);
            Assert.Contains("music-library：IOException：evidence-write-failed", error.Message);
        });
        Test("动效候选路由：第一路在等待时不会启动第二路", () =>
        {
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attempted = new List<string>();
            var second = false;
            var run = MotionProbeEvidence.RunRoutesAsync(["video-library", "music-library"], route =>
            {
                attempted.Add(route);
                if (route == "video-library") return first.Task;
                second = true;
                return Task.CompletedTask;
            });
            Assert.False(run.IsCompleted);
            Assert.False(second);
            Assert.Equal("video-library", string.Join(",", attempted));
            first.SetResult();
            run.GetAwaiter().GetResult();
            Assert.True(second);
            Assert.Equal("video-library,music-library", string.Join(",", attempted));
        });
        Test("动效实际像素：BGRA绿色标记位置、颜色与尺寸严格读取", () =>
        {
            var pixels = new byte[8 * 8 * 4];
            for (var y = 2; y < 5; y++)
                for (var x = 1; x < 4; x++) pixels[(y * 8 + x) * 4 + 1] = 240;
            var frame = MotionProbeEvidence.Read(pixels, 8, 8, true, 10, false);
            Assert.Equal(9, frame.Pixels);
            Assert.Equal(1, frame.Left);
            Assert.Equal(2, frame.Top);
            Assert.Equal(3, frame.Right);
            Assert.Equal(4, frame.Bottom);
            Assert.Equal(240d, frame.Green);
            Assert.Throws<ArgumentException>(() => MotionProbeEvidence.Read(pixels, 9, 8, true, 10, false));
            Assert.Throws<ArgumentException>(() => MotionProbeEvidence.Read(pixels, 8, 8, true, -1, false));
        });
        Test("动效实际像素：黑帧与错通道不冒充可见标记", () =>
        {
            var pixels = new byte[4];
            pixels[0] = 255;
            Assert.False(MotionProbeEvidence.Read(pixels, 1, 1, true, 0, false).Visible);
            pixels[0] = pixels[1] = pixels[2] = 255;
            Assert.False(MotionProbeEvidence.Read(pixels, 1, 1, true, 0, false).Visible);
        });
        Test("动效实际像素：区域外绿色噪声不得污染页面标记", () =>
        {
            var pixels = new byte[8 * 8 * 4];
            pixels[(2 * 8 + 2) * 4 + 1] = 255;
            pixels[(6 * 8 + 6) * 4 + 1] = 255;
            var read = MotionProbeEvidence.Read(pixels, 8, 8, true, 0, false, new(1, 1, 3, 3));
            Assert.Equal(1, read.Pixels);
            Assert.Equal(2, read.Left);
            Assert.Equal(2, read.Top);
            Assert.Throws<ArgumentException>(() => MotionProbeEvidence.Read(pixels, 8, 8, true, 0, false, new(-1, 1, 3, 3)));
        });
        Test("动效候选：捕到中段后关闭仍移动，保留现有方案", () =>
        {
            var decision = Decide([F(0, 20), F(16, 16, true), F(32, 12, true), F(48, 10, true), F(800)]);
            Assert.Equal("retained", decision.Outcome);
            Assert.Equal("in-flight-transition-did-not-settle-on-next-presented-frames", decision.Reason);
            Assert.False(decision.DisableSettled);
        });
        Test("动效候选：完整中段、关闭和历史读数也只批准候选", () =>
        {
            var decision = Decide([F(0, 20), F(16, 10, true), F(32, 10, true), F(48, 10, true), F(800)]);
            Assert.Equal("candidate-only", decision.Outcome);
            Assert.True(decision.NativeIntermediate && decision.DisableSettled && decision.HistorySettled);
            Assert.True(decision.Reason.Contains("system-change-still-required", StringComparison.Ordinal));
        });
        Test("动效候选：未捕中段、关闭前无动画和帧不稳都不算迁移通过", () =>
        {
            var baseline = new[] { F(0), F(16), F(32), F(800) };
            var disabled = new[] { F(0, 20), F(16, 10, true), F(32, 10, true), F(48, 10, true), F(800) };
            Assert.Equal("inconclusive", MotionProbeEvidence.Evaluate(baseline, baseline, disabled, baseline).Outcome);
            Assert.Equal("inconclusive", MotionProbeEvidence.Evaluate(baseline, [F(0, 20), F(16), F(800)],
                [F(0), F(16, 10, true), F(32, 10, true), F(48, 10, true)], baseline).Outcome);
            Assert.Equal("inconclusive", MotionProbeEvidence.Evaluate([F(0) with { Stable = false }, F(16), F(800)],
                baseline, disabled, baseline).Outcome);
        });
        Test("动效候选：历史开关关闭后仍重播的像素必须保留", () =>
        {
            var baseline = new[] { F(0), F(16), F(32), F(800) };
            var decision = MotionProbeEvidence.Evaluate(baseline, [F(0, 20), F(16), F(800)],
                [F(0, 20), F(16, 10, true), F(32, 10, true), F(48, 10, true), F(800)],
                [F(0, 20), F(16, 18), F(32), F(800)]);
            Assert.Equal("retained", decision.Outcome);
            Assert.False(decision.HistorySettled);
        });
        Test("动效候选：无下一帧证据、乱序时间与首内容延迟不会放行", () =>
        {
            var baseline = new[] { F(0), F(16), F(32), F(800) };
            Assert.Equal("inconclusive", MotionProbeEvidence.Evaluate([F(16), F(0), F(800)], baseline, baseline, baseline).Outcome);
            var decision = MotionProbeEvidence.Evaluate(baseline, [F(0, 20) with { Pixels = 0 }, F(100, 20), F(800)],
                [F(0, 20), F(16, 10, true), F(32, 10, true), F(48, 10, true), F(800)], baseline);
            Assert.Equal("retained", decision.Outcome);
            Assert.False(decision.FirstContentComparable);
            Assert.Equal("retained", Decide([F(0, 20), F(16, 10, true), F(800)]).Outcome);
        });
        Test("动效候选：原生和历史最终帧仍错误不能只凭可见通过", () =>
        {
            var baseline = new[] { F(0), F(16), F(32), F(800) };
            Assert.Equal("candidate-final-presentation-differs-from-control", MotionProbeEvidence.Evaluate(baseline,
                [F(0, 20), F(16, 16), F(800, 14)], baseline, baseline).Reason);
            Assert.Equal("candidate-final-presentation-differs-from-control", MotionProbeEvidence.Evaluate(baseline,
                [F(0, 20), F(16), F(800)], baseline, [F(0), F(16), F(800, 14)]).Reason);
        });
        Test("动效候选：首轮完整实帧拒绝中途关闭后仍在移动，缺历史另报证据不足", () =>
        {
            // PID36764首轮匿名video-library三个JSON的完整实帧；没有伪造该轮失败前未采到的历史。
            var baseline = FirstNativeRun.Baseline;
            var native = FirstNativeRun.Entrance;
            var disabled = FirstNativeRun.Disabled;
            var after = disabled.Where(frame => frame.AfterDisable).Take(3).ToArray();
            Assert.Equal(105, after[0].Top);
            Assert.Equal(103, after[1].Top);
            Assert.Equal(102, after[2].Top);
            Assert.True(native.Any(frame => frame.Visible && !MotionProbeEvidence.SamePresentation(frame, baseline[^1])));
            Assert.False(after.All(frame => MotionProbeEvidence.SamePresentation(frame, baseline[^1])));
            // 单独校验已观察到的中断拒绝条件，受控历史只验证判定路径，不作为首轮历史实测。
            var controlledHistory = new[] { baseline[^1] with { AtMs = 0 }, baseline[^1] with { AtMs = 16 }, baseline[^1] with { AtMs = 800 } };
            var decision = MotionProbeEvidence.Evaluate(baseline, native, disabled, controlledHistory);
            Assert.Equal("retained", decision.Outcome);
            Assert.Equal("in-flight-transition-did-not-settle-on-next-presented-frames", decision.Reason);
            Assert.False(decision.DisableSettled);
            Assert.False(decision.FirstContentComparable);
            Assert.Equal("inconclusive", MotionProbeEvidence.Evaluate(baseline, native, disabled, []).Outcome);
        });
    }

    private static MotionMarkerFrame F(double at, int y = 10, bool disabled = false) =>
        new(at, disabled, true, 10, y, 29, y + 9, 200, 255);

    private static MotionCandidateDecision Decide(IReadOnlyList<MotionMarkerFrame> disabled) =>
        MotionProbeEvidence.Evaluate([F(0), F(16), F(32), F(800)], [F(0, 20), F(16, 16), F(800)],
            disabled, [F(0), F(16), F(32), F(800)]);

    private static class FirstNativeRun
    {
        internal static readonly MotionMarkerFrame[] Baseline =
        [
            new(77.8313, false, true, 1000, 720, -1, -1, 0, 0),
            new(313.0992, false, true, 80, 100, 175, 127, 2688, 255),
            new(337.5539, false, true, 80, 100, 175, 127, 2688, 255),
            new(350.7101, false, true, 80, 100, 175, 127, 2688, 255),
            new(379.7494, false, true, 80, 100, 175, 127, 2688, 255),
            new(394.9298, false, true, 80, 100, 175, 127, 2688, 255),
            new(617.2851, false, true, 80, 100, 175, 127, 2688, 255),
            new(634.8926, false, true, 80, 100, 175, 127, 2688, 255),
            new(648.6757, false, true, 80, 100, 175, 127, 2688, 255),
            new(667.6737, false, true, 80, 100, 175, 127, 2688, 255),
            new(688.6419, false, true, 80, 100, 175, 127, 2688, 255),
            new(887.6448, false, true, 80, 100, 175, 127, 2688, 255)
        ];
        internal static readonly MotionMarkerFrame[] Entrance =
        [
            new(116.0327, false, true, 1000, 720, -1, -1, 0, 0),
            new(131.5708, false, true, 1000, 720, -1, -1, 0, 0),
            new(150.1265, false, true, 1000, 720, -1, -1, 0, 0),
            new(170.5464, false, true, 1000, 720, -1, -1, 0, 0),
            new(394.8429, false, true, 1000, 720, -1, -1, 0, 0),
            new(414.6965, false, true, 80, 105, 175, 133, 2784, 247.31034482758622),
            new(429.6323, false, true, 80, 104, 175, 131, 2688, 254.89285714285714),
            new(447.5566, false, true, 80, 102, 175, 130, 2784, 247.31034482758622),
            new(469.6017, false, true, 80, 101, 175, 129, 2784, 247.29238505747125),
            new(669.0848, false, true, 80, 101, 175, 128, 2688, 253.46428571428572),
            new(683.8604, false, true, 80, 100, 175, 127, 2688, 255),
            new(705.1925, false, true, 80, 100, 175, 127, 2688, 255),
            new(732.4137, false, true, 80, 100, 175, 127, 2688, 255),
            new(760.8855, false, true, 80, 100, 175, 127, 2688, 255),
            new(958.7118, false, true, 80, 100, 175, 127, 2688, 255)
        ];
        internal static readonly MotionMarkerFrame[] Disabled =
        [
            new(93.7328, false, true, 1000, 720, -1, -1, 0, 0),
            new(108.2943, false, true, 1000, 720, -1, -1, 0, 0),
            new(128.8943, false, true, 1000, 720, -1, -1, 0, 0),
            new(149.2478, false, true, 1000, 720, -1, -1, 0, 0),
            new(357.2615, false, true, 1000, 720, -1, -1, 0, 0),
            new(371.8814, false, true, 80, 108, 175, 135, 2688, 253.64285714285714),
            new(392.201, true, true, 80, 105, 175, 133, 2784, 247.31034482758622),
            new(413.8563, true, true, 80, 103, 175, 131, 2784, 247.31034482758622),
            new(434.0709, true, true, 80, 102, 175, 129, 2688, 254.14285714285714),
            new(631.8206, true, true, 80, 101, 175, 128, 2688, 253.21428571428572),
            new(642.8915, true, true, 80, 100, 175, 127, 2688, 255),
            new(663.1243, true, true, 80, 100, 175, 127, 2688, 255),
            new(682.9631, true, true, 80, 100, 175, 127, 2688, 255),
            new(704.6653, true, true, 80, 100, 175, 127, 2688, 255),
            new(886.6241, true, true, 80, 100, 175, 127, 2688, 255)
        ];
    }
}

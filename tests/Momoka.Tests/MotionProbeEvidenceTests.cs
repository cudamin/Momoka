using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MotionProbeEvidenceTests
{
    public static void Register()
    {
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
    }

    private static MotionMarkerFrame F(double at, int y = 10, bool disabled = false) =>
        new(at, disabled, true, 10, y, 29, y + 9, 200, 255);

    private static MotionCandidateDecision Decide(IReadOnlyList<MotionMarkerFrame> disabled) =>
        MotionProbeEvidence.Evaluate([F(0), F(16), F(32), F(800)], [F(0, 20), F(16, 16), F(800)],
            disabled, [F(0), F(16), F(32), F(800)]);
}

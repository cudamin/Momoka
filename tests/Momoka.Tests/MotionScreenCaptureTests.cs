using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MotionScreenCaptureTests
{
    internal static void Register()
    {
        Test("动效真实裁切：只拷贝已发布ROI，原客户区坐标与同帧PNG完整保留", () =>
        {
            var state = NewState();
            var region = new MotionCaptureRegion(new(76, 12, 204, 232), 24.3474);
            var bytes = new byte[129 * 221 * 4];
            for (var y = 96; y < 124; y++)
                for (var x = 16; x < 112; x++) bytes[(y * 129 + x) * 4 + 1] = 255;
            state.Frames.Enqueue(new(129, 221, bytes) { OriginX = 1076, OriginY = 212 });
            var captured = CompositionPlaybackProbe.CaptureMotionFrame(ProbeEvidenceState.Own, 123, region);
            Assert.Equal((1076, 212, 129, 221), state.ScreenCopies.Single());
            Assert.True(ReferenceEquals(region, captured.Region));
            Assert.True(ReferenceEquals(bytes, captured.Pixels.Bytes));
            Assert.True(captured.Pixels.Stable);
            Assert.Equal(1000, captured.Bounds.ClientWidth);
            Assert.Equal(720, captured.Bounds.ClientHeight);
            Assert.Equal(123L, captured.Pixels.At);
            var read = MotionProbeEvidence.ReadCrop(bytes, 129, 221, true, 30, false, captured.Bounds);
            Assert.Equal(92, read.Left);
            Assert.Equal(108, read.Top);
            Assert.Equal(187, read.Right);
            Assert.Equal(135, read.Bottom);
            Assert.Equal(2688, read.Pixels);
            var encoded = captured.SaveAsync("anonymous-crop.png");
            Assert.True(encoded.IsCompletedSuccessfully);
            Assert.True(ReferenceEquals(bytes, state.EncodedBytes));
            Assert.Equal(1, state.ScreenCopies.Count);
            Assert.Equal(8, state.OwnerQueries.Count);
            Assert.True(state.OwnerQueries.Skip(4).All(point => point.X >= 1076 && point.X <= 1204
                && point.Y >= 212 && point.Y <= 432));
        });
        Test("动效真实裁切：未有布局ROI时保留整客户区捕获，禁止猜测绿色标记", () =>
        {
            var state = NewState();
            var bytes = new byte[1000 * 720 * 4];
            state.Frames.Enqueue(new(1000, 720, bytes));
            var captured = CompositionPlaybackProbe.CaptureMotionFrame(ProbeEvidenceState.Own, 2, null);
            Assert.Null(captured.Region);
            Assert.True(captured.Pixels.Stable);
            Assert.Equal((1000, 200, 1000, 720), state.ScreenCopies.Single());
            Assert.Equal(new MotionMarkerRegion(0, 0, 999, 719), captured.Bounds.Area);
        });
        Test("动效真实裁切：窗口移动或全客户区尺寸变化时拒绝稳定，即使ROI字节没变", () =>
        {
            foreach (var moved in new[] { false, true })
            {
                var state = NewState();
                state.Frames.Enqueue(new(129, 221, new byte[129 * 221 * 4]) { OriginX = 1076, OriginY = 212 });
                state.AfterScreenCopy = () => { if (moved) state.CurrentOriginX++; else state.ClientWidth--; };
                var captured = CompositionPlaybackProbe.CaptureMotionFrame(ProbeEvidenceState.Own, 3,
                    new(new(76, 12, 204, 232), 1));
                Assert.False(captured.Pixels.Stable);
                Assert.Equal(1000, captured.Bounds.ClientWidth);
                Assert.Equal(1000, captured.Bounds.ClientLeft);
            }
        });
        Test("动效真实裁切：客户区或ROI遮挡在BitBlt前拒绝，坏读数与坏捕获不重试", () =>
        {
            foreach (var roiOnly in new[] { false, true })
            {
                var state = NewState();
                state.OwnerAt = point => !roiOnly || point.X >= 1076 && point.X <= 1204
                    ? ProbeEvidenceState.Foreign : ProbeEvidenceState.Own;
                Assert.Throws<InvalidOperationException>(() => CompositionPlaybackProbe.CaptureMotionFrame(
                    ProbeEvidenceState.Own, 4, new(new(76, 12, 204, 232), 1)));
                Assert.Equal(0, state.ScreenCopies.Count);
            }
            foreach (var origin in new[] { false, true })
            {
                var state = NewState();
                if (origin) state.OriginReadSucceeds = false;
                else state.ClientRectReadSucceeds = false;
                Assert.Throws<InvalidOperationException>(() => CompositionPlaybackProbe.CaptureMotionFrame(
                    ProbeEvidenceState.Own, 5, null));
                Assert.Equal(0, state.ScreenCopies.Count);
            }
            var failureState = NewState();
            var failure = new IOException("actual copy endpoint failed");
            failureState.CaptureFailure = failure;
            Assert.True(ReferenceEquals(failure, Assert.Catch<IOException>(() => CompositionPlaybackProbe.CaptureMotionFrame(
                ProbeEvidenceState.Own, 6, new(new(76, 12, 204, 232), 1)))));
            Assert.Equal(1, failureState.ScreenCopies.Count);
        });
        Test("动效真实裁切：越界、反向、非法尺寸与坐标溢出在采屏前拒绝", () =>
        {
            foreach (var region in new[]
            {
                new MotionMarkerRegion(-1, 12, 204, 232), new MotionMarkerRegion(76, -1, 204, 232),
                new MotionMarkerRegion(76, 12, 1000, 232), new MotionMarkerRegion(76, 12, 204, 720),
                new MotionMarkerRegion(76, 12, 75, 232), new MotionMarkerRegion(76, 12, 204, 11)
            })
            {
                var state = NewState();
                Assert.Throws<ArgumentException>(() => CompositionPlaybackProbe.CaptureMotionFrame(
                    ProbeEvidenceState.Own, 7, new(region, 1)));
                Assert.Equal(0, state.ScreenCopies.Count);
            }
            Assert.Throws<ArgumentException>(() => MotionCaptureBounds.Create(0, 0, 0, 720, null));
            Assert.Throws<OverflowException>(() => MotionCaptureBounds.Create(int.MaxValue, 0, 1000, 720, new(1, 0, 2, 2)));
            Assert.Throws<OverflowException>(() => MotionCaptureBounds.Create(0, 0, int.MaxValue, int.MaxValue, null));
        });
    }

    private static ProbeEvidenceState NewState()
    {
        var state = new ProbeEvidenceState();
        ProbeEvidenceNative.State = state;
        return state;
    }
}

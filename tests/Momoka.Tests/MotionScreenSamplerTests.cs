using System.Diagnostics;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MotionScreenSamplerTests
{
    internal static void Register()
    {
        Test("动效后台采样：真实链接循环在专用线程运行，捕获开始结束与间隔独立记录", () =>
        {
            using var stop = new CancellationTokenSource();
            var owner = Environment.CurrentManagedThreadId;
            var clock = Stopwatch.StartNew();
            var captures = new List<MotionCaptureTiming>();
            var frames = new List<object>();
            var pixelFrame = new object();
            MotionScreenSampler.RunAsync(() => pixelFrame, (frame, timing) =>
            {
                frames.Add(frame);
                captures.Add(timing);
                if (captures.Count == 3) stop.Cancel();
            }, () => clock.Elapsed.TotalMilliseconds, 1000, 4, stop.Token).GetAwaiter().GetResult();
            Assert.Equal(3, captures.Count);
            Assert.True(captures.All(read => read.ThreadId != owner && read.ThreadId == captures[0].ThreadId));
            Assert.True(frames.All(frame => ReferenceEquals(frame, pixelFrame)));
            Assert.Equal(0, captures[0].Index);
            Assert.Null(captures[0].IntervalMs);
            Assert.True(captures.All(read => read.EndMs >= read.StartMs && read.CaptureMs == read.EndMs - read.StartMs));
            Assert.Equal(captures[1].StartMs - captures[0].StartMs, captures[1].IntervalMs!.Value);
        });
        Test("动效后台采样：期间取消等正在捕获退出，禁止发布迟到帧或继续抓屏", () =>
        {
            using var stop = new CancellationTokenSource();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var count = 0;
            var observed = 0;
            var clock = Stopwatch.StartNew();
            var run = MotionScreenSampler.RunAsync(() =>
            {
                count++;
                entered.Set();
                if (!release.Wait(2000)) throw new TimeoutException("fixture-capture-release");
                return 1;
            }, (_, _) => observed++, () => clock.Elapsed.TotalMilliseconds, 1000, 16, stop.Token);
            try
            {
                Assert.True(entered.Wait(2000));
                stop.Cancel();
                Assert.False(run.IsCompleted);
            }
            finally { release.Set(); }
            run.GetAwaiter().GetResult();
            Assert.Equal(1, count);
            Assert.Equal(0, observed);
        });
        Test("动效后台采样：开始前取消不抓屏，捕获或读数异常仍拒绝不重试洗绿", () =>
        {
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            var count = 0;
            MotionScreenSampler.RunAsync(() => ++count, (_, _) => count++, () => 0,
                1000, 16, stop.Token).GetAwaiter().GetResult();
            Assert.Equal(0, count);
            var captureFailure = new IOException("fixture-gdi-failed");
            var run = MotionScreenSampler.RunAsync<int>(() => throw captureFailure, (_, _) => count++,
                () => 0, 1000, 16, CancellationToken.None);
            Assert.True(ReferenceEquals(captureFailure, Assert.Catch<IOException>(() => run.GetAwaiter().GetResult())));
            Assert.Equal(0, count);
            var scanFailure = new ArgumentException("fixture-pixels-invalid");
            run = MotionScreenSampler.RunAsync(() => 1, (_, _) => throw scanFailure,
                () => 0, 1000, 16, CancellationToken.None);
            Assert.True(ReferenceEquals(scanFailure, Assert.Catch<ArgumentException>(() => run.GetAwaiter().GetResult())));
        });
        Test("动效关闭调度：入队不是执行，真实动作结束之后才有Applied且不重复", () =>
        {
            Action? pending = null;
            var now = 20d;
            var queued = 0;
            var applied = 0;
            var dispatch = new MotionDisableDispatch(action =>
            {
                pending = action;
                queued++;
                return true;
            }, () => { applied++; now = 50; }, () => now);
            dispatch.Request();
            dispatch.Request();
            Assert.Equal(1, queued);
            Assert.Equal(0, applied);
            Assert.Equal(20d, dispatch.Snapshot.RequestedAtMs!.Value);
            Assert.Null(dispatch.Snapshot.AppliedAtMs);
            Assert.True(dispatch.Snapshot.Accepted);
            pending!();
            Assert.Equal(1, applied);
            Assert.Equal(50d, dispatch.Snapshot.AppliedAtMs!.Value);
            pending();
            Assert.Equal(1, applied);
        });
        Test("动效关闭调度：Stop拒绝旧页迟到回调和新的关闭请求", () =>
        {
            Action? pending = null;
            var applied = 0;
            var dispatch = new MotionDisableDispatch(action => { pending = action; return true; }, () => applied++, () => 20);
            dispatch.Request();
            dispatch.Stop();
            pending!();
            dispatch.Request();
            Assert.Equal(0, applied);
            Assert.Null(dispatch.Snapshot.AppliedAtMs);
            Assert.True(dispatch.Snapshot.Stopped);
        });
        Test("动效关闭调度：队列拒绝、抛错和实际UI动作失败均保留错误，不能假报已关闭", () =>
        {
            foreach (var throwQueue in new[] { false, true })
            {
                var dispatch = new MotionDisableDispatch(_ => throwQueue
                    ? throw new IOException("fixture-queue") : false, () => { }, () => 20);
                Assert.Throws<InvalidOperationException>(dispatch.Request);
                Assert.NotNull(dispatch.Snapshot.FailureMessage);
                Assert.Null(dispatch.Snapshot.AppliedAtMs);
            }
            Action? pending = null;
            var failedApply = new MotionDisableDispatch(action => { pending = action; return true; },
                () => throw new InvalidOperationException("fixture-apply"), () => 20);
            failedApply.Request();
            pending!();
            Assert.Throws<InvalidOperationException>(failedApply.ThrowIfFailed);
            Assert.Null(failedApply.Snapshot.AppliedAtMs);
            Assert.Equal("fixture-apply", failedApply.Snapshot.FailureMessage);
        });
        Test("动效关闭调度：慢UI动作不持读数锁，后台Snapshot仍立即可读且Applied尚空", () =>
        {
            Action? pending = null;
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var dispatch = new MotionDisableDispatch(action => { pending = action; return true; }, () =>
            {
                entered.Set();
                if (!release.Wait(2000)) throw new TimeoutException("fixture-apply-release");
            }, () => 20);
            dispatch.Request();
            var apply = Task.Run(() => pending!());
            try
            {
                Assert.True(entered.Wait(2000));
                var read = Task.Run(() => dispatch.Snapshot);
                Assert.True(read.Wait(500), "后台采样再次被UI动作读数锁卡住");
                Assert.Null(read.Result.AppliedAtMs);
            }
            finally { release.Set(); }
            apply.GetAwaiter().GetResult();
            Assert.NotNull(dispatch.Snapshot.AppliedAtMs);
        });
        Test("动效候选：排队三次捕获仍未执行或迟到执行均retained，及时执行保留原严格判据", () =>
        {
            var baseline = new[] { F(0), F(16), F(32), F(800) };
            var native = new[] { F(0, 20), F(16), F(800) };
            var delayed = new[] { F(0, 20), F(16, 18), F(32, 16), F(48, 14),
                F(64, 10, true), F(80, 10, true), F(96, 10, true), F(800) };
            var result = MotionProbeEvidence.Evaluate(baseline, native, delayed, baseline,
                new(1, 60, true, false, null, null));
            Assert.Equal("retained", result.Outcome);
            Assert.Contains("dispatch-delayed", result.Reason);
            result = MotionProbeEvidence.Evaluate(baseline, native, delayed, baseline,
                new(1, null, true, false, null, null));
            Assert.Equal("retained", result.Outcome);
            var timely = new[] { F(0, 20), F(16, 10, true), F(32, 10, true), F(48, 10, true), F(800) };
            Assert.Equal("candidate-only", MotionProbeEvidence.Evaluate(baseline, native, timely, baseline,
                new(1, 8, true, false, null, null)).Outcome);
            Assert.Equal("inconclusive", MotionProbeEvidence.Evaluate(baseline, baseline, timely, baseline,
                new(1, 8, true, false, null, null)).Outcome);
        });
    }

    private static MotionMarkerFrame F(double ms, int top = 10, bool disabled = false) =>
        new(ms, disabled, true, 10, top, 13, top + 3, 16, 255);
}

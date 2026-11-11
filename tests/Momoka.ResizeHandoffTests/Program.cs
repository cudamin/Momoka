using Momoka.Shell.Interop;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;
using System.Collections.Concurrent;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("抓帧在飞时窗口保留原尺寸，暂停确认后才取当前帧", () =>
        {
            var page = Page();
            var pause = new TaskCompletionSource();
            page.ViewModel.Playback.PendingPause = pause;
            page.Window.Begin();
            var original = Native.Outer;
            var result = Size(page, Outer(960, 540));
            Rect(original, result);
            Assert.Equal(0, page.Target.Captures.Count, "暂停尚未确认不能抓播放中的旧帧");
            Assert.Equal(0, Native.Applied.Count);
            Assert.False(page.Capture.IsCompleted);
            pause.SetResult();
            _ui.Drain();
            Assert.Equal(1, page.Target.Captures.Count);
            Assert.True(page.Target.Trace.SequenceEqual(["interactive", "pause", "capture"]));
            Assert.Equal(0, Native.Applied.Count);
            page.Target.Captures[0].SetResult(new FrameBoundary(37));
            Complete(page.Capture);
            Assert.Equal(960, Native.Client.Width);
            Assert.Equal(540, Native.Client.Height);
            Assert.Equal(37, page.Overlay!.Frame.Content);
            Assert.Equal(1, VideoFrameOverlay.Flushes);
            page.Window.End(); Complete(page.Change);
        });
        Test("仅移动与原尺寸回报不预抓、不暂停", () =>
        {
            var page = Page();
            page.Window.Begin();
            Rect(Native.Outer, Size(page, Native.Outer));
            Assert.Equal(0, page.Target.Captures.Count);
            Assert.Equal(0, page.ViewModel.Playback.Pauses.Count);
            page.Window.End(); Complete(page.Change);
            Assert.Equal(0, page.Target.Captures.Count);
            Assert.Equal(0, page.ViewModel.Playback.Pauses.Count);
        });
        Test("抓帧同步完成仍等窗口登记待矩形后恢复，不留下首次目标", () =>
        {
            var page = Page(); page.ViewModel.Paused = true;
            page.Target.CompletedFrame = new FrameBoundary(41);
            page.Window.Begin();
            var before = Native.Outer;
            var result = page.Window.Size(Outer(960, 540));
            Rect(before, result);
            Assert.Equal(0, page.Target.Captures.Count, "同步帧端点也必须延后至窗口回调退出");
            Assert.NotNull(page.Window.Pending);
            Complete(page.Capture);
            Assert.Equal(960, Native.Client.Width);
            Assert.Null(page.Window.Pending);
            Assert.Equal(41, page.Overlay!.Frame.Content);
            page.Window.End(); Complete(page.Change);
        });
        Test("快速放大后回到原尺寸，完成抓帧采用最新目标", () =>
        {
            var page = Page();
            page.Window.Begin();
            var original = Native.Outer;
            Size(page, Outer(960, 540));
            Size(page, original);
            Assert.Equal(1, page.Target.Captures.Count);
            Rect(original, page.Window.Pending!.Value);
            page.Target.Captures[0].SetResult(new FrameBoundary(1)); Complete(page.Capture);
            Rect(original, Native.Outer);
            Assert.Equal(640, page.Overlay!.Shows[0].Width);
            Assert.Equal(360, page.Overlay.Shows[0].Height);
            page.Window.End(); Complete(page.Change);
        });
        Test("快速更新尺寸复用单次抓帧与覆盖层，不逐步刷DWM", () =>
        {
            var page = Page();
            page.Window.Begin();
            Size(page, Outer(960, 540));
            Size(page, Outer(1280, 720));
            page.Target.Captures[0].SetResult(new FrameBoundary(2)); Complete(page.Capture);
            Assert.Equal(1280, Native.Client.Width);
            Size(page, Outer(800, 450));
            Assert.Equal(800, Native.Client.Width);
            Assert.Equal(1, page.Target.Captures.Count);
            Assert.Equal(1, VideoFrameOverlay.Created.Count);
            Assert.Equal(1, VideoFrameOverlay.Flushes);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true]));
            page.Window.End(); Complete(page.Change);
        });
        Test("抓帧未完成离页取消待矩形，迟到帧不回填", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            var capture = page.Capture;
            page.Leave(); Complete(page.Change);
            Assert.Null(page.Window.Pending);
            page.Target.Captures[0].SetResult(new FrameBoundary(3)); Complete(capture);
            Assert.Null(page.Overlay);
            Assert.Equal(0, VideoFrameOverlay.Created.Count);
            Assert.Equal(0, Native.Applied.Count);
        });
        Test("抓帧未完成松手再拖，旧完成不接管新目标", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            var oldCapture = page.Capture;
            page.Window.End(); Complete(page.Change);
            page.Window.Begin(); Size(page, Outer(800, 450));
            var currentCapture = page.Capture;
            page.Target.Captures[0].SetResult(new FrameBoundary(4)); Complete(oldCapture);
            Assert.Equal(0, Native.Applied.Count);
            Assert.Null(page.Overlay);
            Assert.NotNull(page.Window.Pending);
            page.Target.Captures[1].SetResult(new FrameBoundary(5)); Complete(currentCapture);
            Assert.Equal(800, Native.Client.Width);
            Assert.Equal(5, page.Overlay!.Frame.Content);
            page.Window.End(); Complete(page.Change);
        });
        Test("抓帧在飞期间原生文件换代取消待目标，不覆盖新片", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            var capture = page.Capture;
            page.ViewModel.SwapFile();
            page.Target.Captures[0].SetResult(new FrameBoundary(6)); Complete(capture);
            Assert.Null(page.Window.Pending);
            Assert.Null(page.Overlay);
            Assert.Equal(0, Native.Applied.Count);
            Assert.True(page.CaptureFailed);
            page.Window.End(); Complete(page.Change);
        });
        Test("同片菜单更新仍完成暂停帧交接", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            page.ViewModel.RefreshMenus();
            page.Target.Captures[0].SetResult(new FrameBoundary(7)); Complete(page.Capture);
            Assert.Equal(960, Native.Client.Width);
            Assert.NotNull(page.Overlay);
            page.Window.End(); Complete(page.Change);
        });
        Test("抓帧异常释放待窗口，回退后的拖动不重复抓帧", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            page.Target.Captures[0].SetException(new InvalidOperationException("Anonymous GPU readback failure"));
            Complete(page.Capture);
            Assert.True(page.CaptureFailed);
            Assert.Null(page.Overlay);
            Assert.Null(page.Window.Pending);
            Assert.Equal(960, Native.Client.Width);
            Size(page, Outer(800, 450));
            Assert.Equal(800, Native.Client.Width);
            Assert.Equal(1, page.Target.Captures.Count);
            page.Window.End(); Complete(page.Change);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true, false]));
        });
        Test("覆盖层失败释放自身并回退，不锁死窗口", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            VideoFrameOverlay.ThrowOnShow = true;
            page.Target.Captures[0].SetResult(new FrameBoundary(8)); Complete(page.Capture);
            Assert.True(page.CaptureFailed);
            Assert.Null(page.Overlay);
            Assert.True(VideoFrameOverlay.Created[0].Disposed);
            Assert.Equal(960, Native.Client.Width);
            Assert.Null(page.Window.Pending);
            page.Window.End(); Complete(page.Change);
        });
        Test("暂停拒绝时不抓活动帧，允许窗口缩放并不补发恢复", () =>
        {
            var page = Page(); page.ViewModel.Playback.RejectPause = true;
            page.Window.Begin(); Size(page, Outer(960, 540)); Complete(page.Capture);
            Assert.Equal(0, page.Target.Captures.Count);
            Assert.True(page.CaptureFailed);
            // Synchronously completed capture failure is allowed by WM_SIZING itself.
            Size(page, Outer(800, 450));
            Assert.Equal(800, Native.Client.Width);
            page.Window.End(); Complete(page.Change);
            Assert.Equal(0, page.ViewModel.Playback.Pauses.Count);
        });
        Test("收尾等待期间新拖动接手，不撤覆盖层、不放开冻结账", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            page.Target.Captures[0].SetResult(new FrameBoundary(9)); Complete(page.Capture);
            var overlay = page.Overlay!;
            page.Target.IsContentReady = false;
            page.Window.End();
            var oldFinish = page.Change;
            Assert.False(oldFinish.IsCompleted);
            page.Window.Begin(); Size(page, Outer(800, 450));
            Complete(oldFinish);
            Assert.True(page.Target.IsInteractiveResize);
            Assert.True(ReferenceEquals(overlay, page.Overlay));
            Assert.False(overlay.Disposed);
            Assert.True(page.Frozen);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true]));
            page.Target.IsContentReady = true;
            page.Window.End(); Complete(page.Change);
            Assert.True(overlay.Disposed);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true, false]));
        });
        Test("收尾回补帧在飞时新拖动的层不被旧回填覆盖", () =>
        {
            var page = Page();
            page.Target.IsContentReady = false;
            page.Window.Begin(); page.Window.End();
            var oldFinish = page.Change;
            Assert.Equal(1, page.Target.Captures.Count);
            page.Window.Begin(); Size(page, Outer(800, 450));
            page.Target.Captures[1].SetResult(new FrameBoundary(10)); Complete(page.Capture);
            var overlay = page.Overlay!;
            page.Target.Captures[0].SetResult(new FrameBoundary(11)); Complete(oldFinish);
            Assert.True(ReferenceEquals(overlay, page.Overlay));
            Assert.False(overlay.Disposed);
            Assert.Equal(10, page.Overlay!.Frame.Content);
            Assert.True(page.Target.IsInteractiveResize);
            page.Target.IsContentReady = true;
            page.Window.End(); Complete(page.Change);
        });
        Test("原本暂停时抓静止帧，收尾不替用户恢复", () =>
        {
            var page = Page(); page.ViewModel.Paused = true;
            page.Window.Begin(); Size(page, Outer(960, 540));
            page.Target.Captures[0].SetResult(new FrameBoundary(12)); Complete(page.Capture);
            page.Window.End(); Complete(page.Change);
            Assert.Equal(0, page.ViewModel.Playback.Pauses.Count);
            Assert.True(page.ViewModel.Paused);
        });
        Test("拖回原尺寸时缓冲已就绪，仍等最后的呈现提交后撤帧", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            page.Target.Captures[0].SetResult(new FrameBoundary(14)); Complete(page.Capture);
            Size(page, Outer(640, 360));
            var overlay = page.Overlay!;
            var commit = new TaskCompletionSource();
            page.Target.PendingCommit = commit;
            Assert.True(page.Target.IsContentReady);
            page.Window.End();
            var finish = page.Change;
            Assert.False(finish.IsCompleted, "缓冲尺寸相同不代表最终画面已经提交");
            Assert.False(overlay.Disposed);
            Assert.True(page.Frozen);
            Until(() => page.Target.Trace.Contains("commit"));
            Assert.False(overlay.Disposed);
            commit.SetResult(); Complete(finish);
            Assert.True(overlay.Disposed);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true, false]));
        });
        Test("旧提交在飞时第二趟已松手，新收尾保留层不被旧提交撤掉", () =>
        {
            var page = Page();
            page.Window.Begin(); Size(page, Outer(960, 540));
            page.Target.Captures[0].SetResult(new FrameBoundary(13)); Complete(page.Capture);
            var overlay = page.Overlay!;
            var commit = new TaskCompletionSource();
            page.Target.PendingCommit = commit;
            page.Target.IsContentReady = false;
            page.Window.End();
            var oldFinish = page.Change;
            page.Target.IsContentReady = true;
            Until(() => page.Target.Trace.Contains("commit"));
            Assert.False(oldFinish.IsCompleted);
            page.Window.Begin(); Size(page, Outer(800, 450));
            page.Target.IsContentReady = false;
            page.Window.End();
            var currentFinish = page.Change;
            Assert.False(currentFinish.IsCompleted);
            commit.SetResult(); Complete(oldFinish);
            Assert.True(ReferenceEquals(overlay, page.Overlay));
            Assert.False(overlay.Disposed, "第二趟虽已松手，覆盖层仍归它的缓冲收尾持有");
            Assert.True(page.Frozen);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true]));
            page.Target.PendingCommit = null;
            page.Target.IsContentReady = true;
            Complete(currentFinish);
            Assert.True(overlay.Disposed);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true, false]));
        });
        return Run();
    }

    private static PlayerPage Page()
    {
        _ui = new UiQueue();
        SynchronizationContext.SetSynchronizationContext(_ui);
        Native.Reset(); VideoFrameOverlay.Reset();
        return new PlayerPage();
    }

    private static UiQueue _ui = new();

    private static NativeRect Size(PlayerPage page, NativeRect requested)
    {
        var result = page.Window.Size(requested);
        _ui.Drain();
        return result;
    }

    private static NativeRect Outer(int width, int height) => new()
    {
        Left = 92,
        Top = 92,
        Right = 108 + width,
        Bottom = 108 + height
    };

    private static void Rect(NativeRect expected, NativeRect actual)
    {
        Assert.Equal(expected.Left, actual.Left); Assert.Equal(expected.Top, actual.Top);
        Assert.Equal(expected.Right, actual.Right); Assert.Equal(expected.Bottom, actual.Bottom);
    }

    private static void Complete(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5))
            _ui.One(TimeSpan.FromMilliseconds(20));
        Assert.True(task.IsCompleted, "拖边任务未完成");
        _ui.Drain();
        if (task.Exception is { } error) throw error.InnerException!;
    }

    private static void Until(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
            _ui.One(TimeSpan.FromMilliseconds(20));
        Assert.True(condition(), "等待的真实方法端点未到达");
    }

    private sealed class UiQueue : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly AutoResetEvent _posted = new(false);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            _queue.Enqueue(() => callback(state)); _posted.Set();
        }
        internal void Drain() { while (_queue.TryDequeue(out var action)) action(); }
        internal void One(TimeSpan timeout)
        {
            if (_queue.TryDequeue(out var action)) { action(); return; }
            _posted.WaitOne(timeout);
            Drain();
        }
    }
}

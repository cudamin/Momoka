using Momoka.Infrastructure;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Windowing;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class Program
{
    private static void RegisterWindowChanges()
    {
        Test("窗口切换：暂停确认前首次发布当前片，放开同一场播放并丢弃旧窗口请求", () =>
        {
            var page = Page();
            var playback = page.ViewModel.Playback;
            var pause = new TaskCompletionSource();
            playback.PendingPause = pause;

            page.Fullscreen(true);
            var change = page.WindowChange;
            Assert.False(change.IsCompleted);
            Assert.True(playback.PauseRequests.SequenceEqual([true]));
            page.ViewModel.PublishNowPlaying();
            pause.SetResult();
            Complete(change);

            Assert.True(playback.Pauses.SequenceEqual([true, false]));
            Assert.False(page.ViewModel.Paused);
            Assert.Equal(WindowForm.Windowed, page.Window.Form);
            Assert.Equal(0, page.Target.Captures.Count);
        });

        Test("窗口切换：离页时已经冻住同一场播放，迟到的画面不能再改窗口", () =>
        {
            var page = Page();
            page.Fullscreen(true);
            var change = page.WindowChange;
            Assert.True(page.Target.GeometryHeld);
            Assert.True(page.ViewModel.Paused);

            page.LeaveWindow();
            page.Target.Captures[0].SetResult(new FrameBoundary(1));
            Complete(change);

            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true, false]));
            Assert.False(page.Target.GeometryHeld);
            Assert.Equal(WindowForm.Windowed, page.Window.Form);
            Assert.Equal(0, VideoFrameOverlay.Created.Count);
        });

        Test("窗口切换：抓帧期间换片或停播，旧事务不改窗口也不向后续播放发恢复", () =>
        {
            foreach (var invalidate in new Action<PlayerViewModel>[]
            {
                vm => vm.Playback.Generation++,
                vm => vm.SwapFile(),
                vm => vm.Playback.IsPlaying = false
            })
            {
                var page = Page();
                page.Maximize(true);
                var change = page.WindowChange;
                Assert.True(page.ViewModel.Paused);

                invalidate(page.ViewModel);
                page.Target.Captures[0].SetResult(new FrameBoundary(1));
                Complete(change);

                Assert.True(page.ViewModel.Playback.PauseRequests.SequenceEqual([true]));
                Assert.Equal(WindowForm.Windowed, page.Window.Form);
                Assert.False(page.Target.GeometryHeld);
                Assert.Equal(0, VideoFrameOverlay.Created.Count);
            }
        });

        Test("窗口切换：全屏落位后快速反向，两趟共用一次暂停并在最后恢复", () =>
        {
            var page = Page();
            page.Fullscreen(true);
            var change = page.WindowChange;
            page.Target.Captures[0].SetResult(new FrameBoundary(1));
            Until(() => page.Window.Fullscreen);
            page.Fullscreen(false);
            Assert.True(ReferenceEquals(change, page.WindowChange));

            Until(() => page.Target.Captures.Count == 2);
            Assert.True(page.ViewModel.Playback.PauseRequests.SequenceEqual([true]));
            page.Target.Captures[1].SetResult(new FrameBoundary(2));
            Complete(change);

            Assert.True(page.Window.Forms.SequenceEqual([WindowForm.Fullscreen, WindowForm.Windowed]));
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true, false]));
            Assert.False(page.FullscreenFrameVisible);
            Assert.True(VideoFrameOverlay.Created.All(overlay => overlay.Disposed));
        });

        Test("窗口切换：暂停中的反向请求已满足，取消首次形态并恢复一次", () =>
        {
            var page = Page();
            var playback = page.ViewModel.Playback;
            var pause = new TaskCompletionSource();
            playback.PendingPause = pause;
            page.Fullscreen(true);
            var change = page.WindowChange;
            page.Fullscreen(false);

            pause.SetResult();
            Complete(change);

            Assert.True(playback.Pauses.SequenceEqual([true, false]));
            Assert.Equal(0, page.Window.Forms.Count);
            Assert.Equal(0, page.Target.Captures.Count);
        });

        Test("窗口切换：用户原先暂停，最大化完成后保持暂停", () =>
        {
            var page = Page();
            page.ViewModel.Paused = true;
            page.Target.HasAttachedVisual = false;
            page.Maximize(true);
            Complete(page.WindowChange);

            Assert.True(page.ViewModel.Paused);
            Assert.Equal(0, page.ViewModel.Playback.PauseRequests.Count);
            Assert.True(page.Window.Forms.SequenceEqual([WindowForm.Maximized]));
        });

        Test("窗口切换：旧请求失效并等待恢复时，新窗口的请求仍能独立完成", () =>
        {
            var page = Page();
            var playback = page.ViewModel.Playback;
            var resume = new TaskCompletionSource();
            playback.PendingResume = resume;
            var original = page.Window;
            page.Fullscreen(true);
            var change = page.WindowChange;
            page.LeaveWindow();
            page.Target.Captures[0].SetResult(new FrameBoundary(1));
            Until(() => playback.PauseRequests.Count == 2);

            page.ReplaceWindow();
            page.Target.HasAttachedVisual = false;
            page.Maximize(true);
            Assert.Equal(WindowForm.Windowed, page.Window.Form);
            Assert.False(change.IsCompleted);

            resume.SetResult();
            Complete(change);

            Assert.Equal(0, original.Forms.Count);
            Assert.True(page.Window.Forms.SequenceEqual([WindowForm.Maximized]));
            Assert.True(playback.Pauses.SequenceEqual([true, false, true, false]));
        });

        Test("窗口切换：新窗口没有新请求时，不继承旧窗口形态", () =>
        {
            var page = Page();
            var original = page.Window;
            page.Fullscreen(true);
            var change = page.WindowChange;
            page.ReplaceWindow();
            page.Target.Captures[0].SetResult(new FrameBoundary(1));
            Complete(change);

            Assert.Equal(0, original.Forms.Count);
            Assert.Equal(0, page.Window.Forms.Count);
            Assert.True(page.ViewModel.Playback.Pauses.SequenceEqual([true, false]));
        });

        Test("窗口切换：暂停请求被拒时依然切换，收尾不发送恢复", () =>
        {
            var page = Page();
            page.ViewModel.Playback.RejectPause = true;
            page.Target.HasAttachedVisual = false;
            page.Fullscreen(true);
            Complete(page.WindowChange);

            Assert.True(page.Window.Fullscreen);
            Assert.True(page.ViewModel.Playback.PauseRequests.SequenceEqual([true]));
            Assert.Equal(0, page.ViewModel.Playback.Pauses.Count);
        });
    }
}

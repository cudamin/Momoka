using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        ServiceBoundaryTests.Register();
        ResizeFreezeTests.Register();
        Test("R11-001 首次UI发布后仍归同播放场次，拖边恢复", () =>
        {
            var page = new PlayerPage();
            page.Begin();
            page.ViewModel.FirstPublish();
            Complete(page.Release());
            Assert.True(page.Pauses.SequenceEqual([true, false]));
        });
        foreach (var change in new[] { "swap", "gap", "stop" })
            Test("R11-001 换片与停止拒绝恢复旧暂停 " + change, () =>
            {
                var page = new PlayerPage();
                page.Begin();
                if (change == "swap") page.ViewModel.Swap();
                else if (change == "gap") page.ViewModel.NativeSwapGap();
                else page.ViewModel.Stop();
                Complete(page.Release());
                Assert.True(page.Pauses.SequenceEqual([true]));
            });
        Test("R11-001 原本暂停及关闭恢复偏好不发恢复", () =>
        {
            var paused = new PlayerPage(); paused.ViewModel.Paused = true; paused.Begin(); Complete(paused.Release());
            Assert.Equal(0, paused.Pauses.Count);
            var disabled = new PlayerPage(); disabled.ViewModel.ResumeAfterWindowResize = false;
            disabled.Begin(); Complete(disabled.Release()); Assert.True(disabled.Pauses.SequenceEqual([true]));
        });
        Test("R11-001 在飞冻结先确认暂停，再恢复，慢命令不反序", () =>
        {
            var page = new PlayerPage(); var pending = new TaskCompletionSource();
            page.ViewModel.Playback.PendingPause = pending;
            page.Begin();
            var release = page.Release();
            _ = Task.Delay(500).ContinueWith(_ => pending.SetResult(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Complete(release);
            Assert.True(page.Pauses.SequenceEqual([true, false]));
        });
        Test("R11-001 暂停被拒绝不向影片补发恢复", () =>
        {
            var page = new PlayerPage(); page.ViewModel.Playback.RejectPause = true;
            page.Begin(); Complete(page.Release()); Assert.Equal(0, page.Pauses.Count);
        });
        foreach (var key in new[] { Windows.System.VirtualKey.Up, Windows.System.VirtualKey.Down, Windows.System.VirtualKey.PageUp, Windows.System.VirtualKey.PageDown })
            Test("R11-004 统计开启才处理按键，关闭恢复原动作 " + key, () =>
            {
                var page = new PlayerPage(); page.ViewModel.Playback.Scrollable = "true";
                Complete(page.Key(key)); Assert.Equal(0, page.Actions.Count); Assert.Equal(1, page.ViewModel.Playback.Commands.Count);
                page.ViewModel.Playback.Scrollable = "false";
                Complete(page.Key(key)); Assert.Equal(1, page.Actions.Count);
            });
        Test("R11-004 读取在飞时换片不发送旧滚动或兜底动作", () =>
        {
            var page = new PlayerPage(); var pending = new TaskCompletionSource<string?>();
            page.ViewModel.Playback.PendingRead = pending;
            var key = page.Key(Windows.System.VirtualKey.Down);
            page.ViewModel.Swap(); pending.SetResult("true"); Complete(key);
            Assert.Equal(0, page.ViewModel.Playback.Commands.Count); Assert.Equal(0, page.Actions.Count);
        });
        return Run();
    }
    private static void Complete(Task task)
    {
        using var finished = new ManualResetEventSlim();
        _ = task.ContinueWith(_ => finished.Set(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)), "匿名UI按键未结束");
        if (task.Exception is { } error) throw error.InnerException!;
    }
}

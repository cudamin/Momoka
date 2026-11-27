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
        Test("R11-001 原本暂停不发恢复", () =>
        {
            var paused = new PlayerPage(); paused.ViewModel.Paused = true; paused.Begin(); Complete(paused.Release());
            Assert.Equal(0, paused.Pauses.Count);
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
        foreach (var action in new[] { Momoka.Playback.ShortcutCatalog.TakeSkip, Momoka.Playback.ShortcutCatalog.DismissSkip })
            Test("提示自定义键先于焦点按钮处理，按住收起后不触发暂停 " + action, () =>
            {
                var page = new PlayerPage();
                page.ViewModel.ShortcutBindings[action] = "Space";
                page.ViewModel.SkipOffered = true;
                page.PreviewKey(Windows.System.VirtualKey.Space);
                Assert.True(page.Actions.SequenceEqual([action]));
                Assert.Equal(0, page.FocusedDefaults);
                page.PreviewKey(Windows.System.VirtualKey.Space, repeated: true);
                Assert.True(page.Actions.SequenceEqual([action]));
                Assert.Equal(0, page.FocusedDefaults);
                Assert.True(page.ReleasePreviewKey(Windows.System.VirtualKey.Space));
                page.PreviewKey(Windows.System.VirtualKey.Space);
                Assert.True(page.Actions.SequenceEqual([action, "toggle-pause"]));
                Assert.Equal(1, page.FocusedDefaults);
            });
        Test("提示重叠方向键优先于统计与滑条，松手后恢复大步快退", () =>
        {
            var page = new PlayerPage();
            page.ViewModel.ShortcutBindings[Momoka.Playback.ShortcutCatalog.DismissSkip] = "Down";
            page.ViewModel.SkipOffered = true;
            page.ViewModel.Playback.Scrollable = "true";
            page.PreviewKey(Windows.System.VirtualKey.Down);
            Assert.True(page.Actions.SequenceEqual([Momoka.Playback.ShortcutCatalog.DismissSkip]));
            Assert.Equal(0, page.ViewModel.Playback.Commands.Count);
            Assert.Equal(0, page.FocusedDefaults);
            page.PreviewKey(Windows.System.VirtualKey.Down, repeated: true);
            Assert.Equal(0, page.FocusedDefaults);
            Assert.True(page.ReleasePreviewKey(Windows.System.VirtualKey.Down));
            page.ViewModel.Playback.Scrollable = "false";
            Complete(page.Key(Windows.System.VirtualKey.Down));
            Assert.True(page.Actions.SequenceEqual([Momoka.Playback.ShortcutCatalog.DismissSkip, "seek-backward-long"]),
                "松手后应恢复 Down 的默认大步快退，音量减小的默认键是 Ctrl+Down");
        });
        Test("提示不可见不抢普通焦点语义，漏松开后新按下解除占用", () =>
        {
            var page = new PlayerPage();
            page.ViewModel.ShortcutBindings[Momoka.Playback.ShortcutCatalog.TakeSkip] = "Space";
            page.PreviewKey(Windows.System.VirtualKey.Space);
            Assert.True(page.Actions.SequenceEqual(["toggle-pause"]));
            Assert.Equal(1, page.FocusedDefaults);
            page.ViewModel.SkipOffered = true;
            page.PreviewKey(Windows.System.VirtualKey.Space);
            page.PreviewKey(Windows.System.VirtualKey.Space);
            Assert.True(page.Actions.SequenceEqual(["toggle-pause", Momoka.Playback.ShortcutCatalog.TakeSkip, "toggle-pause"]));
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

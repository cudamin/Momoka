using Momoka.Playback;

namespace Momoka.Tests;

internal static partial class PlayerVolumeGestureTests
{
    internal static int RunTracks(string shellPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("R-058 真实Player VM轨道补读/通知：匿名挂起后端，无native、窗口、联网或播放");
        try
        {
            using var shell = new ShellFixture(shellPath, sourceRoot);
            PlayerConstructorBoundary.Register(shell.ViewModel, shell.Dispatcher);
            RegisterTracks(shell);
            return TestHarness.Run();
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void RegisterTracks(ShellFixture shell)
    {
        foreach (var audio in new[] { false, true })
        {
            Case("R-058 同场旧补读在新通知之后不覆盖" + (audio ? "音轨" : "字幕"), async () =>
            {
                await using var state = new State(shell, actual: 37);
                var read = state.Handle.HoldTracks();
                var poll = state.PopulateTracks();
                Assert.True(read.Entered.Task.IsCompleted);
                var old = List(1, audio);
                var current = List(2, audio);
                state.NotifyTracks(current);
                Assert.True(ReferenceEquals(current, state.Tracks));
                read.Release.TrySetResult(old);
                await poll;
                Assert.True(ReferenceEquals(current, state.Tracks), "新通知之后旧采样不能把菜单列表/勾选倒退");
                Assert.Equal(2, state.Tracks.Single(track => track.Selected).Id);
                Assert.Equal(1, state.Handle.TrackReads);
                Assert.Equal(0, state.Handle.Commands.Count, "列表回填不发选择轨道命令");
            });
        }
        Case("R-058 旧采样已回复但UI续体晚到，仍不覆盖先应用通知", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldTracks();
            var poll = state.PopulateTracks();
            read.Release.TrySetResult(List(1, audio: true));
            VolumeContext.WaitForPosts(1);
            var current = List(2, audio: true);
            state.NotifyTracks(current);
            VolumeContext.RunPosts(1);
            await poll;
            Assert.True(ReferenceEquals(current, state.Tracks));
            Assert.Equal(2, state.Tracks.Single(track => track.Selected).Id);
        });
        Case("R-058 有效新通知为空也不得被旧非空补读复活", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldTracks();
            var poll = state.PopulateTracks();
            state.NotifyTracks(Array.Empty<MpvTrack>());
            read.Release.TrySetResult(List(1, audio: false));
            await poll;
            Assert.Equal(0, state.Tracks.Count);
            Assert.Equal(1, state.Handle.TrackReads);
        });
        Case("R-058 无通知时正常初始补读仍可填入轨道", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldTracks();
            var poll = state.PopulateTracks();
            var current = List(1, audio: true);
            read.Release.TrySetResult(current);
            await poll;
            Assert.True(ReferenceEquals(current, state.Tracks));
            Assert.Equal(1, state.Handle.TrackReads);
        });
        Case("R-058 多次同场通知后旧采样不盖最后列表/主字幕勾选", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldTracks();
            var poll = state.PopulateTracks();
            state.NotifyTracks(List(2, audio: false));
            var last = List(3, audio: false);
            state.NotifyTracks(last);
            read.Release.TrySetResult(List(1, audio: false));
            await poll;
            Assert.True(ReferenceEquals(last, state.Tracks));
            Assert.Equal(3, state.Tracks.Single(track => track.Selected).Id);
        });
        foreach (var reuse in new[] { false, true })
        {
            Case("R-058 文件代次改变拒绝旧读回，同句柄=" + reuse, async () =>
            {
                await using var state = new State(shell, actual: 37);
                var oldHandle = state.Handle;
                var read = oldHandle.HoldTracks();
                var poll = state.PopulateTracks();
                state.TrackFileBoundary(reuse);
                var current = List(2, audio: true);
                state.NotifyTracks(current);
                read.Release.TrySetResult(List(1, audio: true));
                await poll;
                Assert.True(ReferenceEquals(current, state.Tracks));
                Assert.Equal(1, oldHandle.TrackReads);
            });
        }
        Case("R-058 停止后挂起读回不能填入空缓存", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldTracks();
            var poll = state.PopulateTracks();
            state.CancelAndDropHandle();
            read.Release.TrySetResult(List(1, audio: true));
            await poll;
            Assert.Equal(0, state.Tracks.Count);
        });
        Case("R-058 旧播放代次通知不能压制当前有效初读", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldTracks();
            var poll = state.PopulateTracks();
            state.NotifyTracks(List(2, audio: true), generation: state.Service.Generation - 1);
            var current = List(1, audio: true);
            read.Release.TrySetResult(current);
            await poll;
            Assert.True(ReferenceEquals(current, state.Tracks));
        });
        Case("R-058 排队旧通知在换片后送达也不盖新片初读", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.QueueTracks(List(1, audio: true));
            state.TrackFileBoundary(reuse: true);
            var read = state.Handle.HoldTracks();
            var poll = state.PopulateTracks();
            var current = List(2, audio: true);
            read.Release.TrySetResult(current);
            await poll;
            state.DeliverTracks();
            Assert.True(ReferenceEquals(current, state.Tracks));
        });
    }

    private static IReadOnlyList<MpvTrack> List(int selected, bool audio) => Enumerable.Range(1, 3)
        .Select(id => new MpvTrack(id, audio ? "audio" : "sub", "jpn", "anonymous-" + id, false, id == selected)).ToArray();
}

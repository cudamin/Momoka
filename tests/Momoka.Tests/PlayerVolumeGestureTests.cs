using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>
/// Executes this tree's compiled PlayerViewModel, generated Volume setter and PlaybackService.
/// Only settings, UI dispatch and a suspended player handle are substitutes; no App or native startup.
/// </summary>
internal static partial class PlayerVolumeGestureTests
{
    internal static int Run(string shellAssemblyPath, string sourceRoot)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("R-053／R-054 真实 VM 音量入口（挂起假后端；无窗口、配置 IO、凭据或播放）");
        try
        {
            using var shell = new ShellFixture(shellAssemblyPath, sourceRoot);
            Register(shell);
            return TestHarness.Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Register(ShellFixture shell)
    {
        Case("R-053 未知音量入口连续 -5/-5 只各发送一次步长", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.Nudge(-5);
            Assert.Equal(95.0, state.Volume);
            Assert.True(read.Entered.Task.IsCompleted);
            state.Nudge(-5);
            Assert.Equal(90.0, state.Volume);
            Assert.Equal(1, state.Handle.Commands.Count, "第二命令必须仍挂在真实服务的串行 gate 后");
            Assert.Null(state.Memory.Pending, "显示意图不是可保存读数");
            read.Release.TrySetResult(32);
            VolumeContext.RunPosts(2);
            state.Trace("连续 -5/-5");
            Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(27.0, state.Handle.Actual);
            Assert.Equal(27.0, state.Volume);
            Assert.Equal(27, state.Memory.Pending);
            state.Flush();
            Assert.Equal("27", state.Settings.SavedText);
        });

        Case("R-053 未知音量入口 -5/+5 反向抵消而不丢第二步", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.Nudge(-5);
            Assert.Equal(95.0, state.Volume);
            state.Nudge(5);
            Assert.Equal(100.0, state.Volume);
            read.Release.TrySetResult(32);
            VolumeContext.RunPosts(2);
            state.Trace("反向 -5/+5");
            Assert.Equal("add:-5,add:5", state.Handle.CommandText);
            Assert.Equal(37.0, state.Handle.Actual);
            Assert.Equal(37.0, state.Volume);
            state.Flush();
            Assert.Equal("37", state.Settings.SavedText);
        });

        Case("R-053 三个未确认手势保留 -5/-5/+5 而非累计显示差", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.Nudge(-5);
            state.Nudge(-5);
            state.Nudge(5);
            Assert.Equal(95.0, state.Volume);
            read.Release.TrySetResult(32);
            VolumeContext.RunPosts(3);
            state.Trace("三步反向");
            Assert.Equal("add:-5,add:-5,add:5", state.Handle.CommandText);
            Assert.Equal(32.0, state.Volume);
            Assert.Equal(32, state.Memory.Pending);
        });

        Case("R-053 未知滑杆连续95/90同样只取每次 old/new 差值", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.SetVolume(95);
            state.SetVolume(90);
            Assert.Equal(90.0, state.Volume);
            read.Release.TrySetResult(32);
            VolumeContext.RunPosts(2);
            Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(27.0, state.Volume);
            Assert.Equal(27, state.Memory.Pending);
        });

        foreach (var boundary in new[] { 0, 130 })
        {
            Case($"R-053 未知显示在{boundary}边界也保留真实手势步长", async () =>
            {
                await using var state = new State(shell, actual: 37);
                state.Status(new PlayerStatus { Loaded = true, Volume = boundary, VolumeKnown = false });
                var delta = boundary == 0 ? -5 : 5;
                var read = state.Handle.HoldRead();
                state.Nudge(delta);
                state.Nudge(delta);
                Assert.Equal((double)boundary, state.Volume);
                read.Release.TrySetResult(37 + delta);
                VolumeContext.RunPosts(2);
                Assert.Equal($"add:{delta},add:{delta}", state.Handle.CommandText);
                Assert.Equal(37.0 + delta * 2, state.Volume);
            });
        }

        Case("R-053 非有限手势和滑杆输入不发命令、不保存", async () =>
        {
            await using var state = new State(shell, actual: 37);
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                state.Nudge(invalid);
                state.SetVolume(invalid);
                Assert.Equal(100.0, state.Volume);
            }
            Assert.Equal(0, state.Handle.Commands.Count);
            Assert.Null(state.Memory.Pending);
            state.Flush();
            Assert.Equal(0, state.Settings.SaveCalls);
        });

        Case("R-053 未知旧状态回填不变成额外命令或第二步基准", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.Nudge(-5);
            state.Status(new PlayerStatus { Loaded = true, Volume = 100, VolumeKnown = false });
            Assert.Equal(1, state.Handle.Commands.Count);
            Assert.False(state.Ready);
            state.Nudge(-5);
            read.Release.TrySetResult(32);
            VolumeContext.RunPosts(2);
            Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(27.0, state.Volume);
        });

        Case("R-053 初次相对手势未读回时旧已知37不能让后一步转绝对", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.Nudge(-5);
            state.Status(Known(37));
            state.Nudge(-5);
            read.Release.TrySetResult(32);
            VolumeContext.RunPosts(2);
            state.Trace("在途旧已知37");
            Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(27.0, state.Volume);
            Assert.Equal(27, state.Memory.Pending);
        });

        Case("R-053 第一读回已确认但同一批还有手势排队时继续相对", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var first = state.Handle.HoldRead();
            state.Nudge(-5);
            var second = state.Handle.HoldRead();
            state.Nudge(-5);
            first.Release.TrySetResult(32);
            VolumeContext.RunPosts(1);
            Assert.True(state.Ready);
            Assert.Equal(90.0, state.Volume, "在途手势显示基准保留，真实读数已记但排空后回填");
            Assert.Equal(32, state.Memory.Pending);
            state.Nudge(-5);
            second.Release.TrySetResult(27);
            VolumeContext.RunPosts(2);
            Assert.Equal("add:-5,add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(22.0, state.Volume);
        });

        Case("R-053 滑杆第一读回后继续95/90/85仍是三个减5", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var first = state.Handle.HoldRead();
            state.SetVolume(95);
            var second = state.Handle.HoldRead();
            state.SetVolume(90);
            first.Release.TrySetResult(32);
            VolumeContext.RunPosts(1);
            Assert.True(state.Ready);
            state.SetVolume(85);
            second.Release.TrySetResult(27);
            VolumeContext.RunPosts(2);
            state.Trace("滑杆读回交错95/90/85");
            Assert.Equal("add:-5,add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(22.0, state.Volume);
            Assert.Equal(22, state.Memory.Pending);
            state.Flush();
            Assert.Equal("22", state.Settings.SavedText);
        });

        Case("R-053 滑杆在途已知状态不改写下一手势的用户基准", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var first = state.Handle.HoldRead();
            state.SetVolume(95);
            state.SetVolume(90);
            state.Status(Known(37));
            Assert.True(state.Ready);
            state.SetVolume(85);
            first.Release.TrySetResult(32);
            VolumeContext.RunPosts(3);
            state.Trace("滑杆旧已知状态95/90/85");
            Assert.Equal("add:-5,add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(22.0, state.Volume);
            Assert.Equal(22, state.Memory.Pending);
        });

        Case("R-053 滑杆首笔读回失败不重置仍在途手势的差值基准", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var first = state.Handle.HoldRead();
            state.SetVolume(95);
            var second = state.Handle.HoldRead();
            state.SetVolume(90);
            first.Release.TrySetResult(null);
            VolumeContext.RunPosts(1);
            Assert.False(state.Ready);
            Assert.Null(state.Memory.Pending);
            state.SetVolume(85);
            second.Release.TrySetResult(27);
            VolumeContext.RunPosts(2);
            state.Trace("滑杆首笔读回失败95/90/85");
            Assert.Equal("add:-5,add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(22.0, state.Volume);
            Assert.Equal(22, state.Memory.Pending);
        });

        Case("R-053 在途滑杆NaN恢复用户值而不改变下一次减5", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var first = state.Handle.HoldRead();
            state.SetVolume(95);
            state.SetVolume(90);
            state.SetVolume(double.NaN);
            Assert.Equal(90.0, state.Volume);
            state.SetVolume(85);
            first.Release.TrySetResult(32);
            VolumeContext.RunPosts(3);
            Assert.Equal("add:-5,add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(22.0, state.Volume);
        });

        Case("R-053 同代两笔读回逆序进入UI时只保留较新成功27", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var first = state.Handle.HoldRead();
            state.Nudge(-5);
            var second = state.Handle.HoldRead();
            state.Nudge(-5);
            first.Release.TrySetResult(32);
            VolumeContext.WaitForPosts(1); // second is held, so the first callback is definitely queued
            Assert.True(second.Entered.Task.IsCompleted);
            second.Release.TrySetResult(27);
            VolumeContext.RunPostsReversed(2); // newest successful reading before the older callback
            state.Trace("同代读回逆序32/27");
            Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(27.0, state.Volume);
            Assert.Equal(27, state.Memory.Pending);
            state.Flush();
            Assert.Equal("27", state.Settings.SavedText);
        });

        Case("R-053 较新读回失败不抹掉逆序返回的较早真实成功32", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var first = state.Handle.HoldRead();
            state.Nudge(-5);
            var second = state.Handle.HoldRead();
            state.Nudge(-5);
            first.Release.TrySetResult(32);
            VolumeContext.WaitForPosts(1);
            second.Release.TrySetResult(null);
            VolumeContext.RunPostsReversed(2);
            Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
            Assert.Equal(32.0, state.Volume);
            Assert.Equal(32, state.Memory.Pending, "只保留实际确认值，不猜失败请求的目标");
        });

        foreach (var failure in new[] { ReadFailure.Null, ReadFailure.NonFinite, ReadFailure.Exception })
        {
            Case($"R-053 连续入口读回 {failure} 不确认或保存显示值", async () =>
            {
                await using var state = new State(shell, actual: 37);
                state.Handle.Failure = failure;
                var read = state.Handle.HoldRead();
                state.Nudge(-5);
                state.Nudge(-5);
                if (failure == ReadFailure.Exception) read.Release.TrySetException(new IOException("fixture read failure"));
                else read.Release.TrySetResult(failure == ReadFailure.NonFinite ? double.NaN : null);
                VolumeContext.RunPosts(2);
                state.Trace("读回失败 " + failure);
                Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
                Assert.False(state.Ready);
                Assert.Null(state.Memory.Pending);
                state.Flush();
                Assert.Equal(0, state.Settings.SaveCalls);
                Assert.Equal(100, state.Settings.Settings.Audio.Volume);
            });
        }

        Case("R-053 后端拒绝挂起的连续入口不保存并允许之后重试", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.Handle.Reject = true;
            var command = state.Handle.HoldCommand();
            state.Nudge(-5);
            state.Nudge(-5);
            command.Release.TrySetResult(false);
            VolumeContext.RunPosts(2);
            Assert.Equal("add:-5,add:-5", state.Handle.CommandText);
            Assert.Null(state.Memory.Pending);
            state.Flush();
            Assert.Equal(0, state.Settings.SaveCalls);
            state.Handle.Reject = false;
            state.Nudge(-5);
            Assert.Equal(32.0, state.Volume);
            Assert.Equal(32, state.Memory.Pending);
        });

        Case("R-053 无控制通道的入口不发命令、不记启动显示值", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.Handle.Control = false;
            state.Nudge(-5);
            state.Nudge(5);
            Assert.Equal(0, state.Handle.Commands.Count);
            Assert.False(state.Ready);
            Assert.Null(state.Memory.Pending);
            state.Flush();
            Assert.Equal(0, state.Settings.SaveCalls);
        });

        foreach (var reuse in new[] { true, false })
        {
            Case($"R-053 读回期间换代（复用句柄={reuse}）丢旧结果及排队动作", async () =>
            {
                await using var state = new State(shell, actual: 37);
                var old = state.Handle;
                var read = old.HoldRead();
                state.Nudge(-5);
                state.Nudge(-5);
                state.Replace(reuse, 84);
                read.Release.TrySetResult(32);
                VolumeContext.RunPosts(2);
                Assert.Equal(1, old.Commands.Count);
                Assert.Equal(84.0, state.Volume);
                Assert.False(state.Ready);
                Assert.Null(state.Memory.Pending);
                state.Nudge(-5);
                Assert.Equal(79.0, state.Volume);
                Assert.Equal(79, state.Memory.Pending);
                Assert.Equal(79.0, state.Handle.Actual);
            });
        }

        Case("R-053 成功读回已排入 UI 后换代仍不能回填新片", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.Nudge(-5);
            read.Release.TrySetResult(32);
            VolumeContext.WaitForPosts(1);
            state.Replace(reuse: true, actual: 84);
            VolumeContext.RunPosts(1);
            Assert.Equal(84.0, state.Volume);
            Assert.False(state.Ready);
            Assert.Null(state.Memory.Pending);
            Assert.Equal(0, state.Settings.SaveCalls);
        });

        Case("R-053 停止取消期间旧读回及排队手势不确认或保存", async () =>
        {
            await using var state = new State(shell, actual: 37);
            var read = state.Handle.HoldRead();
            state.Nudge(-5);
            state.Nudge(-5);
            state.CancelAndDropHandle();
            read.Release.TrySetResult(32);
            VolumeContext.RunPosts(2);
            Assert.Equal(1, state.Handle.Commands.Count);
            Assert.False(state.Ready);
            Assert.Null(state.Memory.Pending);
            Assert.Equal(90.0, state.Volume);
            Assert.Equal(0, state.Settings.SaveCalls);
        });

        Case("R-053 已确认音量仍走绝对分支，连续设置只记最新确认", async () =>
        {
            await using var state = new State(shell, actual: 100);
            state.Prime(100);
            var command = state.Handle.HoldCommand();
            state.Nudge(-5);
            state.Nudge(-5);
            Assert.Equal(90.0, state.Volume);
            command.Release.TrySetResult(true);
            VolumeContext.RunPosts(2);
            Assert.Equal("set:95,set:90", state.Handle.CommandText);
            Assert.Equal(90, state.Memory.Pending);
            state.Flush();
            Assert.Equal("90", state.Settings.SavedText);
        });

        Case("R-054 37→请求60→旧37→确认60后强制结算只能保存60", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.Prime(37);
            var command = state.Handle.HoldCommand();
            state.SetVolume(60);
            state.Status(Known(37));
            command.Release.TrySetResult(true);
            VolumeContext.RunPosts(1);
            Assert.Equal(60, state.Memory.Pending);
            state.Flush();
            state.Trace("旧回声强制结算");
            Assert.Equal("60", state.Settings.SavedText);
            Assert.Equal(60.0, state.Volume);
        });

        Case("R-054 确认前生成的旧状态在确认后 UI 送到也不能保存37", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.Prime(37);
            var command = state.Handle.HoldCommand();
            state.SetVolume(60);
            state.QueueStatus(Known(37));
            command.Release.TrySetResult(true);
            VolumeContext.RunPosts(1);
            state.DeliverStatus();
            state.Flush();
            Assert.Equal("60", state.Settings.SavedText);
            Assert.Equal(60.0, state.Volume);
        });

        Case("R-054 确认后真实70仍可结算，迟送旧37不能覆盖70", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.Prime(37);
            state.SetVolume(60);
            state.QueueStatus(Known(37));
            state.Status(Known(70));
            state.DeliverStatus();
            state.Flush();
            Assert.Equal("70", state.Settings.SavedText);
            Assert.Equal(70.0, state.Volume);
        });

        Case("R-054 更新原生70已落地时迟到60确认不能回填或保存60", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.Prime(37);
            var command = state.Handle.HoldCommand();
            state.SetVolume(60);
            state.Status(Known(60));
            state.Status(Known(70));
            command.Release.TrySetResult(true);
            VolumeContext.RunPosts(1);
            Assert.Equal(70.0, state.Volume);
            Assert.Equal(70, state.Memory.Pending);
            state.Flush();
            Assert.Equal("70", state.Settings.SavedText);
        });

        Case("R-054 保存失败和观察重置保留确认60，不被旧37换回", async () =>
        {
            await using var state = new State(shell, actual: 37);
            state.Prime(37);
            var command = state.Handle.HoldCommand();
            state.SetVolume(60);
            state.Status(Known(37));
            command.Release.TrySetResult(true);
            VolumeContext.RunPosts(1);
            state.Settings.SaveSucceeds = false;
            state.Flush();
            Assert.Equal(60, state.Memory.Pending);
            var revision = state.Memory.Revision;
            state.Memory.ResetObservation();
            Assert.Equal(60, state.Memory.Pending);
            Assert.Equal(revision, state.Memory.Revision);
            state.Settings.SaveSucceeds = true;
            state.Flush();
            Assert.Null(state.Memory.Pending);
            Assert.Equal("60", state.Settings.SavedText);
        });
    }

    private static PlayerStatus Known(double volume) => new() { Loaded = true, VolumeKnown = true, Volume = volume };
    private static void Case(string name, Func<Task> action) => TestHarness.Test(name, () => VolumeContext.Run(action));
}

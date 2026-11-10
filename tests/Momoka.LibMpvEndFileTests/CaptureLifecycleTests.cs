using Momoka.Mpv;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CaptureLifecycleTests
{
    internal static void Register(Action<string, Func<Task>> test)
    {
        test("逐帧截图：文件边界无法停止时退出实例，不继续后继文件", async () =>
        {
            await using var f = new HandleFixture();
            f.Ready();
            await f.Handle.CommandAsync(["screenshot", "video+each-frame"], CancellationToken.None);
            f.Native.CommandResult = command => command[0] == "screenshot" ? -12 : 0;
            f.Dispatch(LibMpvNative.EventStartFile);
            Assert.Equal(1, f.Native.QuitCalls);
            Assert.False(await f.Handle.CommandAsync(["screenshot", "video"], CancellationToken.None));
        });
        test("逐帧截图：换片前只停止，不额外截帧；旧代命令不能重开", async () =>
        {
            await using var f = new HandleFixture(standalone: true);
            f.Ready();
            var old = f.Handle.FileGeneration;
            Assert.True(await f.Handle.CommandForFileAsync(old, ["screenshot", "video+each-frame"], CancellationToken.None));
            f.Handle.HandOver();
            Assert.True(await f.Handle.SwapToAsync(HandleFixture.Request(), CancellationToken.None));
            var shots = f.Native.Commands.Where(command => command[0] == "screenshot").ToArray();
            Assert.Equal(2, shots.Length);
            Assert.Equal("video+each-frame", shots[1][1]);
            Assert.True(f.Native.Commands.IndexOf(shots[1]) < f.Native.Commands.FindIndex(command => command[0] == "loadfile"));
            f.Dispatch(LibMpvNative.EventStartFile);
            Assert.False(await f.Handle.CommandForFileAsync(old, ["screenshot", "video+each-frame"], CancellationToken.None));
            Assert.Equal(2, f.Native.Commands.Count(command => command[0] == "screenshot"));
        });
        test("逐帧截图：写入拒绝也清掉mpv先登记的循环", async () =>
        {
            await using var f = new HandleFixture();
            f.Ready();
            var shots = 0;
            f.Native.CommandResult = command => command[0] == "screenshot" && ++shots == 1 ? -12 : 0;
            Assert.False(await f.Handle.CommandAsync(["screenshot", "subtitles+each-frame"], CancellationToken.None));
            Assert.Equal(2, f.Native.Commands.Count(command => command[0] == "screenshot"));
            f.Dispatch(LibMpvNative.EventStartFile);
            Assert.Equal(2, f.Native.Commands.Count(command => command[0] == "screenshot"));
        });
        test("逐帧截图：单帧操作清模式，下一片不会再toggle而误开", async () =>
        {
            await using var f = new HandleFixture();
            f.Ready();
            await f.Handle.CommandAsync(["screenshot", "video+each-frame"], CancellationToken.None);
            await f.Handle.CommandAsync(["screenshot", "subtitles"], CancellationToken.None);
            f.Dispatch(LibMpvNative.EventStartFile);
            Assert.Equal(2, f.Native.Commands.Count(command => command[0] == "screenshot"));
        });
        test("逐帧截图：原生文件边界清模式，重复结束事件不重复toggle", async () =>
        {
            await using var f = new HandleFixture();
            f.Ready();
            await f.Handle.CommandAsync(["screenshot", "window+each-frame"], CancellationToken.None);
            f.End(LibMpvNative.EndFileRedirect);
            f.Dispatch(LibMpvNative.EventStartFile);
            Assert.Equal(2, f.Native.Commands.Count(command => command[0] == "screenshot"));
        });
    }
}

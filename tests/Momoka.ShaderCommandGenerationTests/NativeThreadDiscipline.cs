using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// R-051 的线程纪律：**调用线程不许等原生读取，换片快路的问题也不许排在它后面。**
/// <para>
/// 2026-10-27 的现场：集成管线开着 ArtCNN 链，D3D11 上第一帧要重编译 23.7 秒；播放页的
/// <c>video-params/w</c> 轮询在 UI 线程上直接进了 libmpv，于是同一段 23.4 秒里 10Hz 心跳一拍都没有 ——
/// 用户说的「Momoka 卡住」。修法是把每条原生读取挪到线程池（<c>ReadAsync</c>），并让只读自己字段的
/// <c>CanSwapTo</c>／<c>HandOver</c>／<c>TryRetainAfterEndOfFile</c> 根本不进闸门。这里钉的就是这两条：
/// 原生端点必须落在**别的**线程上，而闸门被一条卡住的读取占着时，换片问题照样当场有答案。
/// </para>
/// <para>
/// 原生端点仍是合成响应：这些检查证明的是线程归属与等待边界，不证明真实 mpv、GLSL 编译或画面。
/// </para>
/// </summary>
internal static class NativeThreadDiscipline
{
    public static void Register()
    {
        Case("R051 线程：五条原生读取都不在调用线程上执行", ReadsLeaveTheCallerThreadAsync);
        Case("R051 线程：卡住的读取当场交回任务，不占住调用线程", BlockedReadReturnsAtOnceAsync);
        Case("R051 线程：闸门被卡住的读取占着时换片问题不等它", StateQuestionsDoNotQueueAsync);
    }

    private static void Case(string name, Func<Task> run) => Test(name, () => run().GetAwaiter().GetResult());

    private static async Task ReadsLeaveTheCallerThreadAsync()
    {
        await using var f = new HandleFixture();
        var caller = Environment.CurrentManagedThreadId;

        Assert.Equal("bilinear", await f.Handle.GetTextAsync("scale", CancellationToken.None));
        Assert.False(f.Native.LastReadThread == caller, "GetTextAsync 的原生读取跑在调用线程上");

        Assert.Null(await f.Handle.GetNumberAsync("dwidth", CancellationToken.None));
        Assert.False(f.Native.LastReadThread == caller, "GetNumberAsync 的原生读取跑在调用线程上");

        Assert.Equal(0, (await f.Handle.GetTracksAsync(CancellationToken.None)).Count);
        Assert.False(f.Native.LastReadThread == caller, "GetTracksAsync 的原生读取跑在调用线程上");

        Assert.Equal(0, (await f.Handle.GetChaptersAsync(CancellationToken.None)).Count);
        Assert.False(f.Native.LastReadThread == caller, "GetChaptersAsync 的原生读取跑在调用线程上");

        Assert.NotNull(await f.Handle.GetStringListAsync("glsl-shaders", CancellationToken.None));
        Assert.False(f.Native.LastReadThread == caller, "GetStringListAsync 的原生读取跑在调用线程上");
    }

    private static async Task BlockedReadReturnsAtOnceAsync()
    {
        await using var f = new HandleFixture();
        // 300ms 足够让「已经交回的任务」在这一拍还是未完成的；真的占住调用线程时，调用本身会等完这 300ms，
        // 交回来的就是一只已完成的任务 —— 这正是修前修后的分界。
        f.Native.BeforeRead = _ => Thread.Sleep(300);

        var read = f.Handle.GetTextAsync("scale", CancellationToken.None);
        Assert.False(read.IsCompleted, "原生读取占住了调用线程：任务回来时已经做完了");
        Assert.Equal("bilinear", await read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static async Task StateQuestionsDoNotQueueAsync()
    {
        await using var f = new HandleFixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Native.BeforeRead = _ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        var blocked = f.Handle.GetTextAsync("scale", CancellationToken.None);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "卡住的读取没有进到原生端点");
        Assert.False(blocked.IsCompleted);

        try
        {
            // 闸门此刻被那条读取占着。这两个问题只读 handle 自己的字段，必须当场有答案。
            var asked = Task.Run(() => f.Handle.CanSwapTo(HandleFixture.Request("B")));
            Assert.True(asked.Wait(TimeSpan.FromSeconds(2)), "换片快路的判断排在了卡住的原生读取后面");
            Assert.True(asked.Result, "同签名的下一集仍应允许同窗换片");

            var handedOver = Task.Run(f.Handle.HandOver);
            Assert.True(handedOver.Wait(TimeSpan.FromSeconds(2)), "交接排在了卡住的原生读取后面");
        }
        finally
        {
            release.Set();
        }

        Assert.Equal("bilinear", await blocked.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(f.Handle.WasHandedOver);
    }
}

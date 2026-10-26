using System.Reflection;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class ShaderCommandGenerationTests
{
    private static ShaderGroup Group => ShaderGroupCatalog.For(GpuTier.Low)[1];
    public static void Register()
    {
        Case("R051 根因：最后 Current 后、真实 native 捕获前交接，B 无旧写也无回滚", () => RaceAsync(1, false));
        Case("R051 根因：已写一项后入口交接，B 无下一笔旧写", () => RaceAsync(2, false));
        Case("R051 根因：拒绝后逆序回滚入口交接，B 无旧 rollback", () => RaceAsync(3, true));
        Case("R051 真实 UI：旧事务交接后不提交档位/钉选/成功反馈", UiRaceAsync);
        Case("R051 真实 UI：正常成功提交/拒绝保旧/未知失败反馈不变", UiFeedbackAsync);
        Case("R051 真实 native：正常新链最后写入，关闭恢复全部基线", NormalAsync);
        Case("R051 真实 native：缺目标文件零写入", MissingFileAsync);
        Case("R051 真实 native：缺默认和缺快照都 fail closed", MissingReadsAsync);
        Case("R051 真实 native：部分拒绝逆序恢复每一项与无损列表", () => RejectedAsync(false, 4));
        Case("R051 真实 native：部分异常逆序恢复每一项", () => RejectedAsync(true, 4));
        Case("R051 真实 native：链末项被拒后无损旧列表也逆序恢复", () => RejectedAsync(false, 8));
        Case("R051 真实 native：回滚失败状态未知，后续完整切链恢复已知", FailedRollbackAsync);
        Case("R051 生命周期：同句柄多次 Swap 后正常新事务不比较 service/native 计数", SuccessAfterSwapAsync);
        Case("R051 生命周期：交接中不写、Swap 后旧代次不写、新事务可写", HandOverAsync);
        Case("R051 生命周期：native Swap 已完成但 service 未接班时旧事务仍被拒", () => BeforeAdoptionAsync(false));
        Case("R051 生命周期：native 已换片但 service 未接班时不得新绑 B 做事务", () => BeforeAdoptionAsync(true));
        Case("R051 生命周期：关闭等待中的旧事务零 native shader 写", CloseAsync);
        Case("R051 生命周期：新 handle 不接收旧事务", ReplaceAsync);
        Case("R051 native gate：已捕获的普通命令仍在执行点拒绝跨片", OrdinaryQueuedAsync);
        Case("R051 取消：入口取消和枚举停点取消都无 native 写", CancellationAsync);
        Case("R051 取消：真实 native 已捕获后取消仍无写入", QueuedCancellationAsync);
        Case("R051 外部兼容：仅普通 IPlayerControl 的切链与控制结果原样", ExternalControlAsync);
        Case("R051 scoped 控制：同句柄 Swap 后旧捕获拒绝，新捕获接受", ScopedControlAsync);
        Case("R051 scoped 控制：销毁后旧捕获零写入", ScopedDestroyedAsync);
        Case("R051 scoped 控制：入口与实际执行点取消均传播", ScopedCancellationAsync);
        Case("R051 普通控制：真实 handle 命令原参数/结果不变", OrdinaryControlAsync);
        Case("R051 seam：完整真实 handle/gate 与基点方法仍被原样编译", ExtractionAsync);
    }

    private static void Case(string name, Func<Task> run) => Test(name, () => run().GetAwaiter().GetResult());

    private static async Task RaceAsync(int pauseAt, bool rollback)
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        var a = f.NativeHandle.Native;
        if (rollback) a.Result = _ => a.SetAttempts == 2 ? -12 : 0;
        using var pause = new CommandPause();
        f.Handle.PauseAt = pauseAt;
        f.Handle.Pause = pause;
        var generation = f.Service.Generation;
        pause.AtBoundary = () => Assert.Equal(generation, f.Service.Generation, "服务最后 Current 已通过且尚未交接");
        var old = Task.Run(() => f.Service.SetShaderGroupAsync(Group));
        bool result;
        try
        {
            await pause.WaitAsync();
            Assert.Equal(pauseAt - 1, a.SetAttempts, "停在 native 实际入口之前");
            await f.SwapAsync();
            Assert.Equal(2L, f.NativeHandle.NativeGeneration);
            pause.Release();
            result = await old.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { pause.Release(); }
        var writes = a.Commands.Count(command => command.IsSet && command.File == "B");
        Console.WriteLine($"    R051_B_OLD_WRITES={writes}; rollback={rollback}; oldResult={result}; native={f.NativeHandle.NativeGeneration}; service={f.Service.Generation}");
        Assert.False(result, "旧事务必须 false");
        Assert.True(f.Service.ShaderStateKnown, "旧事务不能破坏新片已知状态");
        Assert.Equal(0, writes, "服务检查后同句柄 Swap：B 不得收到旧事务任何 set/rollback");
    }

    private static async Task UiRaceAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        using var pause = new CommandPause();
        f.Handle.Pause = pause;
        var ui = new ShaderChoiceBoundary(f.Service);
        var choice = Task.Run(() => ui.ChooseAsync(Group));
        try
        {
            await pause.WaitAsync();
            await f.SwapAsync();
            pause.Release();
            await choice.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { pause.Release(); }
        Assert.Null(ui.ActiveShader, "真实 UI 提交分支没有写 ActiveShader");
        Assert.False(ui.Pinned);
        Assert.Equal(0, ui.Pending);
        Assert.Equal(0, ui.Notices.Count, "旧代次不产生成功/失败反馈给新片");
        Assert.Equal(0, f.NativeHandle.Native.Commands.Count(command => command.IsSet && command.File == "B"));
    }

    private static async Task UiFeedbackAsync()
    {
        foreach (var outcome in new[] { "success", "reject", "unknown" })
        {
            await using var f = new ServiceFixture();
            await f.StartAsync();
            var native = f.NativeHandle.Native;
            native.Result = _ => outcome switch
            {
                "reject" when native.SetAttempts == 3 => -12,
                "unknown" when native.SetAttempts >= 2 => -12,
                _ => 0
            };
            var ui = new ShaderChoiceBoundary(f.Service);
            await ui.ChooseAsync(Group);
            Assert.Equal(0, ui.Pending);
            Assert.Equal(1, ui.Notices.Count, "正常本片失败反馈仍保留");
            if (outcome == "success")
            {
                Assert.Equal(Group, ui.ActiveShader);
                Assert.True(ui.Pinned);
                Assert.Equal(InfoBarSeverity.Informational, ui.Notices[0].Severity);
            }
            else
            {
                Assert.Null(ui.ActiveShader);
                Assert.False(ui.Pinned);
                Assert.Equal(InfoBarSeverity.Warning, ui.Notices[0].Severity);
                Assert.Contains(outcome == "unknown" ? "状态未知" : "已保留切换前状态", ui.Notices[0].Message);
                Assert.Equal(outcome != "unknown", f.Service.ShaderStateKnown);
            }
            Assert.True(native.Commands.Any(command => command.Arguments[0] == "show-text"));
        }
    }

    private static async Task ScopedControlAsync()
    {
        await using var f = new HandleFixture();
        var scoped = new PausingHandle(f);
        var file = scoped.FileGeneration;
        Assert.True(await scoped.CommandForFileAsync(file, ["set", "scale", "lanczos"], CancellationToken.None));
        await f.SwapAsync();
        var previous = f.Native.SetAttempts;
        Assert.False(await scoped.CommandForFileAsync(file, ["set", "scale", "old-file-value"], CancellationToken.None),
            "旧期望不得在命令入口重新捕获 B 的新代次");
        Assert.Equal(previous, f.Native.SetAttempts);
        Assert.True(await scoped.CommandForFileAsync(scoped.FileGeneration, ["set", "scale", "lanczos"], CancellationToken.None));
        Assert.Equal(previous + 1, f.Native.SetAttempts);
    }

    private static async Task ScopedDestroyedAsync()
    {
        await using var f = new HandleFixture();
        var scoped = new PausingHandle(f);
        var file = scoped.FileGeneration;
        await f.Handle.DisposeAsync();
        Assert.False(await scoped.CommandForFileAsync(file, ["set", "scale", "lanczos"], CancellationToken.None));
        Assert.Equal(0, f.Native.SetAttempts);
        Assert.Equal(1, f.Native.DestroyCalls);
    }

    private static async Task ScopedCancellationAsync()
    {
        await using var f = new HandleFixture();
        var scoped = new PausingHandle(f);
        var file = scoped.FileGeneration;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await scoped.CommandForFileAsync(file, ["set", "scale", "lanczos"], cancellation.Token); throw new AssertionException("入口取消应传播"); }
        catch (OperationCanceledException) { }
        using var pending = new CancellationTokenSource();
        Task<bool>? queued = null;
        f.Gate.Run(() =>
        {
            queued = scoped.CommandForFileAsync(file, ["set", "scale", "lanczos"], pending.Token);
            Assert.False(queued.IsCompleted);
            pending.Cancel();
            return true;
        });
        try { await queued!; throw new AssertionException("实际执行点取消应传播"); }
        catch (OperationCanceledException) { }
        Assert.Equal(0, f.Native.SetAttempts);
    }

    private static async Task NormalAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        var native = f.NativeHandle.Native;
        Assert.True(await f.Service.SetShaderGroupAsync(Group));
        Assert.Equal(8, native.Commands.Count(command => command.IsSet));
        Assert.Equal("glsl-shaders", native.Commands.Last(command => command.IsSet).Arguments[1]);
        Assert.Contains("ravu", native.Values["glsl-shaders"]);
        Assert.True(await f.Service.SetShaderGroupAsync(null));
        foreach (var (name, neutral) in ShaderGroupCatalog.NeutralOptions)
        {
            var expected = f.Service.LaunchOptions.LastOrDefault(pair => pair.Key == name).Value ?? neutral;
            Assert.Equal(expected, native.Values[name], name);
        }
        Assert.True(f.Service.ShaderStateKnown);
    }

    private static async Task MissingFileAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        var missing = Group with { Shaders = [ShaderLibrary.RavuZoom with { File = "r051-does-not-exist.glsl" }] };
        Assert.False(await f.Service.SetShaderGroupAsync(missing));
        Assert.Equal(0, f.NativeHandle.Native.SetAttempts);
        Assert.True(f.Service.ShaderStateKnown);
    }

    private static async Task MissingReadsAsync()
    {
        foreach (var name in new[] { "option-info/dscale/default-value", "scale", "glsl-shaders" })
        {
            await using var f = new ServiceFixture();
            await f.StartAsync();
            f.NativeHandle.Native.Missing = name;
            Assert.False(await f.Service.SetShaderGroupAsync(Group), name);
            Assert.Equal(0, f.NativeHandle.Native.SetAttempts, name);
        }
    }

    private static async Task RejectedAsync(bool exception, int rejectAt)
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        var native = f.NativeHandle.Native;
        var before = new Dictionary<string, string>(native.Values, StringComparer.Ordinal);
        native.Result = _ => native.SetAttempts == rejectAt
            ? exception ? throw new InvalidOperationException("R051 injected command failure") : -12 : 0;
        Assert.False(await f.Service.SetShaderGroupAsync(Group));
        var commands = native.Commands.Where(command => command.IsSet).ToArray();
        Assert.Equal(rejectAt * 2, commands.Length, "失败项亦纳入逆序回滚");
        for (var i = 0; i < rejectAt; i++)
        {
            Assert.Equal(commands[rejectAt - 1 - i].Arguments[1], commands[rejectAt + i].Arguments[1], "严格逆序恢复");
            Assert.Equal(before[commands[rejectAt + i].Arguments[1]], commands[rejectAt + i].Arguments[2]);
        }
        foreach (var (name, expected) in before) Assert.Equal(expected, native.Values[name], name);
        Assert.True(f.Service.ShaderStateKnown);
    }

    private static async Task FailedRollbackAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        var native = f.NativeHandle.Native;
        native.Result = _ => native.SetAttempts >= 2 ? -12 : 0;
        Assert.False(await f.Service.SetShaderGroupAsync(Group));
        Assert.False(f.Service.ShaderStateKnown);
        native.Result = null;
        Assert.True(await f.Service.SetShaderGroupAsync(null));
        Assert.True(f.Service.ShaderStateKnown);
    }

    private static async Task SuccessAfterSwapAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        Assert.Equal(1L, f.Service.Generation);
        Assert.Equal(0L, f.NativeHandle.NativeGeneration);
        Assert.True(await f.Service.SetShaderGroupAsync(Group));
        await f.SwapAsync();
        Assert.True(await f.Service.SetShaderGroupAsync(Group));
        await f.SwapAsync("C");
        Assert.Equal(3L, f.Service.Generation);
        Assert.Equal(4L, f.NativeHandle.NativeGeneration);
        Assert.True(await f.Service.SetShaderGroupAsync(null));
        Assert.True(f.Service.ShaderStateKnown);
        Assert.True(f.NativeHandle.Native.Commands.Any(command => command.IsSet && command.File == "C"));
    }

    private static async Task HandOverAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        f.Handle.HandOver();
        Assert.False(await f.Service.SetShaderGroupAsync(Group));
        Assert.Equal(0, f.NativeHandle.Native.SetAttempts);
    }

    private static async Task CloseAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        using var pause = new CommandPause();
        f.Handle.Pause = pause;
        var old = Task.Run(() => f.Service.SetShaderGroupAsync(Group));
        try
        {
            await pause.WaitAsync();
            await f.Service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            pause.Release();
            Assert.False(await old.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { pause.Release(); }
        Assert.False(f.Service.IsPlaying);
        Assert.Equal(0, f.NativeHandle.Native.SetAttempts);
        Assert.Equal(1, f.NativeHandle.Native.DestroyCalls);
    }

    private static async Task ReplaceAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        var previous = f.NativeHandle;
        using var pause = new CommandPause();
        f.Handle.Pause = pause;
        var old = Task.Run(() => f.Service.SetShaderGroupAsync(Group));
        try
        {
            await pause.WaitAsync();
            await f.ReplaceHandleAsync();
            pause.Release();
            Assert.False(await old.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { pause.Release(); }
        Assert.Equal(0, previous.Native.SetAttempts);
        Assert.Equal(0, f.NativeHandle.Native.SetAttempts);
        Assert.True(await f.Service.SetShaderGroupAsync(Group), "新句柄的新事务仍正常");
    }

    private static async Task OrdinaryQueuedAsync()
    {
        await using var f = new HandleFixture();
        Task<bool>? queued = null;
        Task<bool>? swapped = null;
        f.Gate.Run(() =>
        {
            // Reentrant getter captures A now. Its Task.Run cannot enter RunFor until
            // this real native gate is released, after real HandOver/Swap below.
            queued = f.Handle.CommandAsync(["set", "scale", "lanczos"], CancellationToken.None);
            Assert.False(queued.IsCompleted);
            f.Handle.HandOver();
            swapped = f.Handle.SwapToAsync(HandleFixture.Request("B"), CancellationToken.None);
            Assert.True(swapped.IsCompletedSuccessfully);
            return true;
        });
        Assert.True(await swapped!);
        Assert.False(await queued!, "原普通命令已捕获 A，在真实执行点必须被拒");
        Assert.Equal(0, f.Native.SetAttempts);
        f.Ready();
        Assert.True(await f.Handle.CommandAsync(["seek", "1", "relative"], CancellationToken.None));
    }

    private static async Task QueuedCancellationAsync()
    {
        await using var f = new HandleFixture();
        using var cancellation = new CancellationTokenSource();
        Task<bool>? queued = null;
        f.Gate.Run(() =>
        {
            queued = f.Handle.CommandAsync(["set", "scale", "lanczos"], cancellation.Token);
            Assert.False(queued.IsCompleted);
            cancellation.Cancel();
            return true;
        });
        try { await queued!; throw new AssertionException("已捕获后的取消应传播"); }
        catch (OperationCanceledException) { }
        Assert.Equal(0, f.Native.SetAttempts);
    }

    private static async Task BeforeAdoptionAsync(bool startLate)
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        using var commandPause = new CommandPause();
        using var swapPause = new CommandPause();
        f.Handle.AfterSwap = swapPause;
        Task<bool>? old = null;
        Task? next = null;
        var serviceGeneration = f.Service.Generation;
        try
        {
            if (!startLate)
            {
                f.Handle.Pause = commandPause;
                old = Task.Run(() => f.Service.SetShaderGroupAsync(Group));
                await commandPause.WaitAsync();
            }
            next = Task.Run(() => f.StartAsync("B"));
            await swapPause.WaitAsync();
            Assert.Equal(serviceGeneration, f.Service.Generation, "真实 service 尚未发布接班 attempt");
            Assert.Equal(2L, f.NativeHandle.NativeGeneration);
            Assert.Equal("B", f.NativeHandle.Native.File);
            if (startLate) old = f.Service.SetShaderGroupAsync(Group);
            commandPause.Release();
            Assert.False(await old!.WaitAsync(TimeSpan.FromSeconds(5)), "native 已交接但 service 尚未接班，不能成功旧事务/新绑 B");
            Assert.Equal(0, f.NativeHandle.Native.Commands.Count(command => command.IsSet && command.File == "B"));
        }
        finally
        {
            commandPause.Release();
            swapPause.Release();
            if (next is not null) await next.WaitAsync(TimeSpan.FromSeconds(5));
        }
        f.NativeHandle.Ready();
        Assert.True(await f.Service.SetShaderGroupAsync(Group), "service 真正接班后新事务仍可应用");
    }

    private static async Task CancellationAsync()
    {
        await using var f = new HandleFixture();
        using var token = new CancellationTokenSource();
        token.Cancel();
        try { await f.Handle.CommandAsync(["set", "scale", "lanczos"], token.Token); throw new AssertionException("入口取消应传播"); }
        catch (OperationCanceledException) { }
        using var pause = new CommandPause();
        using var pendingToken = new CancellationTokenSource();
        var task = Task.Run(() => f.Handle.CommandAsync(new PausedArguments(["set", "scale", "lanczos"], pause), pendingToken.Token));
        try
        {
            await pause.WaitAsync();
            pendingToken.Cancel();
            pause.Release();
            try { await task; throw new AssertionException("取消应传播"); }
            catch (OperationCanceledException) { }
        }
        finally { pause.Release(); }
        Assert.Equal(0, f.Native.SetAttempts);
    }

    private static async Task OrdinaryControlAsync()
    {
        await using var f = new ServiceFixture();
        await f.StartAsync();
        Assert.True(await f.Service.CommandAsync("seek", "-2.5", "relative"));
        Assert.Equal("seek|-2.5|relative", string.Join('|', f.NativeHandle.Native.Commands[^1].Arguments));
        f.NativeHandle.Native.Result = _ => -12;
        Assert.False(await f.Service.CommandAsync("seek", "1", "relative"));
        Assert.True(await f.NativeHandle.Handle.CommandAsync([], new CancellationToken(true)), "原空命令契约不变");
    }

    private static Task ExtractionAsync()
    {
        Assert.NotNull(typeof(LibMpvHandle).GetMethod("Swap", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(typeof(LibMpvLifetimeGate).GetMethod("RunFor", BindingFlags.Instance | BindingFlags.NonPublic));
        ProductionProof.Verify();
        Assert.False(typeof(LibMpvNative).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Any(method => method.GetCustomAttribute<System.Runtime.InteropServices.DllImportAttribute>() is not null));
        return Task.CompletedTask;
    }
}

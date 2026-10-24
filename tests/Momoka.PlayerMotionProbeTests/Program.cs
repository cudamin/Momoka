using System.Security.Cryptography;
using System.Text;
using Momoka.Shell;
using Momoka.Shell.Interop;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;

namespace Momoka.PlayerMotionProbeTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var baseline = args is ["pre-fix-control", _];
            var root = args switch
            {
                [var currentRoot] => currentRoot,
                ["pre-fix-control", var currentRoot] => currentRoot,
                _ => throw new InvalidOperationException("传本树绝对根；旧源对照额外传 pre-fix-control")
            };
            VerifySource(root, baseline);
            if (baseline)
            {
                await PreFixControlAsync();
                Console.WriteLine("旧正式阶段红对照：原‘直接全屏进场没有第二段缩放’断言按预期失败；不是 actual motion 通过。");
                return 0;
            }

            (string Name, Func<Task> Body)[] cases =
            [
                ("实际完成可同步通过，不再硬等 500/400ms", CompletedAsync),
                ("超过旧 500ms 后到达的同一完成序列可正确通过", LateCompletionAsync),
                ("姿势已归位仍须等真实窗口事务", WindowTransactionAsync),
                ("窗口先完成，仍等待 pose 回调和合成交接", CompletionOrderAsync),
                ("真实入口没开始不能借旧 Identity 通过", NotStartedAsync),
                ("完成后残留缩放仍被原进场断言拒绝", ResidualEntranceAsync),
                ("完成后没全屏仍被原进场断言拒绝", WrongFullscreenAsync),
                ("退场等实际离树与 pose 完成，不凭 400ms", ExitCompletionAsync),
                ("退场残留变换仍被原清理断言拒绝", ResidualExitAsync),
                ("原浏览矩形恢复断言仍执行", WrongBrowseBoundsAsync),
                ("真实窗口事务取消不记通过", WindowCancellationAsync),
                ("等待 pose/合成交接取消不记通过", MotionCancellationAsync),
                ("真实窗口事务异常不吞掉", WindowFailureAsync),
                ("真实窗口事务不完成会超时失败", WindowTimeoutAsync),
                ("pose/合成交接不完成会超时失败", MotionTimeoutAsync)
            ];
            var failed = 0;
            foreach (var (name, body) in cases)
            {
                try { await body(); Console.WriteLine($"通过：{name}"); }
                catch (Exception error) { failed++; Console.WriteLine($"失败：{name}\n{error}"); }
            }
            Console.WriteLine($"共 {cases.Length} 项，通过 {cases.Length - failed}，失败 {failed}，跳过 0；无 UI/native/播放/服务器覆盖。");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.WriteLine($"夹具失败：{error}");
            return 1;
        }
    }

    private static void VerifySource(string root, bool baseline)
    {
        Check(Path.IsPathFullyQualified(root), "仓库根必须是绝对路径");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Check(directory is not null && string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
            "测试程序集必须来自传入的同一工作树");
        if (!baseline)
            Check(string.Equals(Path.GetFullPath(PlayerMotionProbe.SourcePath),
                Path.Combine(Path.GetFullPath(root), "src", "Momoka.Shell", "PlayerMotionProbe.cs"),
                StringComparison.OrdinalIgnoreCase), "current 回归必须编译本树正式探针，不能传不同诊断源冒充");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PlayerMotionProbe.SourcePath)));
        Check(string.Equals(hash, PlayerMotionProbe.SourceHash, StringComparison.OrdinalIgnoreCase),
            "测试之后源文件已变，必须先编译当前源");
        Console.WriteLine($"原样 source-linked 源：{PlayerMotionProbe.SourcePath}\nSHA256={hash}");
    }

    private static Task CompletedAsync()
    {
        using var fixture = new Fixture();
        var run = fixture.Run();
        try
        {
            Check(run.IsCompletedSuccessfully, "完成信号已全部同步送达，探针不应仍在固定 sleep");
            fixture.Passed();
        }
        finally { fixture.Cancel(); }
        return run;
    }

    private static async Task LateCompletionAsync()
    {
        using var fixture = new Fixture();
        fixture.PrepareLateCompletion();
        await fixture.Run().WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.LateDelivery;
        fixture.Passed();
    }

    private static async Task WindowTransactionAsync()
    {
        using var fixture = new Fixture();
        var window = Gate();
        fixture.Page.OnBegin = () =>
        {
            fixture.Page.Active = fixture.Page.PlayerVisible = true;
            fixture.Window.Fullscreen = true;
            fixture.Page.WindowChange = window.Task;
        };
        var run = fixture.Run();
        CheckPending(run, fixture, "旧 Identity/全屏状态不是事务完成");
        window.SetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Passed();
    }

    private static async Task CompletionOrderAsync()
    {
        using var fixture = new Fixture();
        var window = Gate();
        fixture.Page.OnBegin = () =>
        {
            fixture.Page.Active = fixture.Page.PlayerVisible = true;
            fixture.Page.Animating = fixture.Page.StartupHandoverPending = true;
            fixture.Page.Identity = false;
            fixture.Page.WindowChange = window.Task;
        };
        var run = fixture.Run();
        CheckPending(run, fixture, "窗口尚未完成");
        fixture.Window.Fullscreen = true;
        window.SetResult();
        await Task.Yield();
        CheckPending(run, fixture, "窗口已全屏但完成回调尚未送达");
        fixture.Page.Animating = false;
        fixture.Page.Identity = true;
        await Task.Yield();
        CheckPending(run, fixture, "pose 已完成但合成交接还在途");
        fixture.Page.StartupHandoverPending = false;
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        fixture.Passed();
    }

    private static async Task NotStartedAsync()
    {
        using var fixture = new Fixture();
        fixture.Window.Fullscreen = true;
        fixture.Page.OnBegin = () => { };
        await FailureAsync<InvalidOperationException>(fixture.Run(), "真实入口已经开始");
        Check(fixture.Page.Ends == 0, "入口未开始时不能继续退场");
    }

    private static async Task ResidualEntranceAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.Identity = false;
        await FailureAsync<InvalidOperationException>(fixture.Run(), "直接全屏进场没有第二段缩放");
        Check(fixture.Page.Ends == 0, "不能靠退场把坏进场姿势归零后洗绿");
        fixture.Page.Identity = true;
        Check(!fixture.Text.Contains("通过：直接全屏进场没有第二段缩放", StringComparison.Ordinal),
            "迟到的归位不能改写本次失败");
    }

    private static async Task WrongFullscreenAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.OnBegin = () => fixture.Page.Active = fixture.Page.PlayerVisible = true;
        await FailureAsync<InvalidOperationException>(fixture.Run(), "直接全屏进场没有第二段缩放");
    }

    private static async Task ExitCompletionAsync()
    {
        foreach (var waitForPose in new[] { true, false })
        {
            using var fixture = new Fixture();
            fixture.Page.OnEnd = () =>
            {
                fixture.Page.Active = false;
                fixture.Page.Animating = waitForPose;
                fixture.Page.PlayerVisible = !waitForPose;
                fixture.Window.Fullscreen = false;
            };
            var run = fixture.Run();
            Check(fixture.Page.Ends == 1 && !run.IsCompleted,
                waitForPose ? "页面已收起但 pose 尚未完成" : "pose 已完成但页面尚未收起");
            fixture.Page.CompleteExit();
            await run.WaitAsync(TimeSpan.FromSeconds(2));
            fixture.Passed();
        }
    }

    private static async Task ResidualExitAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.OnEnd = () => { fixture.Page.CompleteExit(); fixture.Page.Identity = false; };
        await FailureAsync<InvalidOperationException>(fixture.Run(), "无视频全屏退场清理完整");
    }

    private static async Task WrongBrowseBoundsAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.OnEnd = () =>
        {
            fixture.Page.CompleteExit();
            var bounds = Native.Bounds;
            bounds.Left++;
            bounds.Right++;
            Native.Bounds = bounds;
        };
        await FailureAsync<InvalidOperationException>(fixture.Run(), "重复进出仍恢复浏览尺寸");
        Check(Native.Reads == 1, "原 GetWindowRect 与位置/尺寸检查必须执行");
    }

    private static async Task WindowCancellationAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.WindowChange = Gate().Task;
        var run = fixture.Run();
        CheckPending(run, fixture, "实际窗口事务在途");
        fixture.Cancel();
        await FailureAsync<OperationCanceledException>(run, "");
    }

    private static async Task MotionCancellationAsync()
    {
        foreach (var waitForPose in new[] { true, false })
        {
            using var fixture = new Fixture();
            fixture.Page.Animating = waitForPose;
            fixture.Page.StartupHandoverPending = !waitForPose;
            var run = fixture.Run();
            CheckPending(run, fixture, waitForPose ? "pose 在途" : "合成交接在途");
            fixture.Cancel();
            await FailureAsync<OperationCanceledException>(run, "");
        }
    }

    private static async Task WindowFailureAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.WindowChange = Task.FromException(new IOException("窗口事务失败夹具"));
        await FailureAsync<IOException>(fixture.Run(), "窗口事务失败夹具");
        Check(fixture.Page.Ends == 0, "不能吞掉失败事务再继续通过");
    }

    private static async Task WindowTimeoutAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.WindowChange = Gate().Task;
        await FailureAsync<TimeoutException>(fixture.Run(), "");
        Check(fixture.Page.Ends == 0, "窗口事务超时不能继续记通过");
    }

    private static async Task MotionTimeoutAsync()
    {
        using var fixture = new Fixture();
        fixture.Page.Animating = fixture.Page.StartupHandoverPending = true;
        await FailureAsync<TimeoutException>(fixture.Run(), "直接全屏窗口交接与进场动效已完成");
        Check(fixture.Page.Ends == 0, "动效超时不能跳过原断言");
    }

    private static async Task PreFixControlAsync()
    {
        using var fixture = new Fixture();
        fixture.PrepareLateCompletion();
        await FailureAsync<InvalidOperationException>(fixture.Run(), "直接全屏进场没有第二段缩放");
        Check(fixture.Page.Ends == 0, "红对照必须停在原进场断言");
        await fixture.LateDelivery;
        Check(!fixture.Text.Contains("通过：直接全屏进场没有第二段缩放", StringComparison.Ordinal),
            "同一真实完成序列迟到不能改写旧阶段的假失败");
        Console.Write(fixture.Text);
    }

    private static void CheckPending(Task run, Fixture fixture, string message)
    {
        Check(!run.IsCompleted && fixture.Page.Ends == 0 && Native.Reads == 0, message);
    }

    private static async Task FailureAsync<T>(Task run, string message) where T : Exception
    {
        try { await run.WaitAsync(TimeSpan.FromSeconds(8)); }
        catch (T error)
        {
            Check(run.IsCompleted, "夹具外层保险丝到点不能冒充探针自身的有界失败");
            Check(error.Message.Contains(message, StringComparison.Ordinal), $"错误消息不匹配：{error.Message}");
            return;
        }
        throw new InvalidOperationException($"期望 {typeof(T).Name}，探针却通过了");
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly CancellationTokenSource _stopping = new(TimeSpan.FromSeconds(15));
        private readonly StringWriter _report = new();
        private readonly NativeRect _browse = new() { Left = 120, Top = 120, Right = 1360, Bottom = 1000 };
        internal HostWindow Window { get; } = new();
        internal PlayerPage Page { get; }
        internal ProbeTimer Timer { get; } = new();
        internal string Text => _report.ToString();
        internal Task LateDelivery { get; private set; } = Task.CompletedTask;

        internal Fixture()
        {
            Page = new PlayerPage(Window);
            Native.Bounds = _browse;
            Native.Reads = 0;
        }

        internal void PrepareLateCompletion()
        {
            Page.OnBegin = () =>
            {
                Page.Active = Page.PlayerVisible = true;
                Page.Animating = Page.StartupHandoverPending = true;
                Page.Identity = false;
                var window = Gate();
                Page.WindowChange = window.Task;
                LateDelivery = DeliverAsync(window);
            };
        }

        private async Task DeliverAsync(TaskCompletionSource window)
        {
            // Test endpoint timing only: the same sequence straddles the old 500ms assertion in both builds.
            await Task.Delay(650, _stopping.Token);
            Window.Fullscreen = true;
            window.SetResult();
            await Task.Delay(40, _stopping.Token);
            Page.Animating = false;
            Page.Identity = true;
            await Task.Delay(40, _stopping.Token);
            Page.StartupHandoverPending = false;
        }

        internal Task Run() => PlayerMotionProbe.RunFullscreenStageAsync(Page, Window, Timer, _browse, _report, _stopping.Token);
        internal void Cancel() => _stopping.Cancel();
        internal void Passed()
        {
            Check(Page.Begins == 1 && Page.Ends == 1 && Page.FullscreenRequested, "必须实际进退一轮全屏");
            Check(Timer.Started && Timer.Stopped && Native.Reads == 1, "取样和原几何断言必须完整执行");
            Check(Text.Contains("通过：直接全屏进场没有第二段缩放", StringComparison.Ordinal)
                && Text.Contains("通过：无视频全屏退场清理完整", StringComparison.Ordinal)
                && !Text.Contains("失败：", StringComparison.Ordinal), "原两个断言均须通过");
        }

        public void Dispose()
        {
            Cancel();
            _report.Dispose();
            _stopping.Dispose();
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>编译链接的真实 Shell 监听器；子进程隔离修前后台线程逃逸，不运行 UI 或真实激活信号。</summary>
internal static class ActivationListenerTests
{
    public static void Register()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip("启动监听器：关闭与线程初始化交接", "需要 Windows 本地命名事件");
            return;
        }
        foreach (var (scenario, label) in new[]
        {
            ("stop-before-start", "工作线程尚未初始化就关闭，随后初始化安全退出"),
            ("waiting-stop", "已经等待时关闭且丢弃排队的旧激活"),
            ("normal", "正常信号仍交到 dispatcher，关闭后有序退出"),
            ("concurrent-stop", "并发重复关闭只请求停止一次"),
            ("start-failure", "线程启动失败也释放自己的句柄"),
            ("dispatcher-reject", "dispatcher拒绝排队后停止并释放句柄")
        })
            Test("启动监听器：" + label, () => RunFixture(scenario));
    }

    internal static int RunChild(string scenario)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (scenario == "start-failure")
            {
                var eventName = EventName();
                Assert.Throws<IOException>(() => ActivationListener.Start(eventName, _ => true, () => { },
                    _ => throw new IOException("synthetic thread start failure")));
                var hasEvent = EventWaitHandle.TryOpenExisting(eventName, out var leaked);
                leaked?.Dispose();
                Assert.False(hasEvent, "启动失败不能留下命名事件句柄");
                return 0;
            }

            var name = EventName();
            var actions = new ConcurrentQueue<Action>();
            using var posted = new ManualResetEventSlim();
            Thread? worker = null;
            var activated = 0;
            using var listener = ActivationListener.Start(name,
                action =>
                {
                    actions.Enqueue(action);
                    posted.Set();
                    return scenario != "dispatcher-reject";
                }, () => activated++, thread =>
                {
                    worker = thread;
                    if (scenario is not ("stop-before-start" or "concurrent-stop")) thread.Start();
                });
            if (scenario == "stop-before-start")
            {
                listener.Dispose();
                worker!.Start();
                Assert.True(worker.Join(5000), "先关闭再初始化必须完成退出");
                Assert.Equal(0, activated);
                Assert.True(actions.IsEmpty);
                listener.Dispose();
            }
            else if (scenario == "concurrent-stop")
            {
                Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(listener.Dispose))).GetAwaiter().GetResult();
                worker!.Start();
                Assert.True(worker.Join(5000));
                Assert.Equal(0, activated);
                listener.Dispose();
            }
            else
            {
                using var signal = EventWaitHandle.OpenExisting(name);
                signal.Set();
                Assert.True(posted.Wait(5000), "本地假激活信号必须被监听器消费");
                if (scenario == "normal")
                {
                    Assert.True(actions.TryDequeue(out var action));
                    action!();
                    Assert.Equal(1, activated);
                    listener.Dispose();
                }
                else if (scenario == "waiting-stop")
                {
                    listener.Dispose();
                    while (actions.TryDequeue(out var action)) action();
                    Assert.Equal(0, activated, "关闭后已排队的激活也失效");
                }
                else if (scenario != "dispatcher-reject") throw new InvalidOperationException("Unknown fixture");
                Assert.True(worker!.Join(5000), "停止后真实后台线程必须结束");
                listener.Dispose();
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string EventName() => @"Local\Momoka.Test.Activation." + Guid.NewGuid().ToString("N");

    private static void RunFixture(string scenario)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("缺少测试宿主路径");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(ActivationListenerTests).Assembly.Location);
        info.ArgumentList.Add("activation-listener-fixture");
        info.ArgumentList.Add(scenario);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("监听器夹具未启动");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(); // Only this just-created disposable test child, never the app or a user process.
            throw new AssertionException("监听器夹具超时：" + scenario);
        }
        var text = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
        Assert.Equal(0, process.ExitCode, text);
        Assert.DoesNotContain("Unhandled exception", text);
    }
}

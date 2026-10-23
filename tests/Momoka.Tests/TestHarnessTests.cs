using System.Diagnostics;
using System.Text;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>在本树测试进程的独立子进程中执行真运行器，不往外层套件混入预期失败或跳过。</summary>
internal static class TestHarnessTests
{
    public static void Register()
    {
        Test("测试运行器：运行中跳过不能同时计为通过，后续用例仍执行", () =>
        {
            var result = RunFixture("runtime-skip");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("共 2 项，通过 1，失败 0，跳过 1", result.Output);
            Assert.DoesNotContain("通过  runtime skipped", result.Output);
            Assert.Contains("跳过  runtime skipped", result.Output);
            Assert.Contains("通过  normal passes", result.Output);
        });

        Test("测试运行器：登记前跳过独立计数，总数等于三种结果之和", () =>
        {
            var result = RunFixture("registration-skip");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("共 2 项，通过 1，失败 0，跳过 1", result.Output);
            Assert.Contains("跳过  registered skipped", result.Output);
        });

        Test("测试运行器：正常用例全部通过且返回成功码", () =>
        {
            var result = RunFixture("normal");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("共 2 项，通过 2，失败 0，跳过 0", result.Output);
        });

        Test("测试运行器：抛异常只计失败并返回非零", () =>
        {
            var result = RunFixture("failure");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("共 2 项，通过 1，失败 1，跳过 0", result.Output);
            Assert.Contains("失败  normal failure", result.Output);
            Assert.Contains("fixture-error", result.Output);
        });

        Test("测试运行器：跳过后又抛异常时失败优先，不掩盖失败", () =>
        {
            var result = RunFixture("skip-then-failure");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("共 1 项，通过 0，失败 1，跳过 0", result.Output);
            Assert.DoesNotContain("跳过  skipped then failed", result.Output);
            Assert.Contains("失败  skipped then failed", result.Output);
        });

        Test("测试运行器：同一用例多次Skip只计一次，保留不同原因", () =>
        {
            var result = RunFixture("repeated-skip");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("共 1 项，通过 0，失败 0，跳过 1", result.Output);
            Assert.Contains("missing first fixture", result.Output);
            Assert.Contains("missing second fixture", result.Output);
            Assert.DoesNotContain("通过  repeated skipped", result.Output);
        });

        Test("测试运行器：登记前和运行中跳过不影响后续正常与失败用例", () =>
        {
            var result = RunFixture("mixed");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("共 4 项，通过 1，失败 1，跳过 2", result.Output);
            Assert.DoesNotContain("通过  runtime skipped", result.Output);
            Assert.Contains("通过  normal passes", result.Output);
            Assert.Contains("失败  normal failure", result.Output);
        });
    }

    internal static int RunChild(string scenario)
    {
        Console.OutputEncoding = Encoding.UTF8;
        switch (scenario)
        {
            case "runtime-skip":
                RegisterRuntimeSkip();
                RegisterPass();
                break;
            case "registration-skip":
                Skip("registered skipped", "missing registered fixture");
                RegisterPass();
                break;
            case "normal":
                RegisterPass();
                Test("second passes", () => { });
                break;
            case "failure":
                RegisterFailure();
                RegisterPass();
                break;
            case "skip-then-failure":
                Test("skipped then failed", () =>
                {
                    Skip("skipped then failed", "missing fixture");
                    throw new InvalidOperationException("fixture-error");
                });
                break;
            case "repeated-skip":
                Test("repeated skipped", () =>
                {
                    Skip("repeated skipped", "missing first fixture");
                    Skip("repeated skipped", "missing first fixture");
                    Skip("repeated skipped", "missing second fixture");
                });
                break;
            case "mixed":
                Skip("registered skipped", "missing registered fixture");
                RegisterRuntimeSkip();
                RegisterPass();
                RegisterFailure();
                break;
            default:
                return 2;
        }
        return TestHarness.Run();
    }

    private static void RegisterRuntimeSkip() => Test("runtime skipped", () =>
        Skip("fixture substep", "missing local fixture"));

    private static void RegisterPass() => Test("normal passes", () => { });

    private static void RegisterFailure() => Test("normal failure", () =>
        throw new InvalidOperationException("fixture-error"));

    private static (int ExitCode, string Output) RunFixture(string scenario)
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
            info.ArgumentList.Add(typeof(TestHarnessTests).Assembly.Location);
        info.ArgumentList.Add("test-harness-fixture");
        info.ArgumentList.Add(scenario);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("未启动测试运行器夹具");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            // 仅关闭本用例刚创建的隔离测试子进程，不涉及应用或用户进程。
            process.Kill();
            throw new AssertionException("测试运行器夹具超时");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
    }
}

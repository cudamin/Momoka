using System.Diagnostics;
using System.Text;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>执行实际入口；旧代码误进默认套件时只停止本用例刚启动的子进程。</summary>
internal static class TestEntryPointTests
{
    internal static void Register()
    {
        string[][] invalid =
        [
            ["unknown-fixture"],
            [""],
            ["verification-harness-fixture", "extra"],
            ["startup-state-contract-fixture"],
            ["startup-state-contract-fixture", "missing.dll"],
            ["startup-state-contract-fixture", "missing.dll", "missing-root", "wrong-tail"],
            ["verification-harness-fixture", "desired-invariants"],
            ["startup-state-contract-fixture", "missing.dll", "missing-root", "desired-invariants", "extra"]
        ];
        foreach (var arguments in invalid)
        {
            Test("测试入口：错误参数拒绝且不执行默认套件 " + string.Join('|', arguments), async () =>
            {
                var result = await RunChildAsync(arguments);
                Assert.False(result.DefaultStarted, "非空错误参数启动了默认套件");
                Assert.Equal(2, result.ExitCode);
                Assert.DoesNotContain("Momoka Core 测试", result.Output);
                Assert.DoesNotContain("共 ", result.Output);
            });
        }
        foreach (var name in new[] { "startup-state-contract-fixture", "startup-reapply-notice-fixture", "settings-screenshot-restore-fixture" })
        {
            Test("测试入口：兼容尾参仍进入原契约并拒绝缺失程序集 " + name, async () =>
            {
                var result = await RunChildAsync([name, "missing.dll", "missing-root", "desired-invariants"]);
                Assert.False(result.DefaultStarted, "文档兼容尾参退回了默认套件");
                Assert.Equal(1, result.ExitCode, "入口必须执行原夹具的输入拒绝，不将缺失程序集当通过");
                Assert.DoesNotContain("Momoka Core 测试", result.Output);
                Assert.DoesNotContain("共 ", result.Output);
                Assert.True(result.Output.Length > 0);
            });
        }
    }

    private static async Task<(int ExitCode, string Output, bool DefaultStarted)> RunChildAsync(string[] arguments)
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
            info.ArgumentList.Add(typeof(TestEntryPointTests).Assembly.Location);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("未启动测试入口子进程");
        var defaultStarted = false;
        async Task<string> ReadOutputAsync()
        {
            var output = new StringBuilder();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                output.AppendLine(line);
                if (line == "Momoka Core 测试")
                {
                    defaultStarted = true;
                    if (!process.HasExited) process.Kill();
                }
            }
            return output.ToString();
        }
        var outputTask = ReadOutputAsync();
        var errorsTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            throw new AssertionException("本用例测试入口子进程超时");
        }
        return (process.ExitCode, await outputTask + await errorsTask, defaultStarted);
    }
}

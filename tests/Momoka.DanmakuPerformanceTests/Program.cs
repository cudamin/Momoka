using System.Text;
using System.Text.Json;

namespace Momoka.Tests;

internal static class Program
{
    internal static string OutputRoot { get; private set; } = "";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args is ["--help"])
        {
            Console.WriteLine("用法：--output <全新证据目录> [--measure-only]；默认运行弹幕协议与性能/归属专项回归。");
            return 0;
        }
        string? output = null;
        var measureOnly = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--output" && output is null && index + 1 < args.Length
                && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) output = args[++index];
            else if (args[index] == "--measure-only" && !measureOnly) measureOnly = true;
            else
            {
                Console.Error.WriteLine("参数无效或重复。用法：--output <全新证据目录> [--measure-only]。");
                return 2;
            }
        }
        if (output is null)
        {
            Console.Error.WriteLine("请指定 --output <本树新的证据目录>；--measure-only 只测量，不代表回归通过。");
            return 2;
        }
        OutputRoot = Path.GetFullPath(output);
        if (Directory.Exists(OutputRoot))
        {
            Console.Error.WriteLine("证据目录已存在，请使用新的目录，保留旧结果。");
            return 2;
        }
        Directory.CreateDirectory(OutputRoot);
        var measurements = await DanmakuPerformanceTests.MeasureAsync().ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(OutputRoot, "cache-measurements.json"),
            JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        if (measureOnly) return 0;
        DanmakuTests.Register();
        DanmakuPerformanceTests.Register();
        DanmakuLoadOwnershipTests.Register();
        return TestHarness.Run();
    }
}

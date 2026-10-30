using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Momoka.Tests;

internal static class Program
{
    internal static readonly List<(string Name, Func<Task> Run)> Cases = [];
    internal static readonly List<object> Measurements = [];
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(8);

    internal static void Test(string name, Func<Task> run) => Cases.Add((name, run));
    internal static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task Eventually(Func<bool> condition, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > Deadline) throw new AssertionException(message);
            await Task.Delay(2).ConfigureAwait(false);
        }
    }

    internal static async Task<Exception?> Failure(Task task)
    {
        try { await task.WaitAsync(Deadline).ConfigureAwait(false); return null; }
        catch (TimeoutException) { throw new AssertionException("测试任务未在期限内结束"); }
        catch (Exception error) { return error; }
    }

    internal static async Task Cancelled(Task task) =>
        Assert.True(await Failure(task).ConfigureAwait(false) is OperationCanceledException, "必须是取消，不是空答案或成功");

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "perf-images")
        {
            Console.Error.WriteLine("严格入口：仅接受 perf-images；不运行默认/未知夹具。");
            return 2;
        }
        foreach (var (path, hash) in ProductionPins.Files)
        {
            var actual = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(ProductionPins.Root, path))));
            if (!string.Equals(hash, actual, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"SOURCE_HASH_MISMATCH：{path}，请重新编译当前源码");
                return 3;
            }
        }
        Console.WriteLine($"生产源码根：{ProductionPins.Root}");
        Console.WriteLine("无登录/凭据/真实库/设置/播放/窗口/UIA；仅内存 HTTP 与实际 WinRT 成像。");
        ImageCacheTests.Register();
        CoverArtworkTests.Register();
        var legacyResult = TestHarness.Run();
        SharedBudgetTests.Register();
        StoreBudgetTests.Register();
        CoverDecodeTests.Register();
        CoverCallerTests.Register();
        var log = new Momoka.Diagnostics.RingBufferLogSink(100);
        log.Written += entry => { if (entry.Category == "perf-image-fixture") Console.WriteLine($"CALLER_LOG: {entry.Message}"); };
        Momoka.Diagnostics.Log.UseSink(log);
        var failures = new List<string>();
        var watch = Stopwatch.StartNew();
        foreach (var (name, run) in Cases)
        {
            try
            {
                await run().WaitAsync(TimeSpan.FromSeconds(45)).ConfigureAwait(false);
                Console.WriteLine($"  通过  {name}");
            }
            catch (Exception error)
            {
                failures.Add(name);
                Console.WriteLine($"  失败  {name}：{error}");
            }
        }
        var output = Path.Combine(ProductionPins.Root, "work", "perf-images", "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            actualProductionRoot = ProductionPins.Root,
            productionHashes = ProductionPins.Files.Select(pin => new { path = pin.Path, hash = pin.Hash }),
            legacyExitCode = legacyResult,
            cases = Cases.Count,
            passed = Cases.Count - failures.Count,
            failures,
            skipped = 0,
            elapsedMs = watch.ElapsedMilliseconds,
            measurements = Measurements,
            boundary = "Real Core/WinRT; verbatim cover caller with inert XAML/decode boundaries; not actual WinUI or playback"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"新增专项共 {Cases.Count} 项，通过 {Cases.Count - failures.Count}，失败 {failures.Count}，跳过 0，用时 {watch.ElapsedMilliseconds} ms");
        Console.WriteLine($"结果：{Path.Combine(output, "results.json")}");
        return legacyResult == 0 && failures.Count == 0 ? 0 : 1;
    }
}

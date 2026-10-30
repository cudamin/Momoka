using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FileLogBudgetTests
{
    internal static void Register()
    {
        RegisterBoundaries();
        RegisterRetentionAndFailures();
        RegisterReparseBoundaries();
        Test("PERF-13：默认日志连续9MiB写入不能产生大于8MiB的单文件", () =>
        {
            using var directory = new BudgetDirectory();
            using var sink = new FileLogSink(directory.Path);
            var payload = new string('A', 3 * 1024 * 1024);
            for (var index = 0; index < 3; index++)
                sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Error, "合成预算", payload, null));
            var files = Directory.GetFiles(directory.Path, "app-*.log");
            Assert.True(files.All(path => new FileInfo(path).Length <= 8 * 1024 * 1024),
                string.Join("；", files.Select(path => $"{System.IO.Path.GetFileName(path)}={new FileInfo(path).Length}")));
            Console.WriteLine($"    默认预算实测 files={files.Length} bytes={files.Sum(path => new FileInfo(path).Length)}");
        });
    }
}

internal sealed class BudgetDirectory : IDisposable
{
    private static readonly string Root = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "resource-budget-fixtures"));
    public string Path { get; } = System.IO.Path.Combine(Root, Guid.NewGuid().ToString("N"));

    internal BudgetDirectory() => Directory.CreateDirectory(Path);

    internal static bool Owns(string path) => System.IO.Path.GetFullPath(path)
        .StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        var resolved = System.IO.Path.GetFullPath(Path);
        if (!resolved.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试目录越过独立输出根目录");
        Directory.Delete(resolved, recursive: true);
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Text;
using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FileLogBudgetTests
{
    private static void RegisterRetentionAndFailures()
    {
        Test("PERF-13重启总预算：已有新鲜日志超过总量先删最旧卷，保留新尾部且新写入仍受预算", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var oldParts = Enumerable.Range(0, 10).Select(part => Seed(directory, clock, part, 128, ageDays: 0)).ToArray();
            using var sink = NewSink(directory, clock, file: 128, total: 256);
            Assert.Equal(256L, DiskBytes(directory));
            Assert.True(oldParts.Take(8).All(path => !File.Exists(path)));
            Assert.True(oldParts.Skip(8).All(File.Exists));
            sink.Write(Entry(clock, "重启最新尾部"));
            AssertBudget(directory, sink, 128, 256);
            Assert.Contains("重启最新尾部", string.Join("", Files(directory).Select(ReadOpen)));
        });

        Test("PERF-13失败索引：不同日期创建失败不累计空DayFiles，重试成本仍有限", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock);
            for (var index = 0; index < 20; index++)
            {
                var conflict = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 0));
                Directory.CreateDirectory(conflict);
                sink.Write(Entry(clock, "目录冲突"));
                Directory.Delete(conflict);
                clock.Advance(TimeSpan.FromDays(1), fire: false);
            }
            var days = (System.Collections.IDictionary)typeof(FileLogSink)
                .GetField("_days", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sink)!;
            Assert.Equal(0, days.Count);
            Assert.Equal(0, Files(directory).Length);
        });

        Test("PERF-13运行期：空闲超过14天也由定时器释放并删除旧日志，不依赖重启或新写入", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock);
            sink.Write(Entry(clock, "很久前的尾部"));
            var prior = Files(directory).Single();
            clock.Advance(TimeSpan.FromDays(14));
            Assert.True(File.Exists(prior), "恰好保留期不提前删");
            clock.Advance(TimeSpan.FromMinutes(15));
            Assert.False(File.Exists(prior));
            Assert.Equal(0L, IndexedBytes(sink));
            sink.Write(Entry(clock, "长运行后新尾部"));
            Assert.Contains("长运行后新尾部", ReadOpen(Files(directory).Single()));
            AssertBudget(directory, sink, 512, 4096);
            Assert.Equal(TimeSpan.FromMinutes(15), clock.LastPeriod);
        });

        Test("PERF-13运行期：跨40天持续写入时旧日志被回收，保留范围与字节索引一起更新", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock, file: 512, total: 64 * 1024);
            for (var day = 0; day < 40; day++)
            {
                if (day > 0) clock.Advance(TimeSpan.FromDays(1));
                sink.Write(Entry(clock, $"day-{day}"));
                Assert.True(Files(directory).Length <= 15);
                AssertBudget(directory, sink, 512, 64 * 1024);
            }
            Assert.DoesNotContain("day-0" + Environment.NewLine, string.Join("", Files(directory).Select(ReadOpen)));
            Assert.Contains("day-39", string.Join("", Files(directory).Select(ReadOpen)));
        });

        Test("PERF-13运行期：130个过期文件分64/64/2有界维护，非日志文件不被连坐", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            for (var part = 0; part < 130; part++) Seed(directory, clock, part, 1, ageDays: 30);
            var unrelated = System.IO.Path.Combine(directory.Path, "app-important.log");
            File.WriteAllText(unrelated, "不能删除");
            using var sink = NewSink(directory, clock, total: 512);
            Assert.Equal(66, Files(directory).Count(path => path != unrelated));
            clock.Advance(TimeSpan.FromMinutes(15));
            Assert.Equal(2, Files(directory).Count(path => path != unrelated));
            clock.Advance(TimeSpan.FromMinutes(15));
            Assert.Equal(0, Files(directory).Count(path => path != unrelated));
            Assert.Equal("不能删除", File.ReadAllText(unrelated));
        });

        Test("PERF-13运行期：正常写入/维护不扫描后来外部创建的文件，重启才重新建目录索引", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var external = System.IO.Path.Combine(directory.Path, "app-20000101.log");
            using (var sink = NewSink(directory, clock))
            {
                File.WriteAllText(external, "external-owner");
                File.SetLastWriteTimeUtc(external, clock.Now.AddDays(-30).UtcDateTime);
                sink.Write(Entry(clock, "本owner"));
                clock.Advance(TimeSpan.FromMinutes(15));
                Assert.True(File.Exists(external), "无运行期全目录扫描；不接管新外部owner");
            }
            using var restart = NewSink(directory, clock);
            Assert.False(File.Exists(external), "下次启动匿名目录索引收进已存在的日志");
        });

        Test("PERF-13失败：旧日志拒绝删除时洪峰不能越总预算，解锁后自动恢复新尾部", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var stale = Seed(directory, clock, 0, 128, ageDays: 30);
            using var locked = new FileStream(stale, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sink = NewSink(directory, clock, file: 128, total: 128);
            for (var index = 0; index < 100; index++) sink.Write(Entry(clock, "不能增长"));
            Assert.True(File.Exists(stale));
            Assert.Equal(128L, DiskBytes(directory));
            Assert.Equal(2, Files(directory).Length, "失败时复用空卷，不按日志条数造文件");
            clock.Advance(TimeSpan.FromMinutes(15));
            Assert.Equal(128L, DiskBytes(directory));
            locked.Dispose();
            clock.Advance(TimeSpan.FromMinutes(15));
            Assert.False(File.Exists(stale));
            sink.Write(Entry(clock, "解锁后尾部"));
            Assert.Contains("解锁后尾部", string.Join("", Files(directory).Select(ReadOpen)));
            AssertBudget(directory, sink, 128, 128);
        });

        Test("PERF-13失败：目录冲突时吞IO错误并限每分钟重试，不改冲突目标，恢复后能继续写", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock);
            var collision = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 0));
            Directory.CreateDirectory(collision);
            for (var index = 0; index < 100; index++) sink.Write(Entry(clock, "失败重试"));
            Assert.Equal(0, Files(directory).Length, "不能通过不断新建分卷绕开重试成本");
            Assert.True(Directory.Exists(collision));
            Directory.Delete(collision); // Exact empty directory created above, never a recursive user-data delete.
            sink.Write(Entry(clock, "冷却中"));
            Assert.Equal(0, Files(directory).Length);
            clock.Advance(TimeSpan.FromMinutes(1), fire: false);
            sink.Write(Entry(clock, "恢复尾部"));
            Assert.Contains("恢复尾部", ReadOpen(Files(directory).Single()));
        });

        Test("PERF-13释放：Dispose幂等，已排队维护回调和迟到写入都不能再删或打开文件", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var sink = NewSink(directory, clock);
            sink.Write(Entry(clock, "释放前尾部"));
            var queued = clock.CaptureCallback();
            var before = Files(directory).Single();
            sink.Dispose();
            sink.Dispose();
            Assert.Equal(0, clock.ActiveTimers);
            clock.Advance(TimeSpan.FromDays(20), fire: false);
            queued();
            sink.Write(Entry(clock, "迟到不能写"));
            Assert.True(File.Exists(before));
            Assert.DoesNotContain("迟到不能写", File.ReadAllText(before));
            using var exclusive = new FileStream(before, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        });

        Test("PERF-13崩溃尾部：私有子进程完成写入后不Dispose直接退出，最后一行仍完整可读", () =>
            VerifyTailChildAsync().WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult());

        Test("PERF-13同时间边界：固定clock的基础卷先于分卷淘汰，最近完整尾部不被编号排序反转", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock, file: 128, total: 256);
            sink.Write(ExactEntry(clock, 128));
            var basePart = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 0));
            sink.Write(ExactEntry(clock, 128));
            var firstPart = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 1));
            sink.Write(ExactEntry(clock, 128));
            Assert.False(File.Exists(basePart), "同时间戳不能按 .log 大于 -000001 的字典序保住最旧卷");
            Assert.True(File.Exists(firstPart));
            Assert.True(File.Exists(System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 2))));
            AssertBudget(directory, sink, 128, 256);
        });

        Test("PERF-13预算：默认72MiB合成洪峰每条后单卷不超8MiB、总量不超64MiB", () =>
        {
            using var directory = new BudgetDirectory();
            using var sink = new FileLogSink(directory.Path);
            var message = new string('A', 3 * 1024 * 1024);
            for (var index = 0; index < 24; index++)
            {
                sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Info, "预算", message, null));
                AssertBudget(directory, sink, 8 * 1024 * 1024, 64 * 1024 * 1024);
            }
            Console.WriteLine($"    默认总预算压力 files={Files(directory).Length} totalBytes={DiskBytes(directory)} maxFileBytes={Files(directory).Max(path => new FileInfo(path).Length)}");
        });

        Test("PERF-13并发：8写入者轮转/索引锁下800条不丢行，尾部即时可读且释放全部句柄", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock, file: 512, total: 256 * 1024);
            var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
            {
                for (var index = 0; index < 100; index++) sink.Write(Entry(clock, $"writer-{worker}-line-{index}"));
            })).ToArray();
            Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var lines = Files(directory).SelectMany(path => ReadOpen(path).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            Assert.Equal(800, lines.Length);
            Assert.Equal(800, lines.Distinct(StringComparer.Ordinal).Count());
            AssertBudget(directory, sink, 512, 256 * 1024);
        });

        Test("PERF-13重启：已有分卷编号int上界拒绝回绕，不覆盖旧文件也不抛到应用", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var old = Seed(directory, clock, int.MaxValue, 128, ageDays: 0);
            using var sink = NewSink(directory, clock);
            sink.Write(Entry(clock, "不能回绕"));
            Assert.Equal(1, Files(directory).Length);
            Assert.Equal(128L, new FileInfo(old).Length);
        });
    }

    private static string Seed(BudgetDirectory directory, BudgetClock clock, int part, int bytes, int ageDays)
    {
        var path = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), part));
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(new string('A', bytes)));
        File.SetLastWriteTimeUtc(path, clock.Now.AddDays(-ageDays).UtcDateTime);
        return path;
    }

    private static async Task VerifyTailChildAsync()
    {
        using var directory = new BudgetDirectory();
        var processPath = Environment.ProcessPath!;
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (System.IO.Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("file-tail-child");
        start.ArgumentList.Add(directory.Path);
        using var process = Process.Start(start) ?? throw new AssertionException("私有尾部子进程未启动");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, process.ExitCode, await stderr);
        Assert.Contains("completed-disk-write", await stdout);
        var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Files(directory).Single()));
        Assert.Contains("child-final-tail🌸", text);
        Assert.True(text.EndsWith(Environment.NewLine, StringComparison.Ordinal));
    }

    internal static int WriteTailChild(string path)
    {
        if (!BudgetDirectory.Owns(path)) return 2;
        var sink = new FileLogSink(path);
        sink.Write(new LogEntry(DateTimeOffset.Now, LogLevel.Error, "合成尾部", "child-final-tail🌸", null));
        Console.WriteLine("completed-disk-write");
        Environment.Exit(0); // Only this disposable test child: deliberately bypass sink disposal.
        return 0;
    }
}

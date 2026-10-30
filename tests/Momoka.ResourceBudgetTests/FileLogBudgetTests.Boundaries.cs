using System.Globalization;
using System.Reflection;
using System.Text;
using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FileLogBudgetTests
{
    private static void RegisterBoundaries()
    {
        Test("PERF-13参数：非法预算/保留期/级别/维护周期在创建文件与定时器前拒绝", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var target = System.IO.Path.Combine(directory.Path, "not-created");
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogSink(target, LogLevel.Debug, -1, 128, 256, clock));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogSink(target, LogLevel.Debug, 14, 127, 256, clock));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogSink(target, LogLevel.Debug, 14, (long)int.MaxValue + 1, long.MaxValue, clock));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogSink(target, LogLevel.Debug, 14, 128, 127, clock));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogSink(target, (LogLevel)100, 14, 128, 256, clock));
            foreach (var period in new[] { TimeSpan.Zero, TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(-1), TimeSpan.MaxValue })
                Assert.Throws<ArgumentOutOfRangeException>(() => new FileLogSink(target, LogLevel.Debug, 14, 128, 256, clock, period));
            Assert.Throws<ArgumentNullException>(() => new FileLogSink(target, LogLevel.Debug, 14, 128, 256, null!));
            Assert.False(Directory.Exists(target));
            Assert.Equal(0, clock.TimerCreations);
            using var minimum = NewSink(directory, clock, file: 128, total: 128, retain: 0, period: TimeSpan.FromMilliseconds(1));
            Assert.Equal(1, clock.ActiveTimers);
            using var maximum = NewSink(directory, clock, file: int.MaxValue, total: long.MaxValue, retain: int.MaxValue,
                period: TimeSpan.FromMilliseconds(uint.MaxValue - 1d));
        });

        Test("PERF-13边界：单条恰好128字节不截断，下一条轮转，最小总预算仍保住最新完整尾部", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock, file: 128, total: 128);
            var entry = ExactEntry(clock, 128);
            sink.Write(entry);
            var first = Files(directory).Single();
            Assert.Equal(128L, new FileInfo(first).Length);
            Assert.Equal(entry.ToString() + Environment.NewLine, ReadOpen(first));
            sink.Write(Entry(clock, "新尾部"));
            Assert.False(File.Exists(first));
            Assert.True(DiskBytes(directory) <= 128);
            Assert.Contains("新尾部", ReadOpen(Files(directory).Single()));
        });

        Test("PERF-13边界：超大多行Unicode entry保留头尾并标明省略，UTF8字节和代理对不越预算", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock, file: 512, total: 1024);
            var entry = new LogEntry(clock.Now, LogLevel.Error, "合成日志", "HEADER头部🛰",
                string.Concat(Enumerable.Repeat("中🌸文\n", 4000)) + "TAIL尾部🚀");
            sink.Write(entry);
            var path = Files(directory).Single();
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            reader.CopyTo(copy);
            var bytes = copy.ToArray();
            Assert.True(bytes.Length <= 512);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            Assert.Contains("HEADER头部🛰", text);
            Assert.Contains("已省略中段", text);
            Assert.Contains("TAIL尾部🚀", text);
            Assert.False(text.Contains('�'));
            Assert.True(text.EndsWith(Environment.NewLine, StringComparison.Ordinal));
            Assert.Equal((long)bytes.Length, IndexedBytes(sink));
            Console.WriteLine($"    截断证据 inputChars={entry.ToString().Length} diskBytes={bytes.Length} limit=512");
        });

        Test("PERF-13边界：默认大entry超过8MiB也不生成超大单卷并保留最后诊断", () =>
        {
            using var directory = new BudgetDirectory();
            using var sink = new FileLogSink(directory.Path);
            var entry = new LogEntry(DateTimeOffset.Now, LogLevel.Error, "合成日志", "HEAD",
                new string('A', 9 * 1024 * 1024) + "TAIL");
            sink.Write(entry);
            var path = Files(directory).Single();
            Assert.True(new FileInfo(path).Length <= 8 * 1024 * 1024);
            var text = ReadOpen(path);
            Assert.Contains("HEAD", text);
            Assert.Contains("TAIL", text);
            Assert.Contains("已省略中段", text);
            Console.WriteLine($"    默认超大entry证据 diskBytes={new FileInfo(path).Length} limit=8388608");
        });

        Test("PERF-13边界：同日重启永远新卷，崩溃半行/不完整UTF8既不覆盖也不拼接", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var legacy = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 0));
            var partial = Encoding.UTF8.GetBytes("历史半行").Concat(new byte[] { 0xF0, 0x9F }).ToArray();
            File.WriteAllBytes(legacy, partial);
            File.SetLastWriteTimeUtc(legacy, clock.Now.UtcDateTime);
            using (var sink = NewSink(directory, clock)) sink.Write(Entry(clock, "第一次重启"));
            using (var sink = NewSink(directory, clock)) sink.Write(Entry(clock, "第二次重启"));
            Assert.True(File.ReadAllBytes(legacy).SequenceEqual(partial));
            var first = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 1));
            var second = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 2));
            Assert.Contains("第一次重启", File.ReadAllText(first));
            Assert.DoesNotContain("第二次重启", File.ReadAllText(first));
            Assert.Contains("第二次重启", File.ReadAllText(second));
            Assert.Equal(3, Files(directory).Length);
        });

        Test("PERF-13边界：一日多卷按既有最大编号继续，跨日与时钟回退不覆盖任何卷", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            foreach (var part in new[] { 0, 2, 5 })
            {
                var path = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), part));
                File.WriteAllText(path, "prior-" + part);
                File.SetLastWriteTimeUtc(path, clock.Now.UtcDateTime);
            }
            using var sink = NewSink(directory, clock, file: 128, total: 2048);
            sink.Write(ExactEntry(clock, 128));
            Assert.True(File.Exists(System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 6))));
            sink.Write(ExactEntry(clock, 128));
            Assert.True(File.Exists(System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 7))));
            clock.Advance(TimeSpan.FromDays(1));
            sink.Write(Entry(clock, "明日"));
            clock.Advance(TimeSpan.FromDays(-1));
            sink.Write(Entry(clock, "回到昨日"));
            Assert.Contains("回到昨日", ReadOpen(System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 8))));
            Assert.Equal("prior-5", File.ReadAllText(System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 5))));
        });

        Test("PERF-13编码预算：128至2048字节截断在多种Unicode长度下始终精确计数且不切代理对", () =>
        {
            foreach (var unit in new[] { "A", "é", "中", "🌸", "é🌸" })
                foreach (var budget in new[] { 128, 129, 256, 511, 1024, 2048 })
                {
                    var original = "HEAD" + string.Concat(Enumerable.Repeat(unit, 3000)) + "TAIL";
                    var bounded = FileLogFormat.BoundLine(original, budget);
                    Assert.True(bounded.Bytes <= budget);
                    var bytes = new UTF8Encoding(false, true).GetBytes(bounded.Line + Environment.NewLine);
                    Assert.Equal(bounded.Bytes, bytes.Length);
                    Assert.Contains("HEAD", bounded.Line);
                    Assert.Contains("TAIL", bounded.Line);
                }
        });

        Test("PERF-13命名：日期/编号不随当前文化变动，非法及非日志文件名不接管", () =>
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
                var day = new DateOnly(2026, 10, 30);
                Assert.Equal("app-20261030-000042.log", FileLogFormat.Name(day, 42));
                Assert.True(FileLogFormat.TryReadName("app-20261030-000042.log", out var parsed, out var part));
                Assert.Equal(day, parsed);
                Assert.Equal(42, part);
                Assert.True(FileLogFormat.TryReadName("app-20261030.log", out _, out _));
                foreach (var invalid in new[] { "app-.log", "app-20261330.log", "app-20261030-0.log", "app-20261030--1.log",
                    "app-20261030-2147483648.log", "other.log", "app-important.log", "app-20261030.json" })
                    Assert.False(FileLogFormat.TryReadName(invalid, out _, out _), invalid);
                Assert.Throws<ArgumentOutOfRangeException>(() => FileLogFormat.Name(day, -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => FileLogFormat.BoundLine("line", 127));
            }
            finally { CultureInfo.CurrentCulture = old; }
        });

        Test("PERF-13预算：连续1000条压力每条之后单卷/总量均不越界且索引等于实际字节", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            using var sink = NewSink(directory, clock, file: 512, total: 1536);
            for (var index = 0; index < 1000; index++)
            {
                sink.Write(Entry(clock, index + "：" + new string('界', 120)));
                AssertBudget(directory, sink, 512, 1536);
            }
            Assert.Contains("999：", string.Join("", Files(directory).Select(ReadOpen)));
            Console.WriteLine($"    小预算压力 files={Files(directory).Length} totalBytes={DiskBytes(directory)} maxBytes={Files(directory).Max(path => new FileInfo(path).Length)}");
        });
    }

    private static FileLogSink NewSink(BudgetDirectory directory, BudgetClock clock, long file = 512, long total = 4096,
        int retain = 14, TimeSpan? period = null) => new(directory.Path, LogLevel.Debug, retain, file, total, clock, period);
    private static LogEntry Entry(BudgetClock clock, string message) => new(clock.Now, LogLevel.Info, "预算", message, null);
    private static DateOnly Day(BudgetClock clock) => DateOnly.FromDateTime(clock.Now.DateTime);
    private static LogEntry ExactEntry(BudgetClock clock, int bytes)
    {
        var empty = Entry(clock, "");
        var prefix = FileLogFormat.Utf8.GetByteCount(empty.ToString() + Environment.NewLine);
        return Entry(clock, new string('A', bytes - prefix));
    }
    private static string[] Files(BudgetDirectory directory) => Directory.GetFiles(directory.Path, "app-*.log");
    private static long DiskBytes(BudgetDirectory directory) => Files(directory).Sum(path => new FileInfo(path).Length);
    private static long IndexedBytes(FileLogSink sink) => (long)typeof(FileLogSink)
        .GetField("_totalBytes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sink)!;
    private static string ReadOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }
    private static void AssertBudget(BudgetDirectory directory, FileLogSink sink, long file, long total)
    {
        Assert.True(Files(directory).All(path => new FileInfo(path).Length <= file), "单文件预算");
        var disk = DiskBytes(directory);
        Assert.True(disk <= total, $"总字节 {disk} > {total}");
        Assert.Equal(disk, IndexedBytes(sink), "正常写/删之后索引不能漏计");
    }
}

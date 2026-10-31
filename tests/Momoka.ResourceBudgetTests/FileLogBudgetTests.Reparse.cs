using Momoka.Diagnostics;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FileLogBudgetTests
{
    private static void RegisterReparseBoundaries()
    {
        foreach (var nested in new[] { false, true })
        {
            var throughAncestor = nested;
            Test($"PERF-13重解析：启动拒绝{(nested ? "祖先" : "日志目录")}链接，不扫描/删除/写入链接目标", () =>
            {
                using var directory = new BudgetDirectory();
                var clock = new BudgetClock();
                var target = OwnedChild(directory, "target");
                var link = OwnedChild(directory, "alias");
                var targetLogs = throughAncestor ? System.IO.Path.Combine(target, "logs") : target;
                Directory.CreateDirectory(targetLogs);
                var sentinel = System.IO.Path.Combine(targetLogs, "app-20000101.log");
                File.WriteAllText(sentinel, "protected-target");
                File.SetLastWriteTimeUtc(sentinel, clock.Now.AddDays(-30).UtcDateTime);
                Directory.CreateSymbolicLink(link, target);
                try
                {
                    Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
                    var logs = throughAncestor ? System.IO.Path.Combine(link, "logs") : link;
                    Assert.Throws<UnauthorizedAccessException>(() =>
                        new FileLogSink(logs, LogLevel.Debug, 14, 128, 256, clock));
                    Assert.Equal("protected-target", File.ReadAllText(sentinel));
                    Assert.Equal(1, Directory.GetFiles(targetLogs).Length);
                    Assert.Equal(0, clock.TimerCreations);
                }
                finally { Directory.Delete(link); } // The checked, private link only; never its target.
            });
        }

        Test("PERF-13重解析：文件链接既不接管也不覆盖，CreateNew碰撞只另开自己新卷", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var target = OwnedChild(directory, "target.txt");
            var link = System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 0));
            File.WriteAllText(target, "protected-file-target");
            File.SetLastWriteTimeUtc(target, clock.Now.AddDays(-30).UtcDateTime);
            File.CreateSymbolicLink(link, target);
            try
            {
                using var sink = NewSink(directory, clock, file: 128, total: 256);
                Assert.Equal(0L, IndexedBytes(sink));
                sink.Write(Entry(clock, "independent-tail"));
                Assert.Contains("independent-tail", ReadOpen(System.IO.Path.Combine(directory.Path, FileLogFormat.Name(Day(clock), 1))));
                clock.Advance(TimeSpan.FromDays(20));
                Assert.True(File.Exists(link));
                Assert.Equal("protected-file-target", File.ReadAllText(target));
                Assert.Equal(0L, IndexedBytes(sink));
            }
            finally { File.Delete(link); }
        });

        Test("PERF-13重解析：运行期目录被链接替换后维护不删目标，轮转不写目标，恢复原目录后可继续", () =>
        {
            using var directory = new BudgetDirectory();
            var clock = new BudgetClock();
            var owned = OwnedChild(directory, "logs");
            var parked = OwnedChild(directory, "original-logs");
            var target = OwnedChild(directory, "target");
            Directory.CreateDirectory(target);
            using var sink = new FileLogSink(owned, LogLevel.Debug, 14, 128, 4096, clock);
            sink.Write(ExactEntry(clock, 128));
            var first = Directory.GetFiles(owned).Single();
            clock.Advance(TimeSpan.FromDays(1));
            sink.Write(Entry(clock, "original-tail"));
            var originalNames = Directory.GetFiles(owned).Select(System.IO.Path.GetFileName).ToArray();
            foreach (var name in originalNames) File.WriteAllText(System.IO.Path.Combine(target, name!), "protected-runtime-target");
            // Windows will not rename a directory while its writer is open. Execute the owner's real
            // close path (also used by rotation/maintenance), retaining its index, then replace the path.
            var members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var writerField = typeof(FileLogSink).GetField("_writer", members)!;
            var writer = (StreamWriter)writerField.GetValue(sink)!;
            var handle = ((FileStream)writer.BaseStream).SafeFileHandle;
            typeof(FileLogSink).GetMethod("Close", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(sink, null);
            Assert.True(handle.IsClosed, "真实 Close 必须释放 Windows 文件句柄");
            Assert.True(writerField.GetValue(sink) is null);
            Assert.True(typeof(FileLogSink).GetField("_current", members)!.GetValue(sink) is null);
            Console.WriteLine($"    rename preflight: handleClosed={handle.IsClosed}; attributes={File.GetAttributes(owned)}; owned={owned}; parked={parked}");
            Directory.Move(owned, parked); // Both absolute paths were checked inside this fixture.
            Directory.CreateSymbolicLink(owned, target);
            try
            {
                clock.Advance(TimeSpan.FromDays(20)); // Real production maintenance, with an inert fixture timer.
                sink.Write(Entry(clock, "must-not-redirect"));
                Assert.Equal(originalNames.Length, Directory.GetFiles(target).Length);
                Assert.True(Directory.GetFiles(target).All(path => File.ReadAllText(path) == "protected-runtime-target"));
                Assert.True(File.Exists(System.IO.Path.Combine(parked, System.IO.Path.GetFileName(first))));
            }
            finally
            {
                Directory.Delete(owned);
                Directory.Move(parked, owned);
            }
            clock.Advance(TimeSpan.FromMinutes(15));
            sink.Write(Entry(clock, "restored-tail"));
            Assert.Contains("restored-tail", string.Join("", Directory.GetFiles(owned).Select(ReadOpen)));
            Assert.Equal(Directory.GetFiles(owned).Sum(path => new FileInfo(path).Length), IndexedBytes(sink));
            Assert.True(Directory.GetFiles(target).All(path => File.ReadAllText(path) == "protected-runtime-target"));
        });
    }

    private static string OwnedChild(BudgetDirectory directory, string name)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory.Path, name));
        Assert.True(BudgetDirectory.Owns(path));
        Assert.True(path.StartsWith(directory.Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        return path;
    }
}

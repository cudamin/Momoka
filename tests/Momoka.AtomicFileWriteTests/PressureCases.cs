using System.Text;
using Momoka.Infrastructure;

namespace Momoka.AtomicFileWriteTests;

internal static class PressureCases
{
    // Fixed budgets/seeds are part of this contract. No command-line seed/repeat overrides,
    // retries, success thresholds or ignoring the first IO/access error.
    public const int TargetBudget = 256;
    public const int WritesPerTarget = 12;
    public const int ConcurrentWriters = 4;

    public static IEnumerable<TestCase> All()
    {
        yield return new("pressure-serial-mixed", "6：固定256匿名目标/3072串行文本字节写入，首个失败即红",
            fixture => Run(fixture, concurrent: false, backup: false, seed: 0x6A09E667));
        yield return new("pressure-concurrent-mixed", "6：固定256匿名目标/3072混合文本字节竞写，首个失败即红",
            fixture => Run(fixture, concurrent: true, backup: false, seed: 0xBB67AE85));
        yield return new("pressure-backup-serial", "5/6：固定256匿名目标/3072备份串行写入，每步备份精确为前值",
            fixture => Run(fixture, concurrent: false, backup: true, seed: 0x3C6EF372));
        yield return new("pressure-backup-concurrent", "5/6：固定256匿名目标/3072备份竞写+256尾写，主/备完整且尾写前值精确",
            fixture => Run(fixture, concurrent: true, backup: true, seed: 0xA54FF53A));
    }

    private sealed record Payload(string Text, Encoding Encoding, byte[] Bytes, bool AsBytes);

    private sealed class FixedRandom(uint seed)
    {
        private uint _state = seed;
        public uint Next()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return _state = value;
        }
    }

    private static Payload[] Payloads(FixedRandom random, int target, bool backup)
    {
        var payloads = new Payload[WritesPerTarget];
        for (var operation = 0; operation < payloads.Length; operation++)
        {
            var text = Documents.Text(target * WritesPerTarget + operation,
                65_536 + (int)(random.Next() % 1009), random.Next());
            Encoding encoding = backup && operation % 3 == 0 ? new UTF8Encoding(true, true) : Documents.Utf8;
            payloads[operation] = new(text, encoding, Documents.Encoded(text, encoding), !backup && operation % 2 != 0);
        }
        return payloads;
    }

    private static void Write(string target, string? backup, Payload payload)
    {
        if (payload.AsBytes) AtomicFile.WriteAllBytes(target, payload.Bytes);
        else AtomicFile.WriteAllText(target, payload.Text, payload.Encoding, backup);
    }

    private static void Run(TestFixture fixture, bool concurrent, bool backup, uint seed)
    {
        var successfulCommits = 0;
        var completedTargets = 0;
        var random = new FixedRandom(seed);
        fixture.Fact("seed", $"0x{seed:X8}");
        fixture.Fact("targetBudget", TargetBudget);
        fixture.Fact("writesPerTarget", WritesPerTarget);
        fixture.Fact("commitBudget", TargetBudget * (WritesPerTarget + (concurrent && backup ? 1 : 0)));
        fixture.Fact("concurrentWriters", concurrent ? ConcurrentWriters : 1);
        fixture.Fact("retryBudget", 0);
        try
        {
            for (var targetIndex = 0; targetIndex < TargetBudget; targetIndex++)
            {
                var directory = fixture.Subdirectory($"target-{targetIndex:D3}");
                var target = Path.Combine(directory, "anonymous.bin");
                var backupPath = Path.Combine(directory, "anonymous.backup");
                var payloads = Payloads(random, targetIndex, backup);
                fixture.AtOperation(concurrent ? "concurrent" : "serial", targetIndex, 0, backup ? "backup" : "mixed");
                if (!concurrent)
                {
                    byte[]? previous = null;
                    for (var operation = 0; operation < payloads.Length; operation++)
                    {
                        var payload = payloads[operation];
                        fixture.AtOperation("serial", targetIndex, operation, payload.AsBytes ? "bytes" : "text");
                        Write(target, backup ? backupPath : null, payload);
                        successfulCommits++;
                        Check.Bytes(payload.Bytes, FileIo.ReadComplete(target), $"串行 target={targetIndex} operation={operation} 主文件");
                        if (backup && previous is not null)
                            Check.Bytes(previous, FileIo.ReadComplete(backupPath), $"串行 target={targetIndex} operation={operation} 备份前值");
                        else Check.That(!File.Exists(backupPath), "没有完整前主值时不伪造备份");
                        Check.DirectoryFiles(directory, backup && previous is not null ? [target, backupPath] : [target]);
                        previous = payload.Bytes;
                    }
                }
                else
                {
                    var initial = Documents.Utf8.GetBytes(Documents.Text(-1, 65_536, (uint)targetIndex));
                    File.WriteAllBytes(target, initial);
                    if (backup) File.WriteAllText(backupPath, "anonymous-initial-backup", Documents.Utf8);
                    var first = new FirstFailure();
                    Parallel.For(0, payloads.Length, new ParallelOptions { MaxDegreeOfParallelism = ConcurrentWriters },
                        (operation, state) =>
                        {
                            if (first.Error is not null) { state.Stop(); return; }
                            var payload = payloads[operation];
                            try
                            {
                                Write(target, backup ? backupPath : null, payload);
                                Interlocked.Increment(ref successfulCommits);
                            }
                            catch (Exception error)
                            {
                                first.Set(new ContractFailure($"竞写首个失败：seed=0x{seed:X8} target={targetIndex:D3} " +
                                    $"operation={operation} kind={(payload.AsBytes ? "bytes" : "text")} " +
                                    $"type={error.GetType().FullName} HResult=0x{error.HResult:X8}", error));
                                state.Stop();
                            }
                        });
                    if (first.Error is { } failure) throw failure;
                    var candidates = payloads.Select(payload => payload.Bytes).ToArray();
                    var primary = FileIo.ReadComplete(target);
                    Check.CompleteCandidate(candidates, primary, $"竞写 target={targetIndex} 主文件");
                    if (backup)
                    {
                        var beforeTailBackup = FileIo.ReadComplete(backupPath);
                        Check.CompleteCandidate([initial, .. candidates], beforeTailBackup, $"竞写 target={targetIndex} 备份");
                        Check.That(!primary.AsSpan().SequenceEqual(beforeTailBackup), "不同竞写文档的主/备必须不同，不以返回顺序猜提交顺序");
                        // Parallel completion order is not commit order. A synchronous tail write
                        // fixes an observable predecessor without adding test locks around production.
                        var tailText = Documents.Text(1_000_000 + targetIndex, 67_003, random.Next());
                        fixture.AtOperation("tail", targetIndex, WritesPerTarget, "text-with-backup");
                        AtomicFile.WriteAllText(target, tailText, Documents.Utf8, backupPath);
                        successfulCommits++;
                        Check.Bytes(Documents.Utf8.GetBytes(tailText), FileIo.ReadComplete(target), "尾写完整新主文件");
                        Check.Bytes(primary, FileIo.ReadComplete(backupPath), "尾写备份精确是已观察的完整前主文件");
                    }
                    Check.DirectoryFiles(directory, backup ? [target, backupPath] : [target]);
                }
                completedTargets++;
            }
        }
        finally
        {
            fixture.Fact("successfulCommits", successfulCommits);
            fixture.Fact("completedTargets", completedTargets);
            fixture.Fact("allBudgetCompleted", completedTargets == TargetBudget);
        }
        Check.Equal(TargetBudget * (WritesPerTarget + (concurrent && backup ? 1 : 0)), successfulCommits,
            "不能减少预算/跳过失败目标来取绿");
    }
}

using Momoka.Infrastructure;

namespace Momoka.AtomicFileWriteTests;

internal static class ReaderCases
{
    private const int Rounds = 32;
    private const int ReaderCount = 4;
    private const int WritesPerRound = 4;
    private const int FreshReadsPerRound = 8;
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(20);

    public static IEnumerable<TestCase> All()
    {
        yield return new("readers-cross-commit-mixed", "7：四个真实共享 Delete reader 跨128次混合提交，无半文档/路径空窗",
            fixture => AcrossCommits(fixture, backup: false));
        yield return new("readers-cross-commit-backup", "7/5：共享 Delete 读者跨128次 File.Replace 备份提交，每次备份完整前值",
            fixture => AcrossCommits(fixture, backup: true));
        yield return new("source-attributes-shared-delete-commit", "真实完成 source/target 属性与实际私有 Write commit/清理",
            SourceCommit);
    }

    private static void SourceCommit(TestFixture fixture)
    {
        var target = fixture.FilePath("anonymous.bin");
        var original = Documents.Utf8.GetBytes(Documents.Text(0, 65_539));
        var replacement = Documents.Utf8.GetBytes(Documents.Text(1, 79_009));
        File.WriteAllBytes(target, original);
        using var reader = FileIo.OpenReader(target);
        string? observed = null;
        fixture.Snapshot("targetBefore", target);
        try
        {
            ProductionBody.Invoke(target, temporary =>
            {
                observed = temporary;
                File.WriteAllBytes(temporary, replacement);
                var source = FileState.Capture(temporary);
                fixture.Fact("sourceBeforeCommit", source);
                Check.That((source.Attributes!.Value & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0,
                    "实际 source 是完整普通文件而非 ReadOnly/重解析/目录");
                Check.Bytes(replacement, FileIo.ReadComplete(temporary), "实际 source 完整正文");
            }, backup: null);
        }
        finally
        {
            fixture.Fact("sourceObserved", observed is not null);
            fixture.Fact("sourceCleaned", observed is not null && !File.Exists(observed));
            fixture.Snapshot("targetAfter", target);
        }
        Check.That(observed is not null && !File.Exists(observed), "真实完成源提交/清理后不得残留");
        Check.Bytes(replacement, FileIo.ReadComplete(target), "真实 source 提交成完整新目标");
        Check.Bytes(original, FileIo.ReadRemainder(reader), "属性观测不改变共享 Delete 旧句柄契约");
        Check.DirectoryFiles(fixture.Root, target);
    }

    private static void AcrossCommits(TestFixture fixture, bool backup)
    {
        var target = fixture.FilePath("anonymous.bin");
        var backupPath = fixture.FilePath("anonymous.backup");
        var previous = Documents.Utf8.GetBytes(Documents.Text(-1, 70_003));
        File.WriteAllBytes(target, previous);
        if (backup) File.WriteAllText(backupPath, "anonymous-previous-backup", Documents.Utf8);
        var successfulCommits = 0;
        var completedRounds = 0;
        var completeReads = 0;
        fixture.Fact("roundBudget", Rounds);
        fixture.Fact("readerCount", ReaderCount);
        fixture.Fact("writesPerRound", WritesPerRound);
        fixture.Fact("freshReadsPerReaderRound", FreshReadsPerRound);
        fixture.Fact("retryBudget", 0);
        try
        {
            for (var round = 0; round < Rounds; round++)
            {
                var texts = Enumerable.Range(0, WritesPerRound)
                    .Select(index => Documents.Text(round * WritesPerRound + index, 70_003 + index * 307, (uint)round)).ToArray();
                var replacements = texts.Select(Documents.Utf8.GetBytes).ToArray();
                var original = previous;
                var last = replacements[^1];
                var candidates = new[] { original }.Concat(replacements).ToArray();
                var first = new FirstFailure();
                using var ready = new CountdownEvent(ReaderCount);
                using var start = new ManualResetEventSlim(false);
                using var committed = new ManualResetEventSlim(false);
                var writerCompleted = false;
                var readers = Enumerable.Range(0, ReaderCount).Select(readerIndex => new Thread(() =>
                {
                    var signaled = false;
                    try
                    {
                        using var pinned = FileIo.OpenReader(target);
                        var prefix = new byte[113 + readerIndex * 23];
                        pinned.ReadExactly(prefix);
                        Check.Bytes(original[..prefix.Length], prefix, "reader 提交前必须已读到原文前缀");
                        ready.Signal();
                        signaled = true;
                        Check.That(start.Wait(WaitBudget), "reader 等待开始超预算，不跳过");
                        for (var read = 0; read < FreshReadsPerRound; read++)
                        {
                            // Fresh opens are deliberately unsynchronized with individual commits.
                            // A missing path, partial UTF-8 or spliced document is a failure, not retry.
                            Check.CompleteCandidate(candidates, FileIo.ReadComplete(target),
                                $"round={round} reader={readerIndex} fresh={read} 不得看半文档/空窗");
                            Interlocked.Increment(ref completeReads);
                        }
                        Check.That(committed.Wait(WaitBudget), "reader 等待提交超预算，不跳过");
                        Check.That(!pinned.SafeFileHandle.IsClosed, "pinned reader 必须保持到全部提交后");
                        Check.Bytes(original, FileIo.ReadRemainder(pinned, prefix), "旧 reader 跨提交仍是同一完整原文");
                        Interlocked.Increment(ref completeReads);
                        if (writerCompleted)
                        {
                            Check.Bytes(last, FileIo.ReadComplete(target), "提交后新 reader 得到最终完整文档");
                            Interlocked.Increment(ref completeReads);
                        }
                    }
                    catch (Exception error)
                    {
                        first.Set(new ContractFailure($"reader 首个失败：round={round} reader={readerIndex} " +
                            $"type={error.GetType().FullName} HResult=0x{error.HResult:X8}", error));
                    }
                    finally { if (!signaled) ready.Signal(); }
                })
                { IsBackground = true, Name = $"anonymous-reader-{readerIndex}" }).ToArray();
                try
                {
                    foreach (var reader in readers) reader.Start();
                    Check.That(ready.Wait(WaitBudget), "必须四个真实 reader 已打开并读前缀，超预算即失败");
                    if (first.Error is { } setupError) throw setupError;
                    start.Set();
                    for (var operation = 0; operation < WritesPerRound; operation++)
                    {
                        if (first.Error is { } readerError) throw readerError;
                        fixture.AtOperation("readers-held", round, operation, backup || operation % 2 == 0 ? "text" : "bytes");
                        if (backup || operation % 2 == 0)
                            AtomicFile.WriteAllText(target, texts[operation], Documents.Utf8, backup ? backupPath : null);
                        else AtomicFile.WriteAllBytes(target, replacements[operation]);
                        successfulCommits++;
                        if (backup) Check.Bytes(previous, FileIo.ReadComplete(backupPath), "有读者时每次备份精确为前主文件");
                        previous = replacements[operation];
                    }
                    writerCompleted = true;
                }
                catch (Exception error) { first.Set(error); }
                finally
                {
                    start.Set();
                    committed.Set();
                    foreach (var reader in readers)
                        if ((reader.ThreadState & ThreadState.Unstarted) == 0 && !reader.Join(WaitBudget))
                            first.Set(new ContractFailure("本轮匿名 reader 未按固定预算结束，不降级为通过"));
                }
                if (first.Error is { } failure) throw failure;
                Check.DirectoryFiles(fixture.Root, backup ? [target, backupPath] : [target]);
                completedRounds++;
            }
        }
        finally
        {
            fixture.Fact("successfulCommits", successfulCommits);
            fixture.Fact("completedRounds", completedRounds);
            fixture.Fact("completeReads", completeReads);
            fixture.Snapshot("finalTarget", target);
            if (backup) fixture.Snapshot("finalBackup", backupPath);
        }
        Check.Equal(Rounds * WritesPerRound, successfulCommits, "读者压力完整提交预算");
        Check.Equal(Rounds * ReaderCount * (FreshReadsPerRound + 2), completeReads, "完整读者预算，不能只验证末值");
    }
}

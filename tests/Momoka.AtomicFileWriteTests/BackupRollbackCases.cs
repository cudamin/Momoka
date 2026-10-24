using System.Reflection;
using Momoka.Infrastructure;

namespace Momoka.AtomicFileWriteTests;

internal static class BackupRollbackCases
{
    private sealed class PublicationFailure : Exception { }

    private static readonly Action<string, string, string, Action<string, string>> Commit =
        (typeof(AtomicFile).GetMethod("CommitWithIsolatedBackup", BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: [typeof(string), typeof(string), typeof(string), typeof(Action<string, string>)],
            modifiers: null) ?? throw new ContractFailure("实际备份发布/回滚边界缺失"))
        .CreateDelegate<Action<string, string, string, Action<string, string>>>();

    public static IEnumerable<TestCase> All()
    {
        yield return new("backup-publication-failure-rolls-back", "生产备份发布故障：恢复主文件原字节和旧备份，无临时残留",
            fixture => PublicationFails(fixture, keepOldReader: false, denyRollback: false));
        yield return new("backup-publication-failure-shared-reader-rolls-back", "生产备份发布故障：旧共享 Delete reader 不阻止恢复同一完整原件",
            fixture => PublicationFails(fixture, keepOldReader: true, denyRollback: false));
        yield return new("backup-rollback-denied-retains-recovery", "生产回滚被真实锁拒绝：抛两个异常并给出完整前主文件恢复路径，旧备份不改",
            fixture => PublicationFails(fixture, keepOldReader: true, denyRollback: true));
    }

    private static void PublicationFails(TestFixture fixture, bool keepOldReader, bool denyRollback)
    {
        var target = fixture.FilePath("primary.bin");
        var backup = fixture.FilePath("previous.backup");
        var source = fixture.FilePath("new-complete.tmp");
        var original = Documents.Utf8.GetBytes(Documents.Text(81, 70001));
        var older = Documents.Utf8.GetBytes(Documents.Text(80, 70001));
        var next = Documents.Utf8.GetBytes(Documents.Text(82, 70001));
        File.WriteAllBytes(target, original);
        File.WriteAllBytes(backup, older);
        File.WriteAllBytes(source, next);
        var backupBefore = FileState.Capture(backup);
        FileStream? oldReader = keepOldReader ? FileIo.OpenReader(target) : null;
        FileStream? rollbackLock = null;
        var publicationFailure = new PublicationFailure();
        var publicationCalls = 0;
        string? recovery = null;
        try
        {
            void FailPublication(string predecessor, string destination)
            {
                publicationCalls++;
                recovery = predecessor;
                Check.Equal(backup, destination, "只发布目标备份");
                Check.Bytes(next, FileIo.ReadComplete(target), "前向已提交完整新主文件");
                Check.Bytes(original, FileIo.ReadComplete(predecessor), "前主文件完整保留到发布后");
                Check.Preserved(backupBefore, backup, "故障点旧备份必须未修改");
                if (denyRollback)
                    rollbackLock = FileIo.OpenReader(target, FileShare.ReadWrite);
                throw publicationFailure;
            }
            if (denyRollback)
            {
                var error = Check.Failure<AggregateException>(() => Commit(source, target, backup, FailPublication),
                    "回滚真实共享拒绝必须保留发布/回滚两条失败");
                Check.Equal(2, error.InnerExceptions.Count, "两条失败不被吞掉");
                Check.That(ReferenceEquals(publicationFailure, error.InnerExceptions[0]), "原发布异常对象保持");
                Check.That(error.InnerExceptions[1] is IOException or UnauthorizedAccessException, "内核回滚拒绝保持IO/access类型");
                Check.Equal(recovery, error.Data["RecoveryPath"] as string, "交接实际可恢复前值路径");
                Check.Bytes(next, FileIo.ReadComplete(target), "回滚拒绝后的已提交主文件仍完整");
                Check.Bytes(original, FileIo.ReadComplete(recovery!), "回滚拒绝不删除前值");
                Check.DirectoryFiles(fixture.Root, target, backup, recovery!);
            }
            else
            {
                var error = Check.Failure<PublicationFailure>(() => Commit(source, target, backup, FailPublication),
                    "回滚成功仍报原始发布失败，不能改判保存成功");
                Check.That(ReferenceEquals(publicationFailure, error), "原发布失败保持");
                Check.Bytes(original, FileIo.ReadComplete(target), "恢复完整原主文件");
                Check.That(!File.Exists(recovery!), "回滚消费前主文件临时名");
                Check.DirectoryFiles(fixture.Root, target, backup);
            }
            Check.Equal(1, publicationCalls, "不重试发布或吞错");
            Check.That(!File.Exists(source), "前向提交消费完整新文件");
            Check.Preserved(backupBefore, backup, "成功回滚/失败回滚都不提前改旧备份");
            if (oldReader is not null)
            {
                Check.That(!oldReader.SafeFileHandle.IsClosed, "不关闭外部旧reader取得成功");
                oldReader.Position = 0;
                Check.Bytes(original, FileIo.ReadRemainder(oldReader), "旧reader始终读同一原对象完整数据");
            }
            fixture.Fact("publicationCalls", publicationCalls);
            fixture.Fact("rollbackDeniedByRealHandle", denyRollback);
            fixture.Fact("oldReaderRetained", keepOldReader);
            fixture.Fact("recoveryPath", recovery);
        }
        finally
        {
            rollbackLock?.Dispose();
            oldReader?.Dispose();
        }
    }
}

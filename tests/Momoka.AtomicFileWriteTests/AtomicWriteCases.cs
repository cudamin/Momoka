using System.Text;
using Momoka.Infrastructure;

namespace Momoka.AtomicFileWriteTests;

internal static class AtomicWriteCases
{
    public static IEnumerable<TestCase> All()
    {
        foreach (var bytes in new[] { false, true })
        {
            var kind = bytes ? "bytes" : "text";
            yield return new($"shared-delete-{kind}", "1：共享 Delete 旧读句柄保留旧完整文件，新打开得到新完整文件",
                fixture => SharedReader(fixture, bytes, FileIo.SharedDelete, expectFailure: false));
            yield return new($"deny-delete-{kind}", "2：不共享 Delete 必须抛 public IO/access 错，原文件/属性/已有备份保留",
                fixture => SharedReader(fixture, bytes, FileShare.ReadWrite, expectFailure: true));
            yield return new($"readonly-{kind}", "3：ReadOnly 必须抛错，原内容/属性/已有备份保留",
                fixture => ReadOnly(fixture, bytes, backup: false));
            yield return new($"plain-create-overwrite-{kind}", "5：无备份新建/覆盖、空内容、完整前后值和无临时残留",
                fixture => PlainWrites(fixture, bytes));
        }
        yield return new("shared-delete-text-with-backup", "1/5：File.Replace 备份路径共享 Delete reader 与完整前值",
            fixture => SharedReader(fixture, bytes: false, FileIo.SharedDelete, expectFailure: false, backup: true));
        yield return new("deny-delete-text-with-backup", "2：备份路径也不放宽不共享 Delete 的约束",
            fixture => SharedReader(fixture, bytes: false, FileShare.ReadWrite, expectFailure: true, backup: true));
        yield return new("readonly-text-with-backup", "3：File.Replace 失败原内容/属性/已有备份均保留",
            fixture => ReadOnly(fixture, bytes: false, backup: true));
        yield return new("backup-create-no-previous", "5：带备份新建不伪造前值，随后覆盖备份精确是前主文件",
            fixture => BackupWrites(fixture, existingBackup: false));
        yield return new("backup-create-existing-backup", "5：带备份新建保留已有备份，覆盖时更新为上个完整前值",
            fixture => BackupWrites(fixture, existingBackup: true));
        yield return new("source-target-attributes", "源临时文件和目标/备份的真实属性、无重解析、失败保留和清理",
            SourceAttributes);
        yield return new("malformed-text-existing", "4：严格 UTF-8 坏正文编码失败，原文/属性/备份保留并清理临时",
            fixture => BadText(fixture, existing: true));
        yield return new("malformed-text-new", "4：新建坏编码失败不留下目标或临时，已有备份保持",
            fixture => BadText(fixture, existing: false));
        yield return new("body-failure-text", "4：实际 Write 正文写出部分文本后抛错，清理临时且保留原件/备份",
            fixture => BodyFailure(fixture, bytes: false));
        yield return new("body-failure-bytes", "4：实际 Write 正文写出部分字节后抛错，清理临时且保留原件/备份",
            fixture => BodyFailure(fixture, bytes: true));
        yield return new("long-unicode-shared-delete", "长路径/Unicode：已有共享 Delete 读者下原生 extended path 正确替换",
            LongUnicode);
        yield return new("device-dot-prefix-shared-delete", @"设备前缀 \\.\：真实 public 文本入口共享 Delete reader，不能转换成 UNC",
            fixture => DevicePrefix(fixture, @"\\.\", bytes: false));
        yield return new("device-nt-prefix-shared-delete", @"设备前缀 \??\：真实 public 字节入口共享 Delete reader，不能转换成 UNC",
            fixture => DevicePrefix(fixture, @"\??\", bytes: true));
        yield return new("surrogate-high-dir-low-file", "孤立 surrogate：原始 UTF-16 目录/文件名不被编码成 U+FFFD 别名",
            fixture => SurrogatePath(fixture, '\ud800', '\udc00', bytes: false));
        yield return new("surrogate-low-dir-high-file", "孤立 surrogate：反向目录/文件名与字节写入，别名哨兵不受影响",
            fixture => SurrogatePath(fixture, '\udc00', '\ud800', bytes: true));
    }

    private static void Write(string path, string text, bool bytes, string? backup = null)
    {
        if (bytes)
        {
            Check.That(backup is null, "WriteAllBytes 没有备份参数，不伪造生产 API");
            AtomicFile.WriteAllBytes(path, Documents.Utf8.GetBytes(text));
        }
        else AtomicFile.WriteAllText(path, text, Documents.Utf8, backup);
    }

    private static void SharedReader(TestFixture fixture, bool bytes, FileShare share,
        bool expectFailure, bool backup = false)
    {
        var target = fixture.FilePath("anonymous.bin");
        var backupPath = fixture.FilePath("anonymous.backup");
        var original = Documents.Utf8.GetBytes(Documents.Text(0, 71_003));
        var previousBackup = Documents.Utf8.GetBytes(Documents.Text(-1, 11_111));
        var replacement = Documents.Text(1, 83_027);
        File.WriteAllBytes(target, original);
        File.WriteAllBytes(backupPath, previousBackup);
        var targetBefore = FileState.Capture(target);
        var backupBefore = FileState.Capture(backupPath);
        fixture.Fact("share", share.ToString());
        fixture.Snapshot("targetBefore", target);
        fixture.Snapshot("backupBefore", backupPath);
        using var reader = FileIo.OpenReader(target, share);
        // Leave the reader open at a nonzero offset across the write, not just open and close it.
        var prefix = new byte[137];
        reader.ReadExactly(prefix);
        Check.Bytes(original[..137], prefix, "前件：旧 reader 开始于原文");
        Check.That(!reader.SafeFileHandle.IsClosed, "前件：提交时旧 reader 必须仍打开");
        if (expectFailure)
        {
            var error = Check.IoFailure(() => Write(target, replacement, bytes, backup ? backupPath : null),
                "不允许 Delete 的 reader 必须阻止替换");
            fixture.Fact("rejection", new { type = error.GetType().FullName, hresult = $"0x{error.HResult:X8}" });
            Check.Preserved(targetBefore, target, "拒绝后原目标");
            Check.Preserved(backupBefore, backupPath, "拒绝后已有备份");
        }
        else
        {
            Write(target, replacement, bytes, backup ? backupPath : null);
            Check.Bytes(Documents.Utf8.GetBytes(replacement), FileIo.ReadComplete(target), "新打开必须读到新完整文档");
            if (backup) Check.Bytes(original, FileIo.ReadComplete(backupPath), "备份必须是原完整目标");
            else Check.Preserved(backupBefore, backupPath, "无备份 API 不碰已有旁路备份");
        }
        Check.That(!reader.SafeFileHandle.IsClosed, "后件：测试不能靠关闭旧 reader 使写入通过");
        Check.Bytes(original, FileIo.ReadRemainder(reader, prefix), "跨提交读完仍是原完整文档");
        reader.Position = 0;
        Check.Bytes(original, FileIo.ReadRemainder(reader), "旧 reader 重新定位后仍完整原文");
        fixture.Snapshot("targetAfter", target);
        fixture.Snapshot("backupAfter", backupPath);
        Check.DirectoryFiles(fixture.Root, target, backupPath);
    }

    private static void ReadOnly(TestFixture fixture, bool bytes, bool backup)
    {
        var target = fixture.FilePath("anonymous.bin");
        var backupPath = fixture.FilePath("anonymous.backup");
        File.WriteAllBytes(target, Documents.Utf8.GetBytes(Documents.Text(0, 70_013)));
        File.WriteAllBytes(backupPath, Documents.Utf8.GetBytes(Documents.Text(-1, 11_137)));
        var originalAttributes = File.GetAttributes(target);
        File.SetAttributes(target, originalAttributes | FileAttributes.ReadOnly);
        try
        {
            Check.That(File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly), "前件：ReadOnly 实际生效");
            var targetBefore = FileState.Capture(target);
            var backupBefore = FileState.Capture(backupPath);
            fixture.Fact("targetBefore", targetBefore);
            fixture.Fact("backupBefore", backupBefore);
            var error = Check.IoFailure(() => Write(target, Documents.Text(1, 84_019), bytes, backup ? backupPath : null),
                "ReadOnly 目标不能被覆盖或绕过属性");
            fixture.Fact("rejection", new { type = error.GetType().FullName, hresult = $"0x{error.HResult:X8}" });
            Check.Preserved(targetBefore, target, "ReadOnly 失败后原目标");
            Check.Preserved(backupBefore, backupPath, "ReadOnly 失败后已有备份");
            Check.DirectoryFiles(fixture.Root, target, backupPath);
            fixture.Snapshot("targetAfter", target);
            fixture.Snapshot("backupAfter", backupPath);
        }
        finally { File.SetAttributes(target, originalAttributes); }
    }

    private static void PlainWrites(TestFixture fixture, bool bytes)
    {
        var directory = Path.Combine(fixture.Root, "not-created-yet", "nested");
        var target = Path.Combine(directory, "anonymous.bin");
        Check.That(!Directory.Exists(directory), "前件：生产方法负责创建目录");
        var documents = new[] { Documents.Text(0, 77_019), Documents.Text(1, 113_003), "", Documents.Text(2, 3) };
        foreach (var text in documents)
        {
            Write(target, text, bytes);
            Check.Bytes(Documents.Utf8.GetBytes(text), FileIo.ReadComplete(target), "新建/覆盖/空文档的完整内容");
            Check.DirectoryFiles(fixture.Root, target);
        }
        fixture.Fact("successfulWrites", documents.Length);
        fixture.Snapshot("final", target);
    }

    private static void BackupWrites(TestFixture fixture, bool existingBackup)
    {
        var target = fixture.FilePath("anonymous.bin");
        var backup = fixture.FilePath("anonymous.backup");
        if (existingBackup) File.WriteAllBytes(backup, Documents.Utf8.GetBytes(Documents.Text(-1, 4099)));
        var backupBefore = FileState.Capture(backup);
        var encoding = new UTF8Encoding(true, true);
        var documents = new[] { Documents.Text(0, 65_539), Documents.Text(1, 131_101), "", Documents.Text(2, 7) };
        byte[]? previous = null;
        for (var index = 0; index < documents.Length; index++)
        {
            var expected = Documents.Encoded(documents[index], encoding);
            AtomicFile.WriteAllText(target, documents[index], encoding, backup);
            Check.Bytes(expected, FileIo.ReadComplete(target), "带备份主文件包括正确 BOM/完整正文");
            if (previous is null) Check.Preserved(backupBefore, backup, "新建时不存在前主值，备份不得伪造或被触碰");
            else Check.Bytes(previous, FileIo.ReadComplete(backup), "备份逐次精确等于上一个完整前主值");
            Check.DirectoryFiles(fixture.Root, previous is not null || existingBackup ? [target, backup] : [target]);
            previous = expected;
        }
        fixture.Fact("successfulWrites", documents.Length);
        fixture.Snapshot("primary", target);
        fixture.Snapshot("backup", backup);
    }

    private static void SourceAttributes(TestFixture fixture)
    {
        var target = fixture.FilePath("anonymous.bin");
        var backup = fixture.FilePath("anonymous.backup");
        var original = Documents.Utf8.GetBytes(Documents.Text(0, 65_539));
        var replacement = Documents.Utf8.GetBytes(Documents.Text(1, 79_009));
        File.WriteAllBytes(target, original);
        File.WriteAllText(backup, "anonymous-old-backup", Documents.Utf8);
        var targetBefore = FileState.Capture(target);
        var backupBefore = FileState.Capture(backup);
        string? observed = null;
        var sourceWasComplete = false;
        var failure = new InjectedBodyFailure();
        try
        {
            var caught = Check.Failure<InjectedBodyFailure>(() => ProductionBody.Invoke(target, temporary =>
            {
                observed = temporary;
                File.WriteAllBytes(temporary, replacement);
                var source = FileState.Capture(temporary);
                fixture.Fact("sourceBeforeCommit", source);
                fixture.Fact("sourceSibling", Path.GetDirectoryName(temporary) == Path.GetDirectoryName(target));
                Check.That(source.Exists && source.Length == replacement.Length && source.Sha256 == Check.Hash(replacement),
                    "前件：源临时文件是实际完整已写正文");
                Check.That((source.Attributes!.Value & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0,
                    "前件：源实际是非 ReadOnly/重解析/目录的普通文件");
                Check.That(Path.GetDirectoryName(temporary) == Path.GetDirectoryName(target), "源必须是目标的同目录真实文件");
                sourceWasComplete = true;
                throw failure;
            }, backup), "正文故障用于观测真实 source 属性与 finally 清理");
            Check.That(ReferenceEquals(failure, caught), "故障必须原样传播，不可重新包装/吞掉");
        }
        finally
        {
            fixture.Fact("sourceObserved", observed is not null);
            fixture.Fact("sourceCleaned", observed is not null && !File.Exists(observed));
        }
        Check.That(sourceWasComplete && observed is not null, "实际完整源已被观察");
        Check.That(!File.Exists(observed), "正文失败后实际临时 source 被清理");
        Check.Preserved(targetBefore, target, "正文失败后目标");
        Check.Preserved(backupBefore, backup, "正文失败后备份");
        fixture.Fact("targetBefore", targetBefore);
        fixture.Fact("backupBefore", backupBefore);
        Check.DirectoryFiles(fixture.Root, target, backup);
    }

    private static void BadText(TestFixture fixture, bool existing)
    {
        var target = fixture.FilePath("anonymous.bin");
        var backup = fixture.FilePath("anonymous.backup");
        if (existing) File.WriteAllBytes(target, Documents.Utf8.GetBytes(Documents.Text(0, 65_539)));
        File.WriteAllText(backup, "anonymous-old-backup", Documents.Utf8);
        var targetBefore = FileState.Capture(target);
        var backupBefore = FileState.Capture(backup);
        var badText = new string('x', 131_079) + "\ud800";
        Check.Failure<EncoderFallbackException>(() => AtomicFile.WriteAllText(target, badText, Documents.Utf8, backup),
            "严格编码的大段正文最后含孤立 surrogate 必须失败");
        Check.Preserved(targetBefore, target, "编码失败后目标");
        Check.Preserved(backupBefore, backup, "编码失败后备份");
        Check.DirectoryFiles(fixture.Root, existing ? [target, backup] : [backup]);
        fixture.Fact("targetBefore", targetBefore);
        fixture.Fact("backupBefore", backupBefore);
        fixture.Fact("invalidUtf16Suffix", "D800");
    }

    private static void BodyFailure(TestFixture fixture, bool bytes)
    {
        var target = fixture.FilePath("anonymous.bin");
        var backup = fixture.FilePath("anonymous.backup");
        File.WriteAllText(target, "original-anonymous-complete", Documents.Utf8);
        File.WriteAllText(backup, "previous-anonymous-complete", Documents.Utf8);
        var targetBefore = FileState.Capture(target);
        var backupBefore = FileState.Capture(backup);
        string? observed = null;
        var sourceExisted = false;
        var injected = new InjectedBodyFailure();
        var caught = Check.Failure<InjectedBodyFailure>(() => ProductionBody.Invoke(target, temporary =>
        {
            observed = temporary;
            using (var source = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                var partial = bytes ? new byte[] { 0, 255, 1, 254, 17 } : Documents.Utf8.GetBytes("partial-anonymous-正文");
                source.Write(partial);
                source.Flush();
            }
            sourceExisted = File.Exists(temporary) && new FileInfo(temporary).Length > 0;
            fixture.Snapshot("partialSourceBeforeFailure", temporary);
            throw injected;
        }, backup), "实际生产 Write 的非编码正文异常");
        Check.That(ReferenceEquals(injected, caught), "正文异常必须原样传播");
        Check.That(sourceExisted && observed is not null, "前件：已真实写出部分临时文件，而非写前异常");
        Check.That(!File.Exists(observed), "非编码正文异常的部分 source 必须清理");
        Check.Preserved(targetBefore, target, "正文失败后目标");
        Check.Preserved(backupBefore, backup, "正文失败后备份");
        Check.DirectoryFiles(fixture.Root, target, backup);
        fixture.Fact("targetBefore", targetBefore);
        fixture.Fact("backupBefore", backupBefore);
        fixture.Fact("sourceCleaned", true);
    }

    private static void LongUnicode(TestFixture fixture)
    {
        var directory = fixture.Root;
        for (var index = 0; index < 5; index++) directory = Path.Combine(directory, $"匿名-Ω-é-🎬-{index}-" + new string('x', 43));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "完整-文件-é-Ω-🎬.json");
        Check.That(target.Length > 320, "前件：普通绝对路径超过 MAX_PATH（source 临时路径更长）");
        fixture.Fact("targetUtf16Length", target.Length);
        PathWithReader(fixture, target, bytes: false);
    }

    private static void DevicePrefix(TestFixture fixture, string prefix, bool bytes)
    {
        // \\.\ is a DOS-device prefix, NOT the long-path \\?\ prefix. Its completed source
        // suffix must stay below MAX_PATH so this tests rename/helper semantics, not WriteAllText
        // failing before commit. Keep every fixture under the same ignored evidence batch.
        var evidenceRoot = Directory.GetParent(Directory.GetParent(fixture.Root)!.FullName)!.FullName;
        var directory = Path.Combine(evidenceRoot, prefix == @"\\.\" ? "d" : "n");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "文件-Ω-é-🎬.bin");
        var devicePath = prefix + target;
        var original = Documents.Utf8.GetBytes(Documents.Text(0, 70_003));
        var replacement = Documents.Text(1, 93_011);
        File.WriteAllBytes(target, original);
        fixture.Fact("devicePrefix", prefix);
        fixture.Fact("devicePathUtf16Length", devicePath.Length);
        fixture.Fact("completedSourceUtf16Length", devicePath.Length + 37);
        Check.That(devicePath.Length + 37 < 260,
            "设备前缀 source（含 .GUID.tmp 37字符）必须短于 MAX_PATH，否则尚未进入 rename");
        var bodyProbe = prefix + Path.Combine(directory, "body-control.bin");
        File.WriteAllBytes(bodyProbe, original);
        Check.Bytes(original, FileIo.ReadComplete(bodyProbe), "前件：设备前缀真实正文写入/读取链路可用");
        File.Delete(bodyProbe);
        fixture.Fact("normalizedPrefixPreserved", Path.GetFullPath(devicePath).StartsWith(prefix, StringComparison.Ordinal));
        Check.That(Path.GetFullPath(devicePath).StartsWith(prefix, StringComparison.Ordinal),
            "前件：实际 public Write 接收规范化后仍保留的设备前缀，不仅测试 helper");
        using var reader = FileIo.OpenReader(target);
        Write(devicePath, replacement, bytes);
        Check.That(!reader.SafeFileHandle.IsClosed, "设备路径提交不能靠关旧句柄");
        Check.Bytes(original, FileIo.ReadRemainder(reader), "设备前缀旧 reader 保留原完整文件");
        Check.Bytes(Documents.Utf8.GetBytes(replacement), FileIo.ReadComplete(target), "设备前缀真正匿名目标完整新值");
        Check.DirectoryFiles(directory, target);
        fixture.Snapshot("targetAfter", target);
    }
    private static void SurrogatePath(TestFixture fixture, char directorySurrogate, char fileSurrogate, bool bytes)
    {
        var directory = Path.Combine(fixture.Root, "directory-" + directorySurrogate);
        var aliasDirectory = Path.Combine(fixture.Root, "directory-\ufffd");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(aliasDirectory);
        var target = Path.Combine(directory, "file-" + fileSurrogate + ".bin");
        // All possible UTF-16 replacement aliases, including replacement of only one component,
        // remain independently populated sentinels, so a mistaken successful write is detectable.
        var aliases = new[]
        {
            Path.Combine(directory, "file-\ufffd.bin"),
            Path.Combine(aliasDirectory, "file-" + fileSurrogate + ".bin"),
            Path.Combine(aliasDirectory, "file-\ufffd.bin")
        };
        foreach (var alias in aliases) File.WriteAllText(alias, "untouched-anonymous-alias-" + Check.Hash(Documents.Utf8.GetBytes("alias")), Documents.Utf8);
        var states = aliases.Select(FileState.Capture).ToArray();
        fixture.Fact("directoryCodeUnits", ((ushort)directorySurrogate).ToString("X4"));
        fixture.Fact("fileCodeUnits", ((ushort)fileSurrogate).ToString("X4"));
        fixture.Fact("replacementAliasCodeUnit", "FFFD");
        Check.That(Directory.Exists(directory) && Directory.Exists(aliasDirectory) && directory != aliasDirectory,
            "前件：Windows 上真实建立两个不同的 UTF-16 目录");
        PathWithReader(fixture, target, bytes, aliases);
        for (var index = 0; index < aliases.Length; index++) Check.Preserved(states[index], aliases[index], "编码别名哨兵");
        fixture.Fact("untouchedAliasCount", aliases.Length);
        Check.DirectoryFiles(fixture.Root, [target, .. aliases]);
    }

    private static void PathWithReader(TestFixture fixture, string target, bool bytes, string[]? extraFiles = null)
    {
        var original = Documents.Utf8.GetBytes(Documents.Text(0, 70_003));
        var replacement = Documents.Text(1, 97_033);
        File.WriteAllBytes(target, original);
        using var reader = FileIo.OpenReader(target);
        fixture.Snapshot("targetBefore", target);
        Write(target, replacement, bytes);
        Check.That(!reader.SafeFileHandle.IsClosed, "提交后仍保留旧句柄");
        Check.Bytes(original, FileIo.ReadRemainder(reader), "特殊路径旧句柄保留完整原文");
        Check.Bytes(Documents.Utf8.GetBytes(replacement), FileIo.ReadComplete(target), "特殊路径真正目标是新完整正文");
        fixture.Snapshot("targetAfter", target);
        Check.DirectoryFiles(fixture.Root, [target, .. extraFiles ?? []]);
    }
}

internal static class BackupFailureCases
{
    public static IEnumerable<TestCase> All()
    {
        yield return new("backup-readonly-rejection-preserves-both",
            "备份 ReadOnly：public 写入必须抛错，primary/旧 backup 字节属性保留，无提交前备份改动或临时残留",
            fixture => DeniedBackup(fixture, readOnly: true));
        yield return new("backup-deny-delete-rejection-preserves-both",
            "备份旧 reader 不共享 Delete：public 写入必须抛错，primary/旧 backup 字节属性保留且 reader 未关闭",
            fixture => DeniedBackup(fixture, readOnly: false));
    }

    private static void DeniedBackup(TestFixture fixture, bool readOnly)
    {
        var target = fixture.FilePath("anonymous-primary.bin");
        var backup = fixture.FilePath("anonymous-backup.bin");
        var original = Documents.Utf8.GetBytes(Documents.Text(0, 70_011));
        var previousBackup = Documents.Utf8.GetBytes(Documents.Text(-1, 61_007));
        var replacement = Documents.Text(1, 93_031);
        File.WriteAllBytes(target, original);
        File.WriteAllBytes(backup, previousBackup);
        var originalBackupAttributes = File.GetAttributes(backup);
        Check.That((File.GetAttributes(target) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == 0,
            "前件：primary 是可写普通文件，不能用 primary 拒绝冒充 backup 拒绝");
        Check.That((originalBackupAttributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) == 0,
            "前件：匿名旧 backup 原本是可写普通文件");
        if (readOnly) File.SetAttributes(backup, originalBackupAttributes | FileAttributes.ReadOnly);
        try
        {
            Check.Equal(readOnly, File.GetAttributes(backup).HasFlag(FileAttributes.ReadOnly),
                "前件：实际读回 backup ReadOnly 状态");
            var backupShare = readOnly ? FileIo.SharedDelete : FileShare.ReadWrite;
            var targetBefore = FileState.Capture(target);
            var backupBefore = FileState.Capture(backup);
            fixture.Fact("targetBefore", targetBefore);
            fixture.Fact("backupBefore", backupBefore);
            fixture.Fact("primaryReaderShare", FileIo.SharedDelete.ToString());
            fixture.Fact("backupReaderShare", backupShare.ToString());
            fixture.Fact("backupReadOnly", readOnly);
            fixture.Fact("retryBudget", 0);
            using var primaryReader = FileIo.OpenReader(target);
            using var backupReader = FileIo.OpenReader(backup, backupShare);
            var primaryPrefix = new byte[137];
            var backupPrefix = new byte[113];
            primaryReader.ReadExactly(primaryPrefix);
            backupReader.ReadExactly(backupPrefix);
            Check.Bytes(original[..primaryPrefix.Length], primaryPrefix, "前件：primary reader 原文前缀");
            Check.Bytes(previousBackup[..backupPrefix.Length], backupPrefix, "前件：backup reader 原文前缀");
            Check.That(!primaryReader.SafeFileHandle.IsClosed && !backupReader.SafeFileHandle.IsClosed,
                "前件：两个真实 reader 在 public 写入时都保持打开");
            var returnedSuccessfully = false;
            try
            {
                var error = Check.IoFailure(() =>
                {
                    // Always exercise the real PUBLIC entry with a writable primary and a blocked
                    // old backup. No helper calls, backup pre-deletion, test commit lock or retries.
                    AtomicFile.WriteAllText(target, replacement, Documents.Utf8, backup);
                    returnedSuccessfully = true;
                }, readOnly ? "ReadOnly backup 必须阻止提交" : "backup reader 未共享 Delete 必须阻止提交");
                fixture.Fact("rejection", new { type = error.GetType().FullName, hresult = $"0x{error.HResult:X8}" });
                Check.Bytes(original, FileIo.ReadComplete(target), "失败后按名字重新打开 primary 字节仍是原完整值");
                Check.Bytes(previousBackup, FileIo.ReadComplete(backup), "失败后旧 backup 字节必须原样，不得提交前改写/删除");
                Check.Preserved(targetBefore, target, "backup 拒绝后原 primary");
                Check.Preserved(backupBefore, backup, "backup 拒绝后旧 backup（含 ReadOnly 属性）");
                Check.That(!primaryReader.SafeFileHandle.IsClosed && !backupReader.SafeFileHandle.IsClosed,
                    "不能靠关闭旧 reader 或更改共享权限取得失败原子契约");
                Check.Bytes(original, FileIo.ReadRemainder(primaryReader, primaryPrefix), "旧 primary reader 仍完整原文");
                Check.Bytes(previousBackup, FileIo.ReadRemainder(backupReader, backupPrefix), "旧 backup reader 仍完整原文");
                Check.DirectoryFiles(fixture.Root, target, backup);
            }
            finally
            {
                // Capture before releasing reader/ReadOnly preconditions, including any real
                // failure of a preservation assertion. Keep the first failure, never rerun it.
                fixture.Fact("publicWriteReturnedSuccessfully", returnedSuccessfully);
                fixture.Snapshot("targetAfter", target);
                fixture.Snapshot("backupAfter", backup);
                fixture.Fact("remainingFiles", Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
            }
        }
        finally
        {
            // Restore only this case's injected anonymous attribute, after all observations.
            if (readOnly && File.Exists(backup)) File.SetAttributes(backup, originalBackupAttributes);
        }
    }
}

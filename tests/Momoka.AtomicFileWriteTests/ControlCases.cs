namespace Momoka.AtomicFileWriteTests;

internal static class ControlCases
{
    // These are independent oracle/OS calibration controls, NEVER product pass counts and
    // NEVER an alternative AtomicFile implementation. No Windows rename P/Invoke lives here.
    public static IEnumerable<TestCase> All()
    {
        yield return new("oracle-complete-rejects-corruption", "正确/错误对照：完整文档通过，半份/混拼/单字节坏文档必被断言拒绝", WholeDocument);
        yield return new("oracle-preservation-temp-leak", "正确/错误对照：原件保留通过，内容/属性破坏与临时泄漏必被拒绝", Preservation);
        yield return new("kernel-shared-delete-control", "真实 File.Replace 校准：共享 Delete 时旧句柄/新目标完整语义可实现", SharedDelete);
        yield return new("kernel-deny-delete-readonly-control", "真实 File.Replace/File.Move 校准：无 Delete 共享及 ReadOnly 拒绝生效", Rejections);
    }

    private static void WholeDocument(TestFixture fixture)
    {
        var path = fixture.FilePath("anonymous.bin");
        var first = Documents.Utf8.GetBytes(Documents.Text(0, 65_536));
        var second = Documents.Utf8.GetBytes(Documents.Text(1, 65_536));
        Check.Equal(first.Length, second.Length, "混拼对照使用等长完整候选");
        File.WriteAllBytes(path, first);
        Check.CompleteCandidate([first, second], FileIo.ReadComplete(path), "完整正确文档");
        var spliced = first.ToArray();
        second.AsSpan(second.Length / 2).CopyTo(spliced.AsSpan(first.Length / 2));
        var flipped = first.ToArray();
        flipped[flipped.Length / 2] ^= 1;
        foreach (var wrong in new[] { first[..(first.Length / 2)], spliced, flipped })
        {
            File.WriteAllBytes(path, wrong);
            Check.Failure<ContractFailure>(() => Check.CompleteCandidate([first, second], FileIo.ReadComplete(path), "坏对照"),
                "oracle 必须拒绝错误文档而非放宽阈值");
        }
        fixture.Fact("acceptedCorrect", 1);
        fixture.Fact("rejectedWrong", 3);
    }

    private static void Preservation(TestFixture fixture)
    {
        var path = fixture.FilePath("anonymous.bin");
        File.WriteAllText(path, "complete-anonymous-original", Documents.Utf8);
        var before = FileState.Capture(path);
        Check.Preserved(before, path, "正确保留");
        File.WriteAllText(path, "wrong-anonymous-value", Documents.Utf8);
        Check.Failure<ContractFailure>(() => Check.Preserved(before, path, "内容破坏"), "内容破坏必须被断言拒绝");
        File.WriteAllText(path, "complete-anonymous-original", Documents.Utf8);
        var attributes = File.GetAttributes(path);
        try
        {
            File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            Check.Failure<ContractFailure>(() => Check.Preserved(before, path, "属性破坏"), "属性破坏必须被断言拒绝");
        }
        finally { File.SetAttributes(path, attributes); }
        Check.DirectoryFiles(fixture.Root, path);
        var leaked = fixture.FilePath("leaked-anonymous.tmp");
        File.WriteAllText(leaked, "partial", Documents.Utf8);
        Check.Failure<ContractFailure>(() => Check.DirectoryFiles(fixture.Root, path), "临时泄漏必须被断言拒绝");
        File.Delete(leaked);
        Check.DirectoryFiles(fixture.Root, path);
        fixture.Fact("acceptedCorrect", 2);
        fixture.Fact("rejectedWrong", 3);
    }

    private static void SharedDelete(TestFixture fixture)
    {
        var path = fixture.FilePath("anonymous.bin");
        var source = fixture.FilePath("completed-source.bin");
        var original = Documents.Utf8.GetBytes(Documents.Text(0, 70_013));
        var replacement = Documents.Utf8.GetBytes(Documents.Text(1, 83_029));
        File.WriteAllBytes(path, original);
        File.WriteAllBytes(source, replacement);
        using var reader = FileIo.OpenReader(path);
        File.Replace(source, path, destinationBackupFileName: null);
        Check.Bytes(original, FileIo.ReadRemainder(reader), "内核正确对照旧句柄");
        Check.Bytes(replacement, FileIo.ReadComplete(path), "内核正确对照新句柄");
        Check.DirectoryFiles(fixture.Root, path);
        fixture.Fact("kernelCalibration", "File.Replace with real Delete-shared open reader");
    }

    private static void Rejections(TestFixture fixture)
    {
        var target = fixture.FilePath("anonymous.bin");
        var source = fixture.FilePath("completed-source.bin");
        var backup = fixture.FilePath("anonymous.backup");
        File.WriteAllText(target, "original-complete", Documents.Utf8);
        File.WriteAllText(source, "new-complete", Documents.Utf8);
        File.WriteAllText(backup, "previous-complete", Documents.Utf8);
        var targetBefore = FileState.Capture(target);
        var backupBefore = FileState.Capture(backup);
        using (var reader = FileIo.OpenReader(target, FileShare.ReadWrite))
        {
            var error = Check.IoFailure(() => File.Replace(source, target, backup), "内核不共享 Delete 的拒绝对照");
            fixture.Fact("denyDelete", new { type = error.GetType().FullName, hresult = $"0x{error.HResult:X8}" });
            Check.Preserved(targetBefore, target, "内核拒绝对照原件");
            Check.Preserved(backupBefore, backup, "内核拒绝对照备份");
        }
        var attributes = File.GetAttributes(target);
        File.SetAttributes(target, attributes | FileAttributes.ReadOnly);
        try
        {
            var readOnlyBefore = FileState.Capture(target);
            var error = Check.IoFailure(() => File.Move(source, target, overwrite: true), "内核 ReadOnly 拒绝对照");
            fixture.Fact("readOnly", new { type = error.GetType().FullName, hresult = $"0x{error.HResult:X8}" });
            Check.Preserved(readOnlyBefore, target, "内核 ReadOnly 拒绝后原件");
            Check.Preserved(backupBefore, backup, "内核 ReadOnly 拒绝后备份");
        }
        finally { File.SetAttributes(target, attributes); }
        Check.DirectoryFiles(fixture.Root, target, source, backup);
    }
}

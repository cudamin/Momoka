using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momoka.Infrastructure;

/// <summary>先在同目录写完整临时文件，再替换目标；可将被替换的原件同时保留为备份。</summary>
public static class AtomicFile
{
    private static readonly object CommitGate = new();

    public static void WriteAllText(string path, string contents, System.Text.Encoding encoding, string? backupPath = null) =>
        Write(path, temporary => File.WriteAllText(temporary, contents, encoding), backupPath);

    public static void WriteAllBytes(string path, byte[] contents) =>
        Write(path, temporary => File.WriteAllBytes(temporary, contents), null);

    private static void Write(string path, Action<string> write, string? backupPath)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            write(temporary);
            lock (CommitGate)
            {
                if (backupPath is not null && File.Exists(path))
                    ReplaceWithBackup(temporary, path, Path.GetFullPath(backupPath));
                else
                    MoveCompletedFile(temporary, path);
            }
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void ReplaceWithBackup(string source, string destination, string backup)
    {
        try
        {
            File.Replace(source, destination, backup);
        }
        catch (IOException error) when (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
            && error.HResult == unchecked((int)0x80070497) && File.Exists(backup))
        {
            // ReplaceFile deletes an existing backup with legacy semantics. A reader retained from
            // an earlier save can therefore block the next save even though it shares Delete. Keep
            // the old backup untouched until the primary commit succeeds, then publish its complete
            // predecessor with the same reader-safe rename used for files without a backup.
            using var backupLease = OpenForRename(backup);
            var attributes = File.GetAttributes(backup);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException("备份文件为只读，未替换主文件");
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("备份必须是普通文件，未替换主文件");

            // Requesting DELETE before committing the primary also rejects existing readers that
            // deny Delete sharing and stops new such readers from racing the backup publication.
            CommitWithIsolatedBackup(source, destination, backup, MoveCompletedFile);
        }
    }

    private static void CommitWithIsolatedBackup(string source, string destination, string backup,
        Action<string, string> publishBackup)
    {
        var predecessor = backup + $".{Guid.NewGuid():N}.tmp";
        File.Replace(source, destination, predecessor);
        try
        {
            publishBackup(predecessor, backup);
        }
        catch (Exception publicationError)
        {
            try
            {
                // The predecessor may still have Delete-shared readers. ReplaceFile would reopen
                // it without sharing and reject an otherwise valid rollback; rename restores the
                // original file object (including its metadata) without reopening it exclusively.
                MoveCompletedFile(predecessor, destination);
            }
            catch (Exception rollbackError)
            {
                // A real lock/permission failure must not delete the complete recovery file.
                var failure = new AggregateException("备份发布失败且主文件回滚失败；保留完整前值以供恢复",
                    publicationError, rollbackError);
                failure.Data["RecoveryPath"] = predecessor;
                throw failure;
            }
            throw;
        }
    }

    private static void MoveCompletedFile(string source, string destination)
    {
        try
        {
            File.Move(source, destination, overwrite: true);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            // MoveFileEx can reject replacement even when every reader shares Delete. Use Windows'
            // handle-based POSIX rename in that case: existing readers retain the old complete file,
            // while new opens see the completed replacement. This is a different commit operation,
            // not a delay/retry, and does not ignore ReadOnly, ACLs or readers denying Delete sharing.
            RenameWithSharedReaders(source, destination);
        }
    }

    private static void RenameWithSharedReaders(string source, string destination)
    {
        const int FileRenameInfoEx = 22;
        const int ReplaceIfExistsAndPosixSemantics = 0x00000003;

        using var handle = OpenForRename(source);

        // FILE_RENAME_INFO has pointer-aligned RootDirectory followed by a byte count and UTF-16
        // filename. Preserve raw UTF-16 code units, including valid Windows names with lone surrogates.
        // Compute offsets for both x86 and x64 rather than assuming the process bitness.
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(int);
        var name = ExtendedWindowsPath(destination).ToCharArray();
        var nameBytes = checked(name.Length * sizeof(char));
        var size = checked(nameOffset + nameBytes + sizeof(char));
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size);
            Marshal.WriteInt32(buffer, ReplaceIfExistsAndPosixSemantics);
            Marshal.WriteIntPtr(buffer, rootOffset, IntPtr.Zero);
            Marshal.WriteInt32(buffer, lengthOffset, nameBytes);
            Marshal.Copy(name, 0, IntPtr.Add(buffer, nameOffset), name.Length);
            if (!SetFileInformationByHandle(handle, FileRenameInfoEx, buffer, (uint)size))
                ThrowCommitError();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle OpenForRename(string path)
    {
        const uint DeleteAccess = 0x00010000;
        const uint ShareReadWriteDelete = 0x00000007;
        const uint OpenExisting = 3;
        const uint OpenReparsePoint = 0x00200000;
        var handle = CreateFile(ExtendedWindowsPath(path), DeleteAccess, ShareReadWriteDelete,
            IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            ThrowCommitError(error);
        }
        return handle;
    }

    private static string ExtendedWindowsPath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith(@"\??\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
                : @"\\?\" + path;

    private static void ThrowCommitError() => ThrowCommitError(Marshal.GetLastPInvokeError());

    private static void ThrowCommitError(int error)
    {
        var message = new Win32Exception(error).Message;
        if (error == 5) throw new UnauthorizedAccessException(message);
        throw new IOException(message, unchecked((int)0x80070000) | error);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint disposition, uint attributes, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass,
        IntPtr information, uint size);

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

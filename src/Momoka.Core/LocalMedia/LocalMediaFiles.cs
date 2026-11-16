using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Momoka.LocalMedia;

/// <summary>
/// 保持读取对象和目录句柄；拒绝已检测的重解析路径，不宣称抵御恶意进程的原地重解析竞争。
/// 目录可以是本机盘符或 UNC 共享；网络共享不支持硬链接，由规划与监控入口提前拒绝。
/// </summary>
internal sealed class LocalMediaFiles : IDisposable
{
    private const string PathChoice = "请选择 Windows 绝对路径：本机盘符（如 C:\\媒体）或网络共享（如 \\\\服务器\\共享\\媒体）；不能使用设备路径、相对路径或上级跳转";
    private readonly List<IDisposable> _leases = [];
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    internal static string FullPath(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("本机整理仅支持 Windows");
        if (!HasSupportedShape(path)) throw new IOException(PathChoice);
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new IOException(PathChoice, error); }
        if (!IsUnc(full) && new DriveInfo(Path.GetPathRoot(full)!).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network))
            throw new IOException("请选择本机磁盘或网络共享目录");
        return Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>UNC 共享或映射的网络盘。路径无效时返回 false，只用于能力判断，不代替 <see cref="FullPath"/> 校验。</summary>
    internal static bool IsNetwork(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsDevicePath(path)) return false;
        if (IsUnc(path)) return true;
        try
        {
            var full = Path.GetFullPath(path);
            if (IsUnc(full)) return true;
            var root = Path.GetPathRoot(full);
            return root is { Length: > 0 } && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; }
    }

    private static bool HasSupportedShape(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || IsDevicePath(path)) return false;
        var parts = path.Split(['\\', '/']);
        if (parts.Any(part => part is "." or "..")) return false;
        if (parts.Length > 2 && parts[0].Length == 0 && parts[1].Length == 0)
            return parts.Length >= 4 && parts[2].Length > 0 && parts[3].Length > 0
                && !parts.Skip(2).Any(part => part.Contains(':'));
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is ('\\' or '/')
            && !path[2..].Contains(':');
    }

    private static bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) && !IsDevicePath(path);
    private static bool IsDevicePath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
        || path.StartsWith(@"\??\", StringComparison.Ordinal);

    internal void Directories(string directory, bool create)
    {
        var root = Path.GetPathRoot(directory)!;
        var current = root;
        LockDirectory(current, create: false);
        foreach (var part in directory[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            LockDirectory(current, create);
        }
    }

    private void LockDirectory(string path, bool create)
    {
        if (_directories.Contains(path)) { RequirePlain(path, directory: true); return; }
        if (!Exists(path))
        {
            if (!create) return;
            Directory.CreateDirectory(path);
        }
        RequirePlain(path, directory: true);
        // Denying Delete prevents normal ancestor replacement. Attribute-only reparse writes are
        // not governed by sharing, so every publication separately rechecks the actual path.
        var handle = CreateFile(path, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastPInvokeError(); handle.Dispose(); Throw(error); }
        if (!GetFileInformationByHandle(handle, out var info)) { handle.Dispose(); Throw(Marshal.GetLastPInvokeError()); }
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) { handle.Dispose(); throw new IOException("目录包含重解析点，已停止"); }
        _leases.Add(handle);
        _directories.Add(path);
    }

    internal FileStream Read(string path, bool rename = false)
    {
        Directories(Path.GetDirectoryName(path)!, create: false);
        RequirePlain(path, directory: false);
        var handle = CreateFile(path, 0x80000000 | (rename ? 0x10000u : 0), 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastPInvokeError(); handle.Dispose(); Throw(error); }
        if (!GetFileInformationByHandle(handle, out var info)) { handle.Dispose(); Throw(Marshal.GetLastPInvokeError()); }
        if ((info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        { handle.Dispose(); throw new IOException("源文件必须是普通文件，不能跟随链接"); }
        var stream = new FileStream(handle, FileAccess.Read);
        _leases.Add(stream);
        return stream;
    }

    internal static LocalMediaSnapshot Snapshot(string path, FileStream stream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var hashBuilder = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = stream.Read(buffer, 0, buffer.Length);
                cancellationToken.ThrowIfCancellationRequested();
                if (count == 0) break;
                hashBuilder.AppendData(buffer, 0, count);
            }
        }
        finally { stream.Position = 0; }
        var hash = Convert.ToHexString(hashBuilder.GetHashAndReset());
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info)) Throw(Marshal.GetLastPInvokeError());
        var time = ((long)info.LastWriteTime.dwHighDateTime << 32) | (uint)info.LastWriteTime.dwLowDateTime;
        return new(path, stream.Length, DateTime.FromFileTimeUtc(time), hash,
            $"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}");
    }

    internal static void RequirePlain(string path, bool directory)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
            throw new IOException($"路径不是普通{(directory ? "目录" : "文件")}，或包含重解析点：{path}");
    }

    internal static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal void Recheck() { foreach (var directory in _directories) RequirePlain(directory, directory: true); }

    internal static void HardLink(string source, string target)
    {
        if (!CreateHardLink(target, source, IntPtr.Zero))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == 17) throw new IOException("硬链接需要源和目标位于同一磁盘卷；没有改用复制");
            Throw(error);
        }
    }

    /// <summary>Win32 长路径前缀；UNC 目标必须写成 <c>\\?\UNC\服务器\共享\...</c>，不能直接拼 <c>\\?\</c>。</summary>
    internal static string ExtendedPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    internal static void Rename(SafeFileHandle handle, string target)
    {
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(int);
        var chars = ExtendedPath(target).ToCharArray();
        var bytes = checked(chars.Length * sizeof(char));
        var size = checked(nameOffset + bytes + sizeof(char));
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size); // ReplaceIfExists = false.
            Marshal.WriteInt32(buffer, lengthOffset, bytes);
            Marshal.Copy(chars, 0, IntPtr.Add(buffer, nameOffset), chars.Length);
            if (!SetFileInformationByHandle(handle, 3, buffer, (uint)size)) Throw(Marshal.GetLastPInvokeError());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static void WriteCompleted(string path, byte[] data)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".momoka-" + Guid.NewGuid().ToString("N") + ".tmp");
        var handle = CreateFile(temporary, 0x40000000 | 0x10000, 0, IntPtr.Zero, 1, 0x80, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastPInvokeError(); handle.Dispose(); Throw(error); }
        using var stream = new FileStream(handle, FileAccess.Write);
        try
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
            Rename(handle, path);
        }
        catch (Exception error)
        {
            // Delete by the retained object handle, never by a path someone could replace.
            var disposition = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(disposition, 1);
                if (!SetFileInformationByHandle(handle, 4, disposition, 1))
                    throw new IOException($"写入失败，完整临时文件保留在 {temporary}", error);
            }
            finally { Marshal.FreeHGlobal(disposition); }
            throw;
        }
    }

    internal static void Throw(int error) => throw new IOException(new Win32Exception(error).Message, unchecked((int)0x80070000) | error);
    public void Dispose() { for (var index = _leases.Count - 1; index >= 0; index--) _leases[index].Dispose(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint attributes, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string target, string source, IntPtr security);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, IntPtr information, uint size);
}

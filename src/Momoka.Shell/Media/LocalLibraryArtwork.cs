namespace Momoka.Shell.Media;

/// <summary>本地索引中的图片只读普通文件，可位于本机盘或网络共享，并在解码前限制读取大小。</summary>
internal static class LocalLibraryArtwork
{
    internal const int MaximumBytes = 12 * 1024 * 1024;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    internal static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.Contains("://", StringComparison.Ordinal)) return "";
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal)) return "";
            var extension = Path.GetExtension(full);
            if (!Extensions.Contains(extension) || full.AsSpan(2).Contains(':')) return "";
            if (!IsUnc(full) && DriveTypeFor(full) is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) return "";
            return full;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return "";
        }
    }

    internal static string StampFor(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized.Length == 0) return "";
        // 网络路径不在同步调用里查属性：离线共享会让首页等 SMB 超时；路径本身仍可作缓存键。
        if (IsUnc(normalized) || DriveTypeFor(normalized) == DriveType.Network) return normalized;
        try
        {
            var file = new FileInfo(normalized);
            return file.Exists ? normalized + "\n" + file.Length + "\n" + file.LastWriteTimeUtc.Ticks : normalized;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return normalized; }
    }

    internal static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizePath(path);
        if (normalized.Length == 0) return [];
        // 网络共享的属性查询和打开可能等待 SMB 超时；放到线程池，避免卡住 UI 队列。
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 重解析点可能把本机绝对路径导向别处；目录和最终文件都必须是普通文件系统项。
            for (var current = normalized; current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return [];
            await using var stream = new FileStream(normalized, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumBytes) return [];
            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsUnc(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);
    private static DriveType DriveTypeFor(string full)
    {
        try
        {
            var root = Path.GetPathRoot(full);
            return root is { Length: > 0 } ? new DriveInfo(root).DriveType : DriveType.Unknown;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { return DriveType.Unknown; }
    }
}

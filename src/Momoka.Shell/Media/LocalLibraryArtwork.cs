namespace Momoka.Shell.Media;

/// <summary>本地索引中的图片只读本机普通文件，并在解码前限制读取大小。</summary>
internal static class LocalLibraryArtwork
{
    internal const int MaximumBytes = 12 * 1024 * 1024;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    internal static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal)) return "";
        try
        {
            var full = Path.GetFullPath(path);
            var extension = Path.GetExtension(full);
            if (!Extensions.Contains(extension) || full.AsSpan(2).Contains(':')) return "";
            var root = Path.GetPathRoot(full);
            if (root is null || new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable)) return "";
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
        // 重解析点可能把本机绝对路径导向网络；目录和最终文件都必须是普通文件系统项。
        for (var current = normalized; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return [];
        await using var stream = new FileStream(normalized, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumBytes) return [];
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }
}

namespace Momoka.Playback;

/// <summary>工具只接收本地普通文件；认证、网络读取和原影片身份由宿主负责。</summary>
public static class PlayerToolPaths
{
    public static string ResolveFfprobe(Momoka.Configuration.PlayerToolsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.IsNullOrWhiteSpace(settings.FfprobePath)) return settings.FfprobePath;
        try { return Path.Combine(Path.GetDirectoryName(settings.FfmpegPath) ?? "", "ffprobe.exe"); }
        catch (Exception) { return ""; }
    }

    public static bool IsExecutableAvailable(string? path)
    {
        try
        {
            RequireInput(path);
            return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (PlayerToolException) { return false; }
    }

    public static string RequireInput(string? path)
    {
        var full = LocalPath(path);
        if (!File.Exists(full)) throw new PlayerToolException("播放工具找不到本地输入文件。");
        RejectLinks(full);
        return full;
    }

    public static string RequireMediaInput(string? path)
    {
        var full = RequireInput(path);
        try
        {
            using var stream = File.OpenRead(full);
            var bytes = new byte[4_096];
            var count = stream.Read(bytes);
            var head = System.Text.Encoding.UTF8.GetString(bytes, 0, count).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            if (head.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase) ||
                head.StartsWith("ffconcat", StringComparison.OrdinalIgnoreCase) ||
                head.StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase) ||
                head.StartsWith("<", StringComparison.Ordinal) && (head.Contains("<MPD", StringComparison.OrdinalIgnoreCase) ||
                    head.Contains("<playlist", StringComparison.OrdinalIgnoreCase) || head.Contains("<smil", StringComparison.OrdinalIgnoreCase)) ||
                head.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || head.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new PlayerToolException("播放工具不支持引用外部资源的播放列表，请使用完整的本地媒体文件。");
            return full;
        }
        catch (PlayerToolException) { throw; }
        catch (Exception) { throw new PlayerToolException("播放工具无法核对本地媒体文件。"); }
    }

    public static string RequireOutput(string? path)
    {
        var full = LocalPath(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent)) throw new PlayerToolException("请先选择存在的本地输出文件夹。");
        RejectLinks(parent);
        if (File.Exists(full) || Directory.Exists(full)) throw new PlayerToolException("输出文件已存在，请选择新文件名。");
        return full;
    }

    private static string LocalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains("://", StringComparison.Ordinal) || path.Contains('"'))
            throw new PlayerToolException("播放工具只支持绝对路径的本地文件。");
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (root is not null && new DriveInfo(root).DriveType == DriveType.Network)
                throw new PlayerToolException("播放工具不支持网络映射文件路径。");
            // 禁 NTFS 备用流；允许 Windows 驱动器分隔符。
            if (full.IndexOf(':', 2) >= 0) throw new PlayerToolException("播放工具不支持此文件路径。");
            return full;
        }
        catch (PlayerToolException) { throw; }
        catch (Exception) { throw new PlayerToolException("播放工具文件路径无效。"); }
    }

    private static void RejectLinks(string path)
    {
        try
        {
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new PlayerToolException("播放工具不支持重解析文件或文件夹。");
        }
        catch (PlayerToolException) { throw; }
        catch (Exception) { throw new PlayerToolException("播放工具无法核对本地文件路径。"); }
    }
}

public sealed class PlayerToolException(string message) : Exception(message);

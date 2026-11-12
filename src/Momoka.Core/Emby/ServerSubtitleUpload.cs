namespace Momoka.Emby;

/// <summary>本地字幕原字节的不可变快照；上传协议使用 Base64，不替用户猜测或转换文本编码。</summary>
public sealed class ServerSubtitleFile
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[] { ".sub", ".srt", ".vtt", ".ass", ".ssa", ".mks" });

    public ServerSubtitleFile(string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(bytes);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(['/', '\\', ':', '\0']) >= 0
            || Path.GetFileNameWithoutExtension(fileName).Length == 0)
            throw new InvalidOperationException("请选择有效的本地字幕文件。");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!Extensions.Contains(extension)) throw new InvalidOperationException("字幕格式须为 SUB、SRT、VTT、ASS、SSA 或 MKS。");
        if (bytes.Length is 0 or > MaxBytes) throw new InvalidOperationException("字幕文件须大于 0 且不超过 10 MiB。");
        FileName = fileName;
        Format = extension[1..];
        ByteCount = bytes.Length;
        Data = Convert.ToBase64String(bytes);
    }

    public string FileName { get; }
    public string Format { get; }
    public int ByteCount { get; }
    public string Data { get; }
}

/// <summary>固定文件、ISO 639 三字母语言与两种标记；Jellyfin 没有 IsDefault 上传字段。</summary>
public sealed class ServerSubtitleUpload
{
    public ServerSubtitleUpload(ServerSubtitleFile file, string language, bool isForced = false, bool isHearingImpaired = false)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(language);
        if (language.Length != 3 || language.Any(character => character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z')))
            throw new InvalidOperationException("字幕语言须为三字母语言代码。");
        File = file;
        Language = language.ToLowerInvariant();
        IsForced = isForced;
        IsHearingImpaired = isHearingImpaired;
    }
    public ServerSubtitleFile File { get; }
    public string Language { get; }
    public bool IsForced { get; }
    public bool IsHearingImpaired { get; }
}

/// <summary>上传已发出但回执无法确认；只能重读，不因权限变化、取消或网络失败而重发。</summary>
public sealed class ServerSubtitleUploadUnconfirmedException(Exception inner)
    : Exception("服务器未确认这次字幕上传，请刷新字幕列表核对，不要重复上传。", inner);

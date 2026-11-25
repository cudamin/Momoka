using System.Text.RegularExpressions;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.LocalMedia;

/// <summary>
/// 本机视频文件名里能可靠读出的文件读数：大小 · 分辨率 · 视频编码 · 动态范围 · 音频格式 · 制作组。
/// 与 <see cref="MediaSource.ToFileInfoLabel"/> 同一种写法，让本机详情页的「视频：」那一行与服务器条目读起来一样。
/// 只读文件名与文件长度，不打开视频，也不假装知道文件名里没写的轨道。
/// </summary>
public static class LocalMediaFileInfo
{
    private static readonly Regex Resolution = Pattern(
        @"(?<![A-Za-z0-9])(4320p|2160p|1080p|720p|480p|4K|8K)(?![A-Za-z0-9])");
    private static readonly Regex Hevc = Pattern(@"(?<![A-Za-z0-9])(?:H[ ._-]?265|HEVC|x265)(?![A-Za-z0-9])");
    private static readonly Regex H264 = Pattern(@"(?<![A-Za-z0-9])(?:H[ ._-]?264|AVC|x264)(?![A-Za-z0-9])");
    private static readonly Regex Av1 = Pattern(@"(?<![A-Za-z0-9])AV1(?![A-Za-z0-9])");
    private static readonly Regex Range = Pattern(
        @"(?<![A-Za-z0-9])(HDR10\+?|HDR|Dolby[ ._-]?Vision|DV)(?![A-Za-z0-9])");
    private static readonly Regex Audio = Pattern(
        @"(?<![A-Za-z0-9])(TrueHD|Atmos|DTS[ ._-]?HD(?:[ ._-]?MA)?|DTS|E-?AC-?3|DDP?|AC3|AAC|FLAC)(?![A-Za-z0-9])");

    /// <summary>「641.2 MB  ·  1080p  ·  HEVC  ·  AAC  ·  ADWeb」；认不出的项跳过，全认不出时只用大小。</summary>
    public static string Label(string path, long? size)
    {
        var name = Path.GetFileName(path);
        var parts = new List<string>(6);
        if (TimeFormat.FileSize(size) is { Length: > 0 } formatted) parts.Add(formatted);
        if (Resolution.Match(name) is { Success: true } resolution) parts.Add(ResolutionLabel(resolution.Value));
        if (Hevc.IsMatch(name)) parts.Add("HEVC");
        else if (H264.IsMatch(name)) parts.Add("H264");
        else if (Av1.IsMatch(name)) parts.Add("AV1");
        if (Range.Match(name) is { Success: true } range) parts.Add(RangeLabel(range.Value));
        if (Audio.Match(name) is { Success: true } audio) parts.Add(AudioLabel(audio.Value));
        if (ReleaseGroup.FromFileName(path) is { Length: > 0 } group) parts.Add(group);
        return string.Join("  ·  ", parts);
    }

    private static string ResolutionLabel(string value)
    {
        var upper = value.ToUpperInvariant();
        return upper is "4K" or "8K" ? upper : upper[..^1] + "p";
    }

    private static string RangeLabel(string value) => value.ToUpperInvariant() switch
    {
        "DV" => "DolbyVision",
        var text when text.StartsWith("DOLBY", StringComparison.Ordinal) => "DolbyVision",
        var text => text
    };

    private static string AudioLabel(string value) => value.ToUpperInvariant() switch
    {
        var text when text.StartsWith("DTS", StringComparison.Ordinal) => "DTS",
        "EAC3" or "E-AC-3" or "EAC-3" => "EAC3",
        "DD" or "DDP" => "EAC3",
        var text => text
    };

    private static Regex Pattern(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}

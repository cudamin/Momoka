using System.Text.Json;

namespace Momoka.Translation;

/// <summary>从同一播放文件读取的字幕句；时间用于区分前后相同台词。</summary>
public sealed record SubtitleTranslationCue(string Text, string SubtitleId, double? Start, double? End)
{
    /// <summary>mpv serializes the native user-data map as JSON; strings/arrays/unknown tracks are not cues.</summary>
    public static SubtitleTranslationCue? FromNativeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 25000) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("sid", out var sidValue) || sidValue.ValueKind != JsonValueKind.String)
                return null;
            var text = textValue.GetString();
            var sid = sidValue.GetString();
            if (string.IsNullOrWhiteSpace(text) || text.Length > SubtitleTranslationClient.MaxTextLength
                || sid is null or "no" or "auto" or "-1" || sid.Length > 20) return null;
            return new(text, sid, ReadNumber("start"), ReadNumber("end"));

            double? ReadNumber(string name) => root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>只授予当前文件的字幕读取和独立译文 OSD；不能启动播放或改字幕轨道。</summary>
public interface ISubtitleTranslationPlaybackSession
{
    long Generation { get; }
    bool IsCurrent { get; }
    CancellationToken Lifetime { get; }
    Task<bool> EnsureOverlayAsync(string scriptPath, CancellationToken cancellationToken);
    Task<SubtitleTranslationCue?> ReadCueAsync(CancellationToken cancellationToken);
    Task<bool> ShowAsync(SubtitleTranslationCue cue, string translated, int fontSize, int position,
        CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

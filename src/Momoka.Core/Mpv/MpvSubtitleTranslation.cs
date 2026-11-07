using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text;
using Momoka.Translation;

namespace Momoka.Mpv;

/// <summary>译文使用独立 Lua OSD；不改 sid、secondary-sid 或原字幕外观。</summary>
public static class MpvSubtitleTranslation
{
    public const string ScriptRelativePath = "mpv-ui/scripts/momoka_subtitle_translation.lua";
    public const string ScriptName = "momoka_subtitle_translation";
    public const string ReadyProperty = "user-data/momoka-subtitle-translation/ready";
    public const string CueProperty = "user-data/momoka-subtitle-translation/cue";
    public const string Show = "momoka-subtitle-translation-show";
    public const string Clear = "momoka-subtitle-translation-clear";
    public const int MaxPayloadBytes = 100_000;
    private static readonly JsonSerializerOptions PayloadOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ScriptPath(string baseDirectory) =>
        Path.Combine(baseDirectory, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string Payload(SubtitleTranslationCue cue, string translated, int fontSize, int position)
    {
        // This JSON is an IPC data argument, never HTML. Literal Unicode fits the bounded Lua receiver.
        var payload = JsonSerializer.Serialize(new
        {
            original = cue.Text,
            start = cue.Start,
            end = cue.End,
            sid = cue.SubtitleId,
            revision = cue.Revision,
            text = translated,
            fontSize = Math.Clamp(fontSize, 20, 64),
            position = Math.Clamp(position, 10, 90)
        }, PayloadOptions);
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
            throw new InvalidOperationException("译文超过显示载荷上限，已保留原字幕。");
        return payload;
    }
}

using System.Text.Json;
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

    public static string ScriptPath(string baseDirectory) =>
        Path.Combine(baseDirectory, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string Payload(SubtitleTranslationCue cue, string translated, int fontSize, int position) =>
        JsonSerializer.Serialize(new
        {
            original = cue.Text,
            start = cue.Start,
            end = cue.End,
            sid = cue.SubtitleId,
            text = translated,
            fontSize = Math.Clamp(fontSize, 20, 64),
            position = Math.Clamp(position, 10, 90)
        });
}

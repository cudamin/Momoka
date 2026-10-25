using System.Text.Json;

namespace Momoka.Mpv;

/// <summary>内置播放器两条管线共用的独立 OSD 弹幕层，不替换 uosc／stats，不占用 sid。</summary>
public static class MpvDanmaku
{
    public const string ScriptRelativePath = "mpv-ui/scripts/momoka_danmaku.lua";
    public const string ScriptName = "momoka_danmaku";
    public const string Ready = "momoka-danmaku-ready";
    public const string Loaded = "momoka-danmaku-loaded";
    public const string Ping = "momoka-danmaku-ping";
    public const string Begin = "momoka-danmaku-begin";
    public const string Chunk = "momoka-danmaku-chunk";
    public const string Commit = "momoka-danmaku-commit";
    public const string Clear = "momoka-danmaku-clear";
    public const string Menu = "momoka-danmaku-menu";
    public const string InputState = "momoka-danmaku-input-state";
    public const string InputResult = "momoka-danmaku-input-result";
    public const string Send = "momoka-danmaku-send";
    public const string Visibility = "momoka-danmaku-visibility";

    public sealed record SendRequest(string Context, string RequestId, string Text);

    public static SendRequest? ParseSend(string payload)
    {
        if (payload.Length > 2048) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? Read(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var context = Read("context");
            var request = Read("requestId");
            var text = Read("text");
            if (context is not { Length: 32 } || !context.All(char.IsAsciiHexDigit)
                || request is not { Length: > 0 and <= 80 } || request.Any(char.IsControl)
                || text is not { Length: > 0 and <= 100 } || text.Any(char.IsControl)) return null;
            return new(context, request, text);
        }
        catch (JsonException) { return null; }
    }

    public static string ScriptPath(string baseDirectory) => Path.Combine(baseDirectory, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static bool Exists(string baseDirectory) => File.Exists(ScriptPath(baseDirectory));

    public static bool IsReceipt(string value) => value.Length <= 50 && value.Split(':') is { Length: 3 } parts
        && parts.All(part => long.TryParse(part, out var number) && number >= 0);
}

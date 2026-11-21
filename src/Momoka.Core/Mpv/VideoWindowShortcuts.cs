using Momoka.Playback;

namespace Momoka.Mpv;

/// <summary>Translate the shared shortcut tokens to mpv's key spelling without executing commands.</summary>
public static class VideoWindowShortcuts
{
    public static IReadOnlyDictionary<string, string> Bindings(IReadOnlyDictionary<string, string> bindings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in ShortcutCatalog.Actions)
            if (Key(action.Default) is { } key) result[key] = "";
        foreach (var (id, stroke) in ShortcutCatalog.Resolve(bindings))
            if (Key(stroke) is { } key && string.IsNullOrEmpty(result.GetValueOrDefault(key))) result[key] = id;
        return result;
    }

    public static string? Key(KeyStroke stroke)
    {
        if (stroke.IsEmpty) return null;
        var key = ShortcutKeyCatalog.StandaloneMpvKey(stroke.Key);
        if (key is null) return null;
        return (stroke.Ctrl ? "Ctrl+" : "") + (stroke.Alt ? "Alt+" : "") + (stroke.Shift ? "Shift+" : "") + key;
    }
}

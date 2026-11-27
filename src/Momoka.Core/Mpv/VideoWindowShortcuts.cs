using Momoka.Playback;

namespace Momoka.Mpv;

/// <summary>Translate the shared shortcut tokens to mpv's key spelling without executing commands.</summary>
public static class VideoWindowShortcuts
{
    public static IReadOnlyDictionary<string, string> Bindings(IReadOnlyDictionary<string, string> bindings, bool skipOffered = false)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in ShortcutCatalog.Actions)
            if (Key(action.Default) is { } key) result[key] = "";
        var effective = ShortcutCatalog.Resolve(bindings);
        foreach (var (id, stroke) in effective)
            if (!ShortcutCatalog.IsSkipPrompt(id) && Key(stroke) is { } key) result[key] = id;
        if (skipOffered)
        {
            // 提示的两颗键允许借用普通快捷键；提示收起后下一份映射立即恢复普通动作。
            foreach (var (id, stroke) in effective.Where(pair => ShortcutCatalog.IsSkipPrompt(pair.Key)))
                if (Key(stroke) is { } key) result[key] = id;
            var keypadEnter = new KeyStroke("NumPadEnter", false, false, false);
            if (ShortcutCatalog.Lookup(bindings, keypadEnter, skipOffered: true) is { } alias && ShortcutCatalog.IsSkipPrompt(alias))
                result[Key(keypadEnter)!] = alias;
        }
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

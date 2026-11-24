namespace Momoka.Playback;

/// <summary>Windows virtual-key value to persisted shortcut token, without a WinUI dependency.</summary>
public static class ShortcutKeyCatalog
{
    private static readonly HashSet<string> KnownTokens = Enumerable.Range(1, 0xFF)
        .SelectMany(key => new[] { Token(key), Token(key, extended: true) })
        .Where(token => token is not null).Select(token => token!).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The reverse of <see cref="Token"/>: the Windows virtual-key code behind a persisted token.
    /// <para>
    /// Needed by the long-press seek (2026-11-24): the exclusive window only reports「这个动作按了一下」,
    /// not「键还按着没有」, so the host polls <c>GetAsyncKeyState</c> with the key the action is bound to.
    /// Tokens that <see cref="Token"/> preserves as <c>VkXX</c> resolve back to the same code; a token this
    /// catalog did not produce (or an empty binding) returns 0, which the caller treats as「这把键判不了
    /// 长按」 and falls back to the old one-press-one-jump behavior.
    /// </para>
    /// </summary>
    public static int KeyCode(string token) => KeyCodes.TryGetValue(token, out var key) ? key : 0;

    private static readonly Dictionary<string, int> KeyCodes = BuildKeyCodes();

    private static Dictionary<string, int> BuildKeyCodes()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var key = 1; key <= 0xFF; key++)
        {
            foreach (var extended in new[] { false, true })
            {
                if (Token(key, extended) is { } token) map.TryAdd(token, key);
            }
        }
        return map;
    }

    public static string? Token(int key, bool extended = false)
    {
        if (key is >= 0x41 and <= 0x5A) return ((char)key).ToString();
        if (key is >= 0x30 and <= 0x39) return ((char)key).ToString();
        if (key is >= 0x70 and <= 0x87) return $"F{key - 0x6F}";
        if (key is >= 0x60 and <= 0x69) return $"NumPad{key - 0x60}";

        if (extended && key is >= 0x21 and <= 0x28 or 0x2D or 0x2E)
            return key switch
            {
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x23 => "End",
                0x24 => "Home",
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0x2D => "Insert",
                0x2E => "Delete",
                _ => null
            };
        if (extended && key == 0x0D) return "NumPadEnter";
        if (!extended && key is >= 0x21 and <= 0x28 or 0x2D or 0x2E)
            return key switch
            {
                0x21 => "NumPadPageUp",
                0x22 => "NumPadPageDown",
                0x23 => "NumPadEnd",
                0x24 => "NumPadHome",
                0x25 => "NumPadLeft",
                0x26 => "NumPadUp",
                0x27 => "NumPadRight",
                0x28 => "NumPadDown",
                0x2D => "NumPadInsert",
                0x2E => "NumPadDelete",
                _ => null
            };
        if (!extended && key == 0x0C) return "NumPadBegin";

        var token = key switch
        {
            0x08 => "Back",
            0x09 => "Tab",
            0x0C => "Clear",
            0x0D => "Enter",
            0x10 => "ShiftKey",
            0x11 => "Control",
            0x12 => "Menu",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x15 => "Kana",
            0x16 => "ImeOn",
            0x17 => "Junja",
            0x18 => "Final",
            0x19 => "Hanja",
            0x1B => "Escape",
            0x1C => "Convert",
            0x1D => "NonConvert",
            0x1E => "Accept",
            0x1F => "ModeChange",
            0x20 => "Space",
            0x29 => "Select",
            0x2A => "Print",
            0x2B => "Execute",
            0x2C => "PrintScreen",
            0x2F => "Help",
            0x5B => "LeftWindows",
            0x5C => "RightWindows",
            0x5D => "Application",
            0x5F => "Sleep",
            0x6A => "NumPadMultiply",
            0x6B => "NumPadAdd",
            0x6C => "NumPadSeparator",
            0x6D => "NumPadSubtract",
            0x6E => "NumPadDecimal",
            0x6F => "NumPadDivide",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xA0 => "LeftShift",
            0xA1 => "RightShift",
            0xA2 => "LeftControl",
            0xA3 => "RightControl",
            0xA4 => "LeftMenu",
            0xA5 => "RightMenu",
            0xA6 => "BrowserBack",
            0xA7 => "BrowserForward",
            0xA8 => "BrowserRefresh",
            0xA9 => "BrowserStop",
            0xAA => "BrowserSearch",
            0xAB => "BrowserFavorites",
            0xAC => "BrowserHome",
            0xAD => "VolumeMute",
            0xAE => "VolumeDown",
            0xAF => "VolumeUp",
            0xB0 => "MediaNextTrack",
            0xB1 => "MediaPreviousTrack",
            0xB2 => "MediaStop",
            0xB3 => "MediaPlayPause",
            0xB4 => "LaunchMail",
            0xB5 => "LaunchMediaSelect",
            0xB6 => "LaunchApp1",
            0xB7 => "LaunchApp2",
            0xBA => "Semicolon",
            0xBB => "Equal",
            0xBC => "Comma",
            0xBD => "Minus",
            0xBE => "Period",
            0xBF => "Slash",
            0xC0 => "Backquote",
            0xDB => "BracketLeft",
            0xDC => "Backslash",
            0xDD => "BracketRight",
            0xDE => "Quote",
            0xDF => "Oem8",
            0xE2 => "Oem102",
            0xE5 => "ImeProcess",
            0xE7 => "Packet",
            0xF6 => "Attn",
            0xF7 => "CrSel",
            0xF8 => "ExSel",
            0xF9 => "EraseEof",
            0xFA => "Play",
            0xFB => "Zoom",
            0xFC => "NoName",
            0xFD => "Pa1",
            0xFE => "OemClear",
            _ => null
        };
        // Preserve every remaining virtual-key code as an editable shortcut. Some OEM,
        // IME and vendor keys have no stable display name, but must not vanish at capture time.
        return token ?? (key is > 0 and <= 0xFF ? $"Vk{key:X2}" : null);
    }

    public static bool IsKnownToken(string token) => KnownTokens.Contains(token);

    /// <summary>Builds a key chord while treating the pressed modifier itself as the key, not as a chord flag.</summary>
    public static KeyStroke Stroke(string token, bool ctrl, bool alt, bool shift)
    {
        if (token is "Control" or "LeftControl" or "RightControl") ctrl = false;
        if (token is "Menu" or "LeftMenu" or "RightMenu") alt = false;
        if (token is "ShiftKey" or "LeftShift" or "RightShift") shift = false;
        return new(token, ctrl, alt, shift);
    }

    public static string? StandaloneMpvKey(string token) => token switch
    {
        "Space" => "SPACE",
        "Back" => "BS",
        "Delete" => "DEL",
        "Escape" => "ESC",
        "Enter" => "ENTER",
        "Tab" => "TAB",
        "Insert" => "INS",
        "Home" => "HOME",
        "End" => "END",
        "PageUp" => "PGUP",
        "PageDown" => "PGDWN",
        "Left" => "LEFT",
        "Right" => "RIGHT",
        "Up" => "UP",
        "Down" => "DOWN",
        "PrintScreen" => "PRINT",
        "Pause" => "PAUSE",
        "Sleep" => "SLEEP",
        "Application" => "MENU",
        "NumPad0" => "KP0",
        "NumPad1" => "KP1",
        "NumPad2" => "KP2",
        "NumPad3" => "KP3",
        "NumPad4" => "KP4",
        "NumPad5" => "KP5",
        "NumPad6" => "KP6",
        "NumPad7" => "KP7",
        "NumPad8" => "KP8",
        "NumPad9" => "KP9",
        "NumPadDecimal" => "KP_DEC",
        "NumPadDivide" => "KP_DIVIDE",
        "NumPadMultiply" => "KP_MULTIPLY",
        "NumPadSubtract" => "KP_SUBTRACT",
        "NumPadAdd" => "KP_ADD",
        "NumPadEnter" => "KP_ENTER",
        "NumPadBegin" => "KP_BEGIN",
        "NumPadPageUp" => "KP_PGUP",
        "NumPadPageDown" => "KP_PGDWN",
        "NumPadHome" => "KP_HOME",
        "NumPadEnd" => "KP_END",
        "NumPadLeft" => "KP_LEFT",
        "NumPadRight" => "KP_RIGHT",
        "NumPadUp" => "KP_UP",
        "NumPadDown" => "KP_DOWN",
        "NumPadInsert" => "KP_INS",
        "NumPadDelete" => "KP_DEL",
        "CapsLock" or "NumLock" or "ScrollLock" or "LeftWindows" or "RightWindows" or "Oem8" or "OemClear"
            or "ImeOn" or "ImeProcess" or "LaunchApp1" or "LaunchApp2" or "LaunchMediaSelect" or "Kana" or "Hanja"
            or "Junja" or "Final" or "Convert" or "NonConvert" or "Accept" or "ModeChange" or "Select" or "Execute"
            or "Help" or "Attn" or "CrSel" or "ExSel" or "EraseEof" or "NoName" or "Pa1" => null,
        "VolumeMute" => "MUTE",
        "VolumeDown" => "VOLUME_DOWN",
        "VolumeUp" => "VOLUME_UP",
        "MediaNextTrack" => "NEXT",
        "MediaPreviousTrack" => "PREV",
        "MediaStop" => "STOP",
        "MediaPlayPause" => "PLAYPAUSE",
        "MediaPlay" => "PLAYONLY",
        "MediaPause" => "PAUSEONLY",
        "MediaRecord" => "RECORD",
        "MediaFastForward" => "FORWARD",
        "MediaRewind" => "REWIND",
        "MediaChannelUp" => "CHANNEL_UP",
        "MediaChannelDown" => "CHANNEL_DOWN",
        "BrowserBack" => "GO_BACK",
        "BrowserForward" => "GO_FORWARD",
        "BrowserSearch" => "SEARCH",
        "BrowserFavorites" => "FAVORITES",
        "BrowserHome" => "HOMEPAGE",
        "LaunchMail" => "MAIL",
        "BracketLeft" => "[",
        "BracketRight" => "]",
        "Minus" => "-",
        "Equal" => "=",
        "Comma" => ",",
        "Period" => ".",
        "Backquote" => "`",
        "Semicolon" => ";",
        "Slash" => "/",
        "Backslash" => "\\",
        "Quote" => "'",
        "Oem102" => "\\",
        { Length: 1 } value when char.IsAsciiLetterOrDigit(value[0]) => value.ToLowerInvariant(),
        { Length: 2 or 3 } value when value[0] == 'F' && int.TryParse(value.AsSpan(1), out var number)
            && number is >= 1 and <= 24 => value,
        _ => token.Length == 1 && char.IsAscii(token[0]) ? token : null
    };
}

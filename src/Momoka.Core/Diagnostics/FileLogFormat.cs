using System.Globalization;
using System.Text;

namespace Momoka.Diagnostics;

/// <summary>The naming and UTF-8 accounting shared by rotation, restart, and bounded disk writes.</summary>
internal static class FileLogFormat
{
    internal static readonly UTF8Encoding Utf8 = new(false);
    private const string TruncationMarker = " …[日志超过容量，已省略中段]… ";

    internal static string Name(DateOnly day, int part)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(part);
        var date = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return part == 0 ? $"app-{date}.log" : $"app-{date}-{part.ToString("D6", CultureInfo.InvariantCulture)}.log";
    }

    internal static bool TryReadName(string name, out DateOnly day, out int part)
    {
        day = default;
        part = 0;
        if (!name.StartsWith("app-", StringComparison.Ordinal) || !name.EndsWith(".log", StringComparison.Ordinal)) return false;
        var stem = name.AsSpan(4, name.Length - 8);
        if (stem.Length < 8 || !DateOnly.TryParseExact(stem[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return false;
        return stem.Length == 8 || (stem.Length > 9 && stem[8] == '-'
            && int.TryParse(stem[9..], NumberStyles.None, CultureInfo.InvariantCulture, out part) && part > 0);
    }

    internal static (string Line, int Bytes) BoundLine(string line, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 128);
        var newlineBytes = Utf8.GetByteCount(Environment.NewLine);
        var payloadBudget = maximumBytes - newlineBytes;
        var bytes = Utf8.GetByteCount(line);
        if (bytes <= payloadBudget) return (line, bytes + newlineBytes);

        // An individual exception/message cannot create an oversized file of its own. Keep both the
        // identifying prefix and the diagnostic tail, visibly omit the middle, and never cut a valid
        // surrogate pair. Normal entries are untouched, including their existing multiline details.
        var remaining = payloadBudget - Utf8.GetByteCount(TruncationMarker);
        var prefix = PrefixChars(line, (remaining + 1) / 2);
        var suffix = SuffixChars(line, remaining / 2);
        var bounded = string.Concat(line.AsSpan(0, prefix), TruncationMarker.AsSpan(), line.AsSpan(line.Length - suffix));
        return (bounded, Utf8.GetByteCount(bounded) + newlineBytes);
    }

    private static int PrefixChars(string text, int budget)
    {
        var index = 0;
        while (index < text.Length)
        {
            var count = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            var bytes = count == 2 ? 4 : CharacterBytes(text[index]);
            if (bytes > budget) break;
            budget -= bytes;
            index += count;
        }
        return index;
    }

    private static int SuffixChars(string text, int budget)
    {
        var index = text.Length;
        while (index > 0)
        {
            var count = char.IsLowSurrogate(text[index - 1]) && index > 1 && char.IsHighSurrogate(text[index - 2]) ? 2 : 1;
            var bytes = count == 2 ? 4 : CharacterBytes(text[index - 1]);
            if (bytes > budget) break;
            budget -= bytes;
            index -= count;
        }
        return text.Length - index;
    }

    private static int CharacterBytes(char character) => character <= 0x7F ? 1 : character <= 0x7FF ? 2 : 3;
}

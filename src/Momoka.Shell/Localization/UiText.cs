using System.Globalization;
using System.Text.Json;
using Momoka.Configuration;

namespace Momoka.Shell.Localization;

/// <summary>Translates explicitly supplied app labels; never scans user or server content.</summary>
public static class UiText
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(LoadEnglish);

    public static string Language { get; internal set; } = UiLanguages.Chinese;

    public static string Translate(string source)
    {
        if (Language != UiLanguages.English || string.IsNullOrEmpty(source)) return source;
        if (English.Value.TryGetValue(source, out var translated)) return translated;
        // Annotate appends this exact suffix to app-authored settings notes. Preserve the option
        // verbatim rather than translating arbitrary substrings (which could contain user data).
        const string optionMarker = "（mpv：";
        var optionStart = source.LastIndexOf(optionMarker, StringComparison.Ordinal);
        if (optionStart > 0 && source.EndsWith('）'))
            return Translate(source[..optionStart]) + " (mpv: " + source[(optionStart + optionMarker.Length)..^1] + ")";
        if (source.StartsWith("mpv：", StringComparison.Ordinal)) return "mpv: " + source[4..];
        // App-authored multiline notes translate full lines only.
        return source.Contains('\n')
            ? string.Join('\n', source.Split('\n').Select(line => English.Value.GetValueOrDefault(line.TrimEnd('\r'), line)))
            : source;
    }

    public static string Format(string source, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Translate(source), arguments);

    private static IReadOnlyDictionary<string, string> LoadEnglish()
    {
        var assembly = typeof(UiText).Assembly;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith("English.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing UI dictionary: {name}");
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException($"Invalid UI dictionary: {name}");
            foreach (var entry in entries) result[entry.Key] = entry.Value;
        }
        return result;
    }
}

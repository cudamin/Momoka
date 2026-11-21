using System.Globalization;

namespace Momoka.Configuration;

/// <summary>Persisted UI language preferences, independent of media and subtitle languages.</summary>
public static class UiLanguages
{
    public const string System = "system";
    public const string Chinese = "zh-Hans";
    public const string English = "en-US";

    public static string Normalize(string? preference)
    {
        var value = preference?.Trim();
        if (string.Equals(value, System, StringComparison.OrdinalIgnoreCase)) return System;
        if (IsLanguage(value, "zh")) return Chinese;
        if (IsLanguage(value, "en")) return English;
        return System;
    }

    public static string Resolve(string? preference, string? systemLanguage) => Normalize(preference) switch
    {
        Chinese => Chinese,
        English => English,
        _ => IsLanguage(systemLanguage?.Trim(), "zh") ? Chinese : English
    };

    public static string ResolveWindowsDisplayLanguage(string? preference, ushort languageId)
    {
        var normalized = Normalize(preference);
        if (normalized != System) return normalized;
        if (languageId == 0) return English;
        try { return Resolve(System, CultureInfo.GetCultureInfo(languageId).Name); }
        // Supplemental Windows UI locales can return 0x1400, which has no CultureInfo.
        // Keep startup usable and apply the same English fallback as other unknown languages.
        catch (CultureNotFoundException) { return English; }
    }

    private static bool IsLanguage(string? value, string language) =>
        string.Equals(value, language, StringComparison.OrdinalIgnoreCase)
        || value?.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase) == true;
}

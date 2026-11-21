using System.Runtime.InteropServices;
using Momoka.Configuration;

namespace Momoka.Shell.Localization;

/// <summary>Sets MRT before XAML loads. Language changes take effect on the next launch.</summary>
internal static partial class UiLanguage
{
    internal static void Initialize(string preference)
    {
        // Windows display language, not CurrentCulture (which is the user's regional format).
        var language = UiLanguages.ResolveWindowsDisplayLanguage(preference, GetUserDefaultUILanguage());
        // MRTCore source: dev/MRTCore/mrt/Microsoft.Windows.ApplicationModel.Resources/src/
        // ApplicationLanguages.cpp. Unpackaged overrides live in this process; our preference lives in settings.
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        UiText.Language = language;
    }

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}

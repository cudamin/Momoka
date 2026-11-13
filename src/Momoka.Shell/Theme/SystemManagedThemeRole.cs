namespace Momoka.Shell;

/// <summary>One declared native theme alias; source contracts verify its dynamic Color resources.</summary>
internal static class SystemManagedThemeRole
{
    internal const string DetailContentLayerBrush = "EgDetailContentLayerBrush";

    internal static bool IsManaged(string key, Func<string, string, bool> hasSolidBrush) =>
        key == DetailContentLayerBrush
        && hasSolidBrush("Dark", key)
        && hasSolidBrush("Light", key)
        && hasSolidBrush("HighContrast", key);
}

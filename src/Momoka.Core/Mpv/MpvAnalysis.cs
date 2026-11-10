namespace Momoka.Mpv;

/// <summary>Explicit crop and silence detection, sharing the current player's local decoded data.</summary>
public static class MpvAnalysis
{
    public const string ScriptRelativePath = "mpv-ui/scripts/momoka_analysis.lua";
    public const string ScriptName = "momoka_analysis";
    public const string ReadyProperty = "user-data/momoka-analysis-ready";
    public const string CropProperty = "user-data/momoka-analysis-crop-available";
    public const string SilenceProperty = "user-data/momoka-analysis-silence-available";
    public static string ScriptPath(string baseDirectory) => Path.Combine(baseDirectory, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
    public static bool Exists(string baseDirectory) => File.Exists(ScriptPath(baseDirectory));
    public static bool IsTrue(string? value) => value?.Trim().Trim('"') is "yes" or "true" or "1";
}

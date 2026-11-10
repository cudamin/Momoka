namespace Momoka.Configuration;

/// <summary>用户显式选择的播放工具；空路径表示未配置，不自动执行 PATH 中的同名程序。</summary>
public sealed class PlayerToolsSettings
{
    public static IReadOnlyList<string> WhisperModels { get; } = ["base", "small", "medium", "large", "large-v2", "large-v3", "turbo"];
    public static IReadOnlyList<string> WhisperDevices { get; } = ["cpu", "cuda"];

    public string FfmpegPath { get; set; } = "";
    public string FfprobePath { get; set; } = "";
    public string FfsubsyncPath { get; set; } = "";
    public string AlassPath { get; set; } = "";
    public string FasterWhisperPath { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string WhisperModel { get; set; } = "base";
    public string WhisperDevice { get; set; } = "cpu";
    public string WhisperLanguage { get; set; } = "";

    public void Normalize()
    {
        FfmpegPath = FfmpegPath?.Trim() ?? "";
        FfprobePath = FfprobePath?.Trim() ?? "";
        FfsubsyncPath = FfsubsyncPath?.Trim() ?? "";
        AlassPath = AlassPath?.Trim() ?? "";
        FasterWhisperPath = FasterWhisperPath?.Trim() ?? "";
        OutputDirectory = OutputDirectory?.Trim() ?? "";
        WhisperModel = WhisperModels.Contains(WhisperModel, StringComparer.Ordinal) ? WhisperModel : "base";
        WhisperDevice = WhisperDevices.Contains(WhisperDevice, StringComparer.Ordinal) ? WhisperDevice : "cpu";
        WhisperLanguage = WhisperLanguage?.Trim() ?? "";
        if (WhisperLanguage.Length > 16 || WhisperLanguage.Any(c => !char.IsAsciiLetter(c) && c != '-')) WhisperLanguage = "";
    }
}

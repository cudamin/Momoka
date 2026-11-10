using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

public sealed partial class SettingsViewModel
{
    internal const string PlayerToolsCategory = "播放工具";
    private Func<Task<string?>>? _pickPlayerToolExecutable;
    private Func<Task<string?>>? _pickPlayerToolDirectory;
    private int _playerToolsPickerVersion;
    private bool _playerToolsPicking;

    internal void UsePlayerToolsPickers(Func<Task<string?>> executable, Func<Task<string?>> directory)
    {
        _pickPlayerToolExecutable = executable;
        _pickPlayerToolDirectory = directory;
        _playerToolsPickerVersion++;
    }

    private IReadOnlyList<SettingSection> PlayerToolsSections()
    {
        var tools = Settings.PlayerTools;
        List<SettingRow> exportRows =
        [
            ToolPath("ffmpeg 路径", "选择 ffmpeg.exe", () => tools.FfmpegPath, value => tools.FfmpegPath = value),
            ToolExecutableStatus("ffmpeg 状态", () => tools.FfmpegPath, value => tools.FfmpegPath = value),
            ToolPath("导出保存目录", "留空时使用截图保存目录", () => tools.OutputDirectory, value => tools.OutputDirectory = value,
                "片段、动图与字幕文件存到这里。留空使用截图保存目录。"),
            Fact("保存目录状态", null, ToolDirectoryStatus(tools.OutputDirectory), "选择文件夹…",
                _pickPlayerToolDirectory is null ? null : () => _ = PickPlayerToolPathAsync(true, value => tools.OutputDirectory = value))
        ];
        List<SettingRow> syncRows =
        [
            ToolPath("ffprobe 路径", "留空时查找 ffmpeg 同目录的 ffprobe.exe", () => tools.FfprobePath, value => tools.FfprobePath = value),
            ToolExecutableStatus("ffprobe 状态", () => PlayerToolPaths.ResolveFfprobe(tools), value => tools.FfprobePath = value),
            ToolPath("ffsubsync 路径", "选择 ffsubsync.exe", () => tools.FfsubsyncPath, value => tools.FfsubsyncPath = value),
            ToolExecutableStatus("ffsubsync 状态", () => tools.FfsubsyncPath, value => tools.FfsubsyncPath = value),
            ToolPath("alass 路径", "选择 alass.exe", () => tools.AlassPath, value => tools.AlassPath = value),
            ToolExecutableStatus("alass 状态", () => tools.AlassPath, value => tools.AlassPath = value),
            Fact("字幕同步工具", "ffsubsync 与 alass 可任选一种；按音轨同步还需要 ffmpeg 与 ffprobe。", ToolSyncStatus(tools))
        ];
        List<SettingRow> whisperRows =
        [
            ToolPath("faster-whisper 路径", "选择 faster-whisper-xxl.exe", () => tools.FasterWhisperPath, value => tools.FasterWhisperPath = value,
                "使用兼容 faster-whisper-xxl 参数的独立程序，从当前片段的音频生成字幕。"),
            ToolExecutableStatus("faster-whisper 状态", () => tools.FasterWhisperPath, value => tools.FasterWhisperPath = value)
        ];
        if (PlayerToolPaths.IsExecutableAvailable(tools.FasterWhisperPath))
        {
            whisperRows.Add(Pick("识别模型", "小模型更快，大模型占用更多内存。首次使用可能由识别程序下载模型。",
                PlayerToolsSettings.WhisperModels.Select(model => (Label: model, Value: model)),
                () => tools.WhisperModel, value => tools.WhisperModel = value, StringComparer.Ordinal));
            whisperRows.Add(Pick("识别设备", "CPU 通用；CUDA 需要识别程序支持的 NVIDIA 显卡与驱动。",
                new[] { ("CPU", "cpu"), ("NVIDIA CUDA", "cuda") },
                () => tools.WhisperDevice, value => tools.WhisperDevice = value, StringComparer.Ordinal));
            whisperRows.Add(Pick("识别语言", "自动识别由语音识别程序判断。",
                new[] { ("自动识别", ""), ("中文", "zh"), ("英语", "en"), ("日语", "ja"), ("韩语", "ko"),
                    ("法语", "fr"), ("德语", "de"), ("西班牙语", "es"), ("俄语", "ru") },
                () => tools.WhisperLanguage, value => tools.WhisperLanguage = value, StringComparer.Ordinal));
        }
        return
        [
            new(PlayerToolsCategory, "视频导出", "为播放器右键菜单中的片段与动图导出选择本机工具。配置自动保存，下一次操作使用新值。", exportRows),
            new(PlayerToolsCategory, "字幕同步", "用本机工具校准字幕时间。选择工具只检查文件，不启动同步。", syncRows),
            new(PlayerToolsCategory, "AI 字幕", "通过本机语音识别程序为片段生成字幕。识别参数在选好程序后显示。", whisperRows)
        ];
    }

    private SettingTextRow ToolPath(string label, string placeholder, Func<string> read, Action<string> write, string? note = null) =>
        new(label, note ?? "填写绝对路径，可直接粘贴资源管理器的“复制为路径”。", placeholder, TypedPath.Clean(read()), typed =>
        {
            var value = TypedPath.Clean(typed);
            write(value);
            return value;
        }, Save, RefreshPlayerToolsRows);

    private SettingFactRow ToolExecutableStatus(string label, Func<string> read, Action<string> write) =>
        Fact(label, "找到文件只表示路径可用，不代表工具版本兼容。", ToolExecutableState(read()), "选择程序…",
            _pickPlayerToolExecutable is null ? null : () => _ = PickPlayerToolPathAsync(false, write));

    private static string ToolExecutableState(string path) => string.IsNullOrWhiteSpace(path) ? "未配置"
        : PlayerToolPaths.IsExecutableAvailable(path) ? "已找到本地程序" : "未找到可用的本地 .exe 程序";

    private static string ToolSyncStatus(PlayerToolsSettings tools)
    {
        var available = new[]
        {
            (Label: "ffsubsync", Path: tools.FfsubsyncPath), (Label: "alass", Path: tools.AlassPath)
        }.Where(tool => PlayerToolPaths.IsExecutableAvailable(tool.Path)).Select(tool => tool.Label).ToArray();
        if (available.Length == 0) return "未配置可用的字幕同步工具";
        var audio = PlayerToolPaths.IsExecutableAvailable(tools.FfmpegPath)
            && PlayerToolPaths.IsExecutableAvailable(PlayerToolPaths.ResolveFfprobe(tools));
        return string.Join("、", available) + (audio ? "；音轨参考工具已找到" : "；音轨参考还需要 ffmpeg 与 ffprobe");
    }

    private static string ToolDirectoryStatus(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "跟随截图保存目录";
        try
        {
            _ = PlayerToolPaths.RequireOutput(Path.Combine(path, "momoka-directory-check-" + Guid.NewGuid().ToString("N")));
            return "已找到本地文件夹";
        }
        catch (Exception error) when (error is PlayerToolException or ArgumentException or NotSupportedException)
        {
            return "未找到可用的本地文件夹";
        }
    }

    private void RefreshPlayerToolsRows()
    {
        if (_settings is null) return;
        var current = Sections.Where(section => section.Category == PlayerToolsCategory).ToArray();
        if (current.Length == 0) return;
        foreach (var desired in PlayerToolsSections())
        {
            var section = current.FirstOrDefault(card => card.Title == desired.Title);
            if (section is null) continue;
            section.UpdateRows(desired.Rows);
            foreach (var text in desired.Rows.OfType<SettingTextRow>())
                section.Rows.OfType<SettingTextRow>().FirstOrDefault(row => row.Label == text.Label)?.Reseed(text.Text);
        }
    }

    private async Task PickPlayerToolPathAsync(bool directory, Action<string> write)
    {
        if (_settings is null || !_active || _disposed || _playerToolsPicking) return;
        var picker = directory ? _pickPlayerToolDirectory : _pickPlayerToolExecutable;
        if (picker is null) return;
        var owner = _settings;
        var tools = Settings.PlayerTools;
        var version = _playerToolsPickerVersion;
        bool Current() => _active && !_disposed && version == _playerToolsPickerVersion
            && ReferenceEquals(owner, _settings) && ReferenceEquals(tools, Settings.PlayerTools);
        _playerToolsPicking = true;
        try
        {
            var selected = await picker().ConfigureAwait(true);
            if (!Current() || selected is null) return;
            var path = TypedPath.Clean(selected);
            if (directory) _ = PlayerToolPaths.RequireOutput(Path.Combine(path, "momoka-directory-check-" + Guid.NewGuid().ToString("N")));
            else if (!PlayerToolPaths.IsExecutableAvailable(path)) throw new PlayerToolException("请选择可用的本地 .exe 程序。");
            write(path);
            Save();
            RefreshPlayerToolsRows();
        }
        catch (Exception error)
        {
            if (Current()) Notify("选择播放工具失败", Failure.Describe(error), InfoBarSeverity.Error);
        }
        finally { _playerToolsPicking = false; }
    }
}

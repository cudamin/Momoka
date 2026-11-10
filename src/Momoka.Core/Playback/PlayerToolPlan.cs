using System.Globalization;
using Momoka.Configuration;

namespace Momoka.Playback;

public enum PlayerToolAction { ExportClip, ExportGif, ExportGifWithSubtitles, ExportSubtitle, SynchronizeSubtitle, GenerateSubtitle }
public enum PlayerSubtitleSyncEngine { Ffsubsync, Alass }

public sealed record PlayerToolInput(string MediaPath, string OutputPath)
{
    public string? SubtitlePath { get; init; }
    public int? SubtitleStreamIndex { get; init; }
    public int? AudioStreamIndex { get; init; }
    public int? VideoStreamIndex { get; init; }
    public double StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public bool IncludeAudio { get; init; } = true;
    public PlayerSubtitleSyncEngine SyncEngine { get; init; } = PlayerSubtitleSyncEngine.Ffsubsync;
}

/// <summary>固定工具协议的封存计划；不接收网络地址、认证头或任意命令模板。</summary>
public sealed class PlayerToolPlan
{
    private PlayerToolPlan(string executablePath, IReadOnlyList<string> arguments, string outputPath, IReadOnlyList<string> inputs,
        IReadOnlyDictionary<string, string> environment)
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
        OutputPath = outputPath;
        Inputs = inputs;
        Environment = environment;
    }

    public string ExecutablePath { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string OutputPath { get; }
    public IReadOnlyDictionary<string, string> Environment { get; }
    internal IReadOnlyList<string> Inputs { get; }

    internal static PlayerToolPlan Image(string executable, IReadOnlyList<string> arguments, string output,
        IReadOnlyList<string> inputs) => new(executable, arguments, output, inputs,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>()));

    public static PlayerToolPlan Create(PlayerToolsSettings settings, PlayerToolAction action, PlayerToolInput input)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(action) || !Enum.IsDefined(input.SyncEngine)) throw new PlayerToolException("播放工具操作无效。");
        var media = PlayerToolPaths.RequireMediaInput(input.MediaPath);
        var output = PlayerToolPaths.RequireOutput(input.OutputPath);
        var inputs = new List<string> { media };
        var args = new List<string>();
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        string executable;
        switch (action)
        {
            case PlayerToolAction.SynchronizeSubtitle:
                var subtitle = PlayerToolPaths.RequireInput(input.SubtitlePath);
                RequireExtension(subtitle, ".srt", ".ass", ".ssa");
                RequireExtension(output, Path.GetExtension(subtitle));
                inputs.Add(subtitle);
                var subtitleReference = Path.GetExtension(media).ToLowerInvariant() is ".srt" or ".ass" or ".ssa";
                string? ffmpeg = null;
                string? ffprobe = null;
                if (!subtitleReference)
                {
                    ffmpeg = PlayerToolPaths.RequireInput(settings.FfmpegPath);
                    ffprobe = PlayerToolPaths.RequireInput(PlayerToolPaths.ResolveFfprobe(settings));
                    if (!PlayerToolPaths.IsExecutableAvailable(ffmpeg) || !PlayerToolPaths.IsExecutableAvailable(ffprobe))
                        throw new PlayerToolException("按音轨同步字幕需要配置 FFmpeg 和 FFprobe。");
                }
                if (input.SyncEngine == PlayerSubtitleSyncEngine.Ffsubsync)
                {
                    executable = settings.FfsubsyncPath;
                    args.AddRange([media, "-i", subtitle, "-o", output]);
                    if (ffmpeg is not null && ffprobe is not null)
                    {
                        if (!string.Equals(Path.GetDirectoryName(ffmpeg), Path.GetDirectoryName(ffprobe), StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(Path.GetFileName(ffmpeg), "ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(Path.GetFileName(ffprobe), "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                            throw new PlayerToolException("ffsubsync 需要同一文件夹中的 ffmpeg.exe 和 ffprobe.exe。");
                        args.AddRange(["--ffmpeg-path", Path.GetDirectoryName(ffmpeg)!]);
                    }
                    if (input.AudioStreamIndex is { } audio)
                    {
                        RequireIndex(audio);
                        args.AddRange(["--reference-stream", "0:" + audio.ToString(CultureInfo.InvariantCulture)]);
                    }
                }
                else
                {
                    executable = settings.AlassPath;
                    args.AddRange([media, subtitle, output]);
                    if (ffmpeg is not null && ffprobe is not null)
                    {
                        environment["ALASS_FFMPEG_PATH"] = ffmpeg;
                        environment["ALASS_FFPROBE_PATH"] = ffprobe;
                    }
                }
                break;
            case PlayerToolAction.GenerateSubtitle:
                RequireExtension(output, ".srt");
                if (!PlayerToolsSettings.WhisperModels.Contains(settings.WhisperModel, StringComparer.Ordinal) ||
                    !PlayerToolsSettings.WhisperDevices.Contains(settings.WhisperDevice, StringComparer.Ordinal) ||
                    settings.WhisperLanguage.Length > 16 || settings.WhisperLanguage.Any(c => !char.IsAsciiLetter(c) && c != '-'))
                    throw new PlayerToolException("AI 字幕模型、设备或语言设置无效。");
                executable = settings.FasterWhisperPath;
                output = PlayerToolPaths.RequireOutput(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(media) + ".srt"));
                // Purfview whisper-standalone-win 协议；上游 sub-fastwhisper.lua:697–719。
                args.AddRange([media, "--beep_off", "--model", settings.WhisperModel, "--device", settings.WhisperDevice,
                    "--max_line_width", "100", "--threads", "0", "--output_dir", Path.GetDirectoryName(output)!]);
                if (settings.WhisperLanguage.Length > 0) args.AddRange(["--language", settings.WhisperLanguage]);
                break;
            default:
                executable = settings.FfmpegPath;
                args.AddRange(["-hide_banner", "-nostdin", "-loglevel", "error", "-n"]);
                // 本地副本不应让格式探测加载网络资源或相邻播放列表。
                args.AddRange(["-protocol_whitelist", "file,pipe"]);
                if (action is PlayerToolAction.ExportClip or PlayerToolAction.ExportGif or PlayerToolAction.ExportGifWithSubtitles)
                {
                    if (!double.IsFinite(input.StartSeconds) || input.StartSeconds < 0 || input.EndSeconds is not { } end ||
                        !double.IsFinite(end) || end <= input.StartSeconds)
                        throw new PlayerToolException("请先标记有效的起始时间和结束时间。");
                    args.AddRange(["-ss", Number(input.StartSeconds), "-i", media, "-t", Number(end - input.StartSeconds)]);
                }
                else args.AddRange(["-i", media]);
                if (action == PlayerToolAction.ExportClip)
                {
                    RequireExtension(output, ".mkv");
                    args.AddRange(["-map", Stream(input.VideoStreamIndex, "0:v:0")]);
                    if (input.IncludeAudio) args.AddRange(["-map", Stream(input.AudioStreamIndex, "0:a:0?")]);
                    args.AddRange(["-c", "copy", "-avoid_negative_ts", "make_zero", output]);
                }
                else if (action == PlayerToolAction.ExportSubtitle)
                {
                    RequireExtension(output, ".srt", ".ass", ".sup");
                    if (input.SubtitleStreamIndex is not { } index) throw new PlayerToolException("当前内封字幕轨道未知。");
                    RequireIndex(index);
                    var codec = Path.GetExtension(output).ToLowerInvariant() switch { ".ass" => "ass", ".sup" => "copy", _ => "srt" };
                    args.AddRange(["-map", "0:" + index.ToString(CultureInfo.InvariantCulture), "-c:s", codec, output]);
                }
                else
                {
                    RequireExtension(output, ".gif");
                    var filter = "fps=15,scale=640:-2:flags=lanczos";
                    if (action == PlayerToolAction.ExportGifWithSubtitles)
                    {
                        var sub = PlayerToolPaths.RequireInput(input.SubtitlePath);
                        RequireExtension(sub, ".srt", ".ass", ".ssa");
                        inputs.Add(sub);
                        // Shift a separate text subtitle to the clipped timeline before rendering.
                        filter = "setpts=PTS+" + Number(input.StartSeconds) + "/TB,subtitles=filename='" + EscapeFilterPath(sub) +
                            "',setpts=PTS-STARTPTS," + filter;
                    }
                    args.AddRange(["-an", "-sn", "-filter_complex",
                        "[" + Stream(input.VideoStreamIndex, "0:v:0") + "]" + filter +
                        ",split[a][b];[a]palettegen[p];[b][p]paletteuse[gif]", "-map", "[gif]", "-loop", "0", output]);
                }
                break;
        }
        if (!PlayerToolPaths.IsExecutableAvailable(executable)) throw new PlayerToolException("请先配置存在的本地播放工具程序。");
        return new(PlayerToolPaths.RequireInput(executable), args.AsReadOnly(), output, inputs.AsReadOnly(),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(environment));
    }

    private static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
    private static string Stream(int? index, string fallback)
    {
        if (index is not { } value) return fallback;
        RequireIndex(value);
        return "0:" + value.ToString(CultureInfo.InvariantCulture);
    }
    private static void RequireIndex(int index)
    {
        if (index < 0 || index > 10_000) throw new PlayerToolException("播放轨道索引无效。");
    }
    private static void RequireExtension(string path, params string[] extensions)
    {
        if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new PlayerToolException("播放工具不支持此输入或输出格式。");
    }
    private static string EscapeFilterPath(string path) => path.Replace("\\", "/", StringComparison.Ordinal)
        .Replace("'", "'\\''", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal);
}

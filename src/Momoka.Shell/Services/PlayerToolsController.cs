using System.Text.Json;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Translation;

namespace Momoka.Shell.Services;

/// <summary>每次工具任务只读捕获的影片；子进程只收到准备好的本地文件。</summary>
public sealed class PlayerToolsController(PlaybackService playback, ISettingsService settings, AppPaths paths,
    SubtitleTranslationClient translation, SubtitleTranslationCredentials credentials) : IDisposable
{
    private CancellationTokenSource? _running;
    private bool _disposed;
    public bool Busy => _running is not null;
    public string Status { get; private set; } = "";
    public event Action? Changed;

    public void Cancel() => _running?.Cancel();

    public async Task<string?> RunAsync(string action, MpvTrack? subtitle, MpvTrack? audio, MpvTrack? video,
        double start, double? end)
    {
        if (_disposed || Busy || playback.CapturePlayerToolSession() is not { } session) return null;
        var toolSettings = JsonSerializer.Deserialize<PlayerToolsSettings>(JsonSerializer.Serialize(settings.Settings.PlayerTools))!;
        var translationSettings = JsonSerializer.Deserialize<SubtitleTranslationSettings>(JsonSerializer.Serialize(settings.Settings.SubtitleTranslation))!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime);
        _running = cancellation;
        var token = cancellation.Token;
        var workspace = Path.Combine(paths.Root, "cache", "player-tools", Guid.NewGuid().ToString("N"));
        void CheckCurrent() { token.ThrowIfCancellationRequested(); if (!session.IsCurrent) throw new OperationCanceledException(token); }
        void OnPlayback(PlaybackUpdate<Momoka.Emby.EmbyItem?> _) { if (!session.IsCurrent) cancellation.Cancel(); }
        playback.NowPlayingUpdated += OnPlayback;
        try
        {
            CheckCurrent();
            if (!PlayerToolPaths.IsExecutableAvailable(toolSettings.FfmpegPath)) throw new PlayerToolException("请先配置可用的 FFmpeg。");
            if (action is "tool-clip" or "tool-gif" or "tool-gif-subtitles"
                && (!double.IsFinite(start) || start < 0 || end is not { } stop || !double.IsFinite(stop) || stop <= start))
                throw new PlayerToolException("请先标记有效的开始和结束时间。");
            if (action is "tool-subtitle-export" or "tool-subtitle-translate" or "tool-subtitle-sync" or "tool-subtitle-sync-alass" or "tool-gif-subtitles"
                && (subtitle is null || subtitle.Image || subtitle.FfmpegIndex is null && string.IsNullOrWhiteSpace(subtitle.ExternalFilename)))
                throw new PlayerToolException("请选择可读取的文字字幕。");
            if (action is "tool-subtitle-sync" or "tool-subtitle-sync-alass" or "tool-subtitle-generate"
                && audio?.FfmpegIndex is null) throw new PlayerToolException("请选择可读取的音轨。");
            if (action == "tool-subtitle-generate" && !PlayerToolPaths.IsExecutableAvailable(toolSettings.FasterWhisperPath))
                throw new PlayerToolException("请先配置可用的语音识别程序。");
            if (action is "tool-subtitle-sync" or "tool-subtitle-sync-alass")
            {
                var program = action == "tool-subtitle-sync" ? toolSettings.FfsubsyncPath : toolSettings.AlassPath;
                if (!PlayerToolPaths.IsExecutableAvailable(program) || !PlayerToolPaths.IsExecutableAvailable(PlayerToolPaths.ResolveFfprobe(toolSettings)))
                    throw new PlayerToolException("字幕同步需要配置同步程序及 FFprobe。");
            }
            if (action == "tool-subtitle-translate" && (!translationSettings.Enabled
                || string.IsNullOrWhiteSpace(credentials.GetApiKey(translationSettings, translationSettings.Provider))))
                throw new PlayerToolException("请先启用并配置字幕翻译服务。");
            Directory.CreateDirectory(workspace);
            var outputDirectory = string.IsNullOrWhiteSpace(toolSettings.OutputDirectory)
                ? AppPaths.ResolveScreenshotDirectory(settings.Settings.Mpv.ScreenshotDirectory, paths.ScreenshotDirectory) ?? paths.ScreenshotDirectory
                : toolSettings.OutputDirectory;
            Directory.CreateDirectory(outputDirectory);
            var suffix = action switch { "tool-clip" => ".mkv", "tool-gif" or "tool-gif-subtitles" => ".gif", _ => ".srt" };
            var output = Path.Combine(outputDirectory, "Momoka-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + suffix);
            PlayerToolPaths.RequireOutput(output);
            var subtitleOnly = action is "tool-subtitle-export" or "tool-subtitle-translate";
            string media;
            if (subtitleOnly && subtitle?.ExternalFilename is { Length: > 0 } external)
            {
                Report("正在准备当前字幕…");
                media = await session.PrepareSubtitleAsync(external, Path.Combine(workspace, "subtitle.media"), token).ConfigureAwait(false);
            }
            else
            {
                Report("正在准备本地媒体副本，可在右键菜单取消…");
                var lastUpdate = 0L;
                var progress = new Progress<(long Bytes, long? Total)>(value =>
                {
                    var now = Environment.TickCount64;
                    if (now - Interlocked.Read(ref lastUpdate) < 500) return;
                    Interlocked.Exchange(ref lastUpdate, now);
                    if (!token.IsCancellationRequested && session.IsCurrent)
                        Report(value.Total is > 0 ? $"准备媒体：{value.Bytes * 100 / value.Total}%"
                            : $"准备媒体：{value.Bytes / 1048576} MB");
                });
                media = await session.PrepareMediaAsync(Path.Combine(workspace, "source.media"), progress, token).ConfigureAwait(false);
            }
            CheckCurrent();
            PlayerToolPaths.RequireMediaInput(media);
            if (subtitle?.ExternalFilename is not { Length: > 0 } && subtitle?.FfmpegIndex is null
                && action is "tool-subtitle-export" or "tool-subtitle-translate" or "tool-subtitle-sync" or "tool-subtitle-sync-alass" or "tool-gif-subtitles")
                throw new PlayerToolException("当前字幕轨道的文件索引不可用，请重新选择字幕。");
            var input = new PlayerToolInput(media, Path.Combine(workspace, "result" + suffix))
            {
                StartSeconds = start,
                EndSeconds = end,
                SubtitleStreamIndex = subtitleOnly && subtitle?.ExternalFilename is { Length: > 0 } ? 0 : subtitle?.FfmpegIndex,
                AudioStreamIndex = audio?.FfmpegIndex,
                VideoStreamIndex = video?.FfmpegIndex
            };
            string? preparedSubtitle = null;
            if (action is "tool-gif-subtitles" or "tool-subtitle-sync" or "tool-subtitle-sync-alass")
            {
                if (subtitle is null || subtitle.Image) throw new InvalidOperationException("请选择文字字幕。");
                preparedSubtitle = Path.Combine(workspace, "captions.srt");
                var subMedia = media;
                var index = subtitle.FfmpegIndex;
                if (subtitle.ExternalFilename is { Length: > 0 } name)
                {
                    subMedia = await session.PrepareSubtitleAsync(name, Path.Combine(workspace, "subtitle.media"), token).ConfigureAwait(false);
                    index = 0;
                }
                Report("正在准备字幕…");
                await PlayerToolRunner.RunAsync(PlayerToolPlan.Create(toolSettings, PlayerToolAction.ExportSubtitle,
                    new PlayerToolInput(subMedia, preparedSubtitle) { SubtitleStreamIndex = index }), token).ConfigureAwait(false);
                input = input with { SubtitlePath = preparedSubtitle };
            }
            CheckCurrent();
            var kind = action switch
            {
                "tool-clip" => PlayerToolAction.ExportClip,
                "tool-gif" => PlayerToolAction.ExportGif,
                "tool-gif-subtitles" => PlayerToolAction.ExportGifWithSubtitles,
                "tool-subtitle-sync" or "tool-subtitle-sync-alass" => PlayerToolAction.SynchronizeSubtitle,
                "tool-subtitle-generate" => PlayerToolAction.GenerateSubtitle,
                _ => PlayerToolAction.ExportSubtitle
            };
            if (action == "tool-subtitle-sync-alass") input = input with { SyncEngine = PlayerSubtitleSyncEngine.Alass };
            if (action is "tool-subtitle-sync" or "tool-subtitle-sync-alass")
            {
                Report("正在提取当前音轨用于字幕同步…");
                var reference = Path.Combine(workspace, "reference.wav");
                await PlayerToolRunner.RunAsync(PlayerToolPlan.PrepareAudio(toolSettings,
                    input with { OutputPath = reference, StartSeconds = 0, EndSeconds = null }), token).ConfigureAwait(false);
                CheckCurrent();
                input = input with { MediaPath = reference, AudioStreamIndex = 0, StartSeconds = 0, EndSeconds = null };
            }
            if (action is "tool-subtitle-generate" or "tool-subtitle-translate")
                input = input with { OutputPath = Path.Combine(workspace, "prepared.srt") };
            if (action == "tool-subtitle-generate")
            {
                Report("正在提取当前音轨…");
                var wave = Path.Combine(workspace, "audio.wav");
                await PlayerToolRunner.RunAsync(PlayerToolPlan.PrepareAudio(toolSettings,
                    input with { OutputPath = wave }), token).ConfigureAwait(false);
                CheckCurrent();
                input = input with { MediaPath = wave, StartSeconds = 0, EndSeconds = null };
            }
            Report(action == "tool-subtitle-generate" ? "正在生成 AI 字幕…" : "正在处理媒体…");
            var result = await PlayerToolRunner.RunAsync(PlayerToolPlan.Create(toolSettings, kind, input), token).ConfigureAwait(false);
            CheckCurrent();
            if (action == "tool-subtitle-translate")
            {
                Report("正在翻译整份字幕…");
                var document = SrtDocument.Parse(await File.ReadAllTextAsync(result.OutputPath, token).ConfigureAwait(false));
                var translated = await document.TranslateAsync(translation, translationSettings,
                    credentials.GetApiKey(translationSettings, translationSettings.Provider), token,
                    new Progress<int>(count => { if (!token.IsCancellationRequested) Report($"正在翻译字幕：{count}%"); })).ConfigureAwait(false);
                CheckCurrent();
                var translatedFile = Path.Combine(workspace, "translated.srt");
                await File.WriteAllTextAsync(translatedFile, translated.ToText(), token).ConfigureAwait(false);
                CheckCurrent();
                File.Move(translatedFile, output);
            }
            else
            {
                if (action == "tool-subtitle-generate" && start > 0)
                {
                    var generated = SrtDocument.Parse(await File.ReadAllTextAsync(result.OutputPath, token).ConfigureAwait(false));
                    await File.WriteAllTextAsync(result.OutputPath, generated.ShiftMilliseconds((long)Math.Round(start * 1000)).ToText(), token).ConfigureAwait(false);
                }
                CheckCurrent();
                File.Move(result.OutputPath, output);
            }
            CheckCurrent();
            Report("已保存：" + output);
            return output;
        }
        catch (OperationCanceledException) { Report("播放工具任务已取消"); return null; }
        catch (Exception error)
        {
            Report(error is PlayerToolException or PlayerToolDownloadException or SubtitleTranslationException ? error.Message : "播放工具未完成，请检查工具配置和输出目录。");
            return null;
        }
        finally
        {
            playback.NowPlayingUpdated -= OnPlayback;
            if (ReferenceEquals(_running, cancellation)) _running = null;
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Changed?.Invoke();
        }
    }

    private void Report(string value) { Status = value; Changed?.Invoke(); }
    public void Dispose() { _disposed = true; Cancel(); }
}

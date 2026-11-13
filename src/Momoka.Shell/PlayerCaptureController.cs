using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Services;
using Momoka.Shell.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell;

/// <summary>Owns the saved-image queue. Playback is only asked for a file-scoped screenshot.</summary>
public sealed class PlayerCaptureController : IDisposable
{
    private readonly PlaybackService _playback;
    private readonly ISettingsService _settings;
    private readonly AppPaths _paths;
    private readonly Platform.IUiDispatcher _ui;
    private readonly Func<PlayerToolPlan, CancellationToken, Task<PlayerToolResult>> _run;
    private readonly List<string> _pending = [];
    private CancellationTokenSource? _operation;
    private long _queueGeneration = -1;
    private long? _queueFileGeneration;
    private long _operationGeneration = -1;
    private long? _operationFileGeneration;
    private int _queueWidth;
    private bool _disposed;
    private PlayerCaptureDialog? _dialog;

    public PlayerCaptureController(PlaybackService playback, ISettingsService settings, AppPaths paths, Platform.IUiDispatcher ui)
        : this(playback, settings, paths, ui, PlayerToolRunner.RunAsync) { }

    internal PlayerCaptureController(PlaybackService playback, ISettingsService settings, AppPaths paths, Platform.IUiDispatcher ui,
        Func<PlayerToolPlan, CancellationToken, Task<PlayerToolResult>> run)
    {
        _playback = playback;
        _settings = settings;
        _paths = paths;
        _ui = ui;
        _run = run;
        playback.NowPlayingUpdated += OnNowPlaying;
    }

    public int PendingCount => _pending.Count;
    public event Action<string, bool>? Noticed;

    public void Cancel()
    {
        _operation?.Cancel();
        _dialog?.Revoke();
        _pending.Clear();
        _queueWidth = 0;
    }

    public async Task<string?> ShowCropAsync(XamlRoot root, bool subtitleRegion)
    {
        if (_disposed || _operation is not null || !_playback.CanControl) return null;
        if (!PlayerToolPaths.IsExecutableAvailable(_settings.Settings.PlayerTools.FfmpegPath))
        {
            Noticed?.Invoke("请先在播放工具中配置 FFmpeg。", true);
            return null;
        }
        var generation = _playback.Generation;
        var fileGeneration = _playback.ActiveFileGeneration;
        _operationGeneration = generation;
        _operationFileGeneration = fileGeneration;
        var cts = _operation = new CancellationTokenSource();
        var temporary = "";
        try
        {
            var directory = Path.Combine(_paths.Root, "cache", "player-captures");
            Directory.CreateDirectory(directory);
            temporary = PlayerToolPaths.RequireOutput(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png"));
            if (!await _playback.CommandForActiveFileAsync(generation, fileGeneration,
                "screenshot-to-file", temporary, "subtitles").ConfigureAwait(true))
                throw new PlayerToolException("截图未能保存，请确认画面已经加载。");
            if (!Current(generation, fileGeneration) || cts.IsCancellationRequested) return null;
            var picture = await LoadPictureAsync(temporary).ConfigureAwait(true);
            if (!Current(generation, fileGeneration) || cts.IsCancellationRequested) return null;
            var dialog = new PlayerCaptureDialog(picture, subtitleRegion,
                crop => SaveCropAsync(temporary, crop, subtitleRegion, generation, fileGeneration, cts.Token))
            { XamlRoot = root };
            _dialog = dialog;
            await dialog.ShowAsync();
            return dialog.SavedPath;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { return null; }
        catch (Exception error)
        {
            if (Current(generation, fileGeneration)) Noticed?.Invoke(Momoka.Diagnostics.Failure.Describe(error), true);
            return null;
        }
        finally
        {
            DeleteOwnedImage(temporary);
            _dialog = null;
            if (ReferenceEquals(_operation, cts)) _operation = null;
            cts.Dispose();
        }
    }

    private async Task<string?> SaveCropAsync(string input, PlayerImageCrop crop, bool subtitleRegion,
        long generation, long? fileGeneration, CancellationToken cancellationToken)
    {
        if (!Current(generation, fileGeneration)) return null;
        if (subtitleRegion && _pending.Count >= PlayerImageTools.MaxStitchImages)
            throw new PlayerToolException("字幕拼图最多保留 100 张，请先生成拼图。");
        if (subtitleRegion && _pending.Count > 0 && _queueWidth != crop.Width)
            throw new PlayerToolException("拼图中的字幕截图宽度需要相同，请使用与前一张相同的裁切宽度。");
        var output = OutputPath(subtitleRegion ? "subtitle" : "crop");
        var plan = PlayerImageTools.Crop(_settings.Settings.PlayerTools, input, output, crop);
        var result = await _run(plan, cancellationToken).ConfigureAwait(true);
        if (!Current(generation, fileGeneration))
        {
            DeleteOwnedImage(result.OutputPath);
            return null;
        }
        if (subtitleRegion)
        {
            _queueGeneration = generation;
            _queueFileGeneration = fileGeneration;
            _queueWidth = crop.Width;
            _pending.Add(result.OutputPath);
            Noticed?.Invoke($"字幕区域已保存并加入拼图，共 {_pending.Count} 张。", false);
        }
        else Noticed?.Invoke("裁切截图已保存：" + result.OutputPath, false);
        return result.OutputPath;
    }

    public async Task<string?> StitchAsync()
    {
        if (_disposed || _operation is not null) return null;
        if (_pending.Count < 2)
        {
            Noticed?.Invoke("先截取至少两张字幕区域，再生成拼图。", true);
            return null;
        }
        var generation = _queueGeneration;
        var fileGeneration = _queueFileGeneration;
        var inputs = _pending.ToArray();
        _operationGeneration = generation;
        _operationFileGeneration = fileGeneration;
        var cts = _operation = new CancellationTokenSource();
        try
        {
            if (!Current(generation, fileGeneration)) return null;
            var plan = PlayerImageTools.Stitch(_settings.Settings.PlayerTools, inputs, OutputPath("subtitle-stitch"));
            var result = await _run(plan, cts.Token).ConfigureAwait(true);
            if (!Current(generation, fileGeneration))
            {
                DeleteOwnedImage(result.OutputPath);
                return null;
            }
            _pending.Clear();
            _queueWidth = 0;
            Noticed?.Invoke("字幕拼图已保存：" + result.OutputPath, false);
            return result.OutputPath;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { return null; }
        catch (Exception error)
        {
            if (Current(generation, fileGeneration)) Noticed?.Invoke(Momoka.Diagnostics.Failure.Describe(error), true);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_operation, cts)) _operation = null;
            cts.Dispose();
        }
    }

    private string OutputPath(string prefix)
    {
        var settings = _settings.Settings;
        var directory = string.IsNullOrWhiteSpace(settings.PlayerTools.OutputDirectory)
            ? AppPaths.ResolveScreenshotDirectory(settings.Mpv.ScreenshotDirectory, _paths.ScreenshotDirectory)
            : settings.PlayerTools.OutputDirectory;
        return PlayerToolPaths.RequireOutput(Path.Combine(directory ?? "", $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png"));
    }

    internal static async Task<BitmapImage> LoadPictureAsync(string path)
    {
        var input = PlayerToolPaths.RequireInput(path);
        if (new FileInfo(input).Length is <= 0 or > 64 * 1024 * 1024) throw new PlayerToolException("截图文件无效或过大。");
        return await PosterLoader.DecodeAsync(await File.ReadAllBytesAsync(input).ConfigureAwait(true), 1200).ConfigureAwait(true)
            ?? throw new PlayerToolException("截图无法解码。");
    }

    private bool Current(long generation, long? fileGeneration) => !_disposed && _playback.CanControl
        && generation == _playback.Generation && fileGeneration == _playback.ActiveFileGeneration;

    private void OnNowPlaying(PlaybackUpdate<Momoka.Emby.EmbyItem?> update) => _ui.Run(() =>
    {
        if (_disposed || update.Generation != _playback.Generation) return;
        if (update.Generation != _operationGeneration || _playback.ActiveFileGeneration != _operationFileGeneration || update.Value is null)
        {
            _operation?.Cancel();
            _dialog?.Revoke();
        }
        if (update.Generation != _queueGeneration || _playback.ActiveFileGeneration != _queueFileGeneration || update.Value is null)
        {
            _pending.Clear();
            _queueWidth = 0;
        }
    });

    private static void DeleteOwnedImage(string path)
    {
        if (path.Length == 0) return;
        try { File.Delete(PlayerToolPaths.RequireInput(path)); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playback.NowPlayingUpdated -= OnNowPlaying;
        _operation?.Cancel();
        _pending.Clear();
    }
}

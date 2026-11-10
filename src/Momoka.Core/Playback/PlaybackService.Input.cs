using Momoka.Diagnostics;

namespace Momoka.Playback;

public sealed partial class PlaybackService
{
    public async Task<bool> CommandForActiveFileAsync(long generation, long? fileGeneration, params string[] arguments)
    {
        if (arguments.Length == 0) return false;
        var handle = _current;
        if (generation != Generation || handle is not IPlayerControl control || !handle.HasControlChannel) return false;
        var scoped = handle as IFileScopedPlayerControl;
        if (scoped is not null && (fileGeneration is null || scoped.FileGeneration != fileGeneration)) return false;
        if (_active is { } attempt && (arguments[0] is "sub-add" or "sub-remove"
            || arguments.Length > 1 && arguments[1] is "sid" or "sub-visibility"))
            Interlocked.Exchange(ref attempt.ExternalSubtitleSelection, 1);
        try
        {
            if (!IsCurrent(handle, generation)) return false;
            return scoped is null
                ? await control.CommandAsync(arguments, CancellationToken.None).ConfigureAwait(false)
                : await scoped.CommandForFileAsync(fileGeneration!.Value, arguments, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Log.Warn("播放器", "当前文件按键未执行", error);
            return false;
        }
    }
}

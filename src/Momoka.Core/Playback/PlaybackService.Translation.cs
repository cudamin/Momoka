using Momoka.Mpv;
using Momoka.Translation;

namespace Momoka.Playback;

public sealed partial class PlaybackService
{
    /// <summary>固定当前服务代次和原生文件代次；迟到译文不能跟随同窗下一集。</summary>
    public ISubtitleTranslationPlaybackSession? CaptureSubtitleTranslationSession()
    {
        var attempt = _active;
        var handle = _current;
        if (attempt is null || handle is not IPlayerControl control || !handle.HasControlChannel
            || !ReferenceEquals(attempt.Handle, handle) || !IsCurrent(attempt) || !attempt.Scope.IsAvailable)
            return null;
        var scoped = control as IFileScopedPlayerControl;
        return new TranslationSession(this, attempt, handle, control, scoped, scoped?.FileGeneration);
    }

    private sealed class TranslationSession(PlaybackService owner, PlayingAttempt attempt,
        IPlaybackHandle handle, IPlayerControl control, IFileScopedPlayerControl? scoped, long? fileGeneration)
        : ISubtitleTranslationPlaybackSession
    {
        public long Generation => attempt.Generation;
        public CancellationToken Lifetime => attempt.Scope.Lifetime;
        public bool IsCurrent => owner.IsCurrent(attempt) && owner.IsCurrent(handle, Generation)
            && attempt.Scope.IsAvailable && (scoped is null || scoped.FileGeneration == fileGeneration);

        public async Task<bool> EnsureOverlayAsync(string scriptPath, CancellationToken cancellationToken)
        {
            if (!IsCurrent || !File.Exists(scriptPath)) return false;
            if (await NumberAsync(MpvSubtitleTranslation.ReadyProperty, cancellationToken).ConfigureAwait(false) == 1)
                return IsCurrent;
            if (!await CommandAsync(["load-script", scriptPath], cancellationToken).ConfigureAwait(false)) return false;
            // Lua loading is asynchronous even though mpv accepted load-script.
            for (var index = 0; index < 20 && IsCurrent; index++)
            {
                await Task.Delay(75, cancellationToken).ConfigureAwait(false);
                if (await NumberAsync(MpvSubtitleTranslation.ReadyProperty, cancellationToken).ConfigureAwait(false) == 1)
                    return IsCurrent;
            }
            return false;
        }

        public async Task<SubtitleTranslationCue?> ReadCueAsync(CancellationToken cancellationToken)
        {
            // Lua publishes one atomic cue snapshot. The external backend needs one IPC trip, not seven
            // separate reads that could cross a subtitle boundary while the server is replying.
            var json = await TextAsync(MpvSubtitleTranslation.CueProperty, cancellationToken).ConfigureAwait(false);
            return IsCurrent ? SubtitleTranslationCue.FromNativeJson(json) : null;
        }

        public Task<bool> ShowAsync(SubtitleTranslationCue cue, string translated, int fontSize, int position,
            CancellationToken cancellationToken) => CommandAsync(
                ["script-message-to", MpvSubtitleTranslation.ScriptName, MpvSubtitleTranslation.Show,
                    MpvSubtitleTranslation.Payload(cue, translated, fontSize, position)], cancellationToken);

        public async Task ClearAsync(CancellationToken cancellationToken) =>
            _ = await CommandAsync(["script-message-to", MpvSubtitleTranslation.ScriptName, MpvSubtitleTranslation.Clear],
                cancellationToken).ConfigureAwait(false);

        private async Task<string?> TextAsync(string name, CancellationToken cancellationToken)
        {
            if (!IsCurrent) return null;
            var value = await handle.GetTextAsync(name, cancellationToken).ConfigureAwait(false);
            return IsCurrent ? value : null;
        }

        private async Task<double?> NumberAsync(string name, CancellationToken cancellationToken)
        {
            if (!IsCurrent) return null;
            var value = await handle.GetNumberAsync(name, cancellationToken).ConfigureAwait(false);
            return IsCurrent ? value : null;
        }

        private async Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (!IsCurrent) return false;
            try
            {
                var accepted = scoped is null
                    ? await control.CommandAsync(arguments, cancellationToken).ConfigureAwait(false)
                    : await scoped.CommandForFileAsync(fileGeneration!.Value, arguments, cancellationToken).ConfigureAwait(false);
                return accepted && IsCurrent;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            // Never log Lua payloads: they contain private subtitle text.
            catch (Exception) { return false; }
        }
    }
}

using System.Text.Json;
using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private string? _nativeShortcutsSent;
    private int _nativeShortcutRequest;

    private void NativeShortcutsChanged() => _ = PushNativeShortcutsAsync();

    private async Task PushNativeShortcutsAsync(bool force = false)
    {
        if (!HeadlessPlayback || !_playback.IsPlaying) return;
        var json = JsonSerializer.Serialize(VideoWindowShortcuts.Bindings(ShortcutBindings));
        if (!force && json == _nativeShortcutsSent) return;
        var context = CaptureInteraction();
        var request = ++_nativeShortcutRequest;
        if (await _playback.CommandAsync("script-message", VideoWindowContract.Shortcuts, json).ConfigureAwait(true)
            && IsCurrentInteraction(context) && request == _nativeShortcutRequest)
            _nativeShortcutsSent = json;
    }

    private void RunNativeShortcut(string action)
    {
        if (!HeadlessPlayback || !_playback.CanControl) return;
        switch (action)
        {
            case "toggle-pause": TogglePause(); break;
            case "seek-backward": SeekBackward(); break;
            case "seek-forward": SeekForward(); break;
            case "seek-backward-long": SeekBackwardLong(); break;
            case "seek-forward-long": SeekForwardLong(); break;
            case "volume-up": _ = _playback.CommandAsync("no-osd", "add", "volume", "5"); break;
            case "volume-down": _ = _playback.CommandAsync("no-osd", "add", "volume", "-5"); break;
            case "toggle-fullscreen": ToggleNativeFullscreen(); break;
            case "toggle-mute": ToggleMute(); break;
            case "previous-episode": PreviousEpisode(); break;
            case "next-episode": NextEpisode(); break;
            case "toggle-pin": _ = _playback.CommandAsync("cycle", "ontop"); break;
            case "chapter-previous": StepChapter(-1); break;
            case "chapter-next": StepChapter(1); break;
            case "speed-down": NudgeSpeed(-0.1); break;
            case "speed-up": NudgeSpeed(0.1); break;
            case "speed-reset": SetSpeed(1); break;
            case "subtitle-delay-decrease": NudgeDelay(true, -0.1); break;
            case "subtitle-delay-increase": NudgeDelay(true, 0.1); break;
            case "audio-delay-decrease": NudgeDelay(false, -0.1); break;
            case "audio-delay-increase": NudgeDelay(false, 0.1); break;
            default: RunAdditionalShortcut(action); break;
        }
    }

    internal bool RunAdditionalShortcut(string action)
    {
        if (!_playback.CanControl) return false;
        switch (action)
        {
            case "show-playback-info": MediaInfoRequested?.Invoke(); return true;
            case "toggle-danmaku": ToggleDanmaku(); return true;
            case "danmaku-delay-decrease": NudgeDanmakuDelay(-0.5); return true;
            case "danmaku-delay-increase": NudgeDanmakuDelay(0.5); return true;
        }
        if (AdditionalShortcuts.Command(action) is not { } command) return false;
        var context = CaptureInteraction();
        _ = QueuePictureCommand(async () =>
        {
            if (!IsCurrentPlayback(context)) return;
            var done = await _playback.CommandAsync([.. command]).ConfigureAwait(true);
            if (!IsCurrentPlayback(context)) return;
            if (action.StartsWith("screenshot", StringComparison.Ordinal))
                await _playback.CommandAsync("show-text", done ? "截图已保存到 ${screenshot-directory}" : "截图保存失败，请查看日志", "2000").ConfigureAwait(true);
        });
        return true;
    }
}

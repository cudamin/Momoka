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
            case "seek-backward": NoteNativeSeek("seek-backward"); break;
            case "seek-forward": NoteNativeSeek("seek-forward"); break;
            case "seek-backward-long": SeekBackwardLong(); break;
            case "seek-forward-long": SeekForwardLong(); break;
            case "volume-up": _ = AdjustNativeVolumeAsync("5"); break;
            case "volume-down": _ = AdjustNativeVolumeAsync("-5"); break;
            case "toggle-fullscreen": ToggleNativeFullscreen(); break;
            case "toggle-mute": _ = ToggleNativeMuteAsync(); break;
            case "previous-episode": PreviousEpisode(); break;
            case "next-episode": NextEpisode(); break;
            case "toggle-pin": _ = _playback.CommandAsync("cycle", "ontop"); break;
            case PlayerMenuCatalog.BackgroundClickShortcut: ToggleActivationClickPlayback(); break;
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

    private async Task AdjustNativeVolumeAsync(string delta)
    {
        var context = CaptureInteraction();
        if (!await _playback.CommandAsync("no-osd", "add", "volume", delta).ConfigureAwait(true)
            || !IsCurrentInteraction(context)) return;
        await _playback.CommandAsync("script-message", "flash-elements", "volume").ConfigureAwait(true);
    }

    private async Task ToggleNativeMuteAsync()
    {
        var context = CaptureInteraction();
        await _playback.SetPropertyAsync("mute", !Status.Muted).ConfigureAwait(true);
        if (!IsCurrentInteraction(context)) return;
        await _playback.CommandAsync("script-message", "flash-elements", "volume").ConfigureAwait(true);
    }

    private Dictionary<string, Action>? _additionalShortcutHandlers;

    private Dictionary<string, Action> AdditionalShortcutHandlers => _additionalShortcutHandlers ??= new(StringComparer.Ordinal)
    {
        ["show-playback-info"] = () => MediaInfoRequested?.Invoke(),
        ["toggle-danmaku"] = ToggleDanmaku,
        ["danmaku-delay-decrease"] = () => NudgeDanmakuDelay(-0.5),
        ["danmaku-delay-increase"] = () => NudgeDanmakuDelay(0.5),
        ["danmaku-delay-reset"] = () => NudgeDanmakuDelay(-_danmakuDelay),
        ["subtitle-delay-reset"] = () => ResetDelay(subtitle: true),
        ["audio-delay-reset"] = () => ResetDelay(subtitle: false),
        ["speed-half"] = () => SetSpeed(0.5),
        ["speed-one-and-half"] = () => SetSpeed(1.5),
        ["speed-double"] = () => SetSpeed(2)
    };

    /// <summary>自检只检查处理器在不在，不执行动作，也不接触播放后端。</summary>
    internal bool HandlesAdditionalShortcut(string action) =>
        AdditionalShortcutHandlers.ContainsKey(action) || PlayerToolShortcuts.Contains(action) || AdditionalShortcuts.Plan(action) is not null;

    internal bool RunAdditionalShortcut(string action)
    {
        if (!_playback.CanControl) return false;
        if (PlayerToolShortcuts.Contains(action))
        {
            var row = PlayerMenuCatalog.Flatten(BuildCurrentMenu()).FirstOrDefault(node => node.HostAction == action);
            if (row is not null) _ = RunMenuNodeAsync(row);
            return true;
        }
        if (AdditionalShortcutHandlers.TryGetValue(action, out var run))
        {
            run();
            return true;
        }
        if (AdditionalShortcuts.Plan(action) is not { } plan) return false;
        // 在按键这一刻捕获身份；RunMenuNodeAsync 的队列会在每一条命令前复核，防止复位落到下一集。
        _ = RunMenuNodeAsync(plan);
        return true;
    }
}

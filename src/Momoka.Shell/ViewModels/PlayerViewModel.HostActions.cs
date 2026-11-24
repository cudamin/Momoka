using Momoka.Mpv;
using Momoka.Playback;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private readonly Momoka.Services.IServerCapabilities? _playerCapabilities;
    internal event Action<string>? PlayerDialogRequested;

    internal bool CanDownloadPlayingSubtitles
    {
        get
        {
            if (_playbackScope is not { IsAvailable: true } scope || _nowPlaying is null || PlayingSource is null
                || scope.Connection.ServerKind is not (Momoka.Emby.MediaServerKind.Emby or Momoka.Emby.MediaServerKind.Jellyfin)) return false;
            var permissions = _playerCapabilities?.PermissionsFor(scope.Key)
                ?? new Momoka.Emby.ItemMenuPermissions(scope.IsAdministrator, CanManageSubtitles: scope.Connection.CanManageSubtitles);
            return Momoka.Emby.ItemMenu.Allows(Momoka.Emby.ItemCommand.Subtitles, permissions);
        }
    }

    internal (Momoka.Emby.EmbySessionScope Scope, Momoka.Emby.EmbyItem Item, Momoka.Emby.MediaSource Source)?
        CaptureSubtitleManager() =>
        CanDownloadPlayingSubtitles ? (_playbackScope!, _nowPlaying!, PlayingSource!) : null;

    private async Task RunHostMenuActionAsync(string action, InteractionContext context)
    {
        if (!IsCurrentInteraction(context)) return;
        if (action.StartsWith("chapter-", StringComparison.Ordinal))
        {
            await RunChapterActionAsync(action, context).ConfigureAwait(true);
            return;
        }
        switch (action)
        {
            case "previous-episode": PreviousEpisode(); return;
            case "next-episode": NextEpisode(); return;
            // 「导航」里的后台单击开关：与快捷键 B、uosc 菜单点中走同一句，写完即推给独占窗口。
            case PlayerMenuCatalog.ActivationClickAction: ToggleActivationClickPlayback(); return;
            case "video-queue-next": RequestQueue(next: true); return;
            case "video-queue-append": RequestQueue(next: false); return;
            case "video-queue-manage": RequestQueueManagement(); return;
            case "video-queue-clear":
                if (CanArrangeQueue && HasPendingQueue) ClearQueue();
                return;
            case "subtitle-download":
            case "subtitle-content": PlayerDialogRequested?.Invoke(action); return;
        }
        if (AdditionalShortcutHandlers.TryGetValue(action, out var run)) run();
        else RunNativeShortcut(action);
    }

    internal async Task<string> ReadCurrentSubtitleTextAsync()
    {
        var context = CaptureInteraction();
        var text = await _playback.GetTextAsync("sub-text").ConfigureAwait(true);
        return IsCurrentInteraction(context) ? text ?? "" : "";
    }

    private static PlayerMenuNode HostRow(string label, string action, string? shortcut = null) =>
        new(PlayerMenuKind.Command, label, [], "", []) { HostAction = action, Shortcut = shortcut };

    private static PlayerMenuNode DynamicGroup(string label, IEnumerable<PlayerMenuNode> children) =>
        new(PlayerMenuKind.Group, label, [], "", children.ToArray());

    private static PlayerMenuNode DynamicCommand(string label, string property, string value, string notice = "") =>
        new(PlayerMenuKind.Command, label, [["set", property, value]], notice, [])
        { State = new(property, PlayerMenuMatch.Equals, value, Radio: true) };
}

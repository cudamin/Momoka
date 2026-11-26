using Momoka.Emby;
using Momoka.Mpv;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private int _interactionVersion;

    internal readonly record struct InteractionContext(int Attempt, int Generation, long PlaybackGeneration, EmbyItem? Item, int Version);

    internal InteractionContext CaptureInteraction() => new(_playbackAttempt, _generation, _playback.Generation, _nowPlaying, _interactionVersion);

    internal bool IsCurrentInteraction(InteractionContext context) => !_lifetime.IsCancellationRequested
        && context.Version == _interactionVersion && context.Attempt == _playbackAttempt && context.Generation == _generation
        && context.PlaybackGeneration == _playback.Generation && ReferenceEquals(context.Item, _nowPlaying);

    /// <summary>
    /// 同一场播放还在不在 —— 与 <see cref="IsCurrentInteraction"/> 只差「菜单那一版」那一位。
    /// <para>
    /// 用于忽略菜单刷新但仍依赖当前条目的操作。窗口切换的暂停责任使用
    /// <see cref="FreezeContext"/>，因为首次 NowPlaying 更新时被暂停的文件可能已经在播。
    /// </para>
    /// </summary>
    internal bool IsCurrentPlayback(InteractionContext context) => !_lifetime.IsCancellationRequested
        && context.Attempt == _playbackAttempt && context.Generation == _generation
        && context.PlaybackGeneration == _playback.Generation && ReferenceEquals(context.Item, _nowPlaying);

    internal readonly record struct FreezeContext(long Generation, long? FileGeneration, bool Playing);
    internal FreezeContext CaptureFreeze() => new(_playback.Generation, _playback.ActiveFileGeneration, _playback.IsPlaying);
    internal bool IsCurrentFreeze(FreezeContext context) => !_lifetime.IsCancellationRequested
        && context.Playing && _playback.CanControl && CaptureFreeze() == context;

    internal Task<bool> SetFrozenPausedAsync(FreezeContext context, bool paused)
    {
        if (!IsCurrentFreeze(context)) return Task.FromResult(false);
        return _playback.CommandForActiveFileAsync(context.Generation, context.FileGeneration, "set", "pause", paused ? "yes" : "no");
    }

    internal async Task<bool?> ScrollStatsAsync(InteractionContext context, long? fileGeneration, bool up)
    {
        if (!IsCurrentInteraction(context)) return null;
        var scrollable = await _playback.GetTextAsync("user-data/momoka-stats/scrollable").ConfigureAwait(true);
        if (!IsCurrentInteraction(context) || fileGeneration != _playback.ActiveFileGeneration) return null;
        if (scrollable is not ("yes" or "true")) return false;
        await _playback.CommandForActiveFileAsync(context.PlaybackGeneration, fileGeneration,
            "script-message-to", "stats", "momoka-scroll", up ? "up" : "down").ConfigureAwait(true);
        return true;
    }

    private VideoMenuSnapshot<EmbyItem>? _episodeMenuSnapshot;
    private InteractionContext _episodeMenuContext;
    private VideoMenuSnapshot<MediaSource>? _versionMenuSnapshot;
    private InteractionContext _versionMenuContext;
    private VideoMenuSnapshot<PlayerMenuNode>? _pictureMenuSnapshot;
    private InteractionContext _pictureMenuContext;
    private int _pictureMenuRequest;

    private void InvalidateMenuInteractions()
    {
        _interactionVersion++;
        _episodeMenuSnapshot = null;
        _versionMenuSnapshot = null;
        _pictureMenuSnapshot = null;
        _pictureMenuRequest++;
        _menuReadings = new Dictionary<string, string?>();
    }

    private void SelectEpisodeMenu(string value)
    {
        if (_episodeMenuSnapshot is not { } menu || !IsCurrentInteraction(_episodeMenuContext)
            || !menu.TryResolve(value, out var episode)) return;
        _episodeMenuSnapshot = null;
        SwitchEpisode(episode);
    }

    private void SelectVersionMenu(string value)
    {
        if (_versionMenuSnapshot is not { } menu || !IsCurrentInteraction(_versionMenuContext)
            || !menu.TryResolve(value, out var source)) return;
        _versionMenuSnapshot = null;
        SwitchVersion(source);
    }

    private void SelectPictureMenu(string value)
    {
        if (_pictureMenuSnapshot is not { } menu || !IsCurrentInteraction(_pictureMenuContext)
            || !menu.TryResolve(value, out var node)) return;
        _ = RunMenuNodeAsync(node);
    }
}

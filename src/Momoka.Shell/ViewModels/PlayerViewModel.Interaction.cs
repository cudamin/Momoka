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
    /// 窗口切换的暂停账问的是「<b>哪一部片子</b>被我冻住了」，不是「<b>哪一份菜单</b>还开着」。菜单版本在
    /// 换片、停止，以及每一条 now-playing 更新（<see cref="OnNowPlayingChanged"/>）时都会涨，拿它当播放身份
    /// 会让一笔正在走的切换在半路被判定失效 —— 画面那一半停下来是对的（那份菜单确实过期了），暂停那一半
    /// 却因此没人放开，片子就停在暂停上（2026-10-22 那一晚的账）。
    /// </para>
    /// <para>
    /// 反过来的保护照旧：换了片、停了播，这一问答假，于是我们不会替新的那一场发 <c>pause=no</c>。
    /// </para>
    /// </summary>
    internal bool IsCurrentPlayback(InteractionContext context) => !_lifetime.IsCancellationRequested
        && context.Attempt == _playbackAttempt && context.Generation == _generation
        && context.PlaybackGeneration == _playback.Generation && ReferenceEquals(context.Item, _nowPlaying);

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

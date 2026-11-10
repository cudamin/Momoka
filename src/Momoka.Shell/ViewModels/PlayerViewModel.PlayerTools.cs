using System.Globalization;
using Microsoft.UI.Xaml.Controls;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.Services;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private readonly PlayerToolsController? _playerTools;
    private readonly PlayerCaptureController? _playerCapture;
    private readonly Momoka.Services.IServerCapabilities? _playerCapabilities;
    private double? _toolStart;
    private double? _toolEnd;
    private long _toolMarkerGeneration = -1;
    private string _lastToolStatus = "";
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

    internal (Momoka.Emby.EmbySessionScope Scope, Momoka.Emby.EmbyItem Item, Momoka.Emby.MediaSource Source)? CaptureSubtitleManager() =>
        CanDownloadPlayingSubtitles ? (_playbackScope!, _nowPlaying!, PlayingSource!) : null;

    private void OnPlayerToolsChanged() => OnUi(() =>
    {
        if (_lifetime.IsCancellationRequested || _playerTools is null || _lastToolStatus == _playerTools.Status) return;
        _lastToolStatus = _playerTools.Status;
        Noticed?.Invoke(_lastToolStatus, InfoBarSeverity.Informational);
        if (HeadlessPlayback && _playback.CanControl)
            _ = _playback.CommandAsync("show-text", _lastToolStatus, "3000");
    });

    private async Task RunHostMenuActionAsync(string action, InteractionContext context)
    {
        if (!IsCurrentInteraction(context)) return;
        if (action.StartsWith("chapter-", StringComparison.Ordinal))
        {
            await RunChapterActionAsync(action, context).ConfigureAwait(true);
            return;
        }
        if (action.StartsWith("capture-", StringComparison.Ordinal))
        {
            PlayerDialogRequested?.Invoke(action);
            return;
        }
        switch (action)
        {
            case "previous-episode": PreviousEpisode(); return;
            case "next-episode": NextEpisode(); return;
            case "tool-start":
            case "tool-end":
                var seconds = await _playback.GetNumberAsync("time-pos").ConfigureAwait(true);
                if (!IsCurrentInteraction(context) || seconds is null) return;
                if (_toolMarkerGeneration != _playback.Generation) { _toolStart = null; _toolEnd = null; }
                _toolMarkerGeneration = _playback.Generation;
                if (action == "tool-start") _toolStart = seconds; else _toolEnd = seconds;
                Noticed?.Invoke($"{(action == "tool-start" ? "开始" : "结束")}时间：{TimeSpan.FromSeconds(seconds.Value):hh\\:mm\\:ss\\.fff}", InfoBarSeverity.Informational);
                return;
            case "tool-cancel": _playerTools?.Cancel(); return;
            case "tool-status": return;
            case "subtitle-download":
            case "subtitle-content": PlayerDialogRequested?.Invoke(action); return;
        }
        if (action.StartsWith("tool-", StringComparison.Ordinal) && _playerTools is not null)
        {
            var fileGeneration = _playback.ActiveFileGeneration;
            var subtitle = Tracks.FirstOrDefault(track => track.IsSubtitle && track.Selected && track.MainSelection != 1);
            var audio = Tracks.FirstOrDefault(track => track.IsAudio && track.Selected);
            var hasRange = _toolMarkerGeneration == _playback.Generation;
            var video = Tracks.FirstOrDefault(track => track.Type == "video" && track.Selected);
            var output = await _playerTools.RunAsync(action, subtitle, audio, video, hasRange ? _toolStart ?? 0 : 0,
                hasRange ? _toolEnd : null).ConfigureAwait(true);
            if (output is not null && IsCurrentInteraction(context)
                && action is "tool-subtitle-generate" or "tool-subtitle-sync" or "tool-subtitle-sync-alass" or "tool-subtitle-translate")
                await _playback.CommandForActiveFileAsync(context.PlaybackGeneration, fileGeneration,
                    "sub-add", output, "select").ConfigureAwait(true);
            return;
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

    private IReadOnlyList<PlayerMenuNode> ToolMenuRows()
    {
        if (_playerTools is null) return [];
        if (_playerTools.Busy) return [HostRow(_playerTools.Status, "tool-status"), HostRow("取消工具任务", "tool-cancel")];
        if (!_playback.CanUsePlayerTools) return [];
        var tools = Settings.PlayerTools;
        var rows = new List<PlayerMenuNode>();
        var ffmpeg = PlayerToolPaths.IsExecutableAvailable(tools.FfmpegPath);
        var hasAudio = Tracks.Any(track => track.IsAudio && track.Selected && track.FfmpegIndex is not null);
        var subtitle = Tracks.FirstOrDefault(track => track.IsSubtitle && track.Selected && track.MainSelection != 1);
        var text = subtitle is { Image: false } && (subtitle.FfmpegIndex is not null || !string.IsNullOrWhiteSpace(subtitle.ExternalFilename));
        if (ffmpeg && Tracks.Any(track => track.Type == "video"))
        {
            if (_playerCapture is not null)
            {
                rows.Add(HostRow("自定义裁切截图", "capture-crop", "capture-crop"));
                rows.Add(HostRow("截取字幕区域并加入拼图", "capture-subtitle", "capture-subtitle"));
                if (_playerCapture.PendingCount >= 2)
                    rows.Add(HostRow($"生成字幕拼接图（{_playerCapture.PendingCount} 张）", "capture-stitch", "capture-stitch"));
            }
            var range = _toolMarkerGeneration == _playback.Generation;
            rows.Add(HostRow(range && _toolStart is { } start ? $"开始时间：{TimeSpan.FromSeconds(start):hh\\:mm\\:ss}" : "标记开始时间", "tool-start", "tool-start"));
            rows.Add(HostRow(range && _toolEnd is { } end ? $"结束时间：{TimeSpan.FromSeconds(end):hh\\:mm\\:ss}" : "标记结束时间", "tool-end", "tool-end"));
            if (range && _toolStart is { } a && _toolEnd is { } b && b > a)
            {
                rows.Add(HostRow("导出片段（MKV，无损）", "tool-clip", "tool-clip"));
                rows.Add(HostRow("导出 GIF", "tool-gif", "tool-gif"));
                if (text) rows.Add(HostRow("导出带字幕的 GIF", "tool-gif-subtitles", "tool-gif-subtitles"));
            }
        }
        if (ffmpeg && text)
        {
            rows.Add(HostRow("导出当前文字字幕", "tool-subtitle-export", "tool-subtitle-export"));
            if (hasAudio && PlayerToolPaths.IsExecutableAvailable(PlayerToolPaths.ResolveFfprobe(tools)) && PlayerToolPaths.IsExecutableAvailable(tools.FfsubsyncPath)) rows.Add(HostRow("自动同步字幕（ffsubsync）", "tool-subtitle-sync", "tool-subtitle-sync"));
            if (hasAudio && PlayerToolPaths.IsExecutableAvailable(PlayerToolPaths.ResolveFfprobe(tools)) && PlayerToolPaths.IsExecutableAvailable(tools.AlassPath)) rows.Add(HostRow("自动同步字幕（alass）", "tool-subtitle-sync-alass"));
            var translation = Settings.SubtitleTranslation;
            if (translation.Enabled && (translation.Provider switch
            {
                Momoka.Configuration.SubtitleTranslationProvider.Google => translation.HasGoogleApiKey,
                Momoka.Configuration.SubtitleTranslationProvider.Microsoft => translation.HasMicrosoftApiKey,
                _ => translation.HasAiApiKey
            })) rows.Add(HostRow("翻译当前整份字幕", "tool-subtitle-translate", "tool-subtitle-translate"));
        }
        if (ffmpeg && PlayerToolPaths.IsExecutableAvailable(tools.FasterWhisperPath) && Tracks.Any(track => track.IsAudio && track.Selected && track.FfmpegIndex is not null))
            rows.Add(HostRow("生成 AI 字幕", "tool-subtitle-generate", "tool-subtitle-generate"));
        return rows;
    }
}

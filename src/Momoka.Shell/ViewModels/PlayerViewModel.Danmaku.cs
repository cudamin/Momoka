using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;
using Momoka.Mpv;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private readonly DandanPlayClient _dandanPlay;
    private CancellationTokenSource? _danmakuLoad;
    private CancellationTokenSource? _danmakuSearch;
    private CancellationTokenSource? _danmakuStylePush;
    private IReadOnlyList<DanmakuComment> _danmakuComments = [];
    private DandanPlayEpisode? _danmakuEpisode;
    private bool _danmakuFetched;
    private long _danmakuSerial;
    private long _danmakuTransfer;
    private int _danmakuRequest;
    private int _danmakuSearchRequest;
    private double _danmakuDelay;
    private string _danmakuConnection = "";

    [ObservableProperty]
    public partial string DanmakuStatus { get; set; } = "弹幕来源：弹弹play；搜索作品后选择对应剧集。";

    internal event Action? DanmakuRequested;
    public bool DanmakuAvailable => DandanPlayMatching.IsConfigured(Settings) && Embedded;
    public Visibility DanmakuControlsVisibility => DanmakuAvailable ? Visibility.Visible : Visibility.Collapsed;
    public bool DanmakuShown => Settings.Danmaku.Show;
    internal string DanmakuSearchTitle => _nowPlaying is { } item ? DandanPlayMatching.Title(item) : "";
    internal string DanmakuSearchEpisode => _nowPlaying is { } item ? DandanPlayMatching.EpisodeNumber(item) ?? "" : "";
    internal DanmakuSettings DanmakuPreferences => Settings.Danmaku;

    internal void ChangeDanmakuPreferences(Action<DanmakuSettings> change)
    {
        if (!DanmakuAvailable) return;
        change(Settings.Danmaku);
        Settings.Danmaku.Normalize();
        _settings.TrySave();
        ShellPrefs.ApplyDanmaku();
    }

    internal void CancelDanmakuSearch()
    {
        _danmakuSearchRequest++;
        _danmakuSearch?.Cancel();
    }

    internal void RequestDanmaku() { if (DanmakuAvailable) DanmakuRequested?.Invoke(); }

    private void ResetDanmaku(EmbyItem? item)
    {
        CancelDanmakuRequests();
        _danmakuSerial = 0;
        _danmakuTransfer++;
        _danmakuComments = [];
        _danmakuEpisode = null;
        _danmakuFetched = false;
        _danmakuDelay = 0;
        _danmakuConnection = DanmakuConnection();
        ResetDanmakuInput();
        RefreshDanmakuProperties();
        DanmakuStatus = "弹幕来源：弹弹play；搜索作品后选择对应剧集。";
        if (item is not null && DanmakuAvailable)
        {
            _ = PingDanmakuAsync();
            if (Settings.Danmaku.AutoMatch) _ = AutoMatchDanmakuAsync(item);
        }
    }

    private void CancelDanmakuRequests()
    {
        _danmakuRequest++;
        _danmakuSearchRequest++;
        _danmakuLoad?.Cancel();
        _danmakuLoad?.Dispose();
        _danmakuLoad = null;
        _danmakuSearch?.Cancel();
        _danmakuSearch?.Dispose();
        _danmakuSearch = null;
        _danmakuStylePush?.Cancel();
        _danmakuStylePush?.Dispose();
        _danmakuStylePush = null;
    }

    // 换片、切后端与开关服务要重载弹幕；应用凭据 2026-10-27 起全部内置，设置里已经没有需要跟着重载的密钥。
    private string DanmakuConnection() => $"{DanmakuAvailable}\n{Settings.Danmaku.WithRelated}";

    private void RefreshDanmakuProperties()
    {
        OnPropertyChanged(nameof(DanmakuAvailable));
        OnPropertyChanged(nameof(DanmakuControlsVisibility));
        OnPropertyChanged(nameof(DanmakuShown));
        RefreshDanmakuInput();
    }

    private void DanmakuSettingsChanged()
    {
        if (_lifetime.IsCancellationRequested) return;
        RefreshDanmakuProperties();
        var connection = DanmakuConnection();
        if (connection != _danmakuConnection)
        {
            _danmakuConnection = connection;
            var chosen = _danmakuEpisode;
            CancelDanmakuRequests();
            _danmakuComments = [];
            _danmakuFetched = false;
            ResetDanmakuInput();
            _danmakuTransfer++;
            _ = ClearDanmakuAsync();
            if (DanmakuAvailable && _nowPlaying is { } current)
            {
                _ = PingDanmakuAsync();
                if (chosen is not null) _ = LoadDanmakuAsync(chosen);
                else if (Settings.Danmaku.AutoMatch) _ = AutoMatchDanmakuAsync(current);
            }
        }
        else if (DanmakuAvailable && _danmakuFetched) _ = ScheduleDanmakuPushAsync();
        _ = PushDanmakuVisibilityAsync();
    }

    private async Task ScheduleDanmakuPushAsync()
    {
        _danmakuStylePush?.Cancel();
        _danmakuStylePush?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _danmakuStylePush = cancellation;
        var token = cancellation.Token;
        var context = CaptureInteraction();
        try
        {
            // 连续拖滑块只发送最后一档，避免每拍排序并重复发送数万条弹幕。
            await Task.Delay(100, token).ConfigureAwait(true);
            if (IsCurrentPlayback(context) && DanmakuAvailable) await PushDanmakuAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
    }

    private Task<bool> PingDanmakuAsync() => _playback.CommandAsync("script-message-to", MpvDanmaku.ScriptName, MpvDanmaku.Ping);

    private Task<bool> ClearDanmakuAsync() => _playback.CommandAsync("script-message-to", MpvDanmaku.ScriptName, MpvDanmaku.Clear,
        _danmakuSerial.ToString(CultureInfo.InvariantCulture), _danmakuTransfer.ToString(CultureInfo.InvariantCulture));

    private Task<bool> PushDanmakuVisibilityAsync() => HeadlessPlayback
        ? _playback.CommandAsync("script-message", MpvDanmaku.Visibility, DanmakuAvailable ? "yes" : "no")
        : Task.FromResult(false);

    private async Task AutoMatchDanmakuAsync(EmbyItem item)
    {
        var context = CaptureInteraction();
        if (DandanPlayMatching.Title(item).Trim().Length < 2) return;
        // 第二季与特别篇不发含糊的自动请求；仍然可以从播放页手动搜索。
        if (item.Type == EmbyItemType.Episode && (item.ParentIndexNumber is null or 0 or > 1 || item.IndexNumber is not > 0))
        {
            DanmakuStatus = "续季或特别篇请手动搜索并选择弹幕库，避免串集。";
            return;
        }
        var candidates = await SearchDanmakuAsync(DandanPlayMatching.Title(item), DandanPlayMatching.EpisodeNumber(item)).ConfigureAwait(true);
        if (!IsCurrentPlayback(context) || !DanmakuAvailable) return;
        if (DandanPlayMatching.UniqueMatch(item, candidates.Episodes, candidates.HasMore) is { } match) await LoadDanmakuAsync(match).ConfigureAwait(true);
        else if (!candidates.HasMore && candidates.Episodes.Count > 0) DanmakuStatus = "有多个可能的结果，请手动搜索并选择对应剧集。";
    }

    internal async Task<DandanPlaySearchResult> SearchDanmakuAsync(string title, string? episode)
    {
        if (!DanmakuAvailable || !_playback.IsPlaying) return DandanPlaySearchResult.Empty;
        _danmakuSearch?.Cancel();
        _danmakuSearch?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _danmakuSearch = cancellation;
        var token = cancellation.Token;
        var request = ++_danmakuSearchRequest;
        var context = CaptureInteraction();
        DanmakuStatus = "正在搜索弹弹play…";
        try
        {
            var matches = await _dandanPlay.SearchAsync(title, episode, token).ConfigureAwait(true);
            if (!IsCurrentPlayback(context) || request != _danmakuSearchRequest || !DanmakuAvailable) return DandanPlaySearchResult.Empty;
            DanmakuStatus = matches.HasMore ? "搜索结果过多，请输入更完整的片名或指定集数后重试。"
                : matches.Episodes.Count == 0 ? "没有找到对应剧集，请尝试其他片名或留空集数。" : $"找到 {matches.Episodes.Count} 个剧集，请选择与当前视频对应的一项。";
            return matches;
        }
        catch (OperationCanceledException) { return DandanPlaySearchResult.Empty; }
        catch (DandanPlayException error)
        {
            if (IsCurrentPlayback(context) && request == _danmakuSearchRequest) DanmakuStatus = error.Message;
            return DandanPlaySearchResult.Empty;
        }
    }

    internal async Task LoadDanmakuAsync(DandanPlayEpisode episode)
    {
        if (!DanmakuAvailable || !_playback.IsPlaying) return;
        CancelDanmakuSearch();
        _danmakuLoad?.Cancel();
        _danmakuLoad?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _danmakuLoad = cancellation;
        var token = cancellation.Token;
        var request = ++_danmakuRequest;
        var context = CaptureInteraction();
        _danmakuEpisode = episode;
        _danmakuComments = [];
        _danmakuFetched = false;
        ResetDanmakuInput();
        _danmakuTransfer++;
        DanmakuStatus = $"正在加载弹幕：{episode.Label}…";
        await ClearDanmakuAsync().ConfigureAwait(true);
        if (!IsCurrentPlayback(context) || request != _danmakuRequest) return;
        try
        {
            var comments = await _dandanPlay.GetCommentsAsync(episode.EpisodeId, Settings.Danmaku.WithRelated, token).ConfigureAwait(true);
            if (!IsCurrentPlayback(context) || request != _danmakuRequest || !DanmakuAvailable) return;
            _danmakuComments = comments;
            _danmakuFetched = true;
            RefreshDanmakuInput();
            if (_danmakuSerial == 0)
            {
                DanmakuStatus = "弹幕已下载，正在等待播放器图层就绪…";
                await PingDanmakuAsync().ConfigureAwait(true);
            }
            else await PushDanmakuAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (DandanPlayException error)
        {
            if (IsCurrentPlayback(context) && request == _danmakuRequest) DanmakuStatus = error.Message;
        }
    }

    private async Task PushDanmakuAsync()
    {
        if (!DanmakuAvailable || !_danmakuFetched || _danmakuSerial <= 0 || !_playback.CanControl) return;
        var context = CaptureInteraction();
        var serial = _danmakuSerial;
        var transfer = ++_danmakuTransfer;
        var settings = Settings.Danmaku;
        var options = JsonSerializer.Serialize(new
        {
            show = settings.Show,
            opacity = settings.Opacity,
            fontSize = settings.FontSize,
            speed = settings.Speed,
            area = settings.Area,
            density = settings.Density,
            delay = _danmakuDelay,
        });
        var filtered = DanmakuComments.Filter(_danmakuComments, settings);
        bool Current() => IsCurrentPlayback(context) && serial == _danmakuSerial && transfer == _danmakuTransfer && DanmakuAvailable;
        async Task<bool> Send(string message, string? data = null)
        {
            if (!Current()) return false;
            var arguments = new List<string> { "script-message-to", MpvDanmaku.ScriptName, message,
                serial.ToString(CultureInfo.InvariantCulture), transfer.ToString(CultureInfo.InvariantCulture) };
            if (data is not null) arguments.Add(data);
            var done = await _playback.CommandAsync([.. arguments]).ConfigureAwait(true);
            if (!done && Current()) DanmakuStatus = "弹幕图层未能更新，请重新加载弹幕或重开播放。";
            return done && Current();
        }
        if (!await Send(MpvDanmaku.Begin, options).ConfigureAwait(true)) return;
        // 分块而不是一次发送数 MB 的命令；每拍核对播放身份，旧片的迟到块也会被 Lua 的文件序号拦下。
        for (var offset = 0; offset < filtered.Count; offset += 128)
        {
            var chunk = JsonSerializer.Serialize(filtered.Skip(offset).Take(128).Select(comment => new
            { time = comment.Time, mode = comment.Mode, color = comment.Color, text = comment.Text }));
            if (!await Send(MpvDanmaku.Chunk, chunk).ConfigureAwait(true)) return;
        }
        await Send(MpvDanmaku.Commit).ConfigureAwait(true);
    }

    private void DanmakuScriptReady(long serial)
    {
        _danmakuSerial = serial;
        _ = PushDanmakuVisibilityAsync();
        _ = PushDanmakuInputAsync();
        if (_danmakuFetched) _ = PushDanmakuAsync();
    }

    private void DanmakuScriptLoaded(string receipt)
    {
        var parts = receipt.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[0], out var serial) || !long.TryParse(parts[1], out var transfer)
            || serial != _danmakuSerial || transfer != _danmakuTransfer || !DanmakuAvailable) return;
        DanmakuStatus = $"来源：弹弹play · {_danmakuEpisode?.Label ?? "当前视频"} · 已加载 {parts[2]} 条弹幕";
    }

    internal void ToggleDanmaku()
    {
        if (!DanmakuAvailable) return;
        Settings.Danmaku.Show = !Settings.Danmaku.Show;
        _settings.TrySave();
        ShellPrefs.ApplyDanmaku();
    }

    internal void NudgeDanmakuDelay(double delta)
    {
        if (!DanmakuAvailable) return;
        _danmakuDelay = Math.Clamp(_danmakuDelay + delta, -300, 300);
        _ = ScheduleDanmakuPushAsync();
        _ = _playback.CommandAsync("show-text", $"弹幕延迟：{_danmakuDelay.ToString("+0.0;-0.0;0", CultureInfo.InvariantCulture)} 秒", "1200");
    }
}

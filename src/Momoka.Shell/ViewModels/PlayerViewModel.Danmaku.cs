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
    private DandanPlayEpisode? _danmakuDanmuApiEpisode;
    private bool _danmakuFetched;
    private long _danmakuSerial;
    private long _danmakuTransfer;
    private int _danmakuRequest;
    private int _danmakuSearchRequest;
    private int _danmakuDandanCount;
    private int _danmakuDanmuApiCount;
    private double _danmakuDelay;
    private string _danmakuConnection = "";
    private string _danmakuSourceSummary = "弹弹play";
    private string _danmakuLabel = "";
    private string _danmakuWarning = "";

    [ObservableProperty]
    public partial string DanmakuStatus { get; set; } = "弹幕来源：弹弹play；搜索作品后选择对应剧集。";

    internal event Action? DanmakuRequested;
    public bool DanmakuAvailable => DanmakuSourceRules.IsAvailable(Settings) && Embedded;
    public Visibility DanmakuControlsVisibility => DanmakuAvailable ? Visibility.Visible : Visibility.Collapsed;
    public bool DanmakuShown => Settings.Danmaku.Show;
    public bool DanmakuSendSupported => DanmakuSourceRules.CanSend(Settings.Danmaku.Source);
    internal string DanmakuSearchTitle => _nowPlaying is { } item ? DandanPlayMatching.Title(item) : "";
    internal string DanmakuSearchEpisode => _nowPlaying is { } item ? DandanPlayMatching.EpisodeNumber(item) ?? "" : "";
    internal DanmakuSettings DanmakuPreferences => Settings.Danmaku;

    internal IReadOnlyList<DanmakuSourceOption> DanmakuSearchSources() =>
        [.. DanmakuSourceRules.AvailableSources(Settings)
            .Select(source => new DanmakuSourceOption(DanmakuSourceRules.SourceName(source), source))];

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

    private string DefaultDanmakuStatus =>
        $"弹幕来源：{DanmakuSourceRules.ModeLabel(Settings.Danmaku.Source)}；搜索作品后选择对应剧集。";

    private void ResetDanmaku(EmbyItem? item)
    {
        CancelDanmakuRequests();
        _danmakuSerial = 0;
        _danmakuTransfer++;
        _danmakuComments = [];
        _danmakuEpisode = null;
        _danmakuDanmuApiEpisode = null;
        _danmakuFetched = false;
        _danmakuDelay = 0;
        _danmakuDandanCount = 0;
        _danmakuDanmuApiCount = 0;
        _danmakuLabel = "";
        _danmakuWarning = "";
        _danmakuSourceSummary = DanmakuSourceRules.ModeLabel(Settings.Danmaku.Source);
        _danmakuConnection = DanmakuConnection();
        ResetDanmakuInput();
        RefreshDanmakuProperties();
        DanmakuStatus = DefaultDanmakuStatus;
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

    // 换片、切后端、换来源、改地址或改令牌都要重载弹幕；令牌密文只用于比较，不写日志。
    private string DanmakuConnection() =>
        $"{DanmakuAvailable}\n{Settings.Danmaku.WithRelated}\n{Settings.Danmaku.Source}\n"
        + $"{Settings.Danmaku.DanmuApiBaseUrl}\n{Settings.Danmaku.ProtectedDanmuApiToken}";

    private void RefreshDanmakuProperties()
    {
        OnPropertyChanged(nameof(DanmakuAvailable));
        OnPropertyChanged(nameof(DanmakuControlsVisibility));
        OnPropertyChanged(nameof(DanmakuShown));
        OnPropertyChanged(nameof(DanmakuSendSupported));
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
            var chosenDandan = _danmakuEpisode;
            var chosenCustom = _danmakuDanmuApiEpisode;
            CancelDanmakuRequests();
            _danmakuComments = [];
            _danmakuEpisode = null;
            _danmakuDanmuApiEpisode = null;
            _danmakuFetched = false;
            _danmakuDandanCount = 0;
            _danmakuDanmuApiCount = 0;
            _danmakuLabel = "";
            _danmakuWarning = "";
            _danmakuSourceSummary = DanmakuSourceRules.ModeLabel(Settings.Danmaku.Source);
            DanmakuStatus = DefaultDanmakuStatus;
            ResetDanmakuInput();
            _danmakuTransfer++;
            _ = ClearDanmakuAsync();
            if (DanmakuAvailable && _nowPlaying is { } current)
            {
                _ = PingDanmakuAsync();
                var selections = new List<(DanmakuSource Source, DandanPlayEpisode Episode)>(2);
                if (chosenDandan is not null && DanmakuSourceRules.Includes(Settings.Danmaku.Source, DanmakuSource.DandanPlay))
                    selections.Add((DanmakuSource.DandanPlay, chosenDandan));
                if (chosenCustom is not null && DanmakuSourceRules.Includes(Settings.Danmaku.Source, DanmakuSource.DanmuApi))
                    selections.Add((DanmakuSource.DanmuApi, chosenCustom));
                if (selections.Count > 0) _ = LoadSourcesAsync(selections);
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

    private DanmakuSource DefaultSearchSource() =>
        DanmakuSourceRules.IncludesDandanPlay(Settings.Danmaku.Source)
            ? DanmakuSource.DandanPlay
            : DanmakuSource.DanmuApi;

    private async Task AutoMatchDanmakuAsync(EmbyItem item)
    {
        var context = CaptureInteraction();
        if (DandanPlayMatching.Title(item).Trim().Length < 2) return;
        // 续季与特别篇不发含糊的自动请求；仍然可以从播放页手动搜索。
        if (!DanmakuMatchRules.CanAutoMatch(item))
        {
            DanmakuStatus = "续季或特别篇请手动搜索并选择弹幕库，避免串集。";
            return;
        }
        if (!DanmakuAvailable) return;

        _danmakuSearch?.Cancel();
        _danmakuSearch?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _danmakuSearch = cancellation;
        var token = cancellation.Token;
        var request = ++_danmakuSearchRequest;
        DanmakuStatus = "正在匹配弹幕…";
        try
        {
            DandanPlaySearchResult dandan = DandanPlaySearchResult.Empty;
            var dandanError = "";
            if (DanmakuSourceRules.IsSourceReady(Settings, DanmakuSource.DandanPlay)
                && DanmakuSourceRules.IncludesDandanPlay(Settings.Danmaku.Source))
            {
                (dandan, dandanError) = await SearchSourceAsync(
                    DandanPlayMatching.Title(item), DandanPlayMatching.EpisodeNumber(item),
                    DanmakuSource.DandanPlay, token).ConfigureAwait(true);
            }
            if (!IsCurrentPlayback(context) || request != _danmakuSearchRequest || !DanmakuAvailable) return;

            var dandanMatch = dandanError.Length == 0
                ? DandanPlayMatching.UniqueMatch(item, dandan.Episodes, dandan.HasMore)
                : null;

            DandanPlayEpisode? customMatch = null;
            var customError = "";
            if (DanmakuSourceRules.IsSourceReady(Settings, DanmakuSource.DanmuApi)
                && DanmakuSourceRules.IncludesDanmuApi(Settings.Danmaku.Source))
            {
                try
                {
                    var candidate = await _dandanPlay.MatchAsync(DanmakuMatchRules.MatchFileName(item), token)
                        .ConfigureAwait(true);
                    if (candidate is null) customError = "danmu_api 没有找到匹配。";
                    else if (DanmakuMatchRules.Accepts(item, candidate)) customMatch = candidate;
                    else customError = "danmu_api 匹配到的集号与当前视频不一致，请手动搜索。";
                }
                catch (DandanPlayException error) { customError = error.Message; }
            }
            if (!IsCurrentPlayback(context) || request != _danmakuSearchRequest || !DanmakuAvailable) return;

            var selections = new List<(DanmakuSource Source, DandanPlayEpisode Episode)>(2);
            if (dandanMatch is not null) selections.Add((DanmakuSource.DandanPlay, dandanMatch));
            if (customMatch is not null) selections.Add((DanmakuSource.DanmuApi, customMatch));
            if (selections.Count > 0)
            {
                await LoadSourcesAsync(selections).ConfigureAwait(true);
                return;
            }

            var notes = new List<string>();
            if (dandanError.Length > 0) notes.Add(dandanError);
            else if (dandan.HasMore) notes.Add("搜索结果过多，请输入更完整的片名或指定集数后重试。");
            else if (dandan.Episodes.Count > 0) notes.Add("弹弹play 有多个可能的结果，请手动搜索并选择对应剧集。");
            if (customError.Length > 0) notes.Add(customError);
            DanmakuStatus = notes.Count > 0 ? string.Join("；", notes) : "没有找到对应剧集，请尝试其他片名或留空集数。";
        }
        catch (OperationCanceledException) { }
    }

    internal Task<DandanPlaySearchResult> SearchDanmakuAsync(string title, string? episode,
        CancellationToken cancellationToken = default) =>
        SearchDanmakuAsync(title, episode, DefaultSearchSource(), cancellationToken);

    internal async Task<DandanPlaySearchResult> SearchDanmakuAsync(string title, string? episode,
        DanmakuSource source, CancellationToken cancellationToken = default)
    {
        if (!DanmakuAvailable || !_playback.IsPlaying) return DandanPlaySearchResult.Empty;
        if (!DanmakuSourceRules.Includes(Settings.Danmaku.Source, source)
            || !DanmakuSourceRules.IsSourceReady(Settings, source))
        {
            DanmakuStatus = $"当前弹幕来源未包含或尚未配置 {DanmakuSourceRules.SourceName(source)}。";
            return DandanPlaySearchResult.Empty;
        }
        _danmakuSearch?.Cancel();
        _danmakuSearch?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _danmakuSearch = cancellation;
        var token = cancellation.Token;
        var request = ++_danmakuSearchRequest;
        var context = CaptureInteraction();
        DanmakuStatus = $"正在搜索{DanmakuSourceRules.SourceName(source)}…";
        try
        {
            var (matches, error) = await SearchSourceAsync(title, episode, source, token).ConfigureAwait(true);
            if (!IsCurrentPlayback(context) || request != _danmakuSearchRequest || !DanmakuAvailable)
                return DandanPlaySearchResult.Empty;
            if (error.Length > 0)
            {
                DanmakuStatus = error;
                return DandanPlaySearchResult.Empty;
            }
            DanmakuStatus = matches.HasMore
                ? "搜索结果过多，请输入更完整的片名或指定集数后重试。"
                : matches.Episodes.Count == 0
                    ? $"{DanmakuSourceRules.SourceName(source)} 没有找到对应剧集，请尝试其他片名或留空集数。"
                    : $"找到 {matches.Episodes.Count} 个剧集（{DanmakuSourceRules.SourceName(source)}），请选择与当前视频对应的一项。";
            return matches;
        }
        catch (OperationCanceledException) { return DandanPlaySearchResult.Empty; }
    }

    private async Task<(DandanPlaySearchResult Result, string Error)> SearchSourceAsync(
        string title, string? episode, DanmakuSource source, CancellationToken token)
    {
        try { return (await _dandanPlay.SearchAsync(title, episode, source, token).ConfigureAwait(true), ""); }
        catch (DandanPlayException error) { return (DandanPlaySearchResult.Empty, error.Message); }
    }

    internal Task LoadDanmakuAsync(DandanPlayEpisode episode) =>
        LoadDanmakuAsync(episode, DanmakuSource.DandanPlay);

    internal async Task LoadDanmakuAsync(DandanPlayEpisode episode, DanmakuSource source)
    {
        if (!DanmakuAvailable || !_playback.IsPlaying) return;
        if (!DanmakuSourceRules.Includes(Settings.Danmaku.Source, source)
            || !DanmakuSourceRules.IsSourceReady(Settings, source))
        {
            DanmakuStatus = $"当前弹幕来源未包含或尚未配置 {DanmakuSourceRules.SourceName(source)}。";
            return;
        }
        var selections = new List<(DanmakuSource Source, DandanPlayEpisode Episode)> { (source, episode) };
        await LoadSourcesAsync(selections).ConfigureAwait(true);
    }

    private async Task LoadSourcesAsync(IReadOnlyList<(DanmakuSource Source, DandanPlayEpisode Episode)> selections)
    {
        if (!DanmakuAvailable || !_playback.IsPlaying || selections.Count == 0) return;
        CancelDanmakuSearch();
        _danmakuLoad?.Cancel();
        _danmakuLoad?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _danmakuLoad = cancellation;
        var token = cancellation.Token;
        var request = ++_danmakuRequest;
        var context = CaptureInteraction();
        _danmakuComments = [];
        _danmakuEpisode = null;
        _danmakuDanmuApiEpisode = null;
        _danmakuFetched = false;
        _danmakuDandanCount = 0;
        _danmakuDanmuApiCount = 0;
        _danmakuLabel = selections[0].Episode.Label;
        _danmakuWarning = "";
        _danmakuSourceSummary = string.Join(" + ",
            selections.Select(selection => DanmakuSourceRules.SourceName(selection.Source)));
        ResetDanmakuInput();
        _danmakuTransfer++;
        DanmakuStatus = $"正在加载{_danmakuSourceSummary} 弹幕：{_danmakuLabel}…";
        await ClearDanmakuAsync().ConfigureAwait(true);
        if (!DanmakuAvailable || !IsCurrentPlayback(context) || request != _danmakuRequest) return;

        var comments = new List<DanmakuComment>();
        var loaded = new List<string>();
        var errors = new List<string>();
        var sourceLabels = new List<string>();
        foreach (var (source, episode) in selections)
        {
            if (!DanmakuSourceRules.Includes(Settings.Danmaku.Source, source)) continue;
            try
            {
                var fetched = await _dandanPlay.GetCommentsAsync(episode.EpisodeId,
                        source == DanmakuSource.DandanPlay && Settings.Danmaku.WithRelated, source, token)
                    .ConfigureAwait(true);
                if (!DanmakuAvailable || !IsCurrentPlayback(context) || request != _danmakuRequest) return;
                comments.AddRange(fetched);
                if (source == DanmakuSource.DandanPlay)
                {
                    _danmakuEpisode = episode;
                    _danmakuDandanCount = fetched.Count;
                    _danmakuLabel = episode.Label;
                }
                else
                {
                    _danmakuDanmuApiEpisode = episode;
                    _danmakuDanmuApiCount = fetched.Count;
                    if (_danmakuEpisode is null) _danmakuLabel = episode.Label;
                }
                loaded.Add($"{DanmakuSourceRules.SourceName(source)} {fetched.Count} 条");
                sourceLabels.Add(DanmakuSourceRules.SourceName(source));
            }
            catch (OperationCanceledException) { return; }
            catch (DandanPlayException error)
            {
                if (!DanmakuAvailable || !IsCurrentPlayback(context) || request != _danmakuRequest) return;
                var name = DanmakuSourceRules.SourceName(source);
                errors.Add(error.Message.StartsWith(name, StringComparison.Ordinal)
                    ? error.Message
                    : $"{name}：{error.Message}");
            }
        }
        if (!DanmakuAvailable || !IsCurrentPlayback(context) || request != _danmakuRequest) return;
        if (loaded.Count == 0)
        {
            DanmakuStatus = errors.Count > 0 ? string.Join("；", errors) : "没有加载到弹幕。";
            _danmakuFetched = false;
            return;
        }

        _danmakuComments = DanmakuComments.Combine(comments);
        _danmakuFetched = true;
        _danmakuSourceSummary = string.Join(" + ", sourceLabels);
        _danmakuWarning = errors.Count > 0 ? string.Join("；", errors) : "";
        DanmakuStatus = $"来源：{string.Join(" + ", loaded)} · {_danmakuLabel}"
            + (_danmakuWarning.Length > 0 ? $"；{_danmakuWarning}" : "");
        RefreshDanmakuInput();
        if (_danmakuSerial == 0)
        {
            DanmakuStatus = "弹幕已下载，正在等待播放器图层就绪…";
            await PingDanmakuAsync().ConfigureAwait(true);
        }
        else await PushDanmakuAsync().ConfigureAwait(true);
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
        var label = _danmakuLabel.Length > 0 ? _danmakuLabel : "当前视频";
        DanmakuStatus = $"来源：{_danmakuSourceSummary} · {label} · 已加载 {parts[2]} 条弹幕"
            + (_danmakuWarning.Length > 0 ? $"；{_danmakuWarning}" : "");
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

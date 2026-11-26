using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Services;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private readonly FontLibrary _danmakuFonts;
    private readonly DandanPlayClient _dandanPlay;
    private CancellationTokenSource? _danmakuLoad;
    private CancellationTokenSource? _danmakuSearch;
    private CancellationTokenSource? _danmakuStylePush;
    private IReadOnlyList<DanmakuComment> _danmakuComments = [];
    private IReadOnlyList<DanmakuComment>? _danmakuHeatSource;
    private DanmakuHeat _danmakuHeatmap = DanmakuHeat.Empty;
    private double _danmakuHeatDuration = double.NaN;
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
    private string _danmuApiConnection = "";
    internal int DanmakuConnectionVersion { get; private set; }
    private string _danmakuSourceSummary = "弹弹play";
    private string _danmakuLabel = "";
    private string _danmakuWarning = "";
    private IReadOnlyList<DandanPlayEpisode> _danmakuCandidates = [];
    private DanmakuSource _danmakuCandidatesSource = DanmakuSource.DandanPlay;
    private int _danmakuCandidatesVersion;

    [ObservableProperty]
    public partial string DanmakuStatus { get; set; } = "弹幕来源：弹弹play；搜索作品后选择对应剧集。";

    internal event Action? DanmakuRequested;
    /// <summary>当前设置后端是否提供弹幕设置卡；与全局服务开关和当前播放能力分开。</summary>
    internal bool DanmakuSettingsAvailable => DandanPlayMatching.CanRender(Settings.Mpv);
    public bool DanmakuAvailable => DanmakuSourceRules.IsAvailable(Settings) && Embedded;
    public Visibility DanmakuControlsVisibility => DanmakuAvailable ? Visibility.Visible : Visibility.Collapsed;
    public bool DanmakuShown => Settings.Danmaku.Show;
    internal DanmakuHeat DanmakuHeatmap => _danmakuHeatmap;
    public bool DanmakuHeatVisible => DanmakuAvailable && DanmakuShown && Settings.Danmaku.ShowHeatmap && !_danmakuHeatmap.IsEmpty;
    public string DanmakuToggleLabel => DanmakuShown ? "关闭弹幕" : "开启弹幕";
    public bool DanmakuSendSupported => DanmakuSourceRules.CanSend(Settings.Danmaku.Source);
    internal string DanmakuSearchTitle => _nowPlaying is { } item ? DandanPlayMatching.Title(item) : "";
    internal string DanmakuSearchEpisode => _nowPlaying is { } item ? DandanPlayMatching.EpisodeNumber(item) ?? "" : "";
    internal DanmakuSettings DanmakuPreferences => Settings.Danmaku;
    internal IReadOnlyList<DandanPlayEpisode> DanmakuCandidates => _danmakuCandidates;
    internal DanmakuSource DanmakuCandidatesSource => _danmakuCandidatesSource;
    internal int DanmakuCandidatesVersion => _danmakuCandidatesVersion;
    internal Task<FontCatalogue> DanmakuFontsAsync() => _danmakuFonts.LoadAsync();

    internal IReadOnlyList<DanmakuSourceOption> DanmakuSearchSources() =>
        [.. DanmakuSourceRules.AvailableSources(Settings)
            .Select(source => new DanmakuSourceOption(DanmakuSourceRules.SourceName(source), source))];

    internal bool DanmakuSourceReady(DanmakuSource source) => DanmakuSourceRules.IsSourceReady(Settings, source);

    internal void ChangeDanmakuPreferences(Action<DanmakuSettings> change)
    {
        if (!DanmakuAvailable) return;
        change(Settings.Danmaku);
        Settings.Danmaku.Normalize();
        _settings.TrySave();
        ShellPrefs.ApplyDanmaku();
    }

    private int CancelDanmakuSearch()
    {
        var request = ++_danmakuSearchRequest;
        _danmakuSearch?.Cancel();
        return request;
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
        RefreshDanmakuHeat();
        _danmakuDelay = 0;
        _danmakuDandanCount = 0;
        _danmakuDanmuApiCount = 0;
        _danmakuLabel = "";
        _danmakuWarning = "";
        ClearDanmakuCandidates();
        _danmakuSourceSummary = DanmakuSourceRules.ModeLabel(Settings.Danmaku.Source);
        _danmakuConnection = DanmakuConnection();
        _danmuApiConnection = DanmuApiConnection();
        DanmakuConnectionVersion++;
        OnPropertyChanged(nameof(DanmakuConnectionVersion));
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

    private string DanmuApiConnection() =>
        $"{Settings.Danmaku.DanmuApiBaseUrl}\n{Settings.Danmaku.ProtectedDanmuApiToken}";

    private void RefreshDanmakuProperties()
    {
        OnPropertyChanged(nameof(DanmakuAvailable));
        OnPropertyChanged(nameof(DanmakuControlsVisibility));
        OnPropertyChanged(nameof(DanmakuShown));
        OnPropertyChanged(nameof(DanmakuHeatVisible));
        OnPropertyChanged(nameof(DanmakuToggleLabel));
        OnPropertyChanged(nameof(DanmakuSendSupported));
        RefreshDanmakuInput();
    }

    /// <summary>
    /// 按当前已加载弹幕与时长重算热力快照；评论和时长都没变就直接返回，避免样式滑块每拍重扫四万条。
    /// </summary>
    private void RefreshDanmakuHeat()
    {
        if (ReferenceEquals(_danmakuHeatSource, _danmakuComments) && _danmakuHeatDuration.Equals(Status.Duration)) return;
        _danmakuHeatSource = _danmakuComments;
        _danmakuHeatDuration = Status.Duration;
        _danmakuHeatmap = DanmakuHeat.Build(_danmakuComments, Status.Duration);
        if (_danmakuHeatmap.IsEmpty) DanmakuHeatCaption = null;
        OnPropertyChanged(nameof(DanmakuHeatmap));
        OnPropertyChanged(nameof(DanmakuHeatVisible));
    }

    private void DanmakuSettingsChanged()
    {
        if (_lifetime.IsCancellationRequested) return;
        RefreshDanmakuProperties();
        var connection = DanmakuConnection();
        if (connection != _danmakuConnection)
        {
            _danmakuConnection = connection;
            DanmakuConnectionVersion++;
            OnPropertyChanged(nameof(DanmakuConnectionVersion));
            var customConnection = DanmuApiConnection();
            var chosenDandan = _danmakuEpisode;
            // 自部署集号只在分配它的实例/凭据范围内有效；读取选项变化仍保留选择。
            var chosenCustom = customConnection == _danmuApiConnection ? _danmakuDanmuApiEpisode : null;
            _danmuApiConnection = customConnection;
            CancelDanmakuRequests();
            _danmakuComments = [];
            _danmakuEpisode = null;
            _danmakuDanmuApiEpisode = null;
            _danmakuFetched = false;
            _danmakuDandanCount = 0;
            _danmakuDanmuApiCount = 0;
            _danmakuLabel = "";
            _danmakuWarning = "";
            ClearDanmakuCandidates();
            _danmakuSourceSummary = DanmakuSourceRules.ModeLabel(Settings.Danmaku.Source);
            RefreshDanmakuHeat();
            DanmakuStatus = DefaultDanmakuStatus;
            ResetDanmakuInput(preserveDraft: true);
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

    private async Task<bool> PushDanmakuVisibilityAsync()
    {
        if (!HeadlessPlayback) return false;
        var available = await _playback.CommandAsync("script-message", MpvDanmaku.Visibility, DanmakuAvailable ? "yes" : "no").ConfigureAwait(true);
        await _playback.CommandAsync("script-message", MpvDanmaku.Shown, DanmakuShown ? "yes" : "no").ConfigureAwait(true);
        return available;
    }

    private DanmakuSource DefaultSearchSource() =>
        DanmakuSourceRules.IncludesDandanPlay(Settings.Danmaku.Source)
            ? DanmakuSource.DandanPlay
            : DanmakuSource.DanmuApi;

    private async Task AutoMatchDanmakuAsync(EmbyItem item)
    {
        var context = CaptureInteraction();
        if (DandanPlayMatching.Title(item).Trim().Length < 2) return;
        // 特别篇或集号缺失时不发含糊的自动请求；仍然可以从播放页手动搜索。
        if (!DanmakuMatchRules.CanAutoMatch(item))
        {
            DanmakuStatus = "特别篇或集号缺失时请手动搜索并选择弹幕库，避免串集。";
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
                    var seriesYear = await DanmakuSeriesYearAsync(item, token).ConfigureAwait(true);
                    if (!DanmakuAvailable || !IsCurrentPlayback(context) || request != _danmakuSearchRequest) return;
                    var candidate = await _dandanPlay.MatchAsync(DanmakuMatchRules.MatchFileName(item, seriesYear), token)
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
                var emptyLoad = await LoadSourcesAsync(selections).ConfigureAwait(true);
                if (emptyLoad is { } empty)
                    await TryEmptyDanmakuFallbackAsync(item, empty).ConfigureAwait(true);
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

    private async Task<int?> DanmakuSeriesYearAsync(EmbyItem item, CancellationToken cancellationToken)
    {
        if (item.Type != EmbyItemType.Episode) return null;
        if (string.IsNullOrWhiteSpace(item.SeriesId)) return null;
        try
        {
            var scope = _session.Capture(item);
            if (_parent is { Type: EmbyItemType.Series, ProductionYear: > 0 } parent
                && string.Equals(parent.Id, item.SeriesId, StringComparison.Ordinal)
                && ReferenceEquals(parent.Source?.UserStateOwner, scope.UserStateOwner))
                return parent.ProductionYear;

            var series = await scope
                .ExecuteAsync((client, token) => client.GetItemAsync(item.SeriesId, token, EmbyFields.Browse), cancellationToken)
                .ConfigureAwait(true);
            return series.ProductionYear is > 0 ? series.ProductionYear : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private void ClearDanmakuCandidates()
    {
        _danmakuCandidates = [];
        _danmakuCandidatesSource = DanmakuSource.DandanPlay;
        _danmakuCandidatesVersion++;
        OnPropertyChanged(nameof(DanmakuCandidatesVersion));
    }

    /// <summary>
    /// danmu_api 自动匹配成功但返回零条时，用该次加载的身份试一个同名同集候选；
    /// 仍为空时可以把候选交给面板。合并模式保留已有的弹弹play 内容。
    /// </summary>
    private async Task TryEmptyDanmakuFallbackAsync(EmbyItem item, EmptyDanmakuLoad empty)
    {
        var (owner, attempted) = empty;
        if (!IsCurrentDanmakuFallback(owner)) return;

        DanmakuStatus = $"danmu_api 匹配到的「{attempted.Label}」没有弹幕，正在尝试其他来源行…";
        var (matches, error) = await SearchSourceAsync(DandanPlayMatching.Title(item),
            DandanPlayMatching.EpisodeNumber(item), DanmakuSource.DanmuApi, owner.Token).ConfigureAwait(true);
        if (!IsCurrentDanmakuFallback(owner)) return;
        Log.Debug("danmaku", $"零弹幕兜底：搜索 error={error} 候选={matches.Episodes.Count}");
        if (error.Length > 0)
        {
            DanmakuStatus = $"自动匹配到的「{attempted.Label}」没有弹幕；{error}";
            return;
        }

        var seriesYear = await DanmakuSeriesYearAsync(item, owner.Token).ConfigureAwait(true);
        if (!IsCurrentDanmakuFallback(owner)) return;
        var candidates = DanmakuMatchRules.Alternatives(item, matches.Episodes, attempted.EpisodeId, seriesYear);
        Log.Debug("danmaku", $"零弹幕兜底：year={seriesYear} 同作品候选={candidates.Count}"
            + (candidates.Count > 0 ? $" 首个={candidates[0].EpisodeId}" : ""));
        if (candidates.Count > 0)
        {
            try
            {
                var fetched = await _dandanPlay.GetCommentsAsync(candidates[0].EpisodeId, false,
                    DanmakuSource.DanmuApi, owner.Token).ConfigureAwait(true);
                if (!IsCurrentDanmakuFallback(owner)) return;
                Log.Debug("danmaku", $"零弹幕兜底：候选 {candidates[0].EpisodeId} 读取 {fetched.Count} 条");
                if (fetched.Count > 0)
                {
                    var merged = new List<(DanmakuSource Source, DandanPlayEpisode Episode)>(2);
                    if (_danmakuDandanCount > 0 && _danmakuEpisode is { } dandan)
                        merged.Add((DanmakuSource.DandanPlay, dandan));
                    merged.Add((DanmakuSource.DanmuApi, candidates[0]));
                    await LoadSourcesAsync(merged, owner).ConfigureAwait(true);
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (DandanPlayException) { }
        }

        if (!IsCurrentDanmakuFallback(owner)) return;
        // 弹弹play 已经有内容时不拿空结果或候选去覆盖它。
        if (_danmakuDandanCount > 0) return;
        if (candidates.Count == 0)
        {
            DanmakuStatus = $"自动匹配到的「{attempted.Label}」没有弹幕，也没有找到同作品的其它来源行；请手动搜索。";
            return;
        }
        _danmakuCandidates = candidates;
        _danmakuCandidatesSource = DanmakuSource.DanmuApi;
        _danmakuCandidatesVersion++;
        OnPropertyChanged(nameof(DanmakuCandidatesVersion));
        DanmakuStatus = $"自动匹配到的「{attempted.Label}」没有弹幕；已列出同作品候选，请选择其它来源行。";
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

    private readonly record struct DanmakuLoadOwner(InteractionContext Playback, int ConnectionVersion,
        int SearchRequest, int LoadRequest, CancellationToken Token);

    private readonly record struct EmptyDanmakuLoad(DanmakuLoadOwner Owner, DandanPlayEpisode Episode);

    private bool IsCurrentDanmakuLoad(DanmakuLoadOwner owner) => DanmakuAvailable && _playback.IsPlaying
        && IsCurrentPlayback(owner.Playback) && !owner.Token.IsCancellationRequested
        && owner.ConnectionVersion == DanmakuConnectionVersion && owner.LoadRequest == _danmakuRequest;

    private bool IsCurrentDanmakuFallback(DanmakuLoadOwner owner) =>
        IsCurrentDanmakuLoad(owner) && owner.SearchRequest == _danmakuSearchRequest;

    private DanmakuLoadOwner BeginDanmakuLoad()
    {
        var context = CaptureInteraction();
        var connectionVersion = DanmakuConnectionVersion;
        // 新加载会取消搜索；只让兜底继承这次加载接管后的搜索代数。
        var searchRequest = CancelDanmakuSearch();
        _danmakuLoad?.Cancel();
        _danmakuLoad?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _danmakuLoad = cancellation;
        return new(context, connectionVersion, searchRequest, ++_danmakuRequest, cancellation.Token);
    }

    private async Task<EmptyDanmakuLoad?> LoadSourcesAsync(
        IReadOnlyList<(DanmakuSource Source, DandanPlayEpisode Episode)> selections, DanmakuLoadOwner? fallbackOwner = null)
    {
        if (!DanmakuAvailable || !_playback.IsPlaying || selections.Count == 0) return null;
        var owner = fallbackOwner ?? BeginDanmakuLoad();
        bool Current() => fallbackOwner is null ? IsCurrentDanmakuLoad(owner) : IsCurrentDanmakuFallback(owner);
        if (!Current()) return null;

        _danmakuComments = [];
        _danmakuEpisode = null;
        _danmakuDanmuApiEpisode = null;
        _danmakuFetched = false;
        RefreshDanmakuHeat();
        _danmakuDandanCount = 0;
        _danmakuDanmuApiCount = 0;
        _danmakuLabel = selections[0].Episode.Label;
        _danmakuWarning = "";
        _danmakuSourceSummary = string.Join(" + ",
            selections.Select(selection => DanmakuSourceRules.SourceName(selection.Source)));
        ResetDanmakuInput(preserveDraft: true);
        _danmakuTransfer++;
        DanmakuStatus = $"正在加载{_danmakuSourceSummary} 弹幕：{_danmakuLabel}…";
        await ClearDanmakuAsync().ConfigureAwait(true);
        if (!Current()) return null;

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
                        source == DanmakuSource.DandanPlay && Settings.Danmaku.WithRelated, source, owner.Token)
                    .ConfigureAwait(true);
                if (!Current()) return null;
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
            catch (OperationCanceledException) { return null; }
            catch (DandanPlayException error)
            {
                if (!Current()) return null;
                var name = DanmakuSourceRules.SourceName(source);
                errors.Add(error.Message.StartsWith(name, StringComparison.Ordinal)
                    ? error.Message
                    : $"{name}：{error.Message}");
            }
        }
        if (!Current()) return null;
        if (loaded.Count == 0)
        {
            DanmakuStatus = errors.Count > 0 ? string.Join("；", errors) : "没有加载到弹幕。";
            _danmakuFetched = false;
            return null;
        }

        _danmakuComments = DanmakuComments.Combine(comments);
        _danmakuFetched = true;
        RefreshDanmakuHeat();
        _danmakuSourceSummary = string.Join(" + ", sourceLabels);
        _danmakuWarning = errors.Count > 0 ? string.Join("；", errors) : "";
        DanmakuStatus = $"来源：{string.Join(" + ", loaded)} · {_danmakuLabel}"
            + (_danmakuWarning.Length > 0 ? $"；{_danmakuWarning}" : "");
        var empty = errors.Count == 0 && _danmakuDanmuApiCount == 0 ? _danmakuDanmuApiEpisode : null;
        RefreshDanmakuInput();
        if (_danmakuSerial == 0)
        {
            DanmakuStatus = "弹幕已下载，正在等待播放器图层就绪…";
            await PingDanmakuAsync().ConfigureAwait(true);
        }
        else await PushDanmakuAsync().ConfigureAwait(true);

        return empty is not null && IsCurrentDanmakuFallback(owner) ? new EmptyDanmakuLoad(owner, empty) : null;
    }

    private async Task PushDanmakuAsync()
    {
        if (!DanmakuAvailable || !_danmakuFetched || _danmakuSerial <= 0 || !_playback.CanControl) return;
        var context = CaptureInteraction();
        var serial = _danmakuSerial;
        var transfer = ++_danmakuTransfer;
        var settings = Settings.Danmaku;
        var options = MpvDanmaku.RenderOptions(settings, _danmakuDelay);
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

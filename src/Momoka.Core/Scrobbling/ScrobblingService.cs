using System.Net;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Playback;

namespace Momoka.Scrobbling;

/// <summary>Finds a unique site subject from the playing library episode before any external write.</summary>
public sealed partial class ScrobblingService : IPlaybackScrobbler, IDisposable
{
    public const int RecordLimitPerProvider = 100;

    private readonly AppSettings _settings;
    private readonly ISecretProtector _protector;
    private readonly BangumiClient _bangumi;
    private readonly IDoubanScrobblingClient _douban;
    private readonly IScrobblingIdentityLookup? _identityLookup;
    private readonly Action<ScrobblingProvider?, ScrobblingRecordOutcome, string>? _notifyIssue;
    private readonly TimeSpan _syncTimeout;
    private readonly object _recordsGate = new();
    private readonly List<ScrobblingRecord> _records = [];
    private Snapshot _snapshot;
    private string _lastStatus = "尚未同步观看记录";
    private volatile bool _disposed;
    private bool _doubanSessionChanging;

    private sealed record Snapshot(bool Bangumi, string Token, bool Douban,
        CancellationTokenSource Lifetime, CancellationToken Cancellation);
    private sealed record PlayingEpisode(string Id, string? SeriesId, string? SeasonId, int? Season, int? Number,
        int? LastNumber, string Title)
    {
        internal bool Complete => !string.IsNullOrWhiteSpace(SeriesId) && Season.HasValue && Number.HasValue;
        internal bool Regular => Season is > 0 && Number is > 0 && !(LastNumber > Number);
    }

    private sealed record PlaybackMatch(PlayingEpisode Episode, EmbyItem Series, MoviePilotScrobblingMatch Identity);
    private sealed record SyncResult(ScrobblingRecordOutcome Outcome, string Detail);

    public ScrobblingService(AppSettings settings, ISecretProtector protector, BangumiClient bangumi, IDoubanScrobblingClient douban)
        : this(settings, protector, bangumi, douban, null)
    {
    }

    internal ScrobblingService(AppSettings settings, ISecretProtector protector, BangumiClient bangumi,
        IDoubanScrobblingClient douban, Action<ScrobblingProvider?, ScrobblingRecordOutcome, string>? notifyIssue,
        TimeSpan? syncTimeout = null, IScrobblingIdentityLookup? identityLookup = null)
    {
        _settings = settings;
        _protector = protector;
        _bangumi = bangumi;
        _douban = douban;
        _notifyIssue = notifyIssue;
        _identityLookup = identityLookup;
        _syncTimeout = syncTimeout ?? TimeSpan.FromSeconds(45);
        _snapshot = Capture();
    }

    public string LastStatus => Volatile.Read(ref _lastStatus);
    public event Action? Changed;

    public IReadOnlyList<ScrobblingRecord> GetRecords(ScrobblingProvider provider)
    {
        lock (_recordsGate) return _records.Where(record => record.Provider == provider).Reverse().ToArray();
    }

    public ScrobblingRecord? GetLatestRecord(ScrobblingProvider provider)
    {
        lock (_recordsGate) return _records.FindLast(record => record.Provider == provider);
    }

    public void SetDoubanSessionChanging(bool changing)
    {
        if (_disposed) return;
        _doubanSessionChanging = changing;
        ConfigurationChanged();
    }

    /// <summary>Called on the settings owner thread; old work loses permission before a new snapshot is published.</summary>
    public void ConfigurationChanged()
    {
        if (_disposed) return;
        var next = Capture();
        var previous = Interlocked.Exchange(ref _snapshot, next);
        previous.Lifetime.Cancel();
        previous.Lifetime.Dispose();
        ResetConnections();
        SetStatus("同步配置已更新，将用于下一次播放");
    }

    private Snapshot Capture()
    {
        var settings = _settings.Scrobbling;
        var lifetime = new CancellationTokenSource();
        return new(settings.Bangumi.Enabled, settings.Bangumi.ProtectedAccessToken,
            settings.Douban.Enabled && !_doubanSessionChanging, lifetime, lifetime.Token);
    }

    public PlaybackScrobblingAttempt? CreateAttempt(PlaybackTicket ticket, EmbySessionScope scope, PlaybackRequest request)
    {
        var item = ticket.Item;
        if (_disposed || request.AudioOnly || item.Type != EmbyItemType.Episode) return null;
        var snapshot = Volatile.Read(ref _snapshot);
        var sourceEpisode = new PlayingEpisode(item.Id, item.SeriesId, item.SeasonId, item.ParentIndexNumber,
            item.IndexNumber, item.IndexNumberEnd, item.SeriesName ?? item.Name);
        if (string.IsNullOrWhiteSpace(sourceEpisode.Id)) return null;
        var bangumi = snapshot.Bangumi && snapshot.Token.Length > 0 && sourceEpisode.Season is not <= 0
            && sourceEpisode.Number is not <= 0 && !(sourceEpisode.LastNumber > sourceEpisode.Number);
        var douban = snapshot.Douban;
        if (!bangumi && !douban) return null;
        var stamp = _identityLookup?.ScrobblingConnectionStamp ?? "";
        bool Allowed() => !_disposed && scope.IsAvailable && !snapshot.Cancellation.IsCancellationRequested
            && ReferenceEquals(Volatile.Read(ref _snapshot), snapshot);
        bool Current() => Allowed() && _identityLookup?.IsCurrentScrobblingConnection(stamp) == true;
        bool CanSync() => stamp.Length == 0 ? Allowed() : Current();
        Task<LibraryEpisode?>? libraryTask = null;
        PlaybackMatch? doubanMatch = null;
        var doubanWatched = false;
        string? lastLookupIssue = null;

        void RecordShared(ScrobblingRecordOutcome outcome, string reason)
        {
            if (bangumi) AddRecord(ScrobblingProvider.Bangumi, sourceEpisode.Title, sourceEpisode.Number, outcome, reason);
            if (douban) AddRecord(ScrobblingProvider.Douban, sourceEpisode.Title, null, outcome, reason);
            NotifyIssue(bangumi && douban ? null : bangumi ? ScrobblingProvider.Bangumi : ScrobblingProvider.Douban, outcome, reason);
        }

        async Task<LibraryEpisode?> ReadLibraryAsync(CancellationToken token)
        {
            var library = await RunStageAsync(
                () => scope.ExecuteAsync((client, libraryToken) => ReadEpisodeAsync(client, sourceEpisode, libraryToken), token),
                CanSync, token, _ => "自动匹配读取媒体库信息失败，已跳过同步", "读取媒体库信息超时，已跳过同步",
                reason => RecordShared(ScrobblingRecordOutcome.Failed, reason)).ConfigureAwait(false);
            if (!CanSync() || token.IsCancellationRequested || library is null) return null;
            if (library.Series is null)
            {
                RecordShared(ScrobblingRecordOutcome.Skipped, "媒体库无法确认当前播放的季集，已跳过同步");
                return null;
            }

            if (stamp.Length == 0)
            {
                RecordShared(ScrobblingRecordOutcome.Skipped, "请先连接 MoviePilot，已跳过打格子");
                return null;
            }
            return library;
        }

        async Task<PlaybackMatch?> MatchAsync(LibraryEpisode library, ScrobblingProvider provider, bool requireEpisode, CancellationToken token)
        {
            if (_identityLookup is null || library.Series is not { } series || !Current()) return null;
            var episode = library.Episode;
            var forBangumi = provider == ScrobblingProvider.Bangumi;
            if (requireEpisode && !episode.Regular)
            {
                if (forBangumi) RecordIssue(provider, episode, ScrobblingRecordOutcome.Skipped, "对应集号无法确认，已跳过");
                return null;
            }

            void RecordLookupIssue(ScrobblingRecordOutcome outcome, string reason)
            {
                AddRecord(provider, episode.Title, forBangumi ? episode.Number : null, outcome, reason);
                if (Interlocked.Exchange(ref lastLookupIssue, reason) != reason)
                    NotifyIssue(provider, outcome, reason);
            }

            var season = Math.Max(0, episode.Season ?? 0);
            var number = requireEpisode ? episode.Number!.Value : 0;
            var identity = await RunStageAsync(async () => new LookupResult(
                    await _identityLookup.ResolveScrobblingEpisodeAsync(series.Name, series.OriginalTitle,
                        series.ProductionYear ?? series.PremiereDate?.Year, series.ProviderIds,
                        season, number, forBangumi, !forBangumi, token, stamp).ConfigureAwait(false)),
                Current, token, _ => "MoviePilot 打格子识别失败，请检查连接和媒体资料",
                "MoviePilot 打格子识别超时，请检查连接后重试",
                reason => RecordLookupIssue(ScrobblingRecordOutcome.Failed, reason)).ConfigureAwait(false);
            if (identity is null || !Current() || token.IsCancellationRequested) return null;
            if (identity.Match is not { } match || match.Season != season || match.Episode != number)
            {
                RecordLookupIssue(ScrobblingRecordOutcome.Skipped, "MoviePilot 无法确认播放作品或季集，已跳过同步");
                return null;
            }
            return match.ConnectionStamp == stamp ? new(episode, series, match) : null;
        }

        async Task Sync(bool completed, CancellationToken token)
        {
            if (!completed && !douban) return;
            var library = await (libraryTask ??= ReadLibraryAsync(token)).WaitAsync(token).ConfigureAwait(false);
            if (library is null || !Current()) return;

            if (!completed)
            {
                var match = await MatchAsync(library, ScrobblingProvider.Douban, requireEpisode: false, token).ConfigureAwait(false);
                if (match is not null && await SyncDoubanAsync(match, false, Current, token).ConfigureAwait(false))
                    doubanMatch = match;
                return;
            }

            await Task.WhenAll(
                bangumi ? SyncBangumiForEpisodeAsync(library, token) : Task.CompletedTask,
                !doubanWatched && doubanMatch is { } started ? FinishDoubanAsync(library, started, token) : Task.CompletedTask)
                .ConfigureAwait(false);
        }

        async Task SyncBangumiForEpisodeAsync(LibraryEpisode library, CancellationToken token)
        {
            var match = await MatchAsync(library, ScrobblingProvider.Bangumi, requireEpisode: true, token).ConfigureAwait(false);
            if (match is not null) await SyncBangumiAsync(snapshot, scope, match, Current, token).ConfigureAwait(false);
        }

        async Task FinishDoubanAsync(LibraryEpisode library, PlaybackMatch started, CancellationToken token)
        {
            if (!started.Episode.Regular || started.Identity.DoubanId is not { } subjectId) return;
            var native = started.Identity.TmdbId is null && started.Identity.MediaSource == "douban";
            if (native && started.Identity.Season == 1)
            {
                await SyncDoubanAsync(started, true, Current, token, started.Episode.Number).ConfigureAwait(false);
                return;
            }

            var match = await MatchAsync(library, ScrobblingProvider.Douban, requireEpisode: true, token).ConfigureAwait(false);
            if (match is null) return;
            if (match.Identity.TmdbSeasonLookupFailed || match.Identity.DoubanLookupFailed)
            {
                RecordIssue(ScrobblingProvider.Douban, match.Episode, ScrobblingRecordOutcome.Failed,
                    "MoviePilot 核实豆瓣末集失败，请检查连接和豆瓣数据源");
                return;
            }
            if (match.Identity.DoubanId != subjectId || match.Identity.TmdbId != started.Identity.TmdbId
                || match.Identity.MediaSource != started.Identity.MediaSource)
            {
                RecordIssue(ScrobblingProvider.Douban, match.Episode, ScrobblingRecordOutcome.Skipped,
                    "MoviePilot 未能核实起播时的豆瓣作品，已保留在看状态");
                return;
            }

            // Season joins contain no episode offset within the Douban subject.
            var lastEpisode = native ? match.Identity.DoubanEpisode
                : match.Identity.TmdbEpisodeId.HasValue && match.Identity.TmdbSeasonLastEpisode == match.Identity.Episode
                    ? match.Identity.Episode : (int?)null;
            if (lastEpisode is > 0) await SyncDoubanAsync(match, true, Current, token, lastEpisode).ConfigureAwait(false);
        }

        async Task<bool> SyncDoubanAsync(PlaybackMatch match, bool completed, Func<bool> current, CancellationToken token, int? lastEpisode = null)
        {
            const ScrobblingProvider provider = ScrobblingProvider.Douban;
            if (match.Identity.DoubanId is not { } subjectId)
            {
                if (match.Identity.DoubanLookupFailed)
                    RecordIssue(provider, match.Episode, ScrobblingRecordOutcome.Failed, "MoviePilot 查询豆瓣作品失败，请检查连接和豆瓣数据源");
                else RecordIssue(provider, match.Episode, ScrobblingRecordOutcome.Skipped, "MoviePilot 未确认对应的豆瓣作品，已跳过同步");
                return false;
            }

            var result = await RunStageAsync(async () =>
                {
                    var watched = await _douban.MarkEpisodeAsync(subjectId, lastEpisode ?? 1,
                        completed, current, token).ConfigureAwait(false);
                    return new SyncResult(watched ? ScrobblingRecordOutcome.Watched : ScrobblingRecordOutcome.Watching,
                        watched ? "豆瓣已标记看过" : "豆瓣已标记在看");
                }, current, token, error => ProviderFailure(provider, error, "未能确认同步结果，请到站点核对"),
                "同步超时，结果未确认，请到站点核对",
                reason => RecordIssue(provider, match.Episode, ScrobblingRecordOutcome.Failed, reason)).ConfigureAwait(false);
            if (result is null || !current()) return false;
            doubanWatched = result.Outcome == ScrobblingRecordOutcome.Watched;
            if (!completed || doubanWatched)
                AddRecord(provider, match.Episode.Title, null, result.Outcome, result.Detail);
            return true;
        }

        return new(request.RunTimeTicks, PlaybackService.StopReportPercent(request.IsDonghua,
            _settings.Playback.MarkWatchedPercent, _settings.Playback.DonghuaMarkWatchedPercent),
            CanSync, Sync, scope.Lifetime, syncTimeout: _syncTimeout, additionalLifetime: snapshot.Cancellation);
    }

    private sealed record LookupResult(MoviePilotScrobblingMatch? Match);
    private sealed record LibraryEpisode(PlayingEpisode Episode, EmbyItem? Series);

    private static async Task<LibraryEpisode?> ReadEpisodeAsync(EmbyClient client, PlayingEpisode episode, CancellationToken token)
    {
        if (!episode.Complete)
        {
            var details = await client.GetItemAsync(episode.Id, token, "SeriesName,SeriesId,SeasonId,ParentIndexNumber,IndexNumber,IndexNumberEnd")
                .ConfigureAwait(false);
            if (details.Type != EmbyItemType.Episode || details.Id != episode.Id
                || (!string.IsNullOrWhiteSpace(episode.SeriesId) && !string.IsNullOrWhiteSpace(details.SeriesId)
                    && episode.SeriesId != details.SeriesId)
                || (!string.IsNullOrWhiteSpace(episode.SeasonId) && !string.IsNullOrWhiteSpace(details.SeasonId)
                    && episode.SeasonId != details.SeasonId)
                || (episode.Season.HasValue && details.ParentIndexNumber.HasValue && episode.Season != details.ParentIndexNumber)
                || (episode.Number.HasValue && details.IndexNumber.HasValue && episode.Number != details.IndexNumber))
                return new(episode, null);
            episode = episode with
            {
                SeriesId = string.IsNullOrWhiteSpace(details.SeriesId) ? episode.SeriesId : details.SeriesId,
                SeasonId = details.SeasonId ?? episode.SeasonId,
                Season = details.ParentIndexNumber ?? episode.Season,
                Number = details.IndexNumber ?? episode.Number,
                LastNumber = details.IndexNumberEnd ?? episode.LastNumber,
                Title = details.SeriesName ?? episode.Title
            };
        }
        if (string.IsNullOrWhiteSpace(episode.SeriesId)) return new(episode, null);
        var series = await client.GetItemAsync(episode.SeriesId!, token, "ProviderIds,OriginalTitle,ProductionYear,PremiereDate,Status")
            .ConfigureAwait(false);
        return new(episode with { Title = series.Name },
            series.Type == EmbyItemType.Series && series.Id == episode.SeriesId ? series : null);
    }

    private async Task SyncBangumiAsync(Snapshot snapshot, EmbySessionScope scope, PlaybackMatch match, Func<bool> current,
        CancellationToken token)
    {
        const ScrobblingProvider provider = ScrobblingProvider.Bangumi;
        if (!current()) return;
        if (match.Identity.BangumiLookupFailed && match.Identity.BangumiId is null)
        {
            RecordIssue(provider, match.Episode, ScrobblingRecordOutcome.Failed, "MoviePilot 查询 Bangumi 作品失败，请检查连接和 Bangumi 数据源");
            return;
        }
        var result = await RunStageAsync(() => FindBangumiEpisodeAsync(snapshot, scope, match, token), current, token,
            error => ProviderFailure(provider, error, "Bangumi 单集核对失败，已跳过同步"), "查询作品超时，已跳过同步",
            reason => RecordIssue(provider, match.Episode, ScrobblingRecordOutcome.Failed, reason)).ConfigureAwait(false);
        if (result is null || !current()) return;
        if (result.EpisodeId is not { } episodeId)
        {
            RecordIssue(provider, match.Episode, ScrobblingRecordOutcome.Skipped,
                result.SkipReason ?? "对应集号无法确认，已跳过");
            return;
        }

        var written = await RunStageAsync(async () =>
            {
                var access = _protector.Unprotect(snapshot.Token);
                await _bangumi.MarkEpisodeAsync(result.SubjectId, episodeId, access, current, token).ConfigureAwait(false);
                return new SyncResult(ScrobblingRecordOutcome.Watched, "对应集已标记看过");
            }, current, token, error => ProviderFailure(provider, error, "未能确认同步结果，请到站点核对"),
            "同步超时，结果未确认，请到站点核对",
            reason => RecordIssue(provider, match.Episode, ScrobblingRecordOutcome.Failed, reason)).ConfigureAwait(false);
        if (written is not null && current())
            AddRecord(provider, match.Episode.Title, match.Episode.Number, written.Outcome, written.Detail);
    }

    private sealed record BangumiTarget(long SubjectId, long? EpisodeId, string? SkipReason = null);

    private async Task<BangumiTarget?> FindBangumiEpisodeAsync(Snapshot snapshot, EmbySessionScope scope,
        PlaybackMatch match, CancellationToken token)
    {
        var identity = match.Identity;
        var access = _protector.Unprotect(snapshot.Token);
        if (identity.TmdbEpisodeId is not null && identity.TmdbId is { } tmdbId)
        {
            if (BangumiTmdbMap.Find(tmdbId, identity.Season, identity.Episode) is { } mapped)
            {
                var subject = await _bangumi.FindSubjectAsync(mapped.SubjectId, access, token).ConfigureAwait(false);
                if (subject is not null &&
                    await _bangumi.ConfirmRegularEpisodeAsync(mapped.SubjectId, mapped.EpisodeId, access, token).ConfigureAwait(false))
                    return new(mapped.SubjectId, mapped.EpisodeId);
                return new(0, null, "Bangumi 无法核实 TMDB 对应的单集，已跳过同步");
            }
            if (BangumiTmdbMap.ContainsSeries(tmdbId))
                return new(0, null, "没有找到 TMDB 对应的唯一 Bangumi 单集，已跳过同步");
        }
        if (identity.BangumiId is not { } id)
            return new(0, null, "MoviePilot 未确认对应的 Bangumi 作品，已跳过同步");
        // Series-level identity alone cannot convert a second season or a split season.
        if (identity.Season != 1 || identity.TmdbId.HasValue && (identity.TmdbEpisodeId is null || identity.TmdbSeasonLastEpisode is null))
            return new(0, null, "对应集号无法确认，已跳过");
        var subjectInfo = await _bangumi.SubjectAsync(id, access, token).ConfigureAwait(false);
        var count = identity.TmdbSeasonLastEpisode ?? await scope.ExecuteAsync(
            (client, libraryToken) => ReadSingleFirstSeasonCountAsync(client, match.Episode, libraryToken), token).ConfigureAwait(false);
        if (count is not > 0 || subjectInfo.EpisodeCount is not { } siteCount
            || siteCount != count && !(siteCount > count && string.Equals(match.Series.Status, "Continuing", StringComparison.OrdinalIgnoreCase)))
            return new(0, null, "站点总集数与媒体库不一致，已跳过同步");
        var episodes = await _bangumi.GetEpisodesAsync(id, access, token).ConfigureAwait(false);
        return new(id, episodes.SingleOrDefault(episode => episode.Number == identity.Episode)?.Id);
    }

    private static async Task<int?> ReadSingleFirstSeasonCountAsync(EmbyClient client, PlayingEpisode episode, CancellationToken token)
    {
        var seasons = await client.GetSeasonsAsync(episode.SeriesId!, token).ConfigureAwait(false);
        if (seasons.Where(season => season.IndexNumber != 0).ToArray() is not
            [{ Type: EmbyItemType.Season, IndexNumber: 1 } season] || string.IsNullOrWhiteSpace(season.Id)) return null;
        var episodes = await client.GetEpisodesAsync(episode.SeriesId!, season.Id, token).ConfigureAwait(false);
        var regular = episodes.Where(item => item.ParentIndexNumber != 0).ToArray();
        if (regular.Length is 0 or > 1000
            || regular.Any(item => item.Type != EmbyItemType.Episode || item.IndexNumber is not > 0
                || item.IndexNumberEnd > item.IndexNumber || item.ParentIndexNumber is not (null or 1))
            || !regular.Select(item => item.IndexNumber!.Value).Order().SequenceEqual(Enumerable.Range(1, regular.Length))
            || regular.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != regular.Length
            || !regular.Any(item => item.Id == episode.Id && item.IndexNumber == episode.Number)) return null;
        return regular.Length;
    }

    private void RecordIssue(ScrobblingProvider provider, PlayingEpisode episode, ScrobblingRecordOutcome outcome, string reason)
    {
        AddRecord(provider, episode.Title, provider == ScrobblingProvider.Bangumi ? episode.Number : null, outcome, reason);
        NotifyIssue(provider, outcome, reason);
    }

    private static async Task<T?> RunStageAsync<T>(Func<Task<T?>> work, Func<bool> allowed, CancellationToken token,
        Func<Exception, string> failure, string timeout, Action<string> report) where T : class
    {
        if (!allowed() || token.IsCancellationRequested) return null;
        var finished = 0;
        void FailOnce(string reason)
        {
            if (Interlocked.Exchange(ref finished, 1) == 0 && allowed()) report(reason);
        }
        // 来源或配置失效时 allowed 已为 false。只有播放场次自身的截止时间才记为超时。
        using var deadline = token.Register(() => FailOnce(timeout));
        try
        {
            if (token.IsCancellationRequested || !allowed()) return null;
            var result = await work().ConfigureAwait(false);
            if (token.IsCancellationRequested) { FailOnce(timeout); return null; }
            return Interlocked.Exchange(ref finished, 1) == 0 && allowed() ? result : null;
        }
        catch (OperationCanceledException) { FailOnce(timeout); }
        catch (Exception error) { FailOnce(token.IsCancellationRequested ? timeout : failure(error)); }
        return null;
    }

    private static string ProviderFailure(ScrobblingProvider provider, Exception error, string fallback) =>
        error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
            ? provider == ScrobblingProvider.Bangumi ? "Bangumi 拒绝请求，请检查令牌和权限" : "豆瓣拒绝请求，请重新登录"
            : fallback;

    private static string ProviderName(ScrobblingProvider provider) => provider == ScrobblingProvider.Bangumi ? "Bangumi" : "豆瓣";

    public async Task<ScrobblingSubject> SubjectAsync(ScrobblingProvider provider, long id, CancellationToken token)
    {
        if (provider == ScrobblingProvider.Douban) return await _douban.SubjectAsync(id, token).ConfigureAwait(false);
        var snapshot = Volatile.Read(ref _snapshot);
        return await _bangumi.SubjectAsync(id, _protector.Unprotect(snapshot.Token), token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(ScrobblingProvider provider, string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        if (provider == ScrobblingProvider.Douban) return await _douban.SearchSubjectsAsync(query.Trim(), token).ConfigureAwait(false);
        var snapshot = Volatile.Read(ref _snapshot);
        if (!snapshot.Bangumi || snapshot.Token.Length == 0) throw new InvalidOperationException("请先启用 Bangumi 并配置 Access Token");
        return await _bangumi.SearchSubjectsAsync(query.Trim(), _protector.Unprotect(snapshot.Token), token).ConfigureAwait(false);
    }

    public void SetStatus(string message)
    {
        Volatile.Write(ref _lastStatus, message);
        try { Changed?.Invoke(); } catch (Exception) { /* Optional presentation only. */ }
    }

    private void NotifyIssue(ScrobblingProvider? provider, ScrobblingRecordOutcome outcome, string reason)
    {
        if (!_settings.Scrobbling.NotifyOnFailure) return;
        try { _notifyIssue?.Invoke(provider, outcome, reason); }
        catch (Exception) { /* The optional notification sink cannot change sync results or interrupt playback. */ }
    }

    private void AddRecord(ScrobblingProvider provider, string? title, int? episodeNumber,
        ScrobblingRecordOutcome outcome, string detail)
    {
        var safeTitle = CleanRecordText(title, 160);
        var safeDetail = CleanRecordText(detail, 240);
        var record = new ScrobblingRecord(DateTimeOffset.UtcNow, provider, safeTitle, episodeNumber, outcome, safeDetail);
        lock (_recordsGate)
        {
            _records.Add(record);
            var providerRecords = _records.Count(item => item.Provider == provider);
            if (providerRecords > RecordLimitPerProvider)
            {
                var oldest = _records.FindIndex(item => item.Provider == provider);
                if (oldest >= 0) _records.RemoveAt(oldest);
            }
        }
        SetStatus(ProviderName(provider) + "：" + safeDetail);
    }

    private static string CleanRecordText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "未知作品";
        var clean = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _snapshot.Lifetime.Cancel();
        _snapshot.Lifetime.Dispose();
    }
}

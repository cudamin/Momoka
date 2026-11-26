using System.Net;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
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
    private readonly Action<ScrobblingProvider?, string>? _notifyFailure;
    private readonly TimeSpan _syncTimeout;
    private readonly object _recordsGate = new();
    private readonly List<ScrobblingRecord> _records = [];
    private Snapshot _snapshot;
    private string _lastStatus = "尚未同步观看记录";
    private volatile bool _disposed;
    private bool _doubanSessionChanging;

    private sealed record Snapshot(bool Bangumi, string Token, bool Douban,
        CancellationTokenSource Lifetime, CancellationToken Cancellation);
    private sealed record PlayingEpisode(string Id, string SeriesId, int SeasonNumber, int Number, string Title);
    private sealed record SubjectMatch(ScrobblingSubject? Subject, string Title, string? SkipReason);
    private sealed record LibrarySeason(EmbyItem? Series, int EpisodeCount, string? SkipReason);
    private sealed record SyncResult(ScrobblingRecordOutcome Outcome, string Detail);

    public ScrobblingService(AppSettings settings, ISecretProtector protector, BangumiClient bangumi, IDoubanScrobblingClient douban)
        : this(settings, protector, bangumi, douban, null)
    {
    }

    internal ScrobblingService(AppSettings settings, ISecretProtector protector, BangumiClient bangumi,
        IDoubanScrobblingClient douban, Action<ScrobblingProvider?, string>? notifyFailure, TimeSpan? syncTimeout = null)
    {
        _settings = settings;
        _protector = protector;
        _bangumi = bangumi;
        _douban = douban;
        _notifyFailure = notifyFailure;
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
        if (_disposed || request.AudioOnly || item.Type != EmbyItemType.Episode
            || item.IndexNumber is not int episodeNumber || episodeNumber <= 0
            || item.ParentIndexNumber is not int seasonNumber || seasonNumber <= 0
            || string.IsNullOrWhiteSpace(item.SeriesId)
            || item.IndexNumberEnd is { } last && last > episodeNumber) return null;
        var snapshot = Volatile.Read(ref _snapshot);
        var playing = new PlayingEpisode(item.Id, item.SeriesId, seasonNumber, episodeNumber, item.SeriesName ?? item.Name);
        var providers = new List<ScrobblingProvider>();
        if (snapshot.Bangumi && snapshot.Token.Length > 0)
            providers.Add(ScrobblingProvider.Bangumi);
        if (snapshot.Douban)
            providers.Add(ScrobblingProvider.Douban);
        if (providers.Count == 0)
        {
            if (snapshot.Bangumi || snapshot.Douban) SetStatus("请先配置 Bangumi Access Token");
            return null;
        }
        var lifetime = snapshot.Cancellation;
        bool Allowed() => !_disposed && scope.IsAvailable && !lifetime.IsCancellationRequested
            && ReferenceEquals(Volatile.Read(ref _snapshot), snapshot);
        Task<LibrarySeason?>? libraryRead = null;
        Task<SubjectMatch?>? doubanMatch = null;
        var lookupGate = new object();
        var doubanWatchingUnconfirmed = 0;

        void RecordLibraryResult(string title, ScrobblingRecordOutcome outcome, string reason)
        {
            foreach (var provider in providers)
                AddRecord(provider, title, playing.Number, outcome, reason);
            if (outcome == ScrobblingRecordOutcome.Failed)
                NotifyFailure(providers.Count == 1 ? providers[0] : null, reason);
        }

        async Task Sync(bool completed, CancellationToken cancellationToken)
        {
            if (!completed && !snapshot.Douban) return;
            Task<LibrarySeason?> read;
            lock (lookupGate) read = libraryRead ??= ReadAsync();
            var library = await read.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!Allowed() || cancellationToken.IsCancellationRequested || library?.Series is not { } series) return;

            await Task.WhenAll(providers.Where(provider => completed || provider == ScrobblingProvider.Douban)
                .Select(SyncProviderAsync)).ConfigureAwait(false);

            async Task<LibrarySeason?> ReadAsync()
            {
                var season = await RunStageAsync(
                    () => scope.ExecuteAsync((client, token) => ReadLibrarySeasonAsync(client, playing, token), cancellationToken),
                    Allowed, cancellationToken,
                    _ => "自动匹配读取媒体库信息失败，已跳过同步", "读取媒体库信息超时，已跳过同步",
                    reason => RecordLibraryResult(playing.Title, ScrobblingRecordOutcome.Failed, reason)).ConfigureAwait(false);
                if (season?.SkipReason is not { } skip) return season;
                if (Allowed() && !cancellationToken.IsCancellationRequested)
                    RecordLibraryResult(season.Series?.Name ?? playing.Title, ScrobblingRecordOutcome.Skipped, skip);
                return null;
            }

            async Task SyncProviderAsync(ScrobblingProvider provider)
            {
                void Failed(string title, string reason)
                {
                    AddRecord(provider, title, playing.Number, ScrobblingRecordOutcome.Failed, reason);
                    NotifyFailure(provider, reason);
                }
                async Task<SubjectMatch?> MatchAsync()
                {
                    var found = await RunStageAsync(
                        () => FindMatchAsync(snapshot, provider, series, library.EpisodeCount, Allowed, cancellationToken),
                        Allowed, cancellationToken, error => ProviderFailure(provider, error, "自动匹配失败，已跳过同步"),
                        "查询作品超时，已跳过同步", reason => Failed(series.Name, reason)).ConfigureAwait(false);
                    if (found?.SkipReason is not { } skip) return found;
                    if (Allowed()) AddRecord(provider, found.Title, playing.Number, ScrobblingRecordOutcome.Skipped, skip);
                    return null;
                }

                Task<SubjectMatch?> matchTask;
                if (provider == ScrobblingProvider.Douban)
                {
                    lock (lookupGate) matchTask = doubanMatch ??= MatchAsync();
                }
                else matchTask = MatchAsync();
                var match = await matchTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (match?.Subject is not { } subject) return;
                var douban = provider == ScrobblingProvider.Douban;
                if (douban && completed && (playing.Number != subject.EpisodeCount
                    || Volatile.Read(ref doubanWatchingUnconfirmed) != 0)) return;

                var result = await RunStageAsync(() =>
                    {
                        if (douban && !completed) Volatile.Write(ref doubanWatchingUnconfirmed, 1);
                        return WriteAsync(snapshot, provider, subject.Id, playing.Number, completed, Allowed, cancellationToken);
                    },
                    Allowed, cancellationToken, error => ProviderFailure(provider, error, "未能确认同步结果，请到站点核对"),
                    "同步超时，结果未确认，请到站点核对", reason => Failed(match.Title, reason)).ConfigureAwait(false);
                if (result is null || !Allowed()) return;
                if (douban && !completed) Volatile.Write(ref doubanWatchingUnconfirmed, 0);
                AddRecord(provider, match.Title, playing.Number, result.Outcome, result.Detail);
            }
        }
        return new(request.RunTimeTicks, PlaybackService.StopReportPercent(request.IsDonghua,
            _settings.Playback.MarkWatchedPercent, _settings.Playback.DonghuaMarkWatchedPercent),
            Allowed, Sync, scope.Lifetime, syncTimeout: _syncTimeout, additionalLifetime: snapshot.Cancellation);
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

    private async Task<SubjectMatch?> FindMatchAsync(Snapshot snapshot, ScrobblingProvider provider,
        EmbyItem series, int libraryEpisodeCount, Func<bool> allowed, CancellationToken token)
    {
        var id = await FindSubjectIdAsync(snapshot, provider, series, token).ConfigureAwait(false);
        if (!allowed() || token.IsCancellationRequested) return null;
        if (id is null) return new(null, series.Name, "没有找到标题、年份和集数均能唯一确认的作品，已跳过同步");
        var subject = provider == ScrobblingProvider.Bangumi
            ? await BangumiSubjectAsync(snapshot, id.Value, token).ConfigureAwait(false)
            : await _douban.SubjectAsync(id.Value, token).ConfigureAwait(false);
        if (!allowed() || token.IsCancellationRequested) return null;
        return subject.EpisodeCount is int episodeCount && SubjectCountMatches(episodeCount, libraryEpisodeCount, series.Status)
            ? new(subject, subject.Title, null)
            : new(null, subject.Title, "站点总集数与媒体库不一致，已跳过同步");
    }

    private async Task<SyncResult?> WriteAsync(Snapshot snapshot, ScrobblingProvider provider,
        long subjectId, int episodeNumber, bool completed, Func<bool> allowed, CancellationToken token)
    {
        if (provider == ScrobblingProvider.Bangumi)
        {
            var access = _protector.Unprotect(snapshot.Token);
            var episodes = await _bangumi.GetEpisodesAsync(subjectId, access, token).ConfigureAwait(false);
            var episode = episodes.SingleOrDefault(episode => episode.Number == episodeNumber);
            if (episode is null) return new(ScrobblingRecordOutcome.Skipped, "对应集号无法确认，已跳过");
            await _bangumi.MarkEpisodeAsync(subjectId, episode.Id, access, allowed, token).ConfigureAwait(false);
            return new(ScrobblingRecordOutcome.Watched, "对应集已标记看过");
        }
        var finished = await _douban.MarkEpisodeAsync(subjectId, episodeNumber, completed, allowed, token).ConfigureAwait(false);
        return finished ? new(ScrobblingRecordOutcome.Watched, "已标记看过") : new(ScrobblingRecordOutcome.Watching, "已标记在看");
    }

    private static string ProviderFailure(ScrobblingProvider provider, Exception error, string fallback) =>
        error is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
            ? provider == ScrobblingProvider.Bangumi ? "Bangumi 拒绝请求，请检查令牌和权限" : "豆瓣拒绝请求，请重新登录"
            : fallback;

    private static async Task<LibrarySeason?> ReadLibrarySeasonAsync(
        EmbyClient client, PlayingEpisode playing, CancellationToken token)
    {
        var series = await client.GetItemAsync(playing.SeriesId, token, "ProviderIds,OriginalTitle,ProductionYear,PremiereDate,Status")
            .ConfigureAwait(false);
        if (series.Type != EmbyItemType.Series || !string.Equals(series.Id, playing.SeriesId, StringComparison.Ordinal))
            return new(null, 0, "媒体服务器未返回对应剧集，已跳过同步");

        var seasons = await client.GetSeasonsAsync(playing.SeriesId, token).ConfigureAwait(false);
        var regularSeasons = seasons.Where(season => season.IndexNumber != 0).Take(2).ToArray();
        if (regularSeasons is not [{ IndexNumber: 1, Type: EmbyItemType.Season } regularSeason]
            || playing.SeasonNumber != 1 || string.IsNullOrWhiteSpace(regularSeason.Id)
            || seasons.Any(season => season.Type != EmbyItemType.Season))
            return new(series, 0, "无法确认唯一的常规第一季，已跳过同步");

        var episodes = await client.GetEpisodesAsync(playing.SeriesId, regularSeason.Id, token).ConfigureAwait(false);
        var numbers = new HashSet<int>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var currentEpisodeFound = false;
        const string InvalidNumbers = "媒体库集号不完整、不连续或存在合并集，已跳过同步";
        if (episodes.Count is 0 or > 1000) return new(series, 0, InvalidNumbers);
        foreach (var episode in episodes)
        {
            // 媒体服务器可以在常规季内显示穿插的特别篇，季 0 的集号不参与常规集映射。
            if (episode is { Type: EmbyItemType.Episode, ParentIndexNumber: 0 }) continue;
            if (episode.Type != EmbyItemType.Episode || string.IsNullOrWhiteSpace(episode.Id)
                || !ids.Add(episode.Id) || episode.ParentIndexNumber is { } parent && parent != 1
                || !string.IsNullOrWhiteSpace(episode.SeasonId) && episode.SeasonId != regularSeason.Id
                || episode.IndexNumber is not > 0 || episode.IndexNumberEnd is { } end && end > episode.IndexNumber
                || !numbers.Add(episode.IndexNumber.Value))
                return new(series, 0, InvalidNumbers);
            if (episode.IndexNumber == playing.Number && episode.Id == playing.Id)
                currentEpisodeFound = true;
        }
        if (numbers.Count == 0 || Enumerable.Range(1, numbers.Count).Any(number => !numbers.Contains(number)))
            return new(series, 0, InvalidNumbers);
        if (!currentEpisodeFound)
            return new(series, 0, "当前播放集与媒体库集号无法对应，已跳过同步");
        return new(series, numbers.Count, null);
    }

    private static bool SubjectCountMatches(int siteCount, int libraryCount, string? seriesStatus) =>
        siteCount == libraryCount || siteCount > libraryCount
            && string.Equals(seriesStatus, "Continuing", StringComparison.OrdinalIgnoreCase);

    private async Task<long?> FindSubjectIdAsync(Snapshot snapshot, ScrobblingProvider provider, EmbyItem series,
        CancellationToken cancellationToken)
    {
        var providerName = provider == ScrobblingProvider.Bangumi ? "Bangumi" : "Douban";
        var known = series.ProviderIds.FirstOrDefault(pair => pair.Key.Equals(providerName, StringComparison.OrdinalIgnoreCase)).Value;
        if (known is not null) return long.TryParse(known, out var parsed) && parsed > 0 ? parsed : null;

        var year = series.ProductionYear is > 0 ? series.ProductionYear : series.PremiereDate?.Year;
        if (year is null) return null;
        var titles = new[] { series.Name, series.OriginalTitle }.Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        if (titles.Length == 0) return null;
        var candidates = new Dictionary<long, ScrobblingCandidate>();
        foreach (var title in titles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = await SearchSubjectsAsync(snapshot, provider, title, cancellationToken).ConfigureAwait(false);
            foreach (var candidate in found) candidates.TryAdd(candidate.Id, candidate);
            if (candidates.Count > 20) return null;
        }
        var normalizedTitles = titles.Select(NormalizeTitle).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
        var matches = candidates.Values.Where(candidate => candidate.Year == year
                && (normalizedTitles.Contains(NormalizeTitle(StripYearSuffix(candidate.Title)))
                    || provider == ScrobblingProvider.Bangumi && candidate.Detail is { Length: > 0 } detail
                        && normalizedTitles.Contains(NormalizeTitle(detail))))
            .Select(candidate => candidate.Id).Distinct().Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task<ScrobblingSubject> BangumiSubjectAsync(Snapshot snapshot, long id, CancellationToken cancellationToken) =>
        await _bangumi.SubjectAsync(id, _protector.Unprotect(snapshot.Token), cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<ScrobblingCandidate>> SearchSubjectsAsync(Snapshot snapshot, ScrobblingProvider provider,
        string title, CancellationToken cancellationToken) => provider == ScrobblingProvider.Douban
            ? await _douban.SearchSubjectsAsync(title, cancellationToken).ConfigureAwait(false)
            : await _bangumi.SearchSubjectsAsync(title, _protector.Unprotect(snapshot.Token), cancellationToken).ConfigureAwait(false);

    private static string NormalizeTitle(string value) => new(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string StripYearSuffix(string value)
    {
        var title = value.Trim();
        if (title.Length < 6 || title[^1] is not (')' or '）')) return title;
        var open = title[^6] is '(' or '（' ? title.Length - 6 : -1;
        return open >= 0 && title.AsSpan(open + 1, 4).ToString().All(char.IsAsciiDigit) && open > 0
            ? title[..open].TrimEnd() : title;
    }

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

    private void NotifyFailure(ScrobblingProvider? provider, string reason)
    {
        if (!_settings.Scrobbling.NotifyOnFailure) return;
        try { _notifyFailure?.Invoke(provider, reason); }
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

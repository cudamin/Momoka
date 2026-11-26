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
    private readonly object _recordsGate = new();
    private readonly List<ScrobblingRecord> _records = [];
    private Snapshot _snapshot;
    private string _lastStatus = "尚未同步观看记录";
    private bool _disposed;
    private bool _doubanSessionChanging;

    private sealed record Snapshot(bool Bangumi, string Token, bool Douban,
        CancellationTokenSource Lifetime, CancellationToken Cancellation);
    private sealed record PlayingEpisode(string Id, string SeriesId, int SeasonNumber, int Number, string Title);
    private sealed record MatchedSubject(long Id, string Title, int EpisodeNumber, int EpisodeCount);

    public ScrobblingService(AppSettings settings, ISecretProtector protector, BangumiClient bangumi, IDoubanScrobblingClient douban)
    {
        _settings = settings;
        _protector = protector;
        _bangumi = bangumi;
        _douban = douban;
        _snapshot = Capture();
    }

    public string LastStatus => Volatile.Read(ref _lastStatus);
    public event Action? Changed;

    public IReadOnlyList<ScrobblingRecord> GetRecords(ScrobblingProvider provider)
    {
        lock (_recordsGate) return _records.Where(record => record.Provider == provider).Reverse().ToArray();
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
        Task<IReadOnlyDictionary<ScrobblingProvider, MatchedSubject?>>? lookup = null;
        var lookupGate = new object();
        Task<IReadOnlyDictionary<ScrobblingProvider, MatchedSubject?>> ResolveOnceAsync(CancellationToken cancellationToken)
        {
            lock (lookupGate) return lookup ??= ResolveSubjectsAsync(snapshot, scope, playing, providers, Allowed, cancellationToken);
        }
        async Task Sync(bool completed, CancellationToken cancellationToken)
        {
            using var configurationLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            cancellationToken = configurationLifetime.Token;
            if (!completed && !providers.Contains(ScrobblingProvider.Douban)) return;
            IReadOnlyDictionary<ScrobblingProvider, MatchedSubject?> matches;
            try { matches = await ResolveOnceAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !Allowed()) { return; }
            foreach (var provider in providers)
            {
                if (provider == ScrobblingProvider.Bangumi && !completed) continue;
                if (!matches.TryGetValue(provider, out var match) || match is null) continue;
                if (provider == ScrobblingProvider.Douban && completed && match.EpisodeNumber != match.EpisodeCount) continue;
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
                var token = linked.Token;
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!Allowed()) return;
                    var number = match.EpisodeNumber;
                    if (provider == ScrobblingProvider.Bangumi)
                    {
                        var access = _protector.Unprotect(snapshot.Token);
                        var episodes = await _bangumi.GetEpisodesAsync(match.Id, access, token).ConfigureAwait(false);
                        var episode = episodes.SingleOrDefault(episode => episode.Number == number);
                        if (episode is null)
                        {
                            AddRecord(provider, match.Title, number, ScrobblingRecordOutcome.Skipped, "对应集号无法确认，已跳过");
                            continue;
                        }
                        await _bangumi.MarkEpisodeAsync(match.Id, episode.Id, access, Allowed, token).ConfigureAwait(false);
                        if (!Allowed()) return;
                        AddRecord(provider, match.Title, number, ScrobblingRecordOutcome.Watched, "对应集已标记看过");
                    }
                    else
                    {
                        var finished = await _douban.MarkEpisodeAsync(match.Id, number, completed, Allowed, token).ConfigureAwait(false);
                        if (!Allowed()) return;
                        AddRecord(provider, match.Title, number,
                            finished ? ScrobblingRecordOutcome.Watched : ScrobblingRecordOutcome.Watching,
                            finished ? "已标记看过" : "已标记在看");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested || !Allowed()) { }
                catch (Exception)
                {
                    if (Allowed()) AddRecord(provider, match.Title, match.EpisodeNumber, ScrobblingRecordOutcome.Failed,
                        provider == ScrobblingProvider.Bangumi
                            ? "Bangumi 同步失败，请检查令牌和网络" : "豆瓣同步失败，请重新登录或检查网络");
                }
            }
        }
        return new(request.RunTimeTicks, PlaybackService.StopReportPercent(request.IsDonghua,
            _settings.Playback.MarkWatchedPercent, _settings.Playback.DonghuaMarkWatchedPercent),
            Allowed, Sync, scope.Lifetime, SetStatus, TimeSpan.FromSeconds(45), snapshot.Cancellation);
    }

    private async Task<IReadOnlyDictionary<ScrobblingProvider, MatchedSubject?>> ResolveSubjectsAsync(Snapshot snapshot,
        EmbySessionScope scope, PlayingEpisode playing, IReadOnlyList<ScrobblingProvider> providers,
        Func<bool> allowed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<ScrobblingProvider, MatchedSubject?>();
        void RecordLibraryResult(string title, ScrobblingRecordOutcome outcome, string reason)
        {
            // 起播和完成共用一次媒体库读取，同一个失败或跳过原因每站只写一条。
            if (!allowed() || cancellationToken.IsCancellationRequested) return;
            foreach (var provider in providers)
                AddRecord(provider, title, playing.Number, outcome, reason);
        }

        (EmbyItem? Series, int EpisodeCount, string? SkipReason) library;
        try
        {
            library = await scope.ExecuteAsync((client, token) => ReadLibrarySeasonAsync(client, playing, token),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !allowed()) { throw; }
        catch (Exception)
        {
            RecordLibraryResult(playing.Title, ScrobblingRecordOutcome.Failed, "自动匹配读取媒体库信息失败，已跳过同步");
            return result;
        }
        if (library.SkipReason is { } reason)
        {
            RecordLibraryResult(library.Series?.Name ?? playing.Title, ScrobblingRecordOutcome.Skipped, reason);
            return result;
        }
        var series = library.Series!;
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var subjectId = await FindSubjectIdAsync(snapshot, provider, series, cancellationToken)
                    .ConfigureAwait(false);
                if (subjectId is null)
                {
                    result[provider] = null;
                    AddRecord(provider, series.Name, playing.Number, ScrobblingRecordOutcome.Skipped,
                        "没有找到标题、年份和集数均能唯一确认的作品，已跳过同步");
                    continue;
                }
                var subject = provider == ScrobblingProvider.Bangumi
                    ? await BangumiSubjectAsync(snapshot, subjectId.Value, cancellationToken).ConfigureAwait(false)
                    : await _douban.SubjectAsync(subjectId.Value, cancellationToken).ConfigureAwait(false);
                if (subject.EpisodeCount is not int episodeCount || !SubjectCountMatches(episodeCount, library.EpisodeCount, series.Status))
                {
                    result[provider] = null;
                    AddRecord(provider, subject.Title, playing.Number, ScrobblingRecordOutcome.Skipped,
                        "站点总集数与媒体库不一致，已跳过同步");
                    continue;
                }
                result[provider] = new(subject.Id, subject.Title, playing.Number, episodeCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                result[provider] = null;
                AddRecord(provider, series.Name, playing.Number, ScrobblingRecordOutcome.Failed,
                    "自动匹配失败，已跳过同步");
            }
        }
        return result;
    }

    private static async Task<(EmbyItem? Series, int EpisodeCount, string? SkipReason)> ReadLibrarySeasonAsync(
        EmbyClient client, PlayingEpisode playing, CancellationToken token)
    {
        var series = await client.GetItemAsync(playing.SeriesId, token, "ProviderIds,OriginalTitle,ProductionYear,PremiereDate,Status")
            .ConfigureAwait(false);
        if (series.Type != EmbyItemType.Series || !string.Equals(series.Id, playing.SeriesId, StringComparison.Ordinal))
            return (null, 0, "媒体服务器未返回对应剧集，已跳过同步");

        var seasons = await client.GetSeasonsAsync(playing.SeriesId, token).ConfigureAwait(false);
        var regularSeasons = seasons.Where(season => season.IndexNumber != 0).Take(2).ToArray();
        if (regularSeasons is not [{ IndexNumber: 1, Type: EmbyItemType.Season } regularSeason]
            || playing.SeasonNumber != 1 || string.IsNullOrWhiteSpace(regularSeason.Id)
            || seasons.Any(season => season.Type != EmbyItemType.Season))
            return (series, 0, "无法确认唯一的常规第一季，已跳过同步");

        var episodes = await client.GetEpisodesAsync(playing.SeriesId, regularSeason.Id, token).ConfigureAwait(false);
        var numbers = new HashSet<int>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var currentEpisodeFound = false;
        const string InvalidNumbers = "媒体库集号不完整、不连续或存在合并集，已跳过同步";
        if (episodes.Count is 0 or > 1000) return (series, 0, InvalidNumbers);
        foreach (var episode in episodes)
        {
            // 媒体服务器可以在常规季内显示穿插的特别篇，季 0 的集号不参与常规集映射。
            if (episode is { Type: EmbyItemType.Episode, ParentIndexNumber: 0 }) continue;
            if (episode.Type != EmbyItemType.Episode || string.IsNullOrWhiteSpace(episode.Id)
                || !ids.Add(episode.Id) || episode.ParentIndexNumber is { } parent && parent != 1
                || !string.IsNullOrWhiteSpace(episode.SeasonId) && episode.SeasonId != regularSeason.Id
                || episode.IndexNumber is not > 0 || episode.IndexNumberEnd is { } end && end > episode.IndexNumber
                || !numbers.Add(episode.IndexNumber.Value))
                return (series, 0, InvalidNumbers);
            if (episode.IndexNumber == playing.Number && episode.Id == playing.Id)
                currentEpisodeFound = true;
        }
        if (numbers.Count == 0 || Enumerable.Range(1, numbers.Count).Any(number => !numbers.Contains(number)))
            return (series, 0, InvalidNumbers);
        if (!currentEpisodeFound)
            return (series, 0, "当前播放集与媒体库集号无法对应，已跳过同步");
        return (series, numbers.Count, null);
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

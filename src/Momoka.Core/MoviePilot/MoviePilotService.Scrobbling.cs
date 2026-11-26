using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Momoka.Scrobbling;

namespace Momoka.MoviePilot;

internal sealed record MoviePilotScrobblingMatch(string ConnectionStamp, string MediaSource, string MediaId,
    int? TmdbId, int Season, int Episode, long? TmdbEpisodeId, long? BangumiId, long? DoubanId, int? DoubanEpisode = null,
    int? TmdbSeasonLastEpisode = null, bool BangumiLookupFailed = false, bool DoubanLookupFailed = false,
    bool TmdbSeasonLookupFailed = false);

public sealed partial class MoviePilotService : IScrobblingIdentityLookup
{
    private const string ScrobblingSeriesType = "电视剧";
    private const string ScrobblingTmdb = "themoviedb";
    private const int ScrobblingSearchLimit = 30;

    internal string ScrobblingConnectionStamp
    {
        get
        {
            lock (_sessionGate)
                return CurrentScrobblingIdentity() is { } current ? ScrobblingStamp(current) : "";
        }
    }

    internal bool IsCurrentScrobblingConnection(string stamp) => stamp.Length > 0 && stamp == ScrobblingConnectionStamp;

    string IScrobblingIdentityLookup.ScrobblingConnectionStamp => ScrobblingConnectionStamp;
    bool IScrobblingIdentityLookup.IsCurrentScrobblingConnection(string stamp) => IsCurrentScrobblingConnection(stamp);

    Task<MoviePilotScrobblingMatch?> IScrobblingIdentityLookup.ResolveScrobblingEpisodeAsync(string title,
        string? originalTitle, int? year, IReadOnlyDictionary<string, string>? providerIds, int season, int episode,
        bool includeBangumi, bool includeDouban, CancellationToken ct, string expectedConnectionStamp) =>
        ResolveScrobblingEpisodeAsync(title, originalTitle, year, providerIds, season, episode,
            includeBangumi, includeDouban, ct, expectedConnectionStamp);

    /// <summary>仅用媒体库的父剧元数据及原始单集坐标，沿 MoviePilot v3 精确身份和已有关系核验。</summary>
    internal async Task<MoviePilotScrobblingMatch?> ResolveScrobblingEpisodeAsync(string title,
        string? originalTitle, int? year, IReadOnlyDictionary<string, string>? providerIds, int season, int episode,
        bool includeBangumi, bool includeDouban, CancellationToken ct, string? expectedConnectionStamp = null)
    {
        ct.ThrowIfCancellationRequested();
        var hasEpisode = season > 0 && episode > 0;
        if ((!includeDouban && (!includeBangumi || !hasEpisode)) || season is not (>= 0 and <= 10000) || episode is not (>= 0 and <= 10000)
            || ReadScrobblingAnchors(providerIds) is not { } anchors) return null;

        SessionIdentity? identity;
        lock (_sessionGate) identity = CurrentScrobblingIdentity();
        if (identity is null) return null;
        var stamp = ScrobblingStamp(identity);
        if (expectedConnectionStamp is not null && stamp != expectedConnectionStamp) return null;
        string[] titles = [.. new[] { ScrobblingTitle(title), ScrobblingTitle(originalTitle) }
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var result = await ResolveAsync().ConfigureAwait(false);
            CheckIdentity(identity, timeout.Token);
            return IsCurrentScrobblingConnection(stamp) ? result : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new MoviePilotUnreachableException("MoviePilot 打格子识别超时，请检查连接后重试");
        }
        catch (MoviePilotException error)
        {
            // 回应可以包含服务器原话、地址或令牌。业务层只拿本地文案和状态。
            var message = error.Status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "MoviePilot 拒绝识别请求，请检查登录和权限",
                _ => "MoviePilot 打格子识别失败，请检查连接和媒体资料"
            };
            throw new MoviePilotException(message, error.Status);
        }

        async Task<MoviePilotScrobblingMatch?> ResolveAsync()
        {
            // 只有分季站点编号的库，不能从同名 TMDB 父剧推断编号体系。首季仍可核验库内直给的站点条目。
            var hasSeriesAnchor = anchors.ContainsKey(ScrobblingTmdb) || anchors.ContainsKey("imdb") || anchors.ContainsKey("tvdb");
            if (season == 0 || !hasSeriesAnchor && (anchors.ContainsKey("bangumi") || anchors.ContainsKey("douban")))
            {
                var nativeBangumi = ReadTargetAsync(includeBangumi && hasEpisode && season == 1 && anchors.ContainsKey("bangumi"),
                    () => VerifyNativeSiteAsync("bangumi"));
                var nativeDouban = ReadTargetAsync(includeDouban && anchors.ContainsKey("douban"),
                    () => VerifyNativeSiteAsync("douban"));
                var native = await Task.WhenAll(nativeBangumi, nativeDouban).ConfigureAwait(false);
                var (bangumi, douban) = (native[0], native[1]);
                if (bangumi is (null, false) && douban is (null, false)) return null;
                var source = (bangumi.Id, douban.Id, bangumi.Failed) switch
                {
                    (not null, _, _) => "bangumi",
                    (_, not null, _) => "douban",
                    (_, _, true) => "bangumi",
                    _ => "douban"
                };
                return new(stamp, source, anchors[source], null, season, episode, null, bangumi.Id, douban.Id,
                    DoubanEpisode: douban.Id is not null && season > 1 && hasEpisode ? episode : null,
                    BangumiLookupFailed: bangumi.Failed, DoubanLookupFailed: douban.Failed);
            }

            var tmdbId = ScrobblingNumber(anchors.GetValueOrDefault(ScrobblingTmdb));
            if (tmdbId is null)
            {
                // IMDb / TVDB 是整部剧的锚点。分季、拆篇站点的 ID 无法证明原始 TMDB 集号。
                var externalIds = new HashSet<long>();
                foreach (var source in (string[])["imdb", "tvdb"])
                {
                    if (!anchors.TryGetValue(source, out var id)) continue;
                    var externalDetail = await DetailAsync(source, id).ConfigureAwait(false);
                    if (externalDetail is null) continue;
                    if (ReadScrobblingNumber(externalDetail.Value, "tmdb_id") is { } linked) externalIds.Add(linked);
                }
                if (externalIds.Count > 1) return null;
                if (externalIds.Count == 1) tmdbId = externalIds.Single();
            }
            var byTitle = tmdbId is null;
            tmdbId ??= await FindTmdbAsync().ConfigureAwait(false);
            if (tmdbId is not (> 0 and <= int.MaxValue)) return null;

            var idText = tmdbId.Value.ToString(CultureInfo.InvariantCulture);
            var tmdbDetail = await DetailAsync(ScrobblingTmdb, idText).ConfigureAwait(false);
            if (tmdbDetail is null) return null;
            var detail = tmdbDetail.Value;
            if (byTitle && (ScrobblingYear(detail) != year || !TitleAgrees(detail)
                || anchors.Count > 0 && !anchors.Any(pair => ScrobblingId(pair.Key,
                    MoviePilotSubscription.Text(detail, ScrobblingIdField(pair.Key))) == pair.Value))) return null;

            long? episodeId = null;
            int? seasonLastEpisode = null;
            var seasonLookupFailed = false;
            if (hasEpisode)
            {
                try
                {
                    var seasonEpisodes = await ReadAsync(FormattableString.Invariant($"tmdb/{tmdbId}/{season}")).ConfigureAwait(false);
                    if (seasonEpisodes.ValueKind == JsonValueKind.Array)
                    {
                        var episodes = seasonEpisodes.EnumerateArray()
                            .Where(item => MoviePilotSubscription.Number(item, "episode_number") == episode).Take(2).ToArray();
                        if (episodes.Length > 1) return null;
                        if (episodes is [var single])
                        {
                            if (MoviePilotSubscription.Number(single, "season_number") != season
                                || !ScrobblingFieldAgrees(single, "show_id", ScrobblingTmdb, idText, allowSentinel: false)) return null;
                            episodeId = ReadScrobblingNumber(single, "id");
                            if (episodeId is null) return null;
                            seasonLastEpisode = ReadScrobblingSeasonEnd(seasonEpisodes, detail, season, idText);
                        }
                    }
                }
                catch (MoviePilotException) when (includeDouban)
                {
                    CheckIdentity(identity, timeout.Token);
                    seasonLookupFailed = true;
                }
            }

            var tmdb = (int)tmdbId.Value;
            var bangumiTask = ReadTargetAsync(includeBangumi && episodeId is not null && BangumiTmdbMap.Find(tmdb, season, episode) is null,
                () => FindSiteAsync("bangumi", detail, idText));
            var doubanTask = ReadTargetAsync(includeDouban, () => FindSiteAsync("douban", detail, idText));
            var targets = await Task.WhenAll(bangumiTask, doubanTask).ConfigureAwait(false);
            if (episodeId is null && targets[1] is (null, false) && !seasonLookupFailed) return null;
            return new(stamp, ScrobblingTmdb, idText, tmdb, season, episode, episodeId, targets[0].Id, targets[1].Id,
                TmdbSeasonLastEpisode: seasonLastEpisode, BangumiLookupFailed: targets[0].Failed || includeBangumi && seasonLookupFailed,
                DoubanLookupFailed: targets[1].Failed, TmdbSeasonLookupFailed: seasonLookupFailed);
        }

        async Task<(long? Id, bool Failed)> ReadTargetAsync(bool enabled, Func<Task<long?>> read)
        {
            if (!enabled) return (null, false);
            try { return (await read().ConfigureAwait(false), false); }
            catch (MoviePilotException)
            {
                CheckIdentity(identity, timeout.Token);
                return (null, true);
            }
        }

        async Task<long?> FindTmdbAsync()
        {
            if (year is not (> 0 and <= 9999)) return null;
            var accepted = new HashSet<long>();
            foreach (var name in titles)
            {
                var candidates = await SearchAsync(ScrobblingTmdb, name).ConfigureAwait(false);
                if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() >= ScrobblingSearchLimit) return null;
                foreach (var candidate in candidates.EnumerateArray())
                    if (ScrobblingYear(candidate) == year && TitleAgrees(candidate)
                        && ScrobblingPrimaryId(candidate, ScrobblingTmdb) is { } id && AnchorsAgree(candidate, ScrobblingTmdb, id)
                        && ScrobblingNumber(id) is { } number) accepted.Add(number);
            }
            return accepted.Count == 1 ? accepted.Single() : null;
        }

        bool TitleAgrees(JsonElement data) => ScrobblingTitleAgrees(data, titles);

        async Task<long?> FindSiteAsync(string source, JsonElement tmdbDetail, string tmdbId)
        {
            var hasNativeId = anchors.TryGetValue(source, out var nativeId);
            if (nativeId is not null)
            {
                var direct = await VerifySiteAsync(source, nativeId, tmdbId, tmdbDetail, allowDirect: true).ConfigureAwait(false);
                if (direct.Id is not null || !direct.MapsToOtherSeason || source != "douban" || season <= 1) return direct.Id;
                // 共享关系已把库的豆瓣 ID 指向同一剧的另一季；候选仍须有当前季的共享关系。
            }
            var known = ScrobblingId(source, MoviePilotSubscription.Text(tmdbDetail, ScrobblingIdField(source)));
            if (!hasNativeId && known is not null
                && (await VerifySiteAsync(source, known, tmdbId, tmdbDetail).ConfigureAwait(false)).Id is { } idByLink) return idByLink;

            // 系列与每季的中文名常不同。优先使用明确季名，避免把同名首季当后续季。
            var seasonInfo = MoviePilotSubscription.Member(tmdbDetail, "season_info");
            var seasons = seasonInfo.ValueKind == JsonValueKind.Array ? MoviePilotMediaSeason.Parse(seasonInfo) : [];
            var seasonMetadata = seasons.FirstOrDefault(item => item.Number == season);
            var seasonName = seasonMetadata?.PickerLabel.Contains(" · ", StringComparison.Ordinal) == true
                ? ScrobblingTitle(seasonMetadata.Name) : null;
            var term = seasonName;
            if (term is null && titles.FirstOrDefault() is { } name)
                term = season == 1 ? name : ScrobblingTitle(FormattableString.Invariant($"{name} 第 {season} 季"));
            if (term is null) return null;

            string[] exactTitles = [];
            int? exactYear = null;
            if (source == "douban")
            {
                var seasonDate = DateOnly.TryParseExact(seasonMetadata?.AirDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var airDate) ? airDate.Year : (int?)null;
                if (season == 1)
                {
                    // 采用已回读的父剧资料。库和 MP 年份冲突时，只有已有关系可消歧。
                    var tmdbYear = ScrobblingYear(tmdbDetail);
                    if (year is null || tmdbYear is null || year == tmdbYear)
                    {
                        exactYear = tmdbYear ?? year;
                        exactTitles = [.. titles.Concat(ScrobblingTitles(tmdbDetail)).Distinct(StringComparer.OrdinalIgnoreCase)];
                    }
                }
                else if (seasonName is not null && !titles.Concat(ScrobblingTitles(tmdbDetail)).Contains(seasonName, StringComparer.OrdinalIgnoreCase)
                    && seasonInfo.EnumerateArray().Count(item => MoviePilotSubscription.Number(item, "season_number") == season) == 1)
                {
                    exactYear = seasonDate;
                    exactTitles = [seasonName];
                }
            }

            var exact = new HashSet<string>(StringComparer.Ordinal);
            var discovered = new HashSet<string>(StringComparer.Ordinal);
            string[] terms = season == 1 && source == "douban" && exactYear is not null
                ? [.. exactTitles.Prepend(term).Distinct(StringComparer.OrdinalIgnoreCase).Take(2)] : [term];
            foreach (var query in terms)
            {
                var candidates = await SearchAsync(source, query).ConfigureAwait(false);
                if (candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() >= ScrobblingSearchLimit) return null;
                foreach (var candidate in candidates.EnumerateArray())
                {
                    if (ScrobblingPrimaryId(candidate, source) is not { } id) continue;
                    discovered.Add(id);
                    if (exactYear is not null && ScrobblingYear(candidate) == exactYear && ScrobblingTitleAgrees(candidate, exactTitles))
                        exact.Add(id);
                }
            }

            // 先核实精确候选，其余仅用于查已有的强关联，每个站点最多回读四个。
            var ids = exact.Concat(discovered).Distinct(StringComparer.Ordinal)
                .Where(id => hasNativeId ? id != nativeId : id != known || exact.Contains(id)).Take(5).ToArray();
            if (ids.Length > 4 && (hasNativeId || exact.Count != 1)) return null;
            long? result = null;
            foreach (var id in ids.Take(4))
            {
                var verifyByTitle = !hasNativeId && exact.Count == 1 && exact.Contains(id);
                var candidate = await VerifySiteAsync(source, id, tmdbId, tmdbDetail, exactTitles: verifyByTitle ? exactTitles : null,
                    exactYear: verifyByTitle ? exactYear : null, independentSeason: hasNativeId).ConfigureAwait(false);
                if (candidate.Id is not { } verified) continue;
                if (result is not null) return null;
                result = verified;
            }
            return result;
        }

        async Task<long?> VerifyNativeSiteAsync(string source)
        {
            if (!anchors.TryGetValue(source, out var id)) return null;
            var detail = await DetailAsync(source, id).ConfigureAwait(false);
            if (detail is not { } data) return null;
            // 原生详情的 season 来自站点标题。后续季须有明确同季证据，不能按缺省值猜测。
            var seasonAgrees = season <= 1 ? NativeSeason(data) : MoviePilotSubscription.Number(data, "season") == season;
            return seasonAgrees ? ScrobblingNumber(id) : null;
        }

        async Task<(long? Id, bool MapsToOtherSeason)> VerifySiteAsync(string source, string id, string tmdbId, JsonElement tmdbDetail, bool allowDirect = false,
            string[]? exactTitles = null, int? exactYear = null, bool independentSeason = false)
        {
            var detail = await DetailAsync(source, id, independentSeason).ConfigureAwait(false);
            if (detail is not { } info || !ScrobblingFieldAgrees(info, "tmdb_id", ScrobblingTmdb, tmdbId)) return default;
            var sourceSeason = MoviePilotSubscription.Member(info, "season");
            var number = MoviePilotSubscription.Number(info, "season");
            if (sourceSeason.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && number is not (>= 0 and <= 10000)) return default;
            // MoviePilot v3.2.9 b186df432f2f1657f2e2e70e1fdedf78b4f3bde8
            // app/api/endpoints/media.py: media_mapping, app/schemas/mapping.py: MediaMapping.
            // season 属于原来源，tmdb_season 属于目标；关系没有站点条目内集号。
            var path = $"media/mapping?media_source={source}&media_id={id}&type=tv";
            if (number is { } value) path += FormattableString.Invariant($"&season={value}");
            var mapping = await ReadAsync(path).ConfigureAwait(false);
            if (mapping.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                var acceptsDirectEvidence = !independentSeason && (allowDirect && season == 1 && NativeSeason(info)
                    || exactTitles is not null && exactYear is not null && number is not 0
                        && (season > 1 || NativeSeason(info))
                        && ScrobblingYear(info) == exactYear && ScrobblingTitleAgrees(info, exactTitles));
                return (acceptsDirectEvidence ? ScrobblingNumber(id) : null, false);
            }
            if (MoviePilotSubscription.Text(mapping, "media_source") != source
                || ScrobblingId(source, MoviePilotSubscription.Text(mapping, "media_id")) != id
                || MoviePilotSubscription.Text(mapping, "type") != "tv" || MoviePilotSubscription.Text(mapping, "tmdb_type") != "tv"
                || ScrobblingId(ScrobblingTmdb, MoviePilotSubscription.Text(mapping, "tmdb_id")) != tmdbId)
                return default;
            var mappedSource = MoviePilotSubscription.Member(mapping, "season").ValueKind;
            if (source == "douban" && !independentSeason && season == 1 && NativeSeason(info)
                && mappedSource == JsonValueKind.Null && MoviePilotSubscription.Member(mapping, "tmdb_season").ValueKind == JsonValueKind.Null)
            {
                // MoviePilot can return a series-to-series mapping. Establish the only regular season
                // from the TMDB series, and independently confirm which Douban subject this is.
                var confirmedSubject = allowDirect || ReadScrobblingNumber(tmdbDetail, "douban_id") == ScrobblingNumber(id)
                    || exactYear.HasValue && ScrobblingYear(info) == exactYear && exactTitles is not null && ScrobblingTitleAgrees(info, exactTitles);
                return (confirmedSubject && HasSingleFirstSeason(tmdbDetail) ? ScrobblingNumber(id) : null, false);
            }
            var sameSourceSeason = number is null ? mappedSource is JsonValueKind.Null or JsonValueKind.Undefined
                : MoviePilotSubscription.Number(mapping, "season") == number;
            if (!sameSourceSeason || MoviePilotSubscription.Number(mapping, "tmdb_season") is not int mappedSeason
                || mappedSeason is <= 0 or > 10000)
                return default;
            return mappedSeason == season ? (ScrobblingNumber(id), false) : (null, true);
        }

        static bool NativeSeason(JsonElement data) =>
            MoviePilotSubscription.Member(data, "season").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            || MoviePilotSubscription.Number(data, "season") == 1;

        Task<JsonElement> SearchAsync(string source, string term) => ReadAsync(
            FormattableString.Invariant($"media/search?title={Uri.EscapeDataString(term)}&type=media&page=1&count={ScrobblingSearchLimit}&media_source={source}"));

        async Task<JsonElement?> DetailAsync(string source, string id, bool independentSeason = false)
        {
            var path = $"media/{id}?media_source={source}&type_name={Uri.EscapeDataString(ScrobblingSeriesType)}";
            var data = await ReadAsync(path).ConfigureAwait(false);
            if (data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                || data.ValueKind == JsonValueKind.Object && !data.EnumerateObject().Any()) return null;
            if (ScrobblingPrimaryId(data, source) != id || !AnchorsAgree(data, source, id, independentSeason)) return null;
            return data;
        }

        bool AnchorsAgree(JsonElement data, string source, string id, bool independentSeason = false) =>
            ScrobblingFieldAgrees(data, ScrobblingIdField(source), source, id)
            && anchors.All(pair => pair.Key == source ? independentSeason || pair.Value == id
                : ScrobblingFieldAgrees(data, ScrobblingIdField(pair.Key), pair.Key, pair.Value));

        async Task<JsonElement> ReadAsync(string path)
        {
            try
            {
                return await CallWithSessionAsync((address, token, client) => client.ReadScrobblingMetadataAsync(address, token, path, timeout.Token),
                    timeout.Token, identity).ConfigureAwait(false);
            }
            catch (MoviePilotException error) when (error.Status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
                or HttpStatusCode.NotImplemented)
            {
                // 旧版本可以没有已有关系接口；再次核对连接，404 也可能迟到。
                CheckIdentity(identity, timeout.Token);
                return default;
            }
        }
    }

    private SessionIdentity? CurrentScrobblingIdentity() =>
        Enabled && _availableIdentity is { } identity && MatchesConfiguration(identity) ? identity : null;

    private static string ScrobblingStamp(SessionIdentity identity) =>
        FormattableString.Invariant($"{identity.Generation}:{TransferConnectionStamp(identity)}");

    private static Dictionary<string, string>? ReadScrobblingAnchors(IReadOnlyDictionary<string, string>? providerIds)
    {
        var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (providerIds is null) return anchors;
        foreach (var (provider, value) in providerIds)
        {
            var source = provider.ToLowerInvariant() switch
            {
                "tmdb" or "themoviedb" => ScrobblingTmdb,
                "bangumi" or "bgm" => "bangumi",
                "douban" => "douban",
                "imdb" => "imdb",
                "tvdb" or "thetvdb" => "tvdb",
                _ => null
            };
            if (source is null || string.IsNullOrWhiteSpace(value)) continue;
            if (ScrobblingId(source, value.Trim()) is not { } id || (!anchors.TryAdd(source, id) && anchors[source] != id)) return null;
        }
        if (anchors.TryGetValue("bangumi", out var bgm) && anchors.GetValueOrDefault(ScrobblingTmdb) == bgm)
            anchors.Remove(ScrobblingTmdb); // 一些媒体库整理插件会把 Bangumi 主编号同时填入 TMDB 字段。
        return anchors;
    }

    private static string ScrobblingIdField(string source) => source == ScrobblingTmdb ? "tmdb_id" : source + "_id";

    private static string? ScrobblingPrimaryId(JsonElement data, string source) =>
        MoviePilotSubscription.Text(data, "media_source") == source
        && MoviePilotSubscription.Text(data, "type") == ScrobblingSeriesType
            ? ScrobblingId(source, MoviePilotSubscription.Text(data, "media_id")) : null;

    private static bool ScrobblingFieldAgrees(JsonElement data, string field, string source, string expected,
        bool allowSentinel = true)
    {
        var member = MoviePilotSubscription.Member(data, field);
        if (member.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        var value = MoviePilotSubscription.Scalar(member);
        if (allowSentinel && (value is "" or "0") && member.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            return true;
        return ScrobblingId(source, value) == expected;
    }

    private static string? ScrobblingId(string source, string? value)
    {
        if (source == "imdb") return value is { Length: >= 9 and <= 16 } && value.StartsWith("tt", StringComparison.Ordinal)
            && value[2..].All(char.IsAsciiDigit) ? value : null;
        return ScrobblingNumber(value)?.ToString(CultureInfo.InvariantCulture);
    }

    private static long? ReadScrobblingNumber(JsonElement data, string field) => ScrobblingNumber(MoviePilotSubscription.Text(data, field));

    private static int? ScrobblingYear(JsonElement data)
    {
        if (MoviePilotSubscription.Number(data, "year") is > 0 and var year) return year;
        foreach (var field in (string[])["first_air_date", "release_date"])
            if (DateOnly.TryParseExact(MoviePilotSubscription.Text(data, field), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)) return date.Year;
        return null;
    }

    private static bool HasSingleFirstSeason(JsonElement detail)
    {
        var seasons = MoviePilotSubscription.Member(detail, "season_info");
        if (seasons.ValueKind != JsonValueKind.Array || MoviePilotSubscription.Number(detail, "number_of_seasons") != 1) return false;
        var found = false;
        foreach (var entry in seasons.EnumerateArray())
        {
            var number = MoviePilotSubscription.Number(entry, "season_number");
            if (number == 0) continue;
            if (number != 1 || found) return false;
            found = true;
        }
        return found;
    }

    private static int? ReadScrobblingSeasonEnd(JsonElement data, JsonElement detail, int season, string tmdbId)
    {
        var count = data.GetArrayLength();
        if (count is not (>= 1 and <= 10000)) return null;
        var seasonInfo = MoviePilotSubscription.Member(detail, "season_info");
        if (seasonInfo.ValueKind == JsonValueKind.Array)
        {
            var matchingSeasons = seasonInfo.EnumerateArray()
                .Where(item => MoviePilotSubscription.Number(item, "season_number") == season).Take(2).ToArray();
            if (matchingSeasons.Length > 1 || matchingSeasons is [var match]
                && MoviePilotSubscription.Number(match, "episode_count") is > 0 and var declared && declared != count)
                return null;
        }
        var episodes = new HashSet<int>();
        foreach (var item in data.EnumerateArray())
            if (MoviePilotSubscription.Number(item, "season_number") != season
                || MoviePilotSubscription.Number(item, "episode_number") is not int episode || episode < 1 || episode > count
                || !episodes.Add(episode) || !ScrobblingFieldAgrees(item, "show_id", ScrobblingTmdb, tmdbId, allowSentinel: false))
                return null;
        return count;
    }

    private static long? ScrobblingNumber(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;

    private static IEnumerable<string> ScrobblingTitles(JsonElement data) =>
        new[] { "title", "original_title", "original_name" }
            .Select(field => ScrobblingTitle(MoviePilotSubscription.Text(data, field))).OfType<string>();

    private static bool ScrobblingTitleAgrees(JsonElement data, IReadOnlyList<string> titles) =>
        ScrobblingTitles(data).Any(title => titles.Contains(title, StringComparer.OrdinalIgnoreCase));

    private static string? ScrobblingTitle(string? value)
    {
        var title = value?.Trim();
        if (title is not { Length: > 0 and <= 200 } || title.Contains('/') || title.Contains('\\') || title.Any(char.IsControl)) return null;
        // FormKC 仅在比对时有意义；标题搜索也接受规范化文本，但不忽略标点以免误合并同名候选。
        try { return title.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { return null; } // 非法 Unicode 不能作为外部查询。
    }
}

public sealed partial class MoviePilotClient
{
    internal async Task<JsonElement> ReadScrobblingMetadataAsync(Uri apiBase, string token, string path, CancellationToken cancellationToken)
    {
        var url = MoviePilotAddress.Combine(apiBase, BasePath + path);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await SendAsync(request, cancellationToken, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new MoviePilotTokenExpiredException("MoviePilot 登录已过期");
        if (!response.IsSuccessStatusCode)
            throw new MoviePilotException("MoviePilot 打格子识别请求失败", response.StatusCode);
        try
        {
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new MoviePilotException("MoviePilot 打格子识别响应过大或读取中断");
        }
        return await ReadDataAsync(response, cancellationToken).ConfigureAwait(false);
    }
}

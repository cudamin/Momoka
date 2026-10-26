using System.Net;
using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>一份文件和提供它的媒体条目。媒体源 ID 只在自己的服务器和账号内有意义。</summary>
public sealed record MediaVersion(EmbyItem Item, MediaSource Source, EmbyItem? Parent = null)
{
    public string Label => LabelFor(showSource: true);

    public string LabelFor(bool showSource) => ItemDetail.SourceLabel(Source, showSource ? Item.Source?.ServerName : null);

    public bool IsPlaying(EmbyItem? item, MediaSource? source) => item is not null
        && string.Equals(Item.IdentityKey, item.IdentityKey, StringComparison.Ordinal)
        && MediaVersionSwitch.Same(Source, source);
}

public sealed record MediaVersionDiscoveryResult(
    IReadOnlyList<MediaVersion> Versions,
    IReadOnlyList<string> FailedSources);

/// <summary>
/// 在已登录来源里查找同一媒体。名称只用于缩小候选集，可信刮削 ID 与类型、年份、季集号负责确认身份；
/// 同一服务器的其他账号还可以用该服务器自己的条目 ID 确认；没有这些证据时不把同名片混进版本菜单。
/// </summary>
public static class MediaVersionDiscovery
{
    private static readonly string[] Providers = ["Tmdb", "Imdb", "Tvdb", "Douban", "AniDB"];

    public static bool SameServer(EmbyConnection left, EmbyConnection right) => left.ApiBase == right.ApiBase
        && left.ServerKind == right.ServerKind
        && (string.IsNullOrEmpty(left.ServerId) || string.IsNullOrEmpty(right.ServerId)
            || string.Equals(left.ServerId, right.ServerId, StringComparison.Ordinal));

    public static bool HasMultipleServers(IReadOnlyList<EmbySessionScope> sources) => sources.Count > 1
        && sources.Skip(1).Any(source => !SameServer(sources[0].Connection, source.Connection));

    public static bool MatchesServerItem(EmbyItem item, EmbyItem candidate) => item.Source is { } source
        && candidate.Source is { } candidateSource && SameServer(source.Connection, candidateSource.Connection)
        && item.Id.Length > 0 && string.Equals(item.Id, candidate.Id, StringComparison.Ordinal)
        && string.Equals(item.Type, candidate.Type, StringComparison.OrdinalIgnoreCase)
        && YearsAgree(item, candidate) && !ProviderConflict(item, candidate);

    public static bool Matches(EmbyItem item, EmbyItem candidate, EmbyItem? parent = null, EmbyItem? candidateParent = null)
    {
        if (!string.Equals(item.Type, candidate.Type, StringComparison.OrdinalIgnoreCase)) return false;
        if (!YearsAgree(item, candidate)) return false;
        if (item.Type == EmbyItemType.Episode)
        {
            if (item.ParentIndexNumber is null || item.IndexNumber is null
                || item.ParentIndexNumber != candidate.ParentIndexNumber || item.IndexNumber != candidate.IndexNumber
                || (item.IndexNumberEnd ?? item.IndexNumber) != (candidate.IndexNumberEnd ?? candidate.IndexNumber)) return false;
            if (ProviderMatch(item, candidate)) return true;
            return parent is { Type: EmbyItemType.Series } && candidateParent is { Type: EmbyItemType.Series }
                && Matches(parent, candidateParent) && !ProviderConflict(item, candidate);
        }
        return item.Type is EmbyItemType.Movie or EmbyItemType.Series && ProviderMatch(item, candidate);
    }

    private static bool YearsAgree(EmbyItem left, EmbyItem right) => left.ProductionYear is not > 0
        || right.ProductionYear is not > 0 || left.ProductionYear == right.ProductionYear;

    private static string? Provider(EmbyItem item, string provider) => item.ProviderIds
        .FirstOrDefault(pair => string.Equals(pair.Key, provider, StringComparison.OrdinalIgnoreCase)).Value?.Trim();

    private static bool ProviderConflict(EmbyItem left, EmbyItem right) => Providers.Any(provider =>
        Provider(left, provider) is { Length: > 0 } id && Provider(right, provider) is { Length: > 0 } other
        && !string.Equals(id, other, StringComparison.OrdinalIgnoreCase));

    private static bool ProviderMatch(EmbyItem left, EmbyItem right) => !ProviderConflict(left, right)
        && Providers.Any(provider => Provider(left, provider) is { Length: > 0 } id
            && string.Equals(id, Provider(right, provider), StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<MediaVersion> Local(EmbyItem? item, EmbyItem? parent = null) => item is null
        ? [] : item.MediaSources.Select(source => new MediaVersion(item, source, parent)).ToArray();

    public static async Task<MediaVersionDiscoveryResult> FindAsync(EmbySession session, EmbyItem item,
        MediaSourceOrder order, CancellationToken cancellationToken)
    {
        if (!item.IsPlayable) return new(Local(item), []);
        var own = session.Capture(item);
        own.Bind(item);
        var local = Local(item);
        if (!session.Aggregate) return new(local, []);
        // 同一账号的其他媒体库也有独立条目，不能只查「其他在线账号」。
        var scopes = session.Sources.Where(scope => scope.IsAvailable).ToArray();
        if (scopes.Length == 0) return new(local, []);

        EmbyItem? parent = null;
        var parentFailed = false;
        if (item.Type == EmbyItemType.Episode && (!string.IsNullOrEmpty(item.SeriesId)
            || own.Connection.IsFnNas && !string.IsNullOrEmpty(item.SeasonId ?? item.ParentId)))
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                var seriesId = item.SeriesId;
                if (string.IsNullOrEmpty(seriesId))
                {
                    // 飞牛的单集详情没有 tv_guid；parent_guid 指向季，再沿季的父编号取所属剧。
                    var season = await own.ExecuteAsync((client, token) => client.GetItemAsync(item.SeasonId ?? item.ParentId!, token),
                        deadline.Token).ConfigureAwait(false);
                    if (season.Type == EmbyItemType.Season)
                        seriesId = string.IsNullOrEmpty(season.SeriesId) ? season.ParentId : season.SeriesId;
                }
                if (!string.IsNullOrEmpty(seriesId))
                    parent = await own.ExecuteAsync((client, token) => client.GetItemAsync(seriesId, token), deadline.Token)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { parentFailed = true; }
        }
        var anchor = parent ?? item;
        var canMatchProviders = item.Type is EmbyItemType.Movie or EmbyItemType.Episode
            && Providers.Any(provider => Provider(anchor, provider) is { Length: > 0 });

        using var concurrency = new SemaphoreSlim(4);
        var found = await Task.WhenAll(scopes.Select(async scope =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            var versions = new List<MediaVersion>();
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                string? loadedItemId = scope.Key == own.Key ? item.Id : null;
                if (scope.Key != own.Key && SameServer(own.Connection, scope.Connection))
                {
                    try
                    {
                        var candidate = await scope.ExecuteAsync((client, token) => client.GetItemAsync(item.Id, token, order: order),
                            deadline.Token).ConfigureAwait(false);
                        if (MatchesServerItem(item, candidate))
                        {
                            versions.AddRange(Local(candidate));
                            loadedItemId = candidate.Id;
                        }
                    }
                    // 另一个账号可能只能访问另一媒体库；原条目不可见不等于它没有同片版本。
                    catch (EmbyApiException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) { }
                }
                if (canMatchProviders)
                    versions.AddRange(await FindInSourceAsync(scope, item, parent, order, loadedItemId, deadline.Token)
                        .ConfigureAwait(false));
                return (Versions: (IReadOnlyList<MediaVersion>)versions, Failed: (string?)null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return (Versions: (IReadOnlyList<MediaVersion>)versions, Failed: scope.ServerName); }
            finally { concurrency.Release(); }
        })).ConfigureAwait(false);
        var failedSources = found.Where(result => result.Failed is not null).Select(result => result.Failed!).ToList();
        if (parentFailed) failedSources.Insert(0, own.ServerName);
        // 本地版本必须在前且保留原对象，详情页才能增量追加，不重置用户当前选择。
        return new([.. local, .. found.SelectMany(result => result.Versions)],
            failedSources.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static async Task<IReadOnlyList<MediaVersion>> FindInSourceAsync(EmbySessionScope scope,
        EmbyItem item, EmbyItem? parent, MediaSourceOrder order, string? loadedItemId, CancellationToken cancellationToken)
    {
        var anchor = parent ?? item;
        var candidates = new Dictionary<string, EmbyItem>(StringComparer.Ordinal);
        // Jellyfin 的 provider 兼容查询要扫描全库；常见同名/原名先走快速搜索，仍以 provider 严格复核。
        if (scope.Connection.IsJellyfin) await SearchTitlesAsync().ConfigureAwait(false);
        // Provider 查询只有 Emby/Jellyfin 支持；飞牛/Plex 走它们自己的标题搜索，不能发 Emby 路由。
        if (candidates.Count == 0 && scope.Connection.ServerKind is MediaServerKind.Emby or MediaServerKind.Jellyfin
            && anchor.Type is EmbyItemType.Movie or EmbyItemType.Series)
        {
            foreach (var provider in Providers)
            {
                if (Provider(anchor, provider) is not { Length: > 0 } id) continue;
                ItemsResult result;
                try
                {
                    result = await scope.ExecuteAsync((client, token) => client.FindByProviderAsync(provider, id,
                        anchor.Type == EmbyItemType.Series, token), cancellationToken).ConfigureAwait(false);
                }
                catch (EmbyApiException error) when (error.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                    or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                {
                    // 部署未提供这条兼容接口时仍可搜索，不把不支持查询误报成整台来源失败。
                    break;
                }
                foreach (var candidate in result.Items) AddCandidate(candidate);
                // Jellyfin 的兼容实现会分页扫描媒体库，找到可信候选后不再为另一个 ID 重扫全库。
                if (scope.Connection.IsJellyfin && candidates.Count > 0) break;
            }
        }
        if (!scope.Connection.IsJellyfin || candidates.Count == 0) await SearchTitlesAsync().ConfigureAwait(false);

        var matches = new List<MediaVersion>();
        var loadedItems = new HashSet<string>(StringComparer.Ordinal);
        if (loadedItemId is not null) loadedItems.Add(loadedItemId);
        foreach (var candidate in candidates.Values)
        {
            if (parent is null)
            {
                await AddAsync(candidate, null).ConfigureAwait(false);
                continue;
            }
            var episodes = await scope.ExecuteAsync((client, token) => client.GetEpisodesAsync(candidate.Id, null, token,
                EmbyFields.Detail), cancellationToken).ConfigureAwait(false);
            foreach (var episode in episodes.Where(episode => Matches(item, episode, parent, candidate)))
                await AddAsync(episode, candidate).ConfigureAwait(false);
        }
        return matches;

        void AddCandidate(EmbyItem candidate)
        {
            if (parent is null && string.Equals(candidate.Id, loadedItemId, StringComparison.Ordinal)) return;
            if (Matches(anchor, candidate)) candidates.TryAdd(candidate.Id, candidate);
        }

        async Task SearchTitlesAsync()
        {
            foreach (var name in new[] { anchor.Name, anchor.OriginalTitle }.Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var result = await scope.ExecuteAsync((client, token) => client.GetItemsAsync(new ItemQuery
                {
                    SearchTerm = name,
                    IncludeItemTypes = [anchor.Type],
                    Recursive = true,
                    Fields = EmbyFields.Detail,
                    Limit = 100
                }, token), cancellationToken).ConfigureAwait(false);
                foreach (var candidate in result.Items) AddCandidate(candidate);
            }
        }

        async Task AddAsync(EmbyItem candidate, EmbyItem? candidateParent)
        {
            if (loadedItems.Contains(candidate.Id)) return;
            var detail = await scope.ExecuteAsync((client, token) => client.GetItemAsync(candidate.Id, token, order: order),
                cancellationToken).ConfigureAwait(false);
            // Emby 的合并版本候选可能用别名条目编号返回同一份详情；按实际详情编号再去重。
            if (Matches(item, detail, parent, candidateParent) && loadedItems.Add(detail.Id))
                matches.AddRange(Local(detail, candidateParent));
        }
    }
}

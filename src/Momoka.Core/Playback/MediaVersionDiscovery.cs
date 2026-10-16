using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>一份文件和提供它的媒体条目。媒体源 ID 只在自己的服务器和账号内有意义。</summary>
public sealed record MediaVersion(EmbyItem Item, MediaSource Source, EmbyItem? Parent = null)
{
    public string Label => LabelFor(showSource: true);

    public string LabelFor(bool showSource) => SourceCaption.Append(ItemDetail.SourceLabel(Source), Item.Source, showSource);

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
        var scopes = session.Sources.Where(scope => scope.IsAvailable && scope.Key != own.Key).ToArray();
        if (scopes.Length == 0) return new(local, []);

        EmbyItem? parent = null;
        var parentFailed = false;
        if (item.Type == EmbyItemType.Episode && item.SeriesId is { Length: > 0 } seriesId
            && scopes.Any(scope => !SameServer(own.Connection, scope.Connection)))
        {
            try
            {
                parent = await own.ExecuteAsync((client, token) => client.GetItemAsync(seriesId, token), cancellationToken)
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
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                if (SameServer(own.Connection, scope.Connection))
                {
                    var candidate = await scope.ExecuteAsync((client, token) => client.GetItemAsync(item.Id, token, order: order),
                        deadline.Token).ConfigureAwait(false);
                    if (MatchesServerItem(item, candidate)) return (Versions: Local(candidate), Failed: (string?)null);
                }
                if (!canMatchProviders) return (Versions: (IReadOnlyList<MediaVersion>)[], Failed: (string?)null);
                var versions = await FindInSourceAsync(scope, item, parent, order, deadline.Token).ConfigureAwait(false);
                return (Versions: versions, Failed: (string?)null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return (Versions: (IReadOnlyList<MediaVersion>)[], Failed: scope.ServerName); }
            finally { concurrency.Release(); }
        })).ConfigureAwait(false);
        var failedSources = found.Where(result => result.Failed is not null).Select(result => result.Failed!).ToList();
        if (parentFailed) failedSources.Insert(0, own.ServerName);
        return new([.. local, .. found.SelectMany(result => result.Versions)], failedSources);
    }

    private static async Task<IReadOnlyList<MediaVersion>> FindInSourceAsync(EmbySessionScope scope,
        EmbyItem item, EmbyItem? parent, MediaSourceOrder order, CancellationToken cancellationToken)
    {
        var anchor = parent ?? item;
        var candidates = new Dictionary<string, EmbyItem>(StringComparer.Ordinal);
        // Jellyfin 的 provider 兼容查询要扫描全库；常见同名/原名先走快速搜索，仍以 provider 严格复核。
        if (scope.Connection.IsJellyfin) await SearchTitlesAsync().ConfigureAwait(false);
        if (candidates.Count == 0 && scope.Connection.ServerKind != MediaServerKind.FnNas
            && anchor.Type is EmbyItemType.Movie or EmbyItemType.Series)
        {
            foreach (var provider in Providers)
            {
                if (Provider(anchor, provider) is not { Length: > 0 } id) continue;
                var result = await scope.ExecuteAsync((client, token) => client.FindByProviderAsync(provider, id,
                    anchor.Type == EmbyItemType.Series, token), cancellationToken).ConfigureAwait(false);
                foreach (var candidate in result.Items)
                    if (Matches(anchor, candidate)) candidates.TryAdd(candidate.Id, candidate);
                // Jellyfin 的兼容实现会分页扫描媒体库，找到可信候选后不再为另一个 ID 重扫全库。
                if (candidates.Count > 0) break;
            }
        }
        if (!scope.Connection.IsJellyfin) await SearchTitlesAsync().ConfigureAwait(false);

        var matches = new List<MediaVersion>();
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

        async Task SearchTitlesAsync()
        {
            foreach (var name in new[] { anchor.Name, anchor.OriginalTitle }.Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (candidates.Count > 0) break;
                var result = await scope.ExecuteAsync((client, token) => client.GetItemsAsync(new ItemQuery
                {
                    SearchTerm = name,
                    IncludeItemTypes = [anchor.Type],
                    Recursive = true,
                    Fields = EmbyFields.Detail,
                    Limit = 100
                }, token), cancellationToken).ConfigureAwait(false);
                foreach (var candidate in result.Items)
                    if (Matches(anchor, candidate)) candidates.TryAdd(candidate.Id, candidate);
            }
        }

        async Task AddAsync(EmbyItem candidate, EmbyItem? candidateParent)
        {
            var detail = await scope.ExecuteAsync((client, token) => client.GetItemAsync(candidate.Id, token, order: order),
                cancellationToken).ConfigureAwait(false);
            if (Matches(item, detail, parent, candidateParent)) matches.AddRange(Local(detail, candidateParent));
        }
    }
}

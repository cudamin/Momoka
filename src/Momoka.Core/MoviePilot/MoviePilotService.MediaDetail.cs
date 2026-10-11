using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed partial class MoviePilotService
{
    public async Task<MoviePilotMediaDetail> MediaDetailAsync(MoviePilotMedia media, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(media.ConnectionStamp);
        var path = $"media/{Uri.EscapeDataString(media.MediaId ?? "")}?media_source={Uri.EscapeDataString(media.MediaSource ?? "")}&type_name={Uri.EscapeDataString(media.Type ?? "")}";
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotMediaDetail.Parse(data, media);
    }

    public async Task<IReadOnlyList<MoviePilotMediaSeason>> MediaSeasonsAsync(MoviePilotMediaDetail detail, string group, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(detail.Media.ConnectionStamp);
        if (!detail.SupportsSeasons) return [];
        if (group.Length > 0 && !detail.SupportsEpisodeGroups) throw new MoviePilotException("此媒体来源不支持 TMDB 剧集组");
        if (group.Length == 0 && detail.Seasons.Count > 0) return detail.Seasons;
        var path = group.Length > 0 ? "media/group/seasons/" + Uri.EscapeDataString(group)
            : "media/seasons?media_source=" + Uri.EscapeDataString(detail.Media.MediaSource!) + "&media_id=" + Uri.EscapeDataString(detail.Media.MediaId!);
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotMediaSeason.Parse(data);
    }

    public async Task<IReadOnlyList<MoviePilotEpisodeGroup>> MediaGroupsAsync(MoviePilotMediaDetail detail, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(detail.Media.ConnectionStamp);
        if (!detail.SupportsEpisodeGroups) return [];
        var groups = await CallAsync((address, token) => _client.GetAsync(address, token,
            "media/groups/" + Uri.EscapeDataString(detail.Media.MediaId!), cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotEpisodeGroups.Build(groups, MoviePilotSubscription.Member(detail.Data, "season_info"));
    }

    public async Task<IReadOnlyList<MoviePilotMediaEpisode>> MediaEpisodesAsync(MoviePilotMediaDetail detail, string group, int season, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(detail.Media.ConnectionStamp);
        if (!detail.SupportsEpisodeGroups || season < 0) return [];
        var path = $"tmdb/{Uri.EscapeDataString(detail.Media.MediaId!)}/{season.ToString(CultureInfo.InvariantCulture)}";
        if (group.Length > 0) path += "?episode_group=" + Uri.EscapeDataString(group);
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotMediaEpisode.Parse(data);
    }

    /// <summary>与网页展开季相同的只读 POST；失败不能把所有季显示成缺失或已入库。</summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlySet<int>>> MediaExistingEpisodesAsync(MoviePilotMediaDetail detail, string group, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(detail.Media.ConnectionStamp);
        var body = detail.Data.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        body["episode_group"] = group;
        var data = await CallAsync((address, token) => _client.PostAsync(address, token, "mediaserver/exists_remote", body, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        if (data.ValueKind != JsonValueKind.Object) throw new MoviePilotException("媒体库没有返回有效的季集统计");
        var result = new Dictionary<int, IReadOnlySet<int>>();
        foreach (var property in data.EnumerateObject())
            if (int.TryParse(property.Name, CultureInfo.InvariantCulture, out var season) && season >= 0 && property.Value.ValueKind == JsonValueKind.Array)
                result[season] = property.Value.EnumerateArray().Select(value => int.TryParse(value.ToString(), out var episode) ? episode : -1)
                    .Where(episode => episode > 0).ToHashSet();
        return result;
    }

    public MoviePilotOperationState SeasonSubscribeState(MoviePilotMedia media) => SubscribeState(media);

    /// <summary>每一项都有独立回执与防重键。中途失败由调用者保留已成功项，不重放整个批次。</summary>
    public async Task ApplySubscriptionChangeAsync(MoviePilotSubscribeChange change, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(change.Media.ConnectionStamp);
        if (change.Kind == MoviePilotSubscribeChangeKind.Add)
        {
            var result = await WriteAsync(identity, [SubscribeKey(change.Media)], (address, token) =>
                _client.PostAsync(address, token, "subscribe/", MoviePilotSubscriptionPlan.Body(change), cancellationToken), cancellationToken).ConfigureAwait(false);
            try
            {
                var id = MoviePilotSubscription.Number(result, "id");
                if (id is not > 0) throw new MoviePilotException("订阅已提交，但服务器未返回订阅编号，请刷新核对");
                var reference = new MoviePilotSubscription { Id = id.Value, Media = change.Media, Data = result, ConnectionStamp = change.Media.ConnectionStamp };
                var saved = await SubscriptionAsync(reference, cancellationToken).ConfigureAwait(false);
                if (!MoviePilotSubscriptionPlan.SameMedia(saved.Media, change.Media) || saved.Media.Season != change.Media.Season
                    || saved.EpisodeGroup != change.EpisodeGroup || MoviePilotSubscriptionPlan.Mode(saved) != change.Mode)
                    throw new MoviePilotException("服务器返回的订阅与所选模式或剧集组不同，请刷新核对原有订阅");
            }
            catch
            {
                lock (_operationGate) _operations[SubscribeKey(change.Media)] = MoviePilotOperationState.Uncertain;
                throw;
            }
            // 已由读回的明确 ID 核实成功，此后新增仍先由季选择读取现有记录；不明回执永远不会走到这里。
            lock (_operationGate) _operations.Remove(SubscribeKey(change.Media));
            return;
        }
        var original = change.Original ?? throw new MoviePilotException("订阅已失效");
        var current = await SubscriptionAsync(original, cancellationToken).ConfigureAwait(false);
        if (!MoviePilotSubscriptionPlan.SameMedia(current.Media, change.Media) || current.Media.Season != change.Media.Season
            || MoviePilotSubscriptionPlan.Mode(current) != MoviePilotSubscriptionPlan.Mode(original) || current.EpisodeGroup != original.EpisodeGroup)
            throw new MoviePilotException("服务器上的订阅已改变，请关闭后重新打开季选择");
        if (change.Kind == MoviePilotSubscribeChangeKind.Delete)
            await ManageSubscriptionAsync(current, MoviePilotSubscriptionAction.Delete, cancellationToken).ConfigureAwait(false);
        else
            await MutateSubscriptionAsync(current, HttpMethod.Put, "subscribe/", MoviePilotSubscriptionPlan.Body(change), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>仅在用户明确核对后解除不明回执；刷新与切换剧集组不会解除。</summary>
    public void ConfirmSeasonSubscriptionReviewed(MoviePilotMedia media)
    {
        RequireResultConnection(media.ConnectionStamp);
        lock (_operationGate)
            if (_operations.GetValueOrDefault(SubscribeKey(media)) == MoviePilotOperationState.Uncertain) _operations.Remove(SubscribeKey(media));
    }
}

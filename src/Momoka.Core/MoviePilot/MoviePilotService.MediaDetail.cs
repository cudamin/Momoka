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
        var data = await CallAsync((address, token) => _client.ReadPostAsync(address, token, "mediaserver/exists_remote", body, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        if (data.ValueKind != JsonValueKind.Object) throw new MoviePilotException("媒体库没有返回有效的季集统计");
        var result = new Dictionary<int, IReadOnlySet<int>>();
        foreach (var property in data.EnumerateObject())
        {
            if (!int.TryParse(property.Name, CultureInfo.InvariantCulture, out var season) || season < 0 || property.Value.ValueKind != JsonValueKind.Array)
                throw new MoviePilotException("媒体库季集统计格式不正确，入库状态未知");
            result[season] = property.Value.EnumerateArray().Select(value => int.TryParse(value.ToString(), out var episode) ? episode : -1)
                .Where(episode => episode > 0).ToHashSet();
        }
        return result;
    }

    public MoviePilotOperationState SeasonSubscribeState(MoviePilotMedia media, string group = "") => SubscriptionSettingsState(media, group);

    /// <summary>仅在用户明确核对后解除当前身份的不明回执；读取和切换组不自动解除。</summary>
    public void ConfirmSeasonSubscriptionReviewed(MoviePilotMedia media, string group = "")
    {
        RequireResultConnection(media.ConnectionStamp);
        var key = SubscribeKey(media, group);
        var scope = SubscriptionReviewKey(media);
        lock (_operationGate)
        {
            if (_operations.GetValueOrDefault(key) == MoviePilotOperationState.Uncertain) _operations.Remove(key);
            if (_subscriptionReviews.TryGetValue(scope, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0) _subscriptionReviews.Remove(scope);
            }
        }
    }
}

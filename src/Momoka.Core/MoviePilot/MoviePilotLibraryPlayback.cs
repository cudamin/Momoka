using Momoka.Emby;

namespace Momoka.MoviePilot;

public sealed record MoviePilotPlaybackTarget(EmbyItem Item, EmbyItem? Parent, IReadOnlyList<EmbyItem> Episodes);

/// <summary>在当前 Emby/Jellyfin 身份下查找；核对主身份与类型，默认组按季集、非默认组按唯一路径定位，不直接信任外服 itemid。</summary>
public static class MoviePilotLibraryPlayback
{
    public static string? Provider(MoviePilotMedia media)
    {
        var provider = media.MediaSource?.ToLowerInvariant() switch
        { "themoviedb" => "Tmdb", "douban" => "Douban", "imdb" => "Imdb", "tvdb" => "Tvdb", _ => null };
        var id = media.MediaId ?? "";
        return provider is null || id.Length == 0 || (provider == "Imdb"
            ? !id.StartsWith("tt", StringComparison.Ordinal) || id.Length < 3 || !id[2..].All(char.IsAsciiDigit)
            : !id.All(char.IsAsciiDigit)) ? null : provider;
    }

    public static bool MatchesMedia(MoviePilotSubscription subscription, EmbyItem item) =>
        item.Type == (subscription.IsSeries ? EmbyItemType.Series : EmbyItemType.Movie)
        && Provider(subscription.Media) is { } provider
        && item.ProviderIds.Any(pair => pair.Key.Equals(provider, StringComparison.OrdinalIgnoreCase)
            && pair.Value == subscription.Media.MediaId);

    public static bool MatchesEpisode(MoviePilotSubscription subscription, MoviePilotSubscriptionEpisode episode, EmbyItem item) =>
        episode.InLibrary && (!string.IsNullOrWhiteSpace(item.Path) || item.MediaSources.Count > 0) && (subscription.IsSeries
            ? item.Type == EmbyItemType.Episode && subscription.Media.Season is { } season && item.ParentIndexNumber == season
                && item.IndexNumber is { } start && start <= episode.Number && (item.IndexNumberEnd ?? start) >= episode.Number
            : episode.Number == 0 && item.Type == EmbyItemType.Movie);

    public static bool MatchesPath(MoviePilotSubscriptionEpisode episode, EmbyItem item) =>
        episode.LibraryEntries.Any(file => SamePath(file.Path, item.Path)
            || item.MediaSources.Any(source => SamePath(file.Path, source.Path)));

    private static bool SamePath(string left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        // Unix 路径区分大小写；Windows 驱动器/UNC 路径不区分。这里只比较，不访问或启动路径。
        var windows = left.Contains('\\') || left.Length > 1 && left[1] == ':';
        return string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public static async Task<IReadOnlyDictionary<int, MoviePilotPlaybackTarget>> FindAsync(EmbyClient client,
        MoviePilotSubscription subscription, MoviePilotSubscriptionFiles files, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<int, MoviePilotPlaybackTarget>();
        // 主提供者定位已实现 Emby 与 Jellyfin；旧文案不是移除 Jellyfin 能力的依据。
        if (client.Connection.ServerKind is not (MediaServerKind.Emby or MediaServerKind.Jellyfin) || files.LibraryCount == 0) return result;
        var grouped = subscription.IsSeries && !string.IsNullOrWhiteSpace(subscription.EpisodeGroup);
        var candidates = new List<MoviePilotPlaybackTarget>();
        if (Provider(subscription.Media) is { } provider)
        {
            var matches = await client.FindByProviderAsync(provider, subscription.Media.MediaId!, subscription.IsSeries, cancellationToken).ConfigureAwait(false);
            foreach (var media in matches.Items.Where(item => MatchesMedia(subscription, item)))
            {
                if (!subscription.IsSeries) { candidates.Add(new(media, null, [])); continue; }
                var episodes = await client.GetEpisodesAsync(media.Id, null, cancellationToken, EmbyFields.Detail).ConfigureAwait(false);
                // 组内季号不是 Emby 原始季号；非默认组保留全剧候选及各自的原始季上下文。
                foreach (var group in episodes.Where(item => item.Type == EmbyItemType.Episode
                    && (grouped || item.ParentIndexNumber == subscription.Media.Season)).GroupBy(item => item.ParentIndexNumber))
                {
                    var season = group.ToArray();
                    candidates.AddRange(season.Select(item => new MoviePilotPlaybackTarget(item, media, season)));
                }
            }
        }

        foreach (var episode in files.Episodes.Where(episode => episode.InLibrary))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (grouped)
            {
                // 非默认组只信已核主身份下的唯一路径，不能猜组内数字或借外服 itemid 绕过主媒体核对。
                var groupedPaths = candidates.Where(candidate => MatchesPath(episode, candidate.Item)).ToArray();
                if (groupedPaths.Length == 1) result[episode.Number] = groupedPaths[0];
                continue;
            }
            var matches = candidates.Where(candidate => MatchesEpisode(subscription, episode, candidate.Item)).ToArray();
            var paths = matches.Where(candidate => MatchesPath(episode, candidate.Item)).ToArray();
            if (paths.Length == 1) result[episode.Number] = paths[0];
            else if (matches.Length == 1) result[episode.Number] = matches[0];
            if (result.ContainsKey(episode.Number)) continue;

            // 媒体源在 Emby 中没有对应提供者时，尝试文件统计提供的条目；只有文件路径也匹配才接受。
            foreach (var file in episode.LibraryEntries.Where(file => file.ServerType.Equals("emby", StringComparison.OrdinalIgnoreCase)
                && file.ItemId.Length > 0 && file.ItemId.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')))
            {
                try
                {
                    var item = await client.GetItemAsync(file.ItemId, cancellationToken).ConfigureAwait(false);
                    if (MatchesEpisode(subscription, episode, item) && MatchesPath(episode, item))
                    {
                        result[episode.Number] = new(item, null, []);
                        break;
                    }
                }
                catch (EmbyApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { }
            }
        }
        return result;
    }
}

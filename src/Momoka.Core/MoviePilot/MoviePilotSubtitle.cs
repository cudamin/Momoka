using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed record MoviePilotMediaSearchOptions(string Area = "title", int? Episode = null, bool Subtitles = false);
public sealed record MoviePilotSubtitle(MoviePilotMedia Media, JsonElement Data)
{
    public string Title => MoviePilotSubscription.Text(Data, "title");
    public string Description => MoviePilotSubscription.Text(Data, "description");
    public string Meta => string.Join(" · ", new[] { MoviePilotSubscription.Text(Data, "site_name"), MoviePilotSubscription.Text(Data, "language"), MoviePilotSubscription.Text(Data, "season_episode"), MoviePilotSubscription.Text(Data, "pubdate") }.Where(value => value.Length > 0));
}

public sealed partial class MoviePilotService
{
    private static string SubtitleKey(MoviePilotSubtitle subtitle) => OperationKey(subtitle.Media.ConnectionStamp, "subtitle",
        MoviePilotSubscription.Text(subtitle.Data, "site"), MoviePilotSubscription.Text(subtitle.Data, "enclosure"));
    public MoviePilotOperationState SubtitleState(MoviePilotSubtitle subtitle) => OperationState(SubtitleKey(subtitle));

    public async Task<IReadOnlyList<MoviePilotSubtitle>> SearchSubtitlesAsync(MoviePilotMedia media, MoviePilotSiteSelection? sites,
        int? episode, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(media.ConnectionStamp);
        var path = $"search/subtitle/media/{Uri.EscapeDataString(media.MediaId!)}?media_source={Uri.EscapeDataString(media.MediaSource!)}&mtype={Uri.EscapeDataString(media.Type!)}";
        if (media.Season is >= 0) path += "&season=" + media.Season.Value.ToString(CultureInfo.InvariantCulture);
        if (episode is > 0) path += "&episode=" + episode.Value.ToString(CultureInfo.InvariantCulture);
        path += SearchSitesQuery(sites);
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        if (data.ValueKind != JsonValueKind.Array) throw new MoviePilotException("字幕搜索结果格式不正确");
        return data.EnumerateArray().Where(item => MoviePilotSubscription.Text(item, "title").Length > 0
            && MoviePilotSubscription.Text(item, "enclosure").Length > 0).Select(item => new MoviePilotSubtitle(media, item.Clone())).ToArray();
    }

    public async Task<IReadOnlyList<string>> SubtitlePathsAsync(MoviePilotSubtitle subtitle, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(subtitle.Media.ConnectionStamp);
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, "download/paths", cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        return Values(data, "save_path");
    }

    public async Task<int> DownloadSubtitleAsync(MoviePilotSubtitle subtitle, string? path, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(subtitle.Media.ConnectionStamp);
        var body = new { subtitle_in = subtitle.Data, media_source = subtitle.Media.MediaSource, media_id = subtitle.Media.MediaId, save_path = string.IsNullOrWhiteSpace(path) ? null : path.Trim() };
        var result = await WriteAsync(identity, [SubtitleKey(subtitle)], (address, token) =>
            _client.PostAsync(address, token, "download/subtitle", body, cancellationToken), cancellationToken).ConfigureAwait(false);
        return MoviePilotSubscription.Member(result, "files") is { ValueKind: JsonValueKind.Array } files ? files.GetArrayLength() : 0;
    }
}

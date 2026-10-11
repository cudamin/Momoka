using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed record MoviePilotPerson(string Id, string Name, string Character, string ImageUrl, string Url);

public sealed partial class MoviePilotService
{
    public async Task<IReadOnlyList<MoviePilotMedia>> RelatedMediaAsync(MoviePilotMedia media, bool similar, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(media.ConnectionStamp);
        var source = media.MediaSource switch { "themoviedb" => "tmdb", "douban" when !similar => "douban", _ => "" };
        if (source.Length == 0) return [];
        var path = $"{source}/{(similar ? "similar" : "recommend")}/{Uri.EscapeDataString(media.MediaId!)}/{Uri.EscapeDataString(media.Type!)}?page=1&count=20";
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        return MoviePilotMediaParser.Parse(data).Select(item => item with { ConnectionStamp = media.ConnectionStamp, PosterUrl = MoviePilotMediaDetail.Image(item.PosterUrl ?? "") }).ToArray();
    }

    public async Task<IReadOnlyList<MoviePilotPerson>> MediaPeopleAsync(MoviePilotMedia media, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(media.ConnectionStamp);
        if (media.MediaSource != "themoviedb") return [];
        var path = $"tmdb/credits/{Uri.EscapeDataString(media.MediaId!)}/{Uri.EscapeDataString(media.Type!)}?page=1";
        var data = await CallAsync((address, token) => _client.GetAsync(address, token, path, cancellationToken), cancellationToken, identity).ConfigureAwait(false);
        if (data.ValueKind != JsonValueKind.Array) throw new MoviePilotException("演职员信息格式不正确");
        return data.EnumerateArray().Where(item => MoviePilotSubscription.Text(item, "id").Length > 0)
            .Select(item => new MoviePilotPerson(MoviePilotSubscription.Text(item, "id"), MoviePilotSubscription.Text(item, "name"),
                MoviePilotSubscription.Text(item, "character"), MoviePilotMediaDetail.Image(MoviePilotSubscription.Text(item, "profile_path")),
                "https://www.themoviedb.org/person/" + Uri.EscapeDataString(MoviePilotSubscription.Text(item, "id")))).ToArray();
    }
}

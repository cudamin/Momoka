using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed record MoviePilotCalendarEpisode(int Number, string Title, bool? IsInLibrary = null);

/// <summary>同一订阅同一播出日的摘要；不是媒体播放身份，也不把下载数量当作已入库证明。</summary>
public sealed record MoviePilotCalendarEntry
{
    public required DateOnly Date { get; init; }
    public required int SubscriptionId { get; init; }
    public required string Title { get; init; }
    public required string Type { get; init; }
    public int? Year { get; init; }
    public int? Season { get; init; }
    public string Poster { get; init; } = "";
    public int? TotalEpisodes { get; init; }
    public int? CompletedEpisodes { get; init; }
    public int? MissingEpisodes { get; init; }
    /// <summary>经当前订阅的文件统计确认的已入库集数；null 表示统计读取失败或尚未读取。</summary>
    public int? LibraryEpisodeCount { get; init; }
    /// <summary>同一文件统计快照声明的总集数；不以缺集下载数反推。</summary>
    public int? LibraryTotalEpisodes { get; init; }
    public string LastUpdate { get; init; } = "";
    public int? RuntimeMinutes { get; init; }
    public IReadOnlyList<MoviePilotCalendarEpisode> Episodes { get; init; } = [];
    public string ConnectionStamp { get; init; } = "";
}

internal sealed record MoviePilotCalendarPage(int Total, int Count, IReadOnlyList<MoviePilotCalendarEntry> Entries);
internal sealed record MoviePilotCalendarLibrarySnapshot(IReadOnlySet<int> Episodes, int? TotalEpisodes, string LastUpdate);

internal static class MoviePilotCalendar
{
    // MoviePilot bf2236f024d4e3046be3b4376a986fb216142f02:
    // app/schemas/calendar.py and app/chain/subscribe/calendar.py (also present in v3.2.3).
    internal static MoviePilotCalendarPage ParsePage(JsonElement data, DateOnly start, DateOnly end, string stamp)
    {
        var items = MoviePilotSubscription.Member(data, "items");
        var total = MoviePilotSubscription.Number(data, "total");
        if (items.ValueKind != JsonValueKind.Array || total is not >= 0)
            throw new MoviePilotException("MoviePilot 未返回有效的日历列表，请刷新重试");
        var result = new List<MoviePilotCalendarEntry>();
        foreach (var item in items.EnumerateArray())
        {
            if (!DateOnly.TryParseExact(MoviePilotSubscription.Text(item, "date"), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date < start || date >= end) continue;
            var subscription = MoviePilotSubscription.Member(item, "subscribe");
            var id = MoviePilotSubscription.Number(subscription, "id");
            var type = MoviePilotSubscription.Text(subscription, "type");
            if (id is not > 0 || type is not ("电影" or "电视剧")) continue;
            var episodes = MoviePilotSubscription.Member(item, "episodes");
            if (episodes.ValueKind != JsonValueKind.Array)
                throw new MoviePilotException("MoviePilot 日历缺少分集摘要，请刷新重试");
            result.Add(new MoviePilotCalendarEntry
            {
                Date = date,
                SubscriptionId = id.Value,
                Title = MoviePilotSubscription.Text(subscription, "name") is { Length: > 0 } title ? title : "未命名订阅",
                Type = type,
                Year = MoviePilotSubscription.Number(subscription, "year"),
                Season = MoviePilotSubscription.Number(subscription, "season"),
                Poster = MoviePilotSubscription.Text(subscription, "poster"),
                TotalEpisodes = Nonnegative(subscription, "total_episode"),
                CompletedEpisodes = Nonnegative(subscription, "completed_episode"),
                MissingEpisodes = Nonnegative(subscription, "lack_episode"),
                LastUpdate = MoviePilotSubscription.Text(subscription, "last_update"),
                RuntimeMinutes = MoviePilotSubscription.Number(item, "runtime") is > 0 and var runtime ? runtime : null,
                Episodes = episodes.EnumerateArray().Where(episode => MoviePilotSubscription.Number(episode, "episode_number") is > 0)
                    .Select(episode => new MoviePilotCalendarEpisode(MoviePilotSubscription.Number(episode, "episode_number")!.Value,
                        MoviePilotSubscription.Text(episode, "name"))).DistinctBy(episode => episode.Number).OrderBy(episode => episode.Number).ToArray(),
                ConnectionStamp = stamp
            });
        }
        return new(total.Value, items.GetArrayLength(), result);
    }

    internal static MoviePilotCalendarLibrarySnapshot LibraryEpisodes(JsonElement data, MoviePilotCalendarEntry entry)
    {
        var subscription = MoviePilotSubscription.Member(data, "subscribe");
        var episodes = MoviePilotSubscription.Member(data, "episodes");
        if (MoviePilotSubscription.Number(subscription, "id") != entry.SubscriptionId
            || MoviePilotSubscription.Text(subscription, "type") != entry.Type
            || MoviePilotSubscription.Number(subscription, "season") != entry.Season
            || MoviePilotSubscription.Text(subscription, "name") != entry.Title
            || episodes.ValueKind != JsonValueKind.Object)
            throw new MoviePilotException("MoviePilot 日历的文件统计身份不匹配");
        var numbers = episodes.EnumerateObject()
            .Where(property => int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
                && MoviePilotSubscription.Member(property.Value, "library") is { ValueKind: JsonValueKind.Array } files && files.GetArrayLength() > 0)
            .Select(property => int.Parse(property.Name, CultureInfo.InvariantCulture)).ToHashSet();
        return new(numbers, Nonnegative(subscription, "total_episode") is > 0 and var total ? total : null,
            MoviePilotSubscription.Text(subscription, "last_update"));
    }

    internal static bool IsLegacyEndpoint(MoviePilotException error)
    {
        if (error.Status == System.Net.HttpStatusCode.NotFound) return true;
        if (error.Status != System.Net.HttpStatusCode.UnprocessableEntity || error.Body is null) return false;
        try
        {
            using var document = JsonDocument.Parse(error.Body);
            var details = MoviePilotSubscription.Member(document.RootElement, "detail");
            if (details.ValueKind != JsonValueKind.Array) return false;
            return details.EnumerateArray().Any(detail => MoviePilotSubscription.Text(detail, "type") == "int_parsing"
                && MoviePilotSubscription.Text(detail, "input") == "calendar"
                && MoviePilotSubscription.Member(detail, "loc") is { ValueKind: JsonValueKind.Array } location
                && location.GetArrayLength() == 2 && location[0].GetString() == "path" && location[1].GetString() == "subscribe_id");
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static int? Nonnegative(JsonElement data, string name) =>
        MoviePilotSubscription.Number(data, name) is >= 0 and var number ? number : null;
}

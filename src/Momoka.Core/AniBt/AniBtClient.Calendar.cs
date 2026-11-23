using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Momoka.AniBt;

public sealed record AniBtCalendarEntry(
    DateOnly Date, long? BgmId, string Title, string Format, string? ImageUrl, bool IsPremiere,
    DateTimeOffset? AiringAt, string AnimeId, bool IsMovie);

public sealed record AniBtCalendarResult(IReadOnlyList<AniBtCalendarEntry> Entries, bool Truncated, bool PartialFailure = false);

public sealed partial class AniBtClient
{
    private const string CalendarCoverTypes = "image/jpeg,image/png,image/webp";
    private static readonly string[] CalendarSeasons = ["WINTER", "SPRING", "SUMMER", "FALL"];

    public Task<AniBtCalendarResult> CalendarAsync(DateOnly start, DateOnly endExclusive, CancellationToken cancellationToken) =>
        CalendarAsync(start, endExclusive, TimeZoneInfo.Local, cancellationToken);

    internal async Task<AniBtCalendarResult> CalendarAsync(
        DateOnly start, DateOnly endExclusive, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        if (endExclusive.DayNumber - start.DayNumber is not (> 0 and <= 93))
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "日历日期窗口必须大于 0 且不超过 93 天");

        var entries = new Dictionary<(string AnimeId, DateOnly Date), AniBtCalendarEntry>();
        var truncated = false;
        var quartersRead = 0;
        AniBtException? failure = null;
        var firstQuarter = (start.Year * 4) + ((start.Month - 1) / 3);
        var finalDay = endExclusive.AddDays(-1);
        var finalQuarter = (finalDay.Year * 4) + ((finalDay.Month - 1) / 3);
        for (var quarter = firstQuarter; quarter <= finalQuarter; quarter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var season = string.Create(CultureInfo.InvariantCulture, $"{quarter / 4:D4}-{CalendarSeasons[quarter % 4]}");
            try
            {
                using var document = ParseJson(await RequestAsync("api/seasons/anime?season=" + season, null, false, cancellationToken)
                    .ConfigureAwait(false));
                var data = Field(document.RootElement, "data");
                var groups = Field(data, "byWeekday");
                var incomplete = Field(data, "truncated");
                if (Field(document.RootElement, "ok").ValueKind != JsonValueKind.True
                    || Text(Field(data, "scope"), "kind") != "season" || groups.ValueKind != JsonValueKind.Array
                    || incomplete.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidResponse();

                foreach (var group in groups.EnumerateArray())
                    if (Field(group, "animes").ValueKind != JsonValueKind.Array) throw InvalidResponse();
                foreach (var group in groups.EnumerateArray())
                {
                    foreach (var anime in Field(group, "animes").EnumerateArray())
                        AddCalendarEntries(anime, start, endExclusive, timeZone, entries);
                }
                truncated |= incomplete.GetBoolean();
                quartersRead++;
            }
            catch (AniBtException error)
            {
                failure ??= error;
                if (error.StatusCode == HttpStatusCode.TooManyRequests) break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (quartersRead == 0 && failure is not null) throw failure;
        return new(entries.Values.OrderBy(entry => entry.Date).ThenBy(entry => entry.AiringAt)
            .ThenBy(entry => entry.Title, StringComparer.CurrentCulture).ThenBy(entry => entry.AnimeId, StringComparer.Ordinal).ToArray(),
            truncated, failure is not null);
    }

    private static void AddCalendarEntries(JsonElement anime, DateOnly start, DateOnly endExclusive, TimeZoneInfo timeZone,
        Dictionary<(string AnimeId, DateOnly Date), AniBtCalendarEntry> entries)
    {
        var animeId = Text(anime, "animeId");
        var title = Text(Field(anime, "title"), "primary");
        if (animeId.Length == 0 || title.Length == 0) return;

        var bgmId = Number(anime, "bgmId");
        var format = Text(anime, "format");
        var isMovie = Text(anime, "kind") == "MOVIE";
        var imageUrl = CalendarCoverUri(Text(anime, "cover"))?.AbsoluteUri;

        void Add(DateOnly date, bool isPremiere, DateTimeOffset? airingAt)
        {
            if (date < start || date >= endExclusive) return;
            var key = (animeId, date);
            if (entries.TryGetValue(key, out var existing))
            {
                entries[key] = existing with
                {
                    IsPremiere = existing.IsPremiere || isPremiere,
                    AiringAt = existing.AiringAt ?? airingAt,
                    ImageUrl = existing.ImageUrl ?? imageUrl
                };
                return;
            }

            entries[key] = new(date, bgmId is > 0 and <= 10000000 ? bgmId : null, title, format, imageUrl, isPremiere,
                airingAt, animeId, isMovie);
        }

        // 只采用上游提供的日精度首播和单次实际排播。星期和季别都不能换算成具体播出日。
        if (Text(anime, "premierePrecision") == "day" && DateOnly.TryParseExact(Text(anime, "premiereDate"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var premiere)) Add(premiere, true, null);
        if (Text(anime, "scheduleStatus") == "scheduled" && Number(anime, "airingAt") is > 0 and var seconds
            && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(seconds), timeZone);
            Add(DateOnly.FromDateTime(local.DateTime), false, local);
        }
    }

    public async Task<byte[]> CalendarCoverAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CalendarCoverUri(url) is not { } uri) return [];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var connection = AcquireConnection();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(CalendarCoverTypes);
            using var response = await connection.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not
                    ("image/jpeg" or "image/png" or "image/webp")) return [];
            return await ReadBoundedContentAsync(response.Content, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
        catch (HttpRequestException) { return []; }
        catch (AniBtException) { return []; }
        finally { ReleaseConnection(connection); }
    }

    private static Uri? CalendarCoverUri(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.Host == "r2.anibt.net" && uri.UserInfo.Length == 0
        && uri.Query.Length == 0 && uri.Fragment.Length == 0 ? uri : null;
}

using System.Globalization;
using System.Text.Json;

namespace Momoka.AniBt;

public sealed partial class AniBtClient
{
    /// <summary>使用 AniBT 网页相同的目录和游标；不把 Bangumi 广域搜索结果冒充本站收录。</summary>
    public async Task<AniBtSearchPage> SearchCatalogAsync(string keyword, string? cursor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = keyword?.Trim() ?? "";
        ValidateText(query, 100, "搜索词");
        if (cursor is not null) ValidateText(cursor, 4096, "搜索游标");
        var path = "api/animes/search?q=" + Uri.EscapeDataString(query) + "&limit=24"
            + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        using var document = ParseJson(await RequestAsync(path, null, false, cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        if (Field(root, "page") is not { ValueKind: JsonValueKind.Array } page
            || Field(root, "isDone").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw InvalidResponse();
        var next = Text(root, "continueCursor");
        var hasMore = Field(root, "isDone").ValueKind == JsonValueKind.False;
        if (hasMore && (next.Length is 0 or > 4096 || next.Any(char.IsControl) || next == cursor))
            throw InvalidResponse();
        var items = new List<AniBtAnime>();
        foreach (var item in page.EnumerateArray())
        {
            var anime = ParseCatalogAnime(item);
            if (anime is not null && !items.Any(existing => existing.BgmId == anime.BgmId)) items.Add(anime);
        }
        return new AniBtSearchPage(items, hasMore, hasMore ? next : null);
    }

    /// <summary>
    /// 先读取网页详情给出的规范身份，再读字幕组。分组接口不在每条发布上重复 bgmId，
    /// 因而必须校验整个响应的 animeId，不能把最初搜索的旧 Bangumi 编号交给下载元数据。
    /// </summary>
    public async Task<AniBtGroupPage> GroupPageAsync(AniBtAnime anime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (anime.BgmId is <= 0 or > 10000000) throw new AniBtException("缺少有效的 Bangumi 编号");
        using var detailDocument = ParseJson(await RequestAsync("api/public/v1/anime?bgmId="
            + anime.BgmId.ToString(CultureInfo.InvariantCulture), null, false, cancellationToken).ConfigureAwait(false));
        var detailRoot = detailDocument.RootElement;
        var detail = ParseCatalogAnime(Field(detailRoot, "initialAnime")) ?? throw InvalidResponse();
        detail = detail with { ExternalLinks = ParseExternalLinks(Field(detailRoot, "initialExternalIds")) };
        using var groupsDocument = ParseJson(await RequestAsync("api/anime/groups?bgmId="
            + detail.BgmId.ToString(CultureInfo.InvariantCulture), null, false, cancellationToken).ConfigureAwait(false));
        var root = groupsDocument.RootElement;
        var data = Field(root, "data");
        if (Field(root, "ok").ValueKind != JsonValueKind.True || Number(data, "bgmId") != detail.BgmId
            || Text(data, "animeId") != detail.AnimeId || Field(data, "groups") is not { ValueKind: JsonValueKind.Array } groups)
            throw InvalidResponse();

        var results = new List<AniBtReleaseGroup>();
        var releaseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Text(group, "groupId");
            var name = Text(group, "name");
            if (id.Length == 0 || name.Length == 0 || results.Any(existing => existing.GroupId == id)) continue;
            if (Field(group, "items") is not { ValueKind: JsonValueKind.Array } releases
                || Field(group, "publishedEpisodes") is not { ValueKind: JsonValueKind.Array } episodes)
                throw InvalidResponse();
            var items = new List<AniBtRelease>();
            foreach (var item in releases.EnumerateArray())
            {
                var releaseId = Text(item, "releaseId");
                var title = Text(item, "title");
                if (!ValidReleaseId(releaseId) || title.Length == 0 || !releaseIds.Add(releaseId)) continue;
                long? size = Number(item, "size") is >= 0 and var bytes ? bytes : null;
                var published = Number(item, "publishedAt");
                // FromUnixTimeMilliseconds throws outside the DateTimeOffset range; invalid optional data stays absent.
                DateTimeOffset? time = published is >= -62135596800000 and <= 253402300799999
                    ? DateTimeOffset.FromUnixTimeMilliseconds(published.Value) : null;
                items.Add(new AniBtRelease(releaseId, title, new Uri(Origin, "api/torrent/" + releaseId + ".torrent"),
                    new Uri(Origin, "release/" + releaseId), size, time, name, Text(item, "episodeKey"), Text(item, "resolution"),
                    Text(item, "format"), Strings(Field(item, "language")), Text(item, "subtitle"), Strings(Field(item, "customTags")),
                    Text(item, "torrentStorageId").Length > 0));
            }
            var publishedEpisodes = episodes.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0)
                .Select(value => value.GetInt32()).Distinct().Order().ToArray();
            var slug = Text(group, "slug");
            var rss = slug.Length is > 0 and <= 128 && !slug.Any(char.IsControl)
                ? new Uri(Origin, "rss/anime.xml?bgmId=" + detail.BgmId.ToString(CultureInfo.InvariantCulture)
                    + "&groupSlug=" + Uri.EscapeDataString(slug)) : null;
            results.Add(new AniBtReleaseGroup(id, name, slug, publishedEpisodes,
                NonnegativeInt(group, "totalReleases") ?? items.Count, items, rss));
        }
        return new AniBtGroupPage(detail, results);
    }

    private static AniBtAnime? ParseCatalogAnime(JsonElement anime)
    {
        var bgmId = Number(anime, "bgmId");
        var id = Text(anime, "_id");
        var titles = Field(anime, "title");
        var title = FirstText(Text(titles, "chinese"), Text(titles, "chineseTraditional"), Text(titles, "native"),
            Text(titles, "romaji"), Text(titles, "english"), Text(anime, "titleRomaji"));
        if (id.Length == 0 || bgmId is not (> 0 and <= 10000000) || title.Length == 0) return null;
        double? score = Field(anime, "averageScore") is { ValueKind: JsonValueKind.Number } scoreValue
            && scoreValue.TryGetDouble(out var number) && double.IsFinite(number) && number is > 0 and <= 100 ? number / 10 : null;
        return new AniBtAnime(id, bgmId.Value, title,
            FirstText(Text(titles, "native"), Text(titles, "romaji")),
            ImageUrl(FirstText(Text(Field(anime, "coverImage"), "large"), Text(Field(anime, "coverImage"), "extraLarge"))),
            NonnegativeInt(anime, "seasonYear"), NonnegativeInt(anime, "episodes"), Text(anime, "format"), Text(anime, "status"),
            score, Text(anime, "description"), Text(titles, "english"), Text(anime, "season"), ImageUrl(Text(anime, "officialSite")))
        { Genres = Strings(Field(anime, "genres")) };
    }

    private static IReadOnlyList<string> Strings(JsonElement values) => values.ValueKind != JsonValueKind.Array ? []
        : values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()?.Trim() ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<AniBtExternalLink> ParseExternalLinks(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array) return [];
        var links = new List<AniBtExternalLink>();
        foreach (var value in values.EnumerateArray())
        {
            var id = Text(value, "externalId");
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0) continue;
            var (label, prefix) = Text(value, "source") switch
            {
                "anidb" => ("AniDB", "https://anidb.net/anime/"),
                "anilist" => ("AniList", "https://anilist.co/anime/"),
                "bgm" => ("Bangumi", "https://bgm.tv/subject/"),
                "mal" => ("MyAnimeList", "https://myanimelist.net/anime/"),
                _ => ("", "")
            };
            if (label.Length == 0 || links.Any(link => link.Label == label)) continue;
            // Only the provider's numeric identity is trusted; arbitrary URLs in the response never become action targets.
            links.Add(new AniBtExternalLink(label, new Uri(prefix + number.ToString(CultureInfo.InvariantCulture))));
        }
        return links;
    }
}

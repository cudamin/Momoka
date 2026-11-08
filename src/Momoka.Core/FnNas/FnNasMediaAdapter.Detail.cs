using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnNas;

public sealed partial class FnNasMediaAdapter
{
    private async Task ResolveEpisodeSeriesAsync(EmbyItem item, CancellationToken ct)
    {
        if (item.Type != EmbyItemType.Episode || !string.IsNullOrWhiteSpace(item.SeriesId)) return;
        if (string.IsNullOrWhiteSpace(item.SeasonId))
            throw new FnNasApiException("飞牛影视单集详情缺少所属季，无法读取剧集列表");
        var season = MapItem(await client.GetItemAsync(connection, item.SeasonId, ct).ConfigureAwait(false));
        if (season.Id != item.SeasonId || season.Type != EmbyItemType.Season || string.IsNullOrWhiteSpace(season.SeriesId))
            throw new FnNasApiException("飞牛影视季详情缺少有效的父剧信息，无法读取剧集列表");
        item.SeriesId = season.SeriesId;
        item.SeasonName = season.Name;
    }

    // 官方 0.9.8 的 JB hook 单独调用 person/list/:guid；最多读取实际展示的40名人员。
    private async Task<List<EmbyPerson>> ReadPeopleAsync(string id, CancellationToken ct)
    {
        var result = new List<EmbyPerson>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; ; page++)
        {
            var response = await OptionalReadAsync(() => client.GetPeopleAsync(connection, id, page, ct), "person/list", ct)
                .ConfigureAwait(false);
            if (response is null) break;
            var values = DetailList(response);
            foreach (var value in values)
            {
                var job = Text(value, "job");
                var personId = Text(value, "person_guid");
                var name = Text(value, "name") is { Length: > 0 } localName ? localName : Text(value, "original_name");
                if (personId.Length == 0 || name.Length == 0 || !seen.Add(personId)) continue;
                result.Add(new EmbyPerson
                {
                    Id = personId,
                    Name = name,
                    Type = job == "Screenplay" ? "Writer" : job,
                    Role = job == "Actor" ? Text(value, "role") : null,
                    PrimaryImageTag = Text(value, "profile_path")
                });
                if (result.Count >= ItemDetail.CastLimit) return result;
            }
            if (values.Count == 0 || page * 200 >= (Integer(response, "total") ?? result.Count)) break;
        }
        return result;
    }

    /// <summary>飞牛没有独立 Similar 路由；使用官方库列表筛选同类型、同题材，再按评分取前几项。</summary>
    public async Task<List<EmbyItem>> GetSimilarAsync(string id, int limit, CancellationToken ct)
    {
        if (limit <= 0) return [];
        await GenreNamesAsync(ct).ConfigureAwait(false);
        var item = MapItem(await client.GetItemAsync(connection, id, ct).ConfigureAwait(false));
        await ResolveEpisodeSeriesAsync(item, ct).ConfigureAwait(false);
        if (item.Type is EmbyItemType.Episode or EmbyItemType.Season)
        {
            if (string.IsNullOrWhiteSpace(item.SeriesId)) return [];
            item = MapItem(await client.GetItemAsync(connection, item.SeriesId, ct).ConfigureAwait(false));
        }
        if (item.Type is not (EmbyItemType.Movie or EmbyItemType.Series)) return [];
        var selection = new FnNasFilterSelection();
        // 筛选要的是服务端自己的题材 ID，不是屏上的名字（见 EmbyItem.NativeGenres）。
        var genres = item.NativeGenres.Count > 0 ? item.NativeGenres : item.Genres;
        var genre = genres.FirstOrDefault(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0);
        if (genre is not null) selection.Genres.Add(genre);
        var query = Query(new ItemQuery
        {
            IncludeItemTypes = [item.Type],
            FnNasFilters = selection,
            Limit = Math.Clamp(limit, 1, 200) + 1,
            SortBy = EmbySortBy.Rating,
            Descending = true
        });
        var result = new List<EmbyItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // 自身或重复行可能占掉首屏名额；最多补三页，保持推荐读取有界。
        for (var page = 1; page <= 3 && result.Count < limit; page++)
        {
            query["page"] = page;
            var response = await client.GetItemsAsync(connection, query, ct).ConfigureAwait(false);
            var values = DetailList(response);
            foreach (var candidate in values.Select(MapItem))
                if (candidate.Type == item.Type && candidate.Id.Length > 0 && candidate.Id != item.Id && candidate.Id != id
                    && seen.Add(candidate.Id)) result.Add(candidate);
            if (values.Count == 0 || page * (Integer(query, "page_size") ?? 1) >= (Integer(response, "total") ?? values.Count)) break;
        }
        return result.Take(limit).ToList();
    }

    // 官方人物页的 ZB 分别取这四种职业；不能省略 job 来猜测“全部作品”协议。
    private async Task<ItemsResult> GetPersonItemsAsync(ItemQuery query, CancellationToken ct)
    {
        if (query.Filters is { IsBlank: false } || query.FnNasFilters is { IsBlank: false } || !SupportsSort(query.SortBy))
            throw new NotSupportedException("飞牛影视尚不支持此人物作品浏览条件");
        var items = new List<EmbyItem>();
        foreach (var job in new[] { "Actor", "Director", "Screenplay", "Producer" })
            for (var page = 1; ; page++)
            {
                var response = await client.GetPersonItemsAsync(connection, query.PersonId!, job, page, ct).ConfigureAwait(false);
                var values = DetailList(response);
                items.AddRange(values.Select(MapItem));
                if (values.Count == 0 || page * 200 >= (Integer(response, "total") ?? values.Count)) break;
            }
        IEnumerable<EmbyItem> unique = items.Where(item => item.Id.Length > 0).DistinctBy(item => item.Id);
        if (query.IncludeItemTypes.Count > 0) unique = unique.Where(item => query.IncludeItemTypes.Contains(item.Type, StringComparer.Ordinal));
        var sorted = query.SortBy switch
        {
            EmbySortBy.DateAdded => query.Descending ? unique.OrderByDescending(item => item.DateCreated) : unique.OrderBy(item => item.DateCreated),
            EmbySortBy.ReleaseDate => query.Descending ? unique.OrderByDescending(item => item.PremiereDate) : unique.OrderBy(item => item.PremiereDate),
            EmbySortBy.Rating => query.Descending ? unique.OrderByDescending(item => item.CommunityRating) : unique.OrderBy(item => item.CommunityRating),
            _ => query.Descending ? unique.OrderByDescending(item => item.Name, StringComparer.CurrentCulture) : unique.OrderBy(item => item.Name, StringComparer.CurrentCulture)
        };
        return Page(sorted, query.StartIndex, query.Limit);
    }

    private static List<JsonObject> DetailList(JsonObject response)
    {
        if (response["list"] is not JsonArray list || list.Any(value => value is not JsonObject))
            throw new FnNasApiException("飞牛影视返回的详情列表格式无效");
        return list.OfType<JsonObject>().ToList();
    }
}

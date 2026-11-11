using Momoka.Emby;

namespace Momoka.Plex;

public sealed partial class PlexMediaAdapter
{
    private readonly Dictionary<string, PlexLibraryCatalog> _libraryCatalogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _catalogGenerations = new(StringComparer.Ordinal);
    private readonly object _catalogLock = new();

    public async Task<PlexLibraryCatalog> GetLibraryCatalogAsync(string section, CancellationToken ct, bool refresh = false)
    {
        PlexEndpoint.Segment(section);
        ct.ThrowIfCancellationRequested();
        long generation;
        lock (_catalogLock)
        {
            if (refresh)
            {
                _libraryCatalogs.Remove(section);
                _catalogGenerations[section] = _catalogGenerations.GetValueOrDefault(section) + 1;
            }
            else if (_libraryCatalogs.TryGetValue(section, out var saved)) return saved;
            generation = _catalogGenerations.GetValueOrDefault(section);
        }
        var sections = await client.GetSectionsAsync(connection, ct).ConfigureAwait(false);
        var entry = Items(sections).FirstOrDefault(item => Text(item, "key") == section);
        if (entry is null) throw new InvalidOperationException("媒体库已不在服务器目录中，请刷新媒体库");
        var details = await client.GetSectionDetailsAsync(connection, section, ct).ConfigureAwait(false);
        var catalog = PlexLibraryCatalog.Parse(section, details, Flag(entry, "filters"));
        ct.ThrowIfCancellationRequested();
        lock (_catalogLock)
        {
            if (_catalogGenerations.GetValueOrDefault(section) == generation) _libraryCatalogs[section] = catalog;
        }
        return catalog;
    }

    public async Task<IReadOnlyList<PlexFilterOption>> GetLibraryFilterOptionsAsync(string section, string type,
        string field, PlexFilterSelection selection, CancellationToken ct)
    {
        var catalog = await GetLibraryCatalogAsync(section, ct).ConfigureAwait(false);
        var nativeType = catalog.RequireType(type);
        if (selection.Type != type) throw new InvalidOperationException("筛选选项所属的媒体类型已改变");
        ValidateFields(nativeType, selection);
        var filter = nativeType.Filters.FirstOrDefault(filter => filter.Field == field)
            ?? throw new InvalidOperationException("筛选条件已不在服务器目录中，请重试或清除筛选");
        if (filter.Kind == "boolean") return [new("", "全部"), new("0", "已看"), new("1", "未看")];
        var values = await client.GetSectionFilterOptionsAsync(connection, section, filter.Endpoint,
            FilterParameters(selection, field), ct).ConfigureAwait(false);
        return PlexLibraryCatalog.Options(values, filter);
    }

    public async Task<IReadOnlyList<PlexFirstCharacter>> GetLibraryFirstCharactersAsync(string section,
        PlexFilterSelection selection, CancellationToken ct)
    {
        var catalog = await GetLibraryCatalogAsync(section, ct).ConfigureAwait(false);
        await ValidateSelectionAsync(catalog, selection, ct).ConfigureAwait(false);
        var value = await client.GetSectionFirstCharactersAsync(connection, section, FilterParameters(selection), ct).ConfigureAwait(false);
        return PlexLibraryCatalog.FirstCharacters(value);
    }

    private async Task<IReadOnlyList<PlexParameter>> LibraryParametersAsync(ItemQuery query, CancellationToken ct)
    {
        var catalog = await GetLibraryCatalogAsync(query.ParentId!, ct).ConfigureAwait(false);
        var selection = query.PlexFilters ?? new PlexFilterSelection();
        if (catalog.Types.Count == 0 && !selection.HasConditions && query.PlexSortKey is null) return Query(query);
        if (selection.Type.Length == 0)
            selection = new PlexFilterSelection { Type = catalog.Types.FirstOrDefault()?.Id ?? "", TypeIsExplicit = false, Values = selection.Values };
        await ValidateSelectionAsync(catalog, selection, ct).ConfigureAwait(false);
        var result = FilterParameters(selection).ToList();
        if (query.PlexSortKey is { Length: > 0 } key)
        {
            var sort = catalog.RequireType(selection.Type).Sorts.FirstOrDefault(sort => sort.Key == key)
                ?? throw new InvalidOperationException("保存的排序已不在服务器目录中，请重新选择排序");
            result.Add(new("sort", sort.QueryKey(query.Descending)));
        }
        else result.AddRange(Query(query).Where(pair => pair.Key == "sort"));
        return result;
    }

    private async Task ValidateSelectionAsync(PlexLibraryCatalog catalog, PlexFilterSelection selection, CancellationToken ct)
    {
        var type = catalog.RequireType(selection.Type);
        ValidateFields(type, selection);
        foreach (var (field, values) in selection.Values)
        {
            if (values.Count == 0) continue;
            var filter = type.Filters.FirstOrDefault(filter => filter.Field == field)
                ?? throw new InvalidOperationException("保存的筛选条件已失效，请重试或清除筛选");
            var options = await GetLibraryFilterOptionsAsync(catalog.SectionId, type.Id, field, selection, ct).ConfigureAwait(false);
            if (values.Any(value => !options.Any(option => option.Value == value)))
                throw new InvalidOperationException("保存的筛选值已失效，请重试或清除筛选");
        }
    }

    private static void ValidateFields(PlexLibraryType type, PlexFilterSelection selection)
    {
        foreach (var (field, values) in selection.Values)
        {
            if (values.Count == 0) continue;
            if (!type.Filters.Any(filter => filter.Field == field) || values.Any(value => value.Length == 0 || value.Any(char.IsControl)))
                throw new InvalidOperationException("保存的筛选条件已失效，请重试或清除筛选");
        }
    }

    internal static IReadOnlyList<PlexParameter> FilterParameters(PlexFilterSelection selection, string? excluding = null)
    {
        var result = new List<PlexParameter>();
        if (selection.Type.Length > 0) result.Add(new("type", selection.Type));
        foreach (var pair in selection.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Key == excluding || pair.Value.Count == 0) continue;
            result.Add(new(pair.Key, string.Join(',', pair.Value)));
        }
        return result;
    }
}

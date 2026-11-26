using Momoka.Emby;

namespace Momoka.Plex;

public sealed partial class PlexMediaAdapter
{
    public async Task<List<EmbyItem>> GetLatestAsync(string? parentId, int limit, string? types, CancellationToken ct)
    {
        if (limit <= 0) return [];
        var requested = types?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (requested.Length == 0)
            return Items(await client.GetRecentlyAddedAsync(connection, parentId, 0, limit, ct).ConfigureAwait(false))
                .Select(Item).ToList();

        // recentlyAdded is a mixed raw sequence. Filter locally without inventing a PMS type
        // parameter, and keep reading raw pages until the requested number of matching rows exists.
        var found = new List<EmbyItem>();
        var offset = 0;
        var pageSize = Math.Max(limit, 20);
        while (found.Count < limit)
        {
            ct.ThrowIfCancellationRequested();
            var page = await client.GetRecentlyAddedAsync(connection, parentId, offset, pageSize, ct).ConfigureAwait(false);
            var raw = Items(page).ToList();
            if (raw.Count == 0) break;
            found.AddRange(raw.Select(Item).Where(item => requested.Contains(item.Type, StringComparer.Ordinal))
                .Take(limit - found.Count));
            offset = checked(offset + raw.Count);
            if (Integer(page, "totalSize") is { } total ? offset >= total : raw.Count < pageSize) break;
        }
        return found;
    }
}

using Momoka.Configuration;

namespace Momoka.Emby;

/// <summary>只保存关键词与选中结果的展示快照，不保存路径、媒体源或凭据。</summary>
public sealed record RecentSearchEntry
{
    public string Term { get; init; } = "";
    public string? ItemId { get; init; }
    public string Name { get; init; } = "";
    public string? Type { get; init; }
    public string? PrimaryImageTag { get; init; }

    public EmbyItem ToItem() => new()
    {
        Id = ItemId ?? "",
        Name = Name,
        Type = Type ?? "",
        ImageTags = PrimaryImageTag is { Length: > 0 } tag ? new() { ["Primary"] = tag } : []
    };
}

public static class RecentSearch
{
    public const int Limit = 20;

    public static IReadOnlyList<RecentSearchEntry> Read(UiSettings ui, EmbyConnection connection) =>
        ui.RecentSearches.TryGetValue(BrowsingPreferences.IdentityKey(connection), out var entries) && entries is not null
            ? entries : [];

    public static void Remember(UiSettings ui, EmbyConnection connection, string term, EmbyItem? item = null)
    {
        var keyword = term.Trim();
        if (keyword.Length == 0) return;
        var key = BrowsingPreferences.IdentityKey(connection);
        var entries = Read(ui, connection).ToList();
        var previous = entries.FirstOrDefault(entry => entry.Term.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        entries.RemoveAll(entry => entry.Term.Equals(keyword, StringComparison.OrdinalIgnoreCase)
            || (item is not null && entry.ItemId == item.Id));
        entries.Insert(0, item is null && previous is not null ? previous : new RecentSearchEntry
        {
            Term = keyword,
            ItemId = item?.Id,
            Name = item?.Name ?? keyword,
            Type = item?.Type,
            PrimaryImageTag = item?.PrimaryImageTag
        });
        ui.RecentSearches[key] = entries.Take(Limit).ToList();
    }

    public static void Remove(UiSettings ui, EmbyConnection connection, RecentSearchEntry entry)
    {
        var key = BrowsingPreferences.IdentityKey(connection);
        if (!ui.RecentSearches.TryGetValue(key, out var entries)) return;
        entries.Remove(entry);
        if (entries.Count == 0) ui.RecentSearches.Remove(key);
    }

    public static void Clear(UiSettings ui, EmbyConnection connection) =>
        ui.RecentSearches.Remove(BrowsingPreferences.IdentityKey(connection));
}

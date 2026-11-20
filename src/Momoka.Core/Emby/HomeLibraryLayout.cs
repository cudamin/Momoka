using Momoka.Configuration;

namespace Momoka.Emby;

/// <summary>主页「媒体库」横排中的库入口；最近添加的货架另由 <see cref="HomeLayout"/> 决定。</summary>
public static class HomeLibraryLayout
{
    /// <summary>MoviePilot 订阅汇总库：参与入口次序，但不是服务器目录中的 EmbyItem。</summary>
    public const string SubscriptionLibraryKey = "mp-subscriptions";

    /// <summary>取实际目录里的库名与账号级键，不带最近添加或账号名前缀。</summary>
    public static IReadOnlyList<HomeRowPlan> FromItems(IEnumerable<EmbyItem> libraries) =>
        [.. CurrentItems(libraries).Select(item => new HomeRowPlan(HomeLayout.LibraryKey(item), item.Name, Visible: true))];

    /// <summary>当前可用的库按保存顺序排列，新库保留目录里的相对次序并显示在后面。</summary>
    public static IReadOnlyList<HomeRowPlan> Plan(IReadOnlyList<HomeRowSetting>? saved, IEnumerable<HomeRowPlan> available)
    {
        var directory = available.Where(row => IsEntry(row.Key)).DistinctBy(row => row.Key, StringComparer.Ordinal).ToArray();
        var remaining = directory.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var result = new List<HomeRowPlan>(directory.Length);
        foreach (var row in SavedEntries(saved))
            if (remaining.Remove(row.Key, out var current)) result.Add(current with { Visible = row.Visible });

        foreach (var row in directory)
            if (remaining.ContainsKey(row.Key)) result.Add(row with { Visible = true });

        return result;
    }

    /// <summary>只替换当前编辑的库入口；暂时不可用的库及其它账号的选择留在原位。</summary>
    public static List<HomeRowSetting> MergeEdits(IReadOnlyList<HomeRowSetting>? saved,
        IReadOnlyList<HomeRowPlan> available, IEnumerable<HomeRowPlan> edits)
    {
        var current = Plan(saved, available);
        var lookup = current.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var changes = edits.Where(row => lookup.ContainsKey(row.Key))
            .Select(row => lookup[row.Key] with { Visible = row.Visible }).ToArray();
        var previous = SavedEntries(saved).ToArray();
        if (changes.Length == 0) return HomeLayout.Save(previous);

        var active = lookup.Keys.ToHashSet(StringComparer.Ordinal);
        var merged = new List<HomeRowPlan>(previous.Length + changes.Length);
        var pending = new Queue<HomeRowPlan>(changes);
        foreach (var row in previous)
        {
            if (active.Contains(row.Key) && pending.TryDequeue(out var replacement)) merged.Add(replacement);
            else merged.Add(row);
        }

        merged.AddRange(pending);
        return HomeLayout.Save(merged);
    }

    public static IReadOnlyList<EmbyItem> VisibleItems(IReadOnlyList<HomeRowSetting>? saved, IEnumerable<EmbyItem> libraries)
    {
        var directory = CurrentItems(libraries).ToArray();
        if (saved is not { Count: > 0 }) return directory;

        var items = directory.ToDictionary(HomeLayout.LibraryKey, StringComparer.Ordinal);
        return [.. Plan(saved, directory.Select(item => new HomeRowPlan(HomeLayout.LibraryKey(item), item.Name, Visible: true)))
            .Where(row => row.Visible && IsLibrary(row.Key)).Select(row => items[row.Key])];
    }

    private static IEnumerable<EmbyItem> CurrentItems(IEnumerable<EmbyItem> libraries) => libraries
        .Where(library => library.Id is { Length: > 0 } && library.Source is not { IsAvailable: false })
        .DistinctBy(HomeLayout.LibraryKey, StringComparer.Ordinal);

    private static IEnumerable<HomeRowPlan> SavedEntries(IReadOnlyList<HomeRowSetting>? saved) => (saved ?? [])
        .Where(row => row is not null && IsEntry(row.Key))
        .Select(row => new HomeRowPlan(row.Key, row.Title, row.Visible))
        .DistinctBy(row => row.Key, StringComparer.Ordinal);

    private static bool IsLibrary(string? key) => HomeLayout.LibraryId(key) is { Length: > 0 };

    private static bool IsEntry(string? key) => IsLibrary(key) || key == SubscriptionLibraryKey;
}

using Momoka.Infrastructure;

namespace Momoka.Emby;

/// <summary>One online identity a snapshot was read with; the owner reference is the login generation.</summary>
public sealed record SnapshotSource(string Key, object Owner)
{
    public static SnapshotSource Of(EmbySessionScope scope) => new(scope.Key, scope.UserStateOwner);
}

/// <summary>One home shelf's rows as they were drawn, plus the note the heading carried.</summary>
public sealed record HomeRowSnapshot(string Key, IReadOnlyList<EmbyItem> Items, string? Note);

/// <summary>
/// What the home page showed for one identity, layout and carousel configuration. Items only: cards,
/// decoded bitmaps and the page itself stay outside, so an evicted snapshot is collectable at once.
/// </summary>
public sealed class HomeSnapshot
{
    public required IReadOnlyList<HomeRowSnapshot> Rows { get; init; }

    public required IReadOnlyList<EmbyItem> SlideItems { get; init; }

    public required int LoadedCount { get; init; }

    public required string? Subheading { get; init; }

    public required bool Banner { get; init; }

    public int ItemCount => Rows.Sum(row => row.Items.Count) + SlideItems.Count;
}

/// <summary>
/// What one library grid had read when the reader left it. <see cref="Cursor"/> is already a private
/// copy; a restore must copy it again before consuming pages, because two restores of the same entry
/// must not share offsets.
/// </summary>
public sealed class LibrarySnapshot
{
    public required IReadOnlyList<EmbyItem> Items { get; init; }

    public required int Total { get; init; }

    public required bool FirstPage { get; init; }

    public required int UpcomingReadCount { get; init; }

    public required IReadOnlyCollection<string> ResumeRead { get; init; }

    public required AggregateQueryCursor? Cursor { get; init; }
}

/// <summary>
/// Bounded snapshots for the home page and library grids. Two small LRU caches, a per-snapshot item
/// ceiling and a time-to-live: warm returns reuse content that belonged to the same login, source set
/// and query, while a changed account, removed source or expired entry falls back to the normal read.
/// <para>
/// 快照不延长页面寿命，也不持有卡片或位图。淘汰只丢引用；来源身份按 <see cref="EmbySessionScope.UserStateOwner"/>
/// 的引用核对，所以同一账号退出后重新登录也不会命中上一代登录的内容。
/// </para>
/// </summary>
public sealed class BrowseSnapshotStore
{
    /// <summary>Four pages at the shipped page size. Deeper reads are deliberately not cached.</summary>
    public const int MaxItemsPerSnapshot = 400;

    public const int DefaultHomeCapacity = 3;
    public const int DefaultLibraryCapacity = 6;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    private readonly LruCache<string, Stamped<HomeSnapshot>> _home;
    private readonly LruCache<string, Stamped<LibrarySnapshot>> _libraries;
    private readonly TimeSpan _lifetime;
    private readonly Func<DateTimeOffset> _clock;

    public BrowseSnapshotStore(
        int homeCapacity = DefaultHomeCapacity,
        int libraryCapacity = DefaultLibraryCapacity,
        TimeSpan? lifetime = null,
        Func<DateTimeOffset>? clock = null)
    {
        _home = new LruCache<string, Stamped<HomeSnapshot>>(homeCapacity);
        _libraries = new LruCache<string, Stamped<LibrarySnapshot>>(libraryCapacity);
        _lifetime = lifetime ?? DefaultLifetime;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>For diagnostics and tests; eviction order is the contract they read.</summary>
    public IReadOnlyList<string> HomeKeys => _home.Keys;

    public IReadOnlyList<string> LibraryKeys => _libraries.Keys;

    public bool TryGetHome(string key, IReadOnlyList<SnapshotSource> sources, out HomeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(sources);

        if (!_home.TryGet(key, out var stamped))
        {
            snapshot = null!;
            return false;
        }

        if (Expired(stamped) || !SameSources(stamped.Sources, sources))
        {
            _home.Remove(key);
            snapshot = null!;
            return false;
        }

        snapshot = stamped.Value;
        return true;
    }

    public void StoreHome(string key, IReadOnlyList<SnapshotSource> sources, HomeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.ItemCount > MaxItemsPerSnapshot)
        {
            // 超限说明这一页已经不是「一次返回能记住」的规模；留着旧快照会让恢复的位置比实际少。
            _home.Remove(key);
            return;
        }

        var copy = new HomeSnapshot
        {
            Rows = [.. snapshot.Rows.Select(row => new HomeRowSnapshot(row.Key, [.. row.Items], row.Note))],
            SlideItems = [.. snapshot.SlideItems],
            LoadedCount = snapshot.LoadedCount,
            Subheading = snapshot.Subheading,
            Banner = snapshot.Banner
        };
        _home.Set(key, new Stamped<HomeSnapshot>(copy, [.. sources], _clock()));
    }

    public bool TryGetLibrary(string key, IReadOnlyList<SnapshotSource> sources, out LibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(sources);

        if (!_libraries.TryGet(key, out var stamped))
        {
            snapshot = null!;
            return false;
        }

        if (Expired(stamped) || !SameSources(stamped.Sources, sources) || stamped.Value.Items.Count > MaxItemsPerSnapshot)
        {
            _libraries.Remove(key);
            snapshot = null!;
            return false;
        }

        snapshot = stamped.Value;
        return true;
    }

    public void StoreLibrary(string key, IReadOnlyList<SnapshotSource> sources, LibrarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Items.Count > MaxItemsPerSnapshot)
        {
            _libraries.Remove(key);
            return;
        }

        var copy = new LibrarySnapshot
        {
            Items = [.. snapshot.Items],
            Total = snapshot.Total,
            FirstPage = snapshot.FirstPage,
            UpcomingReadCount = snapshot.UpcomingReadCount,
            ResumeRead = [.. snapshot.ResumeRead],
            Cursor = snapshot.Cursor?.Copy()
        };
        _libraries.Set(key, new Stamped<LibrarySnapshot>(copy, [.. sources], _clock()));
    }

    public void InvalidateHome(string key) => _home.Remove(key);

    public void InvalidateLibrary(string key) => _libraries.Remove(key);

    public void Clear()
    {
        _home.Clear();
        _libraries.Clear();
    }

    private bool Expired<T>(Stamped<T> stamped) => _clock() - stamped.CapturedAt >= _lifetime;

    private static bool SameSources(IReadOnlyList<SnapshotSource> stored, IReadOnlyList<SnapshotSource> current)
    {
        if (stored.Count != current.Count) return false;

        foreach (var token in stored)
        {
            SnapshotSource? match = null;
            foreach (var source in current)
            {
                if (!string.Equals(source.Key, token.Key, StringComparison.Ordinal)) continue;
                match = source;
                break;
            }

            if (match is null || !ReferenceEquals(match.Owner, token.Owner)) return false;
        }

        return true;
    }

    private sealed record Stamped<T>(T Value, IReadOnlyList<SnapshotSource> Sources, DateTimeOffset CapturedAt);
}

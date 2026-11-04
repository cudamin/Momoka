using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Infrastructure;

namespace Momoka.Shell.Media;

/// <summary>
/// Per-application detail backgrounds. One entry keeps original pixels and only the most recently
/// rendered radius; its cost includes both. Tickets belong to an exact login, artwork tag and clear
/// generation. No page, delegate or task is retained. XAML objects are only obtained on the UI thread;
/// source notifications may run on a worker and therefore only remove references and numeric costs.
/// </summary>
internal sealed class DetailHeroCache : IDisposable
{
    internal const long DefaultBudgetBytes = 24L * 1024 * 1024;
    private const int MaxTickets = 128;
    private readonly EmbySession _session;
    private readonly object _gate = new();
    private readonly BudgetCache<Key, Entry> _entries;
    private readonly Dictionary<Key, Ticket> _tickets = [];
    private long _generation;
    private bool _disposed;

    internal DetailHeroCache(EmbySession session, long budgetBytes = DefaultBudgetBytes)
    {
        _session = session;
        _entries = new(budgetBytes, entry => entry.Cost);
        session.SourcesChanged += OnSourcesChanged;
    }

    internal long Bytes => _entries.Bytes;
    internal long Generation { get { lock (_gate) return _generation; } }

    internal readonly record struct Key(object Owner, string Image, int Width);

    internal sealed record Ticket(EmbySessionScope Scope, Key Key, string Tag, long Generation);

    private sealed record Entry(Ticket Ticket, HeroPictureLoader.HeroPixels Pixels,
        int Radius, bool Gaussian, BitmapImage? Picture, long Cost);

    internal Ticket Begin(EmbySessionScope scope, ArtworkRef artwork, int width, long? generation = null)
    {
        // The family excludes tag so a new tag invalidates both retained data and outstanding tickets.
        // Request width stays exact: a smaller processed image is never a final answer for a larger one.
        var key = new Key(scope.UserStateOwner,
            EmbyImageStore.CacheKey(scope.Connection, artwork.ItemId, artwork.ImageType, "", 0), width);
        lock (_gate)
        {
            if (generation is { } expected && expected != _generation)
                return new Ticket(scope, key, artwork.Tag, expected);
            if (_tickets.TryGetValue(key, out var old) && old.Tag == artwork.Tag && Valid(old)) return old;
            _entries.Remove(key);
            var ticket = new Ticket(scope, key, artwork.Tag, _generation);
            if (_disposed || !scope.IsAvailable) return ticket;
            _tickets.Remove(key);
            // Bound even requests that fail before producing pixels. Evicted tickets can finish for
            // their current page but can no longer refill this cache.
            if (_tickets.Count >= MaxTickets)
            {
                var oldest = _tickets.Keys.First();
                _tickets.Remove(oldest);
                _entries.Remove(oldest);
            }
            _tickets.Add(key, ticket);
            return ticket;
        }
    }

    internal bool TryGetPixels(Ticket ticket, out HeroPictureLoader.HeroPixels pixels)
    {
        lock (_gate)
        {
            if (Valid(ticket) && _entries.TryGet(ticket.Key, out var entry))
            {
                pixels = entry.Pixels;
                return true;
            }
            pixels = null!;
            return false;
        }
    }

    internal bool TryGetPicture(Ticket ticket, HeroPictureLoader.HeroPixels pixels, int radius, out BitmapImage picture, bool gaussian = false)
    {
        lock (_gate)
        {
            if (Valid(ticket) && _entries.TryGet(ticket.Key, out var entry)
                && ReferenceEquals(entry.Pixels, pixels) && entry.Radius == radius && entry.Gaussian == gaussian && entry.Picture is { } kept)
            {
                picture = kept;
                return true;
            }
            picture = null!;
            return false;
        }
    }

    internal void RememberPixels(Ticket ticket, HeroPictureLoader.HeroPixels pixels)
    {
        lock (_gate)
        {
            if (!Valid(ticket)) return;
            if (_entries.TryGet(ticket.Key, out var existing) && ReferenceEquals(existing.Pixels, pixels)) return;
            _entries.Set(ticket.Key, new(ticket, pixels, -1, false, null, pixels.Pixels.LongLength));
        }
    }

    internal void RememberPicture(Ticket ticket, HeroPictureLoader.HeroPixels pixels, int radius, BitmapImage picture, bool gaussian = false)
    {
        lock (_gate)
        {
            if (!Valid(ticket)) return;
            // HeroPictureLoader always passes a same-size BMP to Physical-width BitmapImage. Use
            // those known dimensions, never UI-affine bitmap getters in the shared bookkeeping.
            var cost = pixels.Pixels.LongLength + (long)pixels.Width * pixels.Height * 4;
            _entries.Set(ticket.Key, new(ticket, pixels, radius, gaussian, picture, cost));
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _generation++;
            _tickets.Clear();
            _entries.Clear();
        }
    }

    private bool Valid(Ticket ticket) => !_disposed && ticket.Generation == _generation
        && _tickets.TryGetValue(ticket.Key, out var current) && ReferenceEquals(current, ticket)
        && ticket.Scope.IsAvailable;

    private void OnSourcesChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            // SourcesChanged is raised outside the session lock. Do not register cancellation
            // callbacks that would invert this cache -> session lock order during Adopt.
            foreach (var key in _tickets.Where(pair => !pair.Value.Scope.IsAvailable).Select(pair => pair.Key).ToArray())
            {
                _tickets.Remove(key);
                _entries.Remove(key);
            }
        }
    }

    public void Dispose()
    {
        _session.SourcesChanged -= OnSourcesChanged;
        lock (_gate)
        {
            _disposed = true;
            Clear();
        }
    }
}

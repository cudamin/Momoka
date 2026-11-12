using Momoka.Emby;

namespace Momoka.Playback;

/// <summary>
/// One viewing's pending video items. Server playlists and the current series navigation list have
/// separate owners. A reserved item is removed only after the backend confirms its actual start.
/// </summary>
public sealed class SessionPlaybackQueue
{
    public sealed record Entry(long Slot, EmbyItem Item, EmbySessionScope Scope)
    {
        public IReadOnlyList<EmbyItem>? Navigation { get; init; }
    }

    private readonly object _gate = new();
    private readonly List<Entry> _pending = [];
    private Entry? _reserved;
    private long _nextSlot;
    private bool _active;
    private bool _seeded;
    private bool _explicit;

    public bool Active { get { lock (_gate) return _active; } }
    public bool HasReservation { get { lock (_gate) return _reserved is not null; } }
    public int Count { get { lock (_gate) return _pending.Count; } }
    public IReadOnlyList<Entry> Pending { get { lock (_gate) return _pending.ToArray(); } }

    public static bool Supports(EmbyItem item) => item.IsPlayable && !EmbyItemType.IsMusic(item.Type)
        && !string.IsNullOrWhiteSpace(item.Id);

    public void Begin()
    {
        lock (_gate)
        {
            _pending.Clear();
            _reserved = null;
            _active = true;
            _seeded = false;
            _explicit = false;
        }
    }

    /// <summary>Only an accepted initial play-all order seeds pending slots, preserving duplicate IDs.</summary>
    public void AdoptInitial(EmbyItem current, IReadOnlyList<EmbyItem> order)
    {
        lock (_gate)
        {
            if (!_active || _seeded) return;
            _seeded = true;
            if (order is not UserPlaybackQueue queue) return;
            var index = queue.IndexOfCurrent(current.IdentityKey);
            if (index < 0) return;
            for (var offset = index + 1; offset < queue.Count; offset++)
            {
                var item = queue[offset];
                if (Supports(item) && item.Source is { } scope)
                    _pending.Add(new(++_nextSlot, item, scope)
                    { Navigation = new UserPlaybackQueue(queue) { CurrentIndex = offset } });
            }
        }
    }

    public bool Add(EmbyItem item, EmbySessionScope scope, bool next)
    {
        if (!Supports(item) || !scope.IsAvailable || item.Source?.Key != scope.Key) return false;
        lock (_gate)
        {
            if (!_active || _reserved is not null) return false;
            var entry = new Entry(++_nextSlot, item, scope);
            if (next) _pending.Insert(0, entry);
            else _pending.Add(entry);
            _explicit = true;
            return true;
        }
    }

    public bool ShouldContinue(bool autoPlayEnabled)
    {
        lock (_gate) return (_explicit || autoPlayEnabled)
            && _pending.Any(entry => !ReferenceEquals(entry, _reserved) && entry.Scope.IsAvailable);
    }

    /// <summary>Revoked accounts are discarded, never rebound to the default or a replacement owner.</summary>
    public Entry? Peek(bool autoPlayEnabled = true)
    {
        lock (_gate)
        {
            _pending.RemoveAll(entry => !entry.Scope.IsAvailable);
            if (_reserved?.Scope.IsAvailable == false) _reserved = null;
            return (_explicit || autoPlayEnabled) && _reserved is null ? _pending.FirstOrDefault() : null;
        }
    }

    public bool Reserve(Entry entry)
    {
        lock (_gate)
        {
            if (_reserved is not null || !_pending.Contains(entry) || !entry.Scope.IsAvailable) return false;
            _reserved = entry;
            return true;
        }
    }

    public Entry? ReservedFor(EmbyItem item, EmbySessionScope scope)
    {
        lock (_gate) return _reserved is { } entry && entry.Item.IdentityKey == item.IdentityKey
            && ReferenceEquals(entry.Scope.UserStateOwner, scope.UserStateOwner) ? entry : null;
    }

    public bool Confirm(Entry? entry)
    {
        lock (_gate)
        {
            if (entry is null || !ReferenceEquals(_reserved, entry)) return false;
            _reserved = null;
            return _pending.Remove(entry);
        }
    }

    public void Release(Entry? entry)
    {
        lock (_gate)
            if (entry is not null && ReferenceEquals(_reserved, entry)) _reserved = null;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            _reserved = null;
            _active = false;
            _seeded = false;
            _explicit = false;
        }
    }
}

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
    private bool _edited;

    public bool Edited { get { lock (_gate) return _edited; } }
    public bool Active { get { lock (_gate) return _active; } }
    public bool HasReservation { get { lock (_gate) return _reserved is not null; } }
    public int Count { get { lock (_gate) return _pending.Count; } }
    public IReadOnlyList<Entry> Pending { get { lock (_gate) return _pending.Where(entry => entry.Scope.IsAvailable).ToArray(); } }

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
            _edited = false;
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

    public bool Add(EmbyItem item, EmbySessionScope scope, bool next) => Add([item], scope, next);

    public bool Add(IReadOnlyList<EmbyItem> items, EmbySessionScope scope, bool next)
    {
        lock (_gate)
        {
            if (!_active || _reserved is not null || !scope.IsAvailable || items.Count == 0
                || items.Any(item => !Supports(item) || !ReferenceEquals(item.Source?.UserStateOwner, scope.UserStateOwner)))
                return false;
            var entries = items.Select(item => new Entry(++_nextSlot, item, scope));
            _pending.InsertRange(next ? 0 : _pending.Count, entries);
            _explicit = true;
            return true;
        }
    }

    public bool Move(long slot, int offset)
    {
        if (offset is not (-1 or 1)) return false;
        lock (_gate)
        {
            var index = IndexForEdit(slot);
            var next = index + offset;
            if (index < 0 || next < 0 || next >= _pending.Count) return false;
            (_pending[index], _pending[next]) = (_pending[next], _pending[index]);
            _edited = true;
            return true;
        }
    }

    public bool Remove(long slot)
    {
        lock (_gate)
        {
            var index = IndexForEdit(slot);
            if (index < 0) return false;
            _pending.RemoveAt(index);
            _edited = true;
            return true;
        }
    }

    private int IndexForEdit(long slot)
    {
        if (!_active || _reserved is not null) return -1;
        _pending.RemoveAll(entry => !entry.Scope.IsAvailable);
        return _pending.FindIndex(entry => entry.Slot == slot);
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
            _edited = false;
        }
    }
}

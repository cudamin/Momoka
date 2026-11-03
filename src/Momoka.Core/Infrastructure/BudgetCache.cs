using System.Diagnostics.CodeAnalysis;

namespace Momoka.Infrastructure;

/// <summary>
/// A cache bounded by the total size its entries claim, rather than by how many there are.
/// <para>
/// <see cref="LruCache{TKey,TValue}"/> counts entries because the decoded posters it holds are all about the
/// same size. A carousel backdrop is not: the same request comes back as 1280×720 or 1280×1600 depending
/// on what the server stored, and the decoded surface follows. A count cap would let eight panoramas hold
/// a hundred megabytes, so this one asks the value for its cost and evicts by that.
/// </para>
/// <para>
/// The caller supplies the cost because only it knows what the value keeps alive. The cache never calls
/// the cost function under a lock it did not create for that value; the function must be quick and must
/// not re-enter this cache.
/// </para>
/// </summary>
public sealed class BudgetCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _order = new();
    private readonly object _gate = new();
    private readonly Func<TValue, long> _cost;
    private long _bytes;

    /// <param name="capacityBytes">
    /// The most the cache may hold. Zero means it stores nothing — useful for a caller that has switched
    /// the cache off without changing the rest of its code.
    /// </param>
    /// <param name="cost">What one value occupies. Negative answers are treated as zero.</param>
    public BudgetCache(long capacityBytes, Func<TValue, long> cost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);
        ArgumentNullException.ThrowIfNull(cost);

        CapacityBytes = capacityBytes;
        _cost = cost;
    }

    public long CapacityBytes { get; }

    /// <summary>What the entries currently claim, in the same unit the cost function returns.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate) return _bytes;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <summary>The keys held, most recently used first. For tests: eviction order is the whole contract.</summary>
    public IReadOnlyList<TKey> Keys
    {
        get
        {
            lock (_gate) return _order.Select(entry => entry.Key).ToList();
        }
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                value = default!;
                return false;
            }

            Promote(node);
            value = node.Value.Value;
            return true;
        }
    }

    /// <summary>
    /// Adds or replaces, then evicts the least recently used entries until the total fits. Returns false
    /// when the value alone is larger than the whole budget — storing it would evict everything else and
    /// still overrun, so the honest answer is that this cache cannot hold it.
    /// </summary>
    public bool Set(TKey key, TValue value)
    {
        var cost = Math.Max(0, _cost(value));

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                Detach(existing);
                _entries.Remove(key);
            }

            if (CapacityBytes <= 0 || cost > CapacityBytes) return false;

            var node = new LinkedListNode<Entry>(new Entry(key, value, cost));
            _order.AddFirst(node);
            _entries[key] = node;
            _bytes += cost;

            while (_bytes > CapacityBytes && _order.Last is { } oldest)
            {
                Detach(oldest);
                _entries.Remove(oldest.Value.Key);
            }

            return true;
        }
    }

    /// <summary>Drops one entry without touching the rest.</summary>
    public bool Remove(TKey key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node)) return false;
            Detach(node);
            _entries.Remove(key);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
            _bytes = 0;
        }
    }

    private void Detach(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _bytes -= node.Value.Cost;
    }

    /// <summary>
    /// Moves a node to the front. Removing first is what makes re-adding it legal — a node still owned
    /// by a list cannot be inserted into one — and it keeps the value's identity, so nothing that holds
    /// the node sees a different object.
    /// </summary>
    private void Promote(LinkedListNode<Entry> node)
    {
        _order.Remove(node);
        _order.AddFirst(node);
    }

    private readonly record struct Entry(TKey Key, TValue Value, long Cost);
}

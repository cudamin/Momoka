using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// A stable, mutable ItemsSource. Ordinary shelves add ready cards; an episode shelf keeps DTO slots
/// and creates only the card requested by the layout's global index.
/// </summary>
/// <remarks>
/// The non-generic IList is intentional: WinUI InspectingDataSource (693f472, matching the shipped
/// Microsoft.UI.Xaml.dll) consumes its IBindableVector projection with Count/GetAt. An iterable-only
/// source is copied by WrapIterable and would create every episode before the first layout.
/// </remarks>
public sealed class CardCollection : IList<CardItem>, IList, IReadOnlyList<CardItem>,
    INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly List<Slot> _slots = [];
    private Func<EmbyItem, CardItem>? _build;
    private int _version;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _slots.Count;
    public bool IsReadOnly => false;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    internal int MaterializedCount => _slots.Count(slot => slot.Card is not null);

    // Cleanup alone uses this view. The public enumerators must still enumerate all logical items.
    internal IEnumerable<CardItem> Materialized => _slots.Where(slot => slot.Card is not null)
        .Select(slot => slot.Card!);

    public CardItem this[int index]
    {
        get
        {
            var slot = _slots[index];
            if (slot.Card is { } existing) return existing;
            var version = _version;
            var card = _build!(slot.Item);
            if (version != _version)
            {
                card.AbandonPoster();
                throw new InvalidOperationException("卡片集合在创建期间已被替换");
            }
            _slots[index] = slot with { Card = card };
            return card;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = _slots[index].Card;
            _slots[index] = new(value.Item, value);
            Changed(new(NotifyCollectionChangedAction.Replace, value, previous, index), countChanged: false);
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = RequireCard(value);
    }

    internal void ResetDeferred(IReadOnlyList<EmbyItem> items, Func<EmbyItem, CardItem> build)
    {
        _slots.Clear();
        _slots.EnsureCapacity(items.Count);
        foreach (var item in items) _slots.Add(new(item, null));
        _build = build;
        Changed(new(NotifyCollectionChangedAction.Reset), countChanged: true);
    }

    internal int RemoveWhere(Predicate<EmbyItem> match)
    {
        var removed = new List<CardItem>();
        var count = _slots.RemoveAll(slot =>
        {
            if (!match(slot.Item)) return false;
            if (slot.Card is { } card) removed.Add(card);
            return true;
        });
        if (count == 0) return 0;
        foreach (var card in removed) card.AbandonPoster();
        Changed(new(NotifyCollectionChangedAction.Reset), countChanged: true);
        return count;
    }

    public void Add(CardItem item) => Insert(Count, item);

    int IList.Add(object? value)
    {
        var index = Count;
        Add(RequireCard(value));
        return index;
    }

    public void Insert(int index, CardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _slots.Insert(index, new(item.Item, item));
        Changed(new(NotifyCollectionChangedAction.Add, item, index), countChanged: true);
    }

    void IList.Insert(int index, object? value) => Insert(index, RequireCard(value));

    public void Move(int oldIndex, int newIndex)
    {
        if ((uint)newIndex >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
        var slot = _slots[oldIndex];
        _slots.RemoveAt(oldIndex);
        _slots.Insert(newIndex, slot);
        Changed(new(NotifyCollectionChangedAction.Move, slot.Card, newIndex, oldIndex), countChanged: false);
    }

    public bool Remove(CardItem item)
    {
        var index = IndexOf(item);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    void IList.Remove(object? value)
    {
        if (value is CardItem card) Remove(card);
    }

    public void RemoveAt(int index)
    {
        var previous = _slots[index].Card;
        _slots.RemoveAt(index);
        // WinUI also uses null item payloads for vector removal; index and count carry the change.
        Changed(new(NotifyCollectionChangedAction.Remove, previous, index), countChanged: true);
    }

    public void Clear()
    {
        _slots.Clear();
        _build = null;
        Changed(new(NotifyCollectionChangedAction.Reset), countChanged: true);
    }

    public bool Contains(CardItem item) => IndexOf(item) >= 0;
    bool IList.Contains(object? value) => value is CardItem card && Contains(card);

    public int IndexOf(CardItem item) => item is null ? -1
        : _slots.FindIndex(slot => ReferenceEquals(slot.Card, item));
    int IList.IndexOf(object? value) => value is CardItem card ? IndexOf(card) : -1;

    public void CopyTo(CardItem[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count) throw new ArgumentException("目标数组空间不足", nameof(array));
        for (var index = 0; index < Count; index++) array[arrayIndex + index] = this[index];
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array.Rank != 1 || array.GetLowerBound(0) != 0) throw new ArgumentException("需要一维零起点数组", nameof(array));
        if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (array.Length - index < Count) throw new ArgumentException("目标数组空间不足", nameof(array));
        for (var item = 0; item < Count; item++) array.SetValue(this[item], index + item);
    }

    public IEnumerator<CardItem> GetEnumerator()
    {
        var version = _version;
        for (var index = 0; index < Count; index++)
        {
            if (version != _version) throw new InvalidOperationException("枚举期间卡片集合已改变");
            yield return this[index];
        }
        if (version != _version) throw new InvalidOperationException("枚举期间卡片集合已改变");
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Changed(NotifyCollectionChangedEventArgs args, bool countChanged)
    {
        var version = ++_version;
        if (countChanged) PropertyChanged?.Invoke(this, new(nameof(Count)));
        if (version != _version) return;
        PropertyChanged?.Invoke(this, new("Item[]"));
        if (version == _version) CollectionChanged?.Invoke(this, args);
    }

    private static CardItem RequireCard(object? value) => value as CardItem
        ?? throw new ArgumentException("值必须是 CardItem", nameof(value));

    private readonly record struct Slot(EmbyItem Item, CardItem? Card);
}

using System.Collections.ObjectModel;

namespace Momoka.Shell.ViewModels;

/// <summary>Update unique card/day references without replacing their bound collection or clearing it.</summary>
internal static class UpcomingListDiff
{
    // The optional comparer lets the real algorithm's reference comparisons be counted in regressions.
    // Production callers always use reference identity, not a card's DTO/source key or value equality.
    internal static void Apply<T>(ObservableCollection<T> current, IReadOnlyList<T> desired,
        IEqualityComparer<T>? comparer = null) where T : class
    {
        comparer ??= ReferenceEqualityComparer.Instance;
        var prefix = 0;
        while (prefix < current.Count && prefix < desired.Count
            && comparer.Equals(current[prefix], desired[prefix])) prefix++;

        // Normal paging: O(N + M) comparisons/append work, no per-card Contains/IndexOf searches.
        if (prefix == current.Count)
        {
            for (var index = prefix; index < desired.Count; index++) current.Add(desired[index]);
            return;
        }
        if (prefix == desired.Count)
        {
            for (var index = current.Count - 1; index >= prefix; index--) current.RemoveAt(index);
            return;
        }

        var wanted = new HashSet<T>(desired, comparer);
        for (var index = current.Count - 1; index >= prefix; index--)
            if (!wanted.Contains(current[index])) current.RemoveAt(index);

        var positions = new Dictionary<T, Position>(current.Count, comparer);
        var order = new List<Position>(current.Count);
        for (var index = 0; index < current.Count; index++)
        {
            var position = new Position(index);
            positions.Add(current[index], position);
            order.Add(position);
        }
        for (var index = prefix; index < desired.Count; index++)
        {
            var item = desired[index];
            if (index < current.Count && comparer.Equals(current[index], item)) continue;
            if (positions.TryGetValue(item, out var position))
            {
                var oldIndex = position.Index;
                current.Move(oldIndex, index);
                order.RemoveAt(oldIndex);
                order.Insert(index, position);
                // Mirror only the numeric shifts already paid by ObservableCollection.Move. Updating
                // dictionary keys for this range would reintroduce quadratic identity comparisons.
                for (var shifted = index; shifted <= oldIndex; shifted++) order[shifted].Index = shifted;
            }
            else
            {
                current.Insert(index, item);
                position = new Position(index);
                positions.Add(item, position);
                order.Insert(index, position);
                for (var shifted = index + 1; shifted < order.Count; shifted++) order[shifted].Index = shifted;
            }
        }
    }

    private sealed class Position(int index)
    {
        internal int Index { get; set; } = index;
    }
}

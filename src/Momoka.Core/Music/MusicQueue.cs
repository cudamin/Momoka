using Momoka.Emby;

namespace Momoka.Music;

public enum MusicRepeat { None, All, One }

/// <summary>Slot-based music order, including duplicate songs and the actual shuffle history.</summary>
public sealed class MusicQueue
{
    private readonly Stack<int> _history = new();
    private readonly HashSet<int> _visited = [];
    public IReadOnlyList<EmbyItem> Items { get; private set; } = [];
    public int Index { get; private set; } = -1;
    public MusicRepeat Repeat { get; set; }
    public bool Shuffle { get; set; }

    public void Replace(IEnumerable<EmbyItem> items, int index)
    {
        var snapshot = items.ToArray();
        if (snapshot.Any(item => item.Type != EmbyItemType.Audio))
            throw new ArgumentException("音乐队列只能包含歌曲", nameof(items));
        if (snapshot.Length > 0 && (index < 0 || index >= snapshot.Length))
            throw new ArgumentOutOfRangeException(nameof(index));
        Items = Array.AsReadOnly(snapshot);
        Index = snapshot.Length == 0 ? -1 : index;
        _history.Clear();
        _visited.Clear();
        if (Index >= 0) _visited.Add(Index);
    }

    public int Select(int index)
    {
        if (index < 0 || index >= Items.Count) return -1;
        if (Index >= 0 && Index != index) _history.Push(Index);
        Index = index;
        _visited.Add(index);
        return index;
    }

    public int Next(bool automatic)
    {
        if (Index < 0) return -1;
        if (automatic && Repeat == MusicRepeat.One) return Index;
        if (!Shuffle)
            return Index + 1 < Items.Count ? Select(Index + 1)
                : Repeat == MusicRepeat.All ? Select(0) : -1;

        var choices = Enumerable.Range(0, Items.Count).Where(index => index != Index && !_visited.Contains(index)).ToArray();
        if (choices.Length == 0)
        {
            if (Repeat != MusicRepeat.All && automatic) return -1;
            _visited.Clear();
            _visited.Add(Index);
            choices = Enumerable.Range(0, Items.Count).Where(index => index != Index).ToArray();
        }
        return choices.Length == 0 ? Index : Select(choices[Random.Shared.Next(choices.Length)]);
    }

    public int Previous()
    {
        if (Index < 0) return -1;
        if (Shuffle && _history.TryPop(out var previous))
        {
            Index = previous;
            return Index;
        }
        var target = Index > 0 ? Index - 1 : Repeat == MusicRepeat.All ? Items.Count - 1 : -1;
        if (target >= 0) { Index = target; _visited.Add(target); }
        return target;
    }

    public void Clear() => Replace([], -1);
}

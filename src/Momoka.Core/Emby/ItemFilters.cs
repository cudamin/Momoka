namespace Momoka.Emby;

/// <summary>
/// Which filters are on. The panel edits one of these, <see cref="ItemQuery"/> sends it, and
/// <c>UiSettings.Filters</c> stores one per library.
/// <para>
/// A plain mutable object with <see cref="List{T}"/> properties rather than sets or an immutable record,
/// because this is also the shape written to settings.json: a list round-trips through
/// <c>System.Text.Json</c> with no converter and reads back in the file as the user's own order, and a
/// selection of this size never needs a hash lookup.
/// </para>
/// </summary>
public sealed class ItemFilters
{
    /// <summary>
    /// <see cref="EmbyFilterOption.Id"/> values that are on. Ids rather than query values, so a stored
    /// selection survives a relabelling and 高清/标清 stay distinguishable — they share a key.
    /// </summary>
    public List<string> Toggles { get; set; } = [];

    public List<string> Genres { get; set; } = [];

    /// <summary>
    /// 家长评分 — <c>OfficialRatings</c>, pipe-delimited. A list rather than a single value because the
    /// web client's select is a multi-select and the server takes several ratings at once.
    /// </summary>
    public List<string> OfficialRatings { get; set; } = [];

    public List<string> Studios { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Years as strings. They go into the query as text either way, and a settings file holding
    /// <c>["2019"]</c> is no harder to read than <c>[2019]</c> — while a list of strings means
    /// <see cref="Values"/> can hand every dynamic section the same type.
    /// </summary>
    public List<string> Years { get; set; } = [];

    public Dictionary<string, List<string>> MediaValues { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How many individual choices are on; what the button's badge shows.
    /// <para>
    /// Unknown ids are not counted, for the same reason <see cref="Contributions"/> does not send them:
    /// a badge saying one filter is on, with nothing filtered and nothing to untick, is worse than a
    /// selection written by a later build quietly not applying.
    /// </para>
    /// </summary>
    public int Count =>
        Toggles.Count(id => EmbyFilterBy.Find(id) is not null)
        + Genres.Count + OfficialRatings.Count + Studios.Count + Tags.Count + Years.Count
        + MediaValues.Where(pair => EmbyFilterBy.Lists.Any(list => list.Key == pair.Key)).Sum(pair => pair.Value.Count);

    public bool IsEmpty => Count == 0;

    /// <summary>
    /// True when nothing at all is stored, ids this build does not recognise included. The difference
    /// from <see cref="IsEmpty"/> only shows up against a selection written by a later build: that one
    /// filters nothing and shows nothing, so it is empty, but it is not blank and must not be discarded
    /// the first time an older build touches the panel.
    /// </summary>
    public bool IsBlank => Toggles.Count == 0 && Genres.Count == 0 && OfficialRatings.Count == 0
        && Studios.Count == 0 && Tags.Count == 0 && Years.Count == 0 && MediaValues.Values.All(values => values.Count == 0);

    public bool Has(string id) => Toggles.Contains(id);

    /// <summary>
    /// The list behind one of <see cref="EmbyFilterBy.Lists"/>, so the panel can build all three
    /// sections in a loop instead of naming each one.
    /// </summary>
    public List<string> Values(string key) => key switch
    {
        EmbyFilterBy.Keys.Genres => Genres,
        EmbyFilterBy.Keys.OfficialRatings => OfficialRatings,
        EmbyFilterBy.Keys.Studios => Studios,
        EmbyFilterBy.Keys.Tags => Tags,
        EmbyFilterBy.Keys.Years => Years,
        _ when EmbyFilterBy.MediaListKeys.Contains(key) => MediaList(key),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "没有这个筛选分组")
    };

    private List<string> MediaList(string key)
    {
        if (!MediaValues.TryGetValue(key, out var values)) MediaValues[key] = values = [];
        return values;
    }

    /// <summary>
    /// Turns one option on or off, taking its <see cref="EmbyFilterOption.Excludes"/> with it. Returns
    /// whether anything changed, so the caller can skip a reload it does not need.
    /// </summary>
    public bool Set(EmbyFilterOption option, bool on)
    {
        var changed = false;

        if (on)
        {
            foreach (var excluded in option.Excludes)
                changed |= Toggles.Remove(excluded);

            if (!Toggles.Contains(option.Id))
            {
                Toggles.Add(option.Id);
                changed = true;
            }
        }
        else
        {
            changed = Toggles.Remove(option.Id);
        }

        return changed;
    }

    /// <summary>
    /// Picks one choice out of a select field — 播放状态, 预告片, 已锁定 — and clears every other choice of
    /// that same field first. Returns whether anything changed.
    /// <para>
    /// The clearing is per field and not per pair of options: 可继续播放 does not contradict 已播放 the way
    /// 已播放 contradicts 未播放, but the web client's select holds exactly one of the three, and a
    /// selection holding two of them is a state the panel could never draw.
    /// </para>
    /// </summary>
    public bool Select(EmbyFilterField field, FilterChoice choice)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(choice);

        var changed = false;

        foreach (var other in field.Choices)
        {
            if (other.OptionId is not { } id || id == choice.OptionId) continue;
            changed |= Toggles.Remove(id);
        }

        if (choice.OptionId is not { } pick) return changed;
        if (Toggles.Contains(pick)) return changed;

        Toggles.Add(pick);
        return true;
    }

    /// <summary>
    /// Which choice of a select field is on, for the panel to seed its dropdown with; the 全部 choice
    /// when none is.
    /// </summary>
    public FilterChoice Choice(EmbyFilterField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Choices.FirstOrDefault(choice => choice.OptionId is { } id && Toggles.Contains(id))
            ?? field.Choices.First(choice => choice.OptionId is null);
    }

    /// <summary>Whether the one option a switch field carries is on.</summary>
    public bool IsOn(EmbyFilterField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Choices.Any(choice => choice.OptionId is { } id && Toggles.Contains(id));
    }

    /// <summary>Turns one value of a dynamic section on or off. Returns whether anything changed.</summary>
    public bool SetValue(string key, string value, bool on)
    {
        var values = Values(key);

        if (!on) return values.Remove(value);
        if (values.Contains(value)) return false;

        values.Add(value);
        return true;
    }

    public bool Clear()
    {
        // IsBlank rather than IsEmpty, so an id this build does not recognise is also cleared. Otherwise
        // it would be unreachable: not counted, not shown, never removed.
        if (IsBlank) return false;

        Toggles.Clear();
        Genres.Clear();
        OfficialRatings.Clear();
        Studios.Clear();
        Tags.Clear();
        Years.Clear();
        MediaValues.Clear();
        return true;
    }

    /// <summary>A detached copy, so the page can hold one and settings.json another.</summary>
    public ItemFilters Clone() => new()
    {
        Toggles = [.. Toggles],
        Genres = [.. Genres],
        OfficialRatings = [.. OfficialRatings],
        Studios = [.. Studios],
        Tags = [.. Tags],
        Years = [.. Years],
        MediaValues = MediaValues.ToDictionary(pair => pair.Key, pair => new List<string>(pair.Value), StringComparer.Ordinal)
    };

    /// <summary>
    /// Every (key, value) pair this selection contributes, unjoined —
    /// <see cref="ItemQuery.ToParameters"/> groups them by key and joins each with that key's own
    /// delimiter, because several options share a key and one of those keys wants a pipe.
    /// <para>
    /// Toggles come out in catalogue order rather than the order they were ticked, so the same selection
    /// always produces the same query string. An id the catalogue no longer knows is skipped: settings
    /// written by a later build must not become a filter nobody can see or clear.
    /// </para>
    /// </summary>
    public IEnumerable<(string Key, string Value)> Contributions()
    {
        foreach (var option in EmbyFilterBy.All)
            if (Toggles.Contains(option.Id))
                yield return (option.Key, option.Value);

        if (Has("resolution1080")) yield return ("MaxWidth", "2200");
        if (Has("resolution720")) yield return ("MaxWidth", "1799");

        foreach (var list in EmbyFilterBy.Lists)
            foreach (var value in Values(list.Key))
                if (!string.IsNullOrWhiteSpace(value))
                    yield return (list.Key, value);
    }

    /// <summary>
    /// What is on, in words: 「未播放、收藏 · 类型：动画、科幻」. For the filter button's tooltip, where the
    /// badge only has room for a number. The optional <paramref name="fields"/> lets Jellyfin describe the
    /// selection with its own panel rather than Emby's.
    /// </summary>
    public string Describe(IReadOnlyList<EmbyFilterField>? fields = null)
    {
        var sections = new List<string>(4);
        var panel = fields ?? EmbyFilterBy.Fields;

        var labels = panel
            .SelectMany(field => field.Choices)
            .Where(choice => choice.OptionId is { } id && Toggles.Contains(id))
            .Select(choice => choice.ChipLabel ?? choice.Label)
            .ToList();

        if (labels.Count > 0) sections.Add(string.Join('、', labels));

        foreach (var list in EmbyFilterBy.Lists)
        {
            var values = Values(list.Key);
            if (values.Count > 0) sections.Add($"{list.Label}：{string.Join('、', values)}");
        }

        return string.Join("  ·  ", sections);
    }
}

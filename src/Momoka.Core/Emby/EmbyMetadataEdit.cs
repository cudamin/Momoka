using System.Globalization;
using System.Text.Json.Nodes;

namespace Momoka.Emby;

/// <summary>
/// Emby's full editor, separate from the existing Jellyfin/basic form. Lists retain their JSON-backed
/// identities and unknown properties; saving a title must not reconstruct unrelated server metadata.
/// </summary>
public sealed record EmbyMetadataEdit
{
    public static readonly IReadOnlyList<string> VideoFormats =
        ["HalfSideBySide", "FullSideBySide", "FullTopAndBottom", "HalfTopAndBottom", "MVC"];
    public static readonly IReadOnlyList<string> PersonTypes =
        ["Actor", "Director", "Writer", "Producer", "GuestStar", "Composer", "Conductor", "Lyricist"];

    public ItemMetadataEdit Basic { get; init; } = new();
    private string? InitialAddedText { get; init; }
    public string Path { get; init; } = "";
    public string DateCreated { get; init; } = "";
    public string CustomRating { get; init; } = "";
    public string Video3DFormat { get; init; } = "";
    public string PreferredMetadataLanguage { get; init; } = "";
    public string PreferredMetadataCountryCode { get; init; } = "";
    public bool LockData { get; init; }
    public IReadOnlyList<string> LockedFields { get; init; } = [];
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<EmbyMetadataStudio> Studios { get; init; } = [];
    public IReadOnlyList<EmbyMetadataPerson> People { get; init; } = [];
    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = new Dictionary<string, string>();

    public static EmbyMetadataEdit Read(JsonObject item) => new()
    {
        Basic = ItemMetadataEdit.Read(item),
        Path = Text(item, "Path"),
        DateCreated = AddedText(item),
        InitialAddedText = AddedText(item),
        CustomRating = Text(item, "CustomRating"),
        Video3DFormat = Text(item, "Video3DFormat"),
        PreferredMetadataLanguage = Text(item, "PreferredMetadataLanguage"),
        PreferredMetadataCountryCode = Text(item, "PreferredMetadataCountryCode"),
        LockData = item["LockData"] is JsonValue value && value.TryGetValue<bool>(out var locked) && locked,
        LockedFields = Names(item, "LockedFields"),
        Genres = Names(item, "Genres"),
        Tags = Names(item, "Tags"),
        People = Objects(item, "People").Select(EmbyMetadataPerson.Read).ToArray(),
        Studios = Objects(item, "Studios").Select(EmbyMetadataStudio.Read).ToArray(),
        ProviderIds = item["ProviderIds"] is JsonObject ids
            ? ids.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    public string? Problem => Basic.Problem
        ?? (!string.IsNullOrWhiteSpace(DateCreated) && ParseAdded(DateCreated) is null
            ? "加入日期要填成 2026-10-28 17:33:45 这样的日期和时间" : null)
        ?? (Genres.Concat(Tags).Any(string.IsNullOrWhiteSpace) ? "类型和标签不能为空" : null)
        ?? (Studios.Any(studio => string.IsNullOrWhiteSpace(studio.Name)) ? "工作室名称不能为空" : null)
        ?? (People.Any(person => string.IsNullOrWhiteSpace(person.Name)) ? "人物名称不能为空" : null);

    public bool ApplyTo(JsonObject item)
    {
        if (Problem is { } problem) throw new InvalidOperationException(problem);
        var before = (JsonObject)item.DeepClone();
        ApplyBasic(item, before);
        SetText(item, "CustomRating", CustomRating);
        SetText(item, "Video3DFormat", Video3DFormat);
        SetText(item, "PreferredMetadataLanguage", PreferredMetadataLanguage);
        SetText(item, "PreferredMetadataCountryCode", PreferredMetadataCountryCode);
        if (LockData || before["LockData"] is not null) item["LockData"] = LockData;
        SetAdded(item, before);
        // Override the basic form's delimiter-based lists: web list entries can themselves contain commas.
        SetNames(item, before, "Genres", Genres);
        SetNames(item, before, "Tags", Tags);
        SetNames(item, before, "LockedFields", LockedFields);
        SetArray(item, before, "Studios", Studios.Select(studio => (JsonNode)studio.ToJson()));
        SetArray(item, before, "People", People.Select(person => (JsonNode)person.ToJson()));
        SetProviders(item, before);
        return !JsonNode.DeepEquals(before, item);
    }

    private void ApplyBasic(JsonObject item, JsonObject before)
    {
        var original = ItemMetadataEdit.Read(before);
        Basic.ApplyTo(item);
        // Empty/unusual server values in untouched fields are not the user's request to clear them.
        foreach (var (key, initial, edited) in new (string, string, string)[]
        {
            ("Name", original.Name, Basic.Name), ("OriginalTitle", original.OriginalTitle, Basic.OriginalTitle),
            ("ForcedSortName", original.SortName, Basic.SortName), ("Overview", original.Overview, Basic.Overview),
            ("OfficialRating", original.OfficialRating, Basic.OfficialRating),
            ("CommunityRating", original.CommunityRating, Basic.CommunityRating), ("CriticRating", original.CriticRating, Basic.CriticRating),
            ("ProductionYear", original.ProductionYear, Basic.ProductionYear), ("PremiereDate", original.PremiereDate, Basic.PremiereDate),
            ("IndexNumber", original.IndexNumber, Basic.IndexNumber), ("ParentIndexNumber", original.ParentIndexNumber, Basic.ParentIndexNumber)
        })
        {
            if (initial != edited) continue;
            if (before.TryGetPropertyValue(key, out var value)) item[key] = value?.DeepClone();
            else item.Remove(key);
        }
    }

    private void SetAdded(JsonObject item, JsonObject before)
    {
        var input = DateCreated.Trim();
        if (input == InitialAddedText) return;
        var existing = ParseMoment(Text(before, "DateCreated"));
        // Preserve sub-second precision and offset when the displayed local second was not edited.
        if (existing is { } original
            && original.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) == input) return;
        if (input.Length == 0)
        {
            if (before["DateCreated"] is not null) item["DateCreated"] = null;
            return;
        }
        item["DateCreated"] = ParseAdded(input)!.Value.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
    }

    private void SetProviders(JsonObject item, JsonObject before)
    {
        var ids = before["ProviderIds"] is JsonObject original ? (JsonObject)original.DeepClone() : new JsonObject();
        foreach (var pair in ProviderIds)
        {
            var key = ids.Select(entry => entry.Key).FirstOrDefault(key => key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                ?? pair.Key;
            if (ids[key]?.ToString() == pair.Value) continue;
            var value = pair.Value.Trim();
            if (Text(ids, key) == value) continue;
            if (value.Length == 0) ids.Remove(key);
            else ids[key] = value;
        }
        foreach (var key in ids.Select(entry => entry.Key).ToArray())
            if (!ProviderIds.Keys.Contains(key, StringComparer.OrdinalIgnoreCase)) ids.Remove(key);
        if (ids.Count > 0 || before["ProviderIds"] is not null) item["ProviderIds"] = ids;
    }

    private static void SetNames(JsonObject item, JsonObject before, string key, IReadOnlyList<string> values)
    {
        if (Names(before, key).SequenceEqual(values))
        {
            if (before.TryGetPropertyValue(key, out var original)) item[key] = original?.DeepClone();
            else item.Remove(key);
            return;
        }
        SetArray(item, before, key, values.Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode)JsonValue.Create(value)!));
    }

    private static void SetArray(JsonObject item, JsonObject before, string key, IEnumerable<JsonNode> values)
    {
        var array = new JsonArray(values.ToArray());
        if (array.Count == 0 && before[key] is null) return;
        item[key] = array;
    }

    internal static void SetText(JsonObject item, string key, string text)
    {
        var value = text.Trim();
        if (Text(item, key) == value) return;
        item[key] = value.Length == 0 ? null : JsonValue.Create(value);
    }

    internal static string Text(JsonObject item, string key) =>
        item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static string[] Names(JsonObject item, string key) => item[key] is JsonArray array
        ? array.OfType<JsonValue>().Select(value => value.ToString()).ToArray() : [];
    private static IEnumerable<JsonObject> Objects(JsonObject item, string key) =>
        item[key] is JsonArray array ? array.OfType<JsonObject>() : [];
    private static string AddedText(JsonObject item) => ParseMoment(Text(item, "DateCreated")) is { } added
        ? added.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "";
    private static DateTimeOffset? ParseMoment(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : null;
    private static DateTimeOffset? ParseAdded(string text) =>
        DateTimeOffset.TryParseExact(text.Trim(), ["yyyy-MM-dd HH:mm:ss", "yyyy/M/d HH:mm:ss", "yyyy-MM-dd HH:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value) ? value : null;
}

/// <summary>A credit retains its complete source object across role edits and reordering.</summary>
public sealed record EmbyMetadataPerson
{
    private JsonObject? Original { get; init; }
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public string Type { get; init; } = "Actor";
    public string Description => Role.Length > 0 ? Role : Type;

    internal static EmbyMetadataPerson Read(JsonObject value) => new()
    {
        Original = (JsonObject)value.DeepClone(), Name = EmbyMetadataEdit.Text(value, "Name"),
        Role = EmbyMetadataEdit.Text(value, "Role"), Type = EmbyMetadataEdit.Text(value, "Type")
    };

    internal JsonObject ToJson()
    {
        var value = Original is null ? new JsonObject() : (JsonObject)Original.DeepClone();
        if (Original is not null && EmbyMetadataEdit.Text(Original, "Name").Trim() != Name.Trim())
        {
            // A different name must not keep the previous person's identity/photo.
            value.Remove("Id");
            value.Remove("PrimaryImageTag");
        }
        if (Original is null || EmbyMetadataEdit.Text(Original, "Name") != Name) EmbyMetadataEdit.SetText(value, "Name", Name);
        if (Original is null || EmbyMetadataEdit.Text(Original, "Role") != Role) EmbyMetadataEdit.SetText(value, "Role", Role);
        if (Original is null || EmbyMetadataEdit.Text(Original, "Type") != Type) EmbyMetadataEdit.SetText(value, "Type", Type);
        return value;
    }
}

public sealed record EmbyMetadataStudio
{
    private JsonObject? Original { get; init; }
    public string Name { get; init; } = "";
    internal static EmbyMetadataStudio Read(JsonObject value) => new()
        { Original = (JsonObject)value.DeepClone(), Name = EmbyMetadataEdit.Text(value, "Name") };
    internal JsonObject ToJson()
    {
        var value = Original is null ? new JsonObject() : (JsonObject)Original.DeepClone();
        if (Original is not null && EmbyMetadataEdit.Text(Original, "Name").Trim() != Name.Trim()) value.Remove("Id");
        if (Original is null || EmbyMetadataEdit.Text(Original, "Name") != Name) EmbyMetadataEdit.SetText(value, "Name", Name);
        return value;
    }
}

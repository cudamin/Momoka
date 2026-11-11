using System.Globalization;

namespace Momoka.Plex;

/// <summary>A local PMS tag identity, scoped to the library and media type that supplied it.</summary>
public sealed record PlexBrowseRelation(string SectionId, int ItemType, string Field, string Value)
{
    public bool IsValid => IsNativeId(SectionId) && ItemType is 1 or 2
        && Field is "actor" or "director" or "writer" or "genre" && IsNativeId(Value);

    /// <summary>Card identity only; native requests use the separate fields rather than parsing this value.</summary>
    public string Identity => "plex-relation:" + SectionId + ":" + ItemType.ToString(CultureInfo.InvariantCulture)
        + ":" + Field + ":" + Value;

    public void Validate(string? sectionId)
    {
        if (!IsValid || !string.Equals(SectionId, sectionId, StringComparison.Ordinal))
            throw new NotSupportedException("Plex 关联条件缺少有效的来源库或服务器身份");
    }

    public static bool IsNativeId(string? value) => value is { Length: > 0 }
        && value.All(character => character is >= '0' and <= '9') && value.Any(character => character != '0');
}

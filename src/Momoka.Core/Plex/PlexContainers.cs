using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.Plex;

public enum PlexContainerKind { Playlist, Collection }

/// <summary>Only local library-provider features are used; unknown account requirements stay unavailable.</summary>
public sealed record PlexListCapabilities(bool CanCreatePlaylists, bool CanManageCollections, string? VerificationError = null)
{
    public static PlexListCapabilities Parse(JsonObject root, bool authenticatedAccount = false)
    {
        var provider = PlexMediaAdapter.Elements(root, "MediaProvider")
            .FirstOrDefault(item => PlexMediaAdapter.Text(item, "identifier") == PlexClient.LibraryIdentifier);
        if (provider is null) return new(false, false);
        bool Feature(string type, string? path = null) => PlexMediaAdapter.Elements(provider, "Feature").Any(item =>
            PlexMediaAdapter.Text(item, "type") == type && Requirement(item)
            && (path is null || PlexMediaAdapter.Text(item, "key").TrimEnd('/') == path));
        bool Requirement(JsonObject item) => PlexMediaAdapter.Text(item, "requires") is ""
            || PlexMediaAdapter.Text(item, "requires") == "nonAnonymousAccount" && authenticatedAccount;
        return new(Feature("playlist", "/playlists"), Feature("collection", "/library/collections") && Feature("manage"));
    }

    internal static bool NeedsAccountProfile(JsonObject root) => PlexMediaAdapter.Elements(root, "MediaProvider")
        .Where(item => PlexMediaAdapter.Text(item, "identifier") == PlexClient.LibraryIdentifier)
        .SelectMany(item => PlexMediaAdapter.Elements(item, "Feature"))
        .Any(item => PlexMediaAdapter.Text(item, "type") is "playlist" or "collection" or "manage"
            && PlexMediaAdapter.Text(item, "requires") == "nonAnonymousAccount");
}

public sealed record PlexContainer(EmbyItem Item, PlexContainerKind Kind, string? SectionId, string? ItemType,
    bool CanEdit, bool CanOrder)
{
    public string Id => Item.Id;
}

/// <summary>Consumed counts raw server entries, including omitted smart or unsupported containers.</summary>
public sealed record PlexContainerPage(IReadOnlyList<PlexContainer> Items, int TotalRecordCount, int Consumed, bool HasMore)
{
    public int? DeclaredTotal { get; init; }
    public string PageIdentity { get; init; } = "";
}
public sealed record PlexContainerMember(EmbyItem Item, string? EntryId);
public sealed record PlexContainerMembers(IReadOnlyList<PlexContainerMember> Items, int TotalRecordCount, bool HasMore)
{
    public int? DeclaredTotal { get; init; }
}

/// <summary>Accepted with no container means the write succeeded but its optional identity could not be read.</summary>
public sealed record PlexContainerCreationResult(PlexContainer? Container);

/// <summary>A request was sent without a definitive HTTP result; do not replay it automatically.</summary>
public sealed class PlexWriteUncertainException(string message) : Exception(message);

internal static class PlexContainerIdentity
{
    internal static string Id(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.All(char.IsAsciiDigit) || value.All(c => c == '0'))
            throw new ArgumentException("Plex 清单或成员标识无效", nameof(value));
        return value;
    }

    internal static bool IsId(string? value) => value is { Length: > 0 }
        && value.All(char.IsAsciiDigit) && value.Any(c => c != '0');

    internal static int MediaType(string? value) => value switch
    {
        EmbyItemType.Movie or "movie" => 1,
        EmbyItemType.Series or "show" => 2,
        _ => throw new NotSupportedException("Plex 合集首轮支持电影或电视剧")
    };

    internal static bool? Boolean(JsonObject value, string key) => PlexMediaAdapter.Text(value, key) switch
    { "1" or "true" => true, "0" or "false" => false, _ => null };

    internal static int? Count(JsonObject value, string key) => int.TryParse(PlexMediaAdapter.Text(value, key),
        NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;

    internal static PlexContainer Map(JsonObject value, PlexContainerKind kind, PlexListCapabilities permissions,
        string? sectionId = null, string? itemType = null)
    {
        var item = PlexMediaAdapter.Item(value);
        Id(item.Id);
        var expected = kind == PlexContainerKind.Playlist ? EmbyItemType.Playlist : EmbyItemType.BoxSet;
        if (item.Type != expected) throw new InvalidDataException("Plex 返回的清单类型与请求不一致");
        var smart = Boolean(value, "smart");
        var section = PlexMediaAdapter.Text(value, "librarySectionID");
        if (section.Length == 0) section = sectionId ?? "";
        var subtype = PlexMediaAdapter.Text(value, "subtype");
        var type = subtype switch { "movie" => EmbyItemType.Movie, "show" => EmbyItemType.Series, _ => itemType };
        if (kind == PlexContainerKind.Collection && (sectionId is not null && section != sectionId
            || itemType is not null && type != itemType))
            throw new InvalidDataException("Plex 合集与请求的媒体库或类型不一致");
        var ordinary = smart == false;
        var editable = ordinary && (kind == PlexContainerKind.Playlist
            ? permissions.CanCreatePlaylists && item.PlexPlaylistType == "video" && Boolean(value, "readOnly") == false
            : permissions.CanManageCollections && IsId(section) && type is EmbyItemType.Movie or EmbyItemType.Series);
        return new(item, kind, section.Length == 0 ? null : section, type, editable,
            editable && kind == PlexContainerKind.Playlist);
    }
}

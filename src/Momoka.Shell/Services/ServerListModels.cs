using Momoka.Emby;

namespace Momoka.Shell.Services;

public enum ServerListKind { Playlist, Collection }

/// <summary>One account's persistent server list; never the current player's transient queue.</summary>
public sealed record ServerList(EmbyItem Item, ServerListKind Kind, bool CanEdit, bool CanOrder,
    string? SectionId = null, string? ItemType = null)
{
    public string Id => Item.Id;
    public string Name => Item.Name;
}

public sealed record ServerListMember(EmbyItem Item, string EntryId)
{
    public string Name => Item.Name;
    public string Description => Item.Type == EmbyItemType.Episode
        ? $"{Item.SeriesName} · 第 {Item.ParentIndexNumber} 季 第 {Item.IndexNumber} 集" : Item.DisplayTypeName;
}

public sealed record ServerListAccount(EmbySessionScope Scope)
{
    public string Name => Scope.DisplayName;
}

public sealed record ServerListLibrary(EmbyItem Item)
{
    public string Id => Item.Id;
    public string Name => Item.Name;
}

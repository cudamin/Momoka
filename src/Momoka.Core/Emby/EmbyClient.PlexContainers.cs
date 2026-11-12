using Momoka.Plex;

namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public Task<PlexListCapabilities> GetPlexListCapabilitiesAsync(CancellationToken cancellationToken) =>
        Plex.GetListCapabilitiesAsync(PlexConnection, cancellationToken);

    public Task<PlexContainerPage> GetPlexVideoPlaylistsAsync(int start, int limit, CancellationToken cancellationToken) =>
        Plex.GetVideoPlaylistsAsync(PlexConnection, start, limit, cancellationToken);

    public Task<PlexContainerPage> GetPlexCollectionsAsync(string sectionId, string itemType, int start, int limit,
        CancellationToken cancellationToken) => Plex.GetCollectionsAsync(PlexConnection, sectionId, itemType, start, limit, cancellationToken);

    public Task<PlexContainer> GetPlexContainerAsync(PlexContainerKind kind, string id, CancellationToken cancellationToken) =>
        Plex.GetContainerAsync(PlexConnection, kind, id, cancellationToken);

    public Task<PlexContainerMembers> GetPlexContainerMembersAsync(PlexContainer container, int start, int limit,
        CancellationToken cancellationToken) => Plex.GetContainerMembersAsync(PlexConnection, container, start, limit, cancellationToken);

    public Task<PlexContainerCreationResult> CreatePlexContainerAsync(PlexContainerKind kind, string name, string? sectionId,
        string? itemType, IReadOnlyList<string> ids, CancellationToken cancellationToken, Action? confirmed = null,
        Action? beforeWrite = null) => Plex.CreateContainerAsync(PlexConnection, kind, name, sectionId, itemType, ids,
            cancellationToken, confirmed, beforeWrite);

    public Task AddToPlexContainerAsync(PlexContainer container, IReadOnlyList<string> ids, CancellationToken cancellationToken,
        Action? confirmed = null, Action? beforeWrite = null) =>
        Plex.AddToContainerAsync(PlexConnection, container, ids, cancellationToken, confirmed, beforeWrite);

    public Task RemoveFromPlexContainerAsync(PlexContainer container, string entryId, CancellationToken cancellationToken,
        Action? confirmed = null, Action? beforeWrite = null) =>
        Plex.RemoveFromContainerAsync(PlexConnection, container, entryId, cancellationToken, confirmed, beforeWrite);

    public Task MovePlexContainerMemberAsync(PlexContainer container, string entryId, string? afterEntryId,
        CancellationToken cancellationToken, Action? confirmed = null, Action? beforeWrite = null) =>
        Plex.MoveContainerMemberAsync(PlexConnection, container, entryId, afterEntryId, cancellationToken, confirmed, beforeWrite);
}

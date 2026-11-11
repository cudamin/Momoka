namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    /// <summary>Artists are a named index, not descendants; artist songs use an association filter.</summary>
    private Task<ItemsResult>? MusicItems(ItemQuery query, CancellationToken cancellationToken)
    {
        if (query.IncludeItemTypes.SequenceEqual([EmbyItemType.MusicArtist]))
        {
            // Jellyfin ArtistsController.GetArtists; Emby's JS client getArtists uses the same index.
            var parameters = query.ToParameters().Where(pair => pair.Key != "IncludeItemTypes").ToList();
            parameters.Add(("UserId", Connection.UserId));
            return http.GetJsonAsync<ItemsResult>(Url("Artists", [.. parameters]), Context, cancellationToken);
        }
        if (query.ParentItemType == EmbyItemType.MusicArtist)
        {
            var parameters = query.ToParameters().Where(pair => pair.Key != "ParentId").ToArray();
            return http.GetJsonAsync<ItemsResult>(Url($"Users/{Connection.UserId}/Items", parameters), Context, cancellationToken);
        }
        if (query.ParentItemType == EmbyItemType.Playlist && query.IncludeItemTypes.SequenceEqual([EmbyItemType.Audio]))
        {
            var parameters = query.ToParameters().Where(pair => pair.Key is not ("ParentId" or "Recursive" or "SortBy" or "SortOrder")).ToList();
            parameters.Add(("UserId", Connection.UserId));
            return http.GetJsonAsync<ItemsResult>(Url($"Playlists/{Uri.EscapeDataString(query.ParentId!)}/Items", [.. parameters]), Context, cancellationToken);
        }
        return null;
    }
}

namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public Task<ItemsResult> GetLibraryItemsAsync(string collectionType, LibrarySection section, string? group,
        ItemQuery query, CancellationToken cancellationToken)
    {
        // Jellyfin's 我的最爱 tab is no longer one of its library tabs (its web client has no such tab on
        // a series library), but the per-library favorites query itself is still a valid route and is
        // used by the old favorites-page tests and any saved navigation. Keep it callable.
        var supported = LibraryBrowse.Sections(collectionType, Connection.ServerKind).Any(entry => entry.Section == section)
            || Connection.IsJellyfin && section == LibrarySection.Favorites;
        if (!supported) throw new NotSupportedException("当前服务器不支持此媒体库分类");
        if (Connection.IsFnNas || Connection.IsPlex) return GetItemsAsync(query, cancellationToken);
        var path = LibraryBrowse.Path(section, group, Connection.UserId);
        if (section == LibrarySection.Upcoming)
            return http.GetJsonAsync<ItemsResult>(Url(path, LibraryUpcoming.Parameters(query, Connection.UserId, Connection.ServerKind)),
                Context, cancellationToken);
        var parameters = query.ToParameters().ToList();
        if (path is "Genres" or "Studios" or "Trailers") parameters.Add(("UserId", Connection.UserId));
        return http.GetJsonAsync<ItemsResult>(Url(path, [.. parameters]), Context, cancellationToken);
    }
}

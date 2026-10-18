namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public Task<ItemsResult> GetLibraryItemsAsync(string collectionType, LibrarySection section, string? group,
        ItemQuery query, CancellationToken cancellationToken)
    {
        if (!LibraryBrowse.Sections(collectionType, Connection.ServerKind).Any(entry => entry.Section == section))
            throw new NotSupportedException("当前服务器不支持此媒体库分类");
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

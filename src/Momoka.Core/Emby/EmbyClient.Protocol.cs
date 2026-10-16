namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    private Uri Url(string path, params (string Key, string? Value)[] query)
    {
        if (Connection.IsFnNas) throw new NotSupportedException("飞牛影视不支持此 Emby 操作");
        if (!Connection.IsJellyfin) return EmbyUrl.Combine(ApiBase, path, query);
        if (path is "Items/Metadata/Reset" or "Images/Remote" or "Items/RemoteSearch/Image" || path.EndsWith("/HideFromResume", StringComparison.Ordinal)
            || query.Any(pair => pair.Key.EndsWith("RefreshMode", StringComparison.OrdinalIgnoreCase)
                && pair.Value == "ValidationOnly")) RequireEmby("此操作");
        var parameters = query.Select(pair => pair.Key.Equals("Fields", StringComparison.OrdinalIgnoreCase)
            ? (pair.Key, pair.Value is null ? null : JellyfinQuery.FilterFields(pair.Value))
            : pair.Key.Equals("SortBy", StringComparison.OrdinalIgnoreCase)
                ? (pair.Key, pair.Value is null ? null : JellyfinQuery.FilterSort(pair.Value)) : pair).ToList();
        parameters.RemoveAll(pair => pair.Key.Equals("ReplaceThumbnailImages", StringComparison.OrdinalIgnoreCase));
        var prefix = $"Users/{Connection.UserId}/";
        if (Connection.UsesJellyfin12Routes && path.StartsWith(prefix, StringComparison.Ordinal))
        {
            var relative = path[prefix.Length..];
            path = relative switch
            {
                "Views" => "UserViews",
                "Items/Resume" => "UserItems/Resume",
                "Items/Latest" => "Items/Latest",
                _ when relative.StartsWith("PlayedItems/", StringComparison.Ordinal) => "User" + relative,
                _ when relative.StartsWith("FavoriteItems/", StringComparison.Ordinal) => "User" + relative,
                _ => relative
            };
            parameters.Add(("UserId", Connection.UserId));
            if (relative == "Views" || relative.StartsWith("Items/", StringComparison.Ordinal)
                && relative is not "Items/Resume" and not "Items/Latest")
                parameters.RemoveAll(pair => pair.Key.Equals("Fields", StringComparison.OrdinalIgnoreCase));
        }
        return EmbyUrl.Combine(ApiBase, path, [.. parameters]);
    }

    private void RequireEmby(string feature)
    {
        if (Connection.ServerKind != MediaServerKind.Emby) throw new NotSupportedException($"当前服务器不支持{feature}");
    }

    private Uri EmbyOnlyUrl(string path, params (string Key, string? Value)[] query)
    {
        RequireEmby("通知配置");
        return Url(path, query);
    }

    public Task<EmbyUser> GetCurrentUserAsync(CancellationToken cancellationToken) =>
        Connection.IsFnNas ? FnNasMedia.GetCurrentUserAsync(cancellationToken)
        : http.GetJsonAsync<EmbyUser>(Url(Connection.IsJellyfin ? "Users/Me" : $"Users/{Connection.UserId}"), Context, cancellationToken);
}

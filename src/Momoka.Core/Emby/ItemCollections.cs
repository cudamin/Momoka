namespace Momoka.Emby;

/// <summary>合集权限只决定成员关系，不授予删除媒体或服务器管理能力。</summary>
public static class ItemCollections
{
    public static bool CanManage(MediaServerKind kind, ItemMenuPermissions permissions, EmbyItem? item = null) => kind switch
    {
        MediaServerKind.Emby => ItemPermission(item) ?? permissions.CanManageCollections ?? (permissions.IsAdministrator != false),
        MediaServerKind.Jellyfin => permissions.IsAdministrator == true
            || (permissions.CanManageCollections ?? (permissions.IsAdministrator != false)),
        _ => false
    };

    private static bool? ItemPermission(EmbyItem? item)
    {
        if (item?.CanEditItems != true) return item?.CanEditItems;
        // Fresh per-item grants may allow a nonadministrator. After the account snapshot changes,
        // this old grant cannot override a current denial; refresh the item to obtain a new grant.
        return item.Source is null || item.Source.IsAvailable && item.PermissionReadRevision == item.Source.PermissionRevision
            ? true : null;
    }
}

/// <summary>当前页面的一条合集关系；页面取消、来源退出或换成员后不能继续使用。</summary>
public sealed record CollectionMembership(EmbySessionScope Source, string CollectionId, string ItemId, CancellationToken PageLifetime)
{
    public bool Matches(EmbyItem item) => Source.IsAvailable && !PageLifetime.IsCancellationRequested
        && item.Source is { } owner && ReferenceEquals(owner.UserStateOwner, Source.UserStateOwner)
        && item.Id == ItemId && !string.IsNullOrWhiteSpace(CollectionId) && CollectionId != ItemId;
}

/// <summary>合集写入使用捕获的来源；每次认证重试前重新确认权限和页面关系。</summary>
public sealed class CollectionManagement(EmbySessionScope source, Func<ItemMenuPermissions> permissions)
{
    public Task AddAsync(string collectionId, EmbyItem item, CancellationToken cancellationToken) =>
        source.ExecuteAsync((client, token) =>
        {
            Check(item, client);
            return client.AddToCollectionAsync(collectionId, [item.Id], token);
        }, cancellationToken);

    public Task<CollectionCreationResult> CreateAsync(string name, EmbyItem item, CancellationToken cancellationToken) =>
        source.ExecuteAsync((client, token) =>
        {
            Check(item, client);
            return client.CreateCollectionAsync(name, [item.Id], token);
        }, cancellationToken);

    public async Task RemoveAsync(CollectionMembership membership, EmbyItem item, CancellationToken cancellationToken)
    {
        if (membership.PageLifetime.IsCancellationRequested) throw new OperationCanceledException(membership.PageLifetime);
        if (!membership.Source.IsAvailable) throw new OperationCanceledException("合集所属的账号已退出");
        if (!membership.Matches(item) || !ReferenceEquals(membership.Source.UserStateOwner, source.UserStateOwner))
            throw new InvalidOperationException("合集关系不属于当前媒体和账号，请重新打开合集");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, membership.PageLifetime);
        await source.ExecuteAsync((client, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (!membership.Matches(item)) throw new OperationCanceledException("当前合集页面已改变");
            Check(item, client);
            return client.RemoveFromCollectionAsync(membership.CollectionId, [membership.ItemId], token);
        }, lifetime.Token).ConfigureAwait(false);
    }

    private void Check(EmbyItem item, EmbyClient client)
    {
        source.ThrowIfUnavailable();
        if (item.Source is not { } owner || !ReferenceEquals(owner.UserStateOwner, source.UserStateOwner))
            throw new InvalidOperationException("媒体属于另一个服务器账号");
        if (!client.Connection.IsSameIdentityAs(source.Connection))
            throw new InvalidOperationException("合集操作所属的服务器账号已改变");
        if (!ItemCollections.CanManage(client.Connection.ServerKind, permissions(), item))
            throw new UnauthorizedAccessException("当前账号没有合集管理权限");
    }
}

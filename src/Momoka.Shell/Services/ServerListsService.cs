using Momoka.Emby;
using Momoka.Plex;
using System.Runtime.CompilerServices;

namespace Momoka.Shell.Services;

public sealed record ServerListSearchPage(IReadOnlyList<EmbyItem> Items, int Total, int Consumed);

/// <summary>Adapts each server's own persistent-list protocol, retaining account ownership and write receipts.</summary>
public sealed class ServerListsService
{
    private sealed record CapabilitySnapshot(long Revision, PlexListCapabilities Permissions);
    private readonly ConditionalWeakTable<object, CapabilitySnapshot> _plexCapabilities = new();
    internal string? PermissionNotice(EmbySessionScope scope) => _plexCapabilities.TryGetValue(scope.UserStateOwner, out var value)
        && value.Revision == scope.PermissionRevision ? value.Permissions.VerificationError : null;
    internal static bool Supports(EmbySessionScope scope) => scope.IsAvailable
        && scope.Connection.ServerKind is MediaServerKind.Emby or MediaServerKind.Jellyfin or MediaServerKind.Plex;

    internal bool CanCreate(EmbySessionScope scope, ServerListKind kind, EmbyItem? library)
    {
        if (!Supports(scope)) return false;
        if (!scope.Connection.IsPlex) return kind == ServerListKind.Playlist;
        return _plexCapabilities.TryGetValue(scope.UserStateOwner, out var snapshot) && snapshot.Revision == scope.PermissionRevision
            && (kind == ServerListKind.Playlist ? snapshot.Permissions.CanCreatePlaylists : snapshot.Permissions.CanManageCollections
                && library?.CollectionType is "movies" or "tvshows");
    }

    internal async Task<IReadOnlyList<EmbyItem>> ReadLibrariesAsync(EmbySessionScope scope, CancellationToken token)
    {
        Require(scope);
        var views = await scope.ExecuteAsync((client, ct) => client.GetViewsAsync(ct), token);
        return views.Where(item => item.CollectionType is "movies" or "tvshows").Select(scope.Bind).ToArray();
    }

    internal async Task<IReadOnlyList<ServerList>> ReadDirectoryAsync(EmbySessionScope scope, ServerListKind kind,
        EmbyItem? library, CancellationToken token)
    {
        Require(scope);
        if (!scope.Connection.IsPlex)
        {
            if (kind != ServerListKind.Playlist) throw new NotSupportedException("此服务器的合集请从影视媒体库打开");
            var lists = await scope.ExecuteAsync((client, ct) => client.GetServerPlaylistsAsync(ct), token);
            return lists.Where(item => item.MediaType != "Audio").Select(item =>
                new ServerList(scope.Bind(item), kind, item.MediaType == "Video" && item.CanEditItems == true,
                    item.MediaType == "Video" && item.CanEditItems == true)).ToArray();
        }
        await RefreshPlexCapabilitiesAsync(scope, token);
        if (kind == ServerListKind.Collection && library is null) return [];
        var output = new List<ServerList>();
        var start = 0;
        int? declaredTotal = null;
        var seenPages = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var page = await scope.ExecuteAsync((client, ct) => kind == ServerListKind.Playlist
                ? client.GetPlexVideoPlaylistsAsync(start, 100, ct)
                : client.GetPlexCollectionsAsync(library!.Id, TypeFor(library), start, 100, ct), token);
            if (declaredTotal is { } previous && page.DeclaredTotal != previous)
                throw new InvalidDataException("Plex 清单目录在读取中发生变化，请刷新重试");
            declaredTotal = page.DeclaredTotal;
            if (page.Consumed > 0 && !seenPages.Add(page.PageIdentity))
                throw new InvalidDataException("Plex 重复返回清单目录页，请刷新重试");
            foreach (var list in page.Items) output.Add(Map(scope, list));
            start += page.Consumed;
            if (!page.HasMore) break;
            if (page.Consumed == 0) throw new InvalidDataException("服务器清单分页提前结束，请刷新重试");
        }
        await RefreshPlexCapabilitiesAsync(scope, token);
        return output.Select(list => RestrictToCurrentCapabilities(scope, list)).ToArray();
    }

    internal async Task<ServerList> ReadListAsync(EmbySessionScope scope, ServerList list, CancellationToken token)
    {
        RequireList(scope, list);
        if (scope.Connection.IsPlex)
        {
            var result = await scope.ExecuteAsync((client, ct) => client.GetPlexContainerAsync(PlexKind(list.Kind), list.Id, ct), token);
            await RefreshPlexCapabilitiesAsync(scope, token);
            return RestrictToCurrentCapabilities(scope, Map(scope, result));
        }
        var canEdit = await scope.ExecuteAsync((client, ct) => client.GetPlaylistEditPermissionAsync(list.Id, ct), token);
        return list with { CanEdit = canEdit, CanOrder = canEdit };
    }

    private async Task RefreshPlexCapabilitiesAsync(EmbySessionScope scope, CancellationToken token)
    {
        var revision = scope.PermissionRevision;
        var permissions = await scope.ExecuteAsync((client, ct) => client.GetPlexListCapabilitiesAsync(ct), token);
        if (scope.PermissionRevision != revision) throw new OperationCanceledException("账号权限已更新，请刷新后重试");
        _plexCapabilities.AddOrUpdate(scope.UserStateOwner, new(revision, permissions));
    }

    private ServerList RestrictToCurrentCapabilities(EmbySessionScope scope, ServerList list)
    {
        var allowed = _plexCapabilities.TryGetValue(scope.UserStateOwner, out var snapshot)
            && snapshot.Revision == scope.PermissionRevision
            && (list.Kind == ServerListKind.Playlist ? snapshot.Permissions.CanCreatePlaylists : snapshot.Permissions.CanManageCollections);
        return list with { CanEdit = list.CanEdit && allowed, CanOrder = list.CanOrder && allowed };
    }

    internal async Task<IReadOnlyList<ServerListMember>> ReadMembersAsync(EmbySessionScope scope, ServerList list, CancellationToken token)
    {
        RequireList(scope, list);
        var output = new List<ServerListMember>();
        var start = 0;
        int? expectedTotal = null;
        var seenPages = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            int consumed;
            int total;
            int? declaredTotal;
            bool more;
            if (scope.Connection.IsPlex)
            {
                var page = await scope.ExecuteAsync((client, ct) => client.GetPlexContainerMembersAsync(Unmap(list), start, 100, ct), token);
                foreach (var member in page.Items) output.Add(new(scope.Bind(member.Item), member.EntryId ?? ""));
                consumed = page.Items.Count;
                total = page.TotalRecordCount;
                declaredTotal = page.DeclaredTotal;
                more = page.HasMore;
            }
            else
            {
                var page = await scope.ExecuteAsync((client, ct) => client.GetPlaylistItemsAsync(list.Id, start, 100, ct), token);
                foreach (var item in page.Items) output.Add(new(scope.Bind(item), item.PlaylistItemId ?? ""));
                consumed = page.Items.Count;
                total = page.TotalRecordCount;
                declaredTotal = total;
                more = start + consumed < total;
            }
            if (expectedTotal is { } previous && declaredTotal != previous)
                throw new InvalidDataException("清单在读取期间发生变化，请刷新重试");
            expectedTotal = declaredTotal;
            if (consumed > 0)
            {
                var stamp = string.Join('\n', output.Skip(output.Count - consumed).Select(member => member.Item.Id + ":" + member.EntryId));
                if (!seenPages.Add(stamp)) throw new InvalidDataException("服务器重复返回同一成员页，请刷新重试");
            }
            start += consumed;
            if (!more) break;
            if (consumed == 0) throw new InvalidDataException("清单成员分页提前结束，请刷新重试");
        }
        return output;
    }

    internal async Task CreateAsync(EmbySessionScope scope, ServerListKind kind, string name, EmbyItem? library,
        IReadOnlyList<EmbyItem> items, CancellationToken token)
    {
        Require(scope);
        if (!CanCreate(scope, kind, library)) throw new InvalidOperationException("当前账号不能创建此类清单，请刷新核对权限");
        RequireItems(scope, items);
        await WriteAsync(scope, async (client, ct, confirm, guard) =>
        {
            if (scope.Connection.IsPlex)
                await client.CreatePlexContainerAsync(PlexKind(kind), name, library?.Id,
                    kind == ServerListKind.Collection ? TypeFor(library!) : null, items.Select(item => item.Id).ToArray(), ct, confirm, guard);
            else await client.CreatePlaylistAsync(name, items.Select(item => item.Id).ToArray(), ct, confirm, guard);
        }, token);
    }

    internal Task AddAsync(EmbySessionScope scope, ServerList list, IReadOnlyList<EmbyItem> items, CancellationToken token)
    {
        RequireList(scope, list);
        RequireItems(scope, items);
        if (!list.CanEdit || items.Count == 0) throw new InvalidOperationException("清单不可编辑或没有所选媒体");
        return WriteAsync(scope, (client, ct, confirm, guard) => scope.Connection.IsPlex
            ? client.AddToPlexContainerAsync(Unmap(list), items.Select(item => item.Id).ToArray(), ct, confirm, guard)
            : client.AddToPlaylistAsync(list.Id, items.Select(item => item.Id).ToArray(), false, ct, confirm, guard), token);
    }

    internal Task RemoveAsync(EmbySessionScope scope, ServerList list, ServerListMember member, CancellationToken token)
    {
        RequireList(scope, list);
        RequireItems(scope, [member.Item]);
        if (!list.CanEdit || string.IsNullOrWhiteSpace(member.EntryId)) throw new InvalidOperationException("服务器没有可编辑的清单成员标识，请刷新后重试");
        return WriteAsync(scope, (client, ct, confirm, guard) => scope.Connection.IsPlex
            ? client.RemoveFromPlexContainerAsync(Unmap(list), member.EntryId, ct, confirm, guard)
            : client.RemoveFromPlaylistAsync(list.Id, [member.EntryId], ct, confirm, guard), token);
    }

    internal Task MoveAsync(EmbySessionScope scope, ServerList list, IReadOnlyList<ServerListMember> members,
        ServerListMember member, int newIndex, CancellationToken token)
    {
        RequireList(scope, list);
        if (!list.CanOrder || newIndex < 0 || newIndex >= members.Count || !members.Contains(member)
            || members.Any(entry => string.IsNullOrWhiteSpace(entry.EntryId)))
            throw new InvalidOperationException("清单顺序或成员标识已改变，请刷新后重试");
        var remaining = members.Where(entry => !ReferenceEquals(entry, member)).ToArray();
        var after = newIndex == 0 ? null : remaining[newIndex - 1].EntryId;
        return WriteAsync(scope, (client, ct, confirm, guard) => scope.Connection.IsPlex
            ? client.MovePlexContainerMemberAsync(Unmap(list), member.EntryId, after, ct, confirm, guard)
            : client.MovePlaylistItemAsync(list.Id, member.EntryId, newIndex, ct, confirm, guard), token);
    }

    internal async Task<ServerListSearchPage> SearchAsync(EmbySessionScope scope, ServerListKind kind, EmbyItem? library,
        string term, int start, int limit, CancellationToken token)
    {
        Require(scope);
        var query = new ItemQuery
        {
            SearchTerm = term,
            ParentId = kind == ServerListKind.Collection ? library?.Id : null,
            ParentItemType = kind == ServerListKind.Collection ? EmbyItemType.CollectionFolder : null,
            Recursive = true,
            IncludeItemTypes = kind == ServerListKind.Collection
                ? [TypeFor(library!)] : [EmbyItemType.Movie, EmbyItemType.Episode, EmbyItemType.Video],
            StartIndex = start,
            Limit = limit
        };
        var page = await scope.ExecuteAsync((client, ct) => client.GetItemsAsync(query, ct), token);
        var items = page.Items.Where(item => kind != ServerListKind.Collection || !scope.Connection.IsPlex
            || item.PlexSectionId == library?.Id).Select(scope.Bind).ToArray();
        return new(items, page.TotalRecordCount, page.Items.Count);
    }

    private static async Task WriteAsync(EmbySessionScope scope,
        Func<EmbyClient, CancellationToken, Action, Action, Task> operation, CancellationToken token)
    {
        var confirmed = false;
        try
        {
            await scope.ExecuteAsync((client, ct) =>
            {
                var revision = scope.PermissionRevision;
                return operation(client, ct, () => confirmed = true, () =>
                {
                    ct.ThrowIfCancellationRequested();
                    scope.ThrowIfNotAvailable();
                    if (scope.PermissionRevision != revision) throw new OperationCanceledException("账号权限已更新，请刷新后重试");
                });
            }, token);
        }
        catch (OperationCanceledException) when (confirmed) { }
        // The clients report ambiguous delivery with a distinct exception. No automatic replay lives here.
    }
    private static string TypeFor(EmbyItem library) => library.CollectionType switch
    { "movies" => EmbyItemType.Movie, "tvshows" => EmbyItemType.Series, _ => throw new NotSupportedException("请选择电影或电视剧库") };
    private static PlexContainerKind PlexKind(ServerListKind kind) => kind == ServerListKind.Playlist ? PlexContainerKind.Playlist : PlexContainerKind.Collection;
    private static ServerList Map(EmbySessionScope scope, PlexContainer list) => new(scope.Bind(list.Item),
        list.Kind == PlexContainerKind.Playlist ? ServerListKind.Playlist : ServerListKind.Collection,
        list.CanEdit, list.CanOrder, list.SectionId, list.ItemType);
    private static PlexContainer Unmap(ServerList list) => new(list.Item, PlexKind(list.Kind), list.SectionId, list.ItemType, list.CanEdit, list.CanOrder);
    private static void Require(EmbySessionScope scope)
    {
        scope.ThrowIfNotAvailable();
        if (!Supports(scope)) throw new NotSupportedException("此服务器没有普通影视清单管理");
    }
    private static void RequireList(EmbySessionScope scope, ServerList list)
    {
        Require(scope);
        RequireItems(scope, [list.Item]);
        if (!scope.Connection.IsPlex && list.Kind != ServerListKind.Playlist) throw new NotSupportedException("此合集使用媒体库的独立管理入口");
    }
    private static void RequireItems(EmbySessionScope scope, IReadOnlyList<EmbyItem> items)
    {
        if (items.Any(item => item.Source is not { IsAvailable: true } source || source.Key != scope.Key
            || !ReferenceEquals(source.UserStateOwner, scope.UserStateOwner)))
            throw new InvalidOperationException("清单和媒体必须属于同一个在线服务器账号");
    }
}

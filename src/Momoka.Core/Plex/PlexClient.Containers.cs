using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.Plex;

public sealed partial class PlexClient
{
    public async Task<PlexListCapabilities> GetListCapabilitiesAsync(PlexConnection c, CancellationToken ct = default)
    {
        var root = await RootAsync(PlexEndpoint.Api(c.ApiBase, "media/providers"), c.Call, ct).ConfigureAwait(false);
        if (!PlexListCapabilities.NeedsAccountProfile(root)) return PlexListCapabilities.Parse(root);
        try
        {
            var profile = await http.GetAccountProfileAsync(c.Call, ct).ConfigureAwait(false);
            if (profile["id"] is not JsonValue idValue || !idValue.TryGetValue<long>(out var id) || id <= 0
                || id.ToString(CultureInfo.InvariantCulture) != c.AccountId
                || profile["anonymous"] is not JsonValue anonymousValue || !anonymousValue.TryGetValue<bool>(out var anonymous))
                return PlexListCapabilities.Parse(root) with { VerificationError = "Plex 账号权限响应未能确认当前账号身份或匿名状态，相关清单保持只读。" };
            return PlexListCapabilities.Parse(root, authenticatedAccount: !anonymous);
        }
        catch (PlexApiException error) when (!error.IsTokenExpired)
        {
            return PlexListCapabilities.Parse(root) with { VerificationError = "无法核对 Plex 账号的清单权限：" + error.Message };
        }
    }

    public async Task<PlexContainerPage> GetVideoPlaylistsAsync(PlexConnection c, int start, int size, CancellationToken ct = default)
    {
        ContainerPage(start, size);
        var permissions = await GetListCapabilitiesAsync(c, ct).ConfigureAwait(false);
        var root = await RootAsync(PlexEndpoint.Api(c.ApiBase, "playlists/all" + Query(
            [new("type", "15"), new("playlistType", "video"), new("smart", "0"), new("sort", "titleSort:asc")])),
            c.Call with { Headers = Page(start, size) }, ct).ConfigureAwait(false);
        return Containers(root, PlexContainerKind.Playlist, permissions, start, size);
    }

    public async Task<PlexContainerPage> GetCollectionsAsync(PlexConnection c, string sectionId, string itemType,
        int start, int size, CancellationToken ct = default)
    {
        ContainerPage(start, size);
        PlexContainerIdentity.Id(sectionId);
        var type = PlexContainerIdentity.MediaType(itemType);
        var permissions = await GetListCapabilitiesAsync(c, ct).ConfigureAwait(false);
        var root = await RootAsync(PlexEndpoint.Api(c.ApiBase, "library/sections/" + sectionId + "/collections" + Query(
            [new("subtype", type.ToString(CultureInfo.InvariantCulture)), new("smart", "0"), new("sort", "titleSort:asc")])),
            c.Call with { Headers = Page(start, size) }, ct).ConfigureAwait(false);
        return Containers(root, PlexContainerKind.Collection, permissions, start, size, sectionId, type == 1 ? EmbyItemType.Movie : EmbyItemType.Series);
    }

    public async Task<PlexContainer> GetContainerAsync(PlexConnection c, PlexContainerKind kind, string id,
        CancellationToken ct = default)
    {
        var path = ContainerPath(kind, id);
        var permissions = await GetListCapabilitiesAsync(c, ct).ConfigureAwait(false);
        var root = await RootAsync(PlexEndpoint.Api(c.ApiBase, path), c.Call, ct).ConfigureAwait(false);
        var value = PlexMediaAdapter.Items(root).Concat(PlexMediaAdapter.Elements(root, "Playlist")).FirstOrDefault()
            ?? throw new InvalidDataException("Plex 没有返回这份清单");
        if (PlexMediaAdapter.Text(value, "ratingKey") != id) throw new InvalidDataException("Plex 清单身份与请求不一致");
        return PlexContainerIdentity.Map(value, kind, permissions);
    }

    public async Task<PlexContainerMembers> GetContainerMembersAsync(PlexConnection c, PlexContainer container,
        int start, int size, CancellationToken ct = default)
    {
        ContainerPage(start, size);
        var path = ContainerPath(container.Kind, container.Id)
            + (container.Kind == PlexContainerKind.Playlist ? "/items" : "/children");
        var root = await RootAsync(PlexEndpoint.Api(c.ApiBase, path), c.Call with { Headers = Page(start, size) }, ct).ConfigureAwait(false);
        var items = PlexMediaAdapter.Items(root).Select(value => new PlexContainerMember(PlexMediaAdapter.Item(value),
            container.Kind == PlexContainerKind.Collection
                ? PlexContainerIdentity.IsId(PlexMediaAdapter.Text(value, "ratingKey")) ? PlexMediaAdapter.Text(value, "ratingKey") : null
                : PlexContainerIdentity.IsId(PlexMediaAdapter.Text(value, "playlistItemID")) ? PlexMediaAdapter.Text(value, "playlistItemID") : null)).ToArray();
        var total = PlexContainerIdentity.Count(root, "totalSize");
        var end = checked(start + items.Length);
        if (items.Length == 0 && total is { } declared && start < declared)
            throw new InvalidDataException("Plex 清单成员没有完整返回，请刷新重试");
        return new(items, total ?? end, items.Length > 0 && (total is { } known ? end < known : items.Length >= size))
        { DeclaredTotal = total };
    }

    public async Task<PlexContainerCreationResult> CreateContainerAsync(PlexConnection c, PlexContainerKind kind,
        string name, string? sectionId, string? itemType, IReadOnlyList<string> ids, CancellationToken ct = default,
        Action? confirmed = null, Action? beforeWrite = null)
    {
        if (kind is not (PlexContainerKind.Playlist or PlexContainerKind.Collection))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Any(char.IsControl)) throw new ArgumentException("清单名称不能包含控制字符", nameof(name));
        var selected = ContainerIds(ids);
        var permissions = await GetListCapabilitiesAsync(c, ct).ConfigureAwait(false);
        if (kind == PlexContainerKind.Playlist ? !permissions.CanCreatePlaylists : !permissions.CanManageCollections)
            throw new UnauthorizedAccessException("当前 Plex 账号没有创建此清单的权限");
        var pairs = new List<PlexParameter> { new("title", name.Trim()), new("smart", "0") };
        if (kind == PlexContainerKind.Playlist) pairs.Add(new("type", "video"));
        else
        {
            await ValidateCollectionItemsAsync(c, sectionId!, itemType!, selected, ct).ConfigureAwait(false);
            pairs.Add(new("type", PlexContainerIdentity.MediaType(itemType).ToString(CultureInfo.InvariantCulture)));
            pairs.Add(new("sectionId", sectionId));
        }
        pairs.Add(new("uri", await ContainerSourceAsync(c, selected, ct).ConfigureAwait(false)));
        beforeWrite?.Invoke();
        ct.ThrowIfCancellationRequested(); c.ThrowIfNotCurrent();
        var root = await http.WriteAsync(HttpMethod.Post, PlexEndpoint.Api(c.ApiBase,
            (kind == PlexContainerKind.Playlist ? "playlists" : "library/collections") + Query(pairs)), c.Call, ct, confirmed).ConfigureAwait(false);
        // Once the HTTP write has succeeded, an optional malformed identity must not cause a second POST.
        try
        {
            var value = root is null ? null : PlexMediaAdapter.Items(root).Concat(PlexMediaAdapter.Elements(root, "Playlist")).FirstOrDefault();
            return new(value is null ? null : PlexContainerIdentity.Map(value, kind, permissions, sectionId, itemType));
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { return new(null); }
    }

    public async Task AddToContainerAsync(PlexConnection c, PlexContainer container, IReadOnlyList<string> ids,
        CancellationToken ct = default, Action? confirmed = null, Action? beforeWrite = null)
    {
        var selected = ContainerIds(ids);
        var fresh = await EditableContainerAsync(c, container, ct).ConfigureAwait(false);
        if (fresh.Kind == PlexContainerKind.Collection)
            await ValidateCollectionItemsAsync(c, fresh.SectionId!, fresh.ItemType!, selected, ct).ConfigureAwait(false);
        var source = await ContainerSourceAsync(c, selected, ct).ConfigureAwait(false);
        beforeWrite?.Invoke();
        ct.ThrowIfCancellationRequested(); c.ThrowIfNotCurrent();
        _ = await http.WriteAsync(HttpMethod.Put, PlexEndpoint.Api(c.ApiBase, ContainerPath(fresh.Kind, fresh.Id)
            + "/items" + Query([new("uri", source)])), c.Call, ct, confirmed).ConfigureAwait(false);
    }

    public async Task RemoveFromContainerAsync(PlexConnection c, PlexContainer container, string entryId,
        CancellationToken ct = default, Action? confirmed = null, Action? beforeWrite = null)
    {
        PlexContainerIdentity.Id(entryId);
        var fresh = await EditableContainerAsync(c, container, ct).ConfigureAwait(false);
        await RequireMemberAsync(c, fresh, entryId, ct).ConfigureAwait(false);
        beforeWrite?.Invoke();
        ct.ThrowIfCancellationRequested(); c.ThrowIfNotCurrent();
        _ = await http.WriteAsync(HttpMethod.Delete, PlexEndpoint.Api(c.ApiBase, ContainerPath(fresh.Kind, fresh.Id)
            + (fresh.Kind == PlexContainerKind.Playlist ? "/items/" : "/children/") + entryId), c.Call, ct, confirmed).ConfigureAwait(false);
    }

    /// <summary>Null predecessor moves to the first position. Duplicates keep distinct playlistItemID identities.</summary>
    public async Task MoveContainerMemberAsync(PlexConnection c, PlexContainer container, string entryId, string? afterEntryId,
        CancellationToken ct = default, Action? confirmed = null, Action? beforeWrite = null)
    {
        PlexContainerIdentity.Id(entryId);
        if (afterEntryId is not null) PlexContainerIdentity.Id(afterEntryId);
        if (entryId == afterEntryId) throw new ArgumentException("成员不能移动到自身之后", nameof(afterEntryId));
        var fresh = await EditableContainerAsync(c, container, ct).ConfigureAwait(false);
        if (!fresh.CanOrder) throw new NotSupportedException("当前清单不支持手动排序");
        await RequireMemberAsync(c, fresh, entryId, ct).ConfigureAwait(false);
        if (afterEntryId is not null) await RequireMemberAsync(c, fresh, afterEntryId, ct).ConfigureAwait(false);
        beforeWrite?.Invoke();
        ct.ThrowIfCancellationRequested(); c.ThrowIfNotCurrent();
        _ = await http.WriteAsync(HttpMethod.Put, PlexEndpoint.Api(c.ApiBase, ContainerPath(fresh.Kind, fresh.Id)
            + "/items/" + entryId + "/move" + Query([new("after", afterEntryId)])), c.Call, ct, confirmed).ConfigureAwait(false);
    }

    private async Task<PlexContainer> EditableContainerAsync(PlexConnection c, PlexContainer expected, CancellationToken ct)
    {
        var fresh = await GetContainerAsync(c, expected.Kind, expected.Id, ct).ConfigureAwait(false);
        if (!fresh.CanEdit) throw new UnauthorizedAccessException("这份 Plex 清单只读，或当前账号没有编辑权限");
        if (fresh.Kind == PlexContainerKind.Collection && (fresh.SectionId != expected.SectionId || fresh.ItemType != expected.ItemType))
            throw new InvalidOperationException("Plex 合集所属媒体库或类型已改变，请刷新后重试");
        return fresh;
    }

    private async Task RequireMemberAsync(PlexConnection c, PlexContainer container, string entry, CancellationToken ct)
    {
        const int Size = 200;
        int? declaredTotal = null;
        var seenPages = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; ;)
        {
            var page = await GetContainerMembersAsync(c, container, start, Size, ct).ConfigureAwait(false);
            if (declaredTotal is { } previous && page.DeclaredTotal != previous)
                throw new InvalidDataException("Plex 清单在成员校验期间发生变化，请刷新重试");
            declaredTotal = page.DeclaredTotal;
            var pageIdentity = string.Join('\n', page.Items.Select(item => item.Item.Id + ":" + item.EntryId));
            if (page.Items.Count > 0 && !seenPages.Add(pageIdentity))
                throw new InvalidDataException("Plex 重复返回成员校验页，请刷新重试");
            if (page.Items.Any(item => item.EntryId == entry)) return;
            start = checked(start + page.Items.Count);
            if (!page.HasMore) break;
        }
        throw new InvalidOperationException("该成员已不在清单中，请刷新后重试");
    }

    private async Task ValidateCollectionItemsAsync(PlexConnection c, string section, string itemType,
        IReadOnlyList<string> ids, CancellationToken ct)
    {
        PlexContainerIdentity.Id(section);
        var type = PlexContainerIdentity.MediaType(itemType) == 1 ? "movie" : "show";
        foreach (var id in ids)
        {
            var root = await GetMetadataAsync(c, id, ct).ConfigureAwait(false);
            var item = PlexMediaAdapter.Items(root).FirstOrDefault(value => PlexMediaAdapter.Text(value, "ratingKey") == id)
                ?? throw new InvalidDataException("Plex 没有返回选中的媒体条目");
            if (PlexMediaAdapter.Text(item, "librarySectionID") != section || PlexMediaAdapter.Text(item, "type") != type)
                throw new InvalidOperationException("Plex 合集只能包含所选媒体库中的同类条目");
        }
    }

    private async Task<string> ContainerSourceAsync(PlexConnection c, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var root = await GetServerAsync(c, ct).ConfigureAwait(false);
        var machine = PlexMediaAdapter.Text(root, "machineIdentifier");
        if (string.IsNullOrWhiteSpace(machine) || !machine.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
            throw new InvalidDataException("Plex 服务器缺少有效的 machineIdentifier");
        return "server://" + machine + "/" + LibraryIdentifier + "/library/metadata/" + string.Join(',', ids);
    }

    private static string[] ContainerIds(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) throw new ArgumentException("请选择至少一个媒体条目", nameof(ids));
        return ids.Select(PlexContainerIdentity.Id).ToArray();
    }

    private static string ContainerPath(PlexContainerKind kind, string id) => kind switch
    {
        PlexContainerKind.Playlist => "playlists/" + PlexContainerIdentity.Id(id),
        PlexContainerKind.Collection => "library/collections/" + PlexContainerIdentity.Id(id),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void ContainerPage(int start, int size)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
    }

    private static PlexContainerPage Containers(JsonObject root, PlexContainerKind kind, PlexListCapabilities permissions,
        int start, int size, string? section = null, string? itemType = null)
    {
        var raw = PlexMediaAdapter.Items(root).Concat(PlexMediaAdapter.Elements(root, "Playlist")).ToArray();
        var items = raw.Where(value => PlexContainerIdentity.Boolean(value, "smart") == false
                && (kind != PlexContainerKind.Playlist || PlexMediaAdapter.Text(value, "playlistType") == "video"))
            .Select(value => PlexContainerIdentity.Map(value, kind, permissions, section, itemType)).ToArray();
        var total = PlexContainerIdentity.Count(root, "totalSize");
        var end = checked(start + raw.Length);
        if (raw.Length == 0 && total is { } declared && start < declared)
            throw new InvalidDataException("Plex 清单目录没有完整返回，请刷新重试");
        return new(items, total ?? end, raw.Length, raw.Length > 0 && (total is { } known ? end < known : raw.Length >= size))
        {
            DeclaredTotal = total,
            PageIdentity = string.Join('\n', raw.Select(value => PlexMediaAdapter.Text(value, "ratingKey")))
        };
    }
}

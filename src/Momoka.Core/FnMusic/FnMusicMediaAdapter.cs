using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;

namespace Momoka.FnMusic;

/// <summary>Maps the music product into shared identities while retaining its own routes and units.</summary>
public sealed partial class FnMusicMediaAdapter(FnMusicClient client, FnMusicConnection connection)
{
    public const string LibraryId = "music";
    private const string ContainerPrefix = "fnmusic:";

    public static async Task<PublicSystemInfo> DetectAsync(FnMusicClient client, string address, CancellationToken ct)
    {
        var mount = FnMusicEndpoint.Normalize(address);
        var config = await client.GetConfigurationAsync(mount, ct).ConfigureAwait(false);
        if (Text(config, "serverGUID").Length == 0 || !Version.TryParse(Text(config, "serverVersion"), out _)
            || !Version.TryParse(Text(config, "mediasrvVersion"), out _)) throw new EmbyApiException("该地址未返回可识别的飞牛音乐服务器信息");
        return new PublicSystemInfo
        {
            ProductName = "飞牛音乐",
            DetectedServerKind = MediaServerKind.FnMusic,
            ApiBase = mount,
            Id = Text(config, "serverGUID"),
            ServerName = Text(config, "serverName"),
            Version = Text(config, "serverVersion")
        };
    }

    public static async Task<EmbyConnection> SignInAsync(FnMusicClient client, Uri mount, string username, string password,
        DeviceIdentity device, string? version, string? serverId, CancellationToken ct)
    {
        // Confirm the instance before sending a credential, including direct callers of AuthenticateAsync.
        var info = await DetectAsync(client, mount.AbsoluteUri, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(serverId) && info.Id != serverId) throw new EmbyApiException("飞牛音乐服务器身份已改变，请重新确认地址");
        JsonObject result;
        try { result = await client.LoginAsync(mount, username, password, device.DeviceId, ct).ConfigureAwait(false); }
        catch (EmbyTokenExpiredException) { throw new EmbyAuthenticationException("飞牛音乐拒绝了登录，请核对用户名和密码"); }
        var token = Text(result, "userToken");
        var user = User(FnMusicClient.Object(result["user"]));
        if (token.Length == 0 || token.Any(char.IsControl)) throw new EmbyApiException("飞牛音乐登录结果缺少有效令牌");
        var confirmed = User(await client.GetAsync(new(mount, token), "user/me", ct).ConfigureAwait(false));
        if (user.Id != confirmed.Id) throw new EmbyApiException("飞牛音乐登录身份不一致");
        return new EmbyConnection(mount, token, confirmed.Id, confirmed.Name, info.ServerName ?? mount.Host, device)
        {
            ServerKind = MediaServerKind.FnMusic,
            ServerId = info.Id,
            ServerVersion = info.Version ?? version,
            IsAdministrator = confirmed.Policy?.IsAdministrator,
            CanDownload = false,
            CanDeleteContent = false,
            CanManageSubtitles = false,
            CanManageCollections = false
        };
    }

    public static EmbyUser User(JsonObject value)
    {
        var id = Text(value, "guid"); var name = Text(value, "name"); var role = Text(value, "role");
        if (id.Length == 0 || name.Length == 0 || role is not ("admin" or "member")) throw new EmbyApiException("飞牛音乐用户信息缺少有效身份字段");
        return new EmbyUser
        {
            Id = id,
            Name = name,
            HasPassword = true,
            Policy = new()
            {
                IsAdministrator = role == "admin",
                EnableContentDownloading = false,
                EnableContentDeletion = false,
                EnableSubtitleDownloading = false,
                EnableSubtitleManagement = false,
                EnableCollectionManagement = false
            }
        };
    }

    public async Task<EmbyUser> GetCurrentUserAsync(CancellationToken ct) => User(await client.GetAsync(connection, "user/me", ct).ConfigureAwait(false));
    public async Task<List<EmbyItem>> GetViewsAsync(CancellationToken ct)
    {
        _ = await GetCurrentUserAsync(ct).ConfigureAwait(false);
        return [new() { Id = LibraryId, Name = "音乐", Type = EmbyItemType.CollectionFolder, IsFolder = true, CollectionType = "music", PrimaryImageAspectRatio = 1 }];
    }

    public async Task<ItemsResult> GetItemsAsync(ItemQuery query, CancellationToken ct)
    {
        if (query.IsResumable || query.IsPlayed is not null || query.Genre is not null || query.PersonId is not null) return new();
        if (query.IncludeItemTypes.Count > 1) return new();
        var type = query.IncludeItemTypes.Count == 0 ? EmbyItemType.Audio : query.IncludeItemTypes[0];
        if (type is not (EmbyItemType.Audio or EmbyItemType.MusicAlbum or EmbyItemType.MusicArtist or EmbyItemType.Playlist)) return new();
        var (container, guid) = Container(query.ParentId);
        if (query.ArtistIds.Count > 1) throw new NotSupportedException("飞牛音乐每次只支持一个艺术家查询");
        var artist = query.ArtistIds.FirstOrDefault() ?? (container == "artist" ? guid : null);
        if (artist is not null && Container(artist) is ("artist", { } artistGuid)) artist = artistGuid;
        string path; var parameters = new List<(string Key, string? Value)>();
        var isSearch = !string.IsNullOrWhiteSpace(query.SearchTerm);
        if (isSearch && query.IsFavorite) throw new NotSupportedException("飞牛音乐暂不支持在收藏内搜索");
        if (isSearch)
        {
            if (container is not null || artist is not null) throw new NotSupportedException("飞牛音乐不支持容器内搜索");
            path = "search/" + Family(type); parameters.Add(("q", query.SearchTerm!.Trim()));
        }
        else if (query.IsFavorite)
        {
            if (type != EmbyItemType.Audio) return new();
            path = "favorite-track/list";
        }
        else if (artist is not null)
        {
            if (type is not (EmbyItemType.Audio or EmbyItemType.MusicAlbum)) return new();
            path = Family(type) + "/artist-detail/list"; parameters.Add(("artistGUID", artist));
        }
        else if (container is "album" or "playlist")
        {
            if (type != EmbyItemType.Audio) return new();
            path = "track/" + container + "-detail/list"; parameters.Add((container + "GUID", guid));
        }
        else
        {
            if (!string.IsNullOrEmpty(query.ParentId) && query.ParentId != LibraryId) throw new NotSupportedException("飞牛音乐不支持此容器");
            path = Family(type) + "/list";
        }
        var start = Math.Max(0, query.StartIndex); var limit = Math.Clamp(query.Limit, 1, 1000);
        if (isSearch || type == EmbyItemType.Playlist)
        {
            // The official playlist list has no paging input. Search results are sliced locally rather than
            // assuming that every server version honours undocumented page/size search parameters.
            var data = await client.GetAsync(connection, path, ct, [.. parameters]).ConfigureAwait(false);
            var all = Rows(data).Select(value => Item(value, type)).ToList();
            return new() { Items = all.Skip(start).Take(limit).ToList(), TotalRecordCount = Total(data, all.Count) };
        }
        if (!string.IsNullOrEmpty(query.SortBy)) parameters.Add(("sort", Sort(query, type, container)));
        var page = start / limit + 1; var skip = start % limit; var items = new List<EmbyItem>(); var total = 0;
        do
        {
            var args = parameters.Concat(new[] { ("page", (string?)page.ToString(CultureInfo.InvariantCulture)), ("size", (string?)limit.ToString(CultureInfo.InvariantCulture)) }).ToArray();
            var data = await client.GetAsync(connection, path, ct, args).ConfigureAwait(false);
            var rows = Rows(data).ToList(); total = Total(data, rows.Count);
            items.AddRange(rows.Skip(skip).Take(limit - items.Count).Select(value => Item(value, type)));
            skip = 0;
            if (rows.Count == 0 || page * limit >= total) break;
            page++;
        } while (items.Count < limit);
        return new() { Items = items, TotalRecordCount = total };
    }

    public async Task<EmbyItem> GetItemAsync(string id, CancellationToken ct)
    {
        if (id == LibraryId) return (await GetViewsAsync(ct).ConfigureAwait(false)).Single();
        var (container, guid) = Container(id);
        if (container is not null)
        {
            var type = container switch { "album" => EmbyItemType.MusicAlbum, "artist" => EmbyItemType.MusicArtist, _ => EmbyItemType.Playlist };
            return Item(await client.GetAsync(connection, container + "/detail", ct, ("guid", guid)).ConfigureAwait(false), type);
        }
        var data = await client.GetAsync(connection, "track/metadata", ct, ("guid", id)).ConfigureAwait(false);
        var track = FnMusicClient.Object(data["track"]);
        var item = Item(track, EmbyItemType.Audio, data["audioSpec"] as JsonObject);
        if (item.Id != id) throw new EmbyApiException("飞牛音乐曲目身份与请求不一致");
        return item;
    }

    public async Task<EmbyUserData?> SetFavoriteAsync(string id, bool favorite, CancellationToken ct)
    {
        if (Container(id).Kind is not null || id == LibraryId) throw new NotSupportedException("飞牛音乐仅支持收藏歌曲");
        await client.PostAsync(connection, favorite ? "favorite-track/create" : "favorite-track/delete", new() { ["trackGUID"] = id }, ct).ConfigureAwait(false);
        return null;
    }

    public Task<byte[]> GetImageAsync(string? tag, int width, CancellationToken ct) => string.IsNullOrEmpty(tag)
        ? throw new ArgumentException("飞牛音乐条目没有封面", nameof(tag))
        : client.GetCoverAsync(connection, tag, width <= 160 ? 160 : width <= 600 ? 600 : width <= 1024 ? 1024 : width <= 1600 ? 1600 : 2000, ct);

    public async Task ReportPlaybackStartAsync(PlaybackReport report, CancellationToken ct) =>
        await client.PostAsync(connection, "event/report", new()
        {
            ["events"] = new JsonArray(new JsonObject
            { ["eventType"] = "track_play", ["occurredAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["payload"] = new JsonObject { ["trackGUID"] = report.ItemId } })
        }, ct).ConfigureAwait(false);

    private static string Family(string type) => type switch { EmbyItemType.MusicAlbum => "album", EmbyItemType.MusicArtist => "artist", EmbyItemType.Playlist => "playlist", _ => "track" };
    private static string Sort(ItemQuery query, string type, string? container)
    {
        var field = query.SortBy switch
        {
            EmbySortBy.DateAdded => query.IsFavorite ? "favoriteAt" : "createdAt",
            EmbySortBy.Number => "trackNo",
            _ => type == EmbyItemType.Audio ? "title" : "name"
        };
        if (container == "album" && query.SortBy == EmbySortBy.Name) field = "trackNo";
        return field + (query.Descending ? ",desc" : ",asc");
    }
    private static (string? Kind, string? Guid) Container(string? id)
    {
        if (id?.StartsWith(ContainerPrefix, StringComparison.Ordinal) != true) return (null, null);
        var parts = id[ContainerPrefix.Length..].Split(':', 2);
        if (parts.Length != 2 || parts[0] is not ("album" or "artist" or "playlist") || parts[1].Length == 0)
            throw new ArgumentException("飞牛音乐容器标识无效", nameof(id));
        return (parts[0], parts[1]);
    }
    private static IEnumerable<JsonObject> Rows(JsonObject data) => data["list"] is JsonArray list
        ? list.Select(FnMusicClient.Object) : throw new EmbyApiException("飞牛音乐列表响应缺少条目集合");
    private static int Total(JsonObject data, int fallback) => Number(data, "total") is >= 0 and <= int.MaxValue and var total ? (int)total : fallback;
    internal static string Text(JsonObject value, string key) => value[key] is JsonValue node && node.TryGetValue<string>(out var text) ? text : "";
    internal static long? Number(JsonObject value, string key) => value[key] is JsonValue node && node.TryGetValue<long>(out var number) ? number : null;
    internal static bool Flag(JsonObject value, string key) => value[key] is JsonValue node && node.TryGetValue<bool>(out var flag) && flag;
}

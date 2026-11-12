using System.Net;
using System.Globalization;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PlexContainerTests
{
    private static readonly PlexConnection Connection = new(new Uri("https://pms.invalid/proxy/"), "fixture-list-token", "7", "fixture", "device", "1");

    public static void Register()
    {
        Test("Plex 清单：仅匹配原生 library provider 功能且未知要求不授权", () =>
        {
            var capabilities = PlexListCapabilities.Parse(Providers());
            Assert.True(capabilities.CanCreatePlaylists);
            Assert.True(capabilities.CanManageCollections);
            Assert.False(PlexListCapabilities.Parse(Providers(playlistRequires: "unknown-account-grant")).CanCreatePlaylists);
            Assert.False(PlexListCapabilities.Parse(Providers(playlistRequires: "nonAnonymousAccount")).CanCreatePlaylists);
            Assert.True(PlexListCapabilities.Parse(Providers(playlistRequires: "nonAnonymousAccount"), authenticatedAccount: true).CanCreatePlaylists);
            Assert.False(PlexListCapabilities.Parse(Providers(manage: false)).CanManageCollections);
            var foreign = Providers();
            foreign["MediaProvider"]![0]!["identifier"] = "foreign.provider";
            Assert.Equal(new PlexListCapabilities(false, false), PlexListCapabilities.Parse(foreign));
        });
        Test("Plex 清单：视频独立目录分页、过滤智能清单但按原始数量推进", () =>
        {
            using var fixture = new Fixture();
            fixture.Playlists.Add(Playlist("90", smart: true));
            fixture.Playlists.Add(Playlist("91", readOnly: true));
            fixture.Playlists.Add(Playlist("92", family: "audio"));
            var page = fixture.Client.GetVideoPlaylistsAsync(Connection, 20, 50).GetAwaiter().GetResult();
            Assert.Equal(4, page.Consumed);
            Assert.Equal(70, page.TotalRecordCount);
            Assert.Equal<int?>(70, page.DeclaredTotal);
            Assert.Equal("80,91", string.Join(',', page.Items.Select(item => item.Id)));
            Assert.True(page.Items[0].CanEdit && page.Items[0].CanOrder);
            Assert.False(page.Items[1].CanEdit);
            var request = fixture.Requests.Single(item => item.Path == "/proxy/playlists/all");
            Assert.Equal("20", request.Headers["X-Plex-Container-Start"]);
            Assert.Equal("50", request.Headers["X-Plex-Container-Size"]);
            Assert.Equal("video", request.Query["playlistType"]);
            Assert.Equal("0", request.Query["smart"]);
            Assert.Equal("15", request.Query["type"]);
            Assert.False(request.Query.ContainsKey("X-Plex-Container-Start"));
        });
        Test("Plex 清单：合集按媒体库和原生subtype读取", () =>
        {
            using var fixture = new Fixture();
            var page = fixture.Client.GetCollectionsAsync(Connection, "5", EmbyItemType.Movie, 0, 10).GetAwaiter().GetResult();
            Assert.Equal(PlexContainerKind.Collection, page.Items.Single().Kind);
            Assert.Equal("5", page.Items.Single().SectionId);
            Assert.Equal("1", fixture.Requests.Single(item => item.Path.EndsWith("/collections")).Query["subtype"]);
            Assert.False(page.Items.Single().CanOrder);
            Assert.Throws<InvalidDataException>(() => fixture.Client.GetCollectionsAsync(Connection, "5", EmbyItemType.Series, 0, 10).GetAwaiter().GetResult());
            Assert.Throws<InvalidDataException>(() => fixture.Client.GetCollectionsAsync(Connection, "7", EmbyItemType.Movie, 0, 10).GetAwaiter().GetResult());
        });
        Test("Plex 清单：重复媒体保留独立playlistItemID并保留服务器顺序", () =>
        {
            using var fixture = new Fixture();
            var container = fixture.Container();
            var page = fixture.Client.GetContainerMembersAsync(Connection, container, 0, 10).GetAwaiter().GetResult();
            Assert.Equal("10,10,11", string.Join(',', page.Items.Select(item => item.Item.Id)));
            Assert.Equal("201,202,203", string.Join(',', page.Items.Select(item => item.EntryId)));
            Assert.Equal(EmbyItemType.Movie, page.Items[0].Item.Type);
            Assert.Equal("video", container.Item.PlexPlaylistType);
        });
        Test("Plex 清单：缺失或不明smart/readOnly保持只读", () =>
        {
            using var fixture = new Fixture();
            fixture.Playlists[0].Remove("readOnly");
            Assert.False(fixture.Container().CanEdit);
            fixture.Playlists[0]["readOnly"] = "unproven";
            Assert.False(fixture.Container().CanEdit);
            fixture.Playlists[0]["readOnly"] = false;
            fixture.Playlists[0].Remove("smart");
            Assert.False(fixture.Container().CanEdit);
        });
        Test("Plex 清单：缺总数按满页继续，成员后页仍能按entryId定位", () =>
        {
            using var fixture = new Fixture();
            fixture.OmitTotals = true;
            var container = fixture.Container();
            var first = fixture.Client.GetContainerMembersAsync(Connection, container, 0, 2).GetAwaiter().GetResult();
            var second = fixture.Client.GetContainerMembersAsync(Connection, container, 2, 2).GetAwaiter().GetResult();
            Assert.True(first.HasMore);
            Assert.True(first.DeclaredTotal is null);
            Assert.False(second.HasMore);
            Assert.Equal("203", second.Items.Single().EntryId);
            Assert.False(fixture.Client.GetVideoPlaylistsAsync(Connection, 0, 50).GetAwaiter().GetResult().HasMore);
            fixture.Client.RemoveFromContainerAsync(Connection, container, "203").GetAwaiter().GetResult();
            Assert.Equal(1, fixture.Writes.Count);
        });
        Test("Plex 清单：新建和添加使用POST/PUT及同服媒体URI", () =>
        {
            using var fixture = new Fixture();
            var receipt = fixture.Client.CreateContainerAsync(Connection, PlexContainerKind.Playlist, " 假清单 ", null, null,
                ["10", "10", "11"]).GetAwaiter().GetResult();
            Assert.Equal("80", receipt.Container!.Id);
            var write = fixture.Writes.Single();
            Assert.Equal("POST", write.Method);
            Assert.Equal("/proxy/playlists", write.Path);
            Assert.Equal("video", write.Query["type"]);
            Assert.Equal("0", write.Query["smart"]);
            Assert.Equal("假清单", write.Query["title"]);
            Assert.Equal("server://machine/com.plexapp.plugins.library/library/metadata/10,10,11", write.Query["uri"]);
            fixture.Client.AddToContainerAsync(Connection, receipt.Container, ["12"]).GetAwaiter().GetResult();
            Assert.Equal("PUT", fixture.Writes[1].Method);
            Assert.Equal("/proxy/playlists/80/items", fixture.Writes[1].Path);
            Assert.DoesNotContain("fixture-list-token", write.Uri.AbsoluteUri);
            Assert.True(fixture.Requests.All(item => item.Headers["X-Plex-Token"] == "fixture-list-token"));
        });
        Test("Plex 清单：移除和移首使用成员身份而不删除媒体", () =>
        {
            using var fixture = new Fixture();
            var container = fixture.Container();
            fixture.Client.RemoveFromContainerAsync(Connection, container, "202").GetAwaiter().GetResult();
            Assert.Equal("DELETE", fixture.Writes[0].Method);
            Assert.Equal("/proxy/playlists/80/items/202", fixture.Writes[0].Path);
            fixture.Client.MoveContainerMemberAsync(Connection, container, "203", "201").GetAwaiter().GetResult();
            Assert.Equal("PUT", fixture.Writes[1].Method);
            Assert.Equal("201", fixture.Writes[1].Query["after"]);
            fixture.Client.MoveContainerMemberAsync(Connection, container, "202", null).GetAwaiter().GetResult();
            Assert.False(fixture.Writes[2].Query.ContainsKey("after"));
            Assert.True(fixture.Writes.All(item => !item.Path.StartsWith("/proxy/library/metadata/", StringComparison.Ordinal)));
            Assert.Throws<InvalidOperationException>(() => fixture.Client.RemoveFromContainerAsync(Connection, container, "999").GetAwaiter().GetResult());
            Assert.Equal(3, fixture.Writes.Count);
        });
        Test("Plex 清单：写前重新读取只读及provider权限，缓存授权不能越权", () =>
        {
            using var fixture = new Fixture();
            var cached = fixture.Container();
            fixture.Playlists[0]["readOnly"] = true;
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Client.AddToContainerAsync(Connection, cached, ["10"]).GetAwaiter().GetResult());
            fixture.Playlists[0]["readOnly"] = false;
            fixture.Provider = Providers(playlistRequires: "unknown");
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Client.RemoveFromContainerAsync(Connection, cached, "201").GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Writes.Count);
        });
        Test("Plex 清单：写前成员校验拒绝缺总数的重复满页", () =>
        {
            using var fixture = new Fixture();
            fixture.OmitTotals = true;
            fixture.RepeatFullMemberPages = true;
            var container = fixture.Container();
            Assert.Throws<InvalidDataException>(() => fixture.Client.RemoveFromContainerAsync(Connection, container, "999").GetAwaiter().GetResult());
            Assert.Equal(2, fixture.Requests.Count(request => request.Path == "/proxy/playlists/80/items"));
            Assert.Equal(0, fixture.Writes.Count);
        });
        Test("Plex 清单：写前成员校验拒绝声明总数跨页变化", () =>
        {
            using var fixture = new Fixture();
            fixture.RepeatFullMemberPages = true;
            fixture.ChangeMemberTotal = true;
            var container = fixture.Container();
            Assert.Throws<InvalidDataException>(() => fixture.Client.RemoveFromContainerAsync(Connection, container, "999").GetAwaiter().GetResult());
            Assert.Equal(2, fixture.Requests.Count(request => request.Path == "/proxy/playlists/80/items"));
            Assert.Equal(0, fixture.Writes.Count);
        });
        Test("Plex 清单：原生非匿名账号要求由当前认证身份满足", () =>
        {
            using var fixture = new Fixture();
            fixture.Provider = Providers(playlistRequires: "nonAnonymousAccount");
            Assert.True(fixture.Client.GetListCapabilitiesAsync(Connection).GetAwaiter().GetResult().CanCreatePlaylists);
            Assert.False(fixture.Client.GetListCapabilitiesAsync(Connection with { AccountId = "" }).GetAwaiter().GetResult().CanCreatePlaylists);
            fixture.Client.AddToContainerAsync(Connection, fixture.Container(), ["10"]).GetAwaiter().GetResult();
            Assert.Equal(1, fixture.Writes.Count);
            var profileRequest = fixture.Requests.First(item => item.Uri.Host == "plex.tv" && item.Method == "GET");
            Assert.Equal("/api/v2/user", profileRequest.Path);
            Assert.Equal("fixture-list-token", profileRequest.Headers["X-Plex-Token"]);
            Assert.DoesNotContain("fixture-list-token", profileRequest.Uri.AbsoluteUri);
        });
        Test("Plex 清单：PMS账号ID不替代账户anonymous，未知或异身份保持只读", () =>
        {
            using var fixture = new Fixture(); fixture.Provider = Providers(playlistRequires: "nonAnonymousAccount");
            foreach (var profile in new[]
            {
                new JsonObject { ["id"] = 7, ["anonymous"] = true },
                new JsonObject { ["id"] = 8, ["anonymous"] = false },
                new JsonObject { ["id"] = "7", ["anonymous"] = false },
                new JsonObject { ["id"] = 7, ["anonymous"] = "false" },
                new JsonObject { ["id"] = 7 }
            })
            {
                fixture.Profile = profile;
                Assert.False(fixture.Client.GetListCapabilitiesAsync(Connection).GetAwaiter().GetResult().CanCreatePlaylists);
            }
            fixture.ProfileFailure = () => throw new HttpRequestException("fixture account offline");
            var permissions = fixture.Client.GetListCapabilitiesAsync(Connection).GetAwaiter().GetResult();
            Assert.False(permissions.CanCreatePlaylists);
            Assert.True(permissions.CanManageCollections);
            Assert.Contains("Plex 账号", permissions.VerificationError);
            fixture.ProfileFailure = () => new(HttpStatusCode.TemporaryRedirect)
            { Headers = { Location = new Uri("https://foreign.invalid/profile") } };
            Assert.False(fixture.Client.GetListCapabilitiesAsync(Connection).GetAwaiter().GetResult().CanCreatePlaylists);
            Assert.False(fixture.Requests.Any(item => item.Uri.Host == "foreign.invalid"));
            Assert.Equal(0, fixture.Writes.Count);
        });
        Test("Plex 清单：合集写入核对同库同类，移除走children成员路由", () =>
        {
            using var fixture = new Fixture();
            var receipt = fixture.Client.CreateContainerAsync(Connection, PlexContainerKind.Collection, "合集", "5", EmbyItemType.Movie,
                ["10"]).GetAwaiter().GetResult();
            var collection = receipt.Container!;
            Assert.Equal("/proxy/library/collections", fixture.Writes[0].Path);
            Assert.Equal("5", fixture.Writes[0].Query["sectionId"]);
            Assert.Equal("1", fixture.Writes[0].Query["type"]);
            fixture.Client.RemoveFromContainerAsync(Connection, collection, "10").GetAwaiter().GetResult();
            Assert.Equal("/proxy/library/collections/90/children/10", fixture.Writes[1].Path);
            Assert.Throws<InvalidOperationException>(() => fixture.Client.AddToContainerAsync(Connection, collection, ["99"]).GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => fixture.Client.MoveContainerMemberAsync(Connection, collection, "10", null).GetAwaiter().GetResult());
            Assert.Equal(2, fixture.Writes.Count);
        });
        Test("Plex 清单：合集成员即使携带播放列表字段仍只认ratingKey", () =>
        {
            using var fixture = new Fixture();
            var collection = fixture.Client.GetContainerAsync(Connection, PlexContainerKind.Collection, "90").GetAwaiter().GetResult();
            var members = fixture.Client.GetContainerMembersAsync(Connection, collection, 0, 10).GetAwaiter().GetResult();
            Assert.Equal("10", members.Items.Single().EntryId);
        });
        Test("Plex 清单：无效标识、空选择、预取消和身份撤销不写入", () =>
        {
            using var fixture = new Fixture();
            var container = fixture.Container();
            foreach (var id in new[] { "0", "..", "80/items/1", "80,1", "1?X-Plex-Token=x", "1#x" })
                Assert.Throws<ArgumentException>(() => fixture.Client.RemoveFromContainerAsync(Connection, container, id).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => fixture.Client.CreateContainerAsync(Connection, PlexContainerKind.Playlist, "x", null, null, []).GetAwaiter().GetResult());
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Client.AddToContainerAsync(Connection, container, ["10"], cancelled.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => fixture.Client.AddToContainerAsync(Connection with { IsCurrent = () => false }, container, ["10"]).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Writes.Count);
        });
        Test("Plex 清单：HTTP成功而正文损坏仍是已创建回执", () =>
        {
            using var fixture = new Fixture();
            fixture.WriteResponse = () => new(HttpStatusCode.OK) { Content = new StringContent("invalid JSON") };
            var confirmed = 0;
            var receipt = fixture.Client.CreateContainerAsync(Connection, PlexContainerKind.Playlist, "x", null, null,
                ["10"], confirmed: () => confirmed++).GetAwaiter().GetResult();
            Assert.Null(receipt.Container);
            Assert.Equal(1, confirmed);
            Assert.Equal(1, fixture.Writes.Count);
        });
        Test("Plex 清单：最终权限代次守卫拒绝迟到的创建添加移除排序", () =>
        {
            using var fixture = new Fixture();
            var container = fixture.Container();
            var calls = 0;
            void Guard() { calls++; throw new OperationCanceledException("fixture permission revision changed"); }
            Assert.Throws<OperationCanceledException>(() => fixture.Client.CreateContainerAsync(Connection, PlexContainerKind.Playlist,
                "x", null, null, ["10"], beforeWrite: Guard).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => fixture.Client.AddToContainerAsync(Connection, container, ["10"],
                beforeWrite: Guard).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => fixture.Client.RemoveFromContainerAsync(Connection, container, "201",
                beforeWrite: Guard).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => fixture.Client.MoveContainerMemberAsync(Connection, container, "201", "202",
                beforeWrite: Guard).GetAwaiter().GetResult());
            Assert.Equal(4, calls);
            Assert.Equal(0, fixture.Writes.Count);
        });
        Test("Plex 清单：2xx之后取消和正文IO失败保留确认，不重复写", () =>
        {
            using var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            var confirmed = 0;
            fixture.WriteResponse = () => new(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) };
            fixture.Client.AddToContainerAsync(Connection, fixture.Container(), ["10"], cancellation.Token, () =>
            { confirmed++; cancellation.Cancel(); }).GetAwaiter().GetResult();
            Assert.Equal(1, confirmed);
            Assert.Equal(1, fixture.Writes.Count);
        });
        Test("Plex 清单：拒绝明确失败而丢失写回执明确未知", () =>
        {
            using var fixture = new Fixture();
            fixture.WriteResponse = () => new(HttpStatusCode.Forbidden);
            var error = Assert.Catch<PlexApiException>(() => fixture.Client.AddToContainerAsync(Connection, fixture.Container(), ["10"]).GetAwaiter().GetResult());
            Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
            fixture.WriteResponse = () => throw new HttpRequestException("fixture disconnect");
            Assert.Throws<PlexWriteUncertainException>(() => fixture.Client.AddToContainerAsync(Connection, fixture.Container(), ["10"]).GetAwaiter().GetResult());
            Assert.Equal(2, fixture.Writes.Count);
        });
        Test("Plex 清单：发送后取消而未获HTTP收据不能误报取消成功", () =>
        {
            using var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            fixture.WriteResponse = () => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); };
            var confirmed = 0;
            Assert.Throws<PlexWriteUncertainException>(() => fixture.Client.AddToContainerAsync(Connection, fixture.Container(), ["10"],
                cancellation.Token, () => confirmed++).GetAwaiter().GetResult());
            Assert.Equal(0, confirmed);
            Assert.Equal(1, fixture.Writes.Count);
        });
        Test("Plex 清单：服务器5xx不能确证未写，标记未知避免重发", () =>
        {
            using var fixture = new Fixture();
            fixture.WriteResponse = () => new(HttpStatusCode.ServiceUnavailable);
            Assert.Throws<PlexWriteUncertainException>(() => fixture.Client.AddToContainerAsync(Connection, fixture.Container(), ["10"]).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Writes.Count);
        });
        Test("Plex 清单：声明成员未完却返回空页必须报错", () =>
        {
            using var fixture = new Fixture();
            fixture.EmptyMemberPage = true;
            Assert.Throws<InvalidDataException>(() => fixture.Client.GetContainerMembersAsync(Connection, fixture.Container(), 2, 10).GetAwaiter().GetResult());
        });
        Test("Plex 清单：跨来源写重定向不向外服发送请求", () =>
        {
            using var fixture = new Fixture();
            fixture.WriteResponse = () => new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://foreign.invalid/write") } };
            Assert.Throws<PlexWriteUncertainException>(() => fixture.Client.AddToContainerAsync(Connection, fixture.Container(), ["10"]).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Writes.Count);
            Assert.True(fixture.Requests.All(item => item.Uri.Host == "pms.invalid"));
        });
    }

    private static JsonObject Providers(bool manage = true, string? playlistRequires = null) => new()
    {
        ["MediaProvider"] = new JsonArray(new JsonObject
        {
            ["identifier"] = PlexClient.LibraryIdentifier,
            ["Feature"] = new JsonArray(new JsonObject { ["type"] = "playlist", ["key"] = "/playlists", ["requires"] = playlistRequires },
                new JsonObject { ["type"] = "collection", ["key"] = "/library/collections" },
                new JsonObject { ["type"] = manage ? "manage" : "unsupported" })
        })
    };

    private static JsonObject Playlist(string id, bool smart = false, bool readOnly = false, string family = "video") => new()
    { ["ratingKey"] = id, ["type"] = "playlist", ["playlistType"] = family, ["title"] = "假清单", ["smart"] = smart, ["readOnly"] = readOnly };

    private static JsonObject Collection() => new()
    { ["ratingKey"] = "90", ["type"] = "collection", ["title"] = "假合集", ["smart"] = 0, ["subtype"] = "movie", ["librarySectionID"] = "5" };

    private static JsonObject Media(string id, string? entry = null) => new()
    { ["ratingKey"] = id, ["type"] = "movie", ["title"] = "假影片", ["librarySectionID"] = id == "99" ? "7" : "5", ["playlistItemID"] = entry };

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly PlexHttp _http;
        public PlexClient Client { get; }
        public List<Request> Requests { get; } = [];
        public List<Request> Writes => Requests.Where(item => item.Method != "GET").ToList();
        public List<JsonObject> Playlists { get; } = [Playlist("80")];
        public JsonObject Provider { get; set; } = Providers();
        public Func<HttpResponseMessage>? WriteResponse { get; set; }
        public bool OmitTotals { get; set; }
        public bool EmptyMemberPage { get; set; }
        public bool RepeatFullMemberPages { get; set; }
        public bool ChangeMemberTotal { get; set; }
        public JsonObject Profile { get; set; } = new() { ["id"] = 7, ["anonymous"] = false };
        public Func<HttpResponseMessage>? ProfileFailure { get; set; }

        public Fixture() { _http = new(this, disposeHandler: false); Client = new(_http); }
        public PlexContainer Container() => Client.GetContainerAsync(Connection, PlexContainerKind.Playlist, "80").GetAwaiter().GetResult();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new Request(message.Method.Method, message.RequestUri!, message.Headers.ToDictionary(pair => pair.Key,
                pair => string.Join(',', pair.Value), StringComparer.OrdinalIgnoreCase));
            Requests.Add(request);
            if (request.Uri.Host == "plex.tv")
            {
                Assert.Equal("GET", request.Method);
                Assert.Equal("/api/v2/user", request.Path);
                return Task.FromResult(ProfileFailure?.Invoke() ?? new(HttpStatusCode.OK) { Content = new StringContent(Profile.ToJsonString()) });
            }
            if (request.Method != "GET")
                return Task.FromResult(WriteResponse?.Invoke() ?? Reply(new JsonObject
                {
                    ["Metadata"] = new JsonArray(
                    (request.Path == "/proxy/library/collections" ? Collection() : Playlists[0]).DeepClone())
                }));
            var path = request.Path["/proxy".Length..];
            if (path == "/media/providers") return Task.FromResult(Reply(Provider));
            if (path == "/") return Task.FromResult(Reply(new JsonObject { ["machineIdentifier"] = "machine" }));
            if (path == "/playlists/all") return Task.FromResult(Reply(new JsonObject
            { ["Metadata"] = new JsonArray(Playlists.Select(item => item.DeepClone()).ToArray()), ["totalSize"] = OmitTotals ? null : "70" }));
            if (path == "/playlists/80") return Task.FromResult(Reply(new JsonObject { ["Metadata"] = new JsonArray(Playlists[0].DeepClone()) }));
            if (path == "/playlists/80/items")
            {
                var start = int.Parse(request.Headers["X-Plex-Container-Start"]);
                var size = int.Parse(request.Headers["X-Plex-Container-Size"]);
                if (RepeatFullMemberPages)
                {
                    var total = ChangeMemberTotal && start > 0 ? 401 : 400;
                    return Task.FromResult(Reply(new JsonObject
                    {
                        ["Metadata"] = new JsonArray(Enumerable.Range(1, size).Select(index => (JsonNode)Media("10", index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                        ["totalSize"] = OmitTotals ? null : total
                    }));
                }
                var raw = EmptyMemberPage ? Array.Empty<JsonObject>() : new[] { Media("10", "201"), Media("10", "202"), Media("11", "203") };
                return Task.FromResult(Reply(new JsonObject
                { ["Metadata"] = new JsonArray(raw.Skip(start).Take(size).Select(item => (JsonNode)item).ToArray()), ["totalSize"] = OmitTotals ? null : "3" }));
            }
            if (path.EndsWith("/collections", StringComparison.Ordinal) || path == "/library/collections/90")
                return Task.FromResult(Reply(new JsonObject { ["Metadata"] = new JsonArray(Collection()), ["totalSize"] = 1 }));
            if (path == "/library/collections/90/children") return Task.FromResult(Reply(new JsonObject { ["Metadata"] = new JsonArray(Media("10", "901")), ["totalSize"] = 1 }));
            if (path.StartsWith("/library/metadata/", StringComparison.Ordinal))
                return Task.FromResult(Reply(new JsonObject { ["Metadata"] = new JsonArray(Media(path["/library/metadata/".Length..])) }));
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }

        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }

    private sealed record Request(string Method, Uri Uri, IReadOnlyDictionary<string, string> Headers)
    {
        public string Path => Uri.AbsolutePath;
        public IReadOnlyDictionary<string, string> Query => Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
    }

    private static HttpResponseMessage Reply(JsonObject root) => new(HttpStatusCode.OK)
    { Content = new StringContent(new JsonObject { ["MediaContainer"] = root.DeepClone() }.ToJsonString()) };

    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("fixture receipt disconnect");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

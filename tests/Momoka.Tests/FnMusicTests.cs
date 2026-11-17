using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.FnMusic;
using Momoka.Configuration;
using Momoka.Infrastructure;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class FnMusicTests
{
    // Using the append-only enum value keeps this first regression executable before the product exists.
    private static readonly MediaServerKind Kind = (MediaServerKind)4;
    private static readonly DeviceIdentity Device = new("Momoka", "fixture", "fixture-device", "1.0");

    public static void Register()
    {
        Test("飞牛音乐：包装艺术家与歌单原序、收藏搜索边界", () =>
        {
            using var fixture = new Fixture();
            var client = fixture.Client();
            client.GetItemsAsync(new ItemQuery { ParentId = "fnmusic:artist:artist", ArtistIds = ["fnmusic:artist:artist"], IncludeItemTypes = [EmbyItemType.Audio], Limit = 1 }, default).GetAwaiter().GetResult();
            Assert.Contains("artistGUID=artist", fixture.Transport.Requests.Last().Query);
            client.GetItemsAsync(new ItemQuery { ParentId = "fnmusic:playlist:playlist", IncludeItemTypes = [EmbyItemType.Audio], SortBy = "", Limit = 1 }, default).GetAwaiter().GetResult();
            Assert.False(fixture.Transport.Requests.Last().Query.Contains("sort=", StringComparison.Ordinal));
            Assert.Throws<NotSupportedException>(() => client.GetItemsAsync(new ItemQuery
            { ParentId = "music", IncludeItemTypes = [EmbyItemType.Audio], IsFavorite = true, SearchTerm = "song" }, default).GetAwaiter().GetResult());
        });
        Test("飞牛音乐：签名使用音乐应用身份与最终路径", () =>
        {
            var get = FnMusicSigner.Create(HttpMethod.Get, new Uri("https://fixture.invalid/music/api/v1/track/list?b=2&a=旧&a=null&a=更新+空格&empty=&x=undefined"), null, "123456", 1794268800000);
            Assert.Equal("nonce=123456&timestamp=1794268800000&sign=418aedd7bcc0035581520d5f95604aac", get);
            var post = FnMusicSigner.Create(HttpMethod.Post, new Uri("https://fixture.invalid/proxy/music/api/v1/user/password-login"), "{\"username\":\"听众\",\"password\":\"摘要\",\"deviceId\":\"固定\"}", "123456", 1794268800000);
            Assert.Equal("nonce=123456&timestamp=1794268800000&sign=b203e9420c9eb46ce1444e01ce26d904", post);
        });
        Test("飞牛音乐：艺术家关联查询传裸 ID 与对应路由", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client();
            var query = new ItemQuery { ParentId = "music", ArtistIds = ["artist"], IncludeItemTypes = [EmbyItemType.MusicAlbum], Limit = 2 };
            Assert.Contains("ArtistIds=artist", query.ToQueryString());
            var albums = client.GetItemsAsync(query, default).GetAwaiter().GetResult();
            Assert.True(fixture.Transport.Requests[^1].Path.EndsWith("album/artist-detail/list", StringComparison.Ordinal));
            Assert.True(albums.Items.All(item => item.Id.StartsWith("fnmusic:album:", StringComparison.Ordinal)));
            var tracks = client.GetItemsAsync(new ItemQuery { ParentId = "fnmusic:artist:artist", IncludeItemTypes = [EmbyItemType.Audio], Limit = 1 }, default).GetAwaiter().GetResult();
            Assert.Equal("歌手", tracks.Items.Single().Artists.Single());
            Assert.Equal("artist", tracks.Items.Single().ArtistItems.Single().Id);
            Assert.Equal("fnmusic:album:album", tracks.Items.Single().AlbumId);
        });
        Test("飞牛音乐：已保存登录恢复复核服务器与用户，过期同身份重登", () =>
        {
            using var fixture = new SessionFixture();
            fixture.Session.SignInAsync(fixture.Server, fixture.Account, "fixture-password", "listener", true, default).GetAwaiter().GetResult();
            var scope = fixture.Session.Capture(); fixture.Transport.ExpireOnce = true;
            var result = scope.ExecuteAsync((client, ct) => client.GetItemsAsync(new ItemQuery { ParentId = "music", IncludeItemTypes = [EmbyItemType.Audio], Limit = 1 }, ct), default).GetAwaiter().GetResult();
            Assert.Equal(1, result.Items.Count);
            Assert.Equal(2, fixture.Transport.Logins);
            Assert.True(scope.IsCurrent);
            Assert.True(fixture.Session.TakeOffline(fixture.Server, fixture.Account));
            Assert.False(scope.IsCurrent);
            Assert.True(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.Equal(2, fixture.Transport.Logins);
            Assert.Equal("music", fixture.Session.TakeRestoredViews()!.Single().Id);
        });
        Test("飞牛音乐：恢复服务器身份变化不发送已保存凭据", () =>
        {
            using var fixture = new SessionFixture();
            fixture.Account.UserId = "user"; fixture.Account.ProtectedAccessToken = "fixture-token";
            fixture.Server.ServerId = "other-server";
            Assert.False(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.True(fixture.Transport.Requests.All(request => request.Cookie is null));
        });
        Test("飞牛音乐恢复：裸 NAS 根同时安装影视仍恢复音乐账号", () =>
        {
            using var fixture = new SessionFixture();
            fixture.Server.Url = "https://fixture.invalid/";
            fixture.Account.UserId = "user";
            fixture.Account.ProtectedAccessToken = "fixture-token";
            fixture.Transport.OffersVideo = true;
            Assert.True(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
            Assert.Equal(Kind, fixture.Session.Connection!.ServerKind);
            Assert.Equal("https://fixture.invalid/music/", fixture.Session.Connection.ApiBase.AbsoluteUri);
            Assert.Equal("server", fixture.Session.Connection.ServerId);
            Assert.Equal("user", fixture.Session.Connection.UserId);
            Assert.Equal(0, fixture.Transport.Logins);
            Assert.Equal("/music/api/v1/sys/config", fixture.Transport.Requests[0].Path);
            Assert.Null(fixture.Transport.Requests[0].Cookie);
            Assert.True(fixture.Transport.Requests.All(request => request.Path.StartsWith("/music/api/v1/", StringComparison.Ordinal)));
        });
        Test("飞牛音乐恢复：裸 NAS 根仍拒绝音乐服务器或用户身份变化", () =>
        {
            foreach (var differentServer in new[] { true, false })
            {
                using var fixture = new SessionFixture();
                fixture.Server.Url = "https://fixture.invalid/";
                fixture.Account.UserId = "user";
                fixture.Account.ProtectedAccessToken = "fixture-token";
                fixture.Transport.OffersVideo = true;
                if (differentServer) fixture.Transport.MusicServerId = "replacement-server";
                else fixture.Transport.MusicUserId = "replacement-user";
                Assert.False(fixture.Session.TryRestoreAsync(fixture.Server, fixture.Account, default).GetAwaiter().GetResult());
                Assert.False(fixture.Session.IsSignedIn);
                Assert.Equal(0, fixture.Transport.Logins);
                Assert.True(fixture.Transport.Requests.All(request => request.Path.StartsWith("/music/api/v1/", StringComparison.Ordinal)));
                if (differentServer) Assert.True(fixture.Transport.Requests.All(request => request.Cookie is null));
            }
        });
        Test("飞牛音乐：CUE与访问受限曲目不提供错误原流", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client(); fixture.Transport.Cue = true;
            var cue = client.GetItemAsync("track0", default).GetAwaiter().GetResult();
            Assert.Equal(0, cue.MediaSources.Count);
            Assert.Throws<NotSupportedException>(() => client.StreamUrl(cue, null));
            fixture.Transport.Cue = false; fixture.Transport.AccessStatus = 2;
            var denied = client.GetItemAsync("track0", default).GetAwaiter().GetResult();
            Assert.Equal(0, denied.MediaSources.Count);
        });
        Test("飞牛音乐：封面请求按官方尺寸，跨源重定向不泄露Cookie", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client(); fixture.Transport.RedirectCover = true;
            Assert.Throws<EmbyApiException>(() => client.GetImageBytesAsync("track0", EmbyImageStore.Primary, "track_cover", 300, default).GetAwaiter().GetResult());
            Assert.Contains("size=600", fixture.Transport.Requests[^1].Query);
            Assert.True(fixture.Transport.Requests.All(request => request.Host == "fixture.invalid"));
        });
        Test("飞牛音乐：网页及 API 地址收敛独立 music 入口", () =>
        {
            Assert.Equal("FnMusic", Kind.ToString());
            Assert.Equal("https://fixture.invalid/proxy/music/", EmbyServerAddress.Normalize(
                "https://fixture.invalid/proxy/music/api/v1/track/list?token=discard#x", Kind).AbsoluteUri);
            Assert.Equal("https://fixture.invalid/music/", EmbyServerAddress.Normalize("https://fixture.invalid/music/login", Kind).AbsoluteUri);
        });
        Test("飞牛音乐：Cookie 凭据仅音乐后端且不进入 URL", () =>
        {
            var headers = MediaServerHeaders.Create(Device, "fixture-token;unsafe=1", Kind);
            Assert.Equal(1, headers.Count);
            Assert.Equal("Cookie", headers[0].Key);
            Assert.Equal("music-token=fixture-token%3Bunsafe%3D1", headers[0].Value);
            Assert.Throws<InvalidOperationException>(() => MediaServerHeaders.Create(Device, "bad\r\ntoken", Kind));
        });
        Test("飞牛音乐：匿名识别和密码登录不沿用影视身份", () =>
        {
            using var fixture = new Fixture();
            var info = fixture.Gateway.DetectAsync("https://fixture.invalid/music/", default).GetAwaiter().GetResult();
            Assert.Equal(Kind, info.ServerKind);
            Assert.Equal("server", info.Id);
            var login = fixture.Login();
            Assert.Equal("user", login.UserId);
            Assert.Equal(Kind, login.ServerKind);
            Assert.True(login.IsAdministrator == false);
            var body = fixture.Transport.LoginBody!;
            Assert.Equal("listener", body["username"]!.ToString());
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("fixture-password"))), body["password"]!.ToString());
            Assert.Equal(32, body["deviceId"]!.ToString().Length);
            Assert.True(fixture.Transport.Requests.Where(request => request.Path.EndsWith("sys/config", StringComparison.Ordinal)
                || request.Path.EndsWith("password-login", StringComparison.Ordinal)).All(request => request.Cookie is null));
        });
        Test("飞牛音乐：媒体库名写「飞牛音乐」", () =>
        {
            using var fixture = new Fixture();
            var library = fixture.Client().GetViewsAsync(default).GetAwaiter().GetResult().Single();
            Assert.Equal("飞牛音乐", library.Name, "飞牛音乐库不该只叫「音乐」，和别家的音乐库分不出来");
            Assert.Equal("music", library.CollectionType);
        });
        Test("飞牛音乐：曲目列表分页跨页且映射毫秒与音频规格", () =>
        {
            using var fixture = new Fixture();
            var client = fixture.Client();
            var result = client.GetItemsAsync(new ItemQuery { ParentId = "music", IncludeItemTypes = [EmbyItemType.Audio], StartIndex = 3, Limit = 2 }, default).GetAwaiter().GetResult();
            Assert.Equal(6, result.TotalRecordCount);
            Assert.Equal("track3,track4", string.Join(',', result.Items.Select(item => item.Id)));
            var item = result.Items[0];
            Assert.Equal(EmbyItemType.Audio, item.Type);
            Assert.Equal(123_000_000L, item.RunTimeTicks);
            Assert.Equal(2, item.MediaSources.Single().MediaStreams.Single().Channels);
            Assert.Equal("https://fixture.invalid/music/api/v1/track/stream?guid=track3", item.MediaSources.Single().DirectStreamUrl!.AbsoluteUri);
            Assert.False(item.MediaSources.Single().DirectStreamUrl!.AbsoluteUri.Contains("token", StringComparison.Ordinal));
        });
        Test("飞牛音乐：专辑歌单容器使用各自曲目路由", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client();
            _ = client.GetItemsAsync(new ItemQuery { ParentId = "fnmusic:album:album", IncludeItemTypes = [EmbyItemType.Audio], Limit = 2 }, default).GetAwaiter().GetResult();
            _ = client.GetItemsAsync(new ItemQuery { ParentId = "fnmusic:playlist:playlist", IncludeItemTypes = [EmbyItemType.Audio], Limit = 2 }, default).GetAwaiter().GetResult();
            Assert.True(fixture.Transport.Requests.Any(request => request.Path.EndsWith("track/album-detail/list", StringComparison.Ordinal) && request.Query.Contains("albumGUID=album", StringComparison.Ordinal)));
            Assert.True(fixture.Transport.Requests.Any(request => request.Path.EndsWith("track/playlist-detail/list", StringComparison.Ordinal) && request.Query.Contains("playlistGUID=playlist", StringComparison.Ordinal)));
        });
        Test("飞牛音乐：四类搜索分别调用原生接口", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client();
            foreach (var type in new[] { EmbyItemType.Audio, EmbyItemType.MusicAlbum, EmbyItemType.MusicArtist, EmbyItemType.Playlist })
                _ = client.GetItemsAsync(new ItemQuery { SearchTerm = "歌 & 曲", IncludeItemTypes = [type], Limit = 2 }, default).GetAwaiter().GetResult();
            Assert.Equal(4, fixture.Transport.Requests.Count(request => request.Path.Contains("/search/", StringComparison.Ordinal)));
            Assert.True(fixture.Transport.Requests.Where(request => request.Path.Contains("/search/", StringComparison.Ordinal)).All(request => request.Query.Contains("q=", StringComparison.Ordinal)));
        });
        Test("飞牛音乐：喜欢确认不隐式重读重放写入", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client();
            Assert.Null(client.SetFavoriteAsync("track0", true, default).GetAwaiter().GetResult());
            var last = fixture.Transport.Requests[^1];
            Assert.Equal("/music/api/v1/favorite-track/create", last.Path);
            Assert.Equal("fixture-token", last.Cookie?.Replace("music-token=", "", StringComparison.Ordinal));
            Assert.Equal("track0", last.Body?["trackGUID"]?.ToString());
        });
        Test("飞牛音乐：401触发身份失效，权限错误保留身份", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client(); fixture.Transport.Failure = 120001;
            Assert.Throws<EmbyTokenExpiredException>(() => client.GetItemsAsync(new ItemQuery { IncludeItemTypes = [EmbyItemType.Audio] }, default).GetAwaiter().GetResult());
            fixture.Transport.Failure = 100004;
            var error = Assert.Catch<EmbyApiException>(() => client.GetItemsAsync(new ItemQuery { IncludeItemTypes = [EmbyItemType.Audio] }, default).GetAwaiter().GetResult());
            Assert.False(error is EmbyTokenExpiredException);
        });
        Test("飞牛音乐：影视专用读取不发送 Emby 请求", () =>
        {
            using var fixture = new Fixture(); var client = fixture.Client(); var before = fixture.Transport.Requests.Count;
            Assert.Equal(0, client.QueryResumeAsync(0, 10, default).GetAwaiter().GetResult().Items.Count);
            Assert.Equal(0, client.GetNextUpAsync(10, default).GetAwaiter().GetResult().Count);
            Assert.Throws<NotSupportedException>(() => client.MarkPlayedAsync("track0", default).GetAwaiter().GetResult());
            Assert.Throws<NotSupportedException>(() => client.GetChapterImageBytesAsync("track0", 0, "tag", 200, default).GetAwaiter().GetResult());
            Assert.Equal(before, fixture.Transport.Requests.Count);
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Transport Transport = new();
        internal readonly EmbyHttp Http;
        internal readonly EmbyServerGateway Gateway;
        internal Fixture() { Http = new(Transport); Gateway = new(Http, Device); }
        internal EmbyConnection Login() => Gateway.AuthenticateAsync(new Uri("https://fixture.invalid/music/"), "listener", "fixture-password", default, Kind, "1.1.19", "server").GetAwaiter().GetResult();
        internal EmbyClient Client() => new(Http, Login());
        public void Dispose() => Http.Dispose();
    }

    private sealed class SessionFixture : IDisposable
    {
        internal readonly Transport Transport = new();
        internal readonly ServerProfile Server = new() { ServerKind = Kind, Url = "https://fixture.invalid/music/", ServerId = "server" };
        internal readonly AccountProfile Account = new() { Username = "listener" };
        internal readonly EmbySession Session;
        internal SessionFixture()
        {
            var settings = new AppSettings(); Server.Accounts.Add(Account); settings.Servers.Add(Server);
            Session = new(settings, new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "momoka-fnmusic-fixture-" + Guid.NewGuid().ToString("N"))), PassthroughSecretProtector.Instance),
                new CredentialVault(PassthroughSecretProtector.Instance), Device, Transport);
        }
        public void Dispose() => Session.Dispose();
    }

    private sealed record Request(string Path, string Query, string? Cookie, JsonObject? Body, string Host);
    private sealed class Transport : HttpMessageHandler
    {
        internal List<Request> Requests { get; } = [];
        internal JsonObject? LoginBody { get; private set; }
        internal int Failure { get; set; }
        internal bool Cue { get; set; }
        internal int AccessStatus { get; set; }
        internal bool RedirectCover { get; set; }
        internal bool ExpireOnce { get; set; }
        internal int Logins { get; private set; }
        internal bool OffersVideo { get; set; }
        internal string MusicServerId { get; set; } = "server";
        internal string MusicUserId { get; set; } = "user";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken)) as JsonObject;
            var cookie = request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join(';', cookies) : null;
            Requests.Add(new(uri.AbsolutePath, uri.Query, cookie, body, uri.Host));
            if (OffersVideo && uri.AbsolutePath is "/v/api/v1/sys/version" or "/v/api/v1/sys/config")
            {
                var video = uri.AbsolutePath.EndsWith("version", StringComparison.Ordinal)
                    ? new JsonObject { ["version"] = "0.9.8", ["mediasrvVersion"] = "0.8.45" }
                    : new JsonObject { ["server_guid"] = "video-server", ["server_name"] = "影视" };
                return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["code"] = 0, ["data"] = video }.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            if (!uri.AbsolutePath.StartsWith("/music/api/v1/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            var route = uri.AbsolutePath["/music/api/v1/".Length..];
            JsonNode? data;
            var code = 0;
            if (route == "sys/config") data = new JsonObject { ["serverGUID"] = MusicServerId, ["serverName"] = "音乐库", ["serverVersion"] = "1.1.19", ["mediasrvVersion"] = "0.8.45" };
            else if (route == "user/password-login") { Logins++; LoginBody = body; data = new JsonObject { ["userToken"] = "fixture-token", ["user"] = User() }; }
            else if (route == "user/me") data = User();
            else if (Failure != 0) { code = Failure; data = null; }
            else if (ExpireOnce) { ExpireOnce = false; code = 120001; data = null; }
            else if (route == "static/cover" && RedirectCover) { var redirect = new HttpResponseMessage(HttpStatusCode.Found); redirect.Headers.Location = new Uri("https://external.invalid/cover"); return redirect; }
            else if (route.StartsWith("favorite-track/", StringComparison.Ordinal) && request.Method == HttpMethod.Post) data = null;
            else if (route == "track/metadata") data = new JsonObject { ["track"] = Track("track0"), ["audioSpec"] = Spec() };
            else if (route.EndsWith("/detail", StringComparison.Ordinal)) data = new JsonObject { ["guid"] = "album", ["name"] = "专辑", ["trackCount"] = 6 };
            else
            {
                var query = uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "");
                var page = query.TryGetValue("page", out var p) ? int.Parse(p, System.Globalization.CultureInfo.InvariantCulture) : 1;
                var size = query.TryGetValue("size", out var z) ? int.Parse(z, System.Globalization.CultureInfo.InvariantCulture) : 6;
                var list = new JsonArray();
                for (var index = (page - 1) * size; index < Math.Min(page * size, 6); index++) list.Add(Track("track" + index));
                data = new JsonObject { ["list"] = list, ["total"] = 6, ["sort"] = "title,asc" };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["code"] = code, ["msg"] = "", ["data"] = data }.ToJsonString(), Encoding.UTF8, "application/json") };
        }
        private JsonObject User() => new() { ["guid"] = MusicUserId, ["name"] = "listener", ["role"] = "member" };
        private static JsonObject Spec() => new() { ["path"] = "/library/song.flac", ["codec"] = "flac", ["format"] = "flac", ["channel"] = 2, ["duration"] = 12300, ["sampleRate"] = 48000, ["bitDepth"] = 24, ["bitrate"] = 800000, ["size"] = 2000000 };
        private JsonObject Track(string id) => new() { ["guid"] = id, ["title"] = "歌曲", ["name"] = "容器", ["duration"] = 12300, ["audioSpec"] = Spec(), ["artists"] = new JsonArray(new JsonObject { ["guid"] = "artist", ["name"] = "歌手" }), ["album"] = new JsonObject { ["guid"] = "album", ["name"] = "专辑" }, ["coverId"] = "track_cover", ["isFavorite"] = true, ["isCue"] = Cue, ["accessStatus"] = AccessStatus };
    }
}

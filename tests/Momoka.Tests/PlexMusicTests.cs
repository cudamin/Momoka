using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Anonymous protocol fixtures; no account, real HTTP, media IO or playback.</summary>
internal static class PlexMusicTests
{
    public static void Register()
    {
        Test("Plex 音乐：音乐库保留 section key 与 music 类型，照片仍排除", () =>
        {
            using var fixture = new Fixture();
            var views = fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult();
            Assert.Equal("1,3", string.Join(',', views.Select(item => item.Id)));
            Assert.Equal("movies,music", string.Join(',', views.Select(item => item.CollectionType)));
            Assert.True(views.All(item => item.IsFolder && item.Type == EmbyItemType.CollectionFolder));
        });
        Test("Plex 音乐：艺术家、专辑、歌曲、歌单保留各自类型与容器语义", () =>
        {
            foreach (var (native, expected, folder) in new[]
            {
                ("artist", EmbyItemType.MusicArtist, true), ("album", EmbyItemType.MusicAlbum, true),
                ("track", EmbyItemType.Audio, false), ("playlist", EmbyItemType.Playlist, true)
            })
            {
                var item = PlexMediaAdapter.Item(JsonNode.Parse($"{{\"ratingKey\":\"100\",\"title\":\"音乐\",\"type\":\"{native}\"}}")!.AsObject());
                Assert.Equal(expected, item.Type);
                Assert.Equal(folder, item.IsFolder);
                Assert.Equal(1d, item.PrimaryImageAspectRatio);
            }
        });
        Test("Plex 音乐：库根歌曲、专辑、艺术家使用 10、9、8 与容器头分页", () =>
        {
            foreach (var (type, number) in new[]
            {
                (EmbyItemType.Audio, "10"), (EmbyItemType.MusicAlbum, "9"), (EmbyItemType.MusicArtist, "8")
            })
            {
                using var fixture = new Fixture();
                var page = fixture.Read(Query("3", EmbyItemType.CollectionFolder, type, 7, 2));
                var request = fixture.Transport.Requests.Single();
                Assert.Equal("/proxy/library/sections/3/all", request.Path);
                Assert.Contains("type=" + number, request.Query);
                Assert.Equal("7", request.Start);
                Assert.Equal("2", request.Size);
                Assert.Equal(18, page.TotalRecordCount);
                Assert.DoesNotContain("Container", request.Query);
            }
        });
        Test("Plex 音乐：专辑曲目走 children，Track 容器与碟号、曲号和时长不丢", () =>
        {
            using var fixture = new Fixture();
            var page = fixture.Read(Query("200", EmbyItemType.MusicAlbum, EmbyItemType.Audio, 2, 1));
            var request = fixture.Transport.Requests.Single();
            Assert.Equal("/proxy/library/metadata/200/children", request.Path);
            Assert.Equal("", request.Query);
            Assert.Equal("2", request.Start);
            Assert.Equal("1", request.Size);
            Assert.Equal(3, page.TotalRecordCount);
            var item = page.Items.Single();
            Assert.Equal(EmbyItemType.Audio, item.Type);
            Assert.Equal("200", item.ParentId);
            Assert.Equal(3, item.IndexNumber);
            Assert.Equal(2, item.ParentIndexNumber);
            Assert.Equal(TimeSpan.FromSeconds(123).Ticks, item.RunTimeTicks);
            Assert.Equal("/library/metadata/200/thumb/1", item.ImageTags["Primary"]);
            Assert.Null(item.SeriesId);
            Assert.Null(item.SeriesName);
        });
        Test("Plex 音乐：艺术家专辑 children 与全部歌曲 allLeaves 分别分页", () =>
        {
            using var fixture = new Fixture();
            var albums = fixture.Read(Query("100", EmbyItemType.MusicArtist, EmbyItemType.MusicAlbum));
            var tracks = fixture.Read(Query("100", EmbyItemType.MusicArtist, EmbyItemType.Audio));
            Assert.Equal(EmbyItemType.MusicAlbum, albums.Items.Single().Type);
            Assert.Equal(EmbyItemType.Audio, tracks.Items.Single().Type);
            Assert.Equal("/proxy/library/metadata/100/children,/proxy/library/metadata/100/allLeaves",
                string.Join(',', fixture.Transport.Requests.Select(request => request.Path)));
        });
        Test("Plex 音乐：没有父类型提示时核对 section 与媒体容器，不猜数字 ID", () =>
        {
            using var fixture = new Fixture();
            var page = fixture.Read(new ItemQuery { ParentId = "200", IncludeItemTypes = [EmbyItemType.Audio] });
            Assert.Equal(EmbyItemType.Audio, page.Items.Single().Type);
            Assert.Equal("/proxy/library/sections,/proxy/library/metadata/200,/proxy/library/metadata/200/children",
                string.Join(',', fixture.Transport.Requests.Select(request => request.Path)));
        });
        Test("Plex 音乐：没有父类型提示的库根仍用 section，不读取同号媒体", () =>
        {
            using var fixture = new Fixture();
            fixture.Read(new ItemQuery { ParentId = "3", IncludeItemTypes = [EmbyItemType.Audio] });
            Assert.Equal("/proxy/library/sections,/proxy/library/sections/3/all",
                string.Join(',', fixture.Transport.Requests.Select(request => request.Path)));
        });
        Test("Plex 音乐：空末页保持服务端总数，下一页失败与取消不变成空成功", () =>
        {
            using var fixture = new Fixture();
            fixture.Transport.Empty = true;
            var empty = fixture.Read(Query("200", EmbyItemType.MusicAlbum, EmbyItemType.Audio, 3, 2));
            Assert.Equal(0, empty.Items.Count);
            Assert.Equal(3, empty.TotalRecordCount);
            fixture.Transport.Failure = HttpStatusCode.InternalServerError;
            Assert.Throws<PlexApiException>(() => fixture.Read(Query("200", EmbyItemType.MusicAlbum, EmbyItemType.Audio, 3, 2)));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var count = fixture.Transport.Requests.Count;
            Assert.Throws<OperationCanceledException>(() => fixture.Adapter.GetItemsAsync(
                Query("200", EmbyItemType.MusicAlbum, EmbyItemType.Audio), cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(count, fixture.Transport.Requests.Count);
        });
        Test("Plex 音乐：歌单只枚举 audio，进入 items 后保留服务端顺序", () =>
        {
            using var fixture = new Fixture();
            var playlists = fixture.Read(Query("3", EmbyItemType.CollectionFolder, EmbyItemType.Playlist));
            var tracks = fixture.Read(Query("400", EmbyItemType.Playlist, EmbyItemType.Audio, 1, 2));
            Assert.Equal("400", playlists.Items.Single().Id);
            Assert.Equal("/playlists/400/composite/1", playlists.Items.Single().ImageTags["Primary"]);
            Assert.Equal("303,302", string.Join(',', tracks.Items.Select(item => item.Id)));
            Assert.Equal("/proxy/playlists/all", fixture.Transport.Requests[0].Path);
            Assert.Contains("playlistType=audio", fixture.Transport.Requests[0].Query);
            Assert.Equal("/proxy/playlists/400/items", fixture.Transport.Requests[1].Path);
            Assert.Equal("", fixture.Transport.Requests[1].Query);
        });
        Test("Plex 音乐：音轨详情读取原文件、音轨格式与时长，令牌留在头部", () =>
        {
            using var fixture = new Fixture();
            var item = fixture.Adapter.GetItemAsync("300", default).GetAwaiter().GetResult();
            Assert.Equal(EmbyItemType.Audio, item.Type);
            var source = item.MediaSources.Single();
            Assert.Equal("900", source.Id);
            Assert.Equal("flac", source.Container);
            Assert.Equal(900000, source.Bitrate);
            Assert.Equal(TimeSpan.FromSeconds(123).Ticks, source.RunTimeTicks);
            Assert.Equal("https://plex-music.invalid/proxy/library/parts/900/1/file.flac", source.DirectStreamUrl!.AbsoluteUri);
            Assert.DoesNotContain("music-fixture", source.DirectStreamUrl.AbsoluteUri);
            var audio = source.MediaStreams.Single();
            Assert.Equal("Audio", audio.Type);
            Assert.Equal("flac", audio.Codec);
            Assert.Equal(2, audio.Channels);
            Assert.Equal(24, audio.BitDepth);
            Assert.True(source.SupportsDirectPlay);
            Assert.Equal("music-fixture", fixture.Transport.Requests.Single().Token);
        });
        Test("Plex 音乐：视频随机查询不向音乐库发请求", () =>
        {
            using var fixture = new Fixture();
            var items = fixture.Adapter.GetRandomAsync(1, [EmbyItemType.Movie], default).GetAwaiter().GetResult();
            Assert.Equal(EmbyItemType.Movie, items.Single().Type);
            Assert.False(fixture.Transport.Requests.Any(request => request.Path == "/proxy/library/sections/3/all"));
        });
        Test("Plex 音乐：父类型提示不进入 Emby HTTP 查询串", () =>
        {
            var query = Query("200", EmbyItemType.MusicAlbum, EmbyItemType.Audio);
            Assert.DoesNotContain("ParentItemType", query.ToQueryString());
            Assert.NotNull(typeof(ItemQuery).GetProperty("ParentItemType"));
        });
        Test("Plex 音乐：拒绝会静默返回其他类型的混合音乐查询", () =>
        {
            using var fixture = new Fixture();
            var query = new ItemQuery
            {
                ParentId = "3", IncludeItemTypes = [EmbyItemType.Audio, EmbyItemType.MusicAlbum]
            };
            typeof(ItemQuery).GetProperty("ParentItemType")?.SetValue(query, EmbyItemType.CollectionFolder);
            Assert.Throws<NotSupportedException>(() => fixture.Read(query));
            Assert.Equal(0, fixture.Transport.Requests.Count);
        });
    }

    private static ItemQuery Query(string id, string parentType, string type, int start = 0, int limit = 100)
        => new() { ParentId = id, ParentItemType = parentType, IncludeItemTypes = [type], StartIndex = start, Limit = limit };

    private sealed class Fixture : IDisposable
    {
        private readonly PlexHttp _http;
        public Fixture()
        {
            Transport = new Transport();
            _http = new PlexHttp(Transport);
            Adapter = new PlexMediaAdapter(new PlexClient(_http),
                new PlexConnection(new Uri("https://plex-music.invalid/proxy/"), "music-fixture"));
        }
        public Transport Transport { get; }
        public PlexMediaAdapter Adapter { get; }
        public ItemsResult Read(ItemQuery query) => Adapter.GetItemsAsync(query, default).GetAwaiter().GetResult();
        public void Dispose() => _http.Dispose();
    }

    private sealed record Request(string Path, string Query, string Start, string Size, string Token);

    private sealed class Transport : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public bool Empty { get; set; }
        public HttpStatusCode? Failure { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Assert.Equal("plex-music.invalid", uri.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            string Header(string key) => request.Headers.TryGetValues(key, out var values) ? values.Single() : "";
            Requests.Add(new Request(uri.AbsolutePath, Uri.UnescapeDataString(uri.Query),
                Header("X-Plex-Container-Start"), Header("X-Plex-Container-Size"), Header("X-Plex-Token")));
            if (Failure is { } status) return Task.FromResult(new HttpResponseMessage(status));
            var path = uri.AbsolutePath["/proxy".Length..];
            var container = path switch
            {
                "/library/sections" => """{"Directory":[{"key":"1","type":"movie","title":"电影"},{"key":"3","type":"artist","title":"音乐"},{"key":"4","type":"photo","title":"照片"}]}""",
                "/library/sections/1/all" => """{"Metadata":[{"ratingKey":"10","type":"movie","title":"电影"}],"totalSize":1}""",
                "/library/sections/3/all" => "{\"totalSize\":18,\"Metadata\":[" + (uri.Query.Contains("type=8", StringComparison.Ordinal) ? Artist
                    : uri.Query.Contains("type=9", StringComparison.Ordinal) ? Album : Track) + "]}",
                "/library/metadata/200" => "{\"Metadata\":[" + Album + "]}",
                "/library/metadata/100" => "{\"Metadata\":[" + Artist + "]}",
                "/library/metadata/100/children" => "{\"Directory\":[" + Album + "],\"totalSize\":1}",
                "/library/metadata/200/children" => "{\"Track\":[" + (Empty ? "" : Track) + "],\"totalSize\":3}",
                "/library/metadata/100/allLeaves" => "{\"Track\":[" + Track + "],\"totalSize\":1}",
                "/library/metadata/300" => "{\"Track\":[" + Track + "]}",
                "/playlists/all" => """{"Metadata":[{"ratingKey":"400","type":"playlist","playlistType":"audio","title":"歌单","composite":"/playlists/400/composite/1","leafCount":"2"}],"totalSize":1}""",
                "/playlists/400/items" => "{\"Track\":[" + Track.Replace("\"300\"", "\"303\"", StringComparison.Ordinal)
                    + "," + Track.Replace("\"300\"", "\"302\"", StringComparison.Ordinal) + "],\"totalSize\":3}",
                _ => throw new InvalidOperationException("Unexpected music request: " + path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"MediaContainer\":" + container + "}") });
        }

        private const string Artist = """{"ratingKey":"100","type":"artist","title":"艺术家","thumb":"/library/metadata/100/thumb/1"}""";
        private const string Album = """{"ratingKey":"200","type":"album","title":"专辑","parentRatingKey":"100","parentTitle":"艺术家","year":2025,"thumb":"/library/metadata/200/thumb/1"}""";
        private const string Track = """
            {"ratingKey":"300","type":"track","title":"歌曲","parentRatingKey":"200","parentTitle":"专辑",
            "grandparentRatingKey":"100","grandparentTitle":"艺术家","parentThumb":"/library/metadata/200/thumb/1",
            "duration":"123000","index":"3","parentIndex":"2","Media":[{"duration":"123000","bitrate":"900","container":"flac",
            "Part":[{"id":"900","key":"/library/parts/900/1/file.flac","file":"/music/song.flac","size":"1000",
            "Stream":[{"id":"901","streamType":"2","codec":"flac","channels":"2","bitDepth":"24","default":"1"}]}]}]}
            """;
    }
}

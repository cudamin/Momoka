using System.Net;
using System.Text;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Plex;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class PlexMediaTests
{
    private static void RegisterLibraries()
    {
        Test("Plex 库 ID：GetViews 用 section key 区分电影库和剧集库", () =>
        {
            using var fixture = new LibraryFixture();
            var libraries = fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult();

            Assert.Equal("1,2", string.Join(',', libraries.Select(library => library.Id)));
            Assert.Equal("movies,tvshows", string.Join(',', libraries.Select(library => library.CollectionType)));
            Assert.True(libraries.All(library => library.Type == EmbyItemType.CollectionFolder && library.IsFolder));
            Assert.False(HomeLayout.LibraryKey(libraries[0]) == HomeLayout.LibraryKey(libraries[1]));
        });
        Test("Plex 库 ID：section key 优先于 ratingKey，数字 key 仍可读取", () =>
        {
            using var fixture = new LibraryFixture();
            fixture.Transport.Sections = """
                {"Directory":[
                    {"key":"1","ratingKey":"99","title":"电影","type":"movie"},
                    {"key":2,"ratingKey":"98","title":"剧集","type":"show"}]}
                """;
            var libraries = fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult();

            Assert.Equal("1,2", string.Join(',', libraries.Select(library => library.Id)));
        });
        Test("Plex 库 ID：缺失、null、空白 key 拒绝返回空 ID 库", () =>
        {
            foreach (var invalid in new[]
            {
                """{"title":"无标识","type":"movie","ratingKey":"99"}""",
                """{"key":null,"title":"无标识","type":"show","ratingKey":"99"}""",
                """{"key":"","title":"无标识","type":"movie"}""",
                """{"key":"  ","title":"无标识","type":"show"}"""
            })
            {
                using var fixture = new LibraryFixture();
                fixture.Transport.Sections = "{\"Directory\":[{\"key\":\"1\",\"title\":\"电影\",\"type\":\"movie\"}," + invalid + "]}";

                Assert.Throws<InvalidDataException>(() => fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult());
                Assert.Equal(1, fixture.Transport.Requests.Count);
            }
        });
        Test("Plex 库 ID：GetViews 产物直接进入两库浏览，媒体条目仍用 ratingKey", () =>
        {
            using var fixture = new LibraryFixture();
            var libraries = fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult();
            var items = new List<EmbyItem>();
            foreach (var library in libraries)
            {
                var page = fixture.Adapter.GetItemsAsync(new ItemQuery { ParentId = library.Id, Limit = 12 }, default)
                    .GetAwaiter().GetResult();
                items.AddRange(page.Items);
            }

            Assert.Equal("10,30", string.Join(',', items.Select(item => item.Id)));
            Assert.Equal("Movie,Series", string.Join(',', items.Select(item => item.Type)));
            Assert.Equal("/library/sections,/library/sections/1/all,/library/sections/2/all",
                string.Join(',', fixture.Transport.Requests.Select(uri => uri.AbsolutePath)));
        });
        Test("Plex 库 ID：GetViews 产物保留两排最近添加并使用各自 section", () =>
        {
            using var fixture = new LibraryFixture();
            var libraries = fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult();
            var rows = HomeLayout.Plan(null, libraries).Where(row => HomeLayout.LibraryId(row.Key) is not null).ToList();
            Assert.Equal(2, rows.Count);

            var items = new List<EmbyItem>();
            foreach (var row in rows)
                items.AddRange(fixture.Adapter.GetLatestAsync(HomeLayout.LibraryId(row.Key), 12, null, default)
                    .GetAwaiter().GetResult());

            Assert.Equal("10,30", string.Join(',', items.Select(item => item.Id)));
            Assert.Equal("/library/sections,/library/sections/1/recentlyAdded,/library/sections/2/recentlyAdded",
                string.Join(',', fixture.Transport.Requests.Select(uri => uri.AbsolutePath)));
        });
        Test("Plex 库 ID：随机轮播从 GetViews 产物查询两个 section", () =>
        {
            using var fixture = new LibraryFixture();
            var libraries = fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult();
            var items = fixture.Adapter.GetRandomAsync(2, [EmbyItemType.Movie, EmbyItemType.Series], default)
                .GetAwaiter().GetResult();
            var requests = fixture.Transport.Requests.Where(uri => uri.AbsolutePath.EndsWith("/all", StringComparison.Ordinal)).ToList();

            Assert.Equal("10,30", string.Join(',', items.Select(item => item.Id).Order(StringComparer.Ordinal)));
            Assert.Equal(string.Join(',', libraries.Select(library => $"/library/sections/{library.Id}/all")),
                string.Join(',', requests.Select(uri => uri.AbsolutePath)));
            Assert.True(requests.All(uri => Uri.UnescapeDataString(uri.Query).Contains("sort=random", StringComparison.Ordinal)));
        });
        Test("Plex 库 ID：同来源两库与跨来源同 key 的导航和最近添加互不覆盖", () =>
        {
            using var first = new LibraryFixture();
            using var second = new LibraryFixture(new Uri("http://plex-second-libraries.invalid:32400/"));
            var libraries = BindLibrarySource(first, "first").Concat(BindLibrarySource(second, "second")).ToList();
            var navigation = libraries.ToDictionary(HomeLayout.LibraryKey, StringComparer.Ordinal);
            var rows = HomeLayout.Plan(null, libraries).Where(row => HomeLayout.LibraryId(row.Key) is not null).ToList();

            Assert.Equal(4, navigation.Count);
            Assert.Equal(4, rows.Count);
            Assert.Equal("1,2,1,2", string.Join(',', rows.Select(row => HomeLayout.LibraryId(row.Key))));
            Assert.Equal("1,2,1,2", string.Join(',', navigation.Values.Select(library => library.Id)));
        });
    }

    // Bind the same source metadata as the browse session, without login, settings or a live session.
    private static List<EmbyItem> BindLibrarySource(LibraryFixture fixture, string serverId)
    {
        var connection = new EmbyConnection(fixture.ApiBase, "library-fixture", "user", "user", "fixture",
            DeviceIdentity.Create("plex-library-fixture", "test"))
        { ServerKind = MediaServerKind.Plex };
        var scope = new EmbySessionScope(null!, new EmbySessionScope.Identity(new EmbyClient(null!, connection),
            new ServerProfile { Id = serverId, Name = "fixture" },
            new AccountProfile { Id = "account", Username = "fixture" }));
        return scope.Bind(fixture.Adapter.GetViewsAsync(default).GetAwaiter().GetResult());
    }

    private sealed class LibraryFixture : IDisposable
    {
        private readonly PlexHttp _http;

        public LibraryFixture(Uri? apiBase = null)
        {
            ApiBase = apiBase ?? new Uri("http://plex-libraries.invalid:32400/");
            Transport = new LibraryTransport(ApiBase);
            _http = new PlexHttp(Transport);
            Adapter = new PlexMediaAdapter(new PlexClient(_http), new PlexConnection(ApiBase, "library-fixture"));
        }

        public Uri ApiBase { get; }
        public LibraryTransport Transport { get; }
        public PlexMediaAdapter Adapter { get; }
        public void Dispose() => _http.Dispose();
    }

    private sealed class LibraryTransport(Uri apiBase) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public string Sections { get; set; } = """
            {"Directory":[
                {"key":"1","title":"电影","type":"movie"},
                {"key":"2","title":"剧集","type":"show"},
                {"key":"3","title":"音乐","type":"artist"},
                {"key":"4","title":"照片","type":"photo"}]}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.Equal(apiBase.GetLeftPart(UriPartial.Authority), uri.GetLeftPart(UriPartial.Authority));
            Assert.Equal(HttpMethod.Get, request.Method);
            Requests.Add(uri);

            var container = uri.AbsolutePath switch
            {
                "/library/sections" => Sections,
                "/library/sections/1/all" or "/library/sections/1/recentlyAdded" =>
                    """{"size":1,"totalSize":1,"Metadata":[{"ratingKey":"10","key":"/library/metadata/10","type":"movie","title":"电影"}]}""",
                "/library/sections/2/all" or "/library/sections/2/recentlyAdded" =>
                    """{"size":1,"totalSize":1,"Directory":[{"ratingKey":"30","key":"/library/metadata/30","type":"show","title":"剧集"}]}""",
                _ => throw new InvalidOperationException("Unexpected library request: " + uri.AbsolutePath)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"MediaContainer\":" + container + "}", Encoding.UTF8, "application/json")
            });
        }
    }
}

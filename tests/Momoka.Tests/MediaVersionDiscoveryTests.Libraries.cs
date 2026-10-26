using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class MediaVersionDiscoveryTests
{
    private static void RegisterLibraryCases()
    {
        Case("聚合版本：单来源也查找其他库，保留本地顺序并拒绝重复与错片", async () =>
        {
            using var fixture = new Fixture();
            var scope = await fixture.AddAsync("a", "one.invalid");
            var original = scope.Bind(Movie());
            var sibling = LibraryMovie("other-library-item");
            var wrong = LibraryMovie("wrong-item");
            wrong.ProviderIds["Tmdb"] = "99";
            fixture.Transport.Override = request => LibraryReply(request, [Movie(), sibling, wrong]);

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, original, MediaSourceOrder.Default, default);

            Assert.Equal(2, result.Versions.Count);
            Assert.True(ReferenceEquals(original.MediaSources[0], result.Versions[0].Source));
            Assert.Equal(sibling.Id, result.Versions[1].Item.Id);
            Assert.Equal(scope.Key, result.Versions[1].Item.Source?.Key);
            Assert.False(result.Versions[1].IsPlaying(original, original.MediaSources[0]), "不同库允许媒体源编号相同");
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("聚合版本：合并版本候选别名指向同一详情时不重复已有或其他库版本", async () =>
        {
            using var fixture = new Fixture();
            var scope = await fixture.AddAsync("a", "one.invalid");
            var sibling = LibraryMovie("other-item");
            var aliases = new[] { LibraryMovie("local-alias"), LibraryMovie("remote-alias-one"), LibraryMovie("remote-alias-two") };
            fixture.Transport.Override = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/Items", StringComparison.Ordinal)) return CatalogReply(aliases);
                return JsonReply(path.EndsWith("/local-alias", StringComparison.Ordinal) ? Movie() : sibling);
            };

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, scope.Bind(Movie()), MediaSourceOrder.Default, default);

            Assert.Equal(2, result.Versions.Count);
            Assert.Equal(sibling.Id, result.Versions[1].Item.Id);
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("聚合版本：关闭后不查询本来源的其他库", async () =>
        {
            using var fixture = new Fixture(aggregateMode: false);
            var scope = await fixture.AddAsync("a", "one.invalid");
            var queries = 0;
            fixture.Transport.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal)) queries++;
                return LibraryReply(request, [Movie(), LibraryMovie("other-library-item")]);
            };

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, scope.Bind(Movie()), MediaSourceOrder.Default, default);

            Assert.Equal(1, result.Versions.Count);
            Assert.Equal(0, queries);
        });

        Case("聚合版本：同服务器不同账号仍查各自其他库，不因同条目命中提前结束", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            var second = await fixture.AddAsync("b", "one.invalid");
            fixture.Transport.Override = request => LibraryReply(request, [Movie(), LibraryMovie("other-library-item")]);

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, first.Bind(Movie()), MediaSourceOrder.Default, default);

            Assert.Equal(4, result.Versions.Count);
            Assert.Equal(2, result.Versions.Count(version => version.Item.Source?.Key == first.Key));
            Assert.Equal(2, result.Versions.Count(version => version.Item.Source?.Key == second.Key));
            Assert.Equal(4, result.Versions.Select(version => version.Item.IdentityKey).Distinct().Count());
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("聚合版本：另一账号看不到原条目时仍按标识查找它可访问的库", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            var second = await fixture.AddAsync("b", "one.invalid");
            fixture.Transport.Override = request =>
            {
                var other = request.Headers.GetValues("X-Emby-Token").Single() == "offline-b";
                if (other && request.RequestUri!.AbsolutePath.EndsWith("/same-item", StringComparison.Ordinal))
                    return new(HttpStatusCode.NotFound);
                return LibraryReply(request, other ? [LibraryMovie("accessible-item")] : [Movie()]);
            };

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, first.Bind(Movie()), MediaSourceOrder.Default, default);

            Assert.Equal(2, result.Versions.Count);
            Assert.Equal("accessible-item", result.Versions[1].Item.Id);
            Assert.Equal(second.Key, result.Versions[1].Item.Source?.Key);
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("聚合版本：其他库的单集按同剧同季同集匹配", async () =>
        {
            using var fixture = new Fixture();
            var scope = await fixture.AddAsync("a", "one.invalid");
            var parent = Movie();
            parent.Type = EmbyItemType.Series;
            parent.Id = "show";
            var siblingParent = Movie();
            siblingParent.Type = EmbyItemType.Series;
            siblingParent.Id = "other-show";
            var sibling = Episode();
            sibling.Id = "other-episode";
            sibling.SeriesId = siblingParent.Id;
            var wrong = Episode();
            wrong.Id = "wrong-episode";
            wrong.IndexNumber = 2;
            fixture.Transport.Override = request => request.RequestUri!.AbsolutePath switch
            {
                var path when path.EndsWith("/Shows/show/Episodes", StringComparison.Ordinal) => CatalogReply([Episode()]),
                var path when path.EndsWith("/Episodes", StringComparison.Ordinal) => CatalogReply([sibling, wrong]),
                var path when path.EndsWith("/other-episode", StringComparison.Ordinal) => JsonReply(sibling),
                _ => LibraryReply(request, [parent, siblingParent])
            };

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, scope.Bind(Episode()), MediaSourceOrder.Default, default);

            Assert.Equal(2, result.Versions.Count);
            Assert.Equal(sibling.Id, result.Versions[1].Item.Id);
            Assert.Equal(siblingParent.Id, result.Versions[1].Parent?.Id);
            Assert.Equal(scope.Key, result.Versions[1].Parent?.Source?.Key);
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("聚合版本：标识查询不受支持时回退标题搜索，仍严格核对标识", async () =>
        {
            using var fixture = new Fixture();
            var first = await fixture.AddAsync("a", "one.invalid");
            var second = await fixture.AddAsync("b", "two.invalid");
            var sibling = LibraryMovie("remote-item");
            fixture.Transport.Override = request => request.RequestUri!.Query.Contains("AnyProviderIdEquals", StringComparison.Ordinal)
                ? new(HttpStatusCode.NotFound) : LibraryReply(request, [sibling]);

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, first.Bind(Movie()), MediaSourceOrder.Default, default);

            Assert.True(result.Versions.Any(version => version.Item.Source?.Key == second.Key && version.Item.Id == sibling.Id));
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("聚合版本：Jellyfin 同账号的其他库可发现，仍使用标准鉴权并去重", async () =>
        {
            using var fixture = new Fixture();
            var sibling = LibraryMovie("jellyfin-other-library");
            fixture.Transport.Override = request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                    return JsonReply(new { ProductName = "Jellyfin", Version = "10.10.7", Id = "jellyfin" });
                if (path.EndsWith("/Users/Me", StringComparison.Ordinal))
                    return JsonReply(new EmbyUser { Id = "a", Name = "a", Policy = new() { IsAdministrator = true } });
                if (path.Contains("/Items", StringComparison.Ordinal))
                {
                    Assert.True(request.Headers.TryGetValues("Authorization", out var headers)
                        && headers.Single().Contains("MediaBrowser", StringComparison.Ordinal));
                    Assert.False(request.Headers.Contains("X-Emby-Token"));
                    Assert.DoesNotContain("AnyProviderIdEquals", request.RequestUri.Query);
                }
                return LibraryReply(request, [Movie(), sibling]);
            };
            var scope = await fixture.AddAsync("a", "jellyfin.invalid", MediaServerKind.Jellyfin);

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, scope.Bind(Movie()), MediaSourceOrder.Default, default);

            Assert.True(scope.Connection.IsJellyfin);
            Assert.Equal(2, result.Versions.Count);
            Assert.Equal(sibling.Id, result.Versions[1].Item.Id);
            Assert.Equal(scope.Key, result.Versions[1].Item.Source?.Key);
            Assert.Equal(0, result.FailedSources.Count);
        });

        Case("聚合版本：不同库各只含一种刮削标识时不因首次命中漏掉后续版本", async () =>
        {
            using var fixture = new Fixture();
            var scope = await fixture.AddAsync("a", "one.invalid");
            var original = scope.Bind(Movie());
            original.ProviderIds["Imdb"] = "tt1895587";
            var tmdb = LibraryMovie("tmdb-only");
            var imdb = LibraryMovie("imdb-only");
            imdb.ProviderIds = new() { ["Imdb"] = "tt1895587" };
            fixture.Transport.Override = request =>
            {
                if (request.RequestUri!.Query.Contains("AnyProviderIdEquals", StringComparison.Ordinal))
                    return CatalogReply(request.RequestUri.Query.Contains("Tmdb.", StringComparison.Ordinal) ? [tmdb] : [imdb]);
                if (request.RequestUri.AbsolutePath.EndsWith("/Items", StringComparison.Ordinal)) return CatalogReply([tmdb]);
                return LibraryReply(request, [tmdb, imdb]);
            };

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, original, MediaSourceOrder.Default, default);

            Assert.Equal(3, result.Versions.Count);
            Assert.Equal(3, result.Versions.Select(version => version.Item.Id).Distinct().Count());
            Assert.Equal(0, result.FailedSources.Count);
        });

        Test("飞牛聚合标识：映射官方 IMDb 字段，空值与内部 trim_id 不冒充跨服标识", () =>
        {
            var item = FnNasMediaAdapter.Item(new JsonObject
            {
                ["guid"] = "native-movie",
                ["type"] = "Movie",
                ["imdb_id"] = "  tt1895587  ",
                ["trim_id"] = "tm314365"
            });
            Assert.Equal("tt1895587", item.ProviderIds.GetValueOrDefault("Imdb"));
            Assert.Equal(1, item.ProviderIds.Count);
            foreach (var empty in new string?[] { null, "", "   " })
            {
                var missing = FnNasMediaAdapter.Item(new JsonObject { ["type"] = "Movie", ["imdb_id"] = empty, ["trim_id"] = "tm314365" });
                Assert.Equal(0, missing.ProviderIds.Count);
            }
        });

        Case("聚合版本：飞牛单集无 tv_guid 时沿季取所属剧，再按同季同集发现 Emby 版本", async () =>
        {
            using var fixture = new Fixture();
            var lookedUpSeason = false;
            var parent = Movie();
            parent.Type = EmbyItemType.Series;
            parent.Id = "show";
            parent.ProviderIds["Imdb"] = "tt2359704";
            fixture.Transport.Override = request =>
            {
                if (request.RequestUri!.Host != "fnnas.invalid")
                {
                    var path = request.RequestUri.AbsolutePath;
                    if (path.EndsWith("/Episodes", StringComparison.Ordinal)) return CatalogReply([Episode()]);
                    if (path.EndsWith("/episode", StringComparison.Ordinal)) return JsonReply(Episode());
                    return LibraryReply(request, [parent]);
                }
                var data = request.RequestUri.AbsolutePath switch
                {
                    "/v/api/v1/item/native-episode" => "{\"guid\":\"native-episode\",\"title\":\"第一集\",\"type\":\"Episode\",\"parent_guid\":\"native-season\",\"season_number\":1,\"episode_number\":1}",
                    "/v/api/v1/item/native-season" => "{\"guid\":\"native-season\",\"type\":\"Season\",\"parent_guid\":\"native-show\"}",
                    "/v/api/v1/item/native-show" => "{\"guid\":\"native-show\",\"title\":\"电影\",\"type\":\"TV\",\"imdb_id\":\"tt2359704\"}",
                    "/v/api/v1/search/list" => "[{\"guid\":\"native-show\",\"title\":\"电影\",\"type\":\"TV\",\"imdb_id\":\"tt2359704\"}]",
                    "/v/api/v1/season/list/native-show" => "[{\"guid\":\"native-season\",\"type\":\"Season\",\"season_number\":1}]",
                    "/v/api/v1/episode/list/native-season" => "[{\"guid\":\"native-episode\",\"title\":\"第一集\",\"type\":\"Episode\",\"season_number\":1,\"episode_number\":1}]",
                    "/v/api/v1/stream/list/native-episode" => "{\"files\":[{\"guid\":\"same-source\",\"path\":\"/media/episode.mkv\"}]}",
                    _ => null
                };
                if (request.RequestUri.AbsolutePath == "/v/api/v1/item/native-season") lookedUpSeason = true;
                return data is null ? MixedServerReply(request) : FnNasJsonReply(data);
            };
            var emby = await fixture.AddAsync("a", "one.invalid");
            var fnnas = await fixture.AddAsync("b", "fnnas.invalid", MediaServerKind.FnNas);
            var episode = await fnnas.ExecuteAsync((client, token) => client.GetItemAsync("native-episode", token), default);
            Assert.True(string.IsNullOrEmpty(episode.SeriesId));

            var result = await MediaVersionDiscovery.FindAsync(fixture.Session, episode, MediaSourceOrder.Default, default);

            Assert.True(lookedUpSeason);
            Assert.Equal(2, result.Versions.Count);
            var remote = result.Versions.Single(version => version.Item.Source?.Key == emby.Key);
            Assert.Equal(parent.Id, remote.Parent?.Id);
            Assert.Equal(1, remote.Item.ParentIndexNumber);
            Assert.Equal(1, remote.Item.IndexNumber);
            Assert.Equal(0, result.FailedSources.Count);
        });

        foreach (var fromFnNas in new[] { false, true })
        {
            Case($"聚合版本：{(fromFnNas ? "飞牛进入 Emby" : "Emby 进入飞牛")}按 IMDb 双向发现并拒绝同名错片", async () =>
            {
                using var fixture = new Fixture();
                fixture.Transport.Override = MixedServerReply;
                var emby = await fixture.AddAsync("a", "one.invalid");
                var fnnas = await fixture.AddAsync("b", "fnnas.invalid", MediaServerKind.FnNas);
                var scope = fromFnNas ? fnnas : emby;
                var item = await scope.ExecuteAsync((client, token) => client.GetItemAsync(fromFnNas ? "native-movie" : "same-item", token), default);

                var result = await MediaVersionDiscovery.FindAsync(fixture.Session, item, MediaSourceOrder.Default, default);

                Assert.Equal(2, result.Versions.Count);
                Assert.Equal(2, result.Versions.Select(version => version.Item.Source?.Key).Distinct().Count());
                Assert.True(ReferenceEquals(item.MediaSources[0], result.Versions[0].Source));
                Assert.False(result.Versions.Any(version => version.Item.Id == "wrong-native"));
                Assert.Equal(0, result.FailedSources.Count);
            });
        }
    }

    private static EmbyItem LibraryMovie(string id)
    {
        var item = Movie();
        item.Id = id;
        item.ParentId = "another-library";
        return item;
    }

    private static HttpResponseMessage? LibraryReply(HttpRequestMessage request, IReadOnlyList<EmbyItem> catalog)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/Items", StringComparison.Ordinal)) return CatalogReply(catalog);
        var id = path[(path.LastIndexOf('/') + 1)..];
        return catalog.FirstOrDefault(item => item.Id == id) is { } detail ? JsonReply(detail) : null;
    }

    private static HttpResponseMessage CatalogReply(IReadOnlyList<EmbyItem> items) =>
        JsonReply(new ItemsResult { Items = [.. items], TotalRecordCount = items.Count });

    private static HttpResponseMessage JsonReply<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage? MixedServerReply(HttpRequestMessage request)
    {
        if (request.RequestUri!.Host != "fnnas.invalid")
        {
            var item = Movie();
            item.ProviderIds["Imdb"] = "tt1895587";
            return LibraryReply(request, [item]);
        }
        if (request.RequestUri.AbsolutePath.EndsWith("/System/Info/Public", StringComparison.Ordinal))
            return new(HttpStatusCode.NotFound);
        const string movie = "{\"guid\":\"native-movie\",\"title\":\"电影\",\"type\":\"Movie\",\"imdb_id\":\"tt1895587\",\"trim_id\":\"tm314365\",\"release_date\":\"2020-01-01\"}";
        var data = request.RequestUri.AbsolutePath switch
        {
            "/v/api/v1/user/info" => "{\"guid\":\"b\",\"username\":\"b\",\"is_admin\":1}",
            "/v/api/v1/sys/config" => "{\"server_guid\":\"fnnas\",\"server_name\":\"fnnas.invalid\"}",
            "/v/api/v1/sys/version" => "{\"version\":\"0.9.8\",\"mediasrvVersion\":\"0.8.43\"}",
            "/v/api/v1/mediadb/list" => "[]",
            "/v/api/v1/search/list" => "[" + movie + ",{\"guid\":\"wrong-native\",\"title\":\"电影\",\"type\":\"Movie\",\"imdb_id\":\"tt9999999\",\"release_date\":\"2020-01-01\"}]",
            "/v/api/v1/item/native-movie" => movie,
            "/v/api/v1/stream/list/native-movie" => "{\"files\":[{\"guid\":\"same-source\",\"path\":\"/media/movie.mkv\"}]}",
            "/v/api/v1/play/info" => "{\"ts\":0,\"duration\":100}",
            _ => throw new InvalidOperationException("Unexpected mixed-source fixture path: " + request.RequestUri.AbsolutePath)
        };
        return FnNasJsonReply(data);
    }

    private static HttpResponseMessage FnNasJsonReply(string data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"code\":0,\"data\":" + data + "}", Encoding.UTF8, "application/json")
    };
}

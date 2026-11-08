using System.Net;
using System.Text.Json.Nodes;
using Momoka.Emby;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FnNasMediaTests
{
    private static void RegisterDetailLists()
    {
        Test("飞牛详情：单集缺剧标识时经父季恢复全剧范围和演职人员", () =>
        {
            using var fixture = DetailListsFixture();
            var detail = fixture.Session.Client.GetItemAsync("episode96", default).GetAwaiter().GetResult();
            Assert.Equal("series", detail.SeriesId);
            Assert.Equal("season", detail.SeasonId);
            Assert.Equal("episode96", detail.Id);
            var scope = ItemDetail.EpisodeScope(detail, null);
            var episodes = fixture.Session.Client.GetEpisodesAsync(scope.SeriesId, null, default).GetAwaiter().GetResult();
            Assert.Equal("episode1,episode2", string.Join(',', episodes.Select(item => item.Id)));
            Assert.Equal("person-a", detail.People.First().Id);
            Assert.True(fixture.Transport.Paths.Contains("/v/api/v1/item/season"));
            Assert.True(fixture.Transport.Paths.Contains("/v/api/v1/person/list/season"));
            Assert.False(fixture.Transport.Paths.Contains("/v/api/v1/season/list/episode96"));
        });
        Test("飞牛详情：季的 parent_guid 恢复父剧，已有 tv_guid 的集不补取父季", () =>
        {
            using var fixture = DetailListsFixture();
            var season = fixture.Session.Client.GetItemAsync("season", default).GetAwaiter().GetResult();
            Assert.Equal("series", season.SeriesId);
            fixture.Transport.Paths.Clear();
            var episode = fixture.Session.Client.GetItemAsync("episode-known", default).GetAwaiter().GetResult();
            Assert.Equal("series", episode.SeriesId);
            Assert.False(fixture.Transport.Paths.Contains("/v/api/v1/item/season"));
        });
        Test("飞牛单集人员：季名单为空时回退父剧名单", () =>
        {
            using var fixture = DetailListsFixture();
            var previous = fixture.Transport.Override;
            fixture.Transport.Override = (request, token) => request.RequestUri!.AbsolutePath == "/v/api/v1/person/list/season"
                ? Task.FromResult(DetailEnvelope("""{"total":0,"list":[]}""")) : previous!(request, token);
            var item = fixture.Session.Client.GetItemAsync("episode96", default).GetAwaiter().GetResult();
            Assert.Equal("person-a", item.People.First().Id);
            Assert.True(fixture.Transport.Paths.Contains("/v/api/v1/person/list/series"));
        });
        Test("飞牛剧人员：父剧名单为空时借最新正片季名单", () =>
        {
            using var fixture = DetailListsFixture();
            var previous = fixture.Transport.Override;
            fixture.Transport.Override = (request, token) => request.RequestUri!.AbsolutePath == "/v/api/v1/person/list/series"
                ? Task.FromResult(DetailEnvelope("""{"total":0,"list":[]}""")) : previous!(request, token);
            var series = fixture.Session.Client.GetItemAsync("series", default).GetAwaiter().GetResult();
            Assert.Equal("person-a", series.People.First().Id);
            Assert.True(fixture.Transport.Paths.Contains("/v/api/v1/person/list/season"));
        });
        Test("飞牛详情：人员使用独立分页接口并保留角色职业和头像", () =>
        {
            using var fixture = DetailListsFixture();
            var movie = fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult();
            var cast = ItemDetail.Cast(movie);
            Assert.Equal("演员甲,导演乙,编剧丙", string.Join(',', cast.Select(credit => credit.Card.Name)));
            Assert.Equal("主角,导演,编剧", string.Join(',', cast.Select(credit => credit.Credit)));
            Assert.Equal("/portrait/a.webp", cast[0].Card.ImageTags["Primary"]);
            var body = fixture.Transport.Bodies.Last();
            Assert.Equal(1, body["page"]!.GetValue<int>());
            Assert.Equal(200, body["page_size"]!.GetValue<int>());
            Assert.Equal(2, body.Count, "guid 仅在路径中，不能混进人员请求正文");
        });
        // 2026-11-08 用户问详情页那行「1．10·2·9」：item.genres 是 ID，名字要问 tag/genres。
        Test("飞牛详情：题材经 tag/genres 换成名字，一次会话只读一趟", () =>
        {
            using var fixture = DetailListsFixture();
            var detail = fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult();
            // 夹具的 movie 是 genres:["invalid",2]，映射表只有 2→动画：认得的换名字，认不得的原样留着。
            Assert.Equal("invalid,动画", string.Join(',', detail.Genres));
            Assert.Equal("invalid,2", string.Join(',', detail.NativeGenres));
            Assert.True(fixture.Transport.Paths.Contains("/v/api/v1/tag/genres"));

            fixture.Transport.Paths.Clear();
            _ = fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult();
            Assert.False(fixture.Transport.Paths.Contains("/v/api/v1/tag/genres"), "映射表不该每次翻页、开详情都问一遍");
        });
        Test("飞牛详情：题材映射表读不到时照常显示条目，题材保留原始 ID", () =>
        {
            using var fixture = DetailListsFixture();
            var previous = fixture.Transport.Override;
            fixture.Transport.Override = (request, token) => request.RequestUri!.AbsolutePath == "/v/api/v1/tag/genres"
                ? Task.FromResult(DetailEnvelope("null", -4)) : previous!(request, token);
            var detail = fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult();
            Assert.Equal("movie", detail.Id);
            Assert.Equal("invalid,2", string.Join(',', detail.Genres), "读不到名字就写原值，不能吞掉题材");
        });
        Test("飞牛更多类似：同类型题材评分排序，排除自身与重复，单集推荐父剧同类", () =>
        {
            using var fixture = DetailListsFixture();
            var similar = fixture.Session.Client.GetSimilarAsync("movie", 2, default).GetAwaiter().GetResult();
            Assert.Equal("related-a,related-b", string.Join(',', similar.Select(item => item.Id)));
            var query = fixture.Transport.Bodies.Last();
            Assert.Equal("Movie", query["tags"]!["type"]![0]!.GetValue<string>());
            Assert.Equal(2, query["tags"]!["genres"]!.GetValue<int>());
            Assert.Equal("vote_average", query["sort_column"]!.GetValue<string>());
            Assert.Equal("DESC", query["sort_type"]!.GetValue<string>());
            similar = fixture.Session.Client.GetSimilarAsync("episode96", 2, default).GetAwaiter().GetResult();
            Assert.Equal("related-a,related-b", string.Join(',', similar.Select(item => item.Id)));
            query = fixture.Transport.Bodies.Last();
            Assert.Equal("TV", query["tags"]!["type"]![0]!.GetValue<string>());
            Assert.Equal(7, query["tags"]!["genres"]!.GetValue<int>());
            fixture.Transport.Paths.Clear();
            Assert.Equal(0, fixture.Session.Client.GetSimilarAsync("movie", 0, default).GetAwaiter().GetResult().Count);
            Assert.Equal(0, fixture.Transport.Paths.Count);
        });
        Test("飞牛详情：人员身份失败和取消不得降为空列表", () =>
        {
            foreach (var code in new[] { -2, -5, -3 })
            {
                using var fixture = DetailListsFixture();
                var previous = fixture.Transport.Override;
                fixture.Transport.Override = (request, token) => request.RequestUri!.AbsolutePath.Contains("/person/list/", StringComparison.Ordinal)
                    ? Task.FromResult(DetailEnvelope("null", code)) : previous!(request, token);
                var error = Assert.Catch<FnNasApiException>(() => fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult());
                Assert.Equal(code, error.Code);
            }
            using var cancelled = DetailListsFixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => cancelled.Session.Client.GetItemAsync("movie", cancellation.Token).GetAwaiter().GetResult());
        });
        Test("飞牛人员作品：合并已确认的四种职业，去重后分页", () =>
        {
            using var fixture = DetailListsFixture();
            var previous = fixture.Transport.Override;
            fixture.Transport.Bodies.Clear();
            fixture.Transport.Override = (request, token) =>
            {
                // 人物作品也走 GetItemsAsync：映射前的题材映射表不属于这一条协议，交回夹具。
                if (request.RequestUri!.AbsolutePath == "/v/api/v1/tag/genres") return previous!(request, token);
                Assert.Equal("/v/api/v1/person/item/list", request.RequestUri!.AbsolutePath);
                var body = fixture.Transport.Bodies.Last();
                Assert.Equal("person-a", body["person_guid"]!.GetValue<string>());
                Assert.Equal("update_time", body["sort_column"]!.GetValue<string>());
                var job = body["job"]!.GetValue<string>();
                return Task.FromResult(DetailEnvelope(job == "Director"
                    ? """{"total":2,"list":[{"guid":"work-a","title":"甲","type":"Movie"},{"guid":"work-b","title":"乙","type":"TV"}]}"""
                    : """{"total":1,"list":[{"guid":"work-a","title":"甲","type":"Movie"}]}"""));
            };
            var page = fixture.Session.Client.GetItemsAsync(new ItemQuery
            { PersonId = "person-a", SortBy = EmbySortBy.Name, StartIndex = 1, Limit = 1 }, default).GetAwaiter().GetResult();
            Assert.Equal(2, page.TotalRecordCount);
            Assert.Equal("work-b", page.Items.Single().Id);
            Assert.Equal("Actor,Director,Screenplay,Producer", string.Join(',', fixture.Transport.Bodies.Select(body => body["job"]!.GetValue<string>())));
        });
        Test("飞牛人员作品：不支持的筛选不能显示成功而忽略条件", () =>
        {
            using var fixture = DetailListsFixture();
            var filters = new FnNasFilterSelection { Watched = "1" };
            Assert.Throws<NotSupportedException>(() => fixture.Session.Client.GetItemsAsync(new ItemQuery
            { PersonId = "person-a", FnNasFilters = filters }, default).GetAwaiter().GetResult());
        });
        Test("飞牛人员：取得展示上限后不读取多余页，文件读取不请求人员", () =>
        {
            using var fixture = DetailListsFixture();
            var previous = fixture.Transport.Override;
            var reads = 0;
            fixture.Transport.Override = (request, token) =>
            {
                if (!request.RequestUri!.AbsolutePath.Contains("/person/list/", StringComparison.Ordinal)) return previous!(request, token);
                reads++;
                return Task.FromResult(DetailEnvelope(new JsonObject
                {
                    ["total"] = 300,
                    ["list"] = new JsonArray(Enumerable.Range(0, 200).Select(index => (JsonNode)new JsonObject
                    { ["person_guid"] = "person-" + index, ["name"] = "演员" + index, ["job"] = "Actor" }).ToArray())
                }.ToJsonString()));
            };
            var movie = fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult();
            Assert.Equal(ItemDetail.CastLimit, ItemDetail.Cast(movie).Count);
            Assert.Equal(1, reads, "40张人物卡已齐备，不应继续取几百人");
            reads = 0;
            fixture.Session.Client.GetItemAsync("movie", default, EmbyFields.Files).GetAwaiter().GetResult();
            Assert.Equal(0, reads, "文件/字幕读取不依赖演职人员");
        });
        Test("飞牛详情：缺父季与畸形人员表保留明确错误", () =>
        {
            using var fixture = DetailListsFixture();
            var previous = fixture.Transport.Override;
            fixture.Transport.Override = (request, token) => request.RequestUri!.AbsolutePath == "/v/api/v1/item/episode96"
                ? Task.FromResult(DetailEnvelope("""{"guid":"episode96","type":"Episode"}""")) : previous!(request, token);
            var error = Assert.Catch<FnNasApiException>(() => fixture.Session.Client.GetItemAsync("episode96", default).GetAwaiter().GetResult());
            Assert.Contains("所属季", error.Message);
            fixture.Transport.Override = (request, token) => request.RequestUri!.AbsolutePath.Contains("/person/list/", StringComparison.Ordinal)
                ? Task.FromResult(DetailEnvelope("""{"total":1,"list":[null]}""")) : previous!(request, token);
            Assert.Throws<FnNasApiException>(() => fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult());
        });
        Test("飞牛人员：重复信用跨页补齐并保留首个角色，次页权限错误不吞掉", () =>
        {
            foreach (var code in new[] { 0, -2, -16 })
            {
                using var fixture = DetailListsFixture();
                var previous = fixture.Transport.Override;
                fixture.Transport.Override = (request, token) =>
                {
                    if (!request.RequestUri!.AbsolutePath.Contains("/person/list/", StringComparison.Ordinal)) return previous!(request, token);
                    var page = fixture.Transport.Bodies.Last()["page"]!.GetValue<int>();
                    if (page == 2) return Task.FromResult(DetailEnvelope("""{"total":201,"list":[{"person_guid":"second","name":"编剧","job":"Screenplay"}]}""", code));
                    return Task.FromResult(DetailEnvelope(new JsonObject
                    {
                        ["total"] = 201,
                        ["list"] = new JsonArray(Enumerable.Range(0, 200).Select(index => (JsonNode)new JsonObject
                        { ["person_guid"] = "first", ["name"] = "演员", ["job"] = "Actor", ["role"] = "角色" + index }).ToArray())
                    }.ToJsonString()));
                };
                if (code != 0)
                    Assert.Equal(code, Assert.Catch<FnNasApiException>(() => fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult()).Code);
                else
                {
                    var cast = ItemDetail.Cast(fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult());
                    Assert.Equal("first,second", string.Join(',', cast.Select(credit => credit.Card.Id)));
                    Assert.Equal("角色0", cast[0].Credit);
                    Assert.Equal("编剧", cast[1].Credit);
                }
            }
        });
        Test("飞牛人员作品：编剧卡标识取得次页作品与跨职业去重", () =>
        {
            using var fixture = DetailListsFixture();
            var cast = ItemDetail.Cast(fixture.Session.Client.GetItemAsync("movie", default).GetAwaiter().GetResult());
            var writer = cast.Single(credit => credit.Credit == "编剧").Card;
            fixture.Transport.Bodies.Clear();
            fixture.Transport.Override = (request, _) =>
            {
                var body = fixture.Transport.Bodies.Last();
                Assert.Equal(writer.Id, body["person_guid"]!.GetValue<string>());
                if (body["job"]!.GetValue<string>() != "Screenplay")
                    return Task.FromResult(DetailEnvelope("""{"total":0,"list":[]}"""));
                var page = body["page"]!.GetValue<int>();
                return Task.FromResult(DetailEnvelope(page == 2
                    ? """{"total":201,"list":[{"guid":"work-b","title":"乙","type":"Movie"}]}"""
                    : new JsonObject
                    {
                        ["total"] = 201,
                        ["list"] = new JsonArray(Enumerable.Range(0, 200).Select(_ => (JsonNode)new JsonObject
                        { ["guid"] = "work-a", ["title"] = "甲", ["type"] = "Movie" }).ToArray())
                    }.ToJsonString()));
            };
            var works = fixture.Session.Client.GetItemsAsync(new ItemQuery
            { PersonId = writer.Id, SortBy = EmbySortBy.Name }, default).GetAwaiter().GetResult();
            Assert.Equal(2, works.TotalRecordCount);
            Assert.Equal("work-a,work-b", string.Join(',', works.Items.Select(item => item.Id)));
            Assert.Equal(2, fixture.Transport.Bodies.Count(body => body["job"]!.GetValue<string>() == "Screenplay"));
        });
        Test("飞牛人员：请求中取消和来源退出不会发布旧详情", () =>
        {
            foreach (var revoke in new[] { false, true })
            {
                using var fixture = DetailListsFixture();
                var previous = fixture.Transport.Override;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.Transport.Override = (request, token) =>
                    request.RequestUri!.AbsolutePath.Contains("/person/list/", StringComparison.Ordinal) ? Wait(token) : previous!(request, token);
                async Task<HttpResponseMessage> Wait(CancellationToken token)
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return DetailEnvelope("""{"total":0,"list":[]}""");
                }
                using var cancellation = new CancellationTokenSource();
                var scope = fixture.Session.Capture();
                var pending = scope.ExecuteAsync((client, token) => client.GetItemAsync("movie", token), cancellation.Token);
                started.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                if (revoke) fixture.Session.SignOut();
                else cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            }
        });
    }

    private static Fixture DetailListsFixture()
    {
        var fixture = new Fixture();
        fixture.Login();
        fixture.Transport.Override = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var data = path switch
            {
                "/v/api/v1/item/episode96" => """{"guid":"episode96","title":"延康双雄","type":"Episode","parent_guid":"season","episode_number":96,"season_number":1}""",
                "/v/api/v1/item/episode-known" => """{"guid":"episode-known","type":"Episode","tv_guid":"series","parent_guid":"season"}""",
                "/v/api/v1/item/season" => """{"guid":"season","title":"第一季","type":"Season","parent_guid":"series"}""",
                "/v/api/v1/item/series" => """{"guid":"series","title":"剧","type":"TV","genres":[7]}""",
                "/v/api/v1/item/movie" => """{"guid":"movie","title":"电影","type":"Movie","genres":["invalid",2]}""",
                "/v/api/v1/stream/list/episode96" or "/v/api/v1/stream/list/episode-known" => """{"files":[]}""",
                "/v/api/v1/item/list" => fixture.Transport.Bodies.Last()["tags"]!["type"]![0]!.GetValue<string>() == "TV"
                    ? """{"total":4,"list":[{"guid":"series","type":"TV"},{"guid":"related-a","title":"相似甲","type":"TV"},{"guid":"related-a","title":"相似甲","type":"TV"},{"guid":"related-b","title":"相似乙","type":"TV"}]}"""
                    : """{"total":5,"list":[{"guid":"movie","type":"Movie"},{"guid":"series","type":"TV"},{"guid":"related-a","title":"相似甲","type":"Movie"},{"guid":"related-a","title":"相似甲","type":"Movie"},{"guid":"related-b","title":"相似乙","type":"Movie"}]}""",
                var route when route.StartsWith("/v/api/v1/person/list/", StringComparison.Ordinal) => """
                    {"total":3,"list":[
                     {"person_guid":"person-a","name":"演员甲","job":"Actor","role":"主角","profile_path":"/portrait/a.webp","order":0},
                     {"person_guid":"person-b","name":"","original_name":"导演乙","job":"Director","role":"无效角色","order":1},
                     {"person_guid":"person-c","name":"编剧丙","job":"Screenplay","order":2}]}
                    """,
                _ => null
            };
            if (path == "/v/api/v1/item/list")
            {
                var response = JsonNode.Parse(data!)!.AsObject();
                var body = fixture.Transport.Bodies.Last();
                var size = body["page_size"]!.GetValue<int>();
                var page = body["page"]!.GetValue<int>();
                response["list"] = new JsonArray(response["list"]!.AsArray().Skip((page - 1) * size).Take(size)
                    .Select(value => value!.DeepClone()).ToArray());
                data = response.ToJsonString();
            }
            return data is null ? null : Task.FromResult(DetailEnvelope(data));
        };
        return fixture;
    }

    private static HttpResponseMessage DetailEnvelope(string data, int code = 0) => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"code\":" + code + ",\"data\":" + data + "}") };
}

using System.Net;
using System.Reflection;
using System.Text;
using Momoka.Bangumi;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private static void RegisterBangumiRatingTests(Assembly assembly, string root)
    {
        Test("Bangumi 详情评分：媒体库未提供编号时电影和剧集使用官方标题搜索，原评分同行保留", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series })
            {
                var platform = shape == Shape.Movie ? "剧场版" : "TV";
                using var transport = new RatingTransport("7.2")
                {
                    SearchBody = $$$"""{"total":1,"data":[{"id":90367,"type":2,"name":"思い出のマーニー","name_cn":"回忆中的玛妮","date":"2014-07-19","platform":"{{{platform}}}","infobox":[{"key":"别名","value":[{"v":"记忆中的玛妮"}]}]}]}"""
                };
                using var client = new BangumiClient(transport);
                using var fixture = new Fixture(assembly, root, shape, bangumi: client);
                fixture.Seed.Name = "记忆中的玛妮";
                fixture.Seed.ProductionYear = 2014;
                fixture.Seed.CommunityRating = 7.6f;
                fixture.Seed.CriticRating = 92;
                Await(fixture.Load());
                Until(() => fixture.Get("BangumiRatingText") as string == "7.2", "无编号条目应完成官方匹配并显示分数");
                Assert.True(transport.Paths.SequenceEqual(new[] { "/v0/search/subjects", "/v0/subjects/90367" }));
                Assert.Equal("Visible", fixture.Get("BangumiRatingVisibility")?.ToString());
                Assert.Equal("7.6", fixture.Get("CommunityScore") as string);
                Assert.Equal("92%", fixture.Get("CriticScore") as string);
            }
        });
        Test("Bangumi 详情评分：季页只查本季，父剧提供原评分，单集和无编号隐藏", () =>
        {
            using var transport = new RatingTransport("7.25");
            using var client = new BangumiClient(transport);
            using var fixture = new Fixture(assembly, root, Shape.Season, bangumi: client);
            fixture.Seed.ProviderIds["Bangumi"] = "456";
            var parent = fixture.Transport.Catalog["series"];
            parent.ProviderIds["Bangumi"] = "123";
            parent.CommunityRating = 8.4f;
            parent.CriticRating = 92;
            Await(fixture.Load());
            Assert.Equal("/v0/subjects/456", transport.Paths.Single());
            Assert.Equal("7.25", fixture.Get("BangumiRatingText") as string);
            Assert.Equal("Visible", fixture.Get("BangumiRatingVisibility")?.ToString());
            Assert.Equal("8.4", fixture.Get("CommunityScore") as string);
            Assert.Equal("92%", fixture.Get("CriticScore") as string);
            fixture.Reattach(fixture.Transport.Catalog["episode"]);
            fixture.Transport.Catalog["episode"].ProviderIds["Bgm"] = "456";
            Await(fixture.Load());
            Assert.Equal(1, transport.Paths.Count);
            Assert.Null(fixture.Get("BangumiRatingText"));
            fixture.Seed.ProviderIds.Clear();
            fixture.Reattach(fixture.Seed);
            Await(fixture.Load());
            Assert.Equal(1, transport.Paths.Count);
            Assert.Null(fixture.Get("BangumiRatingText"));
        });
        Test("Bangumi 详情评分：独立于 AniBT 和 MoviePilot，有分显示，零评分人数隐藏", () =>
        {
            using var transport = new RatingTransport("5.0");
            using var client = new BangumiClient(transport);
            using var fixture = new Fixture(assembly, root, Shape.Movie, bangumi: client);
            fixture.Seed.ProviderIds["Bangumi"] = "456";
            fixture.Seed.CommunityRating = null;
            fixture.Seed.CriticRating = null;
            fixture.MoviePilot.AniBtEnabled = false;
            Await(fixture.Load());
            Assert.False(fixture.MoviePilot.Enabled);
            Assert.Equal("5", fixture.Get("BangumiRatingText") as string);
            Assert.Equal("Visible", fixture.Get("ScoreVisibility")?.ToString());
            fixture.MoviePilot.AniBtEnabled = true;
            Assert.Equal(1, transport.Paths.Count);
            transport.Total = 0;
            Await(fixture.Load());
            Assert.Null(fixture.Get("BangumiRatingText"));
            Assert.Equal("Collapsed", fixture.Get("BangumiRatingVisibility")?.ToString());
            Assert.Equal("Collapsed", fixture.Get("ScoreVisibility")?.ToString());
        });
        Test("Bangumi 详情评分：慢查询独立于详情，换页后旧评分作废", () =>
        {
            using var transport = new RatingTransport("8.2") { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            using var client = new BangumiClient(transport);
            using var fixture = new Fixture(assembly, root, Shape.Movie, bangumi: client);
            fixture.Seed.ProviderIds["Bangumi"] = "456";
            Await(fixture.Load());
            Assert.False(fixture.Busy);
            Assert.False(transport.Token.IsCancellationRequested);
            Assert.Null(fixture.Get("BangumiRatingText"));
            fixture.Reattach(fixture.Transport.Catalog["series"]);
            Assert.True(transport.Token.IsCancellationRequested);
            Await(fixture.Load());
            Assert.Equal(1, transport.Paths.Count);
            transport.Pending.SetResult(true);
            Assert.Equal("Collapsed", fixture.Get("BangumiRatingVisibility")?.ToString());
        });
        Test("Bangumi 详情评分：无编号搜索在离页时取消，迟到搜索不能再读取或显示评分", () =>
        {
            using var transport = new RatingTransport("8.2") { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            using var client = new BangumiClient(transport);
            using var fixture = new Fixture(assembly, root, Shape.Movie, bangumi: client);
            fixture.Seed.ProductionYear = 2023;
            Await(fixture.Load());
            Assert.False(fixture.Busy);
            Assert.Equal("/v0/search/subjects", transport.Paths.Single());
            Assert.False(transport.Token.IsCancellationRequested);
            fixture.Reattach(fixture.Transport.Catalog["series"]);
            Assert.True(transport.Token.IsCancellationRequested);
            Await(fixture.Load());
            transport.Pending.SetResult(true);
            Assert.Equal(1, transport.Paths.Count);
            Assert.Null(fixture.Get("BangumiRatingText"));
        });
    }

    private sealed class RatingTransport(string rating) : HttpMessageHandler
    {
        public int Total { get; set; } = 1;
        public TaskCompletionSource<bool>? Pending { get; init; }
        public string? SearchBody { get; init; }
        public CancellationToken Token { get; private set; }
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.Equal("api.bgm.tv", uri.Host);
            Assert.Equal(request.Method == HttpMethod.Post ? "?limit=20" : "", uri.Query);
            Token = cancellationToken;
            Paths.Add(uri.AbsolutePath);
            if (Pending is not null) await Pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (request.Method == HttpMethod.Post)
                return new(HttpStatusCode.OK) { Content = new StringContent(SearchBody ?? "{\"total\":0,\"data\":[]}", Encoding.UTF8, "application/json") };
            var subject = uri.Segments.Last();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent($$$"""{"id":{{{subject}}},"type":2,"rating":{"score":{{{rating}}},"total":{{{Total}}}}}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}

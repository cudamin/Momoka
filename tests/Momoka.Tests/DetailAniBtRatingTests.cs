using System.Net;
using System.Reflection;
using System.Text;
using Momoka.AniBt;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private static void RegisterAniBtRatingTests(Assembly assembly, string root)
    {
        Test("AniBT 详情参考分：季页只查本季，父剧仍提供原评分", () =>
        {
            using var transport = new RatingTransport("7.25");
            using var client = new AniBtClient(transport);
            using var fixture = new Fixture(assembly, root, Shape.Season, aniBt: client);
            fixture.Seed.ProviderIds["Bangumi"] = "456";
            var parent = fixture.Transport.Catalog["series"];
            parent.ProviderIds["Bangumi"] = "123";
            parent.CommunityRating = 8.4f;
            parent.CriticRating = 92;

            Await(fixture.Load());
            Assert.False(fixture.MoviePilot.Enabled);
            Assert.Equal("?bgmId=456", transport.Queries.Single());
            Assert.Equal("AniBT 参考分 7.25", fixture.Get("AniBtRatingText") as string);
            Assert.Equal("Visible", fixture.Get("AniBtRatingVisibility")?.ToString());
            Assert.Equal("8.4", fixture.Get("CommunityScore") as string);
            Assert.Equal("92%", fixture.Get("CriticScore") as string);

            fixture.Reattach(fixture.Transport.Catalog["episode"]);
            fixture.Transport.Catalog["episode"].ProviderIds["Bgm"] = "456";
            Await(fixture.Load());
            Assert.Equal(1, transport.Queries.Count);
            Assert.Null(fixture.Get("AniBtRatingText"));

            fixture.Seed.ProviderIds.Clear();
            fixture.Reattach(fixture.Seed);
            Await(fixture.Load());
            Assert.Equal(1, transport.Queries.Count);
            Assert.Null(fixture.Get("AniBtRatingText"));
        });

        Test("AniBT 详情参考分：关闭和重开实时生效，重载后五分隐藏", () =>
        {
            using var transport = new RatingTransport("0");
            using var client = new AniBtClient(transport);
            using var fixture = new Fixture(assembly, root, Shape.Movie, aniBt: client);
            fixture.Seed.ProviderIds["Bangumi"] = "456";
            fixture.MoviePilot.AniBtEnabled = false;
            Await(fixture.Load());
            Assert.Equal(0, transport.Queries.Count);
            Assert.Equal("Collapsed", fixture.Get("AniBtRatingVisibility")?.ToString());

            fixture.MoviePilot.AniBtEnabled = true;
            Assert.Equal("AniBT 参考分 0.0", fixture.Get("AniBtRatingText") as string);
            fixture.MoviePilot.AniBtEnabled = false;
            Assert.Null(fixture.Get("AniBtRatingText"));
            fixture.MoviePilot.AniBtEnabled = true;
            Assert.Equal(2, transport.Queries.Count);

            transport.Rating = "5.0";
            Await(fixture.Load());
            Assert.Equal(3, transport.Queries.Count);
            Assert.Equal("Collapsed", fixture.Get("AniBtRatingVisibility")?.ToString());
        });

        Test("AniBT 详情参考分：慢查询独立于详情，换页后旧评分作废", () =>
        {
            using var transport = new RatingTransport("8.2") { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            using var client = new AniBtClient(transport);
            using var fixture = new Fixture(assembly, root, Shape.Movie, aniBt: client);
            fixture.Seed.ProviderIds["Bangumi"] = "456";
            Await(fixture.Load());
            Assert.False(fixture.Busy);
            Assert.False(transport.Token.IsCancellationRequested);
            Assert.Null(fixture.Get("AniBtRatingText"));

            fixture.Reattach(fixture.Transport.Catalog["series"]);
            Assert.True(transport.Token.IsCancellationRequested);
            Await(fixture.Load());
            Assert.Equal(1, transport.Queries.Count);
            transport.Pending.SetResult(true);
            Assert.Equal("Collapsed", fixture.Get("AniBtRatingVisibility")?.ToString());
        });
    }

    private sealed class RatingTransport(string rating) : HttpMessageHandler
    {
        public string Rating { get; set; } = rating;
        public TaskCompletionSource<bool>? Pending { get; init; }
        public CancellationToken Token { get; private set; }
        public List<string> Queries { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.Equal("anibt.net", uri.Host);
            Assert.Equal("/api/seasons/anime", uri.AbsolutePath);
            Token = cancellationToken;
            Queries.Add(uri.Query);
            if (Pending is not null) await Pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var subject = uri.Query["?bgmId=".Length..];
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent($$$"""
                    {"ok":true,"data":{"scope":{"kind":"bgmId","bgmId":{{{subject}}}},"total":1,"truncated":false,
                    "byWeekday":[{"animes":[{"animeId":"fixture","bgmId":{{{subject}}},"rating":{{{Rating}}}}]}]}}
                    """, Encoding.UTF8, "application/json")
            };
        }
    }
}

public class DetailRatingDispatcherProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name != "Run" || args is not [Action work])
            throw new AssertionException("只允许同步派发 AniBT 开关通知");
        work();
        return null;
    }
}

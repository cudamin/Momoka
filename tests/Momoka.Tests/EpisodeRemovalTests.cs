using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class EpisodeRemovalTests
{
    public static void Register()
    {
        Test("删除后选集：第五集删除后选第四集，不改原列表", () =>
        {
            var episodes = Enumerable.Range(1, 6).Select(number => Episode($"e{number}")).ToArray();
            Assert.True(ReferenceEquals(episodes[3], AfterRemoval(episodes[4], episodes)));
            Assert.Equal("e1,e2,e3,e4,e5,e6", string.Join(',', episodes.Select(episode => episode.Id)));
        });
        Test("删除后选集：首集没有上一集时选择下一集", () =>
        {
            EmbyItem[] episodes = [Episode("e1"), Episode("e2"), Episode("e3")];
            Assert.Equal("e2", AfterRemoval(episodes[0], episodes)?.Id);
        });
        Test("删除后选集：只有一集时没有详情落点", () =>
        {
            var episode = Episode("only");
            Assert.Null(AfterRemoval(episode, [episode]));
        });
        Test("删除后选集：列表末集选择上一集，不回绕", () =>
        {
            EmbyItem[] episodes = [Episode("e1"), Episode("e2")];
            Assert.Equal("e1", AfterRemoval(episodes[1], episodes)?.Id);
        });
        Test("删除后选集：按现有播出顺序跨季选择，不按集号重排", () =>
        {
            var first = Episode("s1e12", season: "s1");
            var special = Episode("special", season: "s0");
            var removed = Episode("s2e1", season: "s2");
            Assert.Equal("special", AfterRemoval(removed, [first, special, removed])?.Id);
        });
        Test("删除后选集：相同条目编号的其它来源不能代替当前上一集", () =>
        {
            var own = Scope("own");
            var foreign = Scope("foreign");
            var previous = own.Bind(Episode("e4"));
            var sameId = foreign.Bind(Episode("e4"));
            var removed = own.Bind(Episode("e5"));
            Assert.Equal(previous.IdentityKey, AfterRemoval(removed, [previous, sameId, removed])?.IdentityKey);
        });
        Test("删除后选集：其它作品与非单集都跳过，仍选最近同剧上一集", () =>
        {
            var previous = Episode("e4");
            var other = Episode("other", series: "another-series");
            var movie = new EmbyItem { Id = "movie", Type = EmbyItemType.Movie };
            var removed = Episode("e5");
            Assert.Equal("e4", AfterRemoval(removed, [previous, other, movie, removed])?.Id);
        });
        Test("删除后选集：缺剧编号时只用同季身份，不猜相同数字季号", () =>
        {
            var previous = Episode("e4", series: "");
            var other = Episode("other", series: "", season: "other-season");
            var removed = Episode("e5", series: "");
            Assert.Equal("e4", AfterRemoval(removed, [previous, other, removed])?.Id);
        });
        Test("删除后选集：被删项不在列表或没有作品身份时不跳转其它作品", () =>
        {
            var removed = Episode("e5");
            Assert.Null(AfterRemoval(removed, [Episode("e1")]));
            var unknown = Episode("unknown", series: "", season: "");
            Assert.Null(AfterRemoval(unknown, [Episode("other", series: "", season: ""), unknown]));
            Assert.Null(AfterRemoval(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie }, [Episode("e1")]));
        });
    }

    private static EmbyItem? AfterRemoval(EmbyItem removed, IReadOnlyList<EmbyItem> episodes)
    {
        return EpisodeNavigation.AfterRemoval(removed, episodes);
    }

    private static EmbyItem Episode(string id, string series = "series", string season = "season") => new()
    {
        Id = id,
        Type = EmbyItemType.Episode,
        SeriesId = series,
        SeasonId = season,
        ParentId = season,
        ParentIndexNumber = 1
    };

    private static EmbySessionScope Scope(string server)
    {
        var connection = new EmbyConnection(new Uri($"https://{server}.invalid/"), "fixture", "fixture", "fixture",
            server, DeviceIdentity.Create("episode-removal-fixture", "test"));
        return new EmbySessionScope(null!, new EmbySessionScope.Identity(new EmbyClient(null!, connection),
            new ServerProfile { Id = server }, new AccountProfile { Id = "fixture" }));
    }
}

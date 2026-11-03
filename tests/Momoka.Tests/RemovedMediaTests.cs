using Momoka.Configuration;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 删掉媒体库文件之后，服务器元数据要等下一次扫描才消失，这段时间里主页不能再把它画出来。
/// 这里钉的是「哪些条目算同一份已经不存在的媒体」：同一件、它的子项、剧／季下面的单集；以及「只在内存里」——
/// 用户令 2026-11-03 明确要求隐藏状态关闭 Momoka 后清空，所以这一份没有落盘入口。
/// </summary>
internal static class RemovedMediaTests
{
    public static void Register()
    {
        Test("已删媒体：同一件重复记住只算一次，Clear 后全部恢复", () =>
        {
            var source = Scope("removed-a", "user");
            var movie = source.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, Name = "电影" });
            var removed = new RemovedMedia();

            removed.Remember(movie);
            removed.Remember(movie);
            Assert.Equal(1, removed.Count, "同一件被记了两次");
            Assert.True(removed.Hides(source.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie })),
                "删除后同一件仍会显示");

            removed.Clear();
            Assert.Equal(0, removed.Count);
            Assert.False(removed.Hides(movie), "Clear 之后仍在隐藏");
        });

        Test("已删媒体：删一部剧，它的季、单集和直接子项一起隐藏，别的电影不动", () =>
        {
            var source = Scope("removed-b", "user");
            var series = source.Bind(new EmbyItem { Id = "series", Type = EmbyItemType.Series, Name = "剧" });
            var season = source.Bind(new EmbyItem
            {
                Id = "season",
                Type = EmbyItemType.Season,
                SeriesId = "series",
                ParentId = "series"
            });
            var episode = source.Bind(new EmbyItem
            {
                Id = "episode",
                Type = EmbyItemType.Episode,
                SeriesId = "series",
                SeasonId = "season",
                ParentId = "season"
            });
            var other = source.Bind(new EmbyItem { Id = "other", Type = EmbyItemType.Movie, Name = "另一部" });
            var removed = new RemovedMedia();
            removed.Remember(series);

            Assert.True(removed.Hides(series));
            Assert.True(removed.Hides(season), "剧删掉后季仍会显示");
            Assert.True(removed.Hides(episode), "剧删掉后单集仍会显示");
            Assert.False(removed.Hides(other), "误伤了别的电影");
        });

        Test("已删媒体：删一季只影响这一季的单集，同剧的另一季和剧本身照旧", () =>
        {
            var source = Scope("removed-c", "user");
            var series = source.Bind(new EmbyItem { Id = "series", Type = EmbyItemType.Series });
            var first = source.Bind(new EmbyItem { Id = "s1", Type = EmbyItemType.Season, SeriesId = "series", ParentId = "series" });
            var second = source.Bind(new EmbyItem { Id = "s2", Type = EmbyItemType.Season, SeriesId = "series", ParentId = "series" });
            var inFirst = source.Bind(new EmbyItem
            {
                Id = "e1",
                Type = EmbyItemType.Episode,
                SeriesId = "series",
                SeasonId = "s1",
                ParentId = "s1"
            });
            var inSecond = source.Bind(new EmbyItem
            {
                Id = "e2",
                Type = EmbyItemType.Episode,
                SeriesId = "series",
                SeasonId = "s2",
                ParentId = "s2"
            });
            var removed = new RemovedMedia();
            removed.Remember(first);

            Assert.True(removed.Hides(inFirst));
            Assert.False(removed.Hides(inSecond), "删一季误伤了另一季");
            Assert.False(removed.Hides(series), "删一季不该把整部剧藏起来");
            Assert.False(removed.Hides(second), "删一季误伤了同剧的另一季");
        });

        Test("已删媒体：删一集只藏那一集，剧和季都还在", () =>
        {
            var source = Scope("removed-d", "user");
            var series = source.Bind(new EmbyItem { Id = "series", Type = EmbyItemType.Series });
            var season = source.Bind(new EmbyItem { Id = "season", Type = EmbyItemType.Season, SeriesId = "series", ParentId = "series" });
            var episode = source.Bind(new EmbyItem
            {
                Id = "episode",
                Type = EmbyItemType.Episode,
                SeriesId = "series",
                SeasonId = "season",
                ParentId = "season"
            });
            var sibling = source.Bind(new EmbyItem
            {
                Id = "sibling",
                Type = EmbyItemType.Episode,
                SeriesId = "series",
                SeasonId = "season",
                ParentId = "season"
            });
            var removed = new RemovedMedia();
            removed.Remember(episode);

            Assert.True(removed.Hides(episode));
            Assert.False(removed.Hides(sibling), "删一集误伤了同季的另一集");
            Assert.False(removed.Hides(season), "删一集把整季藏起来了");
            Assert.False(removed.Hides(series), "删一集把整部剧藏起来了");
        });

        Test("已删媒体：两台服务器上编号相同的条目互不牵连", () =>
        {
            var first = Scope("removed-e", "user");
            var second = Scope("removed-f", "user");
            var mine = first.Bind(new EmbyItem { Id = "same", Type = EmbyItemType.Movie });
            var foreign = second.Bind(new EmbyItem { Id = "same", Type = EmbyItemType.Movie });
            var removed = new RemovedMedia();
            removed.Remember(mine);

            Assert.True(removed.Hides(mine));
            Assert.False(removed.Hides(foreign), "另一台服务器上同号条目被误藏");
        });
    }

    private static EmbySessionScope Scope(string server, string account)
    {
        var connection = new EmbyConnection(new Uri($"https://{server}.invalid/"), "fixture", account, account,
            server, DeviceIdentity.Create("removed-media-fixture", "test"));
        return new EmbySessionScope(null!, new EmbySessionScope.Identity(new EmbyClient(null!, connection),
            new ServerProfile { Id = server, Name = server }, new AccountProfile { Id = account, Username = account }));
    }
}

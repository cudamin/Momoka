using Momoka.Configuration;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 主页「最近添加」上手动标成已看的条目，在本次运行里不再显示（2026-11-17 用户令）。
/// 这里钉它认哪一件：同一来源、同一个 Id；别的来源同号条目、同剧的季与单集都不牵连。
/// 它也只在内存里，没有落盘入口 —— 与删除那本账（<see cref="RemovedMediaTests"/>）同一条规矩。
/// </summary>
internal static class HiddenWatchedMediaTests
{
    public static void Register()
    {
        Test("最近添加已看隐藏：同一件重复记住只算一次，Clear 后全部恢复", () =>
        {
            var source = Scope("watched-a", "user");
            var movie = source.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie, Name = "电影" });
            var hidden = new HiddenWatchedMedia();

            hidden.Remember(movie);
            hidden.Remember(movie);
            Assert.Equal(1, hidden.Count, "同一件被记了两次");
            Assert.True(hidden.Hides(source.Bind(new EmbyItem { Id = "movie", Type = EmbyItemType.Movie })),
                "标成已看后同一件仍会显示");

            hidden.Clear();
            Assert.Equal(0, hidden.Count);
            Assert.False(hidden.Hides(movie), "Clear 之后仍在隐藏");
        });

        Test("最近添加已看隐藏：只认这一件，不动它的季与单集", () =>
        {
            var source = Scope("watched-b", "user");
            var series = source.Bind(new EmbyItem { Id = "series", Type = EmbyItemType.Series });
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
            var hidden = new HiddenWatchedMedia();
            hidden.Remember(series);

            Assert.True(hidden.Hides(series));
            Assert.False(hidden.Hides(season), "标一剧已看把它的季也藏起来了");
            Assert.False(hidden.Hides(episode), "标一剧已看把它的单集也藏起来了");
        });

        Test("最近添加已看隐藏：两台服务器上编号相同的条目互不牵连", () =>
        {
            var first = Scope("watched-c", "user");
            var second = Scope("watched-d", "user");
            var mine = first.Bind(new EmbyItem { Id = "same", Type = EmbyItemType.Movie });
            var foreign = second.Bind(new EmbyItem { Id = "same", Type = EmbyItemType.Movie });
            var hidden = new HiddenWatchedMedia();
            hidden.Remember(mine);

            Assert.True(hidden.Hides(mine));
            Assert.False(hidden.Hides(foreign), "另一台服务器上同号条目被误藏");
        });

        Test("最近添加已看隐藏：没有 Id 的条目不进账", () =>
        {
            var hidden = new HiddenWatchedMedia();
            hidden.Remember(new EmbyItem { Type = EmbyItemType.Movie });
            Assert.Equal(0, hidden.Count);
        });
    }

    private static EmbySessionScope Scope(string server, string account)
    {
        var connection = new EmbyConnection(new Uri($"https://{server}.invalid/"), "fixture", account, account,
            server, DeviceIdentity.Create("hidden-watched-fixture", "test"));
        return AnonymousScopeFixture.DetachedIdentity(connection,
            new ServerProfile { Id = server, Name = server }, new AccountProfile { Id = account, Username = account });
    }
}

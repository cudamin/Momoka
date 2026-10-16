using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「主页的继续观看标记已看之后要自动切换为下一集，如果没有下一集或者是电影标记已看后要移出继续观看」
/// （用户令 2026-10-15）的那一半规则：<see cref="ResumeRow"/> 判「那一格换成谁、还是撤掉」。
/// <para>
/// 值得单测的正是这几种看起来都像「那一排少了一格」的情形：电影该整个撤掉、单集该换成下一集、最后一集该撤掉、
/// 服务器把刚标记的那一集发回来时该撤掉而不是画两个分身。屏幕上它们都只是「那一格变了」。
/// </para>
/// </summary>
internal static class ResumeRowTests
{
    public static void Register()
    {
        Test("继续观看：单集标记已看后换成这部剧的下一集", () =>
        {
            var watched = Episode("ep-3");
            var next = Episode("ep-4");

            Assert.True(ResumeRow.WantsNext(watched), "单集要问下一集");
            Assert.Equal("ep-4", ResumeRow.SlotAfterWatched(watched, next)?.Id);
        });

        Test("继续观看：电影标记已看后撤掉那一格，也不去问下一集", () =>
        {
            var watched = new EmbyItem { Id = "movie", Name = "一部电影", Type = EmbyItemType.Movie };

            Assert.False(ResumeRow.WantsNext(watched), "电影没有下一集可问");
            Assert.Null(ResumeRow.SlotAfterWatched(watched, Episode("ep-1")), "电影即使服务器给了单集也不换");
        });

        Test("继续观看：没有下一集就撤掉那一格", () =>
        {
            // 最后一集：服务器那条 NextUp 对一部看完的剧什么都不给。
            Assert.Null(ResumeRow.SlotAfterWatched(Episode("ep-12"), null));
        });

        Test("继续观看：服务器把刚标记的那一集发回来时当没有下一集", () =>
        {
            var watched = Episode("ep-3");

            Assert.Null(ResumeRow.SlotAfterWatched(watched, Episode("ep-3")), "同一集不能占两格");
        });

        Test("继续观看：没带剧 id 的单集不去问下一集", () =>
        {
            var orphan = Episode("ep-3");
            orphan.SeriesId = null;

            Assert.False(ResumeRow.WantsNext(orphan));
            Assert.Null(ResumeRow.SlotAfterWatched(orphan, Episode("ep-4")));
        });
    }

    private static EmbyItem Episode(string id) => new()
    {
        Id = id,
        Name = id,
        Type = EmbyItemType.Episode,
        SeriesId = "show",
        SeasonId = "season-1"
    };
}

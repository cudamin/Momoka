using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 「在 MoviePilot 搜索其他版本」发什么搜索词 —— 用户定的对应在这里变成断言：剧→<c>剧名</c>；季→<c>剧名 S0x</c>；
/// 电影→<c>电影名</c>；集 2026-09-24 定为 <c>剧名 S0x E0x</c>，2026-10-11 改为 <c>剧名 年份 S0x</c>、集号交给结果
/// 侧的集筛选（<see cref="MoviePilotEpisodePattern"/>）；订阅与遗漏集那两支没有结果页可筛，仍是 <c>剧名 S0x E0x</c>。
/// <para>
/// 值得钉，是因为错起来屏上看着正常：少个空格、季号取错成集号、季页拿了「第 2 季」当剧名 —— 都要有人一条条比
/// 才看得出来，而这几行正是那种「一个输入一个正确答案」的判断（同 <see cref="Emby.ItemMenu"/>）。
/// </para>
/// </summary>
internal static class MoviePilotVersionQueryTests
{
    public static void Register()
    {
        Test("MP 搜版本：电影发片名", () =>
            Assert.Equal("星际穿越", MoviePilotVersionQuery.Keyword(
                new EmbyItem { Id = "m1", Name = "星际穿越", Type = EmbyItemType.Movie })));

        Test("MP 搜版本：剧发整部剧的名字", () =>
            Assert.Equal("三体", MoviePilotVersionQuery.Keyword(
                new EmbyItem { Id = "s1", Name = "三体", Type = EmbyItemType.Series })));

        Test("MP 搜版本：季发「剧名 S0x」，季号取自己的 IndexNumber、剧名取 SeriesName", () =>
        {
            // 季自己的名字是「第 2 季」，不是剧名 —— 拿它当词头就搜不到了，必须用 SeriesName。
            var season = new EmbyItem
            {
                Id = "n2",
                Name = "第 2 季",
                Type = EmbyItemType.Season,
                SeriesName = "三体",
                IndexNumber = 2
            };
            Assert.Equal("三体 S02", MoviePilotVersionQuery.Keyword(season));
        });

        Test("MP 搜版本：集发「剧名 年份 S0x」，季号取 ParentIndexNumber、年份取条目自己的", () =>
        {
            var episode = new EmbyItem
            {
                Id = "e1",
                Name = "崛起",
                Type = EmbyItemType.Episode,
                SeriesName = "攻壳机动队",
                ProductionYear = 1995,
                ParentIndexNumber = 1,
                IndexNumber = 2
            };
            Assert.Equal("攻壳机动队 1995 S01", MoviePilotVersionQuery.Keyword(episode));
        });

        Test("MP 搜版本：集没给年份退回「剧名 S0x」；季号也缺就退回「剧名 年份」，都不缺空段", () =>
        {
            Assert.Equal("攻壳机动队 S01", MoviePilotVersionQuery.Keyword(new EmbyItem
            {
                Id = "e3",
                Name = "某集",
                Type = EmbyItemType.Episode,
                SeriesName = "攻壳机动队",
                ParentIndexNumber = 1,
                IndexNumber = 5
            }));
            Assert.Equal("攻壳机动队 1995", MoviePilotVersionQuery.Keyword(new EmbyItem
            {
                Id = "e4",
                Name = "特别篇",
                Type = EmbyItemType.Episode,
                SeriesName = "攻壳机动队",
                ProductionYear = 1995,
                IndexNumber = 1
            }));
            Assert.Equal("攻壳机动队", MoviePilotVersionQuery.Keyword(new EmbyItem
            {
                Id = "e5",
                Name = "某集",
                Type = EmbyItemType.Episode,
                SeriesName = "攻壳机动队",
                IndexNumber = 2
            }));
        });

        Test("MP 搜版本：集的目标集号取自己的 IndexNumber，电影/剧/季没有", () =>
        {
            Assert.Equal(7, MoviePilotVersionQuery.TargetEpisode(new EmbyItem
            { Id = "e6", Type = EmbyItemType.Episode, ParentIndexNumber = 2, IndexNumber = 7 }));
            Assert.Null(MoviePilotVersionQuery.TargetEpisode(new EmbyItem { Id = "m2", Type = EmbyItemType.Movie }));
            Assert.Null(MoviePilotVersionQuery.TargetEpisode(new EmbyItem { Id = "s2", Type = EmbyItemType.Series }));
            Assert.Null(MoviePilotVersionQuery.TargetEpisode(new EmbyItem { Id = "n3", Type = EmbyItemType.Season }));
        });

        Test("MP 搜版本：季号缺失时退回只有剧名，不发一个光「S」出去", () =>
        {
            var season = new EmbyItem { Id = "n0", Name = "特别篇", Type = EmbyItemType.Season, SeriesName = "某剧" };
            Assert.Equal("某剧", MoviePilotVersionQuery.Keyword(season));
        });

        Test("MP 搜版本：季/集没给 SeriesName 时退到条目自己的名字，总比空词强", () =>
        {
            var episode = new EmbyItem { Id = "e2", Name = "某集", Type = EmbyItemType.Episode, ParentIndexNumber = 1, IndexNumber = 3 };
            Assert.Equal("某集 S01", MoviePilotVersionQuery.Keyword(episode));
        });

        Test("MP 搜版本：只有电影/剧/季/集这四种支持，合集/媒体库/演职人员不支持", () =>
        {
            foreach (var type in (string[])[EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Episode])
                Assert.True(MoviePilotVersionQuery.Supports(new EmbyItem { Id = "x", Type = type }), $"{type} 应支持");

            foreach (var type in (string[])[EmbyItemType.BoxSet, EmbyItemType.CollectionFolder, EmbyItemType.Person, EmbyItemType.Folder])
                Assert.False(MoviePilotVersionQuery.Supports(new EmbyItem { Id = "x", Type = type }), $"{type} 不该支持");
        });

        Test("MP 搜版本：订阅里的一集用剧名与季集号拼词（那边没有 Emby 条目可算）", () =>
        {
            Assert.Equal("漫长的旅途 S01 E01", MoviePilotVersionQuery.Keyword("漫长的旅途", 1, 1));
            Assert.Equal("某剧 S02 E10", MoviePilotVersionQuery.Keyword(" 某剧 ", 2, 10));
            // 季号缺失就退回只有剧名，和 EmbyItem 那一支一个规矩。
            Assert.Equal("某剧", MoviePilotVersionQuery.Keyword("某剧", null, null));
        });
    }
}

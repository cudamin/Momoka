using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class EpisodeDisplayTests
{
    public static void Register()
    {
        Test("集页归季：首季前、季间、下一季首播日与最后季之后的特别篇", () =>
        {
            var seasons = new[] { Season(0), Season(1), Season(2) };
            var before = Episode(0, 1, "2020-01-01");
            var first = Episode(1, 1, "2020-02-01");
            var inserted = Episode(0, 2, "2020-02-05");
            var second = Episode(1, 2, "2020-02-08");
            var between = Episode(0, 3, "2020-04-01");
            var nextFirst = Episode(2, 1, "2020-06-01");
            var boundary = Episode(0, 4, "2020-06-01");
            var after = Episode(0, 5, "2021-01-01");
            var missing = Episode(0, 6, null);
            EmbyItem[] episodes = [missing, after, nextFirst, boundary, between, second, inserted, first, before];
            Assert.Equal("0-1,1-1,0-2,1-2,0-3", Ids(Display(episodes, seasons, seasons[1])));
            Assert.Equal("0-4,2-1,0-5", Ids(Display(episodes, seasons, seasons[2])));
            Assert.Equal("0-6", Ids(Display(episodes, seasons, seasons[0])));
            Assert.True(ReferenceEquals(seasons[1], Owner(episodes, seasons, inserted)));
            Assert.Equal(0, inserted.ParentIndexNumber);
            Assert.Equal("s0", inserted.SeasonId);
            Assert.True(ReferenceEquals(inserted, Display(episodes, seasons, seasons[1])[2]));
        });
        Test("集页归季：空季不退回全剧，未标日期的正片仍留在本季", () =>
        {
            var seasons = new[] { Season(0), Season(1), Season(2), Season(3) };
            EmbyItem[] episodes = [Episode(1, 1, "2020-01-01"), Episode(2, 1, "2021-01-01"), Episode(1, 2, null)];
            Assert.Equal("1-1,1-2", Ids(Display(episodes, seasons, seasons[1])));
            Assert.Equal(0, Display(episodes, seasons, seasons[3]).Count);
        });
        Test("集页归季：不能用第二集或季元数据日期替代缺失的第一集首播日", () =>
        {
            var seasons = new[] { Season(0), Season(1), Season(2) };
            seasons[2].PremiereDate = DateTimeOffset.Parse("2021-01-01");
            var special = Episode(0, 1, "2020-06-01");
            EmbyItem[] episodes = [Episode(1, 1, "2020-01-01"), Episode(2, 1, null), Episode(2, 2, "2021-02-01"), special];
            Assert.True(ReferenceEquals(seasons[0], Owner(episodes, seasons, special)));
            Assert.Equal("0-1", Ids(Display(episodes, seasons, seasons[0])));
        });
        Test("集页归季：季清单失败且只有季号时，不把特别篇重复归入每季", () =>
        {
            EmbyItem[] episodes = [Episode(1, 1, "2020-01-01"), Episode(2, 1, "2021-01-01"),
                Episode(0, 1, "2020-06-01"), Episode(0, 2, "2021-06-01")];
            foreach (var episode in episodes) episode.SeasonId = null;
            var first = Season(1);
            var second = Season(2);
            first.Id = second.Id = "";
            Assert.Equal("1-1,0-1", Ids(Display(episodes, [], first)));
            Assert.Equal("2-1,0-2", Ids(Display(episodes, [], second)));
        });
    }

    private static IReadOnlyList<EmbyItem> Display(IReadOnlyList<EmbyItem> episodes, IReadOnlyList<EmbyItem> seasons, EmbyItem season) =>
        ItemDetail.DisplaySeasonEpisodes(episodes, seasons, season);

    private static EmbyItem? Owner(IReadOnlyList<EmbyItem> episodes, IReadOnlyList<EmbyItem> seasons, EmbyItem episode) =>
        ItemDetail.DisplaySeasonOf(episodes, seasons, episode);

    private static string Ids(IReadOnlyList<EmbyItem> episodes) => string.Join(',', episodes.Select(item => item.Id));
    private static EmbyItem Season(int number) => new()
    { Id = "s" + number, Name = "第" + number + "季", Type = EmbyItemType.Season, IndexNumber = number };
    private static EmbyItem Episode(int season, int number, string? date) => new()
    {
        Id = season + "-" + number,
        Type = EmbyItemType.Episode,
        SeasonId = "s" + season,
        ParentIndexNumber = season,
        IndexNumber = number,
        PremiereDate = date is null ? null : DateTimeOffset.Parse(date)
    };
}

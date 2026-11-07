using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class EpisodeSeasonItemsTests
{
    public static void Register()
    {
        Test("单集当季：特别篇按首播边界归季，早于首季与等于次季均有落点", () =>
        {
            var first = Season(1, At(2024, 10, 1));
            var second = Season(2, At(2025, 4, 1));
            var third = Season(3, At(2026, 1, 1));
            EmbyItem[] seasons = [third, first, second];
            foreach (var (date, expected) in new[]
            {
                (At(2024, 9, 1), 1), (At(2025, 3, 31), 1), (At(2025, 4, 1), 2),
                (At(2025, 12, 31), 2), (At(2026, 1, 1), 3), (At(2028, 1, 1), 3)
            })
                Assert.Equal(expected, Number([], seasons, Episode("special", 0, date)));
        });

        Test("单集当季：季日期优先，缺季日期与缺DTO都从普通集首播补边界", () =>
        {
            var first = Episode("one", 1, At(2024, 1, 1));
            var second = Episode("two", 2, At(2025, 6, 1));
            var special = Episode("special", 0, At(2025, 5, 1));
            EmbyItem[] episodes = [second, special, first];
            Assert.Equal(1, Number(episodes, [], special));
            Assert.Equal(1, Number(episodes, [Season(1, null), Season(2, null)], special));
            Assert.Equal(2, Number(episodes, [Season(1, null), Season(2, At(2025, 4, 1))], special));
        });

        Test("单集当季：普通集按季号归属，缺日期特别篇留S00", () =>
        {
            var undated = Episode("undated-special", 0, null);
            var ordinary = Episode("one", 1, At(2030, 1, 1));
            var other = Episode("two", 2, null);
            EmbyItem[] episodes = [undated, ordinary, other];
            EmbyItem[] seasons = [Season(1, At(2024, 1, 1)), Season(2, At(2025, 1, 1))];
            Assert.Equal(1, Number(episodes, seasons, ordinary));
            Assert.Equal(2, Number(episodes, seasons, other));
            Assert.Equal(0, Number(episodes, seasons, undated));
            Assert.Equal("undated-special", Names(Items(episodes, seasons, undated)));
            Assert.Equal(0, Number([undated], [], Episode("dated-special", 0, At(2026, 1, 1))));
        });

        Test("单集当季：当前特别篇默认归入正片季，显式换季只投影所选季", () =>
        {
            var one = Episode("one", 1, At(2024, 1, 1));
            var between = Episode("between", 0, At(2024, 3, 1));
            var two = Episode("two", 2, At(2025, 1, 1));
            var after = Episode("after", 0, At(2025, 3, 1));
            var undated = Episode("undated", 0, null);
            EmbyItem[] episodes = [after, undated, two, between, one];
            EmbyItem[] seasons = [Season(1, null), Season(2, null), Season(0, null)];
            Assert.Equal("one,between", Names(Items(episodes, seasons, between)));
            Assert.Equal("two,after", Names(Items(episodes, seasons, between, seasons[1])));
            Assert.Equal("undated", Names(Items(episodes, seasons, between, seasons[2])));
            Assert.Equal("", Names(Items(episodes, seasons, between, Season(9, null))));
        });

        Test("单集当季：列表按播出时间插入且不改原字段、对象或输入顺序", () =>
        {
            var first = Episode("first", 1, At(2024, 1, 1));
            var last = Episode("last", 1, null);
            var special = Episode("special", 0, At(2024, 2, 1));
            special.SeasonId = "s0";
            special.IndexNumber = 7;
            var second = Episode("second", 2, At(2025, 1, 1));
            EmbyItem[] episodes = [last, second, special, first];
            var output = Items(episodes, [], first);
            Assert.Equal("first,special,last", Names(output));
            Assert.Equal("last,second,special,first", Names(episodes));
            Assert.True(ReferenceEquals(special, output[1]));
            Assert.Equal("s0", special.SeasonId);
            Assert.Equal(0, special.ParentIndexNumber);
            Assert.Equal(7, special.IndexNumber);
            Assert.Equal(At(2024, 2, 1), special.PremiereDate);
        });

        Test("单集当季：没有季号时使用真实季ID，完全无归属不混入其它未知集", () =>
        {
            var first = Episode("first", null, null);
            first.SeasonId = "season-a";
            var other = Episode("other", null, null);
            other.SeasonId = "season-b";
            Assert.Equal("first", Names(Items([first, other], [], first)));
            var season = Season(3, null);
            season.Id = "season-a";
            Assert.Equal(3, Number([first, other], [season], first));
            var unknown = Episode("unknown", null, null);
            Assert.Equal("unknown", Names(Items([first, other, unknown], [], unknown)));
        });
    }

    private static int? Number(IReadOnlyList<EmbyItem> episodes, IReadOnlyList<EmbyItem> seasons, EmbyItem item)
        => ItemDetail.EpisodeSeasonNumber(episodes, seasons, item);

    private static IReadOnlyList<EmbyItem> Items(IReadOnlyList<EmbyItem> episodes, IReadOnlyList<EmbyItem> seasons,
        EmbyItem current, EmbyItem? selected = null)
        => ItemDetail.EpisodeSeasonItems(episodes, seasons, current, selected);

    private static string Names(IEnumerable<EmbyItem> items) => string.Join(',', items.Select(item => item.Name));

    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static EmbyItem Episode(string name, int? number, DateTimeOffset? premiere) => new()
    {
        Id = name,
        Name = name,
        Type = EmbyItemType.Episode,
        ParentIndexNumber = number,
        PremiereDate = premiere
    };

    private static EmbyItem Season(int number, DateTimeOffset? premiere) => new()
    {
        Id = "s" + number,
        Name = "第" + number + "季",
        Type = EmbyItemType.Season,
        IndexNumber = number,
        PremiereDate = premiere
    };
}

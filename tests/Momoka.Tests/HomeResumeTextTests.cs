using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class HomeResumeTextTests
{
    internal static void Register()
    {
        Test("首页继续观看：剧名在上，季集和集名用短横线分隔", () =>
        {
            var episode = Episode("父亲", "死亡笔记", 1, 29);
            Assert.Equal("死亡笔记", HomeResumeText.Title(episode));
            Assert.Equal("S01E29 - 父亲", HomeResumeText.Subtitle(episode));
            Assert.Equal("S01E29  ·  死亡笔记", episode.CardSubtitle);
            Assert.Equal("父亲", episode.Name);
        });

        foreach (var series in new string?[] { null, "", "   " })
            Test($"首页继续观看：缺少剧名时保留集名 [{series ?? "null"}]", () =>
            {
                var episode = Episode("父亲", series, 1, 29);
                Assert.Equal("父亲", HomeResumeText.Title(episode));
                Assert.Equal("S01E29 - 父亲", HomeResumeText.Subtitle(episode));
            });

        Test("首页继续观看：空集名不留下悬空分隔符，剧名去首尾空白", () =>
        {
            var episode = Episode("   ", "  死亡笔记  ", 0, 1);
            Assert.Equal("死亡笔记", HomeResumeText.Title(episode));
            Assert.Equal("S00E01", HomeResumeText.Subtitle(episode));
        });

        foreach (var numbers in new (int? Season, int? Episode)[] { (null, 29), (1, null), (null, null) })
            Test($"首页继续观看：缺少季集编号不编造 S00E00 [{numbers}]", () =>
            {
                var episode = Episode(" 父亲 ", "死亡笔记", numbers.Season, numbers.Episode);
                Assert.Equal("父亲", HomeResumeText.Subtitle(episode));
            });

        Test("首页继续观看：集名和编号都缺少时第二行为空", () =>
            Assert.Equal("", HomeResumeText.Subtitle(Episode("", "死亡笔记", null, null))));

        foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Series, EmbyItemType.Season })
            Test($"首页继续观看：非单集保留原文案 [{type}]", () =>
            {
                var item = new EmbyItem
                {
                    Id = "fixture",
                    Name = "原标题",
                    Type = type,
                    SeriesName = "不应替换",
                    ProductionYear = 2026,
                    Status = "Continuing"
                };
                Assert.Equal(item.Name, HomeResumeText.Title(item));
                Assert.Equal(item.CardSubtitle, HomeResumeText.Subtitle(item));
            });
    }

    private static EmbyItem Episode(string name, string? series, int? season, int? episode) => new()
    {
        Id = "fixture",
        Name = name,
        SeriesName = series,
        Type = EmbyItemType.Episode,
        ParentIndexNumber = season,
        IndexNumber = episode
    };
}

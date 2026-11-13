using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 人物页上那几个由这一页自己决定的字：出生/逝世与年龄、出生地、数据库链接、影视作品的分组标题和
/// 每一行的署名。测试钉的是纯函数，页面只把结果摆上去。
/// </summary>
internal static class PersonDetailTests
{
    public static void Register()
    {
        RegisterLife();
        RegisterPlace();
        RegisterLinks();
        RegisterCredits();
    }

    private static void RegisterLife()
    {
        Test("人物：出生日期按日历读，年龄按整年算", () =>
        {
            var person = new EmbyItem
            {
                Type = EmbyItemType.Person,
                PremiereDate = new DateTimeOffset(1994, 2, 27, 0, 0, 0, TimeSpan.Zero)
            };

            Assert.Equal("出生：1994年2月27日（31 岁）", PersonDetail.Birth(person, new DateOnly(2026, 2, 26)));
            Assert.Equal("出生：1994年2月27日（32 岁）", PersonDetail.Birth(person, new DateOnly(2026, 2, 27)));
            Assert.Equal("出生：1994年2月27日（32 岁）", PersonDetail.Birth(person, new DateOnly(2026, 12, 31)));
        });

        Test("人物：生日是日历上的一天，不跟机器时区换日", () =>
        {
            // 同一天写成 UTC 午夜、写成 +08:00 午夜，读出来是同一个日期；年龄不因运行这台机器的时区差一岁。
            var utc = new EmbyItem
            {
                PremiereDate = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var east = new EmbyItem
            {
                PremiereDate = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.FromHours(8))
            };

            Assert.Equal(PersonDetail.Birth(utc, new DateOnly(2026, 1, 1)),
                PersonDetail.Birth(east, new DateOnly(2026, 1, 1)));
            Assert.Equal("出生：2000年1月1日（26 岁）", PersonDetail.Birth(utc, new DateOnly(2026, 1, 1)));
        });

        Test("人物：2 月 29 日出生的人在平年按 2 月 28 日满岁", () =>
        {
            var person = new EmbyItem { PremiereDate = new DateTimeOffset(2000, 2, 29, 0, 0, 0, TimeSpan.Zero) };

            Assert.Equal("出生：2000年2月29日（25 岁）", PersonDetail.Birth(person, new DateOnly(2026, 2, 27)));
            Assert.Equal("出生：2000年2月29日（26 岁）", PersonDetail.Birth(person, new DateOnly(2026, 2, 28)));
            Assert.Equal("出生：2000年2月29日（26 岁）", PersonDetail.Birth(person, new DateOnly(2026, 3, 1)));
        });

        Test("人物：逝世之后出生那一行不再写年龄，享年写在逝世那一行", () =>
        {
            var person = new EmbyItem
            {
                PremiereDate = new DateTimeOffset(1965, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EndDate = new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero)
            };

            Assert.Equal("出生：1965年1月1日", PersonDetail.Birth(person, new DateOnly(2026, 11, 13)));
            Assert.Equal("逝世：2020年6月1日（享年 55 岁）", PersonDetail.Death(person));
        });

        Test("人物：没有生卒日期时两行都收起，坏数据不猜", () =>
        {
            Assert.Equal("", PersonDetail.Birth(new EmbyItem(), new DateOnly(2026, 11, 13)));
            Assert.Equal("", PersonDetail.Death(new EmbyItem()));
            Assert.Equal("", PersonDetail.Birth(new EmbyItem
            {
                PremiereDate = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero)
            }, new DateOnly(2026, 11, 13)));
            Assert.Null(PersonDetail.Age(new DateOnly(2020, 1, 1), new DateOnly(2019, 1, 1)));
        });
    }

    private static void RegisterPlace()
    {
        Test("人物：出生地去掉空白和重复，空的整行收起", () =>
        {
            Assert.Equal("Saitama、Japan", PersonDetail.Birthplace(new EmbyItem
            {
                ProductionLocations = ["Saitama", " Japan ", "Saitama", "", "   "]
            }));
            Assert.Equal("", PersonDetail.Birthplace(new EmbyItem()));
            Assert.Equal("", PersonDetail.Birthplace(new EmbyItem { ProductionLocations = ["  "] }));
        });
    }

    private static void RegisterLinks()
    {
        Test("人物：IMDb 走 name 页、TMDB 走 person 页", () =>
        {
            var links = PersonDetail.Links(new EmbyItem
            {
                ProviderIds = new Dictionary<string, string>
                {
                    ["IMDB"] = "nm1234567",
                    ["Tmdb"] = "76501"
                }
            });

            Assert.Equal(2, links.Count);
            Assert.Equal("IMDb", links[0].Text);
            Assert.Equal("https://www.imdb.com/name/nm1234567/", links[0].Url);
            Assert.Equal("TMDB", links[1].Text);
            Assert.Equal("https://www.themoviedb.org/person/76501", links[1].Url);
        });

        Test("人物：没有刮削 id 时数据库链接整块收起", () =>
        {
            Assert.Equal(0, PersonDetail.Links(new EmbyItem()).Count);
            Assert.Equal(0, PersonDetail.Facts(new EmbyItem()).Count);
            Assert.Equal(0, PersonDetail.Facts(new EmbyItem
            {
                ProviderIds = new Dictionary<string, string> { ["Imdb"] = "  " }
            }).Count);
        });
    }

    private static void RegisterCredits()
    {
        Test("影视作品：演员有角色名时写「扮演」，没有时退回职务", () =>
        {
            var item = new EmbyItem
            {
                Id = "m1",
                Name = "葬送的芙莉莲",
                Type = EmbyItemType.Movie,
                ProductionYear = 2023,
                People =
                [
                    new EmbyPerson { Id = "p1", Name = "种崎敦美", Type = "Actor", Role = "芙莉莲" },
                    new EmbyPerson { Id = "p2", Name = "斋藤圭一郎", Type = "Director" }
                ]
            };

            var acting = PersonDetail.Credit(item, "p1", "Actor");
            Assert.Equal("2023", acting.Year);
            Assert.Equal("葬送的芙莉莲", acting.Title);
            Assert.Equal("扮演 芙莉莲", acting.Role);

            var crew = PersonDetail.Credit(item, "p2", "Director");
            Assert.Equal("导演", crew.Role);
        });

        Test("影视作品：列表没带 People 时用这一组问的职务", () =>
        {
            var item = new EmbyItem { Id = "s1", Name = "某部剧", Type = EmbyItemType.Series };

            Assert.Equal("演员", PersonDetail.Credit(item, "p1", "Actor").Role);
            Assert.Equal("客串", PersonDetail.Credit(item, "p1", "GuestStar").Role);
            Assert.Equal("", PersonDetail.Credit(item, "p1", null).Role);
        });

        Test("影视作品：技术署名带角色时把角色接在职务后面", () =>
        {
            var item = new EmbyItem
            {
                People = [new EmbyPerson { Id = "p1", Name = "某人", Type = "Writer", Role = "系列构成" }]
            };

            Assert.Equal("编剧 · 系列构成", PersonDetail.Credit(item, "p1", "Writer").Role);
        });

        Test("影视作品：没有年份就不写 0，分组标题带总数", () =>
        {
            Assert.Equal("", PersonDetail.Year(new EmbyItem()));
            Assert.Equal("1999", PersonDetail.Year(new EmbyItem
            {
                PremiereDate = new DateTimeOffset(1999, 4, 1, 0, 0, 0, TimeSpan.Zero)
            }));
            Assert.Equal("演员 (156)", PersonDetail.Heading("Actor", 156));
            Assert.Equal("导演 (0)", PersonDetail.Heading("Director", 0));
        });

        Test("影视作品：分组顺序固定，单集不进影视作品", () =>
        {
            Assert.Equal("Actor,Director,Writer,Producer,GuestStar,Composer",
                string.Join(',', PersonDetail.CreditTypes));
            Assert.False(PersonDetail.FilmographyTypes.Contains(EmbyItemType.Episode));
            Assert.True(PersonDetail.FilmographyTypes.Contains(EmbyItemType.Series));
            Assert.True(PersonDetail.FilmographyTypes.Contains(EmbyItemType.Movie));
        });
    }
}

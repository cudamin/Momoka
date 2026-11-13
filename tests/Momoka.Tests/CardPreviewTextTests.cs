using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class CardPreviewTextTests
{
    public static void Register()
    {
        Test("悬停文字：剧名与标题相同时只保留集号和时长", () =>
        {
            var episode = Episode();
            Assert.Equal("S02E05  ·  23 分钟", CardPreviewText.Meta(episode, "青之箱"));
            Assert.Equal("S02E05  ·  青之箱  ·  23 分钟", CardPreviewText.Meta(episode, "冬季首战"));
            Assert.Equal("S02E05  ·  青之箱  ·  23 分钟", CardPreviewText.Meta(episode));
        });
        Test("悬停文字：缺集号时去重剧名不会留下分隔点", () =>
        {
            var episode = Episode();
            episode.IndexNumber = null;
            Assert.Equal("23 分钟", CardPreviewText.Meta(episode, "青之箱"));
        });
        Test("悬停文字：剧与电影的时长和年份保持各自语义", () =>
        {
            var item = new EmbyItem
            {
                Type = EmbyItemType.Series,
                ProductionYear = 2024,
                Status = "Continuing",
                RunTimeTicks = TimeSpan.FromMinutes(24).Ticks,
                OfficialRating = "TV-14"
            };
            Assert.Equal("2024 – 至今  ·  每集 24 分钟  ·  TV-14", CardPreviewText.Meta(item));
            item.Type = EmbyItemType.Movie;
            Assert.Equal("2024  ·  24 分钟  ·  TV-14", CardPreviewText.Meta(item));
        });
        Test("悬停文字：空数据不产生占位文字和零评分", () =>
        {
            var item = new EmbyItem { Type = EmbyItemType.Movie, Overview = "  " };
            Assert.Equal("", CardPreviewText.Meta(item));
            Assert.Equal("", CardPreviewText.Genres(item));
            Assert.Equal("", CardPreviewText.Rating(item));
            Assert.Equal("", CardPreviewText.Overview(item));
            Assert.Equal("", CardPreviewText.Resume(item));
            Assert.Equal(0d, CardPreviewText.Progress(item));
        });
        Test("悬停文字：题材过滤空值并限三项，评分有小数", () =>
        {
            var item = new EmbyItem { Genres = ["Animation", " ", "Comedy", "Romance", "Drama"], CommunityRating = 8.25f };
            Assert.Equal("Animation / Comedy / Romance", CardPreviewText.Genres(item));
            Assert.Equal("8.3", CardPreviewText.Rating(item));
            item.CommunityRating = 0;
            Assert.Equal("", CardPreviewText.Rating(item));
        });
        Test("悬停文字：续播读数与服务器百分比一致，近片尾隐藏", () =>
        {
            var item = Episode();
            item.UserData = new EmbyUserData { PlaybackPositionTicks = TimeSpan.FromSeconds(70).Ticks, PlayedPercentage = 12.5 };
            Assert.Equal("1:10 / 23:00", CardPreviewText.Resume(item));
            Assert.Equal(12d, CardPreviewText.Progress(item));
            item.UserData.PlayedPercentage = 99.6;
            Assert.Equal("", CardPreviewText.Resume(item));
            Assert.Equal(0d, CardPreviewText.Progress(item));
        });
    }

    private static EmbyItem Episode() => new()
    {
        Type = EmbyItemType.Episode,
        Name = "冬季首战",
        SeriesName = "青之箱",
        ParentIndexNumber = 2,
        IndexNumber = 5,
        RunTimeTicks = TimeSpan.FromMinutes(23).Ticks
    };
}

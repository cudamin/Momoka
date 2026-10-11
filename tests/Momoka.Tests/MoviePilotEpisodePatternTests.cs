using System.Text.Json;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 集号写法识别（<see cref="MoviePilotEpisodePattern"/>）和按集筛选对它的使用。集的版本搜索词里没有集号了
/// （<see cref="MoviePilotVersionQueryTests"/>），这一侧负责把目标集从「整个季」的结果里认回来 —— 认漏了会把
/// 明明白写着集号的资源滤掉，认错了会把别的集放进结果，都得一条条钉住。
/// </summary>
internal static class MoviePilotEpisodePatternTests
{
    public static void Register()
    {
        Test("MP 集号识别：S01E05／E05／EP05／E5 都是第 5 集，大小写都认", () =>
        {
            foreach (var title in (string[])["剧名.2019.S01E05.1080p", "剧名 S01 e05", "剧名.E05.2160p",
                         "剧名 E5 WEB-DL", "剧名.EP05.1080p", "剧名 ep5", "Show.Name.S1E5.720p"])
                Assert.True(MoviePilotEpisodePattern.Contains(title, 5), title);
        });

        Test("MP 集号识别：第5集／第 5 集／第05集（阿拉伯数字，允许空格）", () =>
        {
            foreach (var title in (string[])["剧名 第5集 1080p", "剧名 第 5 集 4K", "【剧名】第05集.mp4"])
                Assert.True(MoviePilotEpisodePattern.Contains(title, 5), title);
        });

        Test("MP 集号识别：第五集／第十二集／第二十三集／第一百零五集（汉字数字）", () =>
        {
            Assert.True(MoviePilotEpisodePattern.Contains("剧名 第五集 1080p", 5));
            Assert.True(MoviePilotEpisodePattern.Contains("剧名 第十二集 4K", 12));
            Assert.True(MoviePilotEpisodePattern.Contains("剧名 第二十三集", 23));
            Assert.True(MoviePilotEpisodePattern.Contains("剧名 第一百零五集", 105));
            Assert.True(MoviePilotEpisodePattern.Contains("剧名 第十集", 10));
        });

        Test("MP 集号识别：多集合的打包（E05E06、S01E05-E06）也算包含那一集", () =>
        {
            Assert.True(MoviePilotEpisodePattern.Contains("剧名.S01E05E06.1080p", 5));
            Assert.True(MoviePilotEpisodePattern.Contains("剧名.S01E05E06.1080p", 6));
            Assert.True(MoviePilotEpisodePattern.Contains("剧名 S01E05-E06 1080p", 5));
            // 「第5-6集」这种范围写法不认 —— 每个数字都得跟着「第…集」成对出现，别把端点拆出来乱配。
            Assert.False(MoviePilotEpisodePattern.Contains("剧名 第5-6集", 5));
        });

        Test("MP 集号识别：整段数字读完再比 —— E51、E512、第51集、第十二集都不是第 5 集", () =>
        {
            foreach (var title in (string[])["剧名.S01E51.1080p", "剧名 E512 4K", "剧名 第51集", "剧名 第512集"])
                Assert.False(MoviePilotEpisodePattern.Contains(title, 5), title);
            Assert.False(MoviePilotEpisodePattern.Contains("剧名 第十二集", 2));
            Assert.False(MoviePilotEpisodePattern.Contains("剧名 第一百零五集", 15));
            Assert.False(MoviePilotEpisodePattern.Contains("剧名 第05集", 50));
        });

        Test("MP 集号识别：凑巧的字母数字组合不当集号 —— HEVC、PART5、SE05、4KE05 不匹配", () =>
        {
            foreach (var title in (string[])["剧名 2019 HEVC 10bit", "剧名 PART5 合集", "剧名 SE05 4K",
                         "剧名 4KE05 1080p", "剧名 2019 S01 完整版", "第1-12集打包"])
                Assert.False(MoviePilotEpisodePattern.Contains(title, 5), title);
            // 「第1-12集」的 12 也别被拆出来。
            Assert.False(MoviePilotEpisodePattern.Contains("第1-12集打包", 12));
        });

        Test("MP 集号识别：没有单位的汉字罗列不猜，「第二三集」两个数都不认；空文本和负集号直接不匹配", () =>
        {
            Assert.False(MoviePilotEpisodePattern.Contains("剧名 第二三集", 2));
            Assert.False(MoviePilotEpisodePattern.Contains("剧名 第二三集", 3));
            Assert.False(MoviePilotEpisodePattern.Contains(null, 5));
            Assert.False(MoviePilotEpisodePattern.Contains("剧名 E05", -1));
            // 特殊篇的 E00／第0集是真的集号，要认。
            Assert.True(MoviePilotEpisodePattern.Contains("剧名 S00E00 OVA", 0));
        });

        Test("MP 集筛选：meta 没解析出集号时，标题／描述里写着集号的也留下，别的集被滤掉", () =>
        {
            var rows = new[]
            {
                Resource("剧名 2019 S01E05 1080p"),
                Resource("剧名 第五集 4K"),
                Resource("剧名 全一季", "本帖持续更新，本次为第5集"),
                Resource("剧名 S01 Complete", episodes: [1, 2, 3, 4, 5]),
                Resource("剧名 S01E03 1080p"),
                Resource("剧名 S01 全季打包")
            };
            var matched = new MoviePilotResourceFilter { Episode = 5 }.Apply(rows, DateTimeOffset.Now).ToArray();
            // TestHarness 的 Equal 对数组按引用比较，序列断言拼成字符串比。
            Assert.Equal("剧名 2019 S01E05 1080p|剧名 第五集 4K|剧名 全一季|剧名 S01 Complete",
                string.Join('|', matched.Select(row => row.Title)));
        });

        Test("MP 集筛选：meta 里有集号时照旧命中，标题写法只是兜底不是门槛", () =>
        {
            var rows = new[] { Resource("剧名 第5集 1080p", episodes: [5]) };
            Assert.Equal(1, new MoviePilotResourceFilter { Episode = 5 }.Apply(rows, DateTimeOffset.Now).Count());
        });

        Test("MP 集筛选：不筛集时全部保留，行为不变", () =>
        {
            var rows = new[] { Resource("剧名 2019 S01E05 1080p"), Resource("剧名 S01E03 1080p") };
            Assert.Equal(2, new MoviePilotResourceFilter().Apply(rows, DateTimeOffset.Now).Count());
        });
    }

    private static MoviePilotResource Resource(string title, string? description = null, IReadOnlyList<int>? episodes = null) => new()
    {
        Title = title,
        Description = description,
        Episodes = episodes ?? [],
        TorrentInfo = JsonSerializer.Deserialize<JsonElement>("{}")
    };
}

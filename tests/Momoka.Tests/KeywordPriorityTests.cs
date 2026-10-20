using Momoka.Emby;
using Momoka.Playback;
using Momoka.Configuration;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class KeywordPriorityTests
{
    public static void Register()
    {
        Test("关键词排序：交换上方两条规则会改变最佳候选", () =>
        {
            string[] pool = ["HDR", "REMUX", "1080p"];
            List<KeywordRule> rules = [new("REMUX", TitlePreference.Prefer), new("HDR", TitlePreference.Prefer)];
            Assert.Equal("REMUX", KeywordFilter.Rank(pool, rules, text => text).Single());
            rules.Reverse();
            Assert.Equal("HDR", KeywordFilter.Rank(pool, rules, text => text).Single());
        });
        Test("关键词排序：任意数量的低优先级命中不能越过上方规则", () =>
        {
            var rules = new List<KeywordRule> { new("首选", TitlePreference.Prefer) };
            rules.AddRange(Enumerable.Range(0, 160).Select(index => new KeywordRule($"词{index:D3}", TitlePreference.Prefer)));
            var other = string.Join(" ", rules.Skip(1).Select(rule => rule.Term));
            Assert.Equal("首选", KeywordFilter.Rank(new[] { other, "首选" }, rules, text => text).Single());
        });
        Test("关键词排序：中立、空白、未知态度跳过，同分保留服务器次序", () =>
        {
            string[] pool = ["B HDR", "A HDR", "C"];
            List<KeywordRule> rules = [new("B", TitlePreference.Neutral), new(" ", TitlePreference.Prefer),
                new("A", (TitlePreference)99), new("HDR", TitlePreference.Prefer)];
            Assert.Equal("B HDR,A HDR", string.Join(",", KeywordFilter.Rank(pool, rules, text => text)));
            Assert.True(ReferenceEquals(pool, KeywordFilter.Rank(pool, [new("A", TitlePreference.Neutral)], _ => throw new Exception())));
            Assert.True(ReferenceEquals(pool, KeywordFilter.Rank(pool, null, _ => throw new Exception())));
            Assert.Equal(0, KeywordFilter.Rank(Array.Empty<string>(), rules, _ => throw new Exception()).Count);
        });
        Test("关键词排序：全是候补仍兜底，空标题不误匹配", () =>
        {
            List<KeywordRule> rules = [new("枪版", TitlePreference.Exclude), new("SDH", TitlePreference.Exclude)];
            Assert.Equal("枪版", KeywordFilter.Rank(new[] { "枪版 SDH", "枪版" }, rules, text => text).Single());
            Assert.True(KeywordFilter.Compare(null, "枪版", rules) > 0);
            var preferred = KeywordFilter.Rank(new string?[] { null, "枪版" }, rules, text => text);
            Assert.Equal(1, preferred.Count);
            Assert.Null(preferred[0]);
        });
        Test("关键词排序：音轨格式优先级不越过语言", () =>
        {
            var settings = new PlaybackSettings
            {
                AudioLanguages = ["日语", "英语"],
                AudioFormatRules = [new("Atmos", TitlePreference.Prefer), new("FLAC", TitlePreference.Prefer)]
            };
            var source = new MediaSource
            {
                MediaStreams = [
                new MediaStream { Index = 1, Type = "Audio", Language = "jpn", DisplayTitle = "FLAC" },
                new MediaStream { Index = 2, Type = "Audio", Language = "jpn", DisplayTitle = "Atmos" },
                new MediaStream { Index = 3, Type = "Audio", Language = "eng", DisplayTitle = "Atmos FLAC" }]
            };
            Assert.Equal(2, TrackSelection.ChooseAudio(settings, source)!.Index);
            settings.AudioFormatRules.Reverse();
            Assert.Equal(1, TrackSelection.ChooseAudio(settings, source)!.Index);
        });
    }
}

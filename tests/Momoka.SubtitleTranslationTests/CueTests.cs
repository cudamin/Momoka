using Momoka.Translation;

namespace Momoka.SubtitleTranslationTests;

internal static class CueTests
{
    public static void Register()
    {
        Test.Case("cue/latest-only-and-file-generation", () =>
        {
            var latest = new LatestCue(); latest.Reset(100);
            var first = latest.Update(100, "A")!.Value; Test.True(latest.IsCurrent(first));
            var second = latest.Update(100, "B")!.Value; Test.True(!latest.IsCurrent(first) && latest.IsCurrent(second));
            var nextFile = latest.Update(101, "B")!.Value; Test.True(!latest.IsCurrent(second) && latest.IsCurrent(nextFile));
        });
        Test.Case("cue/same-text-repeat-reuses-current", () =>
        {
            var latest = new LatestCue(); latest.Reset(1);
            var first = latest.Update(1, "A")!.Value; var again = latest.Update(1, "A")!.Value;
            Test.Equal(first, again); Test.True(latest.IsCurrent(first));
        });
        Test.Case("cue/aba-rejects-earlier-a", () =>
        {
            var latest = new LatestCue(); latest.Reset(1);
            var first = latest.Update(1, "A")!.Value; latest.Update(1, "B"); var last = latest.Update(1, "A")!.Value;
            Test.True(!latest.IsCurrent(first) && latest.IsCurrent(last) && first.Revision != last.Revision);
        });
        Test.Case("cue/clear-and-config-reset-reject-late-result", () =>
        {
            var latest = new LatestCue(); latest.Reset(1); var first = latest.Update(1, "A")!.Value;
            Test.True(latest.Update(1, "") is null && !latest.IsCurrent(first));
            var second = latest.Update(1, "A")!.Value; latest.Reset(1); Test.True(!latest.IsCurrent(second));
            var third = latest.Update(1, "A")!.Value; Test.True(third != second);
        });
        Test.Case("cue/exact-original-and-long-text-clears", () =>
        {
            var latest = new LatestCue(); var first = latest.Update(1, "A")!.Value;
            var second = latest.Update(1, "A ")!.Value; Test.True(!latest.IsCurrent(first) && latest.IsCurrent(second));
            Test.True(latest.Update(1, new string('x', SubtitleTranslationClient.MaxTextLength + 1)) is null && !latest.IsCurrent(second));
        });
        Test.Case("cache/bounded-lru-and-promotion", () =>
        {
            var cache = new SubtitleTranslationCache(2); var scope = cache.Reset();
            Test.True(cache.Store(scope, "a", "译a") && cache.Store(scope, "b", "译b"));
            Test.True(cache.TryGet(scope, "a", out var translated)); Test.Equal("译a", translated);
            cache.Store(scope, "c", "译c"); Test.Equal(2, cache.Count);
            Test.True(!cache.TryGet(scope, "b", out _) && cache.TryGet(scope, "a", out _));
        });
        Test.Case("cache/reset-scope-blocks-old-reads-and-writes", () =>
        {
            var cache = new SubtitleTranslationCache(); var old = cache.Reset(); cache.Store(old, "a", "译a");
            var current = cache.Reset(); Test.True(old != current && cache.Count == 0);
            Test.True(!cache.TryGet(old, "a", out _) && !cache.Store(old, "a", "旧译"));
            Test.True(cache.Store(current, "a", "新译") && cache.TryGet(current, "a", out var translated) && translated == "新译");
        });
        Test.Case("cue/stale-update-after-file-switch-cannot-roll-back", () =>
        {
            var latest = new LatestCue(); latest.Reset(100); var first = latest.Update(100, "old")!.Value;
            latest.Reset(101); var current = latest.Update(101, "current")!.Value;
            Test.True(latest.Update(100, "stale") is null && latest.IsCurrent(current) && !latest.IsCurrent(first));
        });
        Test.Case("cache/invalid-entry-rejected-capacity-bounds", () =>
        {
            Test.Throws<ArgumentOutOfRangeException>(() => new SubtitleTranslationCache(0));
            Test.Throws<ArgumentOutOfRangeException>(() => new SubtitleTranslationCache(513));
            var cache = new SubtitleTranslationCache(); var scope = cache.Reset();
            Test.True(!cache.Store(scope, "", "x") && !cache.Store(scope, "a", " ") &&
                !cache.Store(scope, new string('x', 4_001), "x") && !cache.Store(scope, "a", new string('x', 16_001)));
            Test.True(!cache.Store(scope, "a", "bad\0line"));
            Test.True(!cache.TryGet(scope, "missing", out var missing)); Test.Equal("", missing);
            Test.Equal(0, cache.Count);
        });
    }
}

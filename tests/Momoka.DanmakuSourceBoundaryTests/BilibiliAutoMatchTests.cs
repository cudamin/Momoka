using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

// Console fixture blocks only at the test boundary, without a UI synchronization context.
#pragma warning disable RS0030

namespace DanmakuReview;

internal static class BilibiliAutoMatchTests
{
    internal static void Register()
    {
        foreach (var title in new[] { "3", "03 这一集的副标题" })
            Test("Bilibili auto match loads only exact title and explicit current episode " + title, () =>
            {
                using var host = Host(new([new(7, "真实 作品", title)], false));
                host.MatchAsync(Episode()).GetAwaiter().GetResult();
                Momoka.Tests.Assert.Equal("真实作品", host.Query);
                Momoka.Tests.Assert.Equal("3", host.EpisodeQuery);
                Momoka.Tests.Assert.Equal(DanmakuSource.Bilibili, host.QueriedSource);
                Momoka.Tests.Assert.True(host.Fetched);
                Momoka.Tests.Assert.Equal(DanmakuSource.Bilibili, host.Client.Source);
                Momoka.Tests.Assert.False(host.HasPrivateTarget);
            });
        foreach (var title in new[] { "", "正片", "4 其他集", "3.5 特别篇", "3A 副标题", "P3 分P", "-3", "0", "第三集", "2147483648" })
            Test("Bilibili auto match refuses missing or conflicting episode " + title, () =>
            {
                using var host = Host(new([new(7, "真实作品", title)], false));
                host.MatchAsync(Episode()).GetAwaiter().GetResult();
                Momoka.Tests.Assert.Equal(1, host.ExternalCalls);
                Momoka.Tests.Assert.False(host.Fetched);
                Momoka.Tests.Assert.Null(host.Client.Source);
                Momoka.Tests.Assert.Contains("手动", host.DanmakuStatus);
            });
        foreach (var scenario in new[] { "multiple", "alias", "truncated", "empty", "error" })
            Test("Bilibili auto match refuses ambiguity or unavailable result " + scenario, () =>
            {
                var matches = scenario switch
                {
                    "multiple" => new DandanPlaySearchResult([new(7, "真实作品", "3"), new(8, "真实作品", "3")], false),
                    "alias" => new([new(7, "另一个作品别名", "3")], false),
                    "truncated" => new([new(7, "真实作品", "3")], true),
                    _ => DandanPlaySearchResult.Empty
                };
                using var host = Host(matches);
                if (scenario == "error") host.MatchError = "B站暂时限制了访问";
                host.MatchAsync(Episode()).GetAwaiter().GetResult();
                Momoka.Tests.Assert.Equal(1, host.ExternalCalls);
                Momoka.Tests.Assert.False(host.Fetched);
                Momoka.Tests.Assert.Null(host.Client.Source);
                Momoka.Tests.Assert.Contains("手动", host.DanmakuStatus);
                if (scenario == "error") Momoka.Tests.Assert.Contains("限制", host.DanmakuStatus);
            });
        foreach (var multiple in new[] { false, true })
            Test("Bilibili movie auto match requires one exact title entry " + multiple, () =>
            {
                using var host = Host(new(multiple
                    ? [new(7, "真实电影", "正片"), new(8, "真实电影", "幕后")]
                    : [new(7, "真实电影", "正片")], false));
                host.MatchAsync(new() { Type = EmbyItemType.Movie, Name = "真实电影" }).GetAwaiter().GetResult();
                Momoka.Tests.Assert.Equal("真实电影", host.Query);
                Momoka.Tests.Assert.Null(host.EpisodeQuery);
                Momoka.Tests.Assert.Equal(!multiple, host.Fetched);
                Momoka.Tests.Assert.False(host.HasPrivateTarget);
            });
        Test("Bilibili auto match rejects punctuation-only movie titles", () =>
        {
            using var host = Host(new([new(7, "??", "正片")], false));
            host.MatchAsync(new() { Type = EmbyItemType.Movie, Name = "!!" }).GetAwaiter().GetResult();
            Momoka.Tests.Assert.False(host.Fetched);
            Momoka.Tests.Assert.Null(host.Client.Source);
        });
        foreach (var condition in new[] { "disabled", "signedout", "off", "backend", "missingepisode" })
            Test("Bilibili auto match does not request when unavailable " + condition, () =>
            {
                var settings = BilibiliPlayerTests.Settings();
                var item = Episode();
                if (condition == "disabled") settings.Danmaku.AutoMatch = false;
                if (condition == "signedout") settings.Danmaku.ProtectedBilibiliCookie = "";
                if (condition == "off") settings.Danmaku.Enabled = false;
                if (condition == "backend") settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
                if (condition == "missingepisode") item.IndexNumber = null;
                using var host = new PlaybackHost(settings);
                host.MatchAsync(item).GetAwaiter().GetResult();
                Momoka.Tests.Assert.Equal(0, host.ExternalCalls);
                Momoka.Tests.Assert.False(host.Fetched);
            });
        foreach (var stale in new[] { "playback", "cancel", "cookie", "source", "automatch" })
            Test("Bilibili auto match cannot apply old search result after " + stale, () =>
            {
                var settings = BilibiliPlayerTests.Settings();
                using var host = new PlaybackHost(settings);
                host.PendingSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = host.MatchAsync(Episode());
                Momoka.Tests.Assert.Equal(1, host.ExternalCalls);
                if (stale == "playback") host.Generation++;
                if (stale == "cancel") host.CancelSearch();
                if (stale == "cookie") settings.Danmaku.ProtectedBilibiliCookie = "replacement protected cookie";
                if (stale == "source") settings.Danmaku.Source = DanmakuSourceMode.DandanPlay;
                if (stale == "automatch") settings.Danmaku.AutoMatch = false;
                host.PendingSearch.SetResult((new([new(7, "真实作品", "3")], false), ""));
                pending.GetAwaiter().GetResult();
                Momoka.Tests.Assert.False(host.Fetched);
                Momoka.Tests.Assert.Null(host.Client.Source);
            });
    }

    private static PlaybackHost Host(DandanPlaySearchResult matches) => new(BilibiliPlayerTests.Settings()) { Matches = matches };
    private static EmbyItem Episode() => new() { Type = EmbyItemType.Episode, Name = "这集的本地标题", SeriesName = "真实作品", ParentIndexNumber = 1, IndexNumber = 3 };
}

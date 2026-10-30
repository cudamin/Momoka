using Momoka.AniBt;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AniBtSeriesIdentityTests
{
    public static void Register()
    {
        Test("AniBT 季名归一：第二季／Season 2／第2期都归到主剧集名", () =>
        {
            Assert.Equal("青之箱", AniBtSeriesIdentity.BaseTitle("青之箱 第二季"));
            Assert.Equal("青之箱", AniBtSeriesIdentity.BaseTitle("青之箱 第2季"));
            Assert.Equal("青之箱", AniBtSeriesIdentity.BaseTitle("青之箱 第2期"));
            Assert.Equal("アオのハコ", AniBtSeriesIdentity.BaseTitle("アオのハコ Season 2"));
            Assert.Equal("bluebox", AniBtSeriesIdentity.BaseTitle("Blue Box 2nd Season"));
            Assert.Equal("86", AniBtSeriesIdentity.BaseTitle("86"));
            Assert.Equal("", AniBtSeriesIdentity.BaseTitle("  "));
            Assert.Equal("青之箱", AniBtSeriesIdentity.DisplayBaseTitle("青之箱 第二季"));
            Assert.Equal("Blue Box", AniBtSeriesIdentity.DisplayBaseTitle("Blue Box Season 2"));
        });

        Test("AniBT 同一作品判断：主剧集与季条目算同一部，不同作品不算", () =>
        {
            Assert.True(AniBtSeriesIdentity.SameSeries(["青之箱 第二季", "アオのハコ Season 2"], ["青之箱"]));
            Assert.True(AniBtSeriesIdentity.SameSeries(["Blue Box Season 2"], ["アオのハコ", "Blue Box"]));
            Assert.False(AniBtSeriesIdentity.SameSeries(["药屋少女的呢喃"], ["青之箱"]));
            Assert.False(AniBtSeriesIdentity.SameSeries([], ["青之箱"]));
            Assert.False(AniBtSeriesIdentity.SameSeries(["青之箱"], []));
        });
    }
}

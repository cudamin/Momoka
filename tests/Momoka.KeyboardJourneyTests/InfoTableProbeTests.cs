using Momoka.Emby;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class InfoTableProbeTests
{
    internal static void Register()
    {
        Test("媒体表匿名源由正式MediaInfo生成准确10行，首标签/轨道/字幕可读", () =>
        {
            var item = InfoTableProbeFixture.Movie();
            var rows = ItemDetail.MediaInfo(item, item.MediaSources.Single());
            Assert.Equal(10, rows.Count);
            Assert.Equal("文 件：", rows[0].Label);
            Assert.True(rows.Any(row => row.Label == "音 频："));
            Assert.True(rows.Any(row => row.Label == "字 幕："));
            Assert.True(item.MediaSources.All(source => source.Path is null));
        });
        Test("媒体表渲染检查拒绝首3行缺失、乱序或无首标签", () =>
        {
            InfoTableProbeFixture.RequireRendered(10, Enumerable.Range(0, 10).ToArray(), true);
            foreach (var action in new Action[]
            {
                () => InfoTableProbeFixture.RequireRendered(10, Enumerable.Range(3, 7).ToArray(), true),
                () => InfoTableProbeFixture.RequireRendered(10, Enumerable.Range(0, 10).Reverse().ToArray(), true),
                () => InfoTableProbeFixture.RequireRendered(10, Enumerable.Range(0, 10).ToArray(), false),
                () => InfoTableProbeFixture.RequireRendered(0, [], true)
            })
            {
                try { action(); throw new AssertionException("错误媒体表读数被接受"); }
                catch (InvalidOperationException) { }
            }
        });
    }
}

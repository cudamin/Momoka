using Momoka.Emby;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class InfoTableProbeTests
{
    internal static void Register()
    {
        Test("正式媒体表定位把页尾目标限制到可滚范围，已到达不重发事务", () =>
        {
            var page = new Momoka.Shell.Views.DetailPage();
            page.InfoPanel.Top = 1364;
            page.Body.ScrollableHeight = 1346;
            page.ScrollToInfo();
            Assert.Equal(1346d, page.Body.VerticalOffset);
            Assert.Equal(1, page.Body.Requests.Count);
            for (var look = 0; look < 8; look++) page.ScrollToInfo();
            Assert.Equal(1, page.Body.Requests.Count, "相同已到达目标不应重开原生view-change");
            Assert.Equal(9, page.Body.LayoutReads);
            page.InfoPanel.Top = 494;
            page.Body.ScrollableHeight = 0;
            page.Body.VerticalOffset = 0;
            page.ScrollToInfo();
            Assert.Equal(1, page.Body.Requests.Count, "短内容无需启动超范围事务");
        });
        Test("正式媒体表定位保留正常上沿，负上沿回零，没有内容不请求", () =>
        {
            var page = new Momoka.Shell.Views.DetailPage();
            page.InfoPanel.Top = 600;
            page.Body.ScrollableHeight = 1000;
            page.ScrollToInfo();
            Assert.Equal(600d, page.Body.VerticalOffset);
            page.InfoPanel.Top = -1;
            page.ScrollToInfo();
            Assert.Equal(0d, page.Body.VerticalOffset);
            Assert.Equal(2, page.Body.Requests.Count);
            page.Body.Content = null;
            page.ScrollToInfo();
            Assert.Equal(2, page.Body.Requests.Count);
        });
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

using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 搜索页「MP 资源」进入时自动铺 MoviePilot 网页端上次搜索缓存的行为。使用真实
/// <c>MoviePilotVersionsViewModel</c> 与匿名内存传输，检查的是「读哪一份、铺不铺、覆不覆盖」，
/// 不是实际 WinUI 画面。
/// </summary>
internal static class CachedSearchTests
{
    internal static void Register()
    {
        Test("MP 资源缓存：进入来源时自动铺网页端上次搜索的结果", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Transport.SearchResults = """
                {"success":true,"data":{"params":{"keyword":"某剧"},"results":[
                  {"torrent_info":{"title":"缓存.S01E01.1080p","site_name":"站点甲","size":1024}},
                  {"torrent_info":{"title":"缓存.S01E02.1080p","site_name":"站点乙","size":2048}}]}}
                """;
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());

            ResourceFixture.Complete(vm.LoadCachedResultsAsync());

            Assert.Equal(2, vm.Versions.Count);
            Assert.Equal("缓存.S01E01.1080p", vm.Versions[0].Title);
            Assert.Contains("search/last/context", fixture.Transport.Requests.Single(), "只读网页端上次搜索的上下文");
            Assert.False(vm.ShowEmptyNotice);
            Assert.False(vm.Busy);
            Assert.False(vm.NoticeOpen, "预读是后台动作，不该弹提示");
        });

        Test("MP 资源缓存：已有结果时预读不覆盖也不发请求", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Transport.SearchResults = "[" + SearchSitePreferenceTests.Result("自己搜到的", "站点甲") + "]";
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));
            Assert.Equal("自己搜到的", vm.Versions.Single().Title);

            fixture.Transport.Requests.Clear();
            fixture.Transport.SearchResults =
                """{"success":true,"data":{"params":{},"results":[{"torrent_info":{"title":"缓存"}}]}}""";
            ResourceFixture.Complete(vm.LoadCachedResultsAsync());

            Assert.Equal("自己搜到的", vm.Versions.Single().Title);
            Assert.Equal(0, fixture.Transport.Requests.Count, "页上已有结果时连缓存都不读");
        });

        Test("MP 资源缓存：本次搜索返回空结果时不回填网页旧缓存", () =>
        {
            using var fixture = new SiteFixture();
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());
            ResourceFixture.Complete(vm.SearchAsync("Target"));

            fixture.Transport.Requests.Clear();
            fixture.Transport.SearchResults =
                """{"success":true,"data":{"params":{},"results":[{"torrent_info":{"title":"网页旧缓存"}}]}}""";
            ResourceFixture.Complete(vm.LoadCachedResultsAsync());

            Assert.Equal(0, fixture.Transport.Requests.Count);
            Assert.Equal(0, vm.Versions.Count);
            Assert.Contains("Target", vm.EmptyNotice);
        });

        Test("MP 资源缓存：网页上次搜的是字幕时不铺种子并保留空态", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Transport.SearchResults =
                """{"success":true,"data":{"params":{"result_type":"subtitle"},"results":[{"title":"某剧 S01E01 字幕"}]}}""";
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());

            ResourceFixture.Complete(vm.LoadCachedResultsAsync());

            Assert.Equal(0, vm.Versions.Count);
            Assert.True(vm.ShowEmptyNotice);
            Assert.Contains("输入关键词", vm.EmptyNotice);
            Assert.False(vm.NoticeOpen);
        });

        Test("MP 资源缓存：预读失败保留空态且不弹提示", () =>
        {
            using var fixture = new SiteFixture();
            fixture.Transport.FailSearch = true;
            var vm = new MoviePilotVersionsViewModel();
            vm.Attach(fixture.Service, new FixtureLauncher());

            ResourceFixture.Complete(vm.LoadCachedResultsAsync());

            Assert.Equal(0, vm.Versions.Count);
            Assert.True(vm.ShowEmptyNotice);
            Assert.Contains("输入关键词", vm.EmptyNotice);
            Assert.False(vm.NoticeOpen, "后台预读失败不拦用户自己搜");
            Assert.False(vm.Busy);
        });
    }
}

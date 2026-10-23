using System.Text;
using Momoka.Emby;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("页面源码控制流契约（无 WinUI 窗口／布局或真实服务）");
        foreach (var key in new[] { HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries })
        {
            Test("订阅导航 Tag：Attach " + key, () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                var request = Request(key);
                page.Attach(request);
                Assert.Equal(key, page.Tag as string);
                Assert.True(ReferenceEquals(request, page.NavigationRequest));
                Assert.Equal(HomeLayout.FixedTitle(key), page.ContractTitle);
                Assert.Equal(1, page.ContractAttaches);
            });
            Test("订阅导航 Tag：真实 OnNavigatedTo " + key, () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                page.ContractNavigateTo(Request(key));
                Assert.Equal(key, page.Tag as string);
                Assert.Equal(HomeLayout.FixedTitle(key), page.ContractTitle);
            });
            Test("订阅导航 Tag：离开后历史重入更新当前目的地 " + key, () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                var opposite = key == HomeLayout.MoviePilotMovies ? HomeLayout.MoviePilotSeries : HomeLayout.MoviePilotMovies;
                page.ContractNavigateTo(Request(opposite));
                page.ContractNavigateFrom();
                page.ContractNavigateTo(Request(key));
                Assert.Equal(key, page.Tag as string);
                Assert.Equal(2, page.ContractAttaches);
                Assert.Equal(1, page.ContractDetaches);
            });
            Test("订阅导航 Tag：Release 保留历史目的地并且重复释放安全 " + key, () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                page.Attach(Request(key));
                page.Release();
                page.Release();
                Assert.Equal(key, page.Tag as string);
                Assert.Equal(1, page.ContractDetaches);
            });
        }
        Test("订阅导航 Tag：无合法请求不创建订阅加载属主", () =>
        {
            var page = new MoviePilotSubscriptionsPage();
            page.ContractNavigateTo(null);
            Assert.Null(page.Tag);
            Assert.Equal(0, page.ContractAttaches);
        });
        DiagnosticsTests.Register();
        CardImageResizeTests.Register();
        return TestHarness.Run();
    }

    private static MoviePilotSubscriptionsRequest Request(string key) => new(null!, key,
        _ => throw new AssertionException("只读导航契约不打开订阅或运行网络"));
}

using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests
{
    /// <summary>
    /// 订阅墙在列表页的接线契约：分类页立墙收横排、汇总库保留横排收墙，墙随附加/离开收放轮询信号，
    /// 分类页页面空态收起。编译链接真实页面 code-behind，墙视图与横排视图是边界替身（只记录接线与启停信号），
    /// 不证明真实 WinUI 布局、DispatcherTimer 启停或渲染——那些由运行期 UI 探针覆盖。
    /// </summary>
    internal static class SubscriptionWallWiringTests
    {
        internal static void Register()
        {
            foreach (var key in new[] { HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries })
            {
                Test("订阅墙接线：分类页立墙收横排并启动轮询 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.Attach(Request(key));
                    Assert.True(page.ContractWallCategory);
                    Assert.Equal(Visibility.Visible, page.ContractWallVisibility);
                    Assert.Equal(Visibility.Collapsed, page.ContractRowVisibility);
                    Assert.Equal(1, page.ContractWallAttaches);
                    Assert.True(page.ContractWallActive, "分类页附加后墙应处于轮询激活态");
                    Assert.Equal(Visibility.Collapsed, page.ContractEmptyVisibility);
                });
                Test("订阅墙接线：真实 OnNavigatedTo 也立墙 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.ContractNavigateTo(Request(key));
                    Assert.Equal(Visibility.Visible, page.ContractWallVisibility);
                    Assert.True(page.ContractWallActive);
                });
                Test("订阅墙接线：Release 停轮询并松开墙 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.Attach(Request(key));
                    page.Release();
                    Assert.False(page.ContractWallActive, "释放后墙必须停轮询");
                    Assert.True(page.ContractWallDetaches >= 1);
                    page.Release();
                    Assert.False(page.ContractWallActive);
                });
                Test("订阅墙接线：OnNavigatedFrom 停轮询 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.ContractNavigateTo(Request(key));
                    page.ContractNavigateFrom();
                    Assert.False(page.ContractWallActive);
                });
            }
            Test("订阅墙接线：汇总库保留横排、收墙、不轮询", () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                page.Attach(Request(MoviePilotSubscriptionsRequest.LibraryKey));
                Assert.False(page.ContractWallCategory);
                Assert.Equal(Visibility.Collapsed, page.ContractWallVisibility);
                Assert.Equal(Visibility.Visible, page.ContractRowVisibility);
                Assert.Equal(0, page.ContractWallAttaches);
                Assert.False(page.ContractWallActive);
                Assert.Equal(Visibility.Visible, page.ContractEmptyVisibility);
            });
            Test("订阅墙接线：分类页与汇总库往返时轮询跟着收放", () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                page.Attach(Request(HomeLayout.MoviePilotMovies));
                Assert.True(page.ContractWallActive);
                page.Attach(Request(MoviePilotSubscriptionsRequest.LibraryKey));
                Assert.False(page.ContractWallActive, "切到汇总库应停墙轮询");
                Assert.Equal(Visibility.Collapsed, page.ContractWallVisibility);
                page.Attach(Request(HomeLayout.MoviePilotSeries));
                Assert.True(page.ContractWallActive, "切回分类页应重新立墙轮询");
                Assert.Equal(Visibility.Visible, page.ContractWallVisibility);
                page.Release();
                Assert.False(page.ContractWallActive);
            });
            // 契约测试用的是边界替身，加载不了真实 XAML：EgSpace* 是 Double 刻度，
            // 接到 Padding / Margin / BorderThickness（都是 Thickness）上会在 InitializeComponent
            // 抛 XamlParseException，订阅页当场打不开。Spacing 那几个是 Double，可以照常引。
            Test("订阅墙 XAML：Thickness 属性不引用 Double 刻度键", () =>
            {
                var root = FindRoot();
                foreach (var file in new[]
                {
                    "MoviePilotSubscriptionsPage.xaml",
                    "MoviePilotSubscriptionWallView.xaml",
                    "MoviePilotSubscriptionWallCard.xaml"
                })
                {
                    var text = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", file));
                    foreach (var property in new[] { "Padding", "Margin", "BorderThickness" })
                        foreach (var key in new[] { "EgSpaceXS", "EgSpaceS", "EgSpaceM", "EgSpaceL", "EgSpaceXL", "EgSpace2XL" })
                            Assert.False(text.Contains(property + "=\"{StaticResource " + key + "}\""),
                                file + " 的 " + property + " 引了 Double 刻度键 " + key + "，运行时会在 InitializeComponent 抛 XamlParseException");
                }
            });
            // 2026-11-12 用户令：删掉那排药丸，复刻网页端订阅页左上角那三个图标。
            // 网页端 subscribe.vue 的 appendButtons 就是筛选／拖拽排序／批量三颗；排序项并进筛选面板。
            Test("订阅墙工具栏：网页端三个图标键，不再有药丸", () =>
            {
                var text = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionWallView.xaml"));
                foreach (var name in new[] { "WallFilterButton", "WallSortModeButton", "WallBatchButton" })
                    Assert.True(text.Contains("x:Name=\"" + name + "\""), "工具栏缺少 " + name);
                foreach (var pill in new[] { "Content=\"拖拽排序\"", "Content=\"批量操作\"", "Content=\"维护\"", "Content=\"刷新订阅\"" })
                    Assert.False(text.Contains(pill), "工具栏还留着药丸键 " + pill);
                // 筛选面板照网页端那颗键的 VMenu：名称搜索 + 状态列表 + 排序列表。
                Assert.True(text.Contains("PlaceholderText=\"名称\""), "筛选面板缺少名称搜索");
                Assert.True(text.Contains("Text=\"状态\"") && text.Contains("Text=\"排序\""), "筛选面板缺少状态／排序两个小标题");
                Assert.True(text.Contains("x:Name=\"WallFilterOptions\"") && text.Contains("x:Name=\"WallSortOptions\""),
                    "筛选面板缺少状态／排序两个列表");
                Assert.False(text.Contains("WallSortFlyout"), "排序浮层应当并进筛选面板，不再单独一颗键");
            });
            // 2026-11-12 用户令：日期该到片名上方，不要和更多按钮一行（网页端 SubscribeCard 桌面档的 year 行）。
            Test("订阅墙卡片：年份一行排在片名上方", () =>
            {
                var text = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionWallCard.xaml"));
                var year = text.IndexOf("x:Name=\"CardYear\"", StringComparison.Ordinal);
                var title = text.IndexOf("x:Name=\"CardTitle\"", StringComparison.Ordinal);
                Assert.True(year > 0, "卡片缺少年份那一行");
                Assert.True(title > year, "年份那一行必须排在片名之前");
                Assert.True(text.Contains("Card.WallYear") && text.Contains("Card.WallTitle"), "年份／片名要读墙上的那一对读数");
            });
        }

        private static string FindRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
            throw new AssertionException("找不到本次编译所属的工作树");
        }

        private static MoviePilotSubscriptionsRequest Request(string key) => new(null!, key,
            _ => throw new AssertionException("只读接线契约不打开订阅或运行网络"));
    }
}
